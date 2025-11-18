using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 대표와 플레이어가 대화하는 인트로 컷신 전용 스크립트
/// - BalloonAutoTyper_LocalizedFX + WorldBubbleAnchor 사용
/// - Dialogue_001 ~ 016 순서대로 직접 출력
/// - Dialogue_013_President 이후 연출:
///   1) 페이드 아웃
///   2) PlayerRoom 오브젝트 활성화, main 루트 비활성화
///   3) 플레이어 (22,0,0), 대표 (24,0,0) 이동
///   4) 페이드 인
///   5) 플레이어 흔들기 연출
/// - Dialogue_015_President 이후 대표 오브젝트 비활성화
/// - Dialogue_016_Player 이후: 페이드 아웃 → "Player's Room" 씬 로드
/// - 전체 진행 로그를 Debug.Log로 출력
/// </summary>
[DisallowMultipleComponent]
public class PresidentIntroSequence : MonoBehaviour
{
    [Header("다이얼로그 시스템")]
    [SerializeField] private BalloonAutoTyper_LocalizedFX bubble;   // 말풍선 타자기
    [SerializeField] private WorldBubbleAnchor anchor;              // 월드 → UI 앵커
    [SerializeField] private string tableName = "Dialogue_Main";    // String Table 이름

    [Header("화자 머리 위치")]
    [SerializeField] private Transform playerHead;      // 플레이어 머리 위치
    [SerializeField] private Transform presidentHead;   // 대표 머리 위치

    [Header("캐릭터 실제 Transform")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform presidentTransform;

    [Header("루트 오브젝트 전환")]
    [Tooltip("집 안 오브젝트 루트 (PlayerRoom)")]
    [SerializeField] private GameObject playerRoomRoot;
    [Tooltip("시작 화면(로비 등) 루트 (main)")]
    [SerializeField] private GameObject mainRoot;

    [Header("페이드 연출")]
    [Tooltip("검은 페이드용 CanvasGroup (알파 변경)")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeDuration = 0.8f;

    [Header("플레이어 흔들기 연출")]
    [SerializeField] private float wobbleDuration = 1.0f;
    [SerializeField] private float wobbleAmplitude = 0.1f;
    [SerializeField] private float wobbleFrequency = 8.0f;

    [Header("마지막에 로드할 씬 이름")]
    [SerializeField] private string playerRoomSceneName = "Player's Room";

    private bool _isRunning;

    private void Reset()
    {
        if (bubble == null) bubble = GetComponentInChildren<BalloonAutoTyper_LocalizedFX>(true);
        if (anchor == null) anchor = GetComponentInChildren<WorldBubbleAnchor>(true);
    }

    private void Awake()
    {
        Debug.Log("[PresidentIntroSequence] Awake 호출");

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
        }
    }

    private void Start()
    {
        Debug.Log("[PresidentIntroSequence] Start 호출, 컷신 코루틴 시작 시도");
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        if (_isRunning)
        {
            Debug.LogWarning("[PresidentIntroSequence] 이미 실행 중이어서 두 번 실행을 막았습니다.");
            yield break;
        }
        _isRunning = true;

        if (!CheckRefs())
        {
            Debug.LogError("[PresidentIntroSequence] 참조가 비어 있어서 컷신 실행을 중단합니다.");
            yield break;
        }

        Debug.Log("[PresidentIntroSequence] 컷신 시작");

        // 001~012까지 순서대로 출력
        yield return ShowLine("Dialogue_001_President", presidentHead, true);
        yield return ShowLine("Dialogue_002_Player", playerHead);
        yield return ShowLine("Dialogue_003_President", presidentHead);
        yield return ShowLine("Dialogue_004_Player", playerHead);
        yield return ShowLine("Dialogue_005_President", presidentHead);
        yield return ShowLine("Dialogue_006_President", presidentHead);
        yield return ShowLine("Dialogue_007_President", presidentHead);
        yield return ShowLine("Dialogue_008_Player", playerHead);
        yield return ShowLine("Dialogue_009_President", presidentHead);
        yield return ShowLine("Dialogue_010_President", presidentHead);
        yield return ShowLine("Dialogue_011_President", presidentHead);
        yield return ShowLine("Dialogue_012_Player", playerHead);

        // 013: 대사 후 연출(페이드 아웃 → PlayerRoom 전환 → 이동 → 페이드 인 → 흔들기)
        yield return ShowLine("Dialogue_013_President", presidentHead);
        Debug.Log("[PresidentIntroSequence] Dialogue_013_President 이후 연출 시작");

        // 페이드 아웃
        yield return Fade(1f, fadeDuration, "[PresidentIntroSequence] 페이드 아웃 완료");

        // PlayerRoom 활성화, main 비활성화
        Debug.Log("[PresidentIntroSequence] PlayerRoom / main 루트 전환 시도");
        if (playerRoomRoot != null)
        {
            playerRoomRoot.SetActive(true);
            Debug.Log("[PresidentIntroSequence] PlayerRoom Root 활성화");
        }
        else
        {
            Debug.LogWarning("[PresidentIntroSequence] playerRoomRoot가 비어 있습니다.");
        }

        if (mainRoot != null)
        {
            mainRoot.SetActive(false);
            Debug.Log("[PresidentIntroSequence] main Root 비활성화");
        }
        else
        {
            Debug.LogWarning("[PresidentIntroSequence] mainRoot가 비어 있습니다.");
        }

        // 좌표 이동
        if (playerTransform != null)
        {
            playerTransform.position = new Vector3(22f, 0f, 0f);
            Debug.Log($"[PresidentIntroSequence] 플레이어 위치 이동: {playerTransform.position}");
        }
        else
        {
            Debug.LogWarning("[PresidentIntroSequence] playerTransform이 비어 있어 위치를 이동하지 못했습니다.");
        }

        if (presidentTransform != null)
        {
            presidentTransform.position = new Vector3(24f, 0f, 0f);
            Debug.Log($"[PresidentIntroSequence] 대표 위치 이동: {presidentTransform.position}");
        }
        else
        {
            Debug.LogWarning("[PresidentIntroSequence] presidentTransform이 비어 있어 위치를 이동하지 못했습니다.");
        }

        // 페이드 인
        yield return Fade(0f, fadeDuration, "[PresidentIntroSequence] 페이드 인 완료");

        // 플레이어 흔들기
        if (playerTransform != null)
        {
            Debug.Log("[PresidentIntroSequence] 플레이어 흔들기 연출 시작");
            yield return StartCoroutine(WobblePlayer());
            Debug.Log("[PresidentIntroSequence] 플레이어 흔들기 연출 종료");
        }

        // 014, 015, 016 이어서 출력
        yield return ShowLine("Dialogue_014_President", presidentHead);
        yield return ShowLine("Dialogue_015_President", presidentHead);

        // 015 이후 대표 슈숙: 대표 비활성화
        if (presidentTransform != null)
        {
            GameObject go = presidentTransform.gameObject;
            Debug.Log("[PresidentIntroSequence] Dialogue_015_President 이후 대표 오브젝트 비활성화");
            go.SetActive(false);
        }

        yield return ShowLine("Dialogue_016_Player", playerHead);

        // 마지막: 페이드 아웃 → Scene 로드
        Debug.Log("[PresidentIntroSequence] Dialogue_016_Player 이후 최종 페이드 아웃 시작");
        yield return Fade(1f, fadeDuration, "[PresidentIntroSequence] 최종 페이드 아웃 완료");

        Debug.Log($"[PresidentIntroSequence] Scene 로드 시도: '{playerRoomSceneName}'");

        try
        {
            SceneManager.LoadScene(playerRoomSceneName);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PresidentIntroSequence] SceneManager.LoadScene 예외 발생: '{playerRoomSceneName}'\n{e}");
        }

        Debug.Log("[PresidentIntroSequence] 컷신 종료");
        _isRunning = false;
    }

    /// <summary>
    /// 한 줄 대사를 표시한 뒤, 출력 완료 및 플레이어 입력(스페이스, 좌클릭)까지 기다림
    /// </summary>
    private IEnumerator ShowLine(string key, Transform speakerHead, bool withEnterFX = false)
    {
        Debug.Log($"[PresidentIntroSequence] ShowLine 시작: key='{key}', speaker='{speakerHead?.name ?? "null"}'");

        if (anchor == null || bubble == null)
        {
            Debug.LogError("[PresidentIntroSequence] anchor 또는 bubble이 비어 있습니다. ShowLine을 실행할 수 없습니다.");
            yield break;
        }

        if (speakerHead != null)
        {
            anchor.SetTarget(speakerHead, true);
        }
        else
        {
            Debug.LogWarning($"[PresidentIntroSequence] speakerHead가 null입니다. key='{key}'");
        }

        bubble.ShowLocalized(tableName, key, withEnterFX);

        // 타자기 완료까지 대기 (스페이스/클릭으로 스킵 가능)
        while (bubble.IsTypingNow)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
            {
                Debug.Log($"[PresidentIntroSequence] 타이핑 중 스킵 요청: key='{key}'");
                bubble.CompleteInstant();
            }
            yield return null;
        }

        // 출력 완료 후, 플레이어가 스페이스/클릭 한 번 더 눌러야 다음으로 진행
        Debug.Log($"[PresidentIntroSequence] 타이핑 완료, 입력 대기 상태 진입: key='{key}'");

        bool confirmed = false;
        while (!confirmed)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
            {
                confirmed = true;
                Debug.Log($"[PresidentIntroSequence] 입력 확인, 다음 줄로 진행: key='{key}'");
            }
            yield return null;
        }
    }

    /// <summary>
    /// CanvasGroup 알파를 targetAlpha로 페이드 (0 = 밝음, 1 = 완전 암흑)
    /// </summary>
    private IEnumerator Fade(float targetAlpha, float duration, string logOnComplete = null)
    {
        if (fadeCanvasGroup == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] fadeCanvasGroup이 비어 있어 페이드 없이 바로 전환합니다.");
            yield break;
        }

        float startAlpha = fadeCanvasGroup.alpha;
        float time = 0f;

        Debug.Log($"[PresidentIntroSequence] Fade 시작: {startAlpha} → {targetAlpha}, duration={duration}");

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;

        if (!string.IsNullOrEmpty(logOnComplete))
        {
            Debug.Log(logOnComplete);
        }
    }

    /// <summary>
    /// 플레이어 Transform을 위아래로 살짝 흔드는 연출
    /// </summary>
    private IEnumerator WobblePlayer()
    {
        if (playerTransform == null)
            yield break;

        Vector3 basePos = playerTransform.position;
        float elapsed = 0f;

        while (elapsed < wobbleDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed * wobbleFrequency * Mathf.PI * 2f;
            float offsetY = Mathf.Sin(t) * wobbleAmplitude;
            playerTransform.position = new Vector3(basePos.x, basePos.y + offsetY, basePos.z);
            yield return null;
        }

        playerTransform.position = basePos;
    }

    /// <summary>
    /// 필수 참조 확인용
    /// </summary>
    private bool CheckRefs()
    {
        bool ok = true;

        if (bubble == null)
        {
            Debug.LogError("[PresidentIntroSequence] bubble이 비어 있습니다.");
            ok = false;
        }
        if (anchor == null)
        {
            Debug.LogError("[PresidentIntroSequence] anchor가 비어 있습니다.");
            ok = false;
        }
        if (playerHead == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] playerHead가 비어 있습니다. 플레이어 말풍선 위치를 못 찾을 수 있습니다.");
        }
        if (presidentHead == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] presidentHead가 비어 있습니다. 대표 말풍선 위치를 못 찾을 수 있습니다.");
        }
        if (playerTransform == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] playerTransform이 비어 있습니다. 위치 이동/흔들기 불가.");
        }
        if (presidentTransform == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] presidentTransform이 비어 있습니다. 위치 이동 및 슈숙 연출 불가.");
        }
        if (fadeCanvasGroup == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] fadeCanvasGroup이 비어 있습니다. 페이드 연출 없이 진행됩니다.");
        }
        if (string.IsNullOrEmpty(playerRoomSceneName))
        {
            Debug.LogWarning("[PresidentIntroSequence] playerRoomSceneName이 비어 있습니다. 마지막 씬 로드에서 문제가 생길 수 있습니다.");
        }

        return ok;
    }
}
