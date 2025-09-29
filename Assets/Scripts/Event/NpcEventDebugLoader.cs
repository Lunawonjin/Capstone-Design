// NpcEventDebugLoader.cs
// Unity 6 (LTS)
// 목적:
// - 하우스 진입(House→Door) 시점에만 이벤트 실행 여부를 판단
// - 판단 기준: DataManager의 PlayerData 내부 불리언 필드(예: Sol_First_Meet)가 false일 때만 실행
// - 실행 직후 해당 불리언을 true로 갱신(+ 선택적으로 DataManager 저장 메서드 호출)
// - 별도 PlayerPrefs/내부 Flag 사용 안 함
//
// 사용법(인스펙터):
// 1) teleporter: HouseDoorTeleporter_BiDirectional2D 참조 연결
// 2) owners: 오너 이름 + 이벤트 파일명들(확장자 제외) 등록
// 3) playerData 또는 dataManager 중 하나를 연결
//    - playerData에 직접 PlayerData 할당하거나
//    - dataManager에 팀의 DataManager(MonoBehaviour) 할당
//      (PlayerData를 public 필드/프로퍼티로 보유하고 있어야 함: 타입이 PlayerData면 자동 탐색)
// 4) saveAfterWrite=true로 두면 저장 메서드를 자동 탐색 호출(이름 후보: Save, Commit, Persist)
//
// 주의:
// - PlayerData 내부에 이벤트명과 "완전 동일한" 불리언 필드가 있어야 합니다(대소문자 무시)
//   예) 이벤트명 "Sol_First_Meet" → PlayerData.bool Sol_First_Meet

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class NpcEventDebugLoader : MonoBehaviour
{
    [Serializable]
    public class OwnerEvents
    {
        [Tooltip("오너 이름(예: Sol). 텔레포터 ownerNames[]와 동일 철자 권장")]
        public string ownerName = "Sol";

        [Tooltip("확장자 제외 이벤트 파일명들(예: Sol_First_Meet, Sol_ClinicIntro)")]
        public string[] eventNames = Array.Empty<string>();
    }

    [Serializable]
    public class LoadedEvent
    {
        public string ownerName;
        public string eventName;
        public string path;
        public string json;
    }

    [Header("텔레포터 연동")]
    [SerializeField] private HouseDoorTeleporter_BiDirectional2D teleporter;

    [Header("이벤트 폴더")]
    [Tooltip("Event/{owner}/{eventName}.json 형태로 탐색")]
    [SerializeField] private string eventFolderName = "Event";

    [Header("오너/이벤트 구성")]
    [SerializeField] private OwnerEvents[] owners = Array.Empty<OwnerEvents>();

    [Header("자동 실행 트리거")]
    [Tooltip("집에 들어섰을 때(오너 변경 감지)만 검사/실행")]
    [SerializeField] private bool autoRunOnHouseEnter = true;

    [Header("DataManager/PlayerData 연결")]
    [Tooltip("직접 PlayerData를 지정(없으면 dataManager에서 자동 탐색)")]
    [SerializeField] private PlayerData playerData;
    [Tooltip("팀의 DataManager(MonoBehaviour). 내부 public 필드/프로퍼티 중 PlayerData 타입을 자동 탐색")]
    [SerializeField] private MonoBehaviour dataManager;

    [Header("저장 호출 옵션")]
    [Tooltip("쓰기 후 DataManager 저장 메서드 호출")]
    [SerializeField] private bool saveAfterWrite = true;

    [Tooltip("저장 메서드 이름 후보(우선순위대로 탐색). 비워두면 기본 후보 사용")]
    [SerializeField] private string[] saveMethodCandidates = new[] { "Save", "Commit", "Persist", "Write", "Flush" };

    [Header("로그")]
    [SerializeField] private bool logWhenRun = true;
    [SerializeField] private bool logWhenSkip = true;
    [SerializeField] private bool verboseLog = true;

    // 메모리 적재(선택)
    private readonly Dictionary<string, Dictionary<string, LoadedEvent>> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    // 내부 상태
    private int _lastTeleporterOwnerIndex = int.MinValue;

    // 리플렉션 캐시
    private PlayerData _cachedPlayer;
    private MemberInfo _cachedSaveMember;
    private readonly BindingFlags _bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        // PlayerData 해석/캐싱
        _cachedPlayer = ResolvePlayerData();
        if (_cachedPlayer == null)
            Debug.LogWarning("[NpcEventDebugLoader] PlayerData를 찾지 못했습니다. playerData 또는 dataManager 연결을 확인하십시오.");

        // 저장 메서드 캐싱
        if (saveAfterWrite)
            _cachedSaveMember = ResolveSaveMember();
    }

    private void Start()
    {
        if (teleporter != null)
            _lastTeleporterOwnerIndex = teleporter.CurrentOwnerIndex;
    }

    private void Update()
    {
        if (!autoRunOnHouseEnter || teleporter == null) return;

        int now = teleporter.CurrentOwnerIndex;
        if (now != _lastTeleporterOwnerIndex && now >= 0)
        {
            _lastTeleporterOwnerIndex = now;
            string owner = Canon(teleporter.CurrentOwnerName);
            if (!string.IsNullOrEmpty(owner))
                RunBundleIfNeeded(owner);
        }
    }

    // ===== 핵심: 오너 번들 조건 실행 =====
    private void RunBundleIfNeeded(string ownerCanonical)
    {
        var cfg = FindOwner(ownerCanonical);
        if (cfg == null || cfg.eventNames == null || cfg.eventNames.Length == 0)
        {
            if (verboseLog) Debug.LogWarning($"[NpcEventDebugLoader] 설정 없음: owner='{ownerCanonical}'");
            return;
        }

        var pd = _cachedPlayer ?? ResolvePlayerData();
        if (pd == null)
        {
            Debug.LogWarning("[NpcEventDebugLoader] PlayerData가 없어 실행을 건너뜁니다.");
            return;
        }

        foreach (var raw in cfg.eventNames)
        {
            var ev = Canon(raw);
            if (string.IsNullOrEmpty(ev)) continue;

            // PlayerData의 bool 필드/프로퍼티를 대소문자 무시로 찾아 읽음
            if (!TryBindBool(pd, ev, out Func<bool> getter, out Action<bool> setter))
            {
                if (verboseLog)
                    Debug.LogWarning($"[NpcEventDebugLoader] PlayerData에 '{ev}'(bool)을 찾지 못했습니다. 이벤트명을 PlayerData 필드와 맞추십시오.");
                continue;
            }

            bool done = getter(); // 이미 수행?
            if (done)
            {
                if (logWhenSkip)
                    Debug.Log($"[NpcEventDebugLoader] 건너뜀(이미 true): owner='{ownerCanonical}', event='{ev}'");
                continue;
            }

            // 아직 false → JSON 로드/로그 → true로 변경(+ 저장)
            if (TryLoadSingle(ownerCanonical, ev, out var le))
            {
                Cache(le);
                if (logWhenRun) LogLoaded(le);

                setter(true);
                if (saveAfterWrite) InvokeSave();

                if (verboseLog)
                    Debug.Log($"[NpcEventDebugLoader] 실행 후 true로 갱신: owner='{ownerCanonical}', event='{ev}'");
            }
            else
            {
                Debug.LogError($"[NpcEventDebugLoader] 로드 실패: owner='{ownerCanonical}', event='{ev}'");
            }
        }
    }

    // ===== 파일 로드 =====
    private bool TryLoadSingle(string ownerCanonical, string eventCanonical, out LoadedEvent loaded)
    {
        loaded = null;
        string file = eventCanonical + ".json";
        string p1 = Path.Combine(Application.streamingAssetsPath, eventFolderName, ownerCanonical, file);
        string p2 = Path.Combine(Application.dataPath, eventFolderName, ownerCanonical, file);

        string chosen = null;
        string json = null;

        try
        {
            if (File.Exists(p1)) { chosen = p1; json = File.ReadAllText(p1); }
            else if (File.Exists(p2)) { chosen = p2; json = File.ReadAllText(p2); }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NpcEventDebugLoader] JSON 예외: owner='{ownerCanonical}', event='{eventCanonical}', err={e}");
            return false;
        }

        if (string.IsNullOrEmpty(chosen) || string.IsNullOrEmpty(json)) return false;

        loaded = new LoadedEvent
        {
            ownerName = ownerCanonical,
            eventName = eventCanonical,
            path = chosen,
            json = json
        };
        return true;
    }

    // ===== 캐시/로그 =====
    private void Cache(LoadedEvent le)
    {
        if (!_loaded.TryGetValue(le.ownerName, out var byEvent))
        {
            byEvent = new Dictionary<string, LoadedEvent>(StringComparer.OrdinalIgnoreCase);
            _loaded[le.ownerName] = byEvent;
        }
        byEvent[le.eventName] = le;
    }

    private void LogLoaded(LoadedEvent le)
    {
        Debug.Log($"[NpcEventDebugLoader] 로드/실행\nowner: {le.ownerName}\nevent: {le.eventName}\n경로: {le.path}\n내용:\n{le.json}");
    }

    // ===== PlayerData 접근 유틸 =====
    private PlayerData ResolvePlayerData()
    {
        if (playerData != null) return playerData;

        if (dataManager == null) return null;

        var t = dataManager.GetType();

        // 필드에서 PlayerData 찾기
        var f = t.GetFields(_bf).FirstOrDefault(fi => fi.FieldType == typeof(PlayerData));
        if (f != null) return f.GetValue(dataManager) as PlayerData;

        // 프로퍼티에서 PlayerData 찾기
        var p = t.GetProperties(_bf).FirstOrDefault(pi => pi.PropertyType == typeof(PlayerData) && pi.CanRead);
        if (p != null) return p.GetValue(dataManager, null) as PlayerData;

        return null;
    }

    private MemberInfo ResolveSaveMember()
    {
        if (dataManager == null) return null;

        var t = dataManager.GetType();
        string[] candidates = (saveMethodCandidates != null && saveMethodCandidates.Length > 0)
            ? saveMethodCandidates
            : new[] { "Save", "Commit", "Persist", "Write", "Flush" };

        foreach (var name in candidates)
        {
            var m = t.GetMethod(name, _bf, null, Type.EmptyTypes, null);
            if (m != null) return m;
        }
        return null;
    }

    private void InvokeSave()
    {
        if (_cachedSaveMember is MethodInfo mi && dataManager != null)
        {
            try { mi.Invoke(dataManager, null); }
            catch (Exception e) { Debug.LogWarning($"[NpcEventDebugLoader] 저장 메서드 호출 실패: {e}"); }
        }
    }

    private static string Canon(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return s.Trim().ToLowerInvariant();
    }

    private OwnerEvents FindOwner(string ownerCanonical)
    {
        if (owners == null) return null;
        foreach (var oe in owners)
        {
            if (oe == null) continue;
            if (Canon(oe.ownerName) == ownerCanonical) return oe;
        }
        return null;
    }

    private bool TryBindBool(object obj, string boolName, out Func<bool> getter, out Action<bool> setter)
    {
        getter = null; setter = null;
        var t = obj.GetType();

        // 1) 필드 검색(대소문자 무시)
        var field = t.GetFields(_bf).FirstOrDefault(fi =>
            fi.FieldType == typeof(bool) &&
            string.Equals(fi.Name, boolName, StringComparison.OrdinalIgnoreCase));

        if (field != null)
        {
            getter = () => (bool)field.GetValue(obj);
            setter = v => field.SetValue(obj, v);
            return true;
        }

        // 2) 프로퍼티 검색(대소문자 무시)
        var prop = t.GetProperties(_bf).FirstOrDefault(pi =>
            pi.PropertyType == typeof(bool) &&
            string.Equals(pi.Name, boolName, StringComparison.OrdinalIgnoreCase) &&
            pi.CanRead && pi.CanWrite);

        if (prop != null)
        {
            getter = () => (bool)prop.GetValue(obj, null);
            setter = v => prop.SetValue(obj, v, null);
            return true;
        }

        return false;
    }
}
