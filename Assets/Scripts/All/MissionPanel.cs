using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class MissionPanel : MonoBehaviour
{
    [Serializable]
    public class MissionEntry
    {
        [Header("키 (예: Boss_First_Calling)")]
        public string key;

        [Header("MissionDetail에 표시할 미션 내용")]
        [TextArea(1, 5)]
        public string detailText;
    }

    [Header("미션 패널 UI 자동 연결 설정")]
    public string targetPanelImageName = "MissionPanelImage";
    public string targetDetailTextName = "MissionDetailText";

    [Header("미션 패널 UI 참조 (자동 할당됨)")]
    [SerializeField] private Image missionPanelImage;
    [SerializeField] private TMP_Text missionDetailText;

    [Header("MissionPanel 루트 오브젝트 (옵션)")]
    [Tooltip("비워두면 missionPanelImage.gameObject를 루트로 사용한다.")]
    [SerializeField] private GameObject missionRoot;

    [Header("시작 시 패널 숨기기")]
    [SerializeField] private bool hideOnAwake = true;

    [Header("키-미션 매핑 리스트")]
    [SerializeField] private List<MissionEntry> missions = new List<MissionEntry>();

    [Header("슬라이드 인 애니메이션 설정")]
    [Tooltip("체크 시: 패널이 활성화될 때 왼쪽에서 오른쪽으로 슬라이드 인 연출을 실행")]
    [SerializeField] private bool useSlideInAnimation = true;

    [Tooltip("슬라이드 인 연출 소요 시간(초)")]
    [SerializeField] private float slideDuration = 0.4f;

    [Tooltip("패널이 들어올 때 x축으로 이동할 거리(양수)")]
    [SerializeField] private float slideOffsetX = 500f;

    private Dictionary<string, MissionEntry> missionMap;
    public static MissionPanel Instance { get; private set; }

    // 상태 저장용 변수
    private string _currentMissionText = "";
    private bool _isPanelActive = false;

    // 1회용 이벤트 체크 변수 ("은하마을로 가보자" 트리거용)
    private bool _hasTriggeredStarestMission = false;

    // Day 2 미션 체크 변수
    private bool _hasTriggeredDay2Mission = false;
    private int _lastCheckedDay = -1;

    // 슬라이드 인 애니메이션용 변수
    private RectTransform _panelRect;
    private Vector2 _panelOriginalAnchoredPos;
    private bool _anchoredPosInitialized = false;
    private Coroutine _slideCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        TryAutoBindUI();
        BuildMissionMap();

        if (hideOnAwake)
        {
            SetMissionPanelActive(false, true); // 초기 숨김은 애니메이션 없이
        }
        else
        {
            _isPanelActive = true;
            RestoreState();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        // Day 값 체크하여 Day 2가 되면 미션 표시
        CheckDayMission();
    }

    private void CheckDayMission()
    {
        if (DataManager.instance == null || DataManager.instance.nowPlayer == null)
            return;

        int currentDay = DataManager.instance.nowPlayer.Day;

        // Day가 변경되었을 때만 체크
        if (currentDay != _lastCheckedDay)
        {
            _lastCheckedDay = currentDay;

            // Day 2가 되면 "출근을 하자" 미션 표시
            if (currentDay == 2 && !_hasTriggeredDay2Mission)
            {
                _hasTriggeredDay2Mission = true;
                _currentMissionText = "출근을 하자";

                if (missionDetailText != null)
                {
                    missionDetailText.text = _currentMissionText;
                }

                SetMissionPanelActive(true, false);
                Debug.Log("[MissionPanel] Day 2 미션 활성화: 출근을 하자");
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 1. 새로운 씬의 UI를 찾아서 연결
        TryAutoBindUI();

        // 1회용 로직: 씬 이름이 "Starest"이고, 아직 실행된 적 없다면
        if (scene.name == "Starest" && !_hasTriggeredStarestMission)
        {
            _currentMissionText = "은하마을로 가보자";
            _hasTriggeredStarestMission = true;
        }

        // 2. 저장해뒀던(혹은 방금 변경한) 상태를 새로운 UI에 강제 적용
        RestoreState();
    }

    // 저장된 상태를 UI에 복구하는 함수
    private void RestoreState()
    {
        if (missionDetailText != null)
        {
            missionDetailText.text = _currentMissionText;
        }

        // 현재 텍스트가 비어 있으면 자동으로 패널 숨김
        if (string.IsNullOrWhiteSpace(_currentMissionText))
        {
            SetMissionPanelActive(false, true);
            return;
        }

        if (_isPanelActive)
        {
            SetMissionPanelActive(true, false);
        }
        else
        {
            SetMissionPanelActive(false, true);
        }
    }

    private void TryAutoBindUI()
    {
        // missions 리스트를 순회하면서 detailText가 비어있는지 확인
        bool hasAnyValidMission = false;

        if (missions != null && missions.Count > 0)
        {
            foreach (var mission in missions)
            {
                if (mission != null && !string.IsNullOrWhiteSpace(mission.detailText))
                {
                    hasAnyValidMission = true;
                    break;
                }
            }
        }

        // 유효한 미션이 하나라도 있을 때만 UI를 찾아서 연결
        if (hasAnyValidMission)
        {
            if (missionPanelImage == null)
                missionPanelImage = FindUIComponent<Image>(targetPanelImageName);
            if (missionDetailText == null)
                missionDetailText = FindUIComponent<TMP_Text>(targetDetailTextName);

            if (missionRoot == null && missionPanelImage != null)
            {
                missionRoot = missionPanelImage.gameObject;
            }
        }
        else
        {
            // 유효한 미션이 없으면 UI를 찾아서 비활성화
            if (missionPanelImage == null)
                missionPanelImage = FindUIComponent<Image>(targetPanelImageName);
            if (missionDetailText == null)
                missionDetailText = FindUIComponent<TMP_Text>(targetDetailTextName);

            if (missionPanelImage != null)
            {
                missionPanelImage.gameObject.SetActive(false);
            }
            if (missionDetailText != null)
            {
                missionDetailText.gameObject.SetActive(false);
            }
        }

        InitRectTransform();
    }

    // 패널 RectTransform 초기화 및 원래 위치 기록
    private void InitRectTransform()
    {
        GameObject root = missionRoot != null ? missionRoot :
                          missionPanelImage != null ? missionPanelImage.gameObject : null;

        if (root == null) return;

        _panelRect = root.GetComponent<RectTransform>();
        if (_panelRect != null && !_anchoredPosInitialized)
        {
            _panelOriginalAnchoredPos = _panelRect.anchoredPosition;
            _anchoredPosInitialized = true;
        }
    }

    private T FindUIComponent<T>(string objectName) where T : Component
    {
        if (string.IsNullOrEmpty(objectName)) return null;

        T[] targets = Resources.FindObjectsOfTypeAll<T>();
        foreach (T target in targets)
        {
            if (target.gameObject.scene.IsValid() && target.name == objectName)
            {
                return target;
            }
        }
        return null;
    }

    private void BuildMissionMap()
    {
        missionMap = new Dictionary<string, MissionEntry>(StringComparer.Ordinal);
        for (int i = 0; i < missions.Count; i++)
        {
            var e = missions[i];
            if (e != null && !string.IsNullOrEmpty(e.key)) missionMap[e.key] = e;
        }
    }

    public void ShowByKey(string key)
    {
        if (missionMap == null) BuildMissionMap();

        if (missionMap.TryGetValue(key, out var entry) && entry != null)
        {
            // ★ 미션 내용이 비어 있으면 패널을 무조건 숨김
            if (string.IsNullOrWhiteSpace(entry.detailText))
            {
                _currentMissionText = string.Empty;
                _isPanelActive = false;
                Hide(); // 무조건 Hide() 호출
                return;
            }

            ApplyMissionText(entry.detailText);
            SetMissionPanelActive(true, false);
        }
        else
        {
            Debug.LogWarning("[MissionPanel] 키를 찾을 수 없음: " + key);
        }
    }

    public bool TryShowByKey(string key)
    {
        if (missionMap == null) BuildMissionMap();

        if (missionMap.TryGetValue(key, out var entry) && entry != null)
        {
            // ★ 미션 내용이 비어 있으면 패널을 무조건 숨김
            if (string.IsNullOrWhiteSpace(entry.detailText))
            {
                _currentMissionText = string.Empty;
                _isPanelActive = false;
                Hide(); // 무조건 Hide() 호출
                return false;
            }

            ApplyMissionText(entry.detailText);
            SetMissionPanelActive(true, false);
            return true;
        }
        return false;
    }

    public void ShowText(string text)
    {
        // ★ 직접 문자열로 호출할 때도 비어 있으면 무조건 숨김
        if (string.IsNullOrWhiteSpace(text))
        {
            _currentMissionText = string.Empty;
            _isPanelActive = false;
            Hide(); // 무조건 Hide() 호출
            return;
        }

        ApplyMissionText(text);
        SetMissionPanelActive(true, false);
    }

    public void Hide()
    {
        SetMissionPanelActive(false, false);
    }

    private void ApplyMissionText(string text)
    {
        _currentMissionText = text ?? string.Empty;

        if (missionDetailText == null) TryAutoBindUI();
        if (missionDetailText != null)
        {
            missionDetailText.text = _currentMissionText;
        }
    }

    /// <summary>
    /// 패널 활성/비활성 설정
    /// </summary>
    /// <param name="active">true면 표시, false면 숨김</param>
    /// <param name="instant">true면 애니메이션 없이 즉시 적용</param>
    private void SetMissionPanelActive(bool active, bool instant = false)
    {
        _isPanelActive = active;

        if (missionRoot == null && missionPanelImage == null) TryAutoBindUI();

        GameObject root = missionRoot != null ? missionRoot :
                          missionPanelImage != null ? missionPanelImage.gameObject :
                          missionDetailText != null ? missionDetailText.gameObject : null;

        if (root == null) return;

        // 숨길 때는 코루틴 정리 후 바로 비활성
        if (!active)
        {
            if (_slideCoroutine != null)
            {
                StopCoroutine(_slideCoroutine);
                _slideCoroutine = null;
            }

            if (_panelRect != null && _anchoredPosInitialized)
            {
                _panelRect.anchoredPosition = _panelOriginalAnchoredPos;
            }

            root.SetActive(false);
            return;
        }

        // 보일 때
        root.SetActive(true);
        InitRectTransform();

        if (!useSlideInAnimation || instant || _panelRect == null)
        {
            if (_panelRect != null && _anchoredPosInitialized)
            {
                _panelRect.anchoredPosition = _panelOriginalAnchoredPos;
            }
            return;
        }

        // 코루틴 시작 전에 MissionPanel 게임 오브젝트가 활성화되어 있는지 확인
        if (!this.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("[MissionPanel] MissionPanel 게임 오브젝트가 비활성화되어 있어 코루틴을 시작할 수 없습니다.");
            if (_panelRect != null && _anchoredPosInitialized)
            {
                _panelRect.anchoredPosition = _panelOriginalAnchoredPos;
            }
            return;
        }

        if (_slideCoroutine != null)
        {
            StopCoroutine(_slideCoroutine);
        }
        _slideCoroutine = StartCoroutine(SlideInRoutine());
    }

    private IEnumerator SlideInRoutine()
    {
        if (_panelRect == null || !_anchoredPosInitialized)
        {
            yield break;
        }

        Vector2 endPos = _panelOriginalAnchoredPos;
        Vector2 startPos = endPos + new Vector2(-Mathf.Abs(slideOffsetX), 0f);

        _panelRect.anchoredPosition = startPos;

        float t = 0f;
        float duration = Mathf.Max(0.01f, slideDuration);

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(t / duration);

            float eased = normalized * normalized * (3f - 2f * normalized);

            _panelRect.anchoredPosition = Vector2.Lerp(startPos, endPos, eased);
            yield return null;
        }

        _panelRect.anchoredPosition = endPos;
        _slideCoroutine = null;
    }

    public void RebuildMap() => BuildMissionMap();
}