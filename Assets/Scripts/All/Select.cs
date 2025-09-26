// Select.cs
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class Select : MonoBehaviour
{
    [Header("새 플레이어 이름 입력 패널")]
    public GameObject creat;                  // 새 플레이어 이름 입력 패널

    [Header("슬롯 UI")]
    public TMP_Text[] slotText;               // 각 슬롯 버튼 아래 텍스트
    public TMP_Text newPlayerName;            // 새 플레이어 이름 입력(TMP_Text)

    [Header("시작/폴백 씬 이름")]
    [SerializeField] private string startSceneName = "Player's Room";

    private bool[] savefile = new bool[3];    // 각 슬롯 세이브 존재 여부

    void Start()
    {
        RefreshSlotsUI();
    }

    // 저장 파일 경로 규칙: <persistent>/save/slot_{i}.json
    private string SlotPath(int i)
    {
        var dm = DataManager.instance;
        if (!Directory.Exists(dm.path)) Directory.CreateDirectory(dm.path);
        return Path.Combine(dm.path, $"slot_{i}.json");
    }

    // 시작/갱신 시 슬롯 UI를 채운다. (파일을 직접 읽어서 이름만 프리뷰)
    private void RefreshSlotsUI()
    {
        for (int i = 0; i < savefile.Length; i++)
        {
            string file = SlotPath(i);
            bool exists = File.Exists(file);
            savefile[i] = exists;

            if (slotText != null && i < slotText.Length && slotText[i] != null)
            {
                if (!exists)
                {
                    slotText[i].text = "비어있음";
                    continue;
                }

                // 이름만 미리보기
                try
                {
                    string json = File.ReadAllText(file);
                    PlayerData pd = JsonUtility.FromJson<PlayerData>(json);
                    string name = (pd != null && !string.IsNullOrEmpty(pd.Name)) ? pd.Name : "Player";
                    slotText[i].text = name;
                }
                catch
                {
                    slotText[i].text = "손상된 저장";
                }
            }
        }
        // DataManager 상태를 건드리지 않음
    }

    // 슬롯 버튼 클릭
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
            // 기존 저장 → 저장된 씬으로 진입
            StartCoroutine(Co_LoadAndEnterSaved(number));
        }
        else
        {
            // 신규 → 이름 입력 패널
            Creat();
        }
    }

    // 새 플레이어 이름 입력 패널 열기
    public void Creat()
    {
        if (creat != null) creat.SetActive(true);
    }

    // "시작" 버튼에서 호출: 신규 저장을 만들고 시작 씬으로 진입
    public void GoGame()
    {
        StartCoroutine(Co_NewGameOrContinue());
    }

    // 신규 저장 or 기존 저장 분기
    private IEnumerator Co_NewGameOrContinue()
    {
        var dm = DataManager.instance;
        int s = dm.nowSlot;

        if (s < 0 || s >= savefile.Length)
        {
            Debug.LogWarning("[Select] 유효한 슬롯이 선택되지 않음. 먼저 슬롯을 선택하세요.");
            if (creat != null) creat.SetActive(true);
            yield break;
        }

        string file = SlotPath(s);
        bool exists = File.Exists(file);

        if (!exists)
        {
            // 신규 생성
            string name = (newPlayerName != null) ? newPlayerName.text.Trim() : "";
            if (string.IsNullOrEmpty(name)) name = "Player";

            dm.nowPlayer = new PlayerData
            {
                Name = name,
                Level = 1,
                Coin = 0,
                Item = 0,
                Day = 1,
                Scene = startSceneName,   // 시작 씬을 세이브에 기록
                HasSavedPosition = false  // 시작은 스폰 지점
            };

            dm.SaveData();
            savefile[s] = true;

            // 시작 씬으로 진입(스폰 지점에서 시작하므로 좌표 적용 불필요)
            yield return LoadSceneAsyncSafe(startSceneName);
        }
        else
        {
            // 기존 저장 불러오기 → 저장된 씬으로
            yield return Co_LoadAndEnterSaved(s);
        }
    }

    // 기존 저장 로드 → 저장된 씬으로 이동 → 좌표 적용
    private IEnumerator Co_LoadAndEnterSaved(int slot)
    {
        var dm = DataManager.instance;

        // 예외 안전 로드
        if (!SafeLoad())
        {
            Debug.LogError("[Select] 저장 로드 실패. 신규 생성으로 전환하세요.");
            yield break;
        }

        // 저장된 씬 이름 확인
        string target = dm.nowPlayer != null ? dm.nowPlayer.Scene : null;
        if (string.IsNullOrEmpty(target) || !Application.CanStreamedLevelBeLoaded(target))
        {
            // 폴백: 시작 씬
            target = startSceneName;
        }

        // 씬 로드
        yield return LoadSceneAsyncSafe(target);

        // 저장된 좌표가 있다면 적용
        yield return dm.LoadSavedSceneAndPlacePlayer();
    }

    // 공용: 안전한 씬 로드 래퍼
    private IEnumerator LoadSceneAsyncSafe(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[Select] 유효하지 않은 씬 이름");
            yield break;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("[Select] 빌드에 없는 씬: " + sceneName);
            yield break;
        }

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;
    }

    // 예외 안전 로드(성공 여부 반환)
    private bool SafeLoad()
    {
        try
        {
            DataManager.instance.LoadData();
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Select] 로드 실패: " + e.Message);
            DataManager.instance.DataClear();
            return false;
        }
    }


    // 슬롯 삭제 버튼: 해당 슬롯의 세이브를 삭제하고 UI 갱신
    public void DeleteSlot(int number)
    {
        if (number < 0 || number >= savefile.Length)
        {
            Debug.LogError("[Select] DeleteSlot 잘못된 인덱스: " + number);
            return;
        }

        bool deleted = DataManager.instance.DeleteData(number);
        savefile[number] = false;

        if (slotText != null && number < slotText.Length && slotText[number] != null)
            slotText[number].text = "비어있음";

        if (DataManager.instance.nowSlot == number)
            DataManager.instance.DataClear();

        if (deleted)
            Debug.Log("[Select] 슬롯 " + number + " 저장 삭제 완료");
        else
            Debug.Log("[Select] 슬롯 " + number + " 저장 파일이 없거나 삭제할 것이 없음");
    }
}
