// Select.cs
// Unity 6 (LTS)
// 의존성 제거 버전: DataManager 없이도 동작
// - 저장/로드(IO)를 Select 내부에서 직접 수행
// - 저장이 있으면 저장된 Scene으로 이동
// - 저장이 없으면 이름 입력 → startSceneName으로 시작
// - DataManager가 "있다면" 일부 상태(nowSlot 등)를 맞춰주는 호환 코드 포함(선택적)
// - Unity 6 API: FindFirstObjectByType 사용, 구버전은 FindObjectOfType(true)로 폴백

using System;
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

    // 내부 상태
    private bool[] hasSave;            // 각 슬롯 세이브 존재 여부
    private string _pendingName = "";  // 입력 캐시
    private int _selectedSlot = -1;    // 버튼에서 선택한 슬롯 인덱스(정확한 동작을 위해 이벤트 연결 권장)

    // ------------------------------------------------------------
    // PlayerData 미존재 환경 대비 간이 미러(structure만 동일)
    // 프로젝트에 이미 PlayerData가 있다면 이 내부 클래슨 무시됨.
    // ------------------------------------------------------------
    [Serializable]
    private class PD
    {
        public string Name;
        public int Level;
        public int Coin;
        public int Day;
        public int Item;

        public float Px, Py, Pz;
        public bool HasSavedPosition;

        public string Scene;

        public int Weekday;
        public string Language;

        public bool Sol_First_Meet;
        public bool Salt_First_Meet;
        public bool Ryu_First_Meet;
        public bool White_First_Meet;

        public PD()
        {
            Name = "";
            Level = 1;
            Coin = 0;
            Day = 1;
            Item = 0;
            Px = Py = Pz = 0f;
            HasSavedPosition = false;
            Scene = "";
            Weekday = 1;
            Language = "ko";
            Sol_First_Meet = Salt_First_Meet = Ryu_First_Meet = White_First_Meet = false;
        }
    }

    // ------------------------------------------------------------
    // 저장/로드(IO) 유틸 (DataManager 없이 순수 파일 접근)
    // ------------------------------------------------------------
    private static string SaveDir
    {
        get
        {
            string dir = Path.Combine(Application.persistentDataPath, "save");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string SlotPath(int slot) => Path.Combine(SaveDir, $"slot_{slot}.json");

    private static bool SlotExists(int slot) => File.Exists(SlotPath(slot));

    private static bool TryReadPD(int slot, out PD pd)
    {
        pd = null;
        string f = SlotPath(slot);
        if (!File.Exists(f)) return false;
        try
        {
            pd = JsonUtility.FromJson<PD>(File.ReadAllText(f));
            if (pd == null) return false;
            return true;
        }
        catch { return false; }
    }

    private static bool TryWritePD(int slot, PD pd)
    {
        try
        {
            string f = SlotPath(slot);
            File.WriteAllText(f, JsonUtility.ToJson(pd, false));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[Select] 저장 실패: " + e.Message);
            return false;
        }
    }

    private static string ReadPlayerNameFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var pd = JsonUtility.FromJson<PD>(File.ReadAllText(filePath));
            return pd?.Name;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------
    // Unity 6 호환 DataManager 탐색 헬퍼(있으면 사용, 없어도 동작)
    // ------------------------------------------------------------
    private static DataManager FindDataManager()
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<DataManager>(FindObjectsInactive.Include);
#else
        return UnityEngine.Object.FindObjectOfType<DataManager>(true);
#endif
    }

    // ------------------------------------------------------------
    // Unity Lifecycle
    // ------------------------------------------------------------
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

    // ------------------------------------------------------------
    // Locale 변경 시: 빈 슬롯만 로컬라이즈 새로고침
    // ------------------------------------------------------------
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

    // ------------------------------------------------------------
    // 입력/라벨 유틸
    // ------------------------------------------------------------
    private void OnNameChanged(string v) => _pendingName = (v ?? "").Trim();

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

    // ------------------------------------------------------------
    // UI 갱신
    // ------------------------------------------------------------
    private void RefreshSlotsUI()
    {
        for (int i = 0; i < hasSave.Length; i++)
            RefreshSingleSlotUI(i);
    }

    private void RefreshSingleSlotUI(int i)
    {
        string file = SlotPath(i);
        bool exists = File.Exists(file);
        if (i < hasSave.Length) hasSave[i] = exists;

        if (slotText == null || i >= slotText.Length || slotText[i] == null) return;

        var label = slotText[i];
        var lse = label.GetComponent<LocalizeStringEvent>();

        if (exists)
        {
            if (lse) lse.enabled = false;
            string name = ReadPlayerNameFromFile(file);
            label.text = string.IsNullOrEmpty(name) ? "Player" : name;
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

    // ------------------------------------------------------------
    // 슬롯 선택/생성/진입
    // ------------------------------------------------------------
    public void OnClickSlotButton_SetSelected(int number) => _selectedSlot = number;

    public void Slot(int number)
    {
        if (number < 0 || number >= hasSave.Length)
        {
            Debug.LogError("[Select] 잘못된 슬롯 인덱스: " + number);
            return;
        }

        // DataManager가 있다면 nowSlot만 동기화(선택적)
        var dm = FindDataManager();
        if (dm != null) dm.nowSlot = number;

        // 정확한 선택 인덱스 보존
        _selectedSlot = number;

        // 저장 유무에 따라 GoGame에서 처리
        if (hasSave[number]) GoGame();
        else if (creat) creat.SetActive(true);
    }

    public void Creat()
    {
        if (creat) creat.SetActive(true);
    }

    public void GoGame()
    {
        int s = GetSelectedSlotOrFirstExisting();
        if (s < 0 || s >= hasSave.Length)
        {
            Debug.LogWarning("[Select] 유효한 슬롯이 선택되지 않음.");
            if (creat) creat.SetActive(true);
            return;
        }

        bool exists = SlotExists(s);

        if (!exists)
        {
            // 신규 생성 → startSceneName으로 시작
            string name = GetFinalEnteredName();
            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogWarning("[Select] 이름이 비어 있습니다. 이름을 입력해 주세요.");
                if (creat) creat.SetActive(true);
                if (newPlayerInput) newPlayerInput.ActivateInputField();
                return;
            }

            var pd = new PD
            {
                Name = name.Trim(),
                Level = 1,
                Coin = 0,
                Item = 0,
                Day = 1,
                Scene = startSceneName,
                HasSavedPosition = false
            };

            if (!TryWritePD(s, pd))
            {
                Debug.LogError("[Select] 신규 저장 생성 실패");
                return;
            }

            if (s < hasSave.Length) hasSave[s] = true;
            RefreshSingleSlotUI(s);

            // DataManager가 있다면 상태 동기화(선택)
            var dm = FindDataManager();
            if (dm != null)
            {
                dm.nowSlot = s;
                CopyIntoExternalPlayerData(pd, dm.nowPlayer);
                dm.SaveData();
            }

            if (!string.IsNullOrEmpty(startSceneName))
                SceneManager.LoadScene(startSceneName);
            else
                Debug.LogError("[Select] startSceneName 이 비어 있습니다.");
        }
        else
        {
            // 저장 존재 → 파일에서 Scene 읽어 바로 로드
            if (!TryReadPD(s, out var pd) || pd == null)
            {
                Debug.LogError("[Select] 저장 로드 실패(파싱)");
                return;
            }

            string savedScene = string.IsNullOrEmpty(pd.Scene) ? startSceneName : pd.Scene;

            // DataManager가 있다면 상태 동기화(선택)
            var dm = FindDataManager();
            if (dm != null)
            {
                dm.nowSlot = s;
                CopyIntoExternalPlayerData(pd, dm.nowPlayer);
                // 위치 적용/코루틴은 DataManager의 정책에 따름
            }

            if (!string.IsNullOrEmpty(savedScene))
                SceneManager.LoadScene(savedScene);
            else
                Debug.LogError("[Select] 저장 Scene 이 비어 있고 startSceneName 도 유효하지 않습니다.");
        }
    }

    // ------------------------------------------------------------
    // 삭제
    // ------------------------------------------------------------
    public void DeleteSlot(int number)
    {
        if (number < 0 || number >= hasSave.Length)
        {
            Debug.LogError("[Select] DeleteSlot 잘못된 인덱스: " + number);
            return;
        }

        string f = SlotPath(number);
        if (!File.Exists(f))
        {
            Debug.Log("[Select] 슬롯 " + number + " 에 파일 없음");
            return;
        }

        try
        {
            File.Delete(f);
            if (number < hasSave.Length) hasSave[number] = false;

            RefreshSingleSlotUI(number);

            // DataManager가 있다면 정리(선택)
            var dm = FindDataManager();
            if (dm != null && dm.nowSlot == number)
                dm.DataClear();

            Debug.Log("[Select] 슬롯 " + number + " 저장 삭제 완료");
        }
        catch (Exception e)
        {
            Debug.LogError("[Select] 삭제 실패: " + e.Message);
        }
    }

    // ------------------------------------------------------------
    // 보조: 선택 슬롯 추론 / PlayerData 동기화(옵션)
    // ------------------------------------------------------------
    private int GetSelectedSlotOrFirstExisting()
    {
        if (_selectedSlot >= 0 && _selectedSlot < hasSave.Length) return _selectedSlot;

        // DataManager가 있다면 그것을 우선
        var dm = FindDataManager();
        if (dm != null && dm.nowSlot >= 0 && dm.nowSlot < hasSave.Length)
            return dm.nowSlot;

        // 폴백: 첫 번째로 저장이 있는 슬롯
        for (int i = 0; i < hasSave.Length; i++)
            if (hasSave[i]) return i;

        // 끝까지 없다면 0 반환(신규 생성 시 사용)
        return 0;
    }

    // PD → 외부 PlayerData로 "제자리 복사"
    // dst는 DataManager.nowPlayer 같은 실제 PlayerData 인스턴스
    private static void CopyIntoExternalPlayerData(PD src, object dst)
    {
        if (src == null || dst == null) return;

        var t = dst.GetType();

        void Set<TVal>(string name, TVal val)
        {
            var f = t.GetField(name);
            if (f != null && f.FieldType == typeof(TVal)) { f.SetValue(dst, val); return; }
            var p = t.GetProperty(name);
            if (p != null && p.CanWrite && p.PropertyType == typeof(TVal)) p.SetValue(dst, val);
        }

        Set("Name", src.Name);
        Set("Level", src.Level);
        Set("Coin", src.Coin);
        Set("Day", src.Day);
        Set("Item", src.Item);
        Set("Px", src.Px);
        Set("Py", src.Py);
        Set("Pz", src.Pz);
        Set("HasSavedPosition", src.HasSavedPosition);
        Set("Scene", src.Scene);
        Set("Weekday", src.Weekday);
        Set("Language", src.Language);
        Set("Sol_First_Meet", src.Sol_First_Meet);
        Set("Salt_First_Meet", src.Salt_First_Meet);
        Set("Ryu_First_Meet", src.Ryu_First_Meet);
        Set("White_First_Meet", src.White_First_Meet);
    }
}
