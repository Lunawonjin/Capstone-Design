// Select.cs
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class Select : MonoBehaviour
{
    [Header("새 플레이어 이름 입력 패널")]
    [SerializeField] private GameObject creat;

    [Header("슬롯 UI 라벨")]
    [SerializeField] private TMP_Text[] slotText;          // 각 슬롯 버튼 아래 라벨

    [Header("이름 입력")]
    [SerializeField] private TMP_InputField newPlayerInput; // 실제 유저 입력창
    [SerializeField] private TMP_Text newPlayerPreview; // (선택) 프리뷰/라벨을 입력 소스로 쓸 때

    [Header("시작/폴백 씬 이름")]
    [SerializeField] private string startSceneName = "Player's Room";

    [Header("빈 슬롯 라벨 유지")]
    [Tooltip("빈 슬롯이면 프리팹/로컬라이즈 기본 라벨을 그대로 둡니다. 끄면 빈 슬롯 라벨을 공백으로 비웁니다.")]
    [SerializeField] private bool leaveEmptySlotTextUntouched = true;

    private readonly bool[] savefile = new bool[3]; // 각 슬롯 세이브 존재 여부

    void Start()
    {
        RefreshSlotsUI();
    }

    // --- 경로/입출력 유틸 ---

    // 정확한 슬롯 파일 경로: <persistent>/save/slot_{i}.json
    private string SlotPath(int i)
    {
        var dm = DataManager.instance;
        if (!Directory.Exists(dm.path)) Directory.CreateDirectory(dm.path);
        return Path.Combine(dm.path, $"slot_{i}.json");
    }

    // 저장 파일에서 이름만 안전하게 읽기(상태 비침투)
    private string ReadPlayerNameSafe(string file)
    {
        try
        {
            string json = File.ReadAllText(file);
            PlayerData pd = JsonUtility.FromJson<PlayerData>(json);
            return pd != null ? pd.Name : null;
        }
        catch
        {
            return null;
        }
    }

    // 입력 필드/프리뷰에서 이름 읽기
    private string GetEnteredPlayerName()
    {
        if (newPlayerInput != null)
        {
            var t = newPlayerInput.text?.Trim();
            if (!string.IsNullOrEmpty(t)) return t;
        }
        if (newPlayerPreview != null)
        {
            var t = newPlayerPreview.text?.Trim();
            if (!string.IsNullOrEmpty(t)) return t;
        }
        return "";
    }

    // --- UI 갱신 ---

    private void RefreshSlotsUI()
    {
        for (int i = 0; i < savefile.Length; i++)
            RefreshSingleSlotUI(i);
    }

    private void RefreshSingleSlotUI(int i)
    {
        string file = SlotPath(i);
        bool exists = File.Exists(file);
        savefile[i] = exists;

        if (slotText == null || i >= slotText.Length || slotText[i] == null) return;

        if (exists)
        {
            string name = ReadPlayerNameSafe(file);
            slotText[i].text = string.IsNullOrEmpty(name) ? "Player" : name;
        }
        else
        {
            // 빈 슬롯: 요청대로 텍스트를 건드리지 않음
            if (!leaveEmptySlotTextUntouched)
                slotText[i].text = string.Empty; // 완전히 비우고 싶을 때만
        }
    }

    // --- 슬롯 선택/생성/진입 ---

    public void Slot(int number)
    {
        if (number < 0 || number >= savefile.Length)
        {
            Debug.LogError("[Select] 잘못된 슬롯 인덱스: " + number);
            return;
        }

        DataManager.instance.nowSlot = number;

        if (savefile[number])
        {
            SafeLoad();
            GoGame();
        }
        else
        {
            Creat();
        }
    }

    public void Creat()
    {
        if (creat) creat.SetActive(true);
    }

    public void GoGame()
    {
        int s = DataManager.instance.nowSlot;

        if (s < 0 || s >= savefile.Length)
        {
            Debug.LogWarning("[Select] 유효한 슬롯이 선택되지 않음. 먼저 슬롯을 선택하세요.");
            if (creat) creat.SetActive(true);
            return;
        }

        bool exists = File.Exists(SlotPath(s));

        if (!exists)
        {
            // 새 저장 생성
            string name = GetEnteredPlayerName();
            if (string.IsNullOrEmpty(name)) name = "Player";

            DataManager.instance.nowPlayer = new PlayerData
            {
                Name = name,
                Level = 1,
                Coin = 0,
                Item = 0,
                Day = 1,
                Scene = startSceneName,
                HasSavedPosition = false
            };

            DataManager.instance.SaveData();
            savefile[s] = true;

            // 라벨에 즉시 반영
            RefreshSingleSlotUI(s);
        }
        else
        {
            SafeLoad();
        }

        // 시작 씬으로 이동
        if (!string.IsNullOrEmpty(startSceneName))
            SceneManager.LoadScene(startSceneName);
        else
            Debug.LogError("[Select] startSceneName 이 비어 있습니다.");
    }

    // --- 로드/삭제 ---

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

    // 슬롯 삭제: 파일만 지우고 라벨은 건드리지 않음(요청사항)
    public void DeleteSlot(int number)
    {
        if (number < 0 || number >= savefile.Length)
        {
            Debug.LogError("[Select] DeleteSlot 잘못된 인덱스: " + number);
            return;
        }

        bool deleted = DataManager.instance.DeleteData(number);

        if (deleted)
        {
            savefile[number] = false;

            // 텍스트는 변경하지 않음(프리팹/로컬라이즈 라벨 유지).
            // 완전히 비우고 싶다면 옵션을 끄고 아래가 적용됨.
            if (!leaveEmptySlotTextUntouched && slotText != null && number < slotText.Length && slotText[number] != null)
                slotText[number].text = string.Empty;

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
