// MapMenuController.cs
// 핵심 요약
//  - 도착 연출: 버스 카메라(busCameras) → 맵 카메라(mapCamera) 확실한 전환
//    · SetCameraActive(cam, on/off)로 컴포넌트와 GO 동시 제어 + depth 보정
//  - 버스 카메라 팔로우: Y 고정(lockY), 최소 X 경계(minX) 지원(SimpleCameraFollower)
//  - 씬 페이드: 로드 직전 페이드 인(검은 화면), "씬 넘어가면 즉시 페이드 아웃(=FadeTo(0) 즉시)"
//  - 출발 연출 시작 시 BusDoor 비활성화(상호작용/충돌 차단)
//  - 다른 씬으로 이동할 때 현재 씬의 버스(GameObject) 비활성화(옵션)
//  - 그 외: 출발 연출, 맵 토글, 화살표, 알림 등 기존 동작 유지

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
    [SerializeField] private GameObject map;
    [SerializeField] private GameObject mapPanel;

    [Header("UI 배타 그룹(선택)")]
    [SerializeField] private UIExclusiveManager uiGroup;

    [Header("단축키")]
    [SerializeField] private KeyCode openKey = KeyCode.M;
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;
    [SerializeField] private KeyCode[] extraCloseKeys = { KeyCode.M, KeyCode.Escape };

    [Header("버튼")]
    [SerializeField] private Button exitButton;

    // ===== 맵 열기/닫기 애니 (스케일 보존) =====
    [Header("맵 애니메이션 (Unscaled) - 스케일 보존")]
    [SerializeField] private bool keepOriginalScale = true;
    [SerializeField] private Vector3 openStartMul = new Vector3(1f, 1f, 1f);
    [SerializeField] private Vector3 openEndMul = new Vector3(1f, 1f, 1f);
    [SerializeField, Min(0.01f)] private float openDuration = 0.14f;
    [SerializeField] private Vector3 closeStartMul = new Vector3(1f, 1f, 1f);
    [SerializeField] private Vector3 closeEndMul = new Vector3(1f, 1f, 1f);
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
    [SerializeField] private RectTransform currentArrow;
    [SerializeField] private Vector2 arrowAnchorOffset = new Vector2(0f, 60f);
    [SerializeField, Min(0f)] private float arrowBobAmplitude = 6f;
    [SerializeField, Min(0.01f)] private float arrowBobSpeed = 2f;
    [SerializeField] private bool hideArrowWhenClosed = true;
    [SerializeField] private bool preferBuildIndexMatch = true;

    // === 충돌로 맵 열기 ===
    [Header("충돌로 맵 열기(F)")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private Collider2D playerCollider;
    [SerializeField] private Collider2D[] openMapColliders = Array.Empty<Collider2D>();

    // === 가이드 화살표 ===
    [Header("가이드 화살표(목표 유도)")]
    [SerializeField] private RectTransform guideArrow;
    [SerializeField] private Vector2 guideArrowOffset = new Vector2(0f, 72f);
    [SerializeField, Min(0f)] private float guideBobAmplitude = 10f;
    [SerializeField, Min(0.01f)] private float guideBobSpeed = 2.2f;
    [SerializeField] private bool showGuideWhenClosed = false;

    [Header("가이드 대상 플래그")]
    public bool PlayerGoPlayerRoom = false;
    public bool PlayerGoStarest = false;
    public bool PlayerGoShopping = false;

    [Header("가이드 대상 인덱스(기본 0/1/2)")]
    [SerializeField] private int guideIndexPlayerRoom = 0;
    [SerializeField] private int guideIndexStarest = 1;
    [SerializeField] private int guideIndexShopping = 2;

    // ===== 출발 연출(씬별 설정) =====
    [Serializable]
    public class DepartConfig
    {
        public string sceneName = "";
        public Vector2 playerStart = new Vector2(14.8f, -22f);
        public float playerEndY = -21.1f;
        [Min(0.01f)] public float riseDuration = 1.0f;

        [Min(0.01f)] public float departBusSpeed = 6f;
        public float busTargetX = 24f;
        public bool enabled = true;
    }

    [Header("출발 연출 - 씬별 설정")]
    [SerializeField] private DepartConfig depart_PlayerRoom = new DepartConfig { sceneName = "Player's Room", playerStart = new Vector2(14.8f, -22f), playerEndY = -21.1f, riseDuration = 1.0f, busTargetX = 24f, departBusSpeed = 6f, enabled = true };
    [SerializeField] private DepartConfig depart_Starest = new DepartConfig { sceneName = "Starest", playerStart = new Vector2(0f, 0f), playerEndY = 1.0f, riseDuration = 1.0f, busTargetX = 24f, departBusSpeed = 6f, enabled = true };
    [SerializeField] private DepartConfig depart_Shopping = new DepartConfig { sceneName = "Shopping Center", playerStart = new Vector2(0f, 0f), playerEndY = 1.0f, riseDuration = 1.0f, busTargetX = 24f, departBusSpeed = 6f, enabled = true };

    [Header("출발 연출 애니/연동")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private GameObject playerRootToDisable;
    [SerializeField] private PlayerMove playerMove;
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private string departWalkState = "Back_Walk";
    [SerializeField, Min(0f)] private float departAnimFade = 0.05f;
    [SerializeField, Min(0f)] private float departAnimSpeed = 0.85f;

    [SerializeField] private Transform busTransform;
    [SerializeField] private AnimationCurve playerRiseEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // ===== 도착 연출(씬별 설정) =====
    [Serializable]
    public class ArrivalConfig
    {
        public string sceneName = "";
        public GameObject busPrefab;
        public float busStartX = -37f;
        public float busStopX = -15f;
        public float busFinalX = -1f;
        public float busY = -18.5f;
        public Vector2 playerDropPos = new Vector2(-14.5f, -18.5f);

        [Min(0.01f)] public float busArriveDuration = 1.2f;
        [Min(0f)] public float stopWait = 0.4f;
        [Min(0.01f)] public float busLeaveDuration = 1.0f;

        [Header("상수 속도(유닛/초)")]
        [Min(0.01f)] public float arriveSpeed = 6f;
        [Min(0.01f)] public float leaveSpeed = 6f;

        public bool enabled = true;
    }

    [Header("도착 연출 - 씬별 설정")]
    [SerializeField] private ArrivalConfig arrive_PlayerRoom = new ArrivalConfig { sceneName = "Player's Room", enabled = false };
    [SerializeField]
    private ArrivalConfig arrive_Starest = new ArrivalConfig
    {
        sceneName = "Starest",
        busStartX = -37f,
        busStopX = -15f,
        busFinalX = -1f,
        busY = -18.5f,
        playerDropPos = new Vector2(-14.5f, -18.5f),
        busArriveDuration = 1.2f,
        stopWait = 0.4f,
        busLeaveDuration = 1.0f,
        arriveSpeed = 3.5f,
        leaveSpeed = 4.0f,
        enabled = true
    };
    [SerializeField] private ArrivalConfig arrive_Shopping = new ArrivalConfig { sceneName = "Shopping Center", enabled = false };

    [Header("도착 연출 - 플레이어 탐색/활성화 옵션")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string playerNameIfNoTag = "Player";
    [SerializeField] private string[] arrivalPlayerCandidateNames = new string[] { "Player", "Hero", "MainCharacter" };
    [SerializeField] private bool forceEnableParentHierarchy = true;
    [SerializeField] private bool arrivalLockControls = true;

    [Header("도착 연출 - 카메라 제어")]
    [SerializeField] private Camera[] busCameras = Array.Empty<Camera>(); // 버스 추적 카메라들
    [SerializeField] private Camera mapCamera;                             // 정차 후 활성화할 맵 카메라

    [Header("버스 카메라 팔로우 설정")]
    [SerializeField] private Vector3 arrivalCameraOffset = new Vector3(0f, 0f, 0f);
    [SerializeField, Min(0.01f)] private float arrivalCameraSmoothTime = 0.15f;
    [SerializeField] private bool addTempFollowerIfMissing = true;

    [Header("버스 카메라 Y 고정")]
    [SerializeField] private bool lockCameraY = true;
    [SerializeField] private float lockedCameraY = -18.5f;

    [Header("버스 카메라 최소 X 경계")]
    [SerializeField] private bool useMinCameraX = true;
    [SerializeField] private float minCameraX = -20f;

    // ===== 씬 페이드 옵션 =====
    [Header("씬 페이드 옵션")]
    [SerializeField] private bool useSceneFade = true;
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.35f;   // 로드 직전: 0→1
    [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.45f;  // 유지(즉시 모드에서는 의미 적음)
    [SerializeField] private Color fadeColor = Color.black;

    // ===== 출발 시 BusDoor/버스 비활성화 =====
    [Header("버스 문(BusDoor) - 출발 시작 즉시 비활성화")]
    [SerializeField] private GameObject busDoor;
    [SerializeField] private bool disableDoorOnDepartStart = true;

    [Header("출발 시 버스 비활성화(다른 씬으로 이동할 때 현재 씬 버스 끄기)")]
    [SerializeField] private bool deactivateBusOnSceneChange = true;

    // ===== 내부 상태 =====
    RectTransform _mapRT;
    CanvasGroup _mapCG;
    RectTransform _notifRT;
    CanvasGroup _notifCG;

    bool _isOpen, _animating, _notifAnimating;
    bool _notifOpen;
    bool _isDeparting;

    Coroutine _openCo, _closeCo, _notifOpenCo, _notifCloseCo;

    RectTransform _arrowParentRT;
    Vector2 _arrowBaseAnchoredPos;
    int _arrowBoundIndex = -1;

    RectTransform _guideParentRT;
    Vector2 _guideBaseAnchoredPos;
    int _guideBoundIndex = -1;

    Vector3 _mapInitialScale = Vector3.one;

    private static ArrivalConfig s_pendingArrival;
    private static bool s_hasPendingArrival = false;

    void Awake()
    {
        if (!map || !mapPanel)
        {
            Debug.LogError("[MapMenuController] map / mapPanel 참조가 필요합니다.");
            enabled = false; return;
        }

        // ScreenFader 준비
        if (useSceneFade) ScreenFader.Initialize(fadeColor);

        _mapRT = map.GetComponent<RectTransform>();
        _mapCG = map.GetComponent<CanvasGroup>();
        if (_mapRT) _mapInitialScale = _mapRT.localScale;

        mapPanel.SetActive(false);
        map.SetActive(false);
        if (_mapRT) _mapRT.localScale = keepOriginalScale ? Vector3.Scale(_mapInitialScale, openEndMul) : Vector3.one;
        if (_mapCG && useAlphaFade)
        {
            _mapCG.alpha = 0f; _mapCG.interactable = false; _mapCG.blocksRaycasts = false;
        }
        _isOpen = _animating = false;

        if (notificationRoot)
        {
            _notifRT = notificationRoot.GetComponent<RectTransform>();
            _notifCG = notificationRoot.GetComponent<CanvasGroup>();
            if (_notifRT) _notifRT.localScale = notifEndScale;
            if (_notifCG) { _notifCG.alpha = 0f; _notifCG.interactable = true; _notifCG.blocksRaycasts = notificationBlocksClicks; }
            notificationRoot.SetActive(false); _notifOpen = false;
            if (okButton) okButton.onClick.AddListener(HideNotification);
        }

#if UNITY_2023_1_OR_NEWER
        if (!uiGroup) uiGroup = UnityEngine.Object.FindAnyObjectByType<UIExclusiveManager>() ?? UnityEngine.Object.FindFirstObjectByType<UIExclusiveManager>();
#else
        if (!uiGroup) uiGroup = UnityEngine.Object.FindObjectOfType<UIExclusiveManager>();
#endif

        for (int i = 0; i < menuItems.Length; i++)
        {
            var go = menuItems[i]; if (!go) continue;
            var hover = go.GetComponent<HoverableMenuItem>() ?? go.AddComponent<HoverableMenuItem>();
            hover.SetVisualParams(normalScale, hoverScale, hoverColor, revertToOriginalColor);
            int idx = i;
            hover.onClick = () => OnMenuClick(idx);
        }

        AutoResolveBuildIndices();

        if (exitButton) exitButton.onClick.AddListener(OnClickExit);

        if (currentArrow) { currentArrow.gameObject.SetActive(false); _arrowParentRT = null; _arrowBoundIndex = -1; }
        if (guideArrow) { guideArrow.gameObject.SetActive(false); _guideParentRT = null; _guideBoundIndex = -1; }

        SceneManager.sceneLoaded += OnSceneLoaded_Arrival;
    }

    void OnDestroy()
    {
        if (okButton) okButton.onClick.RemoveListener(HideNotification);
        if (exitButton) exitButton.onClick.RemoveListener(OnClickExit);
        SceneManager.sceneLoaded -= OnSceneLoaded_Arrival;
    }

    void Update()
    {
        if (_notifOpen && Input.GetKeyDown(KeyCode.Escape)) { HideNotification(); return; }
        if (Input.GetKeyDown(openKey)) OpenMap();
        if (Input.GetKeyDown(interactKey) && IsTouchingAnyOpenable()) OpenMap();
        if ((_isOpen && Input.GetKeyDown(closeKey)) || extraCloseKeys.Any(Input.GetKeyDown)) CloseMap();

        if (syncWithPanelActive)
        {
            if (mapPanel.activeSelf && !_isOpen && !_animating) OpenMap(fromPanelWatchdog: true);
            else if (!mapPanel.activeSelf && (_isOpen || _animating)) ForceCloseImmediately();
        }

        if (_isOpen && currentArrow && currentArrow.gameObject.activeSelf)
        {
            float t = Time.unscaledTime * arrowBobSpeed * Mathf.PI * 2f;
            float dy = Mathf.Sin(t) * arrowBobAmplitude;
            var pos = _arrowBaseAnchoredPos; pos.y += dy;
            currentArrow.anchoredPosition = pos;
        }
        if ((showGuideWhenClosed || _isOpen) && guideArrow && guideArrow.gameObject.activeSelf)
        {
            float t2 = Time.unscaledTime * guideBobSpeed * Mathf.PI * 2f;
            float dy2 = Mathf.Sin(t2) * guideBobAmplitude;
            var pos2 = _guideBaseAnchoredPos; pos2.y += dy2;
            guideArrow.anchoredPosition = pos2;
        }
    }

    // ===== 메뉴 클릭 =====
    void OnMenuClick(int idx)
    {
        if (_notifOpen || _isDeparting) return;

        if (idx == shopItemIndex && IsShopping(idx))
        {
            bool weekend = DataManager.instance != null && DataManager.instance.IsWeekend;
            if (!weekend) { ShowNotification(); return; }
        }

        // ★ 씬 이동 직전 현재 씬의 "켜진 오브젝트들"을 sub_save에 스냅샷 저장
        if (DataManager.instance != null)
            DataManager.instance.SubSaveCommitActivesForCurrentScene();

        // 기존 임시 저장(이벤트용) 유지
        if (DataManager.instance != null) DataManager.instance.CommitDataToTempFile();

        var active = SceneManager.GetActiveScene();

        string targetSceneName = null;
        Action loadAction = null;

        if (sceneIdMode == SceneIdMode.ByBuildIndex)
        {
            int build = (sceneBuildIndices != null && idx >= 0 && idx < sceneBuildIndices.Length) ? sceneBuildIndices[idx] : -1;
            if (build >= 0 && build < SceneManager.sceneCountInBuildSettings)
            {
                if (active.buildIndex == build) { CloseMap(); return; }
                string path = SceneUtility.GetScenePathByBuildIndex(build);
                targetSceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                loadAction = () => SceneManager.LoadScene(build);
            }
        }
        if (loadAction == null)
        {
            targetSceneName = (sceneNames != null && idx >= 0 && idx < sceneNames.Length) ? sceneNames[idx] : null;
            if (string.IsNullOrWhiteSpace(targetSceneName))
            {
                Debug.LogWarning($"[MapMenu] 인덱스 {idx}에 씬 이름이 설정되지 않았습니다.");
                return;
            }
            if (SceneNameEqualsRobust(active.name, targetSceneName)) { CloseMap(); return; }
            loadAction = () => SceneManager.LoadScene(targetSceneName);
        }

        if (TryGetArrivalConfigForScene(targetSceneName, out var arrCfg) && arrCfg.enabled)
        {
            s_pendingArrival = arrCfg;
            s_hasPendingArrival = true;
        }
        else
        {
            s_hasPendingArrival = false;
            s_pendingArrival = null;
        }

        ForceCloseImmediately();

        if (TryGetDepartConfigForScene(active.name, out var depCfg) && depCfg.enabled)
            StartCoroutine(Co_DepartThenLoad(loadAction, depCfg));
        else
            StartCoroutine(Co_LoadWithFadeOnly(loadAction)); // 출발 연출이 없어도 페이드는 적용
    }

    bool TryGetDepartConfigForScene(string sceneName, out DepartConfig cfg)
    {
        if (SceneNameEqualsRobust(sceneName, depart_PlayerRoom.sceneName)) { cfg = depart_PlayerRoom; return true; }
        if (SceneNameEqualsRobust(sceneName, depart_Starest.sceneName)) { cfg = depart_Starest; return true; }
        if (SceneNameEqualsRobust(sceneName, depart_Shopping.sceneName)) { cfg = depart_Shopping; return true; }
        cfg = null; return false;
    }

    IEnumerator Co_LoadWithFadeOnly(Action loadScene)
    {
        if (useSceneFade)
            yield return ScreenFader.Instance.FadeTo(1f, Mathf.Max(0.01f, fadeInDuration), fadeColor);

        loadScene?.Invoke();
    }

    IEnumerator Co_DepartThenLoad(Action loadScene, DepartConfig cfg)
    {
        _isDeparting = true;

        // ★ 출발 연출 시작 즉시 BusDoor 비활성화 (F 아이콘/충돌 차단)
        if (disableDoorOnDepartStart) SetBusDoorEnabled(false);

        if (playerTransform)
        {
            var p = playerTransform.position;
            playerTransform.position = new Vector3(cfg.playerStart.x, cfg.playerStart.y, p.z);

            var pm = playerMove ? playerMove : playerTransform.GetComponent<PlayerMove>();
            Animator anim = playerAnimator ? playerAnimator : playerTransform.GetComponent<Animator>();
            if (pm != null) pm.SetControlEnabled(false);

            var prevMode = anim ? anim.updateMode : AnimatorUpdateMode.Normal;
            if (anim) anim.updateMode = AnimatorUpdateMode.UnscaledTime;

            if (pm != null) pm.ExternalAnim_PlayWalk(Vector2.up, 0.85f);
            else
            {
                if (anim && !string.IsNullOrEmpty(departWalkState))
                    anim.CrossFadeInFixedTime(departWalkState, departAnimFade, 0);
                if (anim) anim.speed = 0.85f;
            }

            float t = 0f, dur = Mathf.Max(0.01f, cfg.riseDuration);
            float startY = cfg.playerStart.y, endY = cfg.playerEndY;
            while (t < dur)
            {
                float u = Mathf.Clamp01(t / dur);
                float e = playerRiseEase != null ? playerRiseEase.Evaluate(u) : u;
                float ny = Mathf.LerpUnclamped(startY, endY, e);
                playerTransform.position = new Vector3(cfg.playerStart.x, ny, p.z);
                t += Time.unscaledDeltaTime; yield return null;
            }
            playerTransform.position = new Vector3(cfg.playerStart.x, endY, p.z);

            if (pm != null) pm.ExternalAnim_StopIdle();
            if (anim) anim.updateMode = prevMode;

            var root = playerRootToDisable ? playerRootToDisable : playerTransform.gameObject;
            root.SetActive(false);
        }

        if (busTransform)
        {
            // 출발 연출: 화면 밖으로 이동하는 연출 유지
            var b = busTransform;
            Vector3 target = new Vector3(cfg.busTargetX, b.position.y, b.position.z);
            float spd = Mathf.Max(0.01f, cfg.departBusSpeed);

            while ((b.position - target).sqrMagnitude > 0.0001f)
            {
                b.position = Vector3.MoveTowards(b.position, target, spd * Time.unscaledDeltaTime);
                yield return null;
            }
            b.position = target;

            // ★ 다른 씬으로 넘어가기 직전, 현재 씬의 버스를 비활성화(옵션)
            if (deactivateBusOnSceneChange && b.gameObject.activeSelf)
                b.gameObject.SetActive(false);
        }

        // 로드 직전 페이드 인(검은 화면)
        if (useSceneFade)
            yield return ScreenFader.Instance.FadeTo(1f, Mathf.Max(0.01f, fadeInDuration), fadeColor);

        loadScene?.Invoke();
        _isDeparting = false;
    }

    bool TryGetArrivalConfigForScene(string sceneName, out ArrivalConfig cfg)
    {
        if (SceneNameEqualsRobust(sceneName, arrive_PlayerRoom.sceneName)) { cfg = arrive_PlayerRoom; return true; }
        if (SceneNameEqualsRobust(sceneName, arrive_Starest.sceneName)) { cfg = arrive_Starest; return true; }
        if (SceneNameEqualsRobust(sceneName, arrive_Shopping.sceneName)) { cfg = arrive_Shopping; return true; }
        cfg = null; return false;
    }

    void OnSceneLoaded_Arrival(Scene scene, LoadSceneMode mode)
    {
        // 요청: 씬 로드 직후 즉시 FadeTo(0)
        if (useSceneFade)
            StartCoroutine(ScreenFader.Instance.FadeTo(0f, 0.001f, fadeColor));

        // 도착 연출이 있으면 그대로 진행(페이드는 이미 0이므로 추가 페이드 없음)
        if (s_hasPendingArrival && s_pendingArrival != null && SceneNameEqualsRobust(scene.name, s_pendingArrival.sceneName))
        {
            var cfg = s_pendingArrival;
            s_hasPendingArrival = false;
            s_pendingArrival = null;
            StartCoroutine(Co_RunArrival(cfg));
        }
    }

    IEnumerator Co_RunArrival(ArrivalConfig cfg)
    {
        // 0) 버스 생성
        GameObject bus = null;
        if (cfg.busPrefab != null)
        {
            bus = Instantiate(cfg.busPrefab);
            var bp = bus.transform.position;
            bus.transform.position = new Vector3(cfg.busStartX, cfg.busY, bp.z);
        }
        else
        {
            Debug.LogWarning("[MapMenu] Arrival: busPrefab이 비어 있습니다. 버스 연출을 건너뜁니다.");
        }

        // 0.5) 플레이어 찾기
        GameObject playerGO = FindPlayerGO(out PlayerMove pm);

        // 0.7) ▶ 버스 카메라들을 버스에 붙이기 (플레이어 활성화 전까지)
        if (bus) AttachCamerasFollow(bus.transform);

        // 1) 시작X → 정지X — 상수 속도
        if (bus)
        {
            var b = bus.transform;
            Vector3 targetStop = new Vector3(cfg.busStopX, cfg.busY, b.position.z);
            float spd = Mathf.Max(0.01f, cfg.arriveSpeed);

            while ((b.position - targetStop).sqrMagnitude > 0.0001f)
            {
                b.position = Vector3.MoveTowards(b.position, targetStop, spd * Time.unscaledDeltaTime);
                yield return null;
            }
            b.position = targetStop;
        }

        // 2) 정차 즉시 플레이어 활성화 + 하차 위치 배치
        if (playerGO)
        {
            if (arrivalLockControls && pm != null) pm.SetControlEnabled(false);

            if (!playerGO.activeInHierarchy && forceEnableParentHierarchy)
                EnsureHierarchyActive(playerGO);
            else if (!playerGO.activeSelf)
                playerGO.SetActive(true);

            var pz = playerGO.transform.position.z;
            playerGO.transform.position = new Vector3(cfg.playerDropPos.x, cfg.playerDropPos.y, pz);
        }

        // 2.1) ▶ 카메라 전환: 맵 카메라 ON, 버스 카메라 OFF (확실하게)
        SwitchToMapCamera();

        // 2.5) 정차 대기(연출 호흡)
        if (cfg.stopWait > 0f)
        {
            float tw = 0f;
            while (tw < cfg.stopWait) { tw += Time.unscaledDeltaTime; yield return null; }
        }

        // 3) 정지X → 최종X — 상수 속도
        if (bus)
        {
            var b = bus.transform;
            Vector3 targetFinal = new Vector3(cfg.busFinalX, cfg.busY, b.position.z);
            float spd = Mathf.Max(0.01f, cfg.leaveSpeed);

            while ((b.position - targetFinal).sqrMagnitude > 0.0001f)
            {
                b.position = Vector3.MoveTowards(b.position, targetFinal, spd * Time.unscaledDeltaTime);
                yield return null;
            }
            b.position = targetFinal;
            Destroy(bus);
        }

        // 4) 입력 해제
        if (arrivalLockControls)
        {
            if (pm == null && playerGO != null) pm = playerGO.GetComponent<PlayerMove>();
            if (pm != null) pm.Unfreeze(keepAnimatorState: true);
        }

        yield break;
    }

    // ───────── 카메라 온/오프 보조 유틸 ─────────
    private void SetCameraActive(Camera cam, bool active, bool alsoToggleGameObject = true, float? overrideDepth = null)
    {
        if (!cam) return;
        if (alsoToggleGameObject && cam.gameObject.activeSelf != active)
            cam.gameObject.SetActive(active);

        cam.enabled = active;

        if (overrideDepth.HasValue)
            cam.depth = overrideDepth.Value;
    }

    // ───────── 카메라 팔로우 유틸 ─────────
    private void AttachCamerasFollow(Transform target)
    {
        if (busCameras == null || busCameras.Length == 0 || target == null) return;

        for (int i = 0; i < busCameras.Length; i++)
        {
            var cam = busCameras[i];
            if (!cam) continue;

            var follower = cam.GetComponent<SimpleCameraFollower>();
            if (!follower && addTempFollowerIfMissing)
                follower = cam.gameObject.AddComponent<SimpleCameraFollower>();

            if (follower)
            {
                follower.target = target;
                follower.offset = arrivalCameraOffset;
                follower.smoothTime = Mathf.Max(0.01f, arrivalCameraSmoothTime);

                // Y 고정 / 최소 X 경계 적용
                follower.lockY = lockCameraY;
                follower.fixedY = lockedCameraY;

                follower.useMinX = useMinCameraX;
                follower.minX = minCameraX;

                follower.enabled = true;
            }

            // 버스 카메라는 항상 보이도록(맵 카메라보다 뒤)
            cam.depth = 0f;
            SetCameraActive(cam, true, alsoToggleGameObject: true);
        }

        // 버스 들어오는 동안 맵 카메라는 꺼둠(컴포넌트+GO)
        if (mapCamera) SetCameraActive(mapCamera, false, alsoToggleGameObject: true);
    }

    // 맵 카메라로 전환(버스 카메라 끄고, 맵 카메라 확실히 켬)
    private void SwitchToMapCamera()
    {
        if (busCameras != null)
        {
            for (int i = 0; i < busCameras.Length; i++)
            {
                var cam = busCameras[i];
                if (!cam) continue;

                var follower = cam.GetComponent<SimpleCameraFollower>();
                if (follower) follower.enabled = false;

                SetCameraActive(cam, false, alsoToggleGameObject: true);
            }
        }

        if (mapCamera)
        {
            // 버스 카메라보다 앞서도록 depth 보정
            float frontDepth = 10f;
            if (busCameras != null)
            {
                foreach (var bc in busCameras)
                    if (bc) frontDepth = Mathf.Max(frontDepth, bc.depth + 1f);
            }
            SetCameraActive(mapCamera, true, alsoToggleGameObject: true, overrideDepth: frontDepth);
        }
        else
        {
            Debug.LogWarning("[MapMenu] mapCamera가 할당되지 않았습니다. 인스펙터에서 Map Camera를 지정하세요.");
        }
    }

    // ───────── 플레이어 탐색 유틸 ─────────
    private GameObject FindPlayerGO(out PlayerMove pm)
    {
        pm = null;
        GameObject playerGO = null;

        if (!string.IsNullOrEmpty(playerTag))
        {
            try { var tagged = GameObject.FindGameObjectWithTag(playerTag); if (tagged) playerGO = tagged; } catch { }
        }
        if (playerGO == null && !string.IsNullOrEmpty(playerNameIfNoTag))
        {
            var byName = GameObject.Find(playerNameIfNoTag);
            if (byName) playerGO = byName;
        }
        if (playerGO == null && arrivalPlayerCandidateNames != null)
        {
            foreach (var nm in arrivalPlayerCandidateNames)
            {
                if (string.IsNullOrWhiteSpace(nm)) continue;
                var go = GameObject.Find(nm.Trim());
                if (go) { playerGO = go; break; }
            }
        }

#if UNITY_2023_1_OR_NEWER
        if (playerGO == null) pm = UnityEngine.Object.FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include);
#else
        if (playerGO == null) pm = UnityEngine.Object.FindObjectOfType<PlayerMove>();
#endif
        if (pm != null && playerGO == null) playerGO = pm.gameObject;
        if (pm == null && playerGO != null) pm = playerGO.GetComponent<PlayerMove>();
        return playerGO;
    }

    // ===== 맵/알림 등 기존 =====
    void OnClickExit() { if (_notifOpen) return; CloseMap(); }

    bool IsShopping(int idx)
    {
        string name = (sceneNames != null && idx >= 0 && idx < sceneNames.Length) ? sceneNames[idx] : null;
        return !string.IsNullOrEmpty(name) && SceneNameEqualsRobust(name, shopSceneName);
    }

    public void OpenMap(bool fromPanelWatchdog = false)
    {
        if (_animating || _isOpen) return;
        if (!fromPanelWatchdog && uiGroup != null && !uiGroup.TryActivate(mapPanel)) return;
        if (!mapPanel.activeSelf) mapPanel.SetActive(true);
        if (_closeCo != null) StopCoroutine(_closeCo);
        _openCo = StartCoroutine(Co_OpenMap());
    }

    public void CloseMap()
    {
        if (_animating || !_isOpen) return;
        if (_openCo != null) StopCoroutine(_openCo);
        _closeCo = StartCoroutine(Co_CloseMap());
    }

    void ForceCloseImmediately()
    {
        if (_openCo != null) StopCoroutine(_openCo);
        if (_closeCo != null) StopCoroutine(_closeCo);
        _animating = false; _isOpen = false;

        if (map.activeSelf) map.SetActive(false);
        if (mapPanel.activeSelf) mapPanel.SetActive(false);

        if (_mapCG && useAlphaFade) { _mapCG.alpha = 0f; _mapCG.interactable = false; _mapCG.blocksRaycasts = false; }
        if (_mapRT) _mapRT.localScale = keepOriginalScale ? Vector3.Scale(_mapInitialScale, closeEndMul) : Vector3.one;

        if (currentArrow && hideArrowWhenClosed) currentArrow.gameObject.SetActive(false);
        if (guideArrow && !showGuideWhenClosed) guideArrow.gameObject.SetActive(false);
    }

    IEnumerator Co_OpenMap()
    {
        _isOpen = true; _animating = true;

        if (!map.activeSelf) map.SetActive(true);
        if (_mapRT) _mapRT.localScale = keepOriginalScale ? Vector3.Scale(_mapInitialScale, openStartMul) : Vector3.one;
        if (_mapCG && useAlphaFade) { _mapCG.alpha = 0f; _mapCG.interactable = false; _mapCG.blocksRaycasts = false; }

        float t = 0f, d = Mathf.Max(0.01f, openDuration);
        while (t < d)
        {
            float u = t / d, e = 1f - Mathf.Pow(1f - u, 3f);
            if (_mapRT && keepOriginalScale)
            {
                var a = Vector3.Scale(_mapInitialScale, openStartMul);
                var b = Vector3.Scale(_mapInitialScale, openEndMul);
                _mapRT.localScale = Vector3.LerpUnclamped(a, b, e);
            }
            if (_mapCG && useAlphaFade) _mapCG.alpha = Mathf.LerpUnclamped(0f, 1f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }

        if (_mapRT) _mapRT.localScale = keepOriginalScale ? Vector3.Scale(_mapInitialScale, openEndMul) : Vector3.one;
        if (_mapCG && useAlphaFade) { _mapCG.alpha = 1f; _mapCG.interactable = true; _mapCG.blocksRaycasts = true; }

        _animating = false;
        RefreshCurrentLocationArrow();
        RefreshGuideArrowBinding();
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
            if (_mapRT && keepOriginalScale)
            {
                var a = Vector3.Scale(_mapInitialScale, closeStartMul);
                var b = Vector3.Scale(_mapInitialScale, closeEndMul);
                _mapRT.localScale = Vector3.LerpUnclamped(a, b, e);
            }
            if (_mapCG && useAlphaFade) _mapCG.alpha = Mathf.LerpUnclamped(startAlpha, 0f, e);
            t += Time.unscaledDeltaTime; yield return null;
        }

        if (_mapRT) _mapRT.localScale = keepOriginalScale ? Vector3.Scale(_mapInitialScale, closeEndMul) : Vector3.one;
        if (_mapCG && useAlphaFade) _mapCG.alpha = 0f;

        if (map.activeSelf) map.SetActive(false);
        if (mapPanel.activeSelf) mapPanel.SetActive(false);

        _animating = false; _isOpen = false;
        if (currentArrow && hideArrowWhenClosed) currentArrow.gameObject.SetActive(false);
        if (guideArrow && !showGuideWhenClosed) guideArrow.gameObject.SetActive(false);
    }

    void ShowNotification()
    {
        if (!notificationRoot || _notifAnimating) return;
        if (_notifCloseCo != null) StopCoroutine(_notifCloseCo);
        _notifOpen = true;

        notificationRoot.SetActive(true);
        if (_notifRT) _notifRT.localScale = notifStartScale;
        if (_notifCG) { _notifCG.alpha = 0f; _notifCG.interactable = true; _notifCG.blocksRaycasts = notificationBlocksClicks; }
        _notifOpenCo = StartCoroutine(Co_ShowNotification());
    }
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
        if (_notifCG) _notifCG.alpha = 1f;
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
        _notifAnimating = false; _notifOpen = false;
    }

    void RefreshCurrentLocationArrow()
    {
        if (!currentArrow) return;

        var active = SceneManager.GetActiveScene();
        string activeName = active.name;
        int activeIndex = active.buildIndex;

        int matchIdx = -1;

        if (preferBuildIndexMatch && sceneIdMode == SceneIdMode.ByBuildIndex && sceneBuildIndices != null && sceneBuildIndices.Length == menuItems.Length)
        {
            for (int i = 0; i < sceneBuildIndices.Length; i++)
                if (sceneBuildIndices[i] == activeIndex) { matchIdx = i; break; }
        }

        if (matchIdx < 0 && sceneNames != null && sceneNames.Length == menuItems.Length)
        {
            for (int i = 0; i < sceneNames.Length; i++)
            {
                var name = sceneNames[i];
                if (!string.IsNullOrWhiteSpace(name) && SceneNameEqualsRobust(activeName, name)) { matchIdx = i; break; }
            }
        }

        if (matchIdx < 0 || matchIdx >= menuItems.Length || menuItems[matchIdx] == null)
        { currentArrow.gameObject.SetActive(false); _arrowParentRT = null; _arrowBoundIndex = -1; return; }

        var targetRT = menuItems[matchIdx].GetComponent<RectTransform>();
        if (!targetRT) { currentArrow.gameObject.SetActive(false); _arrowParentRT = null; _arrowBoundIndex = -1; return; }

        currentArrow.SetParent(targetRT, worldPositionStays: false);
        _arrowBaseAnchoredPos = arrowAnchorOffset;
        currentArrow.anchoredPosition = _arrowBaseAnchoredPos;
        currentArrow.gameObject.SetActive(true);
        _arrowParentRT = targetRT; _arrowBoundIndex = matchIdx;
    }

    void RefreshGuideArrowBinding()
    {
        if (!guideArrow || menuItems == null || menuItems.Length == 0) return;

        int targetIndex = -1;
        if (PlayerGoPlayerRoom) targetIndex = guideIndexPlayerRoom;
        if (PlayerGoStarest) targetIndex = guideIndexStarest;
        if (PlayerGoShopping) targetIndex = guideIndexShopping;

        if (targetIndex < 0 || targetIndex >= menuItems.Length || menuItems[targetIndex] == null)
        { guideArrow.gameObject.SetActive(false); _guideParentRT = null; _guideBoundIndex = -1; return; }

        var targetRT = menuItems[targetIndex].GetComponent<RectTransform>();
        if (!targetRT)
        { guideArrow.gameObject.SetActive(false); _guideParentRT = null; _guideBoundIndex = -1; return; }

        guideArrow.SetParent(targetRT, worldPositionStays: false);
        _guideBaseAnchoredPos = guideArrowOffset;
        guideArrow.anchoredPosition = _guideBaseAnchoredPos;

        bool shouldShow = showGuideWhenClosed || _isOpen;
        guideArrow.gameObject.SetActive(shouldShow);
        _guideParentRT = targetRT; _guideBoundIndex = targetIndex;
    }

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
            string want = sceneNames[i]; if (string.IsNullOrWhiteSpace(want)) continue;
            string norm = Normalize(want);
            if (nameToIndex.TryGetValue(norm, out int idx)) sceneBuildIndices[i] = idx;
            else
            {
                foreach (var kv in nameToIndex)
                    if (SceneNameEqualsRobust(kv.Key, norm)) { sceneBuildIndices[i] = kv.Value; break; }
            }
        }
    }

    static bool SceneNameEqualsRobust(string a, string b)
    {
        string na = Normalize(a), nb = Normalize(b);
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }
    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = s.Trim();
        return new string(s.Where(ch => ch != ' ' && ch != '\'' && ch != '’').ToArray());
    }

    bool IsTouchingAnyOpenable()
    {
        if (!playerCollider || openMapColliders == null || openMapColliders.Length == 0) return false;
        for (int i = 0; i < openMapColliders.Length; i++)
        {
            var c = openMapColliders[i]; if (!c) continue;
            if (c.IsTouching(playerCollider)) return true;
        }
        return false;
    }

    private static void EnsureHierarchyActive(GameObject leaf)
    {
        if (leaf == null) return;
        Transform t = leaf.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    // ───────── BusDoor 제어 유틸 ─────────
    /// <summary>
    /// BusDoor를 활성/비활성화한다. 출발 시작 시 꺼서 상호작용(F) 및 충돌 유도 방지.
    /// </summary>
    private void SetBusDoorEnabled(bool on)
    {
        if (!busDoor) return;
        if (busDoor.activeSelf != on) busDoor.SetActive(on);
    }
}

[RequireComponent(typeof(RectTransform))]
public class HoverableMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
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
        { _baseColor = _graphic.color; _hasBaseColor = true; }
        else
        { Debug.LogWarning($"[HoverableMenuItem] '{name}'에 Graphic이 없어 색상 연출 비활성화", this); }

        SetScale(_normalScale);
    }

    private void OnEnable()
    {
        if (_graphic != null && _hasBaseColor) _graphic.color = _baseColor;
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

// ─────────────────────────────────────────────────────────────────────────────
// SimpleCameraFollower
//  - Y 고정(lockY), 최소 X 경계(minX) 지원
//  - SmoothDamp 추적, 카메라 Z는 현 위치 유지
// ─────────────────────────────────────────────────────────────────────────────
[DisallowMultipleComponent]
public class SimpleCameraFollower : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = Vector3.zero;
    [Min(0.01f)] public float smoothTime = 0.15f;

    [Header("Y 고정 옵션")]
    public bool lockY = false;
    public float fixedY = 0f;

    [Header("최소 X 경계")]
    public bool useMinX = false;
    public float minX = 0f;

    private Vector3 _vel;

    void LateUpdate()
    {
        if (!target) return;

        var p = target.position + offset;

        // 카메라의 기존 Z 유지
        p.z = transform.position.z;

        // Y 고정
        if (lockY) p.y = fixedY;

        // 최소 X 경계 적용
        if (useMinX && p.x < minX)
            p.x = minX;

        transform.position = Vector3.SmoothDamp(transform.position, p, ref _vel, smoothTime);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ScreenFader
//  - 전역(씬 간 유지) 풀스크린 페이드. 별도 프리팹 없이 자동 생성.
//  - FadeTo(알파, 시간, 색) 코루틴 제공. DontDestroyOnLoad.
// ─────────────────────────────────────────────────────────────────────────────
[DisallowMultipleComponent]
public class ScreenFader : MonoBehaviour
{
    private static ScreenFader _instance;
    public static ScreenFader Instance
    {
        get
        {
            if (_instance == null) Initialize(Color.black);
            return _instance;
        }
    }

    private Canvas _canvas;
    private CanvasGroup _group;
    private Image _image;

    private static Color _initialColor = Color.black;

    public static void Initialize(Color color)
    {
        _initialColor = color;
        if (_instance != null) { _instance.SetColorKeepAlpha(color); return; }

        var go = new GameObject("[ScreenFader]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<ScreenFader>();
        _instance.Build(color);
    }

    private void Build(Color color)
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = Int32.MaxValue;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = true; // 페이드 중 입력 차단

        var imgGO = new GameObject("Overlay");
        imgGO.transform.SetParent(transform, false);
        _image = imgGO.AddComponent<Image>();
        _image.raycastTarget = false;

        var rt = _image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _image.color = new Color(color.r, color.g, color.b, 1f);
    }

    private void SetColorKeepAlpha(Color c)
    {
        if (_image == null) return;
        var a = _image.color.a;
        _image.color = new Color(c.r, c.g, c.b, a);
    }

    public IEnumerator FadeTo(float targetAlpha, float duration, Color? overrideColor = null)
    {
        if (_group == null || _image == null) yield break;

        if (overrideColor.HasValue)
        {
            var c = overrideColor.Value;
            _image.color = new Color(c.r, c.g, c.b, _image.color.a);
        }
        else
        {
            var c = _initialColor;
            _image.color = new Color(c.r, c.g, c.b, _image.color.a);
        }

        _group.blocksRaycasts = true;
        float start = _group.alpha;
        float t = 0f;
        duration = Mathf.Max(0.001f, duration);

        while (t < duration)
        {
            float u = t / duration;
            float e = 1f - Mathf.Pow(1f - u, 3f);
            _group.alpha = Mathf.LerpUnclamped(start, targetAlpha, e);
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        _group.alpha = targetAlpha;
        _group.blocksRaycasts = targetAlpha > 0.001f;
    }
}
