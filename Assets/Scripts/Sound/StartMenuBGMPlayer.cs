// StartMenuBGMPlayer.cs
// Unity 6 (LTS) 기준. StartMenu 씬에 배치하면 씬 진입 시 지정한 BGM을 무한 반복 재생한다.
// 전제: SoundManager(앞서 제공한 스크립트)가 어느 한 씬에서 생성되어 DontDestroyOnLoad 상태여야 한다.

using UnityEngine;

[DisallowMultipleComponent]
public class StartMenuBGMPlayer : MonoBehaviour
{
    [Header("재생할 BGM 키 (SoundManager의 BGM 사운드 뱅크에 등록된 Key)")]
    public string bgmKey = "Starest";

    [Header("페이드 인/아웃 설정")]
    [Tooltip("씬 진입 시 페이드 인 초")]
    public float fadeInSeconds = 0.75f;
    [Tooltip("씬 이탈 시 페이드 아웃 초 (옵션)")]
    public float fadeOutSeconds = 0.5f;

    [Header("시작 타임 (초 단위, 0이면 처음부터)")]
    public float startTime = 0f;

    [Header("씬 이탈 시 BGM 정지 여부")]
    public bool stopOnDestroy = false;

    // 설명:
    // - SoundManager의 BGM AudioSource는 loop=true로 생성되므로 별도 설정 없이 무한 반복된다.
    // - bgmKey는 SoundManager의 BGM Clips 배열에 등록한 Key와 일치해야 한다.

    private void Start()
    {
        // SoundManager가 준비되지 않은 경우를 대비한 방어 코드
        if (SoundManager.Instance == null)
        {
            Debug.LogWarning("[StartMenuBGMPlayer] SoundManager 인스턴스를 찾지 못했습니다. 사운드 매니저를 먼저 생성하세요.");
            return;
        }

        if (string.IsNullOrEmpty(bgmKey))
        {
            Debug.LogWarning("[StartMenuBGMPlayer] bgmKey가 비어 있습니다. SoundManager의 BGM 뱅크 Key를 입력하세요.");
            return;
        }

        // StartMenu 씬 진입 시 BGM 재생 (무한 반복은 SoundManager 내부 설정에 의해 자동)
        SoundManager.Instance.PlayBGM(bgmKey, fadeInSeconds, startTime);
    }

    private void OnDestroy()
    {
        // StartMenu 씬을 떠날 때 BGM을 유지하고 싶으면 false,
        // 씬 이동 시 자연스럽게 끄고 싶으면 true로 설정
        if (stopOnDestroy && SoundManager.Instance != null)
        {
            SoundManager.Instance.StopBGM(fadeOutSeconds);
        }
    }
}
