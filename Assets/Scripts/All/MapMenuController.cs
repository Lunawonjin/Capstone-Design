// MapMenuController.cs
// Unity 6 (LTS)
// 기능 요약:
// - M/Esc로 맵 열고 닫기, Exit 버튼으로 닫기
// - 현재 씬과 메뉴 항목의 씬을 비교해 "현재 위치 화살표"를 해당 버튼 위에 표시
// - 맵이 열려 있는 동안 화살표가 UnscaledTime 기준으로 위아래로 천천히 흔들림
// - 주말이 아닌 평일에는 상점가(Shopping Center) 입장 시 안내 알림 표시

using System;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class MapMenuController : MonoBehaviour
{
    public enum SceneIdMode { ByName, ByBuildIndex }

    [Header("메뉴 항목(클릭 대상)")]
    [SerializeField] private GameObject[] menuItems = Array.Empty<GameObject>();

    [Header("씬 식별 방식")]
    [SerializeField] private SceneIdMode sceneIdMode = SceneIdMode.ByBuildIndex;

    [Header("타겟 씬 이름 (menuItems와 인덱스 정렬)")]
    [SerializeField] private string[] sceneNames = Array.Empty<string>();

    [Header("타겟 씬 빌드 인덱스 (menuItems와 인덱스 정렬)")]
    [SerializeField] private int[] sceneBuildIndices = Array.Empty<int>();

    [Header("호버 연출")]
    [SerializeField, Range(1.0f, 2.0f)] private float hoverScale = 1.08f;
    [SerializeField] private Color hoverColor = new Color(1.05f, 1.05f, 1.05f, 1f);
    [SerializeField] private float normalScale = 1.0f;
    [SerializeField] private bool revertToOriginalColor = true;

    [Header("맵 루트 / 맵 패널")]
    [SerializeField] private GameObject map;      // 애니메이션으로 ON/OFF
    [SerializeField] private GameObject mapPanel; // 즉시 ON/OFF (레이아웃 루트)

    [Header("UI 배타 그룹(선택)")]
    [SerializeField] private UIExclusiveManager uiGroup;

    [Header("단축키")]
    [SerializeField] private KeyCode openKey = KeyCode.M;
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;
    [SerializeField] private KeyCode[] extraCloseKeys = { KeyCode.M, KeyCode.Escape };

    [Header("버튼")]
    [SerializeField] private Button exitButton;   // 클릭 시 맵 닫기

    [Header("맵 열기/닫기 애니메이션 (Unscaled Time)")]
    [SerializeField] private Vector3 openStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 openEndScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float openDuration = 0.14f;
    [SerializeField] private Vector3 closeStartScale = Vector3.one;
    [SerializeField] private Vector3 closeEndScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField, Min(0.01f)] private float closeDuration = 0.12f;

    [Header("알파 페이드(CanvasGroup 필요)")]
    [SerializeField] private bool useAlphaFade = true;

    [Header("외부 SetActive 동기화")]
    [SerializeField] private bool syncWithPanelActive = true;

    // 상점가 주말 제한
    [Header("상점가(주말만 입장 가능)")]
    [SerializeField] private int shopItemIndex = 2;
    [SerializeField] private string shopSceneName = "Shopping Center";

    [Header("알림(평일 차단 안내)")]
    [SerializeField] private GameObject notificationRoot;
    [SerializeField] private Button okButton;
    [SerializeField] private bool notificationBlocksClicks = true;
    [SerializeField, Min(0.01f)] private float notifOpenDuration = 0.14f;
    [SerializeField, Min(0.01f)] private float notifCloseDuration = 0.12f;
    [SerializeField] private Vector3 notifStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 notifEndScale = Vector3.one;

    // 현재 위치 화살표
    [Header("현재 위치 화살표")]
    [Tooltip("메뉴 버튼 위에 표시할 화살표 오브젝트(RectTransform). 프리팹 또는 씬상의 오브젝트를 할당")]
    [SerializeField] private RectTransform currentArrow;
    [Tooltip("화살표 기준 위치 오프셋(부모 버튼의 로컬 상단 등 적절히 조정)")]
    [SerializeField] private Vector2 arrowAnchorOffset = new Vector2(0f, 60f);
    [Tooltip("화살표 바운스 진폭(픽셀)")]
    [SerializeField, Min(0f)] private float arrowBobAmplitude = 6f;
    [Tooltip("화살표 바운스 속도(초당 사이클 비율)")]
    [SerializeField, Min(0.01f)] private float arrowBobSpeed = 2f;
    [Tooltip("맵이 닫힐 때 화살표를 숨길지 여부")]
    [SerializeField] private bool hideArrowWhenClosed = true;
    [Tooltip("씬 이름 대신 빌드 인덱스 매칭이 가능할 때 우선 사용")]
    [SerializeField] private bool preferBuildIndexMatch = true;

    // 내부 캐시
    RectTransform _mapRT;
    CanvasGroup _mapCG;
    RectTransform _notifRT;
    CanvasGroup _notifCG;

    bool _isOpen, _animating, _notifAnimating;
    bool _notifOpen;

    Coroutine _openCo, _closeCo, _notifOpenCo, _notifCloseCo;

    // 화살표 애니메이션용 내부 상태
    RectTransform _arrowParentRT;     // 현재 부착된 버튼의 RectTransform
    Vector2 _arrowBaseAnchoredPos;    // 바운스 기준점
    int _arrowBoundIndex = -1;        // 화살표가 연결된 메뉴 인덱스

    void Awake()
    {
        // 필수 참조 확인
        if (!map || !mapPanel)
        {
            Debug.LogError("[MapMenuController] map / mapPanel 참조가 필요합니다.");
            enabled = false; return;
        }

        _mapRT = map.GetComponent<RectTransform>();
        _mapCG = map.GetComponent<CanvasGroup>();

        // 맵 초기 상태
        mapPanel.SetActive(false);
        map.SetActive(false);
        if (_mapRT) _mapRT.localScale = openEndScale;
        if (_mapCG && useAlphaFade)
        {
            _mapCG.alpha = 0f;
            _mapCG.interactable = false;
            _mapCG.blocksRaycasts = false;
        }
        _isOpen = _animating = false;

        // 알림 초기화
        if (notificationRoot)
        {
            _notifRT = notificationRoot.GetComponent<RectTransform>();
            _notifCG = notificationRoot.GetComponent<CanvasGroup>();
            if (_notifRT) _notifRT.localScale = notifEndScale;
            if (_notifCG)
            {
                _notifCG.alpha = 0f;
                _notifCG.interactable = true;
                _notifCG.blocksRaycasts = notificationBlocksClicks;
            }
            notificationRoot.SetActive(false);
            _notifOpen = false;

            if (okButton) okButton.onClick.AddListener(HideNotification);
        }

        // UI 배타 그룹 자동 할당
#if UNITY_2023_1_OR_NEWER
        if (!uiGroup) uiGroup = UnityEngine.Object.FindAnyObjectByType<UIExclusiveManager>() ?? UnityEngine.Object.FindFirstObjectByType<UIExclusiveManager>();
#else
        if (!uiGroup) uiGroup = UnityEngine.Object.FindObjectOfType<UIExclusiveManager>();
#endif

        // 메뉴 항목에 호버/클릭 모듈 부착
        for (int i = 0; i < menuItems.Length; i++)
        {
            var go = menuItems[i]; if (!go) continue;
            var hover = go.GetComponent<HoverableMenuItem>() ?? go.AddComponent<HoverableMenuItem>();
            hover.SetVisualParams(normalScale, hoverScale, hoverColor, revertToOriginalColor);
            int idx = i;
            hover.onClick = () => OnMenuClick(idx);
        }

        // 빌드 인덱스 자동 해석
        AutoResolveBuildIndices();

        // Exit 버튼 연결
        if (exitButton) exitButton.onClick.AddListener(OnClickExit);

        // 화살표 초기 설정
        if (currentArrow)
        {
            currentArrow.gameObject.SetActive(false);
            _arrowParentRT = null;
            _arrowBoundIndex = -1;
        }
    }

    void OnDestroy()
    {
        if (okButton) okButton.onClick.RemoveListener(HideNotification);
        if (exitButton) exitButton.onClick.RemoveListener(OnClickExit);
    }

    void Update()
    {
        // 알림이 열려 있으면 Esc로 알림만 닫기
        if (_notifOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            HideNotification();
            return;
        }

        // 맵 열기
        if (Input.GetKeyDown(openKey)) OpenMap();

        // 맵 닫기 토글
        if ((_isOpen && Input.GetKeyDown(closeKey)) || extraCloseKeys.Any(Input.GetKeyDown))
            CloseMap();

        // 외부 SetActive 동기화
        if (syncWithPanelActive)
        {
            if (mapPanel.activeSelf && !_isOpen && !_animating) OpenMap(fromPanelWatchdog: true);
            else if (!mapPanel.activeSelf && (_isOpen || _animating)) ForceCloseImmediately();
        }

        // 맵이 열려 있고 화살표가 연결되어 있으면 바운스 애니메이션
        if (_isOpen && currentArrow && currentArrow.gameObject.activeSelf)
        {
            float t = Time.unscaledTime * arrowBobSpeed * Mathf.PI * 2f; // 주파수
            float dy = Mathf.Sin(t) * arrowBobAmplitude;
            var pos = _arrowBaseAnchoredPos;
            pos.y += dy;
            currentArrow.anchoredPosition = pos;
        }
    }

    // Exit 버튼 핸들러
    void OnClickExit()
    {
        if (_notifOpen) return; // 알림이 클릭을 막는 경우
        CloseMap();
    }

    // 메뉴 항목 클릭
    void OnMenuClick(int idx)
    {
        if (_notifOpen) return;

        // 상점가 주말 제한 체크
        if (idx == shopItemIndex && IsShopping(idx))
        {
            bool weekend = DataManager.instance != null && DataManager.instance.IsWeekend;
            if (!weekend) { ShowNotification(); return; }
        }

        // [MODIFIED] 씬을 로드하기 전에, 현재 게임 상태(PlayerData)를 임시 파일에 저장합니다.
        // 이렇게 하면 씬 이동 중에 발생할 수 있는 데이터 손실을 방지하고,
        // NpcEventDebugLoader 등 다른 스크립트에 의해 변경된 사항이 다음 씬에 반영됩니다.
        if (DataManager.instance != null)
        {
            DataManager.instance.CommitDataToTempFile();
        }

        var active = SceneManager.GetActiveScene();

        // 빌드 인덱스 우선 로드
        if (sceneIdMode == SceneIdMode.ByBuildIndex)
        {
            int build = (sceneBuildIndices != null && idx >= 0 && idx < sceneBuildIndices.Length) ? sceneBuildIndices[idx] : -1;

            if (build >= 0 && build < SceneManager.sceneCountInBuildSettings)
            {
                if (active.buildIndex == build) { CloseMap(); return; }
                SceneManager.LoadScene(build);
                return;
            }
            // 이름으로 폴백
        }

        string name = (sceneNames != null && idx >= 0 && idx < sceneNames.Length) ? sceneNames[idx] : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            Debug.LogWarning($"[MapMenu] 인덱스 {idx}에 씬 이름이 설정되지 않았습니다.");
            return;
        }

        if (SceneNameEqualsRobust(active.name, name)) { CloseMap(); return; }
        SceneManager.LoadScene(name);
    }

    // 상점가 여부 판별
    bool IsShopping(int idx)
    {
        string name = (sceneNames != null && idx >= 0 && idx < sceneNames.Length) ? sceneNames[idx] : null;
        return !string.IsNullOrEmpty(name) && SceneNameEqualsRobust(name, shopSceneName);
    }

    // 외부에서 호출 가능한 맵 열기
    public void OpenMap(bool fromPanelWatchdog = false)
    {
        if (_animating || _isOpen) return;
        if (!fromPanelWatchdog && uiGroup != null && !uiGroup.TryActivate(mapPanel)) return;

        if (!mapPanel.activeSelf) mapPanel.SetActive(true);

        if (_closeCo != null) StopCoroutine(_closeCo);
        _openCo = StartCoroutine(Co_OpenMap());
    }

    // 외부에서 호출 가능한 맵 닫기
    public void CloseMap()
    {
        if (_animating || !_isOpen) return;
        if (_openCo != null) StopCoroutine(_openCo);
        _closeCo = StartCoroutine(Co_CloseMap());
    }

    // 즉시 강제 닫기(워치독용)
    void ForceCloseImmediately()
    {
        if (_openCo != null) StopCoroutine(_openCo);
        if (_closeCo != null) StopCoroutine(_closeCo);
        _animating = false; _isOpen = false;

        if (map.activeSelf) map.SetActive(false);
        if (mapPanel.activeSelf) mapPanel.SetActive(false);

        if (_mapCG && useAlphaFade) { _mapCG.alpha = 0f; _mapCG.interactable = false; _mapCG.blocksRaycasts = false; }
        if (_mapRT) _mapRT.localScale = closeEndScale;

        // 화살표 처리
        if (currentArrow && hideArrowWhenClosed) currentArrow.gameObject.SetActive(false);
    }

    IEnumerator Co_OpenMap()
    {
        _isOpen = true; _animating = true;

        if (!map.activeSelf) map.SetActive(true);
        if (_mapRT) _mapRT.localScale = openStartScale;
        if (_mapCG && useAlphaFade) { _mapCG.alpha = 0f; _mapCG.interactable = false; _mapCG.blocksRaycasts = false; }

        float t = 0f, d = Mathf.Max(0.01f, openDuration);
        while (t < d)
        {
            float u = t / d, e = 1f - Mathf.Pow(1f - u, 3f);
            if (_mapRT) _mapRT.localScale = Vector3.LerpUnclamped(openStartScale, openEndScale, e);
            if (_mapCG && useAlphaFade) _mapCG.alpha = Mathf.LerpUnclamped(0f, 1f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }

        if (_mapRT) _mapRT.localScale = openEndScale;
        if (_mapCG && useAlphaFade) { _mapCG.alpha = 1f; _mapCG.interactable = true; _mapCG.blocksRaycasts = true; }

        _animating = false;

        // 맵이 열린 직후 현재 위치 화살표 갱신
        RefreshCurrentLocationArrow();
    }

    IEnumerator Co_CloseMap()
    {
        _animating = true;
        if (_mapCG && useAlphaFade) { _mapCG.interactable = false; _mapCG.blocksRaycasts = false; }

        float t = 0f, d = Mathf.Max(0.01f, closeDuration);
        float startAlpha = (_mapCG && useAlphaFade) ? _mapCG.alpha : 1f;

        while (t < d)
        {
            float u = t / d, e = Mathf.Pow(u, 3f);
            if (_mapRT) _mapRT.localScale = Vector3.LerpUnclamped(closeStartScale, closeEndScale, e);
            if (_mapCG && useAlphaFade) _mapCG.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }

        if (_mapRT) _mapRT.localScale = closeEndScale;
        if (_mapCG && useAlphaFade) _mapCG.alpha = 0f;

        if (map.activeSelf) map.SetActive(false);
        if (mapPanel.activeSelf) mapPanel.SetActive(false);

        _animating = false; _isOpen = false;

        // 맵 닫힘 시 화살표 처리
        if (currentArrow && hideArrowWhenClosed) currentArrow.gameObject.SetActive(false);
    }

    // 알림 표시
    void ShowNotification()
    {
        if (!notificationRoot || _notifAnimating) return;
        if (_notifCloseCo != null) StopCoroutine(_notifCloseCo);

        _notifOpen = true;

        notificationRoot.SetActive(true);
        if (_notifRT) _notifRT.localScale = notifStartScale;
        if (_notifCG)
        {
            _notifCG.alpha = 0f;
            _notifCG.interactable = true;
            _notifCG.blocksRaycasts = notificationBlocksClicks;
        }

        _notifOpenCo = StartCoroutine(Co_ShowNotification());
    }

    // 알림 숨김
    void HideNotification()
    {
        if (!notificationRoot || _notifAnimating || !notificationRoot.activeSelf) return;
        if (_notifOpenCo != null) StopCoroutine(_notifOpenCo);
        _notifCloseCo = StartCoroutine(Co_HideNotification());
    }

    IEnumerator Co_ShowNotification()
    {
        _notifAnimating = true;
        float t = 0f, d = Mathf.Max(0.01f, notifOpenDuration);
        while (t < d)
        {
            float u = t / d, e = 1f - Mathf.Pow(1f - u, 3f);
            if (_notifRT) _notifRT.localScale = Vector3.LerpUnclamped(notifStartScale, notifEndScale, e);
            if (_notifCG) _notifCG.alpha = Mathf.LerpUnclamped(0f, 1f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }
        if (_notifRT) _notifRT.localScale = notifEndScale;
        if (_notifCG) { _notifCG.alpha = 1f; }
        _notifAnimating = false;
    }

    IEnumerator Co_HideNotification()
    {
        _notifAnimating = true;
        float t = 0f, d = Mathf.Max(0.01f, notifCloseDuration);
        float startAlpha = _notifCG ? _notifCG.alpha : 1f;
        while (t < d)
        {
            float u = t / d, e = Mathf.Pow(u, 3f);
            if (_notifRT) _notifRT.localScale = Vector3.LerpUnclamped(notifEndScale, notifStartScale, e);
            if (_notifCG) _notifCG.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }
        if (_notifRT) _notifRT.localScale = notifStartScale;
        if (_notifCG) _notifCG.alpha = 0f;

        notificationRoot.SetActive(false);
        _notifAnimating = false;
        _notifOpen = false;
    }

    // 현재 위치 화살표 갱신
    void RefreshCurrentLocationArrow()
    {
        if (!currentArrow) return;

        // 현재 활성 씬 정보
        var active = SceneManager.GetActiveScene();
        string activeName = active.name;
        int activeIndex = active.buildIndex;

        // 메뉴 항목과 매칭될 인덱스 탐색
        int matchIdx = -1;

        // 빌드 인덱스 우선 매칭
        if (preferBuildIndexMatch && sceneIdMode == SceneIdMode.ByBuildIndex && sceneBuildIndices != null && sceneBuildIndices.Length == menuItems.Length)
        {
            for (int i = 0; i < sceneBuildIndices.Length; i++)
            {
                if (sceneBuildIndices[i] == activeIndex)
                {
                    matchIdx = i; break;
                }
            }
        }

        // 이름 매칭(폴백)
        if (matchIdx < 0 && sceneNames != null && sceneNames.Length == menuItems.Length)
        {
            for (int i = 0; i < sceneNames.Length; i++)
            {
                var name = sceneNames[i];
                if (!string.IsNullOrWhiteSpace(name) && SceneNameEqualsRobust(activeName, name))
                {
                    matchIdx = i; break;
                }
            }
        }

        // 매칭 실패 시 화살표 숨김
        if (matchIdx < 0 || matchIdx >= menuItems.Length || menuItems[matchIdx] == null)
        {
            currentArrow.gameObject.SetActive(false);
            _arrowParentRT = null;
            _arrowBoundIndex = -1;
            return;
        }

        // 대상 버튼의 RectTransform
        var targetRT = menuItems[matchIdx].GetComponent<RectTransform>();
        if (!targetRT)
        {
            currentArrow.gameObject.SetActive(false);
            _arrowParentRT = null;
            _arrowBoundIndex = -1;
            return;
        }

        // 화살표를 대상 버튼의 자식으로 부착
        currentArrow.SetParent(targetRT, worldPositionStays: false);

        // 기준 위치 계산
        // 버튼의 상단 중앙 위로 offset 만큼 올려 배치하는 느낌으로 설정
        // anchoredPosition은 버튼의 피벗과 앵커 설정에 따라 달라질 수 있으므로
        // 단순 offset 적용 후, 필요 시 인스펙터에서 arrowAnchorOffset을 조정
        _arrowBaseAnchoredPos = arrowAnchorOffset;
        currentArrow.anchoredPosition = _arrowBaseAnchoredPos;

        // 활성화 및 내부 상태 기록
        currentArrow.gameObject.SetActive(true);
        _arrowParentRT = targetRT;
        _arrowBoundIndex = matchIdx;
    }

    // 빌드 인덱스 자동 해석
    void AutoResolveBuildIndices()
    {
        if (sceneIdMode != SceneIdMode.ByBuildIndex) return;
        if (sceneBuildIndices == null || sceneBuildIndices.Length != menuItems.Length)
            sceneBuildIndices = Enumerable.Repeat(-1, menuItems.Length).ToArray();

        int count = SceneManager.sceneCountInBuildSettings;
        var nameToIndex = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!nameToIndex.ContainsKey(name)) nameToIndex.Add(name, i);
        }

        for (int i = 0; i < sceneBuildIndices.Length && i < sceneNames.Length; i++)
        {
            if (sceneBuildIndices[i] >= 0) continue;
            string want = sceneNames[i];
            if (string.IsNullOrWhiteSpace(want)) continue;
            string norm = Normalize(want);
            if (nameToIndex.TryGetValue(norm, out int idx)) sceneBuildIndices[i] = idx;
            else
            {
                foreach (var kv in nameToIndex)
                {
                    if (SceneNameEqualsRobust(kv.Key, norm)) { sceneBuildIndices[i] = kv.Value; break; }
                }
            }
        }
    }

    // 이름 비교 유틸(공백/따옴표 제거 후 대소문자 무시)
    static bool SceneNameEqualsRobust(string a, string b)
    {
        string na = Normalize(a), nb = Normalize(b);
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    // 정규화(공백 및 따옴표 제거)
    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = s.Trim();
        return new string(s.Where(ch => ch != ' ' && ch != '\'' && ch != '’').ToArray());
    }
}

[RequireComponent(typeof(RectTransform))]
public class HoverableMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // 클릭 콜백(외부에서 할당)
    public System.Action onClick;

    [SerializeField] private float _normalScale = 1f;
    [SerializeField] private float _hoverScale = 1.08f;
    [SerializeField] private Color _hoverColor = new Color(1.05f, 1.05f, 1.05f, 1f);
    [SerializeField] private bool _revertToOriginalColor = true;

    private RectTransform _rect;
    private Graphic _graphic;

    private Color _baseColor;
    private bool _hasBaseColor;

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
            _baseColor = _graphic.color;
            _hasBaseColor = true;
        }
        else
        {
            Debug.LogWarning($"[HoverableMenuItem] '{name}'에 Graphic이 없어 색상 연출 비활성화", this);
        }

        SetScale(_normalScale);
    }

    private void OnEnable()
    {
        if (_graphic != null && _hasBaseColor)
            _graphic.color = _baseColor;
        SetScale(_normalScale);
    }

    // 외부에서 호버 파라미터 세팅
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
        if (_graphic != null && _hasBaseColor)
            _graphic.color = MultiplyColor(_baseColor, _hoverColor);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetScale(_normalScale);
        if (_graphic != null && _revertToOriginalColor && _hasBaseColor)
            _graphic.color = _baseColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
        onClick?.Invoke();
    }

    private void SetScale(float s)
    {
        if (_rect != null) _rect.localScale = new Vector3(s, s, 1f);
        else transform.localScale = new Vector3(s, s, 1f);
    }

    private static Color MultiplyColor(Color baseColor, Color mul)
    {
        return new Color(baseColor.r * mul.r, baseColor.g * mul.g, baseColor.b * mul.b, baseColor.a * mul.a);
    }
}
