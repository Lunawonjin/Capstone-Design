using UnityEngine;
using System.IO;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// 직렬화 대상: 플레이어 데이터
/// </summary>
[Serializable]
public class PlayerData
{
    public string Name;
    public int Level;
    public int Coin;
    public int Day;
    public int Item;

    // 위치 + 플래그
    public float Px, Py, Pz;
    public bool HasSavedPosition;

    // 마지막 씬 이름
    public string Scene;

    // 기본값
    public PlayerData()
    {
        Level = 1;
        Day = 1;
        Scene = ""; // 미저장 상태
    }
}

/// <summary>
/// DataManager
/// - 슬롯 기반 저장/로드
/// - 최근 세이브 탐색
/// - HUD 자동 바인딩
/// - 저장된 씬/좌표 복원(옵션)
/// - Unity 6 API 사용(linearVelocity 등)
/// </summary>
public class DataManager : MonoBehaviour
{
    public static DataManager instance;

    [Header("플레이어/저장 슬롯")]
    public PlayerData nowPlayer = new PlayerData();
    public string path;               // 저장 폴더 경로 (persistentDataPath/save)
    public int nowSlot = -1;          // 현재 선택된 저장 슬롯(0,1,2 ...)

    [Header("HUD(TextMeshProUGUI)")]
    [SerializeField] private TMP_Text coinText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private TMP_Text nameText;

    [Header("HUD 유지/재바인딩 옵션")]
    [SerializeField] private bool dontDestroyOnLoadHUD = false;
    [SerializeField] private bool autoRebindOnSceneLoaded = true;
    [SerializeField] private bool refreshHUDOnStart = true;

    [Header("HUD 자동 탐색 기준(이름/태그)")]
    [SerializeField] private string hudRootTag = "HUD";
    [SerializeField] private string coinObjectName = "Text_Coin";
    [SerializeField] private string levelObjectName = "Text_Level";
    [SerializeField] private string dayObjectName = "Text_Day";
    [SerializeField] private string nameObjectName = "Text_Name";

    [Header("표기 형식")]
    [SerializeField] private string coinFormat = "{0}";
    [SerializeField] private string levelFormat = "Lv. {0}";
    [SerializeField] private string dayFormat = "{0}일차";
    [SerializeField] private string nameFormat = "{0}";

    [Header("플레이어 위치 적용 옵션")]
    [SerializeField] private bool applySavedPositionOnLoad = true;
    [SerializeField] private string playerTagForReposition = "Player";
    [SerializeField] private float applyPosTimeoutSec = 3f;

    [Header("자동 씬 로드 옵션")]
    public bool autoLoadSavedSceneOnStart = false;

    // HUD 변경 감지용 스냅샷
    int _lastCoin = int.MinValue, _lastLevel = int.MinValue, _lastDay = int.MinValue;
    string _lastName = null;

    // 초기화
    void Awake()
    {
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

        // 저장 폴더 경로 초기화 및 보장
        path = System.IO.Path.Combine(Application.persistentDataPath, "save");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        // HUD 오브젝트 유지 설정
        if (dontDestroyOnLoadHUD)
        {
            if (coinText) DontDestroyOnLoad(coinText.gameObject);
            if (levelText) DontDestroyOnLoad(levelText.gameObject);
            if (dayText) DontDestroyOnLoad(dayText.gameObject);
            if (nameText) DontDestroyOnLoad(nameText.gameObject);
        }

        SceneManager.sceneLoaded += OnSceneLoaded_RebindHUD_AndApplyPos;
        SnapshotValues();
    }

    void OnDestroy()
    {
        if (instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded_RebindHUD_AndApplyPos;
    }

    void Start()
    {
        if (refreshHUDOnStart) UpdateHUD();

        // 선택: 가장 최근 세이브로 자동 로드 후 위치 적용
        if (autoLoadSavedSceneOnStart && HasAnySave())
        {
            if (TryLoadMostRecentSave())
                StartCoroutine(LoadSavedSceneAndPlacePlayer());
        }
    }

    void LateUpdate()
    {
        if (HasValueChanged())
        {
            UpdateHUD();
            SnapshotValues();
        }
    }

    // ==================== 저장/로드/삭제 ====================

    /// <summary>
    /// 슬롯 파일 경로(폴더 보장)
    /// </summary>
    private string GetSlotPath(int slot)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return System.IO.Path.Combine(path, $"slot_{slot}.json");
    }

    /// <summary>
    /// 저장
    /// </summary>
    public void SaveData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] nowSlot 미지정");
            return;
        }

        string file = GetSlotPath(nowSlot);
        try
        {
            if (nowPlayer.Level < 1) nowPlayer.Level = 1;
            if (nowPlayer.Day < 1) nowPlayer.Day = 1;

            string json = JsonUtility.ToJson(nowPlayer, false);
            File.WriteAllText(file, json);

            Debug.Log($"[DataManager] Saved: {file}");
            NotifyChanged();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] Save failed: {e}");
        }
    }

    /// <summary>
    /// 로드
    /// </summary>
    public void LoadData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] LoadData: nowSlot 미지정");
            return;
        }

        string file = GetSlotPath(nowSlot);
        if (!File.Exists(file))
        {
            Debug.LogError("[DataManager] 파일 없음: " + file);
            return;
        }

        try
        {
            nowPlayer = JsonUtility.FromJson<PlayerData>(File.ReadAllText(file)) ?? new PlayerData();

            // 구버전 세이브 보정
            if (nowPlayer.Level < 1) nowPlayer.Level = 1;
            if (nowPlayer.Day < 1) nowPlayer.Day = 1;
            if (!nowPlayer.HasSavedPosition && (nowPlayer.Px != 0f || nowPlayer.Py != 0f || nowPlayer.Pz != 0f))
                nowPlayer.HasSavedPosition = true;

            NotifyChanged();
            SnapshotValues();

            // 외부에서 씬 로드 안 할 때, 같은 씬이라면 바로 위치 적용 시도
            if (applySavedPositionOnLoad && nowPlayer.HasSavedPosition)
                StartCoroutine(ApplyPositionWhenReady());
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] Load failed: {e}");
        }
    }

    /// <summary>
    /// 데이터 초기화(메모리)
    /// </summary>
    public void DataClear()
    {
        nowSlot = -1;
        nowPlayer = new PlayerData();
        NotifyChanged();
        SnapshotValues();
    }

    /// <summary>
    /// 해당 슬롯이 존재하는가
    /// </summary>
    public bool ExistsSlot(int slot)
    {
        if (slot < 0) return false;
        string f = GetSlotPath(slot);
        return File.Exists(f);
    }

    /// <summary>
    /// 하나라도 세이브가 있는가
    /// </summary>
    public bool HasAnySave(int slotCount = 3)
    {
        for (int i = 0; i < slotCount; i++)
            if (ExistsSlot(i)) return true;
        return false;
    }

    /// <summary>
    /// 가장 최근(수정 시간 최신) 슬롯 반환
    /// </summary>
    public int GetMostRecentSaveSlot(int slotCount = 3)
    {
        int best = -1;
        DateTime tbest = DateTime.MinValue;
        for (int i = 0; i < slotCount; i++)
        {
            string f = GetSlotPath(i);
            if (!File.Exists(f)) continue;
            var t = File.GetLastWriteTime(f);
            if (t > tbest) { tbest = t; best = i; }
        }
        return best;
    }

    /// <summary>
    /// 가장 최근 세이브 로드 시도
    /// </summary>
    public bool TryLoadMostRecentSave(int slotCount = 3)
    {
        int s = GetMostRecentSaveSlot(slotCount);
        if (s < 0) return false;
        nowSlot = s;
        try
        {
            LoadData();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            DataClear();
            return false;
        }
    }

    /// <summary>
    /// 지정 슬롯 삭제
    /// </summary>
    public bool DeleteData(int slot)
    {
        if (slot < 0) return false;
        string f = GetSlotPath(slot);
        if (!File.Exists(f)) return false;

        try
        {
            File.Delete(f);
            NotifyChanged();
            SnapshotValues();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            return false;
        }
    }

    // ==================== 값 변경 API ====================

    public void SetCoin(int coin) { nowPlayer.Coin = Math.Max(0, coin); NotifyChanged(); SnapshotValues(); }
    public void AddCoin(int delta)
    {
        long v = (long)nowPlayer.Coin + delta;
        nowPlayer.Coin = (int)Mathf.Clamp(v, 0, int.MaxValue);
        NotifyChanged(); SnapshotValues();
    }

    public void SetLevel(int level) { nowPlayer.Level = Mathf.Max(1, level); NotifyChanged(); SnapshotValues(); }
    public void AddLevel(int delta) { nowPlayer.Level = Mathf.Max(1, nowPlayer.Level + delta); NotifyChanged(); SnapshotValues(); }

    public void SetDay(int day) { nowPlayer.Day = Mathf.Max(1, day); NotifyChanged(); SnapshotValues(); }
    public void AddDay(int delta) { nowPlayer.Day = Mathf.Max(1, nowPlayer.Day + delta); NotifyChanged(); SnapshotValues(); }

    public void SetPlayerName(string newName) { nowPlayer.Name = newName ?? ""; NotifyChanged(); SnapshotValues(); }

    // ==================== 위치/씬 저장 ====================

    public void SetPlayerPosition(Vector3 pos)
    {
        nowPlayer.Px = pos.x;
        nowPlayer.Py = pos.y;
        nowPlayer.Pz = pos.z;
        nowPlayer.HasSavedPosition = true;
    }

    public void SetSceneName(string sceneName) => nowPlayer.Scene = sceneName ?? "";

    // ==================== 씬 로드 + 위치 적용 ====================

    /// <summary>
    /// 저장된 씬을 로드하고 플레이어를 저장된 위치로 이동
    /// </summary>
    public IEnumerator LoadSavedSceneAndPlacePlayer()
    {
        if (nowPlayer == null || string.IsNullOrEmpty(nowPlayer.Scene))
            yield break;

        string targetScene = nowPlayer.Scene;
        string currentScene = SceneManager.GetActiveScene().name;

        // 다른 씬이면 먼저 로드
        if (!string.Equals(targetScene, currentScene, StringComparison.Ordinal))
        {
            var op = SceneManager.LoadSceneAsync(targetScene);
            while (!op.isDone) yield return null;
        }

        // 플레이어가 생성될 때까지 대기 후 위치 적용
        if (applySavedPositionOnLoad && nowPlayer.HasSavedPosition)
            yield return ApplyPositionWhenReady();
    }

    /// <summary>
    /// 플레이어를 찾을 때까지 대기 후 위치 적용
    /// </summary>
    private IEnumerator ApplyPositionWhenReady()
    {
        float t = 0f;
        GameObject player = null;

        yield return null; // 한 프레임 대기

        while (t < applyPosTimeoutSec)
        {
            player = GameObject.FindGameObjectWithTag(playerTagForReposition);
            if (player) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!player) yield break;

        Vector3 target = new Vector3(nowPlayer.Px, nowPlayer.Py, nowPlayer.Pz);

        // Rigidbody2D 우선
        var rb2 = player.GetComponent<Rigidbody2D>();
        if (rb2)
        {
            rb2.linearVelocity = Vector2.zero; // Unity 6 API
            rb2.angularVelocity = 0f;
            rb2.position = new Vector2(target.x, target.y);
            player.transform.position = target;
            yield break;
        }

        // 3D Rigidbody
        var rb3 = player.GetComponent<Rigidbody>();
        if (rb3)
        {
            rb3.linearVelocity = Vector3.zero; // Unity 6 API
            rb3.angularVelocity = Vector3.zero;
            rb3.position = target;
            player.transform.position = target;
            yield break;
        }

        // 기본 Transform 이동
        player.transform.position = target;
    }

    // ==================== HUD ====================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void NotifyChanged() => UpdateHUD();

    void UpdateHUD()
    {
        if (coinText) coinText.text = string.Format(coinFormat, nowPlayer.Coin);
        if (levelText) levelText.text = string.Format(levelFormat, nowPlayer.Level);
        if (dayText) dayText.text = string.Format(dayFormat, nowPlayer.Day);

        if (nameText)
        {
            string nm = string.IsNullOrEmpty(nowPlayer.Name) ? "No Name" : nowPlayer.Name;
            nameText.text = string.Format(nameFormat, nm);
        }
    }

    public void BindHUD(TMP_Text coin, TMP_Text level, TMP_Text day = null, TMP_Text name = null)
    {
        coinText = coin;
        levelText = level;
        dayText = day;
        nameText = name;

        if (dontDestroyOnLoadHUD)
        {
            if (coinText) DontDestroyOnLoad(coinText.gameObject);
            if (levelText) DontDestroyOnLoad(levelText.gameObject);
            if (dayText) DontDestroyOnLoad(dayText.gameObject);
            if (nameText) DontDestroyOnLoad(nameText.gameObject);
        }

        UpdateHUD();
        SnapshotValues();
    }

    public void RebindHUDNow()
    {
        OnSceneLoaded_RebindHUD_AndApplyPos(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    void OnSceneLoaded_RebindHUD_AndApplyPos(Scene scene, LoadSceneMode mode)
    {
        if (autoRebindOnSceneLoaded)
        {
            bool needCoin = coinText == null;
            bool needLevel = levelText == null;
            bool needDay = dayText == null && !string.IsNullOrEmpty(dayObjectName);
            bool needName = nameText == null && !string.IsNullOrEmpty(nameObjectName);

            if (needCoin || needLevel || needDay || needName)
            {
                Transform root = null;
                if (!string.IsNullOrEmpty(hudRootTag))
                {
                    var hudRootGO = GameObject.FindWithTag(hudRootTag);
                    if (hudRootGO) root = hudRootGO.transform;
                }

                TMP_Text FindTMP(string n)
                {
                    if (string.IsNullOrEmpty(n)) return null;
                    if (root)
                    {
                        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                            if (t && t.name == n) return t;
                        return null;
                    }
                    else
                    {
                        foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                        {
                            if (!t) continue;
                            if (t.hideFlags != HideFlags.None) continue;
                            if (!t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded) continue;
                            if (t.name == n) return t;
                        }
                        return null;
                    }
                }

                var fc = needCoin ? FindTMP(coinObjectName) : coinText;
                var fl = needLevel ? FindTMP(levelObjectName) : levelText;
                var fd = needDay ? FindTMP(dayObjectName) : dayText;
                var fn = needName ? FindTMP(nameObjectName) : nameText;

                if (fc || fl || fd || fn) BindHUD(fc, fl, fd, fn);
            }
        }

        if (applySavedPositionOnLoad && nowPlayer != null && nowPlayer.HasSavedPosition)
            StartCoroutine(ApplyPositionWhenReady());
    }

    void SnapshotValues()
    {
        _lastCoin = nowPlayer?.Coin ?? 0;
        _lastLevel = nowPlayer?.Level ?? 1;
        _lastDay = nowPlayer?.Day ?? 1;
        _lastName = nowPlayer?.Name ?? "";
    }

    bool HasValueChanged()
    {
        if (nowPlayer == null) return false;
        return _lastCoin != nowPlayer.Coin
            || _lastLevel != nowPlayer.Level
            || _lastDay != nowPlayer.Day
            || _lastName != (nowPlayer.Name ?? "");
    }
}
