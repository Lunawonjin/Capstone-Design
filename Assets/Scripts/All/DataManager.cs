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

    // 요일(1~7) : 월(1) 화(2) 수(3) 목(4) 금(5) 토(6) 일(7)
    public int Weekday;

    // 언어 코드("ko","en","jp") — 기본 "ko"
    public string Language;

    // 기본값
    public PlayerData()
    {
        Level = 1;
        Day = 1;
        Scene = ""; // 미저장 상태
        Weekday = 1; // Day=1은 월요일로 시작(필요 시 DataManager의 baseWeekdayForDay1로 재동기화됨)
        Language = "ko";
    }
}

/// <summary>
/// DataManager
/// - 슬롯 기반 저장/로드
/// - 최근 세이브 탐색
/// - HUD 자동 바인딩
/// - 저장된 씬/좌표 복원(옵션)
/// - Unity 6 API 사용(linearVelocity 등)
/// - 요일(1~7, 월~일) 저장/증가/표기 + 주말 조건 제공
/// - 언어 코드("ko","en","jp") 저장/로드/조회/설정
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
    [Tooltip("예: {0}일 {1}요일  → {0}=일차, {1}=요일명(월/화/수/목/금/토/일)")]
    [SerializeField] private string dayFormat = "{0}일 {1}요일";
    [SerializeField] private string coinFormat = "{0}";
    [SerializeField] private string levelFormat = "Lv. {0}";
    [SerializeField] private string nameFormat = "{0}";

    [Header("요일 동기화 옵션")]
    [Tooltip("Day=1이 무슨 요일인지(1~7: 월=1 … 일=7)")]
    [Range(1, 7)][SerializeField] private int baseWeekdayForDay1 = 1;

    [Tooltip("SetDay 호출 시 Day값에 맞춰 Weekday를 자동으로 재계산")]
    [SerializeField] private bool autoSyncWeekdayOnSetDay = true;

    [Header("플레이어 위치 적용 옵션")]
    [SerializeField] private bool applySavedPositionOnLoad = true;
    [SerializeField] private string playerTagForReposition = "Player";
    [SerializeField] private float applyPosTimeoutSec = 3f;

    [Header("자동 씬 로드 옵션")]
    public bool autoLoadSavedSceneOnStart = false;

    // 내부: 요일 이름표(1~7 인덱스 사용; 0은 미사용)
    private static readonly string[] WEEK_KO = { "", "월", "화", "수", "목", "금", "토", "일" };

    // HUD 변경 감지용 스냅샷
    int _lastCoin = int.MinValue, _lastLevel = int.MinValue, _lastDay = int.MinValue, _lastWeekday = int.MinValue;
    string _lastName = null;
    string _lastLanguage = null;

    // ===== 초기화 =====
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

        // 씬 변경 시 HUD 재바인딩 + 저장 위치 적용
        SceneManager.sceneLoaded += OnSceneLoaded_RebindHUD_AndApplyPos;

        // Weekday 보정(구세이브/초기 데이터 보호)
        EnsureWeekdayValid();

        // Language 필드 보정(구세이브 보호)
        EnsureLanguageValid();

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

    private string GetSlotPath(int slot)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return System.IO.Path.Combine(path, $"slot_{slot}.json");
    }

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
            EnsureWeekdayValid();
            EnsureLanguageValid();

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

            // 요일 필드가 0이면 Day 기준으로 재계산
            if (nowPlayer.Weekday < 1 || nowPlayer.Weekday > 7)
                RecomputeWeekdayFromDay();

            // 언어 코드 보정
            EnsureLanguageValid();

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

    public void DataClear()
    {
        nowSlot = -1;
        nowPlayer = new PlayerData();
        // Day=1 → baseWeekdayForDay1 반영
        if (autoSyncWeekdayOnSetDay) RecomputeWeekdayFromDay();
        EnsureLanguageValid();
        NotifyChanged();
        SnapshotValues();
    }

    public bool ExistsSlot(int slot)
    {
        if (slot < 0) return false;
        string f = GetSlotPath(slot);
        return File.Exists(f);
    }

    public bool HasAnySave(int slotCount = 3)
    {
        for (int i = 0; i < slotCount; i++)
            if (ExistsSlot(i)) return true;
        return false;
    }

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

    /// <summary>
    /// Day를 직접 설정. autoSyncWeekdayOnSetDay=true면 Day=1의 기준 요일(baseWeekdayForDay1)로 Weekday 재계산.
    /// </summary>
    public void SetDay(int day)
    {
        nowPlayer.Day = Mathf.Max(1, day);
        if (autoSyncWeekdayOnSetDay) RecomputeWeekdayFromDay();
        NotifyChanged(); SnapshotValues();
    }

    /// <summary>
    /// Day를 증감. 요일도 함께 순환(1~7).
    /// </summary>
    public void AddDay(int delta)
    {
        int before = nowPlayer.Day;
        nowPlayer.Day = Mathf.Max(1, nowPlayer.Day + delta);

        // 요일 동기화: delta만큼 순환
        if (delta != 0)
        {
            int wd = GetWeekday();
            wd = WrapWeekday(wd + delta);
            SetWeekday(wd, notify: false);
        }

        NotifyChanged(); SnapshotValues();
    }

    public void SetPlayerName(string newName) { nowPlayer.Name = newName ?? ""; NotifyChanged(); SnapshotValues(); }

    // ==================== 언어 코드 API ====================

    /// <summary>
    /// 현재 언어 코드 문자열("ko","en","jp") 조회
    /// </summary>
    public string GetLanguageCode()
    {
        EnsureLanguageValid();
        return nowPlayer.Language;
    }

    /// <summary>
    /// 언어 코드 설정("ko","en","jp" 중 하나). 유효하지 않으면 "ko"로 보정.
    /// 필요 시 저장까지 바로 하고 싶으면 saveImmediately=true로 호출.
    /// </summary>
    public void SetLanguageCode(string code, bool saveImmediately = false)
    {
        string normalized = NormalizeLang(code);
        nowPlayer.Language = normalized;
        NotifyChanged();
        SnapshotValues();

        if (saveImmediately && nowSlot >= 0)
            SaveData();
    }

    private void EnsureLanguageValid()
    {
        nowPlayer.Language = NormalizeLang(nowPlayer.Language);
    }

    private string NormalizeLang(string code)
    {
        if (string.IsNullOrEmpty(code)) return "ko";
        switch (code.ToLowerInvariant())
        {
            case "ko": return "ko";
            case "en": return "en";
            case "jp":
            case "ja": // 호환 처리(ja를 받으면 jp로 저장)
                return "jp";
            default: return "ko";
        }
    }

    // ==================== 요일 유틸 ====================

    /// <summary>1~7 범위의 요일 반환(월=1 … 일=7). 저장값이 깨졌으면 보정.</summary>
    public int GetWeekday()
    {
        EnsureWeekdayValid();
        return nowPlayer.Weekday;
    }

    /// <summary>요일(1~7) 강제 지정. notify=false면 HUD는 호출부에서 묶어서 갱신 가능.</summary>
    public void SetWeekday(int weekday, bool notify = true)
    {
        nowPlayer.Weekday = WrapWeekday(weekday);
        if (notify) { NotifyChanged(); SnapshotValues(); }
    }

    /// <summary>토/일이면 주말</summary>
    public bool IsWeekend => GetWeekday() is 6 or 7;

    /// <summary>요일명(월/화/수/목/금/토/일) 반환</summary>
    public string GetWeekdayName()
    {
        int w = GetWeekday();
        return WEEK_KO[w];
    }

    /// <summary>Day 값을 기준으로 Weekday 재계산. Day=1 → baseWeekdayForDay1.</summary>
    public void RecomputeWeekdayFromDay()
    {
        int day = Mathf.Max(1, nowPlayer.Day);
        int baseW = WrapWeekday(baseWeekdayForDay1);
        // Day=1 → baseW, Day=2 → baseW+1 …
        int w = WrapWeekday(baseW + (day - 1));
        nowPlayer.Weekday = w;
    }

    private int WrapWeekday(int w)
    {
        // 1..7 범위로 순환
        int r = w % 7;
        if (r <= 0) r += 7;
        return r;
    }

    private void EnsureWeekdayValid()
    {
        if (nowPlayer.Weekday < 1 || nowPlayer.Weekday > 7)
        {
            // 저장값이 없거나 깨졌으면 Day 기준으로 재계산
            RecomputeWeekdayFromDay();
        }
    }

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

    public IEnumerator LoadSavedSceneAndPlacePlayer()
    {
        if (nowPlayer == null || string.IsNullOrEmpty(nowPlayer.Scene))
            yield break;

        string targetScene = nowPlayer.Scene;
        string currentScene = SceneManager.GetActiveScene().name;

        if (!string.Equals(targetScene, currentScene, StringComparison.Ordinal))
        {
            var op = SceneManager.LoadSceneAsync(targetScene);
            while (!op.isDone) yield return null;
        }

        if (applySavedPositionOnLoad && nowPlayer.HasSavedPosition)
            yield return ApplyPositionWhenReady();
    }

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

        var rb2 = player.GetComponent<Rigidbody2D>();
        if (rb2)
        {
            rb2.linearVelocity = Vector2.zero;
            rb2.angularVelocity = 0f;
            rb2.position = new Vector2(target.x, target.y);
            player.transform.position = target;
            yield break;
        }

        var rb3 = player.GetComponent<Rigidbody>();
        if (rb3)
        {
            rb3.linearVelocity = Vector3.zero;
            rb3.angularVelocity = Vector3.zero;
            rb3.position = target;
            player.transform.position = target;
            yield break;
        }

        player.transform.position = target;
    }

    // ==================== HUD ====================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void NotifyChanged() => UpdateHUD();

    void UpdateHUD()
    {
        if (coinText) coinText.text = string.Format(coinFormat, nowPlayer.Coin);
        if (levelText) levelText.text = string.Format(levelFormat, nowPlayer.Level);

        // Day + 요일
        if (dayText)
        {
            string weekdayName = GetWeekdayName(); // 월/화/수/목/금/토/일
            dayText.text = string.Format(dayFormat, nowPlayer.Day, weekdayName);
        }

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
        _lastWeekday = nowPlayer?.Weekday ?? 1;
        _lastName = nowPlayer?.Name ?? "";
        _lastLanguage = nowPlayer?.Language ?? "ko";
    }

    bool HasValueChanged()
    {
        if (nowPlayer == null) return false;
        return _lastCoin != nowPlayer.Coin
            || _lastLevel != nowPlayer.Level
            || _lastDay != nowPlayer.Day
            || _lastWeekday != (nowPlayer.Weekday < 1 || nowPlayer.Weekday > 7 ? WrapWeekday(nowPlayer.Weekday) : nowPlayer.Weekday)
            || _lastName != (nowPlayer.Name ?? "")
            || _lastLanguage != (string.IsNullOrEmpty(nowPlayer.Language) ? "ko" : nowPlayer.Language);
    }
}
