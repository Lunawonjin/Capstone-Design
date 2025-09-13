using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// MapMenuController
/// 
/// - 맵의 3개 메뉴 아이템에 호버/클릭 기능 부여.
/// - 클릭 시 현재 씬과 비교:
///   * 같으면 Map 오브젝트만 비활성화
///   * 다르면 씬 이동.
/// 
/// 사용 방법:
/// 1) Map 루트 오브젝트(전체 UI 패널)에 이 스크립트를 붙인다.
/// 2) menuItems 배열에 3개의 이미지(UI 오브젝트)를 순서대로 연결한다.
/// 3) sceneNames 배열에 이동할 씬 이름을 정확히 입력한다.
///    (Build Settings에 추가되어 있어야 함).
/// 4) mapRoot에는 Map UI 루트(GameObject)를 넣는다.
/// 5) 씬에 EventSystem, Canvas + GraphicRaycaster가 있어야 UI 입력이 동작.
/// </summary>
public class MapMenuController : MonoBehaviour
{
    [Header("Menu Items (3 UI Images)")]
    [Tooltip("마우스 호버/클릭을 받을 3개의 UI 오브젝트를 순서대로 등록합니다.")]
    [SerializeField] private GameObject[] menuItems = new GameObject[3];

    [Header("Target Scene Names (Size must match menuItems)")]
    [Tooltip("클릭 시 이동할 씬 이름들 (Build Settings에 포함되어야 함)")]
    [SerializeField]
    private string[] sceneNames = new string[3]
    {
        "Player's Room",
        "Starest",
        "Shopping Center"
    };

    [Header("Hover Visual Settings")]
    [Tooltip("호버 시 오브젝트에 적용할 스케일 배수")]
    [SerializeField, Range(1.0f, 2.0f)] private float hoverScale = 1.08f;

    [Tooltip("호버 시 오브젝트에 적용할 컬러(알파는 기존 유지 권장). 기본: 약간 밝게")]
    [SerializeField] private Color hoverColor = new Color(1.05f, 1.05f, 1.05f, 1f);

    [Tooltip("호버 해제 시 원래 상태로 되돌릴 때의 스케일 (보통 1)")]
    [SerializeField] private float normalScale = 1.0f;

    [Tooltip("호버 해제 시 원래 컬러로 복귀 (Image의 기본 컬러) 여부")]
    [SerializeField] private bool revertToOriginalColor = true;

    [Header("Map Root")]
    [Tooltip("현재 활성화/비활성화할 맵 루트 오브젝트")]
    [SerializeField] private GameObject mapRoot;
    [SerializeField] private GameObject mapAessts;

    private void Awake()
    {
        if (menuItems.Length != sceneNames.Length)
        {
            Debug.LogWarning("menuItems와 sceneNames의 크기가 다릅니다.");
        }

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

                // 현재 씬과 이동할 씬이 같으면 Map만 비활성화
                if (currentScene == targetScene)
                {
                    if (mapRoot != null)
                    {
                        mapRoot.SetActive(false);
                        mapAessts.SetActive(true);
                        Debug.Log($"현재 씬({currentScene})과 동일하여 Map만 비활성화");
                    }
                }
                else
                {
                    // 다른 씬이면 씬 이동
                    SceneManager.LoadScene(targetScene);
                }
            };
        }
    }
}

/// <summary>
/// HoverableMenuItem
/// 
/// 역할:
/// - UI 오브젝트에 마우스 호버/해제/클릭 동작을 부여.
/// - IPointerEnterHandler / IPointerExitHandler / IPointerClickHandler 사용.
/// - 시각 효과: 스케일 변경, 컬러 틴트(옵션).
/// - 클릭 콜백: 외부에서 onClick 델리게이트로 주입.
/// 
/// 주의:
/// - 같은 GameObject에 Image(또는 Graphic)가 있어야 UI Raycast가 동작.
/// - 상호작용을 위해 Canvas에 GraphicRaycaster, 씬에 EventSystem이 필요.
/// </summary>
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
        {
            _originalColor = _graphic.color;
        }
        else
        {
            Debug.LogWarning($"[HoverableMenuItem] '{name}' 에 Graphic(Image/Text 등)이 없어 컬러 틴트를 적용할 수 없습니다.", this);
        }

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
        {
            _graphic.color = _originalColor;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;

        if (onClick != null)
        {
            onClick.Invoke();
        }
        else
        {
            Debug.LogWarning("[HoverableMenuItem] onClick 콜백이 설정되지 않음", this);
        }
    }

    private void SetScale(float target)
    {
        if (_rect != null)
        {
            _rect.localScale = new Vector3(target, target, 1f);
        }
        else
        {
            transform.localScale = new Vector3(target, target, 1f);
        }
    }

    private Color MultiplyColor(Color baseColor, Color mul)
    {
        return new Color(
            baseColor.r * mul.r,
            baseColor.g * mul.g,
            baseColor.b * mul.b,
            baseColor.a * mul.a
        );
    }
}
