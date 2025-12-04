// SoundManager.cs
// Unity 6 (LTS) 기준 사운드 매니저 단일 파일 구현
// 주요 기능:
// - BGM / SFX(효과음) 완전 분리
// - BGM 이중 AudioSource로 크로스페이드 전환
// - SFX 오디오소스 풀링(성능 최적화)
// - 2D/3D 효과음 재생(위치 지정 지원)
// - 채널별 볼륨/뮤트/일시정지
// - 키(문자열) 기반 사운드 뱅크 재생
// - AudioMixerGroup 라우팅(선택)
// - DontDestroyOnLoad 싱글턴
//
// 사용 방법:
// 1) 빈 GameObject에 본 스크립트를 추가하고 첫 씬에서 생성되게 둠.
// 2) (선택) bgmGroup, sfxGroup에 AudioMixerGroup을 배치(없어도 동작).
// 3) BGM Clips / SFX Clips 배열에 키와 클립을 등록하면 문자열 키로 재생 가능.
// 4) 3D 효과음은 PlaySFXAt() 계열 API 사용.
//
// 주의:
// - 프로젝트 오디오 설정에서 AudioListener가 정확히 1개만 존재해야 함.
// - BGM 크로스페이드는 코루틴 기반으로 동작.

// 상단 네임스페이스는 기본값 유지
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class SoundManager : MonoBehaviour
{
    // ===== 싱글턴 =====
    public static SoundManager Instance { get; private set; }

    // ===== 믹서 라우팅(선택) =====
    [Header("AudioMixer 라우팅(선택)")]
    public AudioMixerGroup bgmGroup;
    public AudioMixerGroup sfxGroup;

    // ===== 사운드 뱅크(키-클립 매핑) =====
    [System.Serializable]
    public struct NamedClip
    {
        public string key;       // 재생시 사용할 키
        public AudioClip clip;   // 오디오 클립
    }

    [Header("BGM 사운드 뱅크")]
    public NamedClip[] bgmClips;

    [Header("SFX 사운드 뱅크")]
    public NamedClip[] sfxClips;

    // 내부 조회용 딕셔너리
    private Dictionary<string, AudioClip> _bgmDict = new Dictionary<string, AudioClip>();
    private Dictionary<string, AudioClip> _sfxDict = new Dictionary<string, AudioClip>();

    // ===== BGM 관리 =====
    [Header("BGM 설정")]
    [Range(0f, 1f)] public float bgmVolume = 1f;  // BGM 마스터 볼륨
    public bool bgmMute = false;                  // BGM 뮤트

    private AudioSource _bgmA;
    private AudioSource _bgmB;
    private bool _bgmUsingA = true;               // 현재 활성 소스가 A인지 여부
    private Coroutine _bgmFadeCo;                 // 크로스페이드 코루틴 핸들

    // ===== SFX 관리(풀링) =====
    [Header("SFX 설정")]
    [Range(0f, 1f)] public float sfxVolume = 1f;  // SFX 마스터 볼륨
    public bool sfxMute = false;                  // SFX 뮤트

    [Tooltip("초기 풀 크기")]
    public int sfxPoolSize = 10;
    [Tooltip("동시에 재생 가능한 최대 효과음 소스 수(상한)")]
    public int sfxPoolHardLimit = 32;

    private readonly List<AudioSource> _sfxPool = new List<AudioSource>();

    // ===== 생명주기 =====
    private void Awake()
    {
        // 싱글턴 설정
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // BGM 이중 소스 생성
        _bgmA = CreateChildAudioSource("BGM_A", loop: true, group: bgmGroup);
        _bgmB = CreateChildAudioSource("BGM_B", loop: true, group: bgmGroup);
        ApplyBGMVolumeAndMute();

        // SFX 풀 초기화
        PrewarmSFXPool(sfxPoolSize);

        // 뱅크 빌드
        RebuildDictionaries();
    }

    private void OnValidate()
    {
        // 에디터에서 값 변경 시 반영
        ApplyBGMVolumeAndMute();
        ApplySFXVolumeAndMute();
    }

    // ===== 유틸: AudioSource 생성 =====
    private AudioSource CreateChildAudioSource(string name, bool loop, AudioMixerGroup group)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f; // BGM/SFX 기본은 2D
        if (group != null) src.outputAudioMixerGroup = group;
        return src;
    }

    // ===== 사운드 뱅크 딕셔너리 재구성 =====
    public void RebuildDictionaries()
    {
        _bgmDict.Clear();
        foreach (var nc in bgmClips)
        {
            if (!string.IsNullOrEmpty(nc.key) && nc.clip != null)
                _bgmDict[nc.key] = nc.clip;
        }

        _sfxDict.Clear();
        foreach (var nc in sfxClips)
        {
            if (!string.IsNullOrEmpty(nc.key) && nc.clip != null)
                _sfxDict[nc.key] = nc.clip;
        }
    }

    // =====================================================================
    // BGM 제어
    // =====================================================================

    /// <summary>
    /// BGM을 키로 재생(크로스페이드).
    /// </summary>
    public void PlayBGM(string key, float fadeSeconds = 0.75f, float startTime = 0f)
    {
        if (!_bgmDict.TryGetValue(key, out var clip))
        {
            Debug.LogWarning($"[SoundManager] 존재하지 않는 BGM 키: {key}");
            return;
        }
        PlayBGM(clip, fadeSeconds, startTime);
    }

    /// <summary>
    /// BGM을 AudioClip으로 재생(크로스페이드).
    /// </summary>
    public void PlayBGM(AudioClip clip, float fadeSeconds = 0.75f, float startTime = 0f)
    {
        if (clip == null)
        {
            Debug.LogWarning("[SoundManager] BGM 클립이 null 입니다.");
            return;
        }

        // 현재 사용 중이 아닌 소스에 새 클립 로드 후 페이드
        var from = _bgmUsingA ? _bgmA : _bgmB;
        var to = _bgmUsingA ? _bgmB : _bgmA;

        to.clip = clip;
        to.time = Mathf.Clamp(startTime, 0f, clip.length);
        to.volume = 0f;
        to.Play();

        if (_bgmFadeCo != null) StopCoroutine(_bgmFadeCo);
        _bgmFadeCo = StartCoroutine(Co_BGM_CrossFade(from, to, fadeSeconds));

        _bgmUsingA = !_bgmUsingA; // 활성 소스 전환
    }

    /// <summary>
    /// 현재 BGM 정지(페이드 아웃).
    /// </summary>
    public void StopBGM(float fadeSeconds = 0.5f)
    {
        var active = _bgmUsingA ? _bgmA : _bgmB;
        if (!active.isPlaying)
            return;

        if (_bgmFadeCo != null) StopCoroutine(_bgmFadeCo);
        _bgmFadeCo = StartCoroutine(Co_BGM_FadeOut(active, fadeSeconds));
    }

    /// <summary>
    /// BGM 일시정지/재개.
    /// </summary>
    public void PauseBGM(bool pause)
    {
        var a = _bgmA; var b = _bgmB;
        if (pause)
        {
            if (a.isPlaying) a.Pause();
            if (b.isPlaying) b.Pause();
        }
        else
        {
            if (a.clip != null && !a.isPlaying) a.UnPause();
            if (b.clip != null && !b.isPlaying) b.UnPause();
        }
    }

    /// <summary>
    /// BGM 볼륨(0~1).
    /// </summary>
    public void SetBGMVolume(float volume01)
    {
        bgmVolume = Mathf.Clamp01(volume01);
        ApplyBGMVolumeAndMute();
    }

    /// <summary>
    /// BGM 뮤트 설정.
    /// </summary>
    public void SetBGMMute(bool mute)
    {
        bgmMute = mute;
        ApplyBGMVolumeAndMute();
    }

    private IEnumerator Co_BGM_CrossFade(AudioSource from, AudioSource to, float duration)
    {
        duration = Mathf.Max(0f, duration);
        float t = 0f;

        // 목표 볼륨(뮤트/볼륨 반영)
        float target = bgmMute ? 0f : bgmVolume;

        // from이 재생 중이 아닐 수 있으므로 초기값 안전 처리
        float fromStart = from != null ? from.volume : 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = duration > 0f ? t / duration : 1f;

            if (from != null) from.volume = Mathf.Lerp(fromStart, 0f, p);
            if (to != null) to.volume = Mathf.Lerp(0f, target, p);

            yield return null;
        }

        if (from != null)
        {
            from.volume = 0f;
            from.Stop();
            from.clip = null;
        }
        if (to != null) to.volume = target;
        _bgmFadeCo = null;
    }

    private IEnumerator Co_BGM_FadeOut(AudioSource src, float duration)
    {
        duration = Mathf.Max(0f, duration);
        float t = 0f;
        float start = src.volume;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = duration > 0f ? t / duration : 1f;
            src.volume = Mathf.Lerp(start, 0f, p);
            yield return null;
        }

        src.volume = 0f;
        src.Stop();
        src.clip = null;
        _bgmFadeCo = null;
    }

    private void ApplyBGMVolumeAndMute()
    {
        float vol = bgmMute ? 0f : bgmVolume;
        if (_bgmA != null) _bgmA.volume = Mathf.Clamp01(vol);
        if (_bgmB != null) _bgmB.volume = Mathf.Clamp01(vol);
    }

    // =====================================================================
    // SFX 제어(풀링)
    // =====================================================================

    /// <summary>
    /// SFX를 키로 재생(2D).
    /// </summary>
    public void PlaySFX(string key, float volumeScale = 1f, float pitch = 1f)
    {
        if (!_sfxDict.TryGetValue(key, out var clip))
        {
            Debug.LogWarning($"[SoundManager] 존재하지 않는 SFX 키: {key}");
            return;
        }
        PlaySFX(clip, volumeScale, pitch);
    }

    /// <summary>
    /// SFX를 AudioClip으로 재생(2D).
    /// </summary>
    public void PlaySFX(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        var src = GetFreeSFXSource();
        Configure2D(src, pitch);
        src.clip = clip;
        src.volume = CalcSFXVolume(volumeScale);
        src.Play();
        StartCoroutine(Co_AutoRelease(src));
    }

    /// <summary>
    /// 3D SFX 재생(월드 좌표).
    /// </summary>
    public void PlaySFXAt(string key, Vector3 worldPos, float volumeScale = 1f, float pitch = 1f, float spatialBlend = 1f)
    {
        if (!_sfxDict.TryGetValue(key, out var clip))
        {
            Debug.LogWarning($"[SoundManager] 존재하지 않는 SFX 키: {key}");
            return;
        }
        PlaySFXAt(clip, worldPos, volumeScale, pitch, spatialBlend);
    }

    /// <summary>
    /// 3D SFX 재생(월드 좌표).
    /// </summary>
    public void PlaySFXAt(AudioClip clip, Vector3 worldPos, float volumeScale = 1f, float pitch = 1f, float spatialBlend = 1f)
    {
        if (clip == null) return;
        var src = GetFreeSFXSource();
        Configure3D(src, pitch, spatialBlend);
        src.transform.position = worldPos;
        src.clip = clip;
        src.volume = CalcSFXVolume(volumeScale);
        src.Play();
        StartCoroutine(Co_AutoRelease(src));
    }

    /// <summary>
    /// 현재 재생 중인 모든 SFX 정지.
    /// </summary>
    public void StopAllSFX()
    {
        for (int i = 0; i < _sfxPool.Count; i++)
        {
            var s = _sfxPool[i];
            if (s.isPlaying) s.Stop();
            s.clip = null;
        }
    }

    /// <summary>
    /// SFX 볼륨(0~1).
    /// </summary>
    public void SetSFXVolume(float volume01)
    {
        sfxVolume = Mathf.Clamp01(volume01);
        ApplySFXVolumeAndMute();
    }

    /// <summary>
    /// SFX 뮤트 설정.
    /// </summary>
    public void SetSFXMute(bool mute)
    {
        sfxMute = mute;
        ApplySFXVolumeAndMute();
    }

    private void ApplySFXVolumeAndMute()
    {
        // 풀에 있는 소스들의 상대 볼륨은 유지하고, 마스터 비율만 반영
        // 간단 구현: 다음 재생부터 반영. 실시간 반영이 필요하면 아래 주석 해제.
        // for (int i = 0; i < _sfxPool.Count; i++)
        // {
        //     if (_sfxPool[i].isPlaying)
        //         _sfxPool[i].volume = Mathf.Clamp01(_sfxPool[i].volume) * (sfxMute ? 0f : sfxVolume);
        // }
    }

    private float CalcSFXVolume(float volumeScale)
    {
        if (sfxMute) return 0f;
        return Mathf.Clamp01(sfxVolume * Mathf.Clamp01(volumeScale));
    }

    private void Configure2D(AudioSource src, float pitch)
    {
        src.outputAudioMixerGroup = sfxGroup != null ? sfxGroup : null;
        src.loop = false;
        src.spatialBlend = 0f;
        src.pitch = Mathf.Clamp(pitch, -3f, 3f);
        src.minDistance = 1f;
        src.maxDistance = 500f;
        src.rolloffMode = AudioRolloffMode.Linear;
    }

    private void Configure3D(AudioSource src, float pitch, float spatialBlend)
    {
        src.outputAudioMixerGroup = sfxGroup != null ? sfxGroup : null;
        src.loop = false;
        src.spatialBlend = Mathf.Clamp01(spatialBlend);
        src.pitch = Mathf.Clamp(pitch, -3f, 3f);
        src.minDistance = 3f;
        src.maxDistance = 50f;
        src.rolloffMode = AudioRolloffMode.Linear;
    }

    private IEnumerator Co_AutoRelease(AudioSource src)
    {
        // 클립 길이가 없거나 재생이 즉시 끝나는 경우를 대비한 방어 코드
        float wait = (src.clip != null && src.clip.length > 0f) ? src.clip.length / Mathf.Max(0.01f, src.pitch) : 0.1f;
        // 타임스케일 영향 없는 정확한 시간 경과를 원하면 WaitForSecondsRealtime 사용
        yield return new WaitForSecondsRealtime(wait + 0.05f);
        if (!src.loop) // 루프가 아니면 자동 해제
        {
            src.Stop();
            src.clip = null;
            // 위치성 SFX는 다음 재생 때 새 위치로 갱신되므로 추가 조치 불필요
        }
    }

    private void PrewarmSFXPool(int count)
    {
        count = Mathf.Clamp(count, 0, sfxPoolHardLimit);
        for (int i = _sfxPool.Count; i < count; i++)
        {
            var src = CreateChildAudioSource($"SFX_{i:00}", loop: false, group: sfxGroup);
            _sfxPool.Add(src);
        }
    }

    private AudioSource GetFreeSFXSource()
    {
        // 재생 중이 아닌 소스 검색
        for (int i = 0; i < _sfxPool.Count; i++)
        {
            if (!_sfxPool[i].isPlaying && _sfxPool[i].clip == null)
                return _sfxPool[i];
        }

        // 없으면 풀 확장(상한 이내)
        if (_sfxPool.Count < sfxPoolHardLimit)
        {
            var src = CreateChildAudioSource($"SFX_{_sfxPool.Count:00}", loop: false, group: sfxGroup);
            _sfxPool.Add(src);
            return src;
        }

        // 최후의 수단: 가장 먼저 만든 소스를 강제 재사용(폴리포니 초과 상황)
        // 품질보다 실패 회피를 우선시
        return _sfxPool[0];
    }

    // =====================================================================
    // 디버그/도움 메서드
    // =====================================================================

    /// <summary>
    /// 등록된 BGM 키 목록 반환.
    /// </summary>
    public IReadOnlyCollection<string> GetBGMKeys() => _bgmDict.Keys;

    /// <summary>
    /// 등록된 SFX 키 목록 반환.
    /// </summary>
    public IReadOnlyCollection<string> GetSFXKeys() => _sfxDict.Keys;

    /// <summary>
    /// 모든 오디오 정지 및 초기화.
    /// </summary>
    public void StopAll()
    {
        StopBGM(0f);
        StopAllSFX();
    }
}
