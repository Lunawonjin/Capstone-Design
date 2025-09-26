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

    [Header("게임 씬 이름(폴백용)")]
    public string gameSceneName = "Player's Room";

    [Header("슬롯 개수")]
    public int slotCount = 3;

    void Awake()
    {
        if (newGamePanel) newGamePanel.SetActive(false);
        if (settingPanel) settingPanel.SetActive(false);
    }

    void Start()
    {
        if (newGameButton) newGameButton.onClick.AddListener(OnClickNewGame);
        if (settingButton) settingButton.onClick.AddListener(OnClickSetting);
        if (exitButton) exitButton.onClick.AddListener(OnClickExit);
        if (loadGameButton) loadGameButton.onClick.AddListener(() => StartCoroutine(Co_OnClickLoadGame()));

        bool hasAny = DataManager.instance.HasAnySave(slotCount);
        if (loadGameButton) loadGameButton.gameObject.SetActive(hasAny);
    }

    void OnClickNewGame()
    {
        if (newGamePanel) newGamePanel.SetActive(true);
        if (settingPanel) settingPanel.SetActive(false);
    }

    void OnClickSetting()
    {
        if (settingPanel) settingPanel.SetActive(true);
        if (newGamePanel) newGamePanel.SetActive(false);
    }

    // ▼ 변경: 코루틴으로 저장된 씬을 우선 로드하고, 포지션 적용까지 호출
    IEnumerator Co_OnClickLoadGame()
    {
        var dm = DataManager.instance;
        bool ok = dm.TryLoadMostRecentSave(slotCount);
        if (!ok)
        {
            Debug.LogWarning("[StartMenu] 로드할 세이브가 없거나 로드 실패. NewGame을 진행하십시오.");
            yield break;
        }

        // 저장된 씬 이름 가져오기
        string target = dm.nowPlayer.Scene;

        // 폴백: 비어 있거나 빌드에 없으면 gameSceneName 사용
        bool hasSavedScene = !string.IsNullOrEmpty(target);
#if UNITY_2021_1_OR_NEWER
        // Unity 6에서도 Application.CanStreamedLevelBeLoaded는 제공됩니다.
        if (!hasSavedScene || !Application.CanStreamedLevelBeLoaded(target))
            target = gameSceneName;
#else
        if (!hasSavedScene)
            target = gameSceneName;
#endif

        // 씬 로드
        AsyncOperation op = SceneManager.LoadSceneAsync(target);
        while (!op.isDone) yield return null;

        // 씬이 로드된 뒤, 저장된 위치 적용(같은 씬이라도 좌표만 적용)
        yield return dm.LoadSavedSceneAndPlacePlayer();
    }

    void OnClickExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
