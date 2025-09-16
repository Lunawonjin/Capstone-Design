using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 시작 메뉴(메인 메뉴) 관리:
/// - NewGame / Setting / Exit / LoadGame 버튼 처리
/// - 세이브가 하나라도 있으면 LoadGame 버튼 활성화
/// - NewGame/Setting 클릭 시 각 패널 오브젝트 토글
/// </summary>
public class StartMenu : MonoBehaviour
{
    [Header("버튼 참조")]
    public Button newGameButton;
    public Button settingButton;
    public Button loadGameButton;    // 세이브 존재 시에만 활성화
    public Button exitButton;

    [Header("패널 오브젝트")]
    public GameObject newGamePanel;  // 예: 슬롯 선택 + 이름 입력 UI(Select.cs가 붙은 오브젝트)
    public GameObject settingPanel;  // 예: 사운드/그래픽 설정 패널

    [Header("게임 씬 이름")]
    public string gameSceneName = "Player's Room"; // 실제 게임 씬 이름

    [Header("슬롯 개수")]
    public int slotCount = 3; // 현재 3개 슬롯

    private void Awake()
    {
        // 패널은 기본적으로 비활성화
        if (newGamePanel != null) newGamePanel.SetActive(false);
        if (settingPanel != null) settingPanel.SetActive(false);
    }

    private void Start()
    {
        // 버튼 리스너 연결 (인스펙터에서 연결해도 됨)
        if (newGameButton != null) newGameButton.onClick.AddListener(OnClickNewGame);
        if (settingButton != null) settingButton.onClick.AddListener(OnClickSetting);
        if (exitButton != null) exitButton.onClick.AddListener(OnClickExit);
        if (loadGameButton != null) loadGameButton.onClick.AddListener(OnClickLoadGame);

        // 세이브가 하나라도 있으면 LoadGame 버튼 활성화
        bool hasAny = DataManager.instance.HasAnySave(slotCount);
        if (loadGameButton != null) loadGameButton.gameObject.SetActive(hasAny);
    }

    /// <summary>
    /// NewGame 버튼: 새 게임 패널 열기(Select 화면 등)
    /// </summary>
    private void OnClickNewGame()
    {
        if (newGamePanel != null) newGamePanel.SetActive(true);
        if (settingPanel != null) settingPanel.SetActive(false);
    }

    /// <summary>
    /// Setting 버튼: 설정 패널 열기
    /// </summary>
    private void OnClickSetting()
    {
        if (settingPanel != null) settingPanel.SetActive(true);
        if (newGamePanel != null) newGamePanel.SetActive(false);
    }

    /// <summary>
    /// LoadGame 버튼: 가장 최근 저장 슬롯을 찾아 로드 후 게임 씬으로 이동
    /// </summary>
    private void OnClickLoadGame()
    {
        bool ok = DataManager.instance.TryLoadMostRecentSave(slotCount);
        if (!ok)
        {
            Debug.LogWarning("[StartMenu] 로드할 세이브가 없거나 로드 실패. NewGame을 진행하세요.");
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Exit 버튼: 애플리케이션 종료(에디터에선 플레이 중지)
    /// </summary>
    private void OnClickExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
