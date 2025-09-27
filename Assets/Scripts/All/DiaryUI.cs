// DiaryUI.cs
// Unity 6 (LTS)
// 도감 패널 열기/닫기
// - Tab: DiaryPanel, DiaryAssets 둘 다 활성화
// - 연출(스케일/알파)은 DiaryAssets에만 적용
// - Esc: 닫기(외부 차단 플래그 지원)
// - UnscaledTime 기반 트윈

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class DiaryUI : MonoBehaviour
{
    [Header("필수 참조")]
    [Tooltip("배경/블러/딤 등 루트 패널(토글 시 활성/비활성)")]
    [SerializeField] private GameObject diaryPanel;
    [Tooltip("실제 콘텐츠(애니메이션 대상). 알파/스케일 연출은 여기만 적용")]
    [SerializeField] private GameObject diaryAssets;
    [SerializeField] private Button bookIconButton;

    [Header("키 설정")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;

    [Header("초기 상태")]
    [SerializeField] private bool forceHideOnStart = true;

    [Header("열림 연출 (UnscaledTime, Assets 전용)")]
    [SerializeField] private Vector3 openStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 openEndScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float openDuration = 0.18f;

    [Header("닫힘 연출 (UnscaledTime, Assets 전용)")]
    [SerializeField] private Vector3 closeStartScale = Vector3.one;
    [SerializeField] private Vector3 closeEndScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField, Min(0.01f)] private float closeDuration = 0.14f;

    [Header("알파 페이드 (Assets에 CanvasGroup 필요)")]
    [SerializeField] private bool useAlphaFade = true;
    [SerializeField, Range(0f, 1f)] private float openStartAlpha = 0f;
    [SerializeField, Range(0f, 1f)] private float closeEndAlpha = 0f;

    [Header("이벤트")]
    public UnityEvent onOpened;
    public UnityEvent onClosed;

    // 외부에서 Esc 닫기 차단(모달 중 등)
    [HideInInspector] public bool externalEscBlocked = false;

    // 내부 상태
    private CanvasGroup _cgAssets;
    private RectTransform _rtAssets;
    private bool _isOpen;
    private bool _isAnimating;

    void Awake()
    {
        if (diaryPanel == null)
        {
            Debug.LogError("[DiaryUI] diaryPanel is null.");
            enabled = false;
            return;
        }
        if (diaryAssets == null)
        {
            Debug.LogError("[DiaryUI] diaryAssets is null.");
            enabled = false;
            return;
        }

        _cgAssets = diaryAssets.GetComponent<CanvasGroup>();
        _rtAssets = diaryAssets.GetComponent<RectTransform>();

        if (bookIconButton != null) bookIconButton.onClick.AddListener(Toggle);
    }

    void Start()
    {
        if (forceHideOnStart) HideImmediate();
        else
        {
            // 시작 상태 동기화
            _isOpen = diaryPanel.activeSelf && diaryAssets.activeSelf;
        }
    }

    void OnDestroy()
    {
        if (bookIconButton != null) bookIconButton.onClick.RemoveListener(Toggle);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && !_isAnimating)
        {
            if (WasPressed(Keyboard.current, toggleKey)) Toggle();

            // 외부 차단 중이면 Esc로 닫기 금지
            if (_isOpen && !externalEscBlocked && WasPressed(Keyboard.current, closeKey))
                Close();
        }
#else
        if (!_isAnimating)
        {
            if (Input.GetKeyDown(toggleKey)) Toggle();
            if (_isOpen && !externalEscBlocked && Input.GetKeyDown(closeKey)) Close();
        }
#endif
    }

    public void Toggle()
    {
        if (_isAnimating) return;
        if (_isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (_isOpen || _isAnimating) return;
        _isOpen = true;
        _isAnimating = true;

        // 둘 다 활성화
        diaryPanel.SetActive(true);
        diaryAssets.SetActive(true);

        // 연출은 Assets에만 적용
        if (_rtAssets != null) _rtAssets.localScale = openStartScale;
        if (_cgAssets != null)
        {
            if (useAlphaFade) _cgAssets.alpha = openStartAlpha;
            _cgAssets.interactable = false;
            _cgAssets.blocksRaycasts = false;
        }

        StartCoroutine(Co_Open());
    }

    public void Close()
    {
        if (!_isOpen || _isAnimating) return;
        _isOpen = false;
        _isAnimating = true;

        // 닫힘 시작 상태는 Assets 기준
        if (_rtAssets != null) _rtAssets.localScale = closeStartScale;
        if (_cgAssets != null)
        {
            _cgAssets.interactable = false;
            _cgAssets.blocksRaycasts = false;
        }

        StartCoroutine(Co_Close());
    }

    private void HideImmediate()
    {
        _isOpen = false;
        _isAnimating = false;

        // 연출 상태 초기화(Assets 전용)
        if (_cgAssets != null)
        {
            _cgAssets.alpha = 0f;
            _cgAssets.interactable = false;
            _cgAssets.blocksRaycasts = false;
        }
        if (_rtAssets != null) _rtAssets.localScale = openEndScale;

        // 둘 다 비활성화
        diaryAssets.SetActive(false);
        diaryPanel.SetActive(false);
    }

    private System.Collections.IEnumerator Co_Open()
    {
        float t = 0f, d = Mathf.Max(0.01f, openDuration);
        while (t < d)
        {
            float u = t / d;
            float e = 1f - Mathf.Pow(1f - u, 3f); // EaseOutCubic

            if (_rtAssets != null)
                _rtAssets.localScale = Vector3.LerpUnclamped(openStartScale, openEndScale, e);

            if (_cgAssets != null && useAlphaFade)
                _cgAssets.alpha = Mathf.LerpUnclamped(openStartAlpha, 1f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_rtAssets != null) _rtAssets.localScale = openEndScale;
        if (_cgAssets != null)
        {
            if (useAlphaFade) _cgAssets.alpha = 1f;
            _cgAssets.interactable = true;
            _cgAssets.blocksRaycasts = true;
        }

        _isAnimating = false;
        onOpened?.Invoke();
    }

    private System.Collections.IEnumerator Co_Close()
    {
        float t = 0f, d = Mathf.Max(0.01f, closeDuration);
        float startAlpha = (_cgAssets != null) ? _cgAssets.alpha : 1f;

        while (t < d)
        {
            float u = t / d;
            float e = Mathf.Pow(u, 3f); // EaseInCubic

            if (_rtAssets != null)
                _rtAssets.localScale = Vector3.LerpUnclamped(closeStartScale, closeEndScale, e);

            if (_cgAssets != null && useAlphaFade)
                _cgAssets.alpha = Mathf.LerpUnclamped(startAlpha, closeEndAlpha, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_rtAssets != null) _rtAssets.localScale = closeEndScale;
        if (_cgAssets != null)
        {
            if (useAlphaFade) _cgAssets.alpha = closeEndAlpha;
            _cgAssets.interactable = false;
            _cgAssets.blocksRaycasts = false;
        }

        // 둘 다 비활성화
        diaryAssets.SetActive(false);
        diaryPanel.SetActive(false);

        _isAnimating = false;
        onClosed?.Invoke();
    }

#if ENABLE_INPUT_SYSTEM
    private bool WasPressed(Keyboard kb, KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Tab:    return kb.tabKey.wasPressedThisFrame;
            case KeyCode.Escape: return kb.escapeKey.wasPressedThisFrame;
            case KeyCode.Return: return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            case KeyCode.Space:  return kb.spaceKey.wasPressedThisFrame;
            default:             return Keyboard.current.anyKey.wasPressedThisFrame && Input.GetKeyDown(key);
        }
    }
#endif
}
