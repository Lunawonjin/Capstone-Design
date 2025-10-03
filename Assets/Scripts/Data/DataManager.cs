using UnityEngine;
using System.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine.SceneManagement;

#region 저장 포맷(활성 오브젝트 기록)

// 활성 오브젝트 스냅샷(씬 저장용)
[Serializable]
public class ActiveObjectInfo
{
    // 예: "Environment/Trees/Tree_01"
    public string HierarchyPath;

    // GameObject.name
    public string Name;

    // GameObject.tag (Untagged 포함)
    public string Tag;

    // activeInHierarchy 당시의 활성 상태(요청 사양상 true만 저장되지만, 포맷 확장성 위해 둠)
    public bool ActiveInHierarchy;
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

    // 요일(1~7) : 월(1) 화(2) 수(3) 목(4) 금(5) 토(6) 일(7)
    public int Weekday;

    // 언어 코드("ko","en","jp") — 기본 "ko"
    public string Language;

    //첫 방문 지역 이벤트
    public static bool Starest_Frist_Visit;

    // 첫 만남 플래그(스토리 트리거)
    public bool Sol_First_Meet;
    public bool Salt_First_Meet;
    public bool Ryu_First_Meet;
    public bool White_First_Meet;

    public int Sol_FriendShip;
    public int Salt_FriendShip;
    public int Ryu_FriendShip;
    public int White_FriendShip;


    // ===== 활성 씬 오브젝트 스냅샷 =====
    public string ActiveSceneName;      // 저장 시점 활성 씬명
    public ActiveObjectInfo[] ActiveObjects; // 저장 시점 activeInHierarchy == true 목록

    public PlayerData()
    {
        Level = 1;
        Day = 1;
        Scene = "";
        Weekday = 1;
        Language = "ko";

        Starest_Frist_Visit = false;

        Sol_First_Meet = false;
        Salt_First_Meet = false;
        Ryu_First_Meet = false;
        White_First_Meet = false;

        Sol_FriendShip = 0;
        Salt_FriendShip = 0;
        Ryu_FriendShip = 0;
        White_FriendShip = 0;

        ActiveSceneName = "";
        ActiveObjects = Array.Empty<ActiveObjectInfo>();
    }
}

public class DataManager : MonoBehaviour
{
    public static DataManager instance;

    [Header("플레이어/저장 슬롯")]
    public PlayerData nowPlayer = new PlayerData();
    public string path;          // 저장 폴더 경로 (persistentDataPath/save)
    public int nowSlot = -1;     // 현재 선택된 저장 슬롯(0,1,2 ...)

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
    // {0}=일차(숫자), {1}=요일명
    [SerializeField] private string dayFormatKo = "{0}일 ({1})";
    [SerializeField] private string dayFormatEn = "Day {0} ({1})";
    [SerializeField] private string dayFormatJp = "{0}日（{1}）";

    [Header("호감도 표기 형식(언어별)")]
    [Tooltip("{0}=캐릭터 이름, {1}=호감도 값")]
    [SerializeField] private string friendshipFormatKo = "{0} 호감도: {1}";
    [Tooltip("{0}=Character Name, {1}=Friendship Value")]
    [SerializeField] private string friendshipFormatEn = "{0} Affinity: {1}";
    [Tooltip("{0}=キャラクター名, {1}=好感度")]
    [SerializeField] private string friendshipFormatJp = "{0} 好感度: {1}";

    [Header("캐릭터 이름 (언어별)")]
    [SerializeField] private string solNameKo = "솔";
    [SerializeField] private string solNameEn = "Sol";
    [SerializeField] private string solNameJp = "ソル";
    [Space(10)]
    [SerializeField] private string saltNameKo = "솔트";
    [SerializeField] private string saltNameEn = "Salt";
    [SerializeField] private string saltNameJp = "ソルト";
    [Space(10)]
    [SerializeField] private string ryuNameKo = "류";
    [SerializeField] private string ryuNameEn = "Ryu";
    [SerializeField] private string ryuNameJp = "リュウ";
    [Space(10)]
    [SerializeField] private string whiteNameKo = "화이트";
    [SerializeField] private string whiteNameEn = "White";
    [SerializeField] private string whiteNameJp = "ホワイト";

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

    // === 언어별 요일 이름표(1~7 인덱스 사용; 0은 미사용) ===
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

    // ===== 추가된 부분 & 수정된 부분 =====
    [Header("호감도 텍스트 폰트/사이즈(언어별)")]
    [SerializeField] private TMP_FontAsset friendshipFontKo;
    [SerializeField] private TMP_FontAsset friendshipFontEn;
    [SerializeField] private TMP_FontAsset friendshipFontJp;

    [SerializeField] private int friendshipFontSizeKo = 30;
    [SerializeField] private int friendshipFontSizeEn = 30;
    [SerializeField] private int friendshipFontSizeJp = 30;
    // ===== 추가/수정 끝 =====

    [Header("활성 오브젝트 저장 옵션")]
    [Tooltip("활성 씬에서 activeInHierarchy == true 인 오브젝트를 저장")]
    [SerializeField] private bool captureActiveObjectsOnSave = true;

    [Tooltip("이 태그를 가진 오브젝트는 저장/복원에서 제외(HUD 등)")]
    [SerializeField] private string[] excludeTagsForActiveObjects = new string[] { "HUD", "UIPanel" };

    [Tooltip("이 이름(정확 일치)을 가진 오브젝트는 저장/복원에서 제외")]
    [SerializeField] private string[] excludeNamesForActiveObjects = new string[] { "UIPanel" };

    public enum ActiveRestoreMode
    {
        OnlyListedToActive,
        FullSyncActiveVsOthersInactive
    }

    [Header("활성 오브젝트 복원 옵션")]
    [Tooltip("씬 로드 시 자동으로 활성 오브젝트 상태를 복원")]
    [SerializeField] private bool autoRestoreActiveObjectsOnSceneLoaded = true;

    [Tooltip("복원 모드: 보수적(기록된 것만 활성) / 완전 동기화(기록 외 비활성)")]
    [SerializeField] private ActiveRestoreMode activeRestoreMode = ActiveRestoreMode.OnlyListedToActive;

    [Tooltip("복원 시 어떤 경로가 적용/누락되었는지 로그 출력")]
    [SerializeField] private bool logRestoreDetails = false;

    // 변경 감지 스냅샷
    int _lastCoin = int.MinValue, _lastLevel = int.MinValue, _lastDay = int.MinValue, _lastWeekday = int.MinValue;
    string _lastName = null;
    string _lastLanguage = null;

    int _lastSolFriendship = int.MinValue, _lastSaltFriendship = int.MinValue, _lastRyuFriendship = int.MinValue, _lastWhiteFriendship = int.MinValue;

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

        path = System.IO.Path.Combine(Application.persistentDataPath, "save");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

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

        SceneManager.sceneLoaded += OnSceneLoaded_RebindHUD_AndApplyPos;

        EnsureWeekdayValid();
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

    // === 저장/로드/삭제 ===

    public string GetSlotFullPath(int slot)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return System.IO.Path.Combine(path, $"slot_{slot}.json");
    }

    private string GetSlotPath(int slot) => GetSlotFullPath(slot);

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

            if (captureActiveObjectsOnSave)
            {
                var activeScene = SceneManager.GetActiveScene();
                nowPlayer.ActiveSceneName = activeScene.name;
                nowPlayer.ActiveObjects = CaptureActiveObjectsInCurrentScene().ToArray();
            }
            else
            {
                nowPlayer.ActiveSceneName = SceneManager.GetActiveScene().name;
                nowPlayer.ActiveObjects = Array.Empty<ActiveObjectInfo>();
            }

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

    // === 값 변경 API ===

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
            int wd = GetWeekday();
            wd = WrapWeekday(wd + delta);
            SetWeekday(wd, notify: false);
        }
        NotifyChanged(); SnapshotValues();
    }

    public void SetPlayerName(string newName) { nowPlayer.Name = newName ?? ""; NotifyChanged(); SnapshotValues(); }

    // === 언어 코드 API ===

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

    // === 요일/날짜 로컬라이즈 유틸 ===

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

    // === 요일 유틸 ===

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

    public string GetWeekdayName()
    {
        return GetWeekdayNameLocalized(CurrentLang());
    }

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

    // === 위치/씬 저장 ===

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

    // === HUD ===

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

    // ===== 추가된 부분 & 수정된 부분 =====
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

        // 모든 호감도 텍스트에 폰트와 사이즈 적용
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
    // ===== 추가/수정 끝 =====

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

        // ===== 추가된 부분 & 수정된 부분 =====
        // 호감도 스타일 적용
        ApplyFriendshipStylePerLanguage(lang);
        // ===== 추가/수정 끝 =====

        string format;
        string characterName;

        switch (lang)
        {
            case "en": format = friendshipFormatEn; break;
            case "jp": format = friendshipFormatJp; break;
            default: format = friendshipFormatKo; break;
        }

        if (solFriendshipText)
        {
            characterName = lang switch { "en" => solNameEn, "jp" => solNameJp, _ => solNameKo };
            solFriendshipText.text = string.Format(format, characterName, nowPlayer.Sol_FriendShip);
        }

        if (saltFriendshipText)
        {
            characterName = lang switch { "en" => saltNameEn, "jp" => saltNameJp, _ => saltNameKo };
            saltFriendshipText.text = string.Format(format, characterName, nowPlayer.Salt_FriendShip);
        }

        if (ryuFriendshipText)
        {
            characterName = lang switch { "en" => ryuNameEn, "jp" => ryuNameJp, _ => ryuNameKo };
            ryuFriendshipText.text = string.Format(format, characterName, nowPlayer.Ryu_FriendShip);
        }

        if (whiteFriendshipText)
        {
            characterName = lang switch { "en" => whiteNameEn, "jp" => whiteNameJp, _ => whiteNameKo };
            whiteFriendshipText.text = string.Format(format, characterName, nowPlayer.White_FriendShip);
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

            bool needSol = solFriendshipText == null && !string.IsNullOrEmpty(solFriendshipObjectName);
            bool needSalt = saltFriendshipText == null && !string.IsNullOrEmpty(saltFriendshipObjectName);
            bool needRyu = ryuFriendshipText == null && !string.IsNullOrEmpty(ryuFriendshipObjectName);
            bool needWhite = whiteFriendshipText == null && !string.IsNullOrEmpty(whiteFriendshipObjectName);

            if (needCoin || needLevel || needDay || needName || needSol || needSalt || needRyu || needWhite)
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

                var fSol = needSol ? FindTMP(solFriendshipObjectName) : solFriendshipText;
                var fSalt = needSalt ? FindTMP(saltFriendshipObjectName) : saltFriendshipText;
                var fRyu = needRyu ? FindTMP(ryuFriendshipObjectName) : ryuFriendshipText;
                var fWhite = needWhite ? FindTMP(whiteFriendshipObjectName) : whiteFriendshipText;

                if (fc || fl || fd || fn || fSol || fSalt || fRyu || fWhite)
                    BindHUD(fc, fl, fd, fn, fSol, fSalt, fRyu, fWhite);
            }
        }

        if (applySavedPositionOnLoad && nowPlayer != null && nowPlayer.HasSavedPosition)
            StartCoroutine(ApplyPositionWhenReady());

        if (autoRestoreActiveObjectsOnSceneLoaded)
        {
            StartCoroutine(ApplyActiveObjectsSnapshotCoroutine());
        }
    }

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

    // ===== 활성 오브젝트 수집(저장) =====

    private List<ActiveObjectInfo> CaptureActiveObjectsInCurrentScene()
    {
        var result = new List<ActiveObjectInfo>(256);

        var activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
            return result;

        var roots = activeScene.GetRootGameObjects();
        foreach (var root in roots)
        {
            if (!root) continue;
            TraverseAndCollect(root.transform, activeScene, result);
        }

        return result;
    }

    private void TraverseAndCollect(Transform t, Scene activeScene, List<ActiveObjectInfo> sink)
    {
        if (!t) return;

        var go = t.gameObject;

        if (go.scene != activeScene || !go.scene.isLoaded)
            return;

        if (go.hideFlags != HideFlags.None)
            return;

        if (go.activeInHierarchy)
        {
            if (!ShouldExclude(go))
            {
                sink.Add(new ActiveObjectInfo
                {
                    HierarchyPath = BuildHierarchyPath(go.transform),
                    Name = go.name,
                    Tag = SafeTag(go),
                    ActiveInHierarchy = true
                });
            }
        }

        for (int i = 0; i < t.childCount; i++)
            TraverseAndCollect(t.GetChild(i), activeScene, sink);
    }

    // ===== 활성 오브젝트 복원(restore) =====

    public void ApplyActiveObjectsSnapshotNow()
    {
        try
        {
            ApplyActiveObjectsSnapshotInternal();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DataManager] ApplyActiveObjectsSnapshotNow 실패: {e}");
        }
    }

    public IEnumerator ApplyActiveObjectsSnapshotCoroutine(float delayOneFrame = 0f)
    {
        if (delayOneFrame <= 0f) yield return null;
        else
        {
            float t = 0f;
            while (t < delayOneFrame)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        ApplyActiveObjectsSnapshotInternal();
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

        var sceneObjects = BuildSceneObjectMap();

        var activeSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in nowPlayer.ActiveObjects)
        {
            if (info == null || string.IsNullOrEmpty(info.HierarchyPath)) continue;
            activeSet.Add(info.HierarchyPath);
        }

        int setOn = 0, setOff = 0, missing = 0;

        if (activeRestoreMode == ActiveRestoreMode.OnlyListedToActive)
        {
            foreach (var path in activeSet)
            {
                if (sceneObjects.TryGetValue(path, out var go))
                {
                    if (!go.activeSelf) { go.SetActive(true); setOn++; }
                }
                else
                {
                    missing++;
                    if (logRestoreDetails) Debug.Log($"[DataManager] 경로 누락(활성 처리 불가): {path}");
                }
            }
        }
        else // FullSyncActiveVsOthersInactive
        {
            foreach (var kv in sceneObjects)
            {
                var go = kv.Value;
                if (!activeSet.Contains(kv.Key))
                {
                    if (go.activeSelf) { go.SetActive(false); setOff++; }
                }
            }

            foreach (var path in activeSet)
            {
                if (sceneObjects.TryGetValue(path, out var go))
                {
                    if (!go.activeSelf) { go.SetActive(true); setOn++; }
                }
                else
                {
                    missing++;
                    if (logRestoreDetails) Debug.Log($"[DataManager] 경로 누락(활성 처리 불가): {path}");
                }
            }
        }

        if (logRestoreDetails)
            Debug.Log($"[DataManager] 복원 완료 — 켬:{setOn}, 끔:{setOff}, 경로누락:{missing}, 씬:'{currentSceneName}'");
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
            TraverseAndMap(root.transform, scene, map);
        }
        return map;
    }

    private void TraverseAndMap(Transform t, Scene scene, Dictionary<string, GameObject> sink)
    {
        if (!t) return;
        var go = t.gameObject;

        if (go.scene != scene || !go.scene.isLoaded) return;
        if (go.hideFlags != HideFlags.None) return;
        if (ShouldExclude(go)) { /* 예외: 통째로 가지치기 */ }
        else
        {
            string path = BuildHierarchyPath(go.transform);
            if (!sink.ContainsKey(path))
                sink.Add(path, go);
        }

        for (int i = 0; i < t.childCount; i++)
            TraverseAndMap(t.GetChild(i), scene, sink);
    }

    // ===== 공통 유틸 =====

    private static string BuildHierarchyPath(Transform tr)
    {
        var stack = new Stack<string>(8);
        var cur = tr;
        while (cur != null)
        {
            stack.Push(cur.name);
            cur = cur.parent;
        }
        return string.Join("/", stack);
    }

    private static string SafeTag(GameObject go)
    {
        try { return go.tag; }
        catch { return "Untagged"; }
    }

    private bool ShouldExclude(GameObject go)
    {
        if (excludeTagsForActiveObjects != null && excludeTagsForActiveObjects.Length > 0)
        {
            string gTag = SafeTag(go);
            for (int i = 0; i < excludeTagsForActiveObjects.Length; i++)
            {
                var ex = excludeTagsForActiveObjects[i];
                if (!string.IsNullOrEmpty(ex) && string.Equals(gTag, ex, StringComparison.Ordinal))
                    return true;
            }
        }

        if (excludeNamesForActiveObjects != null && excludeNamesForActiveObjects.Length > 0)
        {
            string nm = go.name;
            for (int i = 0; i < excludeNamesForActiveObjects.Length; i++)
            {
                var ex = excludeNamesForActiveObjects[i];
                if (!string.IsNullOrEmpty(ex) && string.Equals(nm, ex, StringComparison.Ordinal))
                    return true;
            }
        }

        var uiPanelType = Type.GetType("UIPanel");
        if (uiPanelType != null && go.GetComponent(uiPanelType) != null)
            return true;

        return false;
    }
}