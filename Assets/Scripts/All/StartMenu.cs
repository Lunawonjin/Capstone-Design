// StartMenu.cs
// Unity 6 (LTS)
// 변경점: EndPanel이 활성화된 동안 ESC(또는 Cancel 축)를 누르면
//         OnClickExitCancel()을 호출해 No와 동일하게 EndPanel만 닫습니다.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class StartMenu : MonoBehaviour
{
    [Header("버튼 참조")]
    public Button newGameButton;
    public Button settingButton;
    public Button loadGameButton;
    public Button exitButton;

    [Header("패널 오브젝트")]
    public GameObject newGamePanel;
    public GameObject settingPanel;

    [Header("종료 확인 패널(EndPanel)")]
    public GameObject endPanel;      // Exit 클릭 시 띄울 확인 패널
    public Button endYesButton;      // EndPanel의 Yes
    public Button endNoButton;       // EndPanel의 No

    [Header("ESC 입력 옵션")]
    [Tooltip("Input.GetButtonDown(\"Cancel\")도 함께 인식 (기본 ESC 매핑)")]
    public bool useCancelAxis = true;

    [Header("게임 씬 이름(폴백용)")]
    public string gameSceneName = "Player's Room";

    [Header("슬롯 개수")]
    public int slotCount = 3;

    void Awake()
    {
        if (newGamePanel) newGamePanel.SetActive(false);
        if (settingPanel) settingPanel.SetActive(false);
        if (endPanel) endPanel.SetActive(false); // EndPanel은 기본 비활성화
    }

    void Start()
    {
        if (newGameButton) newGameButton.onClick.AddListener(OnClickNewGame);
        if (settingButton) settingButton.onClick.AddListener(OnClickSetting);
        if (exitButton) exitButton.onClick.AddListener(OnClickExitRequest);
        if (loadGameButton) loadGameButton.onClick.AddListener(() => StartCoroutine(Co_OnClickLoadGame()));

        if (endYesButton) endYesButton.onClick.AddListener(OnClickExitConfirm);
        if (endNoButton) endNoButton.onClick.AddListener(OnClickExitCancel);

        bool hasAny = false;
        if (DataManager.instance != null)
        {
            hasAny = DataManager.instance.HasAnySave(slotCount);
        }
        if (loadGameButton) loadGameButton.gameObject.SetActive(hasAny);
    }

    void Update()
    {
        // EndPanel이 표시 중일 때만 ESC를 소비하여 "No" 동작 수행
        if (endPanel && endPanel.activeInHierarchy)
        {
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            if (!esc && useCancelAxis) esc = Input.GetButtonDown("Cancel");
            if (esc)
            {
                OnClickExitCancel();   // No와 동일: EndPanel만 닫기
            }
        }
    }

    void OnClickNewGame()
    {
        if (newGamePanel) newGamePanel.SetActive(true);
        if (settingPanel) settingPanel.SetActive(false);
        if (endPanel) endPanel.SetActive(false);
    }

    void OnClickSetting()
    {
        if (settingPanel) settingPanel.SetActive(true);
        if (newGamePanel) newGamePanel.SetActive(false);
        if (endPanel) endPanel.SetActive(false);
    }

    // 저장된 씬을 우선 로드하고, 포지션 적용까지 호출
    IEnumerator Co_OnClickLoadGame()
    {
        var dm = DataManager.instance;
        if (dm == null)
        {
            Debug.LogWarning("[StartMenu] DataManager가 없습니다.");
            yield break;
        }

        bool ok = dm.TryLoadMostRecentSave(slotCount);
        if (!ok)
        {
            Debug.LogWarning("[StartMenu] 로드할 세이브가 없거나 로드 실패. NewGame을 진행하십시오.");
            yield break;
        }

        string target = dm.nowPlayer != null ? dm.nowPlayer.Scene : null;

        bool hasSavedScene = !string.IsNullOrEmpty(target);
#if UNITY_2021_1_OR_NEWER
        if (!hasSavedScene || !Application.CanStreamedLevelBeLoaded(target))
            target = gameSceneName;
#else
        if (!hasSavedScene)
            target = gameSceneName;
#endif

        AsyncOperation op = SceneManager.LoadSceneAsync(target);
        while (!op.isDone) yield return null;

        yield return dm.LoadSavedSceneAndPlacePlayer();
    }

    // Exit 버튼: EndPanel 표시
    void OnClickExitRequest()
    {
        if (endPanel)
        {
            if (newGamePanel) newGamePanel.SetActive(false);
            if (settingPanel) settingPanel.SetActive(false);
            endPanel.SetActive(true);
        }
        else
        {
            QuitApplication();
        }
    }

    // EndPanel -> Yes
    void OnClickExitConfirm()
    {
        QuitApplication();
    }

    // EndPanel -> No
    void OnClickExitCancel()
    {
        if (endPanel) endPanel.SetActive(false);
    }

    // 공통 종료 처리
    void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
