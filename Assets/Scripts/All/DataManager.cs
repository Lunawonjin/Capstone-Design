using UnityEngine;
using System.IO;
using System;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine.SceneManagement;

//
// PlayerData: 직렬화 대상
//
[Serializable]
public class PlayerData
{
    public string Name;  // 플레이어 이름
    public int Level;    // 레벨
    public int Coin;     // 보유 재화
    public int Item;     // 아이템 코드(예: 시작 아이템)
}

//
// DataManager: 세이브/로드/삭제 + 현재 슬롯/플레이어 상태 + HUD 갱신 + 씬 로드시 자동 재바인딩
// - 파일명: path + 슬롯번호 (예: ".../save0")
// - 값 감시자(watcher)로 직접 대입도 실시간 반영
//
public class DataManager : MonoBehaviour
{
    public static DataManager instance;       // 싱글톤 인스턴스

    [Header("플레이어/저장 슬롯")]
    public PlayerData nowPlayer = new PlayerData(); // 현재 플레이어 데이터
    public string path;                        // 파일 경로 접두부 (예: ".../save")
    public int nowSlot = -1;                   // 현재 슬롯 인덱스(미선택: -1)

    [Header("HUD(TextMeshProUGUI)")]
    [SerializeField] private TMP_Text coinText;   // 코인 표시
    [SerializeField] private TMP_Text levelText;  // 레벨 표시
    [SerializeField] private TMP_Text nameText;   // 이름 표시(선택)

    [Header("HUD 유지/재바인딩 옵션")]
    [Tooltip("씬 전환에도 HUD 오브젝트를 유지(같은 HUD를 계속 사용)")]
    [SerializeField] private bool dontDestroyOnLoadHUD = false;

    [Tooltip("씬 로드될 때마다 HUD를 자동으로 다시 찾음(각 씬이 자기 HUD를 가질 때 사용)")]
    [SerializeField] private bool autoRebindOnSceneLoaded = true;

    [Tooltip("Start 시 1회 강제 갱신")]
    [SerializeField] private bool refreshHUDOnStart = true;

    [Header("HUD 자동 탐색 기준(이름/태그)")]
    [Tooltip("씬 내 HUD 루트에 설정할 태그. 비워두면 전체 탐색")]
    [SerializeField] private string hudRootTag = "HUD";

    [Tooltip("코인 TMP_Text 오브젝트 이름")]
    [SerializeField] private string coinObjectName = "Text_Coin";

    [Tooltip("레벨 TMP_Text 오브젝트 이름")]
    [SerializeField] private string levelObjectName = "Text_Level";

    [Tooltip("이름 TMP_Text 오브젝트 이름(선택)")]
    [SerializeField] private string nameObjectName = "Text_Name";

    [Header("표기 형식")]
    [Tooltip("코인 표기 포맷(예: \"Coin: {0}\", \"{0:N0} G\")")]
    [SerializeField] private string coinFormat = "Coin: {0}";
    [Tooltip("레벨 표기 포맷(예: \"Lv. {0}\")")]
    [SerializeField] private string levelFormat = "Lv. {0}";
    [Tooltip("이름 표기 포맷(예: \"{0}\")")]
    [SerializeField] private string nameFormat = "{0}";

    // ===== 값 감시자(watcher): nowPlayer의 변화 감지해 HUD 자동 갱신 =====
    int _lastCoin = int.MinValue;
    int _lastLevel = int.MinValue;
    string _lastName = null;

    void Awake()
    {
        // 싱글톤
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 기존 파일 형식과 호환: "save0", "save1", "save2"
        path = Application.persistentDataPath + "/save";
        Debug.Log("[DataManager] 저장 경로: " + path);

        // HUD 오브젝트 유지(옵션)
        if (dontDestroyOnLoadHUD)
        {
            if (coinText != null) DontDestroyOnLoad(coinText.gameObject);
            if (levelText != null) DontDestroyOnLoad(levelText.gameObject);
            if (nameText != null) DontDestroyOnLoad(nameText.gameObject);
        }

        // 씬 로드시 자동 재바인딩
        if (autoRebindOnSceneLoaded)
        {
            SceneManager.sceneLoaded += OnSceneLoaded_RebindHUD;
        }

        // watcher 초기 스냅샷
        SnapshotValues();
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded_RebindHUD;
        }
    }

    void Start()
    {
        if (refreshHUDOnStart)
        {
            UpdateHUD(); // 초기 1회 강제 갱신
        }
    }

    void LateUpdate()
    {
        // 값이 직접 대입으로 변경되었는지 감시 → 바뀌었으면 HUD 갱신
        if (HasValueChanged())
        {
            UpdateHUD();
            SnapshotValues();
        }
    }

    // =========================
    // 세이브/로드/삭제
    // =========================

    public void SaveData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] SaveData 호출 시 nowSlot이 유효하지 않음: " + nowSlot);
            return;
        }

        string data = JsonUtility.ToJson(nowPlayer);
        string file = path + nowSlot.ToString();
        File.WriteAllText(file, data);

        NotifyChanged();
    }

    public void LoadData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] LoadData 호출 시 nowSlot이 유효하지 않음: " + nowSlot);
            return;
        }

        string file = path + nowSlot.ToString();
        if (!File.Exists(file))
        {
            Debug.LogError("[DataManager] 저장 파일이 없음: " + file);
            return;
        }

        string data = File.ReadAllText(file);
        var loaded = JsonUtility.FromJson<PlayerData>(data);
        nowPlayer = loaded ?? new PlayerData();

        NotifyChanged();
        SnapshotValues(); // 로드 직후 스냅샷 갱신
    }

    public void DataClear()
    {
        nowSlot = -1;
        nowPlayer = new PlayerData();
        NotifyChanged();
        SnapshotValues();
    }

    public bool ExistsSlot(int slot)
    {
        if (slot < 0) return false;
        string file = path + slot.ToString();
        return File.Exists(file);
    }

    public bool HasAnySave(int slotCount = 3)
    {
        for (int i = 0; i < slotCount; i++)
        {
            if (ExistsSlot(i)) return true;
        }
        return false;
    }

    public int GetMostRecentSaveSlot(int slotCount = 3)
    {
        int bestSlot = -1;
        DateTime bestTime = DateTime.MinValue;

        for (int i = 0; i < slotCount; i++)
        {
            string file = path + i.ToString();
            if (!File.Exists(file)) continue;

            DateTime t = File.GetLastWriteTime(file);
            if (t > bestTime)
            {
                bestTime = t;
                bestSlot = i;
            }
        }
        return bestSlot;
    }

    public bool TryLoadMostRecentSave(int slotCount = 3)
    {
        int slot = GetMostRecentSaveSlot(slotCount);
        if (slot < 0) return false;

        nowSlot = slot;
        try
        {
            LoadData();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[DataManager] TryLoadMostRecentSave 실패: " + e.Message);
            DataClear();
            return false;
        }
    }

    public bool DeleteData(int slot)
    {
        if (slot < 0)
        {
            Debug.LogError("[DataManager] DeleteData: 잘못된 슬롯 인덱스: " + slot);
            return false;
        }

        string file = path + slot.ToString();

        if (File.Exists(file))
        {
            try
            {
                File.Delete(file);
                NotifyChanged(); // HUD/슬롯 표시 갱신
                SnapshotValues();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[DataManager] DeleteData 실패 (" + file + "): " + e.Message);
                return false;
            }
        }
        return false;
    }

    // =========================
    // 코인/레벨 변경 API (+ HUD 즉시 갱신)
    // =========================

    public void SetCoin(int coin)
    {
        nowPlayer.Coin = Math.Max(0, coin);
        NotifyChanged();
        SnapshotValues();
    }

    public void AddCoin(int delta)
    {
        long v = (long)nowPlayer.Coin + delta;
        nowPlayer.Coin = (int)Mathf.Clamp(v, 0, int.MaxValue);
        NotifyChanged();
        SnapshotValues();
    }

    public void SetLevel(int level)
    {
        nowPlayer.Level = Mathf.Max(1, level);
        NotifyChanged();
        SnapshotValues();
    }

    public void AddLevel(int delta)
    {
        int v = nowPlayer.Level + delta;
        nowPlayer.Level = Mathf.Max(1, v);
        NotifyChanged();
        SnapshotValues();
    }

    // =========================
    // HUD 갱신 / 바인딩
    // =========================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void NotifyChanged()
    {
        UpdateHUD();
    }

    void UpdateHUD()
    {
        if (coinText != null)
            coinText.text = string.Format(coinFormat, nowPlayer.Coin);

        if (levelText != null)
            levelText.text = string.Format(levelFormat, nowPlayer.Level);

        if (nameText != null)
        {
            string nm = string.IsNullOrEmpty(nowPlayer.Name) ? "No Name" : nowPlayer.Name;
            nameText.text = string.Format(nameFormat, nm);
        }
    }

    public void BindHUD(TMP_Text coin, TMP_Text level, TMP_Text name = null)
    {
        coinText = coin;
        levelText = level;
        nameText = name;

        if (dontDestroyOnLoadHUD)
        {
            if (coinText != null) DontDestroyOnLoad(coinText.gameObject);
            if (levelText != null) DontDestroyOnLoad(levelText.gameObject);
            if (nameText != null) DontDestroyOnLoad(nameText.gameObject);
        }

        UpdateHUD();
        SnapshotValues();
    }

    // =========================
    // 씬 로드시 자동 재바인딩
    // =========================

    void OnSceneLoaded_RebindHUD(Scene scene, LoadSceneMode mode)
    {
        if (!autoRebindOnSceneLoaded) return;

        bool needCoin = coinText == null;
        bool needLevel = levelText == null;
        bool needName = nameText == null && !string.IsNullOrEmpty(nameObjectName);

        if (!(needCoin || needLevel || needName)) return;

        // HUD 루트 태그 우선 탐색
        Transform searchRoot = null;
        if (!string.IsNullOrEmpty(hudRootTag))
        {
            var hudRootGO = GameObject.FindWithTag(hudRootTag);
            if (hudRootGO != null) searchRoot = hudRootGO.transform;
        }

        TMP_Text foundCoin = needCoin ? FindTMPTextByName(coinObjectName, searchRoot) : coinText;
        TMP_Text foundLevel = needLevel ? FindTMPTextByName(levelObjectName, searchRoot) : levelText;
        TMP_Text foundName = needName ? FindTMPTextByName(nameObjectName, searchRoot) : nameText;

        if (foundCoin != null || foundLevel != null || foundName != null)
        {
            BindHUD(foundCoin, foundLevel, foundName);
        }
    }

    TMP_Text FindTMPTextByName(string targetName, Transform root = null)
    {
        if (string.IsNullOrEmpty(targetName)) return null;

        if (root != null)
        {
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in texts)
            {
                if (t != null && t.name == targetName) return t;
            }
            return null;
        }
        else
        {
            var texts = GameObject.FindObjectsOfType<TMP_Text>(true);
            foreach (var t in texts)
            {
                if (t != null && t.name == targetName) return t;
            }
            return null;
        }
    }

    // ===== watcher 유틸 =====
    void SnapshotValues()
    {
        _lastCoin = nowPlayer?.Coin ?? 0;
        _lastLevel = nowPlayer?.Level ?? 0;
        _lastName = nowPlayer?.Name ?? "";
    }

    bool HasValueChanged()
    {
        if (nowPlayer == null) return false;
        return _lastCoin != nowPlayer.Coin
            || _lastLevel != nowPlayer.Level
            || _lastName != (nowPlayer.Name ?? "");
    }
}
