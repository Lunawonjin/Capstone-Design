using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Exit & Main 경고 모달 (확장형)
/// - Exit: 게임 종료 확인
/// - Main: 메인 화면(스타트 메뉴)로 이동 확인
/// - 두 모달 모두 UnscaledTime 애니메이션, 동일 파라미터 사용
/// - 겹쳐 열리지 않도록 가드
/// </summary>
[DisallowMultipleComponent]
public class ExitWarningModal : MonoBehaviour
{
    // ===== 공통 애니메이션 파라미터 =====
    [Header("열림 연출 (UnscaledTime)")]
    [SerializeField] private Vector3 openStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 openEndScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float openDuration = 0.14f;

    [Header("닫힘 연출 (UnscaledTime)")]
    [SerializeField] private Vector3 closeStartScale = Vector3.one;
    [SerializeField] private Vector3 closeEndScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField, Min(0.01f)] private float closeDuration = 0.12f;

    // ===== Exit 모달 세트 =====
    [Header("WarringExit 세트")]
    [SerializeField] private GameObject warringExitPanel;
    [SerializeField] private RectTransform exitRect;
    [SerializeField] private CanvasGroup exitCanvasGroup;
    [SerializeField] private Button exitOpenButton; // 메인 메뉴의 Exit 버튼
    [SerializeField] private Button yesExitButton;
    [SerializeField] private Button noExitButton;

    // ===== Main(메인 화면 이동) 모달 세트 =====
    [Header("WarringMain 세트")]
    [SerializeField] private GameObject warringMainPanel;
    [SerializeField] private RectTransform mainRect;
    [SerializeField] private CanvasGroup mainCanvasGroup;
    [SerializeField] private Button mainOpenButton; // 메인으로 버튼
    [SerializeField] private Button yesMainButton;
    [SerializeField] private Button noMainButton;
    [SerializeField] private string startMenuSceneName = "StartMenu";

    // 상태
    private bool _exitOpen;
    private bool _mainOpen;
    private bool _animating;

    void Reset()
    {
        openStartScale = new Vector3(0.9f, 0.9f, 1f);
        openEndScale = Vector3.one;
        openDuration = 0.14f;

        closeStartScale = Vector3.one;
        closeEndScale = new Vector3(0.9f, 0.9f, 1f);
        closeDuration = 0.12f;

        startMenuSceneName = "StartMenu";
    }

    void Awake()
    {
        // 안전 초기화(Exit)
        if (warringExitPanel != null)
        {
            if (!exitRect) exitRect = warringExitPanel.GetComponent<RectTransform>();
            if (!exitCanvasGroup) exitCanvasGroup = warringExitPanel.GetComponent<CanvasGroup>();

            if (exitRect) exitRect.localScale = openEndScale;
            if (exitCanvasGroup)
            {
                exitCanvasGroup.alpha = 0f;
                exitCanvasGroup.interactable = false;
                exitCanvasGroup.blocksRaycasts = false;
            }
            warringExitPanel.SetActive(false);
        }

        // 안전 초기화(Main)
        if (warringMainPanel != null)
        {
            if (!mainRect) mainRect = warringMainPanel.GetComponent<RectTransform>();
            if (!mainCanvasGroup) mainCanvasGroup = warringMainPanel.GetComponent<CanvasGroup>();

            if (mainRect) mainRect.localScale = openEndScale;
            if (mainCanvasGroup)
            {
                mainCanvasGroup.alpha = 0f;
                mainCanvasGroup.interactable = false;
                mainCanvasGroup.blocksRaycasts = false;
            }
            warringMainPanel.SetActive(false);
        }

        // 버튼 연결
        if (exitOpenButton) exitOpenButton.onClick.AddListener(OpenExit);
        if (yesExitButton) yesExitButton.onClick.AddListener(OnYesExit);
        if (noExitButton) noExitButton.onClick.AddListener(CloseExit);

        if (mainOpenButton) mainOpenButton.onClick.AddListener(OpenMain);
        if (yesMainButton) yesMainButton.onClick.AddListener(OnYesMain);
        if (noMainButton) noMainButton.onClick.AddListener(CloseMain);
    }

    void OnDestroy()
    {
        if (exitOpenButton) exitOpenButton.onClick.RemoveListener(OpenExit);
        if (yesExitButton) yesExitButton.onClick.RemoveListener(OnYesExit);
        if (noExitButton) noExitButton.onClick.RemoveListener(CloseExit);

        if (mainOpenButton) mainOpenButton.onClick.RemoveListener(OpenMain);
        if (yesMainButton) yesMainButton.onClick.RemoveListener(OnYesMain);
        if (noMainButton) noMainButton.onClick.RemoveListener(CloseMain);
    }

    void Update()
    {
        if (_animating) return;

#if ENABLE_INPUT_SYSTEM
        bool esc = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        bool esc = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (!esc) return;

        // 열린 쪽을 닫기(Exit 우선)
        if (_exitOpen) CloseExit();
        else if (_mainOpen) CloseMain();
    }

    // ===== Exit 공개 API =====
    public void OpenExit()
    {
        if (_animating || _exitOpen) return;
        if (_mainOpen) return; // 다른 모달이 열려 있으면 막기

        if (!warringExitPanel) return;

        _exitOpen = true;
        _animating = true;

        warringExitPanel.SetActive(true);
        if (exitRect) exitRect.localScale = openStartScale;
        if (exitCanvasGroup)
        {
            exitCanvasGroup.alpha = 0f;
            exitCanvasGroup.interactable = false;
            exitCanvasGroup.blocksRaycasts = false;
        }

        StartCoroutine(Co_Open(exitRect, exitCanvasGroup, () =>
        {
            _animating = false;
        }));
    }

    public void CloseExit()
    {
        if (_animating || !_exitOpen) return;
        if (!warringExitPanel) return;

        _exitOpen = false;
        _animating = true;

        if (exitRect) exitRect.localScale = closeStartScale;
        if (exitCanvasGroup)
        {
            exitCanvasGroup.interactable = false;
            exitCanvasGroup.blocksRaycasts = false;
        }

        StartCoroutine(Co_Close(exitRect, exitCanvasGroup, () =>
        {
            if (warringExitPanel) warringExitPanel.SetActive(false);
            _animating = false;
        }));
    }

    private void OnYesExit()
    {
        // 즉시 종료
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ===== Main 공개 API =====
    public void OpenMain()
    {
        if (_animating || _mainOpen) return;
        if (_exitOpen) return; // 다른 모달이 열려 있으면 막기

        if (!warringMainPanel) return;

        _mainOpen = true;
        _animating = true;

        warringMainPanel.SetActive(true);
        if (mainRect) mainRect.localScale = openStartScale;
        if (mainCanvasGroup)
        {
            mainCanvasGroup.alpha = 0f;
            mainCanvasGroup.interactable = false;
            mainCanvasGroup.blocksRaycasts = false;
        }

        StartCoroutine(Co_Open(mainRect, mainCanvasGroup, () =>
        {
            _animating = false;
        }));
    }

    public void CloseMain()
    {
        if (_animating || !_mainOpen) return;
        if (!warringMainPanel) return;

        _mainOpen = false;
        _animating = true;

        if (mainRect) mainRect.localScale = closeStartScale;
        if (mainCanvasGroup)
        {
            mainCanvasGroup.interactable = false;
            mainCanvasGroup.blocksRaycasts = false;
        }

        StartCoroutine(Co_Close(mainRect, mainCanvasGroup, () =>
        {
            if (warringMainPanel) warringMainPanel.SetActive(false);
            _animating = false;
        }));
    }

    private void OnYesMain()
    {
        // 메인 화면(스타트 메뉴)로 이동
        if (!string.IsNullOrEmpty(startMenuSceneName))
        {
            // 필요하다면 여기에서 Time.timeScale 등을 원복할 수 있음
            SceneManager.LoadScene(startMenuSceneName);
        }
        else
        {
            Debug.LogError("[ExitWarningModal] startMenuSceneName 이 비어 있습니다.");
        }
    }

    // ===== 공통 코루틴 =====
    private System.Collections.IEnumerator Co_Open(RectTransform rect, CanvasGroup cg, System.Action onDone)
    {
        float t = 0f, d = Mathf.Max(0.01f, openDuration);
        while (t < d)
        {
            float u = t / d;
            float e = 1f - Mathf.Pow(1f - u, 3f); // EaseOutCubic

            if (rect) rect.localScale = Vector3.LerpUnclamped(openStartScale, openEndScale, e);
            if (cg) cg.alpha = Mathf.LerpUnclamped(0f, 1f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (rect) rect.localScale = openEndScale;
        if (cg)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        onDone?.Invoke();
    }

    private System.Collections.IEnumerator Co_Close(RectTransform rect, CanvasGroup cg, System.Action onDone)
    {
        float t = 0f, d = Mathf.Max(0.01f, closeDuration);
        float startAlpha = (cg != null) ? cg.alpha : 1f;

        while (t < d)
        {
            float u = t / d;
            float e = Mathf.Pow(u, 3f); // EaseInCubic

            if (rect) rect.localScale = Vector3.LerpUnclamped(closeStartScale, closeEndScale, e);
            if (cg) cg.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (rect) rect.localScale = closeEndScale;
        if (cg) cg.alpha = 0f;

        onDone?.Invoke();
    }
}
