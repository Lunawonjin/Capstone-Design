// DataManager.cs (Unity 6 LTS) 
// 주석은 모두 한국어. 전체 코드 누락 없음.
// 변경점 요약:
// 1) AutoRebindHUDIfNeeded 메서드 개선 (FindObjectsByType 사용)
// 2) AddDay에서 하루가 바뀔 때 NPC Today_Talk 플래그 리셋
// 3) 기존 기능(저장/로드, 스냅샷 등) 유지
// 4) Sol_Second_Meet 이벤트 플래그 추가(PlayerData)

using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#region 저장 포맷(활성/비활성 오브젝트 기록)

// 활성/비활성 오브젝트 스냅샷(씬 저장용)
[Serializable]
public class ActiveObjectInfo
{
    public string HierarchyPath;    // 예: "Environment/Trees/Tree_01"
    public string Name;             // GameObject.name
    public string Tag;              // GameObject.tag (Untagged 포함)
    public bool ActiveSelf;         // go.activeSelf (복원에 사용)
    public bool ActiveInHierarchy;  // 저장 시점의 계층 활성 상태(디버깅 참고)
}

#endregion

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

    // 요일(1~7)
    public int Weekday;

    // 언어 코드("ko","en","jp")
    public string Language;

    //문자 조건
    public bool StartGame;

    //첫날밤 잘수있는 조건
    public bool CanFirstSleep;

    //첫 방문 지역 이벤트
    public bool Starest_First_Visit;

    //오늘 하루 말을 NPC에게 걸었는지 확인하는 Bool값
    public bool Sol_Today_Talk;
    public bool Salt_Today_Talk;
    public bool Ryu_Today_Talk;
    public bool White_Today_Talk;

    // 첫 만남 플래그
    public bool Sol_First_Meet;
    public bool Salt_First_Meet;
    public bool Ryu_First_Meet;
    public bool White_First_Meet;

    // 두 번째 만남 플래그 예시 (Sol_Second_Meet 이벤트용)
    public bool Sol_Second_Meet;
    public bool Boss_SaltKey_Lost;
    public bool Boss_Seconday_Busstop;
    public int Sol_FriendShip;
    public int Salt_FriendShip;
    public int Ryu_FriendShip;
    public int White_FriendShip;

    //다이어리 해금
    public bool DiaryOpen;

    //소금이 집 해금
    public bool Salt_House_Key;
    public bool Boss_Sol_FinalGame;
    public bool Sol_Puzzle_Clear;

    //메신저 상태
    public List<string> MessengerDelivered = new List<string>();
    public List<string> MessengerReadList = new List<string>();

    // 씬 오브젝트 스냅샷(활성/비활성 모두)
    public string ActiveSceneName;
    public ActiveObjectInfo[] ActiveObjects;

    public PlayerData()
    {
        Name = "Player";
        Level = 1;
        Coin = 0;
        Day = 1;
        Item = 0;

        Px = Py = Pz = 0f;
        HasSavedPosition = false;

        Scene = "";
        Weekday = 1;
        Language = "ko";

        StartGame = false;
        CanFirstSleep = false;

        Sol_Today_Talk = false;
        Salt_Today_Talk = false;
        Ryu_Today_Talk = false;
        White_Today_Talk = false;

        Starest_First_Visit = false;
        Boss_SaltKey_Lost = false;
        Sol_First_Meet = false;
        Salt_First_Meet = false;
        Ryu_First_Meet = false;
        White_First_Meet = false;

        // 두 번째 만남 플래그 초기값
        Sol_Second_Meet = false;

        Sol_FriendShip = 0;
        Salt_FriendShip = 0;
        Ryu_FriendShip = 0;
        White_FriendShip = 0;

        Boss_Sol_FinalGame = false;
        DiaryOpen = false;
        Salt_House_Key = false;
        Sol_Puzzle_Clear = false;
        Boss_Seconday_Busstop=false;

        ActiveSceneName = "";
        ActiveObjects = Array.Empty<ActiveObjectInfo>();

        MessengerDelivered = new List<string>();
        MessengerReadList = new List<string>();
    }
}

public class DataManager : MonoBehaviour
{
    public static DataManager instance;

    [Header("플레이어/저장 슬롯")]
    public PlayerData nowPlayer = new PlayerData();
    public string path;      // persistentDataPath/save
    public int nowSlot = -1; // 현재 선택된 저장 슬롯

    [Header("임시 저장 (이벤트/씬 복귀용)")]
    public string subPath;               // persistentDataPath/sub_save
    private string _tempSavePath = null; // 임시(nowPlayer 전체) 파일 경로
    private bool _isQuitting = false;    // 앱 종료 중
    private bool _sessionSaved = false;  // 이번 실행에서 정식 SaveData 수행 여부

    [Header("저장 불가 씬 (메뉴 등)")]
    [SerializeField] private string[] nonGameplayScenes = new string[] { "StartMenu" };

    [Header("HUD(TextMeshProUGUI)")]
    [SerializeField] private TMP_Text coinText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private TMP_Text nameText;

    [Header("호감도 UI(TextMeshProUGUI)")]
    [SerializeField] private TMP_Text solFriendshipText;
    [SerializeField] private TMP_Text saltFriendshipText;
    [SerializeField] private TMP_Text ryuFriendshipText;
    [SerializeField] private TMP_Text whiteFriendshipText;

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
    [SerializeField] private string solFriendshipObjectName = "Text_Sol_Friendship";
    [SerializeField] private string saltFriendshipObjectName = "Text_Salt_Friendship";
    [SerializeField] private string ryuFriendshipObjectName = "Text_Ryu_Friendship";
    [SerializeField] private string whiteFriendshipObjectName = "Text_White_Friendship";

    [Header("표기 형식(언어별)")]
    [SerializeField] private string dayFormatKo = "{0}일 ({1})";
    [SerializeField] private string dayFormatEn = "Day {0} ({1})";
    [SerializeField] private string dayFormatJp = "{0}日（{1）";

    [Header("호감도 표기 형식(언어별)")]
    [SerializeField] private string friendshipFormatKo = "{0} 호감도: {1}";
    [SerializeField] private string friendshipFormatEn = "{0} Frendship: {1}";
    [SerializeField] private string friendshipFormatJp = "{0} 好感度: {1}";

    [Header("캐릭터 이름 (언어별)")]
    [SerializeField] private string solNameKo = "솔";
    [SerializeField] private string solNameEn = "Sol";
    [SerializeField] private string solNameJp = "ソル";
    [Space(10)]
    [SerializeField] private string saltNameKo = "소금";
    [SerializeField] private string saltNameEn = "Sogeum";
    [SerializeField] private string saltNameJp = "ソグミ";
    [Space(10)]
    [SerializeField] private string ryuNameKo = "류지현";
    [SerializeField] private string ryuNameEn = "Ryu Jihyeon";
    [SerializeField] private string ryuNameJp = "リュ・ジヒョン";
    [Space(10)]
    [SerializeField] private string whiteNameKo = "천하얀";
    [SerializeField] private string whiteNameEn = "WhiteCheon Hayan";
    [SerializeField] private string whiteNameJp = "チョン・ハヤン";

    [Header("기타 표기 형식")]
    [SerializeField] private string coinFormat = "{0}";
    [SerializeField] private string levelFormat = "Lv. {0}";
    [SerializeField] private string nameFormat = "{0}";

    [Header("요일 동기화 옵션")]
    [Range(1, 7)][SerializeField] private int baseWeekdayForDay1 = 1;
    [SerializeField] private bool autoSyncWeekdayOnSetDay = true;

    [Header("플레이어 위치 적용 옵션")]
    [SerializeField] private bool applySavedPositionOnLoad = true;
    [SerializeField] private string playerTagForReposition = "Player";
    [SerializeField] private float applyPosTimeoutSec = 3f;

    [Header("자동 씬 로드 옵션")]
    public bool autoLoadSavedSceneOnStart = false;

    // 언어별 요일 이름표(1~7; 0 미사용)
    private static readonly string[] WEEK_KO = { "", "월", "화", "수", "목", "금", "토", "일" };
    private static readonly string[] WEEK_EN = { "", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    private static readonly string[] WEEK_JP = { "", "月", "火", "水", "木", "金", "土", "日" };

    [Header("Day 텍스트 폰트/사이즈(언어별)")]
    [SerializeField] private TMP_FontAsset fontKo;
    [SerializeField] private TMP_FontAsset fontEn;
    [SerializeField] private TMP_FontAsset fontJp;

    [SerializeField] private int dayFontSizeKo = 45;
    [SerializeField] private int dayFontSizeEn = 35;
    [SerializeField] private int dayFontSizeJp = 35;

    [Header("호감도 텍스트 폰트/사이즈(언어별)")]
    [SerializeField] private TMP_FontAsset friendshipFontKo;
    [SerializeField] private TMP_FontAsset friendshipFontEn;
    [SerializeField] private TMP_FontAsset friendshipFontJp;

    [SerializeField] private int friendshipFontSizeKo = 30;
    [SerializeField] private int friendshipFontSizeEn = 30;
    [SerializeField] private int friendshipFontSizeJp = 30;

    [Header("오브젝트 저장/복원 옵션")]
    [Tooltip("기록/복원에서 제외할 태그(예: HUD, UIPanel 등)")]
    [SerializeField] private string[] excludeTagsForActiveObjects = new string[] { "HUD", "UIPanel" };

    [Tooltip("기록/복원에서 제외할 이름(정확 일치)")]
    [SerializeField] private string[] excludeNamesForActiveObjects = new string[] { "UIPanel" };

    public enum ActiveRestoreMode
    {
        ExactMatchSavedActiveSelf,   // 스냅샷 ActiveSelf 그대로 적용
        OnlyListedExactMatch         // 기록된 것만 ActiveSelf 적용(기록 없는 애는 건드리지 않음)
    }

    [Tooltip("씬 로드 시 자동으로 오브젝트 상태를 복원")]
    [SerializeField] private bool autoRestoreActiveObjectsOnSceneLoaded = true;

    [Tooltip("복원 모드(기본: 정확히 일치)")]
    [SerializeField] private ActiveRestoreMode activeRestoreMode = ActiveRestoreMode.ExactMatchSavedActiveSelf;

    [Tooltip("복원/저장 로그 상세 출력")]
    [SerializeField] private bool logRestoreDetails = false;

    // 변경 감지 스냅샷
    int _lastCoin = int.MinValue, _lastLevel = int.MinValue, _lastDay = int.MinValue, _lastWeekday = int.MinValue;
    string _lastName = null, _lastLanguage = null;
    int _lastSolFriendship = int.MinValue, _lastSaltFriendship = int.MinValue, _lastRyuFriendship = int.MinValue, _lastWhiteFriendship = int.MinValue;

    // 씬별 스냅샷 파일 포맷/경로
    [Serializable]
    private class SceneObjectSnapshot
    {
        public string SceneName;
        public ActiveObjectInfo[] Objects;
    }

    private string GetSceneActivesTempPathForSlot(int slot, string sceneName)
    {
        string safe = SanitizeSceneName(sceneName);
        return Path.Combine(subPath, $"slot_{slot}_{safe}_actives.json");
    }

    private string GetSceneActivesSavePathForSlot(int slot, string sceneName)
    {
        string safe = SanitizeSceneName(sceneName);
        return Path.Combine(path, $"slot_{slot}_{safe}_actives.json");
    }

    private static string SanitizeSceneName(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return "UnknownScene";
        foreach (var c in Path.GetInvalidFileNameChars())
            sceneName = sceneName.Replace(c, '_');
        sceneName = sceneName.Replace('/', '_').Replace('\\', '_');
        return sceneName;
    }

    // ───────── Lifecycle ─────────
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

        path = Path.Combine(Application.persistentDataPath, "save");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        subPath = Path.Combine(Application.persistentDataPath, "sub_save");
        if (!Directory.Exists(subPath)) Directory.CreateDirectory(subPath);

        // 시작 시 임시 파일(지난 세션 잔여물) 정리
        CleanupAllSubSaves();

        SceneManager.sceneLoaded += OnSceneLoaded_RebindHUD_Apply_And_Restore;

        EnsureWeekdayValid();
        EnsureLanguageValid();
        SnapshotValues();
    }

    void Start()
    {
        if (refreshHUDOnStart) UpdateHUD();

        if (autoLoadSavedSceneOnStart && HasAnySave())
        {
            if (TryLoadMostRecentSave())
                StartCoroutine(LoadSavedSceneAndPlacePlayer());
        }
    }

    void OnApplicationQuit()
    {
        _isQuitting = true;

        // 저장하지 않고 종료하면 씬 스냅샷/임시 저장 제거
        if (!_sessionSaved)
        {
            CleanupAllSubSaves();

            try
            {
                var stray = Directory.GetFiles(path, "slot_*_*_actives.json", SearchOption.TopDirectoryOnly);
                foreach (var f in stray) File.Delete(f);
                if (stray.Length > 0 && logRestoreDetails)
                    Debug.Log($"[DataManager] 세션 저장 없음 → 정식 씬 스냅샷 {stray.Length}개 제거");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DataManager] 종료 시 정식 씬 스냅샷 정리 중 예외: {e.Message}");
            }
        }
        else
        {
            CleanupAllSubSaves();
        }
    }

    void OnDestroy()
    {
        if (instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded_RebindHUD_Apply_And_Restore;

        if (_isQuitting)
            CleanupAllSubSaves();
    }

    void LateUpdate()
    {
        if (HasValueChanged())
        {
            UpdateHUD();
            SnapshotValues();
        }
    }

    // ───────── 저장/로드/삭제 ─────────
    public string GetSlotFullPath(int slot)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return Path.Combine(path, $"slot_{slot}.json");
    }

    public void SaveData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] nowSlot 미지정");
            return;
        }

        var activeScene = SceneManager.GetActiveScene();
        if (nonGameplayScenes != null && nonGameplayScenes.Any(n => SceneNameEquals(activeScene.name, n)))
        {
            Debug.LogWarning($"[DataManager] '{activeScene.name}' 씬에서는 저장하지 않습니다.");
            return;
        }

        try
        {
            if (nowPlayer.Level < 1) nowPlayer.Level = 1;
            if (nowPlayer.Day < 1) nowPlayer.Day = 1;
            EnsureWeekdayValid();
            EnsureLanguageValid();

            // 플레이어 위치 기록(가능 시)
            var player = FindPlayer();
            if (player != null)
            {
                var p = player.position;
                nowPlayer.Px = p.x; nowPlayer.Py = p.y; nowPlayer.Pz = p.z;
                nowPlayer.HasSavedPosition = true;
            }

            // 씬/오브젝트 스냅샷(활성/비활성 모두)
            nowPlayer.Scene = activeScene.name;
            nowPlayer.ActiveSceneName = activeScene.name;
            nowPlayer.ActiveObjects = CaptureAllObjectsSnapshotForScene(activeScene);

            // 1) 메인 세이브
            string file = GetSlotFullPath(nowSlot);
            File.WriteAllText(file, JsonUtility.ToJson(nowPlayer, false));

            // 2) 씬 스냅샷을 /save에 보관
            string sceneSnapshotPath = GetSceneActivesSavePathForSlot(nowSlot, activeScene.name);
            var wrapper = new SceneObjectSnapshot { SceneName = activeScene.name, Objects = nowPlayer.ActiveObjects };
            File.WriteAllText(sceneSnapshotPath, JsonUtility.ToJson(wrapper, false));

            _sessionSaved = true;
            if (logRestoreDetails)
                Debug.Log($"[DataManager] 저장 완료: {file}\n＋ 씬 스냅샷 → {sceneSnapshotPath} (count={nowPlayer.ActiveObjects?.Length ?? 0})");

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

        string file = GetSlotFullPath(nowSlot);
        if (!File.Exists(file))
        {
            Debug.LogError("[DataManager] 파일 없음: " + file);
            return;
        }

        try
        {
            nowPlayer = JsonUtility.FromJson<PlayerData>(File.ReadAllText(file)) ?? new PlayerData();

            if (nowPlayer.Level < 1) nowPlayer.Level = 1;
            if (nowPlayer.Day < 1) nowPlayer.Day = 1;
            if (!nowPlayer.HasSavedPosition && (nowPlayer.Px != 0f || nowPlayer.Py != 0f || nowPlayer.Pz != 0f))
                nowPlayer.HasSavedPosition = true;

            if (nowPlayer.Weekday < 1 || nowPlayer.Weekday > 7)
                RecomputeWeekdayFromDay();

            EnsureLanguageValid();

            NotifyChanged();
            SnapshotValues();

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
        if (autoSyncWeekdayOnSetDay) RecomputeWeekdayFromDay();
        EnsureLanguageValid();
        NotifyChanged();
        SnapshotValues();
    }

    public bool ExistsSlot(int slot)
    {
        if (slot < 0) return false;
        return File.Exists(GetSlotFullPath(slot));
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
            string f = GetSlotFullPath(i);
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
        string f = GetSlotFullPath(slot);
        if (!File.Exists(f)) return false;

        try
        {
            File.Delete(f);

            // 해당 슬롯의 씬별 스냅샷들도 정리
            var stray = Directory.GetFiles(path, $"slot_{slot}_*_actives.json", SearchOption.TopDirectoryOnly);
            foreach (var s in stray) File.Delete(s);

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

    // ───────── 값 변경 API ─────────
    public void SetCoin(int coin) { nowPlayer.Coin = Math.Max(0, coin); NotifyChanged(); SnapshotValues(); }
    public void AddCoin(int delta)
    {
        long v = (long)nowPlayer.Coin + delta;
        nowPlayer.Coin = (int)Mathf.Clamp(v, 0, int.MaxValue);
        NotifyChanged(); SnapshotValues();
    }

    public void SetLevel(int level) { nowPlayer.Level = Mathf.Max(1, level); NotifyChanged(); SnapshotValues(); }
    public void AddLevel(int delta) { nowPlayer.Level = Mathf.Max(1, nowPlayer.Level + delta); NotifyChanged(); SnapshotValues(); }

    public void SetDay(int day)
    {
        nowPlayer.Day = Mathf.Max(1, day);
        if (autoSyncWeekdayOnSetDay) RecomputeWeekdayFromDay();
        NotifyChanged(); SnapshotValues();
    }

    public void AddDay(int delta)
    {
        nowPlayer.Day = Mathf.Max(1, nowPlayer.Day + delta);
        if (delta != 0)
        {
            // 요일 갱신
            int wd = GetWeekday();
            wd = WrapWeekday(wd + delta);
            SetWeekday(wd, notify: false);

            // 하루가 바뀌었으므로 NPC Today_Talk 플래그 리셋
            ResetNpcTodayTalkFlags();
        }
        NotifyChanged(); SnapshotValues();
    }

    public void SetPlayerName(string newName) { nowPlayer.Name = newName ?? ""; NotifyChanged(); SnapshotValues(); }

    // 하루가 바뀔 때 NPC Today_Talk 플래그를 초기화하는 메서드
    private void ResetNpcTodayTalkFlags()
    {
        if (nowPlayer == null) return;

        nowPlayer.Sol_Today_Talk = false;
        nowPlayer.Salt_Today_Talk = false;
        nowPlayer.Ryu_Today_Talk = false;
        nowPlayer.White_Today_Talk = false;
    }

    // ───────── 언어/요일 유틸 ─────────
    public string GetLanguageCode()
    {
        EnsureLanguageValid();
        return nowPlayer.Language;
    }

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
            case "ja": return "jp";
            default: return "ko";
        }
    }

    private string CurrentLang() =>
        string.IsNullOrEmpty(nowPlayer?.Language) ? "ko" : NormalizeLang(nowPlayer.Language);

    public string GetWeekdayNameLocalized(string langCode = null)
    {
        int w = GetWeekday(); // 1~7
        string code = NormalizeLang(langCode ?? CurrentLang());
        return code switch
        {
            "en" => WEEK_EN[w],
            "jp" => WEEK_JP[w],
            _ => WEEK_KO[w],
        };
    }

    public string FormatDayAndWeekLocalized(int day, int weekday, string langCode = null)
    {
        string code = NormalizeLang(langCode ?? CurrentLang());
        string weekdayName = code switch
        {
            "en" => WEEK_EN[weekday],
            "jp" => WEEK_JP[weekday],
            _ => WEEK_KO[weekday],
        };

        return code switch
        {
            "en" => string.Format(dayFormatEn, day, weekdayName),
            "jp" => string.Format(dayFormatJp, day, weekdayName),
            _ => string.Format(dayFormatKo, day, weekdayName),
        };
    }

    public int GetWeekday()
    {
        EnsureWeekdayValid();
        return nowPlayer.Weekday;
    }

    public void SetWeekday(int weekday, bool notify = true)
    {
        nowPlayer.Weekday = WrapWeekday(weekday);
        if (notify) { NotifyChanged(); SnapshotValues(); }
    }

    public bool IsWeekend => GetWeekday() is 6 or 7;

    public void RecomputeWeekdayFromDay()
    {
        int day = Mathf.Max(1, nowPlayer.Day);
        int baseW = WrapWeekday(baseWeekdayForDay1);
        int w = WrapWeekday(baseW + (day - 1));
        nowPlayer.Weekday = w;
    }

    private int WrapWeekday(int w)
    {
        int r = w % 7;
        if (r <= 0) r += 7;
        return r;
    }

    private void EnsureWeekdayValid()
    {
        if (nowPlayer.Weekday < 1 || nowPlayer.Weekday > 7)
        {
            RecomputeWeekdayFromDay();
        }
    }

    // ───────── 위치/씬 저장 및 적용 ─────────
    public void SetPlayerPosition(Vector3 pos)
    {
        nowPlayer.Px = pos.x;
        nowPlayer.Py = pos.y;
        nowPlayer.Pz = pos.z;
        nowPlayer.HasSavedPosition = true;
    }

    public void SetSceneName(string sceneName) => nowPlayer.Scene = sceneName ?? "";

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

        yield return null;

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

    // ───────── HUD ─────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void NotifyChanged() => UpdateHUD();

    private void ApplyDayStylePerLanguage(string langCode)
    {
        if (!dayText) return;

        switch (NormalizeLang(langCode))
        {
            case "ko":
                if (fontKo) dayText.font = fontKo;
                dayText.fontSize = dayFontSizeKo;
                break;

            case "jp":
                if (fontJp) dayText.font = fontJp;
                dayText.fontSize = dayFontSizeJp;
                break;

            default:  // en
                if (fontEn) dayText.font = fontEn;
                dayText.fontSize = dayFontSizeEn;
                break;
        }

        dayText.ForceMeshUpdate();
    }

    private void ApplyFriendshipStylePerLanguage(string langCode)
    {
        TMP_FontAsset targetFont = null;
        int targetSize = 30;

        switch (NormalizeLang(langCode))
        {
            case "ko":
                targetFont = friendshipFontKo;
                targetSize = friendshipFontSizeKo;
                break;
            case "jp":
                targetFont = friendshipFontJp;
                targetSize = friendshipFontSizeJp;
                break;
            default: // en
                targetFont = friendshipFontEn;
                targetSize = friendshipFontSizeEn;
                break;
        }

        var friendshipTexts = new[] { solFriendshipText, saltFriendshipText, ryuFriendshipText, whiteFriendshipText };
        foreach (var text in friendshipTexts)
        {
            if (text)
            {
                if (targetFont) text.font = targetFont;
                text.fontSize = targetSize;
            }
        }
    }

    void UpdateHUD()
    {
        if (coinText) coinText.text = string.Format(coinFormat, nowPlayer.Coin);
        if (levelText) levelText.text = string.Format(levelFormat, nowPlayer.Level);

        string lang = CurrentLang();

        if (dayText)
        {
            int wd = GetWeekday();
            dayText.text = FormatDayAndWeekLocalized(nowPlayer.Day, wd, lang);
            ApplyDayStylePerLanguage(lang);
        }

        if (nameText)
        {
            string nm = string.IsNullOrEmpty(nowPlayer.Name) ? "No Name" : nowPlayer.Name;
            nameText.text = string.Format(nameFormat, nm);
        }

        ApplyFriendshipStylePerLanguage(lang);

        string format = lang switch
        {
            "en" => friendshipFormatEn,
            "jp" => friendshipFormatJp,
            _ => friendshipFormatKo
        };

        if (solFriendshipText)
        {
            string n = lang switch { "en" => solNameEn, "jp" => solNameJp, _ => solNameKo };
            solFriendshipText.text = string.Format(format, n, nowPlayer.Sol_FriendShip);
        }
        if (saltFriendshipText)
        {
            string n = lang switch { "en" => saltNameEn, "jp" => saltNameJp, _ => saltNameKo };
            saltFriendshipText.text = string.Format(format, n, nowPlayer.Salt_FriendShip);
        }
        if (ryuFriendshipText)
        {
            string n = lang switch { "en" => ryuNameEn, "jp" => ryuNameJp, _ => ryuNameKo };
            ryuFriendshipText.text = string.Format(format, n, nowPlayer.Ryu_FriendShip);
        }
        if (whiteFriendshipText)
        {
            string n = lang switch { "en" => whiteNameEn, "jp" => whiteNameJp, _ => whiteNameKo };
            whiteFriendshipText.text = string.Format(format, n, nowPlayer.White_FriendShip);
        }
    }

    public void BindHUD(TMP_Text coin, TMP_Text level, TMP_Text day = null, TMP_Text name = null,
                        TMP_Text solFriendship = null, TMP_Text saltFriendship = null, TMP_Text ryuFriendship = null, TMP_Text whiteFriendship = null)
    {
        coinText = coin;
        levelText = level;
        dayText = day;
        nameText = name;

        solFriendshipText = solFriendship;
        saltFriendshipText = saltFriendship;
        ryuFriendshipText = ryuFriendship;
        whiteFriendshipText = whiteFriendship;

        if (dontDestroyOnLoadHUD)
        {
            if (coinText) DontDestroyOnLoad(coinText.gameObject);
            if (levelText) DontDestroyOnLoad(levelText.gameObject);
            if (dayText) DontDestroyOnLoad(dayText.gameObject);
            if (nameText) DontDestroyOnLoad(nameText.gameObject);

            if (solFriendshipText) DontDestroyOnLoad(solFriendshipText.gameObject);
            if (saltFriendshipText) DontDestroyOnLoad(saltFriendshipText.gameObject);
            if (ryuFriendshipText) DontDestroyOnLoad(ryuFriendshipText.gameObject);
            if (whiteFriendshipText) DontDestroyOnLoad(whiteFriendshipText.gameObject);
        }

        UpdateHUD();
        SnapshotValues();
    }

    public void RebindHUDNow()
    {
        OnSceneLoaded_RebindHUD_Apply_And_Restore(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    // ───────── 씬 로드시: HUD/포지션/스냅샷 복원 ─────────
    private void OnSceneLoaded_RebindHUD_Apply_And_Restore(Scene scene, LoadSceneMode mode)
    {
        // 임시(nowPlayer) 저장이 있다면 우선 반영 후 삭제
        TryLoadAndDeleteSubSave();

        if (autoRebindOnSceneLoaded)
        {
            AutoRebindHUDIfNeeded();
        }

        if (applySavedPositionOnLoad && nowPlayer != null && nowPlayer.HasSavedPosition)
            StartCoroutine(ApplyPositionWhenReady());

        // 씬별 임시 스냅샷(sub_save)이 존재하면 우선 적용
        TryApplySubSaveSceneSnapshotForCurrentScene();

        if (autoRestoreActiveObjectsOnSceneLoaded)
        {
            // 정식 세이브의 스냅샷으로도 복원 시도
            ApplyActiveObjectsSnapshotNow();
        }
    }

    // [디버깅용] 수정된 AutoRebindHUDIfNeeded
    private void AutoRebindHUDIfNeeded()
    {
        // 1. 필요한지 체크
        bool needCoin = coinText == null;
        bool needLevel = levelText == null;
        bool needDay = dayText == null && !string.IsNullOrEmpty(dayObjectName);
        bool needName = nameText == null && !string.IsNullOrEmpty(nameObjectName);
        bool needSol = solFriendshipText == null && !string.IsNullOrEmpty(solFriendshipObjectName);
        bool needSalt = saltFriendshipText == null && !string.IsNullOrEmpty(saltFriendshipObjectName);
        bool needRyu = ryuFriendshipText == null && !string.IsNullOrEmpty(ryuFriendshipObjectName);
        bool needWhite = whiteFriendshipText == null && !string.IsNullOrEmpty(whiteFriendshipObjectName);

        if (!(needCoin || needLevel || needDay || needName || needSol || needSalt || needRyu || needWhite))
        {
            // Debug.Log("[DataManager] 모든 UI가 이미 연결되어 있어 검색을 건너뜁니다.");
            return;
        }

        Debug.Log($"[DataManager] UI 자동 연결 시작... (Coin필요: {needCoin}, Day필요: {needDay})");

        // 2. 검색 루트(부모) 설정
        Transform root = null;
        if (!string.IsNullOrEmpty(hudRootTag))
        {
            var hudRootGO = GameObject.FindWithTag(hudRootTag);
            if (hudRootGO)
            {
                root = hudRootGO.transform;
                Debug.Log($"[DataManager] 태그('{hudRootTag}')를 가진 부모 객체 '{hudRootGO.name}' 하위에서 검색합니다.");
            }
            else
            {
                Debug.LogWarning($"[DataManager] 경고: HudRootTag가 '{hudRootTag}'로 설정되어 있으나, 해당 태그를 가진 오브젝트를 씬에서 찾을 수 없습니다. (전체 검색으로 전환합니다)");
            }
        }
        else
        {
            Debug.Log("[DataManager] HudRootTag가 비어있어 씬 전체(비활성 포함)에서 검색합니다.");
        }

        // 3. 검색 함수
        TMP_Text FindTMP(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;

            if (root)
            {
                // 루트 하위 검색
                foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.name == n)
                    {
                        Debug.Log($"[DataManager] 성공: '{n}' 오브젝트를 찾았습니다.");
                        return t;
                    }
                }
            }
            else
            {
                // 전체 검색 (Unity 6 / 2023+ FindObjectsByType)
                var allSceneTexts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var t in allSceneTexts)
                {
                    if (t.name == n)
                    {
                        Debug.Log($"[DataManager] 성공: '{n}' 오브젝트를 찾았습니다.");
                        return t;
                    }
                }
            }

            // 여기만 Error → Warning 으로 변경됨
            Debug.LogWarning($"[DataManager] 경고: 이름이 '{n}'인 TMP_Text 오브젝트를 찾지 못했습니다. 이름을 확인해주세요.");
            return null;
        }

        // 4. 연결 시도
        var fc = needCoin ? FindTMP(coinObjectName) : coinText;
        var fl = needLevel ? FindTMP(levelObjectName) : levelText;
        var fd = needDay ? FindTMP(dayObjectName) : dayText;
        var fn = needName ? FindTMP(nameObjectName) : nameText;
        var fSol = needSol ? FindTMP(solFriendshipObjectName) : solFriendshipText;
        var fSalt = needSalt ? FindTMP(saltFriendshipObjectName) : saltFriendshipText;
        var fRyu = needRyu ? FindTMP(ryuFriendshipObjectName) : ryuFriendshipText;
        var fWhite = needWhite ? FindTMP(whiteFriendshipObjectName) : whiteFriendshipText;

        if (fc || fl || fd || fn || fSol || fSalt || fRyu || fWhite)
            BindHUD(fc, fl, fd, fn, fSol, fSalt, fRyu, fWhite);
    }

    // ───────── 변경 감지 스냅샷 ─────────
    void SnapshotValues()
    {
        _lastCoin = nowPlayer?.Coin ?? 0;
        _lastLevel = nowPlayer?.Level ?? 1;
        _lastDay = nowPlayer?.Day ?? 1;
        _lastWeekday = nowPlayer?.Weekday ?? 1;
        _lastName = nowPlayer?.Name ?? "";
        _lastLanguage = nowPlayer?.Language ?? "ko";

        if (nowPlayer != null)
        {
            _lastSolFriendship = nowPlayer.Sol_FriendShip;
            _lastSaltFriendship = nowPlayer.Salt_FriendShip;
            _lastRyuFriendship = nowPlayer.Ryu_FriendShip;
            _lastWhiteFriendship = nowPlayer.White_FriendShip;
        }
    }

    bool HasValueChanged()
    {
        if (nowPlayer == null) return false;
        return _lastCoin != nowPlayer.Coin
            || _lastLevel != nowPlayer.Level
            || _lastDay != nowPlayer.Day
            || _lastWeekday != (nowPlayer.Weekday < 1 || nowPlayer.Weekday > 7 ? WrapWeekday(nowPlayer.Weekday) : nowPlayer.Weekday)
            || _lastName != (nowPlayer.Name ?? "")
            || _lastLanguage != (string.IsNullOrEmpty(nowPlayer.Language) ? "ko" : nowPlayer.Language)
            || _lastSolFriendship != nowPlayer.Sol_FriendShip
            || _lastSaltFriendship != nowPlayer.Salt_FriendShip
            || _lastRyuFriendship != nowPlayer.Ryu_FriendShip
            || _lastWhiteFriendship != nowPlayer.White_FriendShip;
    }

    // ───────── 활성/비활성 전체 스냅샷 캡쳐/복원 ─────────
    private ActiveObjectInfo[] CaptureAllObjectsSnapshotForScene(Scene scene)
    {
        var list = new List<ActiveObjectInfo>(512);
        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            if (!root) continue;

            var all = root.GetComponentsInChildren<Transform>(true); // 비활성 포함
            foreach (var tr in all)
            {
                var go = tr.gameObject;
                if (!go) continue;
                if (go.scene != scene || !go.scene.isLoaded) continue;
                if (go.hideFlags != HideFlags.None) continue;
                if (ShouldExclude(go)) continue;

                list.Add(new ActiveObjectInfo
                {
                    HierarchyPath = BuildHierarchyPath(tr),
                    Name = go.name,
                    Tag = SafeTag(go),
                    ActiveSelf = go.activeSelf,
                    ActiveInHierarchy = go.activeInHierarchy
                });
            }
        }
        return list.ToArray();
    }

    public void ApplyActiveObjectsSnapshotNow()
    {
        try { ApplyActiveObjectsSnapshotInternal(); }
        catch (Exception e) { Debug.LogError($"[DataManager] ApplyActiveObjectsSnapshotNow 실패: {e}"); }
    }

    private void ApplyActiveObjectsSnapshotInternal()
    {
        if (nowPlayer == null || nowPlayer.ActiveObjects == null) return;

        string currentSceneName = SceneManager.GetActiveScene().name;
        string snapshotSceneName = nowPlayer.ActiveSceneName ?? "";

        if (!string.Equals(currentSceneName, snapshotSceneName, StringComparison.Ordinal))
        {
            if (logRestoreDetails)
                Debug.LogWarning($"[DataManager] 스냅샷 씬('{snapshotSceneName}')과 현재 씬('{currentSceneName}')이 다릅니다. 복원 중단.");
            return;
        }

        var sceneMap = BuildSceneObjectMap(); // 경로 → GameObject

        int applied = 0, missing = 0;
        foreach (var info in nowPlayer.ActiveObjects)
        {
            if (info == null || string.IsNullOrEmpty(info.HierarchyPath)) continue;

            if (sceneMap.TryGetValue(info.HierarchyPath, out var go))
            {
                if (activeRestoreMode == ActiveRestoreMode.ExactMatchSavedActiveSelf ||
                    activeRestoreMode == ActiveRestoreMode.OnlyListedExactMatch)
                {
                    if (go.activeSelf != info.ActiveSelf)
                    {
                        go.SetActive(info.ActiveSelf);
                        applied++;
                    }
                }
            }
            else
            {
                missing++;
                if (logRestoreDetails)
                    Debug.Log($"[DataManager] 복원 대상 경로 누락: {info.HierarchyPath}");
            }
        }

        if (logRestoreDetails)
            Debug.Log($"[DataManager] 스냅샷 복원 완료 — 적용:{applied}, 경로누락:{missing}, 씬:'{currentSceneName}'");
    }

    private Dictionary<string, GameObject> BuildSceneObjectMap()
    {
        var map = new Dictionary<string, GameObject>(1024, StringComparer.Ordinal);
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return map;

        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            if (!root) continue;

            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var tr in all)
            {
                var go = tr.gameObject;
                if (!go) continue;
                if (go.scene != scene || !go.scene.isLoaded) continue;
                if (go.hideFlags != HideFlags.None) continue;
                if (ShouldExclude(go)) continue;

                string path = BuildHierarchyPath(tr);
                if (!map.ContainsKey(path))
                    map.Add(path, go);
            }
        }

        return map;
    }

    // ───────── SubSave(임시) — nowPlayer 전체/씬별 스냅샷 ─────────
    string GetTempPathForSlot(int slot) => Path.Combine(subPath, $"slot_{slot}_temp.json");

    public void SubSaveCommit() // nowPlayer 전체 임시 저장
    {
        if (nowSlot < 0)
        {
            Debug.LogWarning("[DataManager] SubSaveCommit: nowSlot 미지정");
            return;
        }
        try
        {
            string file = GetTempPathForSlot(nowSlot);
            File.WriteAllText(file, JsonUtility.ToJson(nowPlayer, false));
            if (logRestoreDetails) Debug.Log($"[DataManager] SubSaveCommit → {file}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] SubSaveCommit 실패: {e}");
        }
    }

    public bool TryLoadAndDeleteSubSave()
    {
        if (nowSlot < 0) return false;
        string file = GetTempPathForSlot(nowSlot);
        if (!File.Exists(file)) return false;

        try
        {
            var json = File.ReadAllText(file);
            var tmp = JsonUtility.FromJson<PlayerData>(json);
            if (tmp != null) nowPlayer = tmp;
            if (logRestoreDetails) Debug.Log($"[DataManager] SubSave 로드 성공 → {file}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] SubSave 로드 실패: {e}");
        }
        finally
        {
            try
            {
                File.Delete(file);
                if (logRestoreDetails) Debug.Log($"[DataManager] SubSave 삭제 완료 → {file}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataManager] SubSave 삭제 실패: {e}");
            }
        }

        NotifyChanged(); SnapshotValues();
        return true;
    }

    // 요구사항: 씬 이동 직전 활성/비활성 모두 임시 저장
    public void SubSaveCommitSceneSnapshotAllObjects()
    {
        if (nowSlot < 0)
        {
            Debug.LogWarning("[DataManager] SubSaveCommitSceneSnapshotAllObjects: nowSlot 미지정");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;

        try
        {
            var objects = CaptureAllObjectsSnapshotForScene(scene);
            var snap = new SceneObjectSnapshot { SceneName = scene.name, Objects = objects };

            string dst = GetSceneActivesTempPathForSlot(nowSlot, scene.name);
            File.WriteAllText(dst, JsonUtility.ToJson(snap, false));

            if (logRestoreDetails)
                Debug.Log($"[DataManager] (SubSave) 이동 전 씬 스냅샷 저장 → {dst} (count={objects.Length})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] SubSaveCommitSceneSnapshotAllObjects 실패: {e}");
        }
    }

    // 씬 로드시: 임시 씬 스냅샷 적용
    public bool TryApplySubSaveSceneSnapshotForCurrentScene()
    {
        if (nowSlot < 0) return false;

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return false;

        string tempPath = GetSceneActivesTempPathForSlot(nowSlot, scene.name);
        if (!File.Exists(tempPath)) return false;

        try
        {
            var json = File.ReadAllText(tempPath);
            var snap = JsonUtility.FromJson<SceneObjectSnapshot>(json);
            if (snap == null || string.IsNullOrEmpty(snap.SceneName) || snap.Objects == null)
                return false;

            nowPlayer.ActiveSceneName = snap.SceneName;
            nowPlayer.ActiveObjects = snap.Objects;

            ApplyActiveObjectsSnapshotInternal();

            // 임시 스냅샷은 적용 후 삭제
            File.Delete(tempPath);

            if (logRestoreDetails)
                Debug.Log($"[DataManager] (SubSave) 씬 스냅샷 복구 및 삭제 완료 — 씬:'{snap.SceneName}', count:{snap.Objects.Length}");

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] TryApplySubSaveSceneSnapshotForCurrentScene 실패: {e}");
            return false;
        }
    }

    // 이벤트용(기존 호환) — begin/commit/cancel
    public void BeginEventWithTempSave()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] 임시 저장을 시작하려면 먼저 슬롯이 선택되어야 합니다.");
            return;
        }
        string tempFileName = $"slot_{nowSlot}_temp.json";
        _tempSavePath = Path.Combine(subPath, tempFileName);
        try
        {
            string json = JsonUtility.ToJson(nowPlayer, false);
            File.WriteAllText(_tempSavePath, json);
            if (logRestoreDetails) Debug.Log($"[DataManager] 이벤트 시작. 임시 저장 파일 생성: {_tempSavePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 임시 파일 생성 실패: {e}");
            _tempSavePath = null;
        }
    }

    public void CommitEventAndLoadScene(string sceneNameToLoad)
    {
        if (string.IsNullOrEmpty(_tempSavePath))
        {
            Debug.LogError("[DataManager] 시작된 임시 저장이 없습니다. BeginEventWithTempSave()를 먼저 호출하세요.");
            return;
        }
        try
        {
            string json = JsonUtility.ToJson(nowPlayer, false);
            File.WriteAllText(_tempSavePath, json);
            if (logRestoreDetails) Debug.Log($"[DataManager] 이벤트 데이터 임시 파일에 최종 저장 완료: {_tempSavePath}");
            SceneManager.LoadScene(sceneNameToLoad);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] 임시 파일 최종 저장 또는 씬 로드 실패: {e}");
            _tempSavePath = null;
        }
    }

    public void CancelEventAndRevert()
    {
        if (!string.IsNullOrEmpty(_tempSavePath) && File.Exists(_tempSavePath))
        {
            try { File.Delete(_tempSavePath); if (logRestoreDetails) Debug.Log($"[DataManager] 이벤트 취소. 임시 파일 삭제: {_tempSavePath}"); }
            catch (Exception e) { Debug.LogError($"[DataManager] 임시 파일 삭제 실패: {e}"); }
        }
        _tempSavePath = null;
        if (nowSlot >= 0)
        {
            if (logRestoreDetails) Debug.Log($"[DataManager] 원래 데이터로 되돌리기 위해 슬롯 {nowSlot} 재로드");
            LoadData();
        }
    }

    // ───────── 씬 전환 유틸 ─────────
    public void ChangeSceneWithSubSave(string sceneName)
    {
        // 이동 직전 현재 씬 스냅샷(활성/비활성 모두) 저장
        SubSaveCommitSceneSnapshotAllObjects();

        // nowPlayer 전체 임시 저장(선택적)
        SubSaveCommit();

        // 씬 로드
        SceneManager.LoadScene(sceneName);
    }

    // ───────── 메신저 상태 도우미 ─────────
    public bool HasMessengerDelivered(string name)
        => nowPlayer?.MessengerDelivered != null && nowPlayer.MessengerDelivered.Contains(name);

    public bool HasMessengerRead(string name)
        => nowPlayer?.MessengerReadList != null && nowPlayer.MessengerReadList.Contains(name);

    public void AddMessengerDelivered(string name, bool commitSubSave = true)
    {
        if (string.IsNullOrEmpty(name) || nowPlayer == null) return;
        nowPlayer.MessengerDelivered ??= new List<string>();
        if (!nowPlayer.MessengerDelivered.Contains(name))
            nowPlayer.MessengerDelivered.Add(name);
        if (commitSubSave) SubSaveCommit();
    }

    public void AddMessengerRead(string name, bool commitSubSave = true)
    {
        if (string.IsNullOrEmpty(name) || nowPlayer == null) return;
        nowPlayer.MessengerReadList ??= new List<string>();
        if (!nowPlayer.MessengerReadList.Contains(name))
            nowPlayer.MessengerReadList.Add(name);
        if (commitSubSave) SubSaveCommit();
    }

    // ───────── 임시 저장 폴더 정리 ─────────
    public void CleanupAllSubSaves()
    {
        try
        {
            if (string.IsNullOrEmpty(subPath) || !Directory.Exists(subPath)) return;

            int removed = 0;
            var files = Directory.GetFiles(subPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var f in files)
            {
                try { File.Delete(f); removed++; }
                catch (Exception e) { Debug.LogError($"[DataManager] SubSave 삭제 실패: {f}\n{e}"); }
            }
            if (removed > 0 && logRestoreDetails)
                Debug.Log($"[DataManager] SubSave 정리 완료 ({removed}개 삭제)");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] SubSave 정리 중 오류: {e}");
        }
    }

    // ───────── 유틸 ─────────
    private static string BuildHierarchyPath(Transform tr)
    {
        var stack = new Stack<string>(8);
        var cur = tr;
        while (cur != null) { stack.Push(cur.name); cur = cur.parent; }
        return string.Join("/", stack);
    }

    private static string SafeTag(GameObject go)
    {
        try { return go.tag; } catch { return "Untagged"; }
    }

    private bool ShouldExclude(GameObject go)
    {
        // 태그 제외
        if (excludeTagsForActiveObjects != null && excludeTagsForActiveObjects.Length > 0)
        {
            string gTag = SafeTag(go);
            for (int i = 0; i < excludeTagsForActiveObjects.Length; i++)
            {
                if (!string.IsNullOrEmpty(excludeTagsForActiveObjects[i]) &&
                    string.Equals(gTag, excludeTagsForActiveObjects[i], StringComparison.Ordinal))
                    return true;
            }
        }

        // 이름 제외
        if (excludeNamesForActiveObjects != null && excludeNamesForActiveObjects.Length > 0)
        {
            string nm = go.name;
            for (int i = 0; i < excludeNamesForActiveObjects.Length; i++)
            {
                if (!string.IsNullOrEmpty(excludeNamesForActiveObjects[i]) &&
                    string.Equals(nm, excludeNamesForActiveObjects[i], StringComparison.Ordinal))
                    return true;
            }
        }

        // 특정 컴포넌트(UIPanel 등)로 제외
        var uiPanel = go.GetComponent("UIPanel");
        if (uiPanel != null) return true;

        return false;
    }

    private Transform FindPlayer()
    {
        Transform t = null;
        if (!string.IsNullOrEmpty(playerTagForReposition))
        {
            try
            {
                var go = GameObject.FindGameObjectWithTag(playerTagForReposition);
                if (go) t = go.transform;
            }
            catch { }
        }
        if (t == null)
        {
            var go = GameObject.Find("Player");
            if (go) t = go.transform;
        }
        return t;
    }

    private static bool SceneNameEquals(string a, string b)
    {
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = s.Trim();
        return new string(s.Where(ch => ch != ' ' && ch != '\'' && ch != '’').ToArray());
    }

    // ───────── 언어 미리보기(선택) ─────────
    public string PeekLanguageFromMostRecentSave(int slotCount = 3)
    {
        int recentSlot = GetMostRecentSaveSlot(slotCount);
        if (recentSlot < 0) return "ko";
        return PeekLanguageFromSlot(recentSlot);
    }

    public string PeekLanguageFromSlot(int slot)
    {
        if (slot < 0) return "ko";
        string filePath = GetSlotFullPath(slot);
        if (!File.Exists(filePath)) return "ko";

        try
        {
            string json = File.ReadAllText(filePath);
            PlayerData tempData = JsonUtility.FromJson<PlayerData>(json);
            if (tempData != null) return NormalizeLang(tempData.Language);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] PeekLanguageFromSlot ({filePath}) 실패: {e}");
        }

        return "ko";
    }

    // ──────────────── [중요] 기존 코드 호환 래퍼 메서드 ────────────────
    // 기존 스크립트(CallingSystem, MessageSystem, MapMenuController 등)에서 호출하는
    // 메서드명을 그대로 유지하기 위한 래퍼.

    /// <summary>
    /// 기존 코드 호환: CommitDataToTempFile() → 내부적으로 SubSaveCommit()을 호출하여 nowPlayer 전체를 sub_save에 기록.
    /// </summary>
    public void CommitDataToTempFile()
    {
        // 필요 시 추가 디버그
        // Debug.Log($"[디버그] 저장 직전 Coin 값: {nowPlayer.Coin}");
        SubSaveCommit();
    }

    /// <summary>
    /// 기존 코드 호환: SubSaveCommitActivesForCurrentScene() →
    /// 비활성 포함 전체 오브젝트 스냅샷을 sub_save에 저장하는 현재 방식으로 매핑.
    /// </summary>
    public void SubSaveCommitActivesForCurrentScene()
    {
        SubSaveCommitSceneSnapshotAllObjects();
    }
}
