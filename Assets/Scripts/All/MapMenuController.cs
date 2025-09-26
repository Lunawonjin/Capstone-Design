using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// MapMenuController
/// - M으로 맵 열기(커지며 등장), Esc로 닫기(작아지며 퇴장)
/// - 3개 메뉴 아이템 호버/클릭
/// - 클릭 시: 같은 씬이면 맵만 닫기, 다르면 씬 이동
/// </summary>
public class MapMenuController : MonoBehaviour
{
    [Header("Menu Items (3 UI Images)")]
    [SerializeField] private GameObject[] menuItems = new GameObject[3];

    [Header("Target Scene Names (Size must match menuItems)")]
    [SerializeField]
    private string[] sceneNames = new string[3]
    {
        "Player's Room",
        "Starest",
        "Shopping Center"
    };

    [Header("Hover Visual Settings")]
    [SerializeField, Range(1.0f, 2.0f)] private float hoverScale = 1.08f;
    [SerializeField] private Color hoverColor = new Color(1.05f, 1.05f, 1.05f, 1f);
    [SerializeField] private float normalScale = 1.0f;
    [SerializeField] private bool revertToOriginalColor = true;

    [Header("Map Root")]
    [Tooltip("활성/비활성화할 맵 루트 UI")]
    [SerializeField] private GameObject mapRoot;

    [Tooltip("맵을 닫을 때 다시 켜 줄 월드/게임 오브젝트(옵션)")]
    [SerializeField] private GameObject mapAessts;

    [Header("Keys")]
    [SerializeField] private KeyCode openKey = KeyCode.M;
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;
    [SerializeField] private KeyCode[] extraCloseKeys = { KeyCode.M, KeyCode.Escape };

    [Header("Open/Close Animation (Unscaled Time)")]
    [SerializeField] private Vector3 openStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 openEndScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float openDuration = 0.14f;

    [SerializeField] private Vector3 closeStartScale = Vector3.one;
    [SerializeField] private Vector3 closeEndScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField, Min(0.01f)] private float closeDuration = 0.12f;

    [Header("Alpha Fade (CanvasGroup)")]
    [SerializeField] private bool useAlphaFade = true;

    // 내부 참조
    private RectTransform _rt;
    private CanvasGroup _cg;
    private bool _isOpen;
    private bool _animating;

    private void Awake()
    {
        if (menuItems.Length != sceneNames.Length)
            Debug.LogWarning("menuItems와 sceneNames의 크기가 다릅니다.");

        // 맵 루트 준비
        if (mapRoot == null)
        {
            Debug.LogError("[MapMenuController] mapRoot가 비었습니다.");
            enabled = false;
            return;
        }
        _rt = mapRoot.GetComponent<RectTransform>();
        _cg = mapRoot.GetComponent<CanvasGroup>();

        // 초기 상태: 비활성 권장
        if (_rt) _rt.localScale = openEndScale;
        if (_cg && useAlphaFade)
        {
            _cg.alpha = 0f;
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
        }
        mapRoot.SetActive(false);
        _isOpen = false;
        _animating = false;

        // 메뉴 아이템 세팅
        int count = Mathf.Min(menuItems.Length, sceneNames.Length);
        for (int i = 0; i < count; i++)
        {
            var go = menuItems[i];
            if (go == null) continue;

            var hover = go.GetComponent<HoverableMenuItem>();
            if (hover == null) hover = go.AddComponent<HoverableMenuItem>();
            hover.SetVisualParams(normalScale, hoverScale, hoverColor, revertToOriginalColor);

            string targetScene = sceneNames[i];
            hover.onClick = () =>
            {
                string currentScene = SceneManager.GetActiveScene().name;
                if (currentScene == targetScene)
                {
                    // 같은 씬이면 맵만 부드럽게 닫기
                    CloseMap();
                    if (mapAessts) mapAessts.SetActive(true);
                }
                else
                {
                    // 다른 씬이면 이동
                    SceneManager.LoadScene(targetScene);
                }
            };
        }
    }

    void Update()
    {
        if (WasPressed(openKey)) OpenMap();
        if (_isOpen && WasPressed(closeKey) || WasPressed(extraCloseKeys)) CloseMap();
    }
    bool WasPressed(params KeyCode[] keys)
    {
        foreach (var k in keys)
            if (Input.GetKeyDown(k)) return true;
        return false;
    }
    // ===== 공개 API =====
    public void OpenMap()
    {
        if (_animating || _isOpen) return;

        _isOpen = true;
        _animating = true;

        mapRoot.SetActive(true);

        if (_rt) _rt.localScale = openStartScale;
        if (_cg && useAlphaFade)
        {
            _cg.alpha = 0f;
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
        }

        StartCoroutine(Co_Open());
    }

    public void CloseMap()
    {
        if (_animating || !_isOpen) return;

        _isOpen = false;
        _animating = true;

        if (_rt) _rt.localScale = closeStartScale;
        if (_cg && useAlphaFade)
        {
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
        }

        StartCoroutine(Co_Close());
    }

    // ===== 연출 코루틴 =====
    private System.Collections.IEnumerator Co_Open()
    {
        float t = 0f, d = Mathf.Max(0.01f, openDuration);
        while (t < d)
        {
            float u = t / d;
            float e = 1f - Mathf.Pow(1f - u, 3f); // EaseOutCubic

            if (_rt) _rt.localScale = Vector3.LerpUnclamped(openStartScale, openEndScale, e);
            if (_cg && useAlphaFade) _cg.alpha = Mathf.LerpUnclamped(0f, 1f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_rt) _rt.localScale = openEndScale;
        if (_cg && useAlphaFade)
        {
            _cg.alpha = 1f;
            _cg.interactable = true;
            _cg.blocksRaycasts = true;
        }

        _animating = false;
    }

    private System.Collections.IEnumerator Co_Close()
    {
        float t = 0f, d = Mathf.Max(0.01f, closeDuration);
        float startAlpha = (_cg && useAlphaFade) ? _cg.alpha : 1f;

        while (t < d)
        {
            float u = t / d;
            float e = Mathf.Pow(u, 3f); // EaseInCubic

            if (_rt) _rt.localScale = Vector3.LerpUnclamped(closeStartScale, closeEndScale, e);
            if (_cg && useAlphaFade) _cg.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_rt) _rt.localScale = closeEndScale;
        if (_cg && useAlphaFade) _cg.alpha = 0f;

        mapRoot.SetActive(false);
        _animating = false;
    }
}

// ===== 그대로 사용 =====
[RequireComponent(typeof(RectTransform))]
public class HoverableMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public System.Action onClick;

    private float _normalScale = 1f;
    private float _hoverScale = 1.08f;
    private Color _hoverColor = Color.white;
    private bool _revertToOriginalColor = true;

    private RectTransform _rect;
    private Graphic _graphic;
    private Color _originalColor;

    private void Reset()
    {
        _normalScale = 1f;
        _hoverScale = 1.08f;
        _hoverColor = new Color(1.05f, 1.05f, 1.05f, 1f);
        _revertToOriginalColor = true;
    }

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _graphic = GetComponent<Graphic>();

        if (_graphic != null)
            _originalColor = _graphic.color;
        else
            Debug.LogWarning($"[HoverableMenuItem] '{name}' 에 Graphic(Image/Text 등)이 없어 컬러 틴트를 적용할 수 없습니다.", this);

        SetScale(_normalScale);
    }

    public void SetVisualParams(float normalScale, float hoverScale, Color hoverColor, bool revertToOriginalColor)
    {
        _normalScale = Mathf.Max(0.0001f, normalScale);
        _hoverScale = Mathf.Max(_normalScale, hoverScale);
        _hoverColor = hoverColor;
        _revertToOriginalColor = revertToOriginalColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetScale(_hoverScale);

        if (_graphic != null)
        {
            _originalColor = _graphic.color;
            _graphic.color = MultiplyColor(_originalColor, _hoverColor);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetScale(_normalScale);

        if (_graphic != null && _revertToOriginalColor)
            _graphic.color = _originalColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
        onClick?.Invoke();
    }

    private void SetScale(float target)
    {
        if (_rect != null) _rect.localScale = new Vector3(target, target, 1f);
        else transform.localScale = new Vector3(target, target, 1f);
    }

    private Color MultiplyColor(Color baseColor, Color mul)
    {
        return new Color(baseColor.r * mul.r, baseColor.g * mul.g, baseColor.b * mul.b, baseColor.a * mul.a);
    }
}
