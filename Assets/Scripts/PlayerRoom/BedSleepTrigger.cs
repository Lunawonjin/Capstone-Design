using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class BedSleepTrigger : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("체크시: 1일차에만 수면 UI/코인 연출이 나오고, 2일차부터는 바로 넘어감.\n해제시: 매일 수면 UI 연출이 나옴")]
    public bool isFirstDayOnly = true;

    [Tooltip("체크시: 게임 시작(씬 로드) 직후 검은 화면에서 서서히 밝아짐 (Player's Room에 체크 추천)")]
    public bool startWithFadeIn = false;

    [Header("패널 / 버튼")]
    public GameObject goodNightPanel;   // 전체 패널 루트
    public GameObject goodNightQA;      // 질문/버튼 컨테이너
    public Button sleepButton;          // 자러간다 버튼
    public Button notYetButton;         // 아직 버튼

    [Header("CantGoodNight 텍스트")]
    public TMP_Text cantGoodNightText;

    [Header("플레이어(비우면 자동 탐색)")]
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;

    [Header("진입시 바로 다시 못열게 잠금")]
    public bool lockIfPlayerInsideOnStart = true;

    [Header("페이드 설정")]
    [Tooltip("페이드 화면을 생성할 캔버스를 지정하세요. 비워두면 자동으로 찾습니다.")]
    [SerializeField] private Canvas targetFadeCanvas;
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeDuration = 0.6f;

    [Header("수면 요약 UI (연출용)")]
    [SerializeField] private GameObject sleepUIRoot;

    [Space(10)]
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private RectTransform dayUIRect;

    [Space(10)]
    [SerializeField] private TMP_Text coinText;
    [SerializeField] private RectTransform coinUIRect;

    [Space(10)]
    [SerializeField] private Button sleepNextButton;
    [SerializeField] private float sleepSlideDuration = 0.5f;
    [SerializeField] private float sleepSlideStartX = -500f;
    [SerializeField] private float sleepSlideTargetX = 0f;

    [Header("씬 설정")]
    [SerializeField] private string prologSceneName = "Prolog";
    [SerializeField] private string playerRoomSceneName = "Player's Room"; // [복구] 이동할 씬 이름

    [Header("디버그")]
    public bool verboseLog = false;

    // 내부 상태
    private bool _cantSleepActive = false;
    private bool _sleepingRoutine = false;
    private bool _requireExitToReopen = false;
    private bool _sceneLoading = false;
    private bool _sleepNextClicked = false;
    private const string PlayerTag = "Player";

    private void OnValidate()
    {
        var col = GetComponent<Collider2D>();
        if (col && !col.isTrigger) col.isTrigger = true;
    }

    private void Awake()
    {
        if (fadeCanvasGroup == null) CreateAutoFadeOverlay();

        // [수정] startWithFadeIn 옵션이 켜져있으면 처음부터 검은 화면(Alpha 1)으로 시작
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = startWithFadeIn ? 1f : 0f;
            fadeCanvasGroup.blocksRaycasts = startWithFadeIn;
            fadeCanvasGroup.gameObject.SetActive(true);
        }

        // UI 초기화
        if (sleepUIRoot) sleepUIRoot.SetActive(false);
        if (dayUIRect) dayUIRect.gameObject.SetActive(false);
        if (coinUIRect) coinUIRect.gameObject.SetActive(false);

        if (goodNightPanel) goodNightPanel.SetActive(false);
        if (goodNightQA) goodNightQA.SetActive(false);
        if (cantGoodNightText)
        {
            cantGoodNightText.text = "";
            cantGoodNightText.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        if (autoFindPlayerMove && !playerMove)
            playerMove = FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include);

        if (sleepButton)
        {
            sleepButton.onClick.RemoveAllListeners();
            sleepButton.onClick.AddListener(OnClickSleep);
        }
        if (notYetButton)
        {
            notYetButton.onClick.RemoveAllListeners();
            notYetButton.onClick.AddListener(OnClickNotYet);
        }
        if (sleepNextButton)
        {
            sleepNextButton.onClick.RemoveAllListeners();
            sleepNextButton.onClick.AddListener(OnClickSleepNextButton);
        }

        if (lockIfPlayerInsideOnStart)
            StartCoroutine(CoLockIfPlayerAlreadyInsideOnStart());

        // [추가] 시작 시 페이드 인 효과 (검은 화면 -> 투명)
        if (startWithFadeIn && fadeCanvasGroup != null)
        {
            StartCoroutine(FadeTo(0f, fadeDuration));
        }
    }

    private void Update()
    {
        if (playerMove)
        {
            bool isPanelOpen = (goodNightPanel && goodNightPanel.activeInHierarchy);
            bool isSleepUIOpen = (sleepUIRoot && sleepUIRoot.activeInHierarchy);
            playerMove.controlEnabled = !(isPanelOpen || isSleepUIOpen || _sceneLoading);
        }

        if (_cantSleepActive && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
            CloseCantSleep();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        if (_requireExitToReopen || _sleepingRoutine || _sceneLoading) return;

        bool cantSleep = false;
        var dm = DataManager.instance;
        if (dm != null && dm.nowPlayer != null)
            cantSleep = (dm.nowPlayer.Day == 1 && dm.nowPlayer.CanFirstSleep == false);

        if (cantSleep && !IsPrologScene())
            ShowCantSleep();
        else
            OpenPanel();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(PlayerTag)) return;
        _requireExitToReopen = false;
    }

    private void OpenPanel()
    {
        if (!goodNightPanel) return;
        if (goodNightQA) goodNightQA.SetActive(true);
        if (cantGoodNightText) cantGoodNightText.gameObject.SetActive(false);
        goodNightPanel.SetActive(true);
    }

    private void ClosePanel()
    {
        if (goodNightPanel) goodNightPanel.SetActive(false);
    }

    private void ShowCantSleep()
    {
        if (IsPrologScene()) { OpenPanel(); return; }

        _cantSleepActive = true;
        if (goodNightQA) goodNightQA.SetActive(false);
        if (cantGoodNightText)
        {
            string msg = LocalizationSettings.StringDatabase.GetLocalizedString("UI_Table", "UI_CantGoodNight");
            cantGoodNightText.text = msg;
            cantGoodNightText.gameObject.SetActive(true);
        }
        if (goodNightPanel) goodNightPanel.SetActive(true);
    }

    private void CloseCantSleep()
    {
        _cantSleepActive = false;
        if (cantGoodNightText) cantGoodNightText.gameObject.SetActive(false);
        if (goodNightPanel) goodNightPanel.SetActive(false);
        _requireExitToReopen = true;
    }

    private void OnClickNotYet()
    {
        ClosePanel();
        _requireExitToReopen = true;
    }

    private void OnClickSleep()
    {
        if (_sleepingRoutine || _sceneLoading) return;

        // [중요] Prolog 씬일 경우 별도 처리 (씬 이동)
        if (IsPrologScene())
        {
            _sleepingRoutine = true;
            ClosePanel();
            StartCoroutine(CoPrologSequence());
            return;
        }

        _sleepingRoutine = true;
        _sceneLoading = true;

        ClosePanel();

        int currentDay = 1;
        if (DataManager.instance != null) currentDay = DataManager.instance.nowPlayer.Day;

        bool skipAnimation = (isFirstDayOnly && currentDay >= 2);

        if (skipAnimation)
        {
            StartCoroutine(CoQuickSleepSequence());
        }
        else
        {
            bool isDay1 = (currentDay == 1);
            StartCoroutine(CoFullSleepSequence(isDay1));
        }

        _requireExitToReopen = true;
        _sleepingRoutine = false;
    }

    // [추가] Prolog 전용 시퀀스: 페이드 아웃 -> 씬 이동
    private IEnumerator CoPrologSequence()
    {
        // 1. 페이드 아웃 (검게)
        yield return StartCoroutine(FadeTo(1f, fadeDuration));

        // 2. 필요하다면 여기서 데이터 저장 (DataManager.instance.SaveData() 등)
        // Prolog는 보통 저장 안 하거나, 씬 넘어가면서 저장함.

        // 3. 씬 이동
        SceneManager.LoadScene(playerRoomSceneName);
    }

    // 빠른 수면 (화면 깜빡임만)
    private IEnumerator CoQuickSleepSequence()
    {
        yield return StartCoroutine(FadeTo(1f, fadeDuration));

        ApplySleepAndSave();
        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(FadeTo(0f, fadeDuration));

        _sceneLoading = false;
    }

    // 풀 애니메이션 수면 (검은 배경 위에서 UI 연출)
    private IEnumerator CoFullSleepSequence(bool isDay1)
    {
        // 페이드 인 (화면 검게)
        yield return StartCoroutine(FadeTo(1f, fadeDuration));

        ApplySleepAndSave();

        if (sleepUIRoot == null)
        {
            yield return StartCoroutine(FadeTo(0f, fadeDuration));
            _sceneLoading = false;
            yield break;
        }

        sleepUIRoot.SetActive(true);
        sleepUIRoot.transform.SetAsLastSibling(); // UI를 맨 앞으로

        if (dayUIRect) dayUIRect.gameObject.SetActive(false);
        if (coinUIRect) coinUIRect.gameObject.SetActive(false);
        if (sleepNextButton) sleepNextButton.gameObject.SetActive(false);

        if (dayText != null && DataManager.instance != null)
        {
            var dm = DataManager.instance;
            dayText.text = dm.FormatDayAndWeekLocalized(dm.nowPlayer.Day, dm.GetWeekday(), dm.GetLanguageCode());
        }
        else if (dayText != null) dayText.text = "Day ?";

        int currentCoin = (DataManager.instance != null) ? DataManager.instance.nowPlayer.Coin : 0;
        if (coinText != null) coinText.text = currentCoin.ToString();

        // 슬라이드 애니메이션
        if (dayUIRect)
        {
            dayUIRect.gameObject.SetActive(true);
            yield return StartCoroutine(SlideRectX(dayUIRect, sleepSlideStartX, sleepSlideTargetX, sleepSlideDuration));
        }

        if (coinUIRect)
        {
            coinUIRect.gameObject.SetActive(true);
            yield return StartCoroutine(SlideRectX(coinUIRect, sleepSlideStartX, sleepSlideTargetX, sleepSlideDuration));
        }

        // 코인 보상 (1일차)
        if (isDay1 && DataManager.instance != null)
        {
            int startCoin = DataManager.instance.nowPlayer.Coin;
            int rewardAmount = 10;

            DataManager.instance.AddCoin(rewardAmount);
            DataManager.instance.SaveData();

            int endCoin = DataManager.instance.nowPlayer.Coin;
            float elapsed = 0f;
            while (elapsed < 0.8f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.8f);
                int visibleCoin = (int)Mathf.Lerp(startCoin, endCoin, t);
                if (coinText) coinText.text = visibleCoin.ToString();
                yield return null;
            }
            if (coinText) coinText.text = endCoin.ToString();
        }

        // Next 버튼
        _sleepNextClicked = false;
        if (sleepNextButton) sleepNextButton.gameObject.SetActive(true);

        yield return new WaitUntil(() => _sleepNextClicked);

        // 종료
        sleepUIRoot.SetActive(false);
        yield return StartCoroutine(FadeTo(0f, 0.6f)); // 화면 밝게

        _sceneLoading = false;
    }

    private void OnClickSleepNextButton()
    {
        if (_sleepNextClicked) return;
        _sleepNextClicked = true;
    }

    private void ApplySleepAndSave()
    {
        if (IsPrologScene()) return;

        var dm = DataManager.instance;
        if (dm == null) return;

        dm.AddDay(1);

        Vector3 pos = playerMove
            ? playerMove.transform.position
            : (GameObject.FindGameObjectWithTag("Player")?.transform.position ?? Vector3.zero);

        dm.SetPlayerPosition(pos);

        if (dm.nowSlot >= 0) dm.SaveData();
    }

    private bool IsPrologScene()
    {
        return SceneManager.GetActiveScene().name == prologSceneName;
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (fadeCanvasGroup == null) yield break;

        fadeCanvasGroup.gameObject.SetActive(true);
        if (targetAlpha > 0.01f) fadeCanvasGroup.blocksRaycasts = true;

        float startAlpha = fadeCanvasGroup.alpha;
        float time = 0f;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / Mathf.Max(0.0001f, duration));
            float eased = t * t * (3f - 2f * t);
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, eased);
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
        if (targetAlpha <= 0.01f) fadeCanvasGroup.blocksRaycasts = false;
    }

    private IEnumerator SlideRectX(RectTransform rect, float startX, float targetX, float duration)
    {
        if (rect == null) yield break;

        float time = 0f;
        Vector2 startPos = rect.anchoredPosition;
        startPos.x = startX;
        rect.anchoredPosition = startPos;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / Mathf.Max(0.0001f, duration));
            float eased = t * t * (3f - 2f * t);
            float newX = Mathf.Lerp(startX, targetX, eased);
            rect.anchoredPosition = new Vector2(newX, startPos.y);
            yield return null;
        }
        rect.anchoredPosition = new Vector2(targetX, startPos.y);
    }

    private IEnumerator CoLockIfPlayerAlreadyInsideOnStart()
    {
        yield return null;
        float timer = 0.4f;
        while (timer > 0f)
        {
            timer -= Time.unscaledDeltaTime;
            if (IsPlayerOverlappingMe(out _)) { _requireExitToReopen = true; yield break; }
            yield return null;
        }
    }

    private bool IsPlayerOverlappingMe(out Collider2D playerCollider)
    {
        playerCollider = null;
        var col = GetComponent<Collider2D>();
        if (!col) return false;

        var results = new List<Collider2D>(8);
        col.Overlap(new ContactFilter2D { useTriggers = true }, results);

        foreach (var c in results)
        {
            if (c && c.CompareTag(PlayerTag)) { playerCollider = c; return true; }
        }
        return false;
    }

    private void CreateAutoFadeOverlay()
    {
        Canvas canvasToUse = targetFadeCanvas;

        if (canvasToUse == null)
        {
            Canvas[] allCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in allCanvases) { if (c.renderMode == RenderMode.ScreenSpaceOverlay) { canvasToUse = c; break; } }
            if (canvasToUse == null && allCanvases.Length > 0) canvasToUse = allCanvases[0];
        }

        if (canvasToUse == null) return;

        GameObject fadeObj = new GameObject("AutoFadeOverlay");
        fadeObj.layer = canvasToUse.gameObject.layer;
        fadeObj.transform.SetParent(canvasToUse.transform, false);

        RectTransform rt = fadeObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;

        Image img = fadeObj.AddComponent<Image>();
        img.color = Color.black; img.raycastTarget = true;

        fadeCanvasGroup = fadeObj.AddComponent<CanvasGroup>();
        fadeCanvasGroup.alpha = 0f; fadeCanvasGroup.blocksRaycasts = false;

        fadeObj.transform.SetAsLastSibling();
    }
}