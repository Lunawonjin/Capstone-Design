using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// HealingEventManager
/// - 캐릭터별 치유 게이지({Name}_FriendShip)에 따라 JSON 이벤트 실행을 관리한다.
/// - JSON 실행은 NpcEventDebugLoader.RunEventByName_External(...)를 통해 이루어진다.
/// - PlayerData에는 다음 필드를 준비해두면 된다(필요한 것만 선택적으로):
///   · int  {Name}_FriendShip              : 치유 게이지
///   · int  Day                            : 현재 날짜(필드 이름은 인스펙터에서 설정 가능)
///   · bool {Name}_Heal_G1CasualDone       : 게이지 1 캐주얼 게임1 실행 여부
///   · bool {Name}_Heal_G2SimpleDone       : 게이지 2 간단 이벤트1 실행 여부
///   · bool {Name}_Heal_G4CasualDone       : 게이지 4 캐주얼 게임2 실행 여부
///   · bool {Name}_Heal_G5SeriousDone      : 게이지 5 진지한 게임 실행 여부
///   · bool {Name}_Heal_ItemDone           : 게이지 3 아이템 이벤트 완료 여부
///   · int  {Name}_Heal_ItemStartDay       : 아이템 이벤트 3일 랜덤 구간 시작 날짜
///   · bool {Name}_Heal_S2Done             : 게이지 4 이후 간단 이벤트2 완료 여부
///   · int  {Name}_Heal_S2StartDay         : 간단 이벤트2 3일 랜덤 구간 시작 날짜
///   · int  {Name}_Heal_SimpleRandomLastDay: "랜덤 간단 이벤트(2이상)"가 마지막으로 실행된 날짜
///
/// 치유 설계:
/// - 치유게이지 0  : 기본 대화 (여기서는 아무것도 안 함)
/// - 치유게이지 1  : 캐주얼 게임1 바로 1회 실행
/// - 치유게이지 2  : 간단 이벤트1 바로 1회 실행
///                    + (optional) 치유게이지 2 이상일 때 랜덤 간단 이벤트 (집이 아닐 때, 확률로 실행)
/// - 치유게이지 3  : 아이템 이벤트
///                    · 3에 도달한 시점을 기준으로 3일 중 하루 랜덤
///                    · 1일차: 1/3, 2일차: 1/2, 3일차: 남은 확률 1 (실질적으로 3일 내 하루 랜덤)
/// - 치유게이지 4  : 캐주얼 게임2 바로 1회 실행
///                    + 캐주얼게임2 이후 3일 중 하루 랜덤으로 간단 이벤트2
///                      (똑같이 1/3, 1/2, 1 방식으로 확률 결정)
/// - 치유게이지 5  : 진지한 게임 1회 실행 후 엔딩
///
/// 사용 방법:
/// - 말걸기 시도 시, HealingEventManager.TryRunHealingEvent("Sol", isAtHome) 호출
///   · true를 반환하면 치유용 JSON 이벤트가 실행되었으므로, 기본 대화는 열지 않는 것을 권장.
///   · false면 이번에는 치유 이벤트가 없으니 기본 대화(말풍선)를 진행.
/// </summary>
[DisallowMultipleComponent]
public class HealingEventManager : MonoBehaviour
{
    #region Inspector Types

    [Serializable]
    public class CharacterConfig
    {
        [Header("Character / Owner Names")]
        [Tooltip("캐릭터 이름. PlayerData의 {Name}_FriendShip, 각종 Heal 필드 이름에 사용된다.")]
        public string characterName = "Sol";

        [Tooltip("JSON 이벤트 폴더 owner 이름. Resources/Event/{ownerName}/{eventName}.json 구조에서 ownerName에 해당.")]
        public string ownerName = "Sol";

        [Header("Gauge 1: 캐주얼 게임1 (1회)")]
        [Tooltip("치유게이지가 1일 때 실행할 캐주얼 게임 이벤트 이름 리스트.")]
        public string[] gauge1CasualGameEvents = Array.Empty<string>();

        [Header("Gauge 2: 간단 이벤트1 (1회)")]
        [Tooltip("치유게이지가 2일 때 바로 1회 실행되는 간단 이벤트1.")]
        public string[] gauge2SimpleEvents = Array.Empty<string>();

        [Header("Gauge 2 이상: 랜덤 간단 이벤트(집이 아닐 때)")]
        [Tooltip("치유게이지 2 이상이고, 집이 아닐 때 랜덤으로 실행되는 간단 이벤트 목록.")]
        public string[] randomSimpleEvents = Array.Empty<string>();

        [Range(0f, 1f)]
        [Tooltip("랜덤 간단 이벤트 실행 확률(하루에 한 번만 발생). 예: 0.2 = 20%.")]
        public float randomSimpleEventChance = 0.2f;

        [Header("Gauge 3: 아이템 이벤트 (3일 중 하루 랜덤)")]
        [Tooltip("치유게이지 3 달성 후, 3일 안에 하루 랜덤으로 1회 실행할 아이템 이벤트.")]
        public string[] itemRequestEvents = Array.Empty<string>();

        [Header("Gauge 4: 캐주얼 게임2 + 간단 이벤트2")]
        [Tooltip("치유게이지 4일 때 바로 1회 실행되는 캐주얼 게임2 이벤트.")]
        public string[] gauge4CasualGameEvents = Array.Empty<string>();

        [Tooltip("캐주얼 게임2 이후, 3일 중 하루 랜덤으로 1회 실행할 간단 이벤트2.")]
        public string[] simpleEventAfterGauge4Events = Array.Empty<string>();

        [Header("Gauge 5: 진지한 게임 + 엔딩")]
        [Tooltip("치유게이지 5일 때 1회 실행되는 진지한 게임 이벤트.")]
        public string[] gauge5SeriousGameEvents = Array.Empty<string>();
    }

    #endregion

    #region Inspector Fields

    [Header("캐릭터별 치유 이벤트 설정")]
    [SerializeField] private CharacterConfig[] characterConfigs = Array.Empty<CharacterConfig>();

    [Header("NpcEventDebugLoader 참조")]
    [SerializeField] private NpcEventDebugLoader eventRunner;
    [SerializeField] private bool autoFindEventRunner = true;

    [Header("PlayerData 날짜 필드 이름")]
    [Tooltip("PlayerData 안에서 날짜를 의미하는 int 필드 이름. 예: Day, Today, DayCount 등.")]
    [SerializeField] private string playerDayFieldName = "Day";

    [Header("로그")]
    [SerializeField] private bool enableLog = true;

    #endregion

    private BindingFlags _bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

    #region Unity Hooks

    private void Awake()
    {
        if (autoFindEventRunner && eventRunner == null)
        {
            eventRunner = FindFirstObjectByType<NpcEventDebugLoader>(FindObjectsInactive.Include);
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// 대화를 시도할 때 호출.
    /// - characterName: Sol, Salt 등 캐릭터 이름
    /// - isAtHome: 현재 위치가 "집"인지 여부 (집이 아닐 때 랜덤 간단 이벤트가 발생함)
    /// 반환값: 이번에 치유 이벤트(JSON)가 실행되었으면 true, 아니면 false
    /// </summary>
    public bool TryRunHealingEvent(string characterName, bool isAtHome)
    {
        var cfg = FindConfig(characterName);
        if (cfg == null)
        {
            Log($"Config not found for character '{characterName}'.");
            return false;
        }

        var pd = ResolvePlayerData();
        if (pd == null)
        {
            Log("PlayerData not found.");
            return false;
        }

        int gauge = GetFriendShipGauge(pd, cfg.characterName);
        int day = GetCurrentDay(pd);

        Log($"[{cfg.characterName}] gauge={gauge}, day={day}");

        // 게이지 0: 기본 대화만. 여기서는 아무것도 안 함.
        if (gauge <= 0)
            return false;

        // 1) 게이지 5: 진지한 게임 1회
        if (gauge == 5)
        {
            if (TryRunGauge5_Serious(pd, cfg, day))
                return true;
        }

        // 2) 게이지 4: 캐주얼 게임2 바로 + (이후 3일간 간단 이벤트2 스케줄)
        if (gauge == 4)
        {
            // 캐주얼 게임2
            if (TryRunGauge4_Casual(pd, cfg, day))
                return true;

            // 캐주얼 게임2 이후 3일 랜덤 간단 이벤트2
            if (TryRunGauge4_Simple2(pd, cfg, day))
                return true;
        }
        else if (gauge > 4)
        {
            // 게이지가 이미 5 이상인데 캐주얼 게임2/간단2가 아직 안 끝났다면,
            // 뒤늦게라도 처리해 주고 싶다면 여기에서 호출해도 된다.
            // 필요 없다면 생략 가능. 여기서는 "게이지 4 구간에서만" 처리한다고 가정.
        }

        // 3) 게이지 3 이상: 아이템 부탁 이벤트 (3일 중 하루 랜덤)
        if (gauge >= 3)
        {
            if (TryRunGauge3_Item(pd, cfg, day))
                return true;
        }

        // 4) 게이지 2: 간단 이벤트1 바로 1회
        if (gauge == 2)
        {
            if (TryRunGauge2_SimpleImmediate(pd, cfg, day))
                return true;
        }

        // 5) 게이지 1: 캐주얼 게임1 바로 1회
        if (gauge == 1)
        {
            if (TryRunGauge1_Casual(pd, cfg, day))
                return true;
        }

        // 6) 게이지 2 이상 & 집이 아님: 랜덤 간단 이벤트(반복 가능, 하루 1회 제한)
        if (gauge >= 2 && !isAtHome)
        {
            if (TryRunRandomSimpleEvent(pd, cfg, day))
                return true;
        }

        // 아무 치유 이벤트도 실행되지 않았다.
        return false;
    }

    #endregion

    #region Gauge Stage Handlers

    // Gauge 1: 캐주얼 게임1 바로 1회
    private bool TryRunGauge1_Casual(object pd, CharacterConfig cfg, int day)
    {
        if (cfg.gauge1CasualGameEvents == null || cfg.gauge1CasualGameEvents.Length == 0)
            return false;

        string flagName = $"{cfg.characterName}_Heal_G1CasualDone";
        if (!GetBool(pd, flagName, out bool done) || done)
            return false;

        string eventName = PickRandom(cfg.gauge1CasualGameEvents);
        if (string.IsNullOrEmpty(eventName))
            return false;

        Log($"[{cfg.characterName}] Gauge 1 casual game: {eventName}");

        return RunJsonEvent(cfg.ownerName, eventName, () =>
        {
            SetBool(pd, flagName, true);
            SaveIfPossible();
        });
    }

    // Gauge 2: 간단 이벤트1 바로 1회
    private bool TryRunGauge2_SimpleImmediate(object pd, CharacterConfig cfg, int day)
    {
        if (cfg.gauge2SimpleEvents == null || cfg.gauge2SimpleEvents.Length == 0)
            return false;

        string flagName = $"{cfg.characterName}_Heal_G2SimpleDone";
        if (!GetBool(pd, flagName, out bool done) || done)
            return false;

        string eventName = PickRandom(cfg.gauge2SimpleEvents);
        if (string.IsNullOrEmpty(eventName))
            return false;

        Log($"[{cfg.characterName}] Gauge 2 simple event1: {eventName}");

        return RunJsonEvent(cfg.ownerName, eventName, () =>
        {
            SetBool(pd, flagName, true);
            SaveIfPossible();
        });
    }

    // Gauge 3: 아이템 이벤트 (3일 중 하루 랜덤)
    private bool TryRunGauge3_Item(object pd, CharacterConfig cfg, int day)
    {
        if (cfg.itemRequestEvents == null || cfg.itemRequestEvents.Length == 0)
            return false;

        string doneName = $"{cfg.characterName}_Heal_ItemDone";
        if (!GetBool(pd, doneName, out bool done))
            return false;

        if (done)
            return false;

        string startDayName = $"{cfg.characterName}_Heal_ItemStartDay";
        int startDay = GetInt(pd, startDayName, defaultValue: -1);

        if (startDay <= 0)
        {
            // 아직 윈도우 시작일이 설정되지 않았다면 오늘을 시작일로 설정
            startDay = day;
            SetInt(pd, startDayName, startDay);
            SaveIfPossible();
            Log($"[{cfg.characterName}] Item event window start set to day {startDay}");
        }

        int diff = day - startDay;
        if (diff < 0)
            return false;

        // 0일차: 1/3, 1일차: 1/2, 2일차 이상: 강제 실행
        float chance = 0f;
        if (diff == 0) chance = 1f / 3f;
        else if (diff == 1) chance = 1f / 2f;
        else chance = 1f;

        if (!CheckRandom(chance))
            return false;

        string eventName = PickRandom(cfg.itemRequestEvents);
        if (string.IsNullOrEmpty(eventName))
            return false;

        Log($"[{cfg.characterName}] Gauge 3 item request event (dayDiff={diff}, chance={chance}): {eventName}");

        return RunJsonEvent(cfg.ownerName, eventName, () =>
        {
            SetBool(pd, doneName, true);
            SaveIfPossible();
        });
    }

    // Gauge 4: 캐주얼 게임2 바로 1회
    private bool TryRunGauge4_Casual(object pd, CharacterConfig cfg, int day)
    {
        if (cfg.gauge4CasualGameEvents == null || cfg.gauge4CasualGameEvents.Length == 0)
            return false;

        string flagName = $"{cfg.characterName}_Heal_G4CasualDone";
        if (!GetBool(pd, flagName, out bool done) || done)
            return false;

        string eventName = PickRandom(cfg.gauge4CasualGameEvents);
        if (string.IsNullOrEmpty(eventName))
            return false;

        Log($"[{cfg.characterName}] Gauge 4 casual game2: {eventName}");

        return RunJsonEvent(cfg.ownerName, eventName, () =>
        {
            SetBool(pd, flagName, true);

            // 캐주얼 게임2가 끝난 날짜를 기준으로 간단 이벤트2 3일 윈도우를 시작한다.
            string s2StartName = $"{cfg.characterName}_Heal_S2StartDay";
            SetInt(pd, s2StartName, day);

            SaveIfPossible();
        });
    }

    // Gauge 4 이후: 간단 이벤트2 (캐주얼 게임2 이후 3일 중 하루 랜덤)
    private bool TryRunGauge4_Simple2(object pd, CharacterConfig cfg, int day)
    {
        if (cfg.simpleEventAfterGauge4Events == null || cfg.simpleEventAfterGauge4Events.Length == 0)
            return false;

        string doneName = $"{cfg.characterName}_Heal_S2Done";
        if (!GetBool(pd, doneName, out bool done))
            return false;

        if (done)
            return false;

        string startDayName = $"{cfg.characterName}_Heal_S2StartDay";
        int startDay = GetInt(pd, startDayName, defaultValue: -1);
        if (startDay <= 0)
        {
            // 캐주얼 게임2가 아직 안 끝난 상태이거나 시작일이 세팅되지 않은 경우
            // 여기서 새로 시작하고 싶다면 아래 로직을 활성화할 수 있다.
            // 지금은 "캐주얼 게임2 끝난 날"에만 startDay를 세팅한다고 가정.
            return false;
        }

        int diff = day - startDay;
        if (diff < 0)
            return false;

        float chance = 0f;
        if (diff == 0) chance = 1f / 3f;
        else if (diff == 1) chance = 1f / 2f;
        else chance = 1f;

        if (!CheckRandom(chance))
            return false;

        string eventName = PickRandom(cfg.simpleEventAfterGauge4Events);
        if (string.IsNullOrEmpty(eventName))
            return false;

        Log($"[{cfg.characterName}] Gauge 4 simple event2 (dayDiff={diff}, chance={chance}): {eventName}");

        return RunJsonEvent(cfg.ownerName, eventName, () =>
        {
            SetBool(pd, doneName, true);
            SaveIfPossible();
        });
    }

    // Gauge 5: 진지한 게임 1회
    private bool TryRunGauge5_Serious(object pd, CharacterConfig cfg, int day)
    {
        if (cfg.gauge5SeriousGameEvents == null || cfg.gauge5SeriousGameEvents.Length == 0)
            return false;

        string flagName = $"{cfg.characterName}_Heal_G5SeriousDone";
        if (!GetBool(pd, flagName, out bool done) || done)
            return false;

        string eventName = PickRandom(cfg.gauge5SeriousGameEvents);
        if (string.IsNullOrEmpty(eventName))
            return false;

        Log($"[{cfg.characterName}] Gauge 5 serious game: {eventName}");

        return RunJsonEvent(cfg.ownerName, eventName, () =>
        {
            SetBool(pd, flagName, true);
            SaveIfPossible();
        });
    }

    // Gauge 2 이상 & 집이 아닐 때: 랜덤 간단 이벤트 (반복 가능, 하루 1회 제한)
    private bool TryRunRandomSimpleEvent(object pd, CharacterConfig cfg, int day)
    {
        if (cfg.randomSimpleEvents == null || cfg.randomSimpleEvents.Length == 0)
            return false;

        if (cfg.randomSimpleEventChance <= 0f)
            return false;

        string lastDayName = $"{cfg.characterName}_Heal_SimpleRandomLastDay";
        int lastDay = GetInt(pd, lastDayName, defaultValue: -1);

        if (lastDay == day)
            return false; // 오늘은 이미 한 번 실행했다.

        if (!CheckRandom(cfg.randomSimpleEventChance))
            return false;

        string eventName = PickRandom(cfg.randomSimpleEvents);
        if (string.IsNullOrEmpty(eventName))
            return false;

        Log($"[{cfg.characterName}] Random simple event (day={day}, lastDay={lastDay}, chance={cfg.randomSimpleEventChance}): {eventName}");

        return RunJsonEvent(cfg.ownerName, eventName, () =>
        {
            SetInt(pd, lastDayName, day);
            SaveIfPossible();
        });
    }

    #endregion

    #region JSON Event Execution

    private bool RunJsonEvent(string ownerName, string eventName, Action onComplete)
    {
        if (eventRunner == null)
        {
            if (autoFindEventRunner)
            {
                eventRunner = FindFirstObjectByType<NpcEventDebugLoader>(FindObjectsInactive.Include);
            }
        }

        if (eventRunner == null)
        {
            Debug.LogWarning("[HealingEventManager] NpcEventDebugLoader reference is null.");
            return false;
        }

        bool ok = eventRunner.RunEventByName_External(ownerName, eventName, onComplete);
        if (!ok)
        {
            Debug.LogWarning($"[HealingEventManager] RunEventByName_External failed: {ownerName}/{eventName}");
        }
        return ok;
    }

    #endregion

    #region PlayerData Helpers

    private object ResolvePlayerData()
    {
        if (DataManager.instance == null)
        {
            Debug.LogError("[HealingEventManager] DataManager.instance is null.");
            return null;
        }
        return DataManager.instance.nowPlayer;
    }

    private int GetFriendShipGauge(object pd, string characterName)
    {
        string[] candidates = new[]
        {
            $"{characterName}_FriendShip",
            $"{characterName}_Friendship",
            $"{characterName}_Affinity"
        };

        foreach (var name in candidates)
        {
            if (TryBindInt(pd, name, out Func<int> getter, out _))
                return getter();
        }

        Log($"[{characterName}] FriendShip int field not found in PlayerData. default 0.");
        return 0;
    }

    private int GetCurrentDay(object pd)
    {
        if (string.IsNullOrWhiteSpace(playerDayFieldName))
            return 0;

        if (TryBindInt(pd, playerDayFieldName, out Func<int> getter, out _))
            return getter();

        Log($"Day field '{playerDayFieldName}' not found in PlayerData. default 0.");
        return 0;
    }

    private int GetInt(object pd, string fieldName, int defaultValue)
    {
        if (TryBindInt(pd, fieldName, out Func<int> getter, out _))
            return getter();
        return defaultValue;
    }

    private void SetInt(object pd, string fieldName, int value)
    {
        if (TryBindInt(pd, fieldName, out _, out Action<int> setter))
            setter(value);
    }

    private bool GetBool(object pd, string fieldName, out bool value)
    {
        value = false;
        if (TryBindBool(pd, fieldName, out Func<bool> getter, out _))
        {
            value = getter();
            return true;
        }
        return false;
    }

    private void SetBool(object pd, string fieldName, bool value)
    {
        if (TryBindBool(pd, fieldName, out _, out Action<bool> setter))
            setter(value);
    }

    private bool TryBindInt(object obj, string name, out Func<int> getter, out Action<int> setter)
    {
        getter = null;
        setter = null;
        if (obj == null || string.IsNullOrWhiteSpace(name))
            return false;

        var t = obj.GetType();

        var f = t.GetField(name, _bf);
        if (f != null && f.FieldType == typeof(int))
        {
            getter = () => (int)f.GetValue(obj);
            setter = v => f.SetValue(obj, v);
            return true;
        }

        var p = t.GetProperty(name, _bf);
        if (p != null && p.PropertyType == typeof(int) && p.CanRead && p.CanWrite)
        {
            getter = () => (int)p.GetValue(obj, null);
            setter = v => p.SetValue(obj, v, null);
            return true;
        }

        return false;
    }

    private bool TryBindBool(object obj, string name, out Func<bool> getter, out Action<bool> setter)
    {
        getter = null;
        setter = null;
        if (obj == null || string.IsNullOrWhiteSpace(name))
            return false;

        var t = obj.GetType();

        var f = t.GetField(name, _bf);
        if (f != null && f.FieldType == typeof(bool))
        {
            getter = () => (bool)f.GetValue(obj);
            setter = v => f.SetValue(obj, v);
            return true;
        }

        var p = t.GetProperty(name, _bf);
        if (p != null && p.PropertyType == typeof(bool) && p.CanRead && p.CanWrite)
        {
            getter = () => (bool)p.GetValue(obj, null);
            setter = v => p.SetValue(obj, v, null);
            return true;
        }

        return false;
    }

    private void SaveIfPossible()
    {
        if (DataManager.instance == null)
            return;

        try
        {
            DataManager.instance.SubSaveCommit();
        }
        catch
        {
            // SubSaveCommit이 없거나 예외가 나도 게임이 터지지는 않게 조용히 무시
        }
    }

    #endregion

    #region Utility

    private CharacterConfig FindConfig(string characterName)
    {
        if (characterConfigs == null)
            return null;

        string key = (characterName ?? "").Trim();
        if (key.Length == 0)
            return null;

        foreach (var cfg in characterConfigs)
        {
            if (cfg == null) continue;
            if (string.Equals(cfg.characterName, key, StringComparison.OrdinalIgnoreCase))
                return cfg;
        }
        return null;
    }

    private string PickRandom(string[] arr)
    {
        if (arr == null || arr.Length == 0)
            return null;

        var list = arr.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (list.Length == 0)
            return null;

        int idx = UnityEngine.Random.Range(0, list.Length);
        return list[idx].Trim();
    }

    private bool CheckRandom(float chance)
    {
        if (chance >= 1f)
            return true;
        if (chance <= 0f)
            return false;
        return UnityEngine.Random.value < chance;
    }

    private void Log(string msg)
    {
        if (!enableLog)
            return;
        Debug.Log("[HealingEventManager] " + msg);
    }

    #endregion
}
