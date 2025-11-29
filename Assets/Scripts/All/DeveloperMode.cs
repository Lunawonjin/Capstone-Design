// DeveloperMode.cs
// Unity 6 LTS 기준
// 기능
//  - Ctrl + Tab : 개발자 패널 토글
//  - F1         : 현재 진행 중인 대사 강제 스킵(EndDialogue 리플렉션 호출)
//  - F2         : 플레이어 속도 + step (최대 maxSpeed)
//  - F3         : 플레이어 속도 - step (최소 minSpeed)
//  - 개발자 패널 내부 씬 버튼 클릭 시 라벨 텍스트와 같은 씬 로드
//    · 현재 씬과 같으면 로그만 남기고 이동하지 않음
//  - TMP InputField별 "확인" 버튼 지원
//    · 각 버튼은 자기 입력만 DataManager.nowPlayer에 반영
//  - TMP Dropdown별 "확인" 버튼 지원 (True/False, 기본 False)
//    · 각 버튼은 자기 Bool만 DataManager.nowPlayer에 반영
//  - (선택) 전체 적용 버튼도 유지

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class DeveloperMode : MonoBehaviour
{
    [Header("Developer Panel")]
    [Tooltip("Ctrl+Tab으로 켜고 끌 개발자 패널")]
    public GameObject developerPanel;

    [Header("Dialogue Skip")]
    [Tooltip("F1로 대사를 스킵할 DialogueRunnerStringTables 참조(비우면 자동 탐색)")]
    public DialogueRunnerStringTables dialogueRunner;
    public bool autoFindDialogueRunner = true;
    public bool includeInactiveOnFind = true;

    [Header("Player Speed Control")]
    [Tooltip("F2/F3로 속도를 바꿀 PlayerMove 참조(비우면 자동 탐색)")]
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;
    public bool includeInactivePlayerOnFind = true;

    [Tooltip("한 번에 증감할 속도 값")]
    public float speedStep = 5f;

    [Tooltip("최소 속도")]
    public float minSpeed = 3f;

    [Tooltip("최대 속도")]
    public float maxSpeed = 20f;

    [Header("Developer Scene Buttons")]
    [Tooltip("개발자 패널 안의 버튼들을 자동으로 스캔해서 씬 이동 리스너를 붙입니다.")]
    public bool autoHookSceneButtons = true;

    [Tooltip("패널 오픈 때마다 다시 스캔하여 새로 생긴 버튼도 자동 연결")]
    public bool rehookOnPanelOpen = true;

    [Header("Dev Data Edit UI (TMP InputField)")]
    [Tooltip("전체 적용 버튼(선택). 누르면 아래 입력값/드롭다운 전부를 DataManager.nowPlayer에 반영")]
    public Button applyDataButton;

    [Header("String Fields")]
    public TMP_InputField inputName;
    [Tooltip("이름 확인 버튼(선택). 누르면 Name만 적용")]
    public Button applyNameButton;

    [Header("Int Fields")]
    public TMP_InputField inputDay;
    public Button applyDayButton;

    public TMP_InputField inputCoin;
    public Button applyCoinButton;

    public TMP_InputField inputSolFriendShip;
    public Button applySolFriendShipButton;

    public TMP_InputField inputSaltFriendShip;
    public Button applySaltFriendShipButton;

    public TMP_InputField inputRyuFriendShip;
    public Button applyRyuFriendShipButton;

    public TMP_InputField inputWhiteFriendShip;
    public Button applyWhiteFriendShipButton;

    [Header("Bool Fields (TMP Dropdown True/False)")]
    public TMP_Dropdown ddStartGame;
    public Button applyStartGameButton;

    public TMP_Dropdown ddCanFirstSleep;
    public Button applyCanFirstSleepButton;

    public TMP_Dropdown ddStarestFirstVisit;
    public Button applyStarestFirstVisitButton;

    public TMP_Dropdown ddSolTodayTalk;
    public Button applySolTodayTalkButton;

    public TMP_Dropdown ddSaltTodayTalk;
    public Button applySaltTodayTalkButton;

    public TMP_Dropdown ddRyuTodayTalk;
    public Button applyRyuTodayTalkButton;

    public TMP_Dropdown ddWhiteTodayTalk;
    public Button applyWhiteTodayTalkButton;

    public TMP_Dropdown ddSolFirstMeet;
    public Button applySolFirstMeetButton;

    public TMP_Dropdown ddSaltFirstMeet;
    public Button applySaltFirstMeetButton;

    public TMP_Dropdown ddRyuFirstMeet;
    public Button applyRyuFirstMeetButton;

    public TMP_Dropdown ddWhiteFirstMeet;
    public Button applyWhiteFirstMeetButton;

    public TMP_Dropdown ddDiaryOpen;
    public Button applyDiaryOpenButton;

    [Tooltip("Awake에서 확인 버튼 리스너 자동 연결")]
    public bool autoHookApplyButtons = true;

    // True/False 옵션 텍스트
    [Header("Bool Dropdown Options")]
    public string boolFalseLabel = "False";
    public string boolTrueLabel = "True";

    private readonly HashSet<int> _wiredSceneButtons = new();
    private bool _applyAllButtonHooked = false;
    private bool _applySinglesHooked = false;

    private void Awake()
    {
        if (developerPanel != null)
            developerPanel.SetActive(false);

        if (autoFindDialogueRunner && dialogueRunner == null)
            FindDialogueRunner();

        if (autoFindPlayerMove && playerMove == null)
            FindPlayerMove();

        if (autoHookSceneButtons)
            HookSceneButtons();

        // Bool 드롭다운 기본 세팅(False/True, 기본 False)
        SetupAllBoolDropdowns();

        if (autoHookApplyButtons)
        {
            HookApplyAllButtonOnce();
            HookApplySingleButtonsOnce();
        }
    }

    private void Update()
    {
        HandleDeveloperPanelToggle();
        HandleDialogueSkip();
        HandlePlayerSpeedControl_F2F3();
    }

    private void HandleDeveloperPanelToggle()
    {
        bool ctrlHeld =
            Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);

        if (ctrlHeld && Input.GetKeyDown(KeyCode.Tab))
        {
            if (developerPanel == null)
            {
                Debug.LogWarning("[DeveloperMode] developerPanel is null.");
                return;
            }

            bool nextState = !developerPanel.activeSelf;
            developerPanel.SetActive(nextState);

            if (nextState)
            {
                if (autoHookSceneButtons && rehookOnPanelOpen)
                    HookSceneButtons();

                SetupAllBoolDropdowns();

                if (autoHookApplyButtons)
                {
                    HookApplyAllButtonOnce();
                    HookApplySingleButtonsOnce();
                }
            }
        }
    }

    private void HandleDialogueSkip()
    {
        if (!Input.GetKeyDown(KeyCode.F1)) return;

        if (dialogueRunner == null && autoFindDialogueRunner)
            FindDialogueRunner();

        if (dialogueRunner == null)
        {
            Debug.LogWarning("[DeveloperMode] DialogueRunnerStringTables not found.");
            return;
        }

        if (TryInvokeAnyPublicSkip(dialogueRunner))
            return;

        if (TryInvokePrivateEndDialogue(dialogueRunner))
            return;

        dialogueRunner.gameObject.SetActive(false);
        if (dialogueRunner.playerMove != null)
            dialogueRunner.playerMove.controlEnabled = true;

        Debug.Log("[DeveloperMode] Fallback skip: disabled dialogue runner.");
    }

    private void HandlePlayerSpeedControl_F2F3()
    {
        if (Input.GetKeyDown(KeyCode.F2))
        {
            AdjustPlayerSpeed(+speedStep);
        }
        else if (Input.GetKeyDown(KeyCode.F3))
        {
            AdjustPlayerSpeed(-speedStep);
        }
    }

    private void AdjustPlayerSpeed(float delta)
    {
        if (playerMove == null && autoFindPlayerMove)
            FindPlayerMove();

        if (playerMove == null)
        {
            Debug.LogWarning("[DeveloperMode] PlayerMove not found.");
            return;
        }

        float before = playerMove.moveSpeed;
        float after = Mathf.Clamp(before + delta, minSpeed, maxSpeed);

        playerMove.moveSpeed = after;

        Debug.Log($"[DeveloperMode] Player moveSpeed {before} -> {after}");
    }

    private void FindDialogueRunner()
    {
        dialogueRunner = includeInactiveOnFind
            ? FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include)
            : FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Exclude);
    }

    private void FindPlayerMove()
    {
        playerMove = includeInactivePlayerOnFind
            ? FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include)
            : FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Exclude);
    }

    private bool TryInvokeAnyPublicSkip(DialogueRunnerStringTables runner)
    {
        string[] candidates =
        {
            "SkipAll",
            "SkipDialogue",
            "ForceSkip",
            "ForceEnd",
            "End",
            "EndDialogue"
        };

        Type t = runner.GetType();
        for (int i = 0; i < candidates.Length; i++)
        {
            MethodInfo m = t.GetMethod(
                candidates[i],
                BindingFlags.Instance | BindingFlags.Public);

            if (m == null) continue;
            if (m.GetParameters().Length != 0) continue;

            m.Invoke(runner, null);
            Debug.Log($"[DeveloperMode] Invoked public skip: {candidates[i]}()");
            return true;
        }

        return false;
    }

    private bool TryInvokePrivateEndDialogue(DialogueRunnerStringTables runner)
    {
        Type t = runner.GetType();
        MethodInfo m = t.GetMethod(
            "EndDialogue",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (m == null) return false;
        if (m.GetParameters().Length != 0) return false;

        m.Invoke(runner, null);
        Debug.Log("[DeveloperMode] Invoked private EndDialogue() via reflection.");
        return true;
    }

    // =========================
    // Developer panel scene buttons
    // =========================
    private void HookSceneButtons()
    {
        if (developerPanel == null)
        {
            Debug.LogWarning("[DeveloperMode] developerPanel is null. Scene buttons not hooked.");
            return;
        }

        Button[] buttons = developerPanel.GetComponentsInChildren<Button>(true);
        if (buttons == null || buttons.Length == 0) return;

        for (int i = 0; i < buttons.Length; i++)
        {
            Button btn = buttons[i];
            if (btn == null) continue;

            int id = btn.GetInstanceID();
            if (_wiredSceneButtons.Contains(id)) continue;

            _wiredSceneButtons.Add(id);

            btn.onClick.AddListener(() =>
            {
                string targetScene = GetButtonLabelText(btn);
                if (string.IsNullOrWhiteSpace(targetScene))
                {
                    Debug.LogWarning("[DeveloperMode] Button label is empty. Cannot load scene.");
                    return;
                }

                targetScene = targetScene.Trim();
                string currentScene = SceneManager.GetActiveScene().name;

                if (string.Equals(currentScene, targetScene, StringComparison.Ordinal))
                {
                    Debug.Log($"[DeveloperMode] Already in scene '{currentScene}'.");
                    return;
                }

                if (!Application.CanStreamedLevelBeLoaded(targetScene))
                {
                    Debug.LogError($"[DeveloperMode] Scene '{targetScene}' is not in Build Settings or cannot be loaded.");
                    return;
                }

                SceneManager.LoadScene(targetScene);
            });
        }
    }

    private string GetButtonLabelText(Button btn)
    {
        if (btn == null) return "";

        TMP_Text tmp = btn.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
            return tmp.text;

        Text legacy = btn.GetComponentInChildren<Text>(true);
        if (legacy != null && !string.IsNullOrWhiteSpace(legacy.text))
            return legacy.text;

        return "";
    }

    // =========================
    // Dev InputFields / Dropdown apply to DataManager
    // =========================
    private DataManager GetDataManagerSafe()
    {
        DataManager dm = DataManager.instance;
        if (dm == null)
            dm = FindFirstObjectByType<DataManager>(FindObjectsInactive.Include);
        return dm;
    }

    private void HookApplyAllButtonOnce()
    {
        if (_applyAllButtonHooked) return;
        if (applyDataButton == null) return;

        applyDataButton.onClick.RemoveListener(ApplyAllInputsToDataManager);
        applyDataButton.onClick.AddListener(ApplyAllInputsToDataManager);

        _applyAllButtonHooked = true;
    }

    private void HookApplySingleButtonsOnce()
    {
        if (_applySinglesHooked) return;

        // String/Int
        HookSingle(applyNameButton, ApplyNameOnly);
        HookSingle(applyDayButton, ApplyDayOnly);
        HookSingle(applyCoinButton, ApplyCoinOnly);
        HookSingle(applySolFriendShipButton, ApplySolFriendShipOnly);
        HookSingle(applySaltFriendShipButton, ApplySaltFriendShipOnly);
        HookSingle(applyRyuFriendShipButton, ApplyRyuFriendShipOnly);
        HookSingle(applyWhiteFriendShipButton, ApplyWhiteFriendShipOnly);

        // Bool
        HookSingle(applyStartGameButton, ApplyStartGameOnly);
        HookSingle(applyCanFirstSleepButton, ApplyCanFirstSleepOnly);
        HookSingle(applyStarestFirstVisitButton, ApplyStarestFirstVisitOnly);

        HookSingle(applySolTodayTalkButton, ApplySolTodayTalkOnly);
        HookSingle(applySaltTodayTalkButton, ApplySaltTodayTalkOnly);
        HookSingle(applyRyuTodayTalkButton, ApplyRyuTodayTalkOnly);
        HookSingle(applyWhiteTodayTalkButton, ApplyWhiteTodayTalkOnly);

        HookSingle(applySolFirstMeetButton, ApplySolFirstMeetOnly);
        HookSingle(applySaltFirstMeetButton, ApplySaltFirstMeetOnly);
        HookSingle(applyRyuFirstMeetButton, ApplyRyuFirstMeetOnly);
        HookSingle(applyWhiteFirstMeetButton, ApplyWhiteFirstMeetOnly);

        HookSingle(applyDiaryOpenButton, ApplyDiaryOpenOnly);

        _applySinglesHooked = true;
    }

    private void HookSingle(Button btn, Action action)
    {
        if (btn == null || action == null) return;
        btn.onClick.RemoveListener(() => action());
        btn.onClick.AddListener(() => action());
    }

    private void ApplyAllInputsToDataManager()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null)
        {
            Debug.LogWarning("[DeveloperMode] DataManager not found.");
            return;
        }

        // String/Int
        ApplyNameOnly();
        ApplyDayOnly();
        ApplyCoinOnly();
        ApplySolFriendShipOnly();
        ApplySaltFriendShipOnly();
        ApplyRyuFriendShipOnly();
        ApplyWhiteFriendShipOnly();

        // Bool
        ApplyStartGameOnly();
        ApplyCanFirstSleepOnly();
        ApplyStarestFirstVisitOnly();

        ApplySolTodayTalkOnly();
        ApplySaltTodayTalkOnly();
        ApplyRyuTodayTalkOnly();
        ApplyWhiteTodayTalkOnly();

        ApplySolFirstMeetOnly();
        ApplySaltFirstMeetOnly();
        ApplyRyuFirstMeetOnly();
        ApplyWhiteFirstMeetOnly();

        ApplyDiaryOpenOnly();

        Debug.Log("[DeveloperMode] Applied all InputField/Dropdown values to DataManager.");
    }

    // -------- String --------
    private void ApplyNameOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        if (inputName == null) return;

        string nameVal = inputName.text != null ? inputName.text.Trim() : "";
        dm.SetPlayerName(nameVal);

        Debug.Log($"[DeveloperMode] Applied Name = '{nameVal}'");
    }

    // -------- Int --------
    private void ApplyDayOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        ApplyIntField(inputDay, v =>
        {
            int day = Mathf.Max(1, v);
            dm.SetDay(day);
            Debug.Log($"[DeveloperMode] Applied Day = {day}");
        }, "Day");
    }

    private void ApplyCoinOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        ApplyIntField(inputCoin, v =>
        {
            int coin = Mathf.Max(0, v);
            dm.SetCoin(coin);
            Debug.Log($"[DeveloperMode] Applied Coin = {coin}");
        }, "Coin");
    }

    private void ApplySolFriendShipOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        ApplyIntField(inputSolFriendShip, v =>
        {
            int val = Mathf.Max(0, v);
            dm.nowPlayer.Sol_FriendShip = val;
            Debug.Log($"[DeveloperMode] Applied Sol_FriendShip = {val}");
        }, "Sol_FriendShip");
    }

    private void ApplySaltFriendShipOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        ApplyIntField(inputSaltFriendShip, v =>
        {
            int val = Mathf.Max(0, v);
            dm.nowPlayer.Salt_FriendShip = val;
            Debug.Log($"[DeveloperMode] Applied Salt_FriendShip = {val}");
        }, "Salt_FriendShip");
    }

    private void ApplyRyuFriendShipOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        ApplyIntField(inputRyuFriendShip, v =>
        {
            int val = Mathf.Max(0, v);
            dm.nowPlayer.Ryu_FriendShip = val;
            Debug.Log($"[DeveloperMode] Applied Ryu_FriendShip = {val}");
        }, "Ryu_FriendShip");
    }

    private void ApplyWhiteFriendShipOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        ApplyIntField(inputWhiteFriendShip, v =>
        {
            int val = Mathf.Max(0, v);
            dm.nowPlayer.White_FriendShip = val;
            Debug.Log($"[DeveloperMode] Applied White_FriendShip = {val}");
        }, "White_FriendShip");
    }

    private void ApplyIntField(TMP_InputField field, Action<int> setter, string label)
    {
        if (field == null || setter == null) return;

        string raw = field.text != null ? field.text.Trim() : "";
        if (string.IsNullOrEmpty(raw)) return;

        if (int.TryParse(raw, out int value))
        {
            setter(value);
        }
        else
        {
            Debug.LogWarning($"[DeveloperMode] '{label}' input is not a valid int: '{raw}'");
        }
    }

    // -------- Bool (Dropdown) --------
    private void SetupAllBoolDropdowns()
    {
        SetupBoolDropdown(ddStartGame);
        SetupBoolDropdown(ddCanFirstSleep);
        SetupBoolDropdown(ddStarestFirstVisit);

        SetupBoolDropdown(ddSolTodayTalk);
        SetupBoolDropdown(ddSaltTodayTalk);
        SetupBoolDropdown(ddRyuTodayTalk);
        SetupBoolDropdown(ddWhiteTodayTalk);

        SetupBoolDropdown(ddSolFirstMeet);
        SetupBoolDropdown(ddSaltFirstMeet);
        SetupBoolDropdown(ddRyuFirstMeet);
        SetupBoolDropdown(ddWhiteFirstMeet);

        SetupBoolDropdown(ddDiaryOpen);
    }

    private void SetupBoolDropdown(TMP_Dropdown dd)
    {
        if (dd == null) return;

        // 옵션이 2개 미만이면 False/True로 자동 구성
        if (dd.options == null || dd.options.Count < 2)
        {
            dd.options = new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData(boolFalseLabel),
                new TMP_Dropdown.OptionData(boolTrueLabel)
            };
            dd.RefreshShownValue();
        }

        // 값이 이상하면 기본 False로
        if (dd.value < 0 || dd.value > 1)
            dd.value = 0;
    }

    private bool ReadBoolFromDropdown(TMP_Dropdown dd)
    {
        if (dd == null) return false;
        if (dd.value == 1) return true;
        return false;
    }

    private void ApplyStartGameOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddStartGame);
        dm.nowPlayer.StartGame = val;
        Debug.Log($"[DeveloperMode] Applied StartGame = {val}");
    }

    private void ApplyCanFirstSleepOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddCanFirstSleep);
        dm.nowPlayer.CanFirstSleep = val;
        Debug.Log($"[DeveloperMode] Applied CanFirstSleep = {val}");
    }

    private void ApplyStarestFirstVisitOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddStarestFirstVisit);
        dm.nowPlayer.Starest_First_Visit = val;
        Debug.Log($"[DeveloperMode] Applied Starest_First_Visit = {val}");
    }

    private void ApplySolTodayTalkOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddSolTodayTalk);
        dm.nowPlayer.Sol_Today_Talk = val;
        Debug.Log($"[DeveloperMode] Applied Sol_Today_Talk = {val}");
    }

    private void ApplySaltTodayTalkOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddSaltTodayTalk);
        dm.nowPlayer.Salt_Today_Talk = val;
        Debug.Log($"[DeveloperMode] Applied Salt_Today_Talk = {val}");
    }

    private void ApplyRyuTodayTalkOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddRyuTodayTalk);
        dm.nowPlayer.Ryu_Today_Talk = val;
        Debug.Log($"[DeveloperMode] Applied Ryu_Today_Talk = {val}");
    }

    private void ApplyWhiteTodayTalkOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddWhiteTodayTalk);
        dm.nowPlayer.White_Today_Talk = val;
        Debug.Log($"[DeveloperMode] Applied White_Today_Talk = {val}");
    }

    private void ApplySolFirstMeetOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddSolFirstMeet);
        dm.nowPlayer.Sol_First_Meet = val;
        Debug.Log($"[DeveloperMode] Applied Sol_First_Meet = {val}");
    }

    private void ApplySaltFirstMeetOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddSaltFirstMeet);
        dm.nowPlayer.Salt_First_Meet = val;
        Debug.Log($"[DeveloperMode] Applied Salt_First_Meet = {val}");
    }

    private void ApplyRyuFirstMeetOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddRyuFirstMeet);
        dm.nowPlayer.Ryu_First_Meet = val;
        Debug.Log($"[DeveloperMode] Applied Ryu_First_Meet = {val}");
    }

    private void ApplyWhiteFirstMeetOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddWhiteFirstMeet);
        dm.nowPlayer.White_First_Meet = val;
        Debug.Log($"[DeveloperMode] Applied White_First_Meet = {val}");
    }

    private void ApplyDiaryOpenOnly()
    {
        DataManager dm = GetDataManagerSafe();
        if (dm == null || dm.nowPlayer == null) return;

        bool val = ReadBoolFromDropdown(ddDiaryOpen);
        dm.nowPlayer.DiaryOpen = val;
        Debug.Log($"[DeveloperMode] Applied DiaryOpen = {val}");
    }
}
