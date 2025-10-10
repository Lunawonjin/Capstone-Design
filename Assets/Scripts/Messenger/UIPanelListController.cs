using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Vertical ScrollView 컨트롤러
/// - Viewport: 700x500 (에디터에서 설정)
/// - Content는 패널 수에 따라 높이 자동 증가
/// - 패널 크기: 680x200
/// - P 키로 새 패널을 "맨 위"에 추가
/// </summary>
[DisallowMultipleComponent]
public class UIPanelListController : MonoBehaviour
{
    [Header("필수 참조")]
    [Tooltip("ScrollRect가 붙은 오브젝트")]
    public ScrollRect scrollRect;

    [Tooltip("패널들이 들어갈 Content RectTransform")]
    public RectTransform content;

    [Tooltip("680x200 패널 프리팹")]
    public RectTransform panelPrefab;

    [Header("레이아웃 옵션")]
    public float spacing = 8f;
    public float horizontalPadding = 10f;
    public float verticalPadding = 10f;

    [Header("패널 규격")]
    public float panelWidth = 680f;
    public float panelHeight = 200f;

    private VerticalLayoutGroup _vlg;
    private ContentSizeFitter _csf;

    void Reset()
    {
        scrollRect = GetComponentInChildren<ScrollRect>(true);
        if (scrollRect != null) content = scrollRect.content;
    }

    void Awake()
    {
        if (scrollRect == null || content == null || panelPrefab == null)
        {
            Debug.LogError("[UIPanelListController] 참조 누락: ScrollRect/Content/PanelPrefab 확인");
            return;
        }

        // ScrollRect 세팅
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Content 레이아웃 보장
        _vlg = content.GetComponent<VerticalLayoutGroup>();
        if (_vlg == null) _vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        _vlg.spacing = spacing;
        _vlg.padding = new RectOffset(
            Mathf.RoundToInt(horizontalPadding),
            Mathf.RoundToInt(horizontalPadding),
            Mathf.RoundToInt(verticalPadding),
            Mathf.RoundToInt(verticalPadding)
        );
        _vlg.childAlignment = TextAnchor.UpperCenter;
        _vlg.childControlWidth = true;
        _vlg.childControlHeight = false;   // 각 패널의 LayoutElement가 높이를 결정
        _vlg.childForceExpandWidth = true;
        _vlg.childForceExpandHeight = false;

        _csf = content.GetComponent<ContentSizeFitter>();
        if (_csf == null) _csf = content.gameObject.AddComponent<ContentSizeFitter>();
        _csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Content 상단 기준
        content.anchorMin = new Vector2(0.5f, 1f);
        content.anchorMax = new Vector2(0.5f, 1f);
        content.pivot = new Vector2(0.5f, 1f);

        // Viewport 마스크 보장
        var viewport = scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.transform.Find("Viewport") as RectTransform;
        if (viewport != null)
        {
            var img = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
            img.raycastTarget = true;
            var mask = viewport.GetComponent<Mask>() ?? viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
        }

        // 씬에 이미 있는 기존 자식 패널들도 높이 자동 보정
        FixupExistingChildrenHeights();

        // 초기 위치: 최상단
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 1f;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            AddPanelAtTop();
        }
    }

    /// <summary>
    /// 씬에 배치된 기존 자식 패널들의 LayoutElement를 강제 보정
    /// </summary>
    private void FixupExistingChildrenHeights()
    {
        for (int i = 0; i < content.childCount; i++)
        {
            var rt = content.GetChild(i) as RectTransform;
            if (rt == null) continue;
            ApplyPanelLayoutRules(rt);
        }
        ForceRebuildLayouts();
    }

    /// <summary>
    /// 새 패널 하나를 생성하여 "맨 위"에 추가
    /// </summary>
    public void AddPanelAtTop()
    {
        if (panelPrefab == null || content == null) return;

        RectTransform panel = Instantiate(panelPrefab, content);
        panel.gameObject.SetActive(true);
        panel.SetAsFirstSibling();                // 맨 위

        ApplyPanelLayoutRules(panel);
        ForceRebuildLayouts();

        // 데모 텍스트
        var example = panel.GetComponent<UIPanelSampleView>();
        if (example != null)
        {
            example.SetLabel($"New Panel - {System.DateTime.Now:HH:mm:ss}");
        }

        // 추가 직후 최상단으로 스크롤 고정
        scrollRect.verticalNormalizedPosition = 1f;
    }

    /// <summary>
    /// 패널에 LayoutElement 부착 및 사이즈 보정
    /// </summary>
    private void ApplyPanelLayoutRules(RectTransform panel)
    {
        panel.anchorMin = new Vector2(0.5f, 1f);
        panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 0.5f);

        var le = panel.GetComponent<LayoutElement>();
        if (le == null) le = panel.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = panelHeight;
        le.minHeight = panelHeight;
        le.flexibleHeight = 0f;

        panel.sizeDelta = new Vector2(panelWidth, panelHeight);
    }

    /// <summary>
    /// 강제 레이아웃 리빌드(두 번 호출로 안정화)
    /// </summary>
    private void ForceRebuildLayouts()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();
    }
}
