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
    public GameObject endPanel;
    public Button endYesButton;
    public Button endNoButton;

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
        if (endPanel) endPanel.SetActive(false);
    }

    void Start()
    {
        if (newGameButton) newGameButton.onClick.AddListener(OnClickNewGame);
        if (settingButton) settingButton.onClick.AddListener(OnClickSetting);
        if (exitButton) exitButton.onClick.AddListener(OnClickExitRequest);
        if (loadGameButton) loadGameButton.onClick.AddListener(() => StartCoroutine(Co_OnClickLoadGame()));

        if (endYesButton) endYesButton.onClick.AddListener(OnClickExitConfirm);
        if (endNoButton) endNoButton.onClick.AddListener(OnClickExitCancel);

        RefreshLoadButtonVisibility();
    }

    void OnEnable()
    {
        RefreshLoadButtonVisibility();
    }

    void Update()
    {
        if (endPanel && endPanel.activeInHierarchy)
        {
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            if (!esc && useCancelAxis) esc = Input.GetButtonDown("Cancel");
            if (esc)
            {
                OnClickExitCancel();
            }
        }
    }

    // [수정됨] Select.cs에서 호출할 수 있도록 public으로 변경
    public void RefreshLoadButtonVisibility()
    {
        bool hasAnySave = false;
        if (DataManager.instance != null)
        {
            hasAnySave = DataManager.instance.HasAnySave(slotCount);
        }

        if (loadGameButton)
        {
            loadGameButton.gameObject.SetActive(hasAnySave);
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
            RefreshLoadButtonVisibility();
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

    void OnClickExitConfirm()
    {
        QuitApplication();
    }

    void OnClickExitCancel()
    {
        if (endPanel) endPanel.SetActive(false);
    }

    void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}