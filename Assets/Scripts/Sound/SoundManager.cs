using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("AudioMixer 라우팅(선택)")]
    public AudioMixerGroup bgmGroup;
    public AudioMixerGroup sfxGroup;

    [System.Serializable]
    public struct NamedClip
    {
        public string key;       // 호출 키 (예: "Title")
        public AudioClip clip;   // 오디오 파일
    }

    [Header("BGM 등록")]
    public NamedClip[] bgmClips;

    [Header("SFX 등록")]
    public NamedClip[] sfxClips;

    private Dictionary<string, AudioClip> _bgmDict = new Dictionary<string, AudioClip>();
    private Dictionary<string, AudioClip> _sfxDict = new Dictionary<string, AudioClip>();

    [Header("BGM 설정")]
    [Range(0f, 1f)] public float bgmVolume = 1f;
    public bool bgmMute = false;

    private AudioSource _bgmA;
    private AudioSource _bgmB;
    private bool _bgmUsingA = true;
    private Coroutine _bgmFadeCo;

    [Header("SFX 설정")]
    [Range(0f, 1f)] public float sfxVolume = 1f;
    public bool sfxMute = false;
    public int sfxPoolSize = 10;
    public int sfxPoolHardLimit = 32;

    private readonly List<AudioSource> _sfxPool = new List<AudioSource>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _bgmA = CreateChildAudioSource("BGM_A", true, bgmGroup);
        _bgmB = CreateChildAudioSource("BGM_B", true, bgmGroup);
        ApplyBGMVolumeAndMute();

        PrewarmSFXPool(sfxPoolSize);
        RebuildDictionaries();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyBGMVolumeAndMute();
            ApplySFXVolumeAndMute();
        }
    }

    public void RebuildDictionaries()
    {
        _bgmDict.Clear();
        foreach (var nc in bgmClips) if (!string.IsNullOrEmpty(nc.key)) _bgmDict[nc.key] = nc.clip;

        _sfxDict.Clear();
        foreach (var nc in sfxClips) if (!string.IsNullOrEmpty(nc.key)) _sfxDict[nc.key] = nc.clip;
    }

    private AudioSource CreateChildAudioSource(string name, bool loop, AudioMixerGroup group)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;
        if (group != null) src.outputAudioMixerGroup = group;
        return src;
    }

    // =====================================================================
    // BGM 기능
    // =====================================================================

    public void PlayBGM(string key, float fadeSeconds = 0.75f, float startTime = 0f)
    {
        if (!_bgmDict.TryGetValue(key, out var clip))
        {
            Debug.LogError($"[SoundManager] BGM 키를 찾을 수 없습니다: '{key}'");
            return;
        }
        PlayBGM(clip, fadeSeconds, startTime);
    }

    public void PlayBGM(AudioClip clip, float fadeSeconds = 0.75f, float startTime = 0f)
    {
        if (clip == null) return;

        // ★ 핵심: 이미 재생 중인 클립이면 무시 (음악 끊김/재시작 방지)
        var currentSource = _bgmUsingA ? _bgmA : _bgmB;
        if (currentSource.isPlaying && currentSource.clip == clip)
        {
            // Debug.Log($"[SoundManager] '{clip.name}' 이미 재생 중. 유지합니다.");
            return;
        }

        Debug.Log($"[SoundManager] BGM 교체 시작: {clip.name}");

        var from = _bgmUsingA ? _bgmA : _bgmB;
        var to = _bgmUsingA ? _bgmB : _bgmA;

        to.clip = clip;
        to.time = Mathf.Clamp(startTime, 0f, clip.length);
        to.volume = 0f;
        to.Play();

        if (_bgmFadeCo != null) StopCoroutine(_bgmFadeCo);
        _bgmFadeCo = StartCoroutine(Co_BGM_CrossFade(from, to, fadeSeconds));

        _bgmUsingA = !_bgmUsingA;
    }

    public void StopBGM(float fadeSeconds = 0.5f)
    {
        var active = _bgmUsingA ? _bgmA : _bgmB;
        if (!active.isPlaying) return;

        Debug.Log("[SoundManager] BGM 정지");
        if (_bgmFadeCo != null) StopCoroutine(_bgmFadeCo);
        _bgmFadeCo = StartCoroutine(Co_BGM_FadeOut(active, fadeSeconds));
    }

    // --- UI 슬라이더 연동용 함수들 ---
    public void SetBGMVolume(float volume)
    {
        bgmVolume = Mathf.Clamp01(volume);
        ApplyBGMVolumeAndMute();
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        // SFX는 즉시 반영 안 해도 다음 재생 때 반영됨
    }

    public void SetBGMMute(bool mute)
    {
        bgmMute = mute;
        ApplyBGMVolumeAndMute();
    }

    public void SetSFXMute(bool mute)
    {
        sfxMute = mute;
    }

    // --- 내부 로직 ---
    private void ApplyBGMVolumeAndMute()
    {
        float vol = bgmMute ? 0f : bgmVolume;
        if (_bgmA != null) _bgmA.volume = vol; // 페이드 중에는 덮어씌워질 수 있음
        if (_bgmB != null) _bgmB.volume = vol;
    }

    private void ApplySFXVolumeAndMute() { /* ... */ }

    private IEnumerator Co_BGM_CrossFade(AudioSource from, AudioSource to, float duration)
    {
        float t = 0f;
        float target = bgmMute ? 0f : bgmVolume;
        float fromStart = from != null ? from.volume : 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = duration > 0f ? t / duration : 1f;
            if (from != null) from.volume = Mathf.Lerp(fromStart, 0f, p);
            if (to != null) to.volume = Mathf.Lerp(0f, target, p);
            yield return null;
        }

        if (from != null) { from.volume = 0f; from.Stop(); from.clip = null; }
        if (to != null) to.volume = target;
        _bgmFadeCo = null;
    }

    private IEnumerator Co_BGM_FadeOut(AudioSource src, float duration)
    {
        float t = 0f;
        float start = src.volume;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = duration > 0f ? t / duration : 1f;
            src.volume = Mathf.Lerp(start, 0f, p);
            yield return null;
        }
        src.volume = 0f; src.Stop(); src.clip = null; _bgmFadeCo = null;
    }

    // =====================================================================
    // SFX 기능
    // =====================================================================

    // --- 일반 SFX (원샷) ---
    public void PlaySFX(string key, float v = 1f, float p = 1f)
    {
        if (_sfxDict.TryGetValue(key, out var c)) PlaySFX(c, v, p);
    }

    public void PlaySFX(AudioClip c, float v = 1f, float p = 1f)
    {
        if (c == null) return;
        var s = GetFreeSFXSource();
        ConfigureSource(s, p, 0f);
        s.clip = c;
        s.volume = (sfxMute ? 0f : sfxVolume * v);
        s.Play();
        StartCoroutine(Co_AutoRelease(s));
    }

    public void PlaySFXAt(string key, Vector3 pos, float v = 1f, float p = 1f)
    {
        if (_sfxDict.TryGetValue(key, out var c)) PlaySFXAt(c, pos, v, p);
    }

    public void PlaySFXAt(AudioClip c, Vector3 pos, float v = 1f, float p = 1f)
    {
        if (c == null) return;
        var s = GetFreeSFXSource();
        ConfigureSource(s, p, 1f);
        s.transform.position = pos;
        s.clip = c;
        s.volume = (sfxMute ? 0f : sfxVolume * v);
        s.Play();
        StartCoroutine(Co_AutoRelease(s));
    }

    public void StopAllSFX()
    {
        foreach (var s in _sfxPool)
        {
            if (s.isPlaying) s.Stop();
            s.loop = false;
            s.clip = null;
        }
    }

    // ⭐ 루프 SFX 재생 (반환된 AudioSource로 나중에 정지 가능)
    public AudioSource PlaySFXLoop(string key, float v = 1f, float p = 1f)
    {
        if (!_sfxDict.TryGetValue(key, out var c))
        {
            Debug.LogWarning($"[SoundManager] SFX 키를 찾을 수 없습니다: '{key}'");
            return null;
        }
        return PlaySFXLoop(c, v, p);
    }

    public AudioSource PlaySFXLoop(AudioClip c, float v = 1f, float p = 1f)
    {
        if (c == null) return null;
        var s = GetFreeSFXSource();
        ConfigureSource(s, p, 0f);
        s.loop = true; // ⭐ 루프 활성화
        s.clip = c;
        s.volume = (sfxMute ? 0f : sfxVolume * v);
        s.Play();
        Debug.Log($"[SoundManager] 루프 SFX 재생 시작: {c.name}");
        return s;
    }

    // ⭐ 특정 AudioSource 정지
    public void StopSFXSource(AudioSource s)
    {
        if (s == null) return;
        Debug.Log($"[SoundManager] SFX 정지: {s.clip?.name ?? "null"}");
        s.Stop();
        s.loop = false;
        s.clip = null;
    }

    private void ConfigureSource(AudioSource s, float p, float sb)
    {
        s.outputAudioMixerGroup = sfxGroup;
        s.pitch = p;
        s.spatialBlend = sb;
        s.loop = false;
    }

    private IEnumerator Co_AutoRelease(AudioSource s)
    {
        yield return new WaitForSecondsRealtime(s.clip.length + 0.1f);
        if (!s.loop)
        {
            s.Stop();
            s.clip = null;
        }
    }

    private void PrewarmSFXPool(int c)
    {
        for (int i = 0; i < c; i++)
            _sfxPool.Add(CreateChildAudioSource($"SFX_{i}", false, sfxGroup));
    }

    private AudioSource GetFreeSFXSource()
    {
        foreach (var s in _sfxPool)
            if (!s.isPlaying) return s;

        if (_sfxPool.Count < sfxPoolHardLimit)
        {
            var s = CreateChildAudioSource($"SFX_{_sfxPool.Count}", false, sfxGroup);
            _sfxPool.Add(s);
            return s;
        }
        return _sfxPool[0];
    }
}