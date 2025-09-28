// Select.cs
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Components;

public class Select : MonoBehaviour
{
    [Header("새 플레이어 이름 입력 패널")]
    [SerializeField] private GameObject creat;

    [Header("슬롯 UI 라벨 (버튼 하단 Text)")]
    [SerializeField] private TMP_Text[] slotText;

    [Header("이름 입력")]
    [SerializeField] private TMP_InputField newPlayerInput; // 유저 입력 필드
    [SerializeField] private TMP_Text newPlayerPreview;     // (선택) 프리뷰 라벨

    [Header("시작/폴백 씬 이름")]
    [SerializeField] private string startSceneName = "Player's Room";

    [Header("빈 슬롯 라벨 유지")]
    [Tooltip("빈 슬롯이면 프리팹/로컬라이즈 기본 라벨을 그대로 둡니다. 끄면 빈 슬롯 라벨을 공백으로 비웁니다.")]
    [SerializeField] private bool leaveEmptySlotTextUntouched = true;

    private bool[] hasSave;          // 각 슬롯 세이브 존재 여부
    private string _pendingName = ""; // 입력 캐시

    // ---------- Unity Lifecycle ----------
    void Awake()
    {
        if (newPlayerInput != null)
        {
            newPlayerInput.onValueChanged.AddListener(OnNameChanged);
            newPlayerInput.onEndEdit.AddListener(OnNameChanged);
        }

        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnDestroy()
    {
        if (newPlayerInput != null)
        {
            newPlayerInput.onValueChanged.RemoveListener(OnNameChanged);
            newPlayerInput.onEndEdit.RemoveListener(OnNameChanged);
        }

        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    void Start()
    {
        if (slotText == null || slotText.Length == 0)
            Debug.LogWarning("[Select] slotText 가 비었습니다. 슬롯 라벨을 연결하세요.");

        hasSave = new bool[Mathf.Max(3, slotText != null ? slotText.Length : 3)];

        if (newPlayerInput != null) _pendingName = newPlayerInput.text?.Trim() ?? "";
        RefreshSlotsUI();
    }

    // ---------- Locale 변경 시: 빈 슬롯만 로컬라이즈 새로고침 ----------
    private void OnLocaleChanged(Locale _)
    {
        if (slotText == null) return;

        for (int i = 0; i < hasSave.Length && i < slotText.Length; i++)
        {
            if (slotText[i] == null) continue;

            var lse = slotText[i].GetComponent<LocalizeStringEvent>();
            if (!hasSave[i])
            {
                // 빈 슬롯: 로컬라이즈 유지/갱신
                if (lse) { lse.enabled = true; lse.RefreshString(); }
                else if (!leaveEmptySlotTextUntouched) slotText[i].text = string.Empty;
            }
            // 세이브 슬롯은 이름 고정이므로 아무것도 하지 않음
        }
    }

    // ---------- 유틸 ----------
    private string GetSlotFilePath(int slot)
    {
        // DataManager에 공개 메서드가 있으면 사용, 없으면 동일 규칙으로 생성
        var dm = DataManager.instance;
        if (dm != null)
        {
            // DataManager에 GetSlotFullPath가 있다면 그걸 쓰세요.
            var mi = typeof(DataManager).GetMethod("GetSlotFullPath");
            if (mi != null) return (string)mi.Invoke(dm, new object[] { slot });

            if (!Directory.Exists(dm.path)) Directory.CreateDirectory(dm.path);
            return Path.Combine(dm.path, $"slot_{slot}.json");
        }
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
            return pd != null ? pd.Name : null;
        }
        catch { return null; }
    }

    private void OnNameChanged(string v)
    {
        _pendingName = (v ?? "").Trim();
    }

    private string GetFinalEnteredName()
    {
        if (!string.IsNullOrWhiteSpace(_pendingName))
            return _pendingName.Trim();

        if (newPlayerPreview != null)
        {
            var t = newPlayerPreview.text?.Trim();
            if (!string.IsNullOrEmpty(t)) return t;
        }
        return "";
    }

    // ---------- UI 갱신 ----------
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
            // 세이브가 있으면: 로컬라이즈 컴포넌트 꺼서 텍스트 고정
            if (lse) lse.enabled = false;

            string name = ReadPlayerNameSafe(file);
            label.text = string.IsNullOrEmpty(name) ? "Player" : name; // 저장된 이름 고정 표기
        }
        else
        {
            // 빈 슬롯: 로컬라이즈 활성화(언어 변경에 따라 자동 갱신)
            if (lse)
            {
                lse.enabled = true;
                lse.RefreshString();
            }
            else if (!leaveEmptySlotTextUntouched)
            {
                label.text = string.Empty; // 로컬라이즈 미사용이면 공백 처리 옵션
            }
        }
    }

    // ---------- 슬롯 선택/생성/진입 ----------
    public void Slot(int number)
    {
        if (number < 0 || number >= hasSave.Length)
        {
            Debug.LogError("[Select] 잘못된 슬롯 인덱스: " + number);
            return;
        }

        DataManager.instance.nowSlot = number;

        if (hasSave[number])
        {
            SafeLoad();
            GoGame();
        }
        else
        {
            if (creat) creat.SetActive(true);
        }
    }

    public void Creat()
    {
        if (creat) creat.SetActive(true);
    }

    public void GoGame()
    {
        int s = DataManager.instance.nowSlot;

        if (s < 0 || s >= hasSave.Length)
        {
            Debug.LogWarning("[Select] 유효한 슬롯이 선택되지 않음.");
            if (creat) creat.SetActive(true);
            return;
        }

        bool exists = File.Exists(GetSlotFilePath(s));

        if (!exists)
        {
            string name = GetFinalEnteredName();
            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogWarning("[Select] 이름이 비어 있습니다. 이름을 입력해 주세요.");
                if (creat) creat.SetActive(true);
                if (newPlayerInput) newPlayerInput.ActivateInputField();
                return;
            }

            DataManager.instance.nowPlayer = new PlayerData
            {
                Name = name.Trim(),
                Level = 1,
                Coin = 0,
                Item = 0,
                Day = 1,
                Scene = startSceneName,
                HasSavedPosition = false
            };

            DataManager.instance.SaveData();
            if (s < hasSave.Length) hasSave[s] = true;

            // 방금 만든 슬롯을 이름 고정 모드로 갱신
            RefreshSingleSlotUI(s);
        }
        else
        {
            SafeLoad();
        }

        if (!string.IsNullOrEmpty(startSceneName))
            SceneManager.LoadScene(startSceneName);
        else
            Debug.LogError("[Select] startSceneName 이 비어 있습니다.");
    }

    // ---------- 로드/삭제 ----------
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

            // 삭제된 슬롯은 다시 로컬라이즈 모드로 복귀
            RefreshSingleSlotUI(number);

            if (DataManager.instance.nowSlot == number)
                DataManager.instance.DataClear();

            Debug.Log("[Select] 슬롯 " + number + " 저장 삭제 완료");
        }
        else
        {
            Debug.Log("[Select] 슬롯 " + number + " 저장 파일이 없거나 삭제할 것이 없음");
        }
    }
}
