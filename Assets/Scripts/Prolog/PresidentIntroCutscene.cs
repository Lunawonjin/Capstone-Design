using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PresidentIntroSequence : MonoBehaviour
{
    [Header("대사 시스템")]
    [SerializeField] private BalloonAutoTyper_LocalizedFX balloon;
    [SerializeField] private WorldBubbleAnchor anchor;
    [SerializeField] private string tableName = "Dialogue_Main";

    [Header("대사 키(순서대로)")]
    [SerializeField]
    private string[] dialogueKeys =
    {
        "Dialogue_001_President",
        "Dialogue_002_Player",
        "Dialogue_003_President",
        "Dialogue_004_Player",
        "Dialogue_005_President",
        "Dialogue_006_President",
        "Dialogue_007_President",
        "Dialogue_008_Player",
        "Dialogue_009_President",
        "Dialogue_010_President",
        "Dialogue_011_President",
        "Dialogue_012_Player",
        "Dialogue_013_President",
        "Dialogue_014_President",
        "Dialogue_015_President",
        "Dialogue_016_Player"
    };

    [Header("캐릭터 트랜스폼")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform presidentTransform;

    [Header("중간 전환용 루트 오브젝트")]
    [SerializeField] private GameObject mainRoot;
    [SerializeField] private GameObject playerRoomRoot;

    [Header("Dialogue_013 이후 위치")]
    [SerializeField] private Vector3 playerPosAfter013 = new Vector3(22f, 0f, 0f);
    [SerializeField] private Vector3 presidentPosAfter013 = new Vector3(24f, 0f, 0f);

    [Header("대표 사라짐 연출")]
    [SerializeField] private float vanishDuration = 0.4f;
    [SerializeField] private float vanishMoveUp = 0.5f;

    [Header("플레이어 흔들림 연출")]
    [SerializeField] private float shakeDuration = 0.8f;
    [SerializeField] private float shakeAmount = 0.15f;

    [Header("페이드 설정")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("입력 설정")]
    [SerializeField] private KeyCode advanceKey = KeyCode.Space;
    [SerializeField] private int advanceMouseButton = 0;

    [Header("말풍선 루트(옵션)")]
    [Tooltip("말풍선 전체를 끄고 켤 루트 오브젝트. 비우면 balloon.gameObject 사용.")]
    [SerializeField] private GameObject balloonRoot;

    [Header("대사 중 플레이어 이동 스크립트 비활성화")]
    [SerializeField] private PlayerMove playerMove;
    [SerializeField] private bool autoFindPlayerMove = true;

    [Header("미션 패널")]
    [SerializeField] private Image MissionUI;

    private bool sequenceRunning;
    private bool endingRoutine;

    private bool playerMoveDisabledByMe;
    private bool playerMoveWasEnabled;

    private void Reset()
    {
        balloon = GetComponentInChildren<BalloonAutoTyper_LocalizedFX>(true);
        anchor = GetComponentInChildren<WorldBubbleAnchor>(true);
    }

    private void Awake()
    {
        if (fadeCanvasGroup == null)
        {
            CreateAutoFadeOverlay();
        }

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.gameObject.SetActive(true);
        }

        if (balloonRoot == null && balloon != null)
        {
            balloonRoot = balloon.gameObject;
        }
    }

    private void Start()
    {
        if (balloon == null || anchor == null)
        {
            Debug.LogError("[PresidentIntroSequence] balloon 또는 anchor가 없습니다.");
            enabled = false;
            return;
        }

        if (dialogueKeys == null || dialogueKeys.Length == 0)
        {
            Debug.LogError("[PresidentIntroSequence] dialogueKeys가 비어 있습니다.");
            enabled = false;
            return;
        }

        if (playerTransform == null || presidentTransform == null)
        {
            Debug.LogError("[PresidentIntroSequence] playerTransform 또는 presidentTransform이 없습니다.");
            enabled = false;
            return;
        }

        // PlayerMove 자동 탐색
        if (autoFindPlayerMove && playerMove == null)
        {
            playerMove = FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include);
        }

        // 시퀀스 시작 전에 PlayerMove 비활성화
        DisablePlayerMove();

        Debug.Log("[PresidentIntroSequence] 시퀀스 시작");
        StartCoroutine(RunSequence());
    }

    private void OnDisable()
    {
        // 비정상 종료/중간 비활성화 상황에서 PlayerMove가 꺼진 채로 남는 것 방지
        RestorePlayerMoveIfNeeded();
    }

    private void OnDestroy()
    {
        // 오브젝트 파괴 시에도 PlayerMove 복구
        RestorePlayerMoveIfNeeded();
    }

    private IEnumerator RunSequence()
    {
        sequenceRunning = true;

        for (int i = 0; i < dialogueKeys.Length; i++)
        {
            string key = dialogueKeys[i];
            Debug.Log("[PresidentIntroSequence] 대사 시작: " + key);

            // 일반 대사 출력
            yield return StartCoroutine(ShowOneLine(key, isFirstLine: i == 0));

            Debug.Log("[PresidentIntroSequence] 대사 종료: " + key);

            // Dialogue_013 이후: 이동/전환 연출
            if (i == 12)
            {
                Debug.Log("[PresidentIntroSequence] 013 이후 전환 연출 시작");
                yield return StartCoroutine(HandleMidTransition());
                Debug.Log("[PresidentIntroSequence] 013 이후 전환 연출 종료");
            }

            // Dialogue_015 이후: 대표 사라짐
            if (i == 14)
            {
                Debug.Log("[PresidentIntroSequence] 대표 사라짐 연출 시작");
                yield return StartCoroutine(VanishPresident());
                Debug.Log("[PresidentIntroSequence] 대표 사라짐 연출 종료");
            }

            // Dialogue_016 이후: 페이드 아웃 -> 페이드 인(씬 이동 없음)
            if (i == 15)
            {
                Debug.Log("[PresidentIntroSequence] 마지막 페이드 아웃/인 시작(씬 이동 없음)");
                yield return StartCoroutine(FadeOutThenIn());
                Debug.Log("[PresidentIntroSequence] 마지막 페이드 아웃/인 종료");
                MissionUI.gameObject.SetActive(true);
            }
        }

        sequenceRunning = false;

        // 모든 대사가 끝났으니 PlayerMove 다시 활성화
        EnablePlayerMove();
    }

    /// <summary>
    /// 한 줄 재생 규칙
    /// - 타이핑 중:
    ///   첫 클릭: 즉시 전부 출력
    ///   두 번째 클릭부터: 타이핑 끝나면 다음 줄로 넘김 예약
    /// - 타이핑 끝:
    ///   예약이 있거나 클릭이 들어오면 다음 줄로 진행
    /// </summary>
    private IEnumerator ShowOneLine(string key, bool isFirstLine)
    {
        string speakerId = ExtractSpeakerId(key);
        if (!string.IsNullOrEmpty(speakerId))
        {
            anchor.SetSpeakerId(speakerId, true);
            Debug.Log("[PresidentIntroSequence] 화자: " + speakerId + " (key: " + key + ")");
        }
        else
        {
            Debug.LogWarning("[PresidentIntroSequence] key에서 화자를 추출하지 못했습니다: " + key);
        }

        // 연출 구간이 아닐 때는 말풍선 켜기
        SetBalloonVisible(true);

        bool withEnterFx = isFirstLine;
        balloon.ShowLocalized(tableName, key, withEnterFx);

        bool skipRequested = false;
        bool readyToAdvance = false;

        while (true)
        {
            if (balloon.IsTypingNow)
            {
                if (IsAdvanceDown())
                {
                    if (!skipRequested)
                    {
                        Debug.Log("[PresidentIntroSequence] 타이핑 중 입력 -> 즉시 완전 출력");
                        skipRequested = true;
                        balloon.CompleteInstant();
                    }
                    else
                    {
                        Debug.Log("[PresidentIntroSequence] 타이핑 중 두 번째 입력 -> 다음 줄 예약");
                        readyToAdvance = true;
                    }
                }
            }
            else
            {
                if (readyToAdvance)
                {
                    Debug.Log("[PresidentIntroSequence] 타이핑 종료 + 예약됨 -> 다음 줄로 이동");
                    break;
                }

                if (IsAdvanceDown())
                {
                    Debug.Log("[PresidentIntroSequence] 대사 끝난 뒤 입력 -> 다음 줄로 이동");
                    break;
                }
            }

            yield return null;
        }

        // 입력이 떼어질 때까지 대기(연속 입력 방지)
        while (IsAdvanceHeld())
        {
            yield return null;
        }
    }

    // 013 이후: 페이드 아웃 -> 방 전환 -> 위치 이동 -> 페이드 인 -> 플레이어 흔들림
    // 이 전체 연출 동안 말풍선은 숨기고, 연출 완료 후 다시 켬
    private IEnumerator HandleMidTransition()
    {
        Debug.Log("[PresidentIntroSequence] HandleMidTransition 시작");

        // 말풍선 숨기기
        SetBalloonVisible(false);

        // 1) 화면 어둡게
        yield return StartCoroutine(FadeTo(1f));

        // 2) 방 전환
        if (mainRoot != null)
            mainRoot.SetActive(false);
        if (playerRoomRoot != null)
            playerRoomRoot.SetActive(true);

        // 3) 캐릭터 위치 이동
        if (playerTransform != null)
            playerTransform.position = playerPosAfter013;
        if (presidentTransform != null)
            presidentTransform.position = presidentPosAfter013;

        // 4) 앵커 재연결
        if (anchor != null)
        {
            Debug.Log("[PresidentIntroSequence] PlayerRoom에서 WorldBubbleAnchor 재연결");

            anchor.SetWorldCamera(Camera.main, snap: false);
            anchor.SetSpeakerId("President", snapNow: false);

            yield return new WaitForEndOfFrame();
            anchor.SnapNow();

            Debug.Log("[PresidentIntroSequence] 방 전환 후 앵커 스냅 완료");
        }

        // 5) 화면 다시 밝게
        yield return StartCoroutine(FadeTo(0f));

        // 6) 플레이어 흔들림(말풍선 숨김 유지)
        yield return StartCoroutine(ShakePlayer());

        // 7) 연출 종료 후 말풍선 다시 켬
        SetBalloonVisible(true);
    }

    private IEnumerator VanishPresident()
    {
        if (presidentTransform == null)
            yield break;

        SpriteRenderer sr = presidentTransform.GetComponentInChildren<SpriteRenderer>();
        Color baseColor = sr != null ? sr.color : Color.white;

        Vector3 startPos = presidentTransform.position;
        Vector3 endPos = startPos + new Vector3(0f, vanishMoveUp, 0f);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, vanishDuration);
            float s = t * t;

            presidentTransform.position = Vector3.Lerp(startPos, endPos, s);

            if (sr != null)
            {
                float a = Mathf.Lerp(baseColor.a, 0f, s);
                sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            }

            yield return null;
        }

        if (sr != null)
        {
            sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
        }

        presidentTransform.gameObject.SetActive(false);
    }

    private IEnumerator ShakePlayer()
    {
        if (playerTransform == null)
            yield break;

        Vector3 origin = playerTransform.position;
        float timer = 0f;

        while (timer < shakeDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / Mathf.Max(0.0001f, shakeDuration);

            float offsetX = Mathf.Sin(progress * Mathf.PI * 8f) * shakeAmount;
            float offsetY = Mathf.Sin(progress * Mathf.PI * 6f) * shakeAmount * 0.5f;
            playerTransform.position = origin + new Vector3(offsetX, offsetY, 0f);

            yield return null;
        }

        playerTransform.position = origin;
    }

    // 마지막에 페이드 아웃 후 페이드 인으로 마무리(씬 이동 없음)
    private IEnumerator FadeOutThenIn()
    {
        if (endingRoutine)
            yield break;

        endingRoutine = true;

        // 말풍선 숨기기
        SetBalloonVisible(false);

        // 1) 페이드 아웃
        yield return StartCoroutine(FadeTo(1f, fadeDuration));

        // 2) 잠깐 정지
        yield return new WaitForSeconds(0.1f);

        // 3) 페이드 인
        yield return StartCoroutine(FadeTo(0f, fadeDuration));
    }

    // 페이드만 담당(말풍선 on/off는 바깥에서 처리)
    private IEnumerator FadeTo(float targetAlpha, float durationOverride = -1f)
    {
        if (fadeCanvasGroup == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] fadeCanvasGroup이 없어 페이드를 건너뜁니다. targetAlpha=" + targetAlpha);
            yield break;
        }

        float duration = (durationOverride > 0f) ? durationOverride : fadeDuration;

        fadeCanvasGroup.gameObject.SetActive(true);

        float startAlpha = fadeCanvasGroup.alpha;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / Mathf.Max(0.0001f, duration));
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
    }

    private bool IsAdvanceDown()
    {
        if (Input.GetKeyDown(advanceKey))
            return true;
        if (Input.GetMouseButtonDown(advanceMouseButton))
            return true;
        return false;
    }

    private bool IsAdvanceHeld()
    {
        if (Input.GetKey(advanceKey))
            return true;
        if (Input.GetMouseButton(advanceMouseButton))
            return true;
        return false;
    }

    private static string ExtractSpeakerId(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        int idx = key.LastIndexOf('_');
        if (idx < 0 || idx >= key.Length - 1)
            return null;

        return key.Substring(idx + 1).Trim();
    }

    private void SetBalloonVisible(bool visible)
    {
        if (balloonRoot == null && balloon != null)
        {
            balloonRoot = balloon.gameObject;
        }

        if (balloonRoot != null)
        {
            Debug.Log("[PresidentIntroSequence] 말풍선 표시(" + visible + "): " + balloonRoot.name);
            balloonRoot.SetActive(visible);
        }
        else
        {
            Debug.LogWarning("[PresidentIntroSequence] balloonRoot가 없어 말풍선 표시를 바꿀 수 없습니다.");
        }
    }

    // PlayerMove를 끄는 처리
    private void DisablePlayerMove()
    {
        if (playerMove == null) return;

        playerMoveWasEnabled = playerMove.enabled;
        if (playerMoveWasEnabled)
        {
            playerMove.enabled = false;
            playerMoveDisabledByMe = true;
            Debug.Log("[PresidentIntroSequence] PlayerMove 비활성화");
        }
    }

    // PlayerMove를 다시 켜는 처리
    private void EnablePlayerMove()
    {
        if (playerMove == null) return;
        if (!playerMoveDisabledByMe) return;

        playerMove.enabled = playerMoveWasEnabled;
        playerMoveDisabledByMe = false;
        Debug.Log("[PresidentIntroSequence] PlayerMove 활성화 복구");
    }

    // 혹시라도 중간에 이 스크립트가 꺼지면 PlayerMove를 복구
    private void RestorePlayerMoveIfNeeded()
    {
        if (sequenceRunning && playerMoveDisabledByMe)
        {
            EnablePlayerMove();
        }
    }

    private void CreateAutoFadeOverlay()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] 상위 Canvas가 없어 자동 페이드 오버레이를 만들 수 없습니다.");
            return;
        }

        RectTransform canvasRect = parentCanvas.transform as RectTransform;
        if (canvasRect == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] 상위 Canvas에 RectTransform이 없어 자동 페이드 오버레이를 만들 수 없습니다.");
            return;
        }

        GameObject fadeObj = new GameObject("AutoFadeOverlay");
        fadeObj.layer = parentCanvas.gameObject.layer;
        fadeObj.transform.SetParent(parentCanvas.transform, false);

        RectTransform rt = fadeObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.anchoredPosition = Vector2.zero;

        Image img = fadeObj.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;

        fadeCanvasGroup = fadeObj.AddComponent<CanvasGroup>();
        fadeCanvasGroup.alpha = 0f;

        fadeObj.transform.SetAsLastSibling();

        Debug.Log("[PresidentIntroSequence] Canvas '" + parentCanvas.name + "' 아래에 자동 페이드 오버레이 생성 완료");
    }
}
