using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Components;

public class Select : MonoBehaviour
{
    [Header("슬롯 UI 라벨 (버튼 하단 Text)")]
    [SerializeField] private TMP_Text[] slotText;

    [Header("시작/폴백 씬 이름")]
    [SerializeField] private string startSceneName = "Player's Room";

    [Header("빈 슬롯 라벨 유지")]
    [Tooltip("빈 슬롯이면 프리팹/로컬라이즈 기본 라벨을 그대로 둡니다. 끄면 빈 슬롯 라벨을 공백으로 비웁니다.")]
    [SerializeField] private bool leaveEmptySlotTextUntouched = true;

    private bool[] hasSave;

    void Awake()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    void Start()
    {
        if (slotText == null || slotText.Length == 0)
            Debug.LogWarning("[Select] slotText 가 비었습니다. 슬롯 라벨을 연결하세요.");

        hasSave = new bool[Mathf.Max(3, slotText != null ? slotText.Length : 3)];
        RefreshSlotsUI();
    }

    private void OnLocaleChanged(Locale _)
    {
        if (slotText == null) return;
        for (int i = 0; i < hasSave.Length && i < slotText.Length; i++)
        {
            if (slotText[i] == null) continue;
            var lse = slotText[i].GetComponent<LocalizeStringEvent>();
            if (!hasSave[i])
            {
                if (lse) { lse.enabled = true; lse.RefreshString(); }
                else if (!leaveEmptySlotTextUntouched) slotText[i].text = string.Empty;
            }
        }
    }

    private string GetSlotFilePath(int slot)
    {
        var dm = DataManager.instance;
        if (dm != null)
            return dm.GetSlotFullPath(slot);

        string fallback = Path.Combine(Application.persistentDataPath, "save");
        if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback);
        return Path.Combine(fallback, $"slot_{slot}.json");
    }

    private string ReadPlayerNameSafe(string file)
    {
        try
        {
            string json = File.ReadAllText(file);
            PlayerData pd = JsonUtility.FromJson<PlayerData>(json);
            return pd?.Name;
        }
        catch { return null; }
    }

    private void RefreshSlotsUI()
    {
        for (int i = 0; i < hasSave.Length; i++)
            RefreshSingleSlotUI(i);
    }

    private void RefreshSingleSlotUI(int i)
    {
        string file = GetSlotFilePath(i);
        bool exists = File.Exists(file);
        if (i < hasSave.Length) hasSave[i] = exists;

        if (slotText == null || i >= slotText.Length || slotText[i] == null) return;

        var label = slotText[i];
        var lse = label.GetComponent<LocalizeStringEvent>();

        if (exists)
        {
            // 저장이 존재하지만 이름이 없을 수도 있으므로 "Player" 같은 임의 텍스트를 넣지 않음.
            string name = ReadPlayerNameSafe(file);

            if (!string.IsNullOrEmpty(name))
            {
                if (lse) lse.enabled = false; // 사용자 이름이 있으면 고정 텍스트 사용
                label.text = name;
            }
            else
            {
                // 이름이 없으면 로컬라이즈 기본 라벨로 되돌리거나(있다면) 공백 처리
                if (lse)
                {
                    lse.enabled = true;
                    lse.RefreshString();
                }
                else if (!leaveEmptySlotTextUntouched)
                {
                    label.text = string.Empty;
                }
                // leaveEmptySlotTextUntouched=true 이고 lse가 없다면 프리팹 기본 텍스트 유지
            }
        }
        else
        {
            if (lse)
            {
                lse.enabled = true;
                lse.RefreshString();
            }
            else if (!leaveEmptySlotTextUntouched)
            {
                label.text = string.Empty;
            }
        }
    }

    public void Slot(int number)
    {
        if (number < 0 || number >= hasSave.Length)
        {
            Debug.LogError("[Select] 잘못된 슬롯 인덱스: " + number);
            return;
        }
        DataManager.instance.nowSlot = number;
        GoGame();
    }

    public void GoGame()
    {
        int s = DataManager.instance.nowSlot;

        if (s < 0 || s >= hasSave.Length)
        {
            Debug.LogWarning("[Select] 유효한 슬롯이 선택되지 않음.");
            return;
        }

        bool exists = File.Exists(GetSlotFilePath(s));

        if (!exists)
        {
            try
            {
                // 현재 선택된 로케일을 새 세이브에 반영
                var locale = LocalizationSettings.SelectedLocale;
                string currentLocaleCode = (locale != null) ? locale.Identifier.Code : "ko";

                // 이름 미설정 상태로 바로 시작 (임의의 "Player" 지정 삭제)
                DataManager.instance.nowPlayer = new PlayerData
                {
                    Name = "",                  // 빈 문자열 유지
                    Level = 1,
                    Coin = 0,
                    Item = 0,
                    Day = 1,
                    Scene = startSceneName,
                    HasSavedPosition = false
                };

                DataManager.instance.SetLanguageCode(currentLocaleCode, saveImmediately: false);

                DataManager.instance.SaveData();
                if (s < hasSave.Length) hasSave[s] = true;

                RefreshSingleSlotUI(s);

                if (!string.IsNullOrEmpty(startSceneName))
                    SceneManager.LoadScene(startSceneName);
                else
                    Debug.LogError("[Select] startSceneName 이 비어 있습니다.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Select] 새 플레이어 생성 또는 저장 중 오류 발생: {e.Message}\n{e.StackTrace}");
            }
        }
        else
        {
            SafeLoad();
            string savedScene = DataManager.instance.nowPlayer?.Scene;
            if (!string.IsNullOrEmpty(savedScene))
            {
                StartCoroutine(DataManager.instance.LoadSavedSceneAndPlacePlayer());
            }
            else
            {
                Debug.LogWarning("[Select] 저장 파일에 Scene 정보가 비어 있습니다. startSceneName으로 폴백합니다.");
                if (!string.IsNullOrEmpty(startSceneName))
                    SceneManager.LoadScene(startSceneName);
                else
                    Debug.LogError("[Select] startSceneName 이 비어 있습니다.");
            }
        }
    }

    private void SafeLoad()
    {
        try
        {
            DataManager.instance.LoadData();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Select] 로드 실패: " + e.Message);
            DataManager.instance.DataClear();
        }
    }

    public void DeleteSlot(int number)
    {
        if (number < 0 || number >= hasSave.Length)
        {
            Debug.LogError("[Select] DeleteSlot 잘못된 인덱스: " + number);
            return;
        }

        bool deleted = DataManager.instance.DeleteData(number);

        if (deleted)
        {
            if (number < hasSave.Length) hasSave[number] = false;

            RefreshSingleSlotUI(number);

            if (DataManager.instance.nowSlot == number)
                DataManager.instance.DataClear();

            Debug.Log("[Select] 슬롯 " + number + " 저장 삭제 완료");

            StartMenu startMenu = FindObjectOfType<StartMenu>();
            if (startMenu != null)
                startMenu.RefreshLoadButtonVisibility();
        }
        else
        {
            Debug.Log("[Select] 슬롯 " + number + " 저장 파일이 없거나 삭제할 것이 없음");
        }
    }
}
