using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Exit 경고 모달:
/// - Exit 버튼을 누르면 Warring 패널이 "커지며 등장"
/// - Yes 클릭 시 게임 종료
/// - No 또는 Esc 시 "작아지며 퇴장"
/// - 애니메이션은 UnscaledTime 사용(일시정지 중에도 자연스럽게 동작)
/// - TogglePanelWithPause의 autoSync를 활용해 시간/오디오/입력 잠금 자동 동기화
/// </summary>
[DisallowMultipleComponent]
public class ExitWarningModal : MonoBehaviour
{
    [Header("Warring 패널 루트 (활성/비활성 대상)")]
    [SerializeField] private GameObject warringPanel;

    [Header("패널의 RectTransform / CanvasGroup (연출용)")]
    [SerializeField] private RectTransform panelRect;      // warringPanel의 RectTransform
    [SerializeField] private CanvasGroup panelCanvasGroup; // 있으면 알파 연출

    [Header("버튼 연결")]
    [SerializeField] private Button exitButton; // 메인 메뉴의 Exit 버튼(옵션: 인스펙터 연결 시 자동 Open)
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;

    [Header("열림 연출 (UnscaledTime)")]
    [SerializeField] private Vector3 openStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 openEndScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float openDuration = 0.14f;

    [Header("닫힘 연출 (UnscaledTime)")]
    [SerializeField] private Vector3 closeStartScale = Vector3.one;
    [SerializeField] private Vector3 closeEndScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField, Min(0.01f)] private float closeDuration = 0.12f;

    private bool _isOpen;
    private bool _animating;

    void Reset()
    {
        openStartScale = new Vector3(0.9f, 0.9f, 1f);
        openEndScale = Vector3.one;
        openDuration = 0.14f;

        closeStartScale = Vector3.one;
        closeEndScale = new Vector3(0.9f, 0.9f, 1f);
        closeDuration = 0.12f;
    }

    void Awake()
    {
        if (warringPanel != null)
        {
            // 안전 초기화: 비활성 시작 권장
            if (panelRect == null) panelRect = warringPanel.GetComponent<RectTransform>();
            if (panelCanvasGroup == null) panelCanvasGroup = warringPanel.GetComponent<CanvasGroup>();

            if (panelRect != null) panelRect.localScale = openEndScale;
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
            }
            warringPanel.SetActive(false);
        }

        if (exitButton != null) exitButton.onClick.AddListener(Open);
        if (yesButton != null) yesButton.onClick.AddListener(OnYes);
        if (noButton != null) noButton.onClick.AddListener(Close);
    }

    void OnDestroy()
    {
        if (exitButton != null) exitButton.onClick.RemoveListener(Open);
        if (yesButton != null) yesButton.onClick.RemoveListener(OnYes);
        if (noButton != null) noButton.onClick.RemoveListener(Close);
    }

    void Update()
    {
        if (!_isOpen || _animating) return;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Close();
#else
        if (Input.GetKeyDown(KeyCode.Escape))
            Close();
#endif
    }

    // ===== 공개 API =====
    public void Open()
    {
        if (warringPanel == null || _animating || _isOpen) return;

        _isOpen = true;
        _animating = true;

        warringPanel.SetActive(true);

        if (panelRect != null) panelRect.localScale = openStartScale;
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

        StartCoroutine(Co_Open());
    }

    public void Close()
    {
        if (warringPanel == null || _animating || !_isOpen) return;

        _isOpen = false;
        _animating = true;

        if (panelRect != null) panelRect.localScale = closeStartScale;
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

        StartCoroutine(Co_Close());
    }

    // ===== 버튼 핸들러 =====
    private void OnYes()
    {
        // 즉시 종료
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ===== 코루틴: 연출 =====
    private System.Collections.IEnumerator Co_Open()
    {
        float t = 0f, d = Mathf.Max(0.01f, openDuration);
        while (t < d)
        {
            float u = t / d;
            float e = 1f - Mathf.Pow(1f - u, 3f); // EaseOutCubic

            if (panelRect != null)
                panelRect.localScale = Vector3.LerpUnclamped(openStartScale, openEndScale, e);
            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = Mathf.LerpUnclamped(0f, 1f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (panelRect != null) panelRect.localScale = openEndScale;
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }

        _animating = false;
    }

    private System.Collections.IEnumerator Co_Close()
    {
        float t = 0f, d = Mathf.Max(0.01f, closeDuration);
        float startAlpha = (panelCanvasGroup != null) ? panelCanvasGroup.alpha : 1f;

        while (t < d)
        {
            float u = t / d;
            float e = Mathf.Pow(u, 3f); // EaseInCubic

            if (panelRect != null)
                panelRect.localScale = Vector3.LerpUnclamped(closeStartScale, closeEndScale, e);
            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (panelRect != null) panelRect.localScale = closeEndScale;
        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;

        warringPanel.SetActive(false);
        _animating = false;
    }
}
