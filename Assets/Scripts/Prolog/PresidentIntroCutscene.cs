using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PresidentIntroSequence : MonoBehaviour
{
    [Header("Dialogue System")]
    [SerializeField] private BalloonAutoTyper_LocalizedFX balloon;
    [SerializeField] private WorldBubbleAnchor anchor;
    [SerializeField] private string tableName = "Dialogue_Main";

    [Header("Dialogue Keys (in order)")]
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

    [Header("Character Transforms")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform presidentTransform;

    [Header("Root Objects for mid transition")]
    [SerializeField] private GameObject mainRoot;
    [SerializeField] private GameObject playerRoomRoot;

    [Header("Positions after Dialogue_013")]
    [SerializeField] private Vector3 playerPosAfter013 = new Vector3(22f, 0f, 0f);
    [SerializeField] private Vector3 presidentPosAfter013 = new Vector3(24f, 0f, 0f);

    [Header("President vanish effect")]
    [SerializeField] private float vanishDuration = 0.4f;
    [SerializeField] private float vanishMoveUp = 0.5f;

    [Header("Player shake effect")]
    [SerializeField] private float shakeDuration = 0.8f;
    [SerializeField] private float shakeAmount = 0.15f;

    [Header("Fade settings")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("Input settings")]
    [SerializeField] private KeyCode advanceKey = KeyCode.Space;
    [SerializeField] private int advanceMouseButton = 0;

    [Header("Next scene name")]
    [SerializeField] private string nextSceneName = "Player's Room";

    [Header("Balloon Root (optional)")]
    [Tooltip("말풍선 전체를 끄고 켤 루트 오브젝트. 비우면 balloon.gameObject 사용.")]
    [SerializeField] private GameObject balloonRoot;

    private bool sequenceRunning;
    private bool sceneLoading;

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
            Debug.LogError("[PresidentIntroSequence] balloon or anchor is missing.");
            enabled = false;
            return;
        }

        if (dialogueKeys == null || dialogueKeys.Length == 0)
        {
            Debug.LogError("[PresidentIntroSequence] dialogueKeys is empty.");
            enabled = false;
            return;
        }

        if (playerTransform == null || presidentTransform == null)
        {
            Debug.LogError("[PresidentIntroSequence] playerTransform or presidentTransform is missing.");
            enabled = false;
            return;
        }

        Debug.Log("[PresidentIntroSequence] Sequence start.");
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        sequenceRunning = true;

        for (int i = 0; i < dialogueKeys.Length; i++)
        {
            string key = dialogueKeys[i];
            Debug.Log("[PresidentIntroSequence] Line start: " + key);

            // 일반 대사 출력
            yield return StartCoroutine(ShowOneLine(key, isFirstLine: i == 0));

            Debug.Log("[PresidentIntroSequence] Line end: " + key);

            // Dialogue_013 이후: 이동 연출
            if (i == 12)
            {
                Debug.Log("[PresidentIntroSequence] Post-013 transition start.");
                yield return StartCoroutine(HandleMidTransition());
                Debug.Log("[PresidentIntroSequence] Post-013 transition end.");
            }

            // Dialogue_015 이후: 대표 사라짐
            if (i == 14)
            {
                Debug.Log("[PresidentIntroSequence] President vanish start.");
                yield return StartCoroutine(VanishPresident());
                Debug.Log("[PresidentIntroSequence] President vanish end.");
            }

            // Dialogue_016 이후: 페이드 아웃 후 새 씬
            if (i == 15)
            {
                Debug.Log("[PresidentIntroSequence] Final fade and scene load start.");
                yield return StartCoroutine(FadeOutAndLoadScene());
                Debug.Log("[PresidentIntroSequence] Final fade and scene load end.");
            }
        }

        sequenceRunning = false;
    }

    /// <summary>
    /// 한 줄 재생:
    /// - 타이핑 중
    ///   첫 클릭: 완전 출력
    ///   두 번째 클릭부터: 다음 줄로 넘길 준비(readyToAdvance)
    /// - 타이핑 끝남
    ///   readyToAdvance가 이미 true이거나, 새 클릭이 들어오면 다음 줄로 진행
    /// </summary>
    private IEnumerator ShowOneLine(string key, bool isFirstLine)
    {
        string speakerId = ExtractSpeakerId(key);
        if (!string.IsNullOrEmpty(speakerId))
        {
            anchor.SetSpeakerId(speakerId, true);
            Debug.Log("[PresidentIntroSequence] Speaker: " + speakerId + " (key: " + key + ")");
        }
        else
        {
            Debug.LogWarning("[PresidentIntroSequence] Could not extract speaker from key: " + key);
        }

        // 연출 구간이 아닐 때는 말풍선 켜져 있어야 함
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
                        Debug.Log("[PresidentIntroSequence] Input while typing, complete instantly.");
                        skipRequested = true;
                        balloon.CompleteInstant();
                    }
                    else
                    {
                        Debug.Log("[PresidentIntroSequence] Second click while typing, will advance after typing.");
                        readyToAdvance = true;
                    }
                }
            }
            else
            {
                if (readyToAdvance)
                {
                    Debug.Log("[PresidentIntroSequence] Typing finished and advance was reserved. Moving to next line.");
                    break;
                }

                if (IsAdvanceDown())
                {
                    Debug.Log("[PresidentIntroSequence] Click after line finished. Moving to next line.");
                    break;
                }
            }

            yield return null;
        }

        // 현재 클릭이 끝나기를 한 번 기다렸다가 다음 줄로 진행
        while (IsAdvanceHeld())
        {
            yield return null;
        }
    }

    // 013 이후: 페이드 아웃 → PlayerRoom 전환 → 좌표 이동 → 페이드 인 → 플레이어 흔들기
    // 이 전체 연출 동안 말풍선 비활성화, 연출 끝난 뒤 다시 켜고 다음 대사 시작
    private IEnumerator HandleMidTransition()
    {
        Debug.Log("[PresidentIntroSequence] HandleMidTransition start");

        // 말풍선 숨기기
        SetBalloonVisible(false);

        // 1) 화면을 완전히 어둡게
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

        // 4) 앵커 카메라/위치 재연결
        if (anchor != null)
        {
            Debug.Log("[PresidentIntroSequence] Rebind WorldBubbleAnchor in PlayerRoom");

            anchor.SetWorldCamera(Camera.main, snap: false);
            anchor.SetSpeakerId("President", snapNow: false);

            yield return new WaitForEndOfFrame();
            anchor.SnapNow();

            Debug.Log("[PresidentIntroSequence] Anchor snapped after room switch");
        }

        // 5) 다시 밝게
        yield return StartCoroutine(FadeTo(0f));

        // 6) 플레이어 흔들리는 연출 (이때도 말풍선은 숨김 상태 유지)
        yield return StartCoroutine(ShakePlayer());

        // 7) 전체 연출이 끝난 뒤 다음 대사를 위해 말풍선 다시 켬
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
            Vector3 pos = Vector3.Lerp(startPos, endPos, s);
            presidentTransform.position = pos;

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

    private IEnumerator FadeOutAndLoadScene()
    {
        if (sceneLoading)
            yield break;

        sceneLoading = true;

        // 마지막 연출에서도 말풍선 숨김
        SetBalloonVisible(false);

        yield return StartCoroutine(FadeTo(1f, fadeDuration));

        Debug.Log("[PresidentIntroSequence] SceneManager.LoadScene: " + nextSceneName);
        SceneManager.LoadScene(nextSceneName);
    }

    // 실제 페이드만 담당. 말풍선 on/off는 바깥에서 처리.
    private IEnumerator FadeTo(float targetAlpha, float durationOverride = -1f)
    {
        if (fadeCanvasGroup == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] fadeCanvasGroup is null. Skipping fade. targetAlpha=" + targetAlpha);
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
            float a = Mathf.Lerp(startAlpha, targetAlpha, t);
            fadeCanvasGroup.alpha = a;
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
            Debug.Log("[PresidentIntroSequence] SetBalloonVisible(" + visible + ") on " + balloonRoot.name);
            balloonRoot.SetActive(visible);
        }
        else
        {
            Debug.LogWarning("[PresidentIntroSequence] balloonRoot is null. Cannot SetBalloonVisible.");
        }
    }

    private void CreateAutoFadeOverlay()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] No parent Canvas found. Cannot auto-create fade overlay.");
            return;
        }

        RectTransform canvasRect = parentCanvas.transform as RectTransform;
        if (canvasRect == null)
        {
            Debug.LogWarning("[PresidentIntroSequence] Parent Canvas has no RectTransform. Cannot auto-create fade overlay.");
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

        Debug.Log("[PresidentIntroSequence] Auto-created fade overlay under Canvas '" + parentCanvas.name + "'.");
    }
}
