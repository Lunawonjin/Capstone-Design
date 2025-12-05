using System;
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

    private Dictionary<string, MissionEntry> missionMap;
    public static MissionPanel Instance { get; private set; }

    // ★ 상태 저장용 변수
    private string _currentMissionText = "";
    private bool _isPanelActive = false;

    // ★ [추가] 1회용 이벤트 체크 변수 ("은하마을로 가보자" 트리거용)
    private bool _hasTriggeredStarestMission = false;

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
            SetMissionPanelActive(false);
        }
        else
        {
            _isPanelActive = true;
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

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 1. 새로운 씬의 UI를 찾아서 연결
        TryAutoBindUI();

        // ---------------------------------------------------------
        // ★ [추가] 1회용 로직: 씬 이름이 "Starest"이고, 아직 실행된 적 없다면
        // ---------------------------------------------------------
        if (scene.name == "Starest" && !_hasTriggeredStarestMission)
        {
            // 텍스트 내용 변경
            _currentMissionText = "은하마을로 가보자";

            // 1회용이므로 플래그를 true로 변경하여 중복 실행 방지
            _hasTriggeredStarestMission = true;

            // 필요하다면 여기서 패널을 강제로 켤 수도 있습니다. (선택사항)
            // _isPanelActive = true; 
        }
        // ---------------------------------------------------------

        // 2. 저장해뒀던(혹은 방금 변경한) 상태를 새로운 UI에 강제 적용
        RestoreState();
    }

    // ★ 저장된 상태를 UI에 복구하는 함수
    private void RestoreState()
    {
        // 텍스트 복구
        if (missionDetailText != null)
        {
            missionDetailText.text = _currentMissionText;
        }

        // 활성/비활성 상태 복구
        if (missionRoot != null) missionRoot.SetActive(_isPanelActive);
        else if (missionPanelImage != null) missionPanelImage.gameObject.SetActive(_isPanelActive);
        else if (missionDetailText != null) missionDetailText.gameObject.SetActive(_isPanelActive);
    }

    private void TryAutoBindUI()
    {
        if (missionPanelImage == null) missionPanelImage = FindUIComponent<Image>(targetPanelImageName);
        if (missionDetailText == null) missionDetailText = FindUIComponent<TMP_Text>(targetDetailTextName);

        if (missionRoot == null && missionPanelImage != null)
        {
            missionRoot = missionPanelImage.gameObject;
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
            ApplyMissionText(entry.detailText);
            SetMissionPanelActive(true);
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
            ApplyMissionText(entry.detailText);
            SetMissionPanelActive(true);
            return true;
        }
        return false;
    }

    public void ShowText(string text)
    {
        ApplyMissionText(text);
        SetMissionPanelActive(true);
    }

    public void Hide()
    {
        SetMissionPanelActive(false);
    }

    private void ApplyMissionText(string text)
    {
        // ★ 현재 텍스트 상태 저장
        _currentMissionText = text ?? string.Empty;

        if (missionDetailText == null) TryAutoBindUI();
        if (missionDetailText != null)
        {
            missionDetailText.text = _currentMissionText;
        }
    }

    private void SetMissionPanelActive(bool active)
    {
        // ★ 현재 활성화 상태 저장
        _isPanelActive = active;

        if (missionRoot == null && missionPanelImage == null) TryAutoBindUI();

        if (missionRoot != null)
        {
            missionRoot.SetActive(active);
        }
        else if (missionPanelImage != null)
        {
            missionPanelImage.gameObject.SetActive(active);
        }
        else if (missionDetailText != null)
        {
            missionDetailText.gameObject.SetActive(active);
        }
    }

    public void RebuildMap() => BuildMissionMap();
}