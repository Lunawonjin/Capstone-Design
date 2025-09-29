// NpcEventDebugLoader.cs
// Unity 6 (LTS)
//
// 기능 요약
// - HouseDoorTeleporter_BiDirectional2D의 CurrentOwnerName 변화를 감지(집 진입)
// - PlayerData의 해당 불리언(이벤트명) == false 일 때만 이벤트 실행
// - 실행 순서:
//   1) UI 캔버스 비활성화 + 플레이어 조작 비활성화 + 현재 위치 저장
//   2) 이벤트 JSON 파싱(상대 이동 스텝들) → 순차 이동
//   3) 이벤트 종료: PlayerData 불리언 true로 설정(+ 옵션 저장)
//   4) 화면 페이드아웃(짧게) → 플레이어를 저장 위치로 스냅 → 페이드인
//   5) UI/조작 복구
//
// JSON 스키마(예시는 본 파일 맨 아래 주석 참고)

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;
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

    // ── 이벤트 JSON 스키마 ─────────────────────────────────────────────
    [Serializable]
    public class EventScript
    {
        public EventStep[] steps = Array.Empty<EventStep>();
        public float defaultStepDuration = 0.5f;   // 개별 step에 duration이 없을 때 사용
        public bool useWorldSpace = true;          // true=월드 좌표, false=로컬
        public bool useRigidbodyMove = true;       // Rigidbody2D가 있으면 MovePosition 사용
    }

    [Serializable]
    public class EventStep
    {
        // 방법 1) axis+delta : axis="x"|"y", delta=±거리
        public string axis = "";       // "x" 또는 "y" (대소문자 무시)
        public float delta = 0f;

        // 방법 2) dx,dy를 직접 사용(둘 중 하나 방식만 써도 됨)
        public float dx = 0f;
        public float dy = 0f;

        // 선택: 이 스텝만의 개별 지속시간(없으면 defaultStepDuration 사용)
        public float duration = -1f;
    }
    // ──────────────────────────────────────────────────────────────────

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
    [Tooltip("저장 메서드 후보. 비우면 기본 후보 사용")]
    [SerializeField] private string[] saveMethodCandidates = new[] { "Save", "Commit", "Persist", "Write", "Flush" };

    [Header("연출 제어(이벤트 중)")]
    [Tooltip("이벤트 중 비활성화할 UI Canvas 루트(여러 개 가능)")]
    [SerializeField] private GameObject[] uiCanvasesToDisable = Array.Empty<GameObject>();

    [Tooltip("조작 비활성화를 담당할 컴포넌트(없으면 playerTransform에서 자동 탐색 시도)")]
    [SerializeField] private MonoBehaviour playerControlToggle; // IPlayerControlToggle 구현체 권장(아래 인터페이스 참고)

    [Tooltip("플레이어 Transform(비우면 본 오브젝트 Transform)")]
    [SerializeField] private Transform playerTransform;

    [Tooltip("플레이어 이동 시 Rigidbody2D 사용(있다면)")]
    [SerializeField] private bool preferRigidbodyMove = true;

    [Header("페이드(어둡게했다가 복귀)")]
    [Tooltip("검은 화면용 CanvasGroup(알파 0~1)")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeOutDuration = 0.25f;
    [SerializeField] private float fadeInDuration = 0.25f;

    [Header("로그")]
    [SerializeField] private bool logWhenRun = true;
    [SerializeField] private bool logWhenSkip = true;
    [SerializeField] private bool verboseLog = true;

    // 런타임 상태
    private readonly Dictionary<string, Dictionary<string, LoadedEvent>> _loaded =
        new(StringComparer.OrdinalIgnoreCase);
    private int _lastTeleporterOwnerIndex = int.MinValue;

    // 리플렉션 캐시
    private PlayerData _cachedPlayer;
    private MemberInfo _cachedSaveMember;
    private readonly BindingFlags _bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // 내부
    private Vector3 _savedPlayerPosition;
    private Rigidbody2D _playerRb2D;
    private IPlayerControlToggle _control; // 아래 인터페이스

    // ── 플레이어 조작 토글용 인터페이스(게임 쪽 구현 권장) ─────────────
    public interface IPlayerControlToggle
    {
        void SetControlEnabled(bool enabled);
    }
    // ─────────────────────────────────────────────────────────────────

    private void Reset()
    {
        if (!playerTransform) playerTransform = transform;
    }

    private void Awake()
    {
        if (!playerTransform) playerTransform = transform;
        _playerRb2D = playerTransform.GetComponent<Rigidbody2D>();

        // PlayerData/Save 메서드 캐시
        _cachedPlayer = ResolvePlayerData();
        if (_cachedPlayer == null)
            Debug.LogWarning("[NpcEventDebugLoader] PlayerData를 찾지 못했습니다. playerData 또는 dataManager 연결을 확인하십시오.");
        if (saveAfterWrite)
            _cachedSaveMember = ResolveSaveMember();

        // 조작 토글 소스
        _control = playerControlToggle as IPlayerControlToggle;
        if (_control == null && playerTransform)
            _control = playerTransform.GetComponent<IPlayerControlToggle>();
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

    // ── 실행 진입점 ────────────────────────────────────────────────
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

            // PlayerData bool 바인딩
            if (!TryBindBool(pd, ev, out Func<bool> getter, out Action<bool> setter))
            {
                if (verboseLog)
                    Debug.LogWarning($"[NpcEventDebugLoader] PlayerData에 '{ev}'(bool)을 찾지 못했습니다. 이벤트명을 PlayerData 필드와 맞추십시오.");
                continue;
            }

            bool done = getter();
            if (done)
            {
                if (logWhenSkip)
                    Debug.Log($"[NpcEventDebugLoader] 건너뜀(이미 true): owner='{ownerCanonical}', event='{ev}'");
                continue;
            }

            // 아직 false → JSON 로드 → 이벤트 코루틴 실행
            if (TryLoadSingle(ownerCanonical, ev, out var le))
            {
                Cache(le);
                if (logWhenRun) LogLoaded(le);

                // 실제 연출 실행: 끝나면 true 세팅
                StartCoroutine(RunEventCoroutine(ownerCanonical, ev, le.json, onComplete: () =>
                {
                    setter(true);
                    if (saveAfterWrite) InvokeSave();
                    if (verboseLog)
                        Debug.Log($"[NpcEventDebugLoader] 이벤트 종료 → '{ev}' = true");
                }));
            }
            else
            {
                Debug.LogError($"[NpcEventDebugLoader] 로드 실패: owner='{ownerCanonical}', event='{ev}'");
            }
        }
    }

    // ── 이벤트 코루틴(연출 본체) ──────────────────────────────────
    private IEnumerator RunEventCoroutine(string owner, string eventName, string json, Action onComplete)
    {
        // 1) UI/조작 OFF + 현재 위치 저장
        ToggleUICanvases(false);
        SetPlayerControl(false);
        _savedPlayerPosition = playerTransform.position;

        // 2) JSON 파싱
        EventScript script = null;
        try { script = JsonUtility.FromJson<EventScript>(json); }
        catch (Exception e)
        {
            Debug.LogError($"[NpcEventDebugLoader] JSON 파싱 실패: owner='{owner}', event='{eventName}', err={e}");
        }
        if (script == null)
        {
            Debug.LogWarning($"[NpcEventDebugLoader] 유효하지 않은 JSON. 이벤트를 즉시 종료합니다: {owner}/{eventName}");
            yield return StartCoroutine(FadeOutInAndReturn());
            SetPlayerControl(true);
            ToggleUICanvases(true);
            onComplete?.Invoke();
            yield break;
        }

        // 3) 이동 스텝 실행
        foreach (var step in script.steps ?? Array.Empty<EventStep>())
        {
            var (dx, dy) = NormalizeStep(step);
            float dur = step.duration > 0f ? step.duration : Mathf.Max(0f, script.defaultStepDuration);

            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f))
            {
                if (dur > 0f) yield return new WaitForSeconds(dur);
                continue;
            }

            // 월드/로컬 기준 결정
            if (script.useWorldSpace)
                yield return MoveByWorld(new Vector2(dx, dy), dur, script.useRigidbodyMove);
            else
                yield return MoveByLocal(new Vector2(dx, dy), dur, script.useRigidbodyMove);
        }

        // 4) 페이드아웃 → 원위치로 복귀 → 페이드인
        yield return StartCoroutine(FadeOutInAndReturn());

        // 5) UI/조작 ON
        SetPlayerControl(true);
        ToggleUICanvases(true);

        // 6) 완료 콜백(불리언 True 설정)
        onComplete?.Invoke();
    }

    private (float dx, float dy) NormalizeStep(EventStep s)
    {
        float dx = s.dx, dy = s.dy;
        if (!string.IsNullOrEmpty(s.axis))
        {
            var ax = s.axis.Trim().ToLowerInvariant();
            if (ax == "x") dx += s.delta;
            else if (ax == "y") dy += s.delta;
        }
        return (dx, dy);
    }

    private IEnumerator MoveByWorld(Vector2 delta, float duration, bool useRb)
    {
        Vector3 start = playerTransform.position;
        Vector3 target = start + new Vector3(delta.x, delta.y, 0f);

        if (duration <= 0f)
        {
            SnapPlayer(target, useRb);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Vector3 pos = Vector3.Lerp(start, target, Mathf.Clamp01(t / duration));
            SnapPlayer(pos, useRb);
            yield return null;
        }
        SnapPlayer(target, useRb);
    }

    private IEnumerator MoveByLocal(Vector2 delta, float duration, bool useRb)
    {
        Vector3 start = playerTransform.localPosition;
        Vector3 target = start + new Vector3(delta.x, delta.y, 0f);

        if (duration <= 0f)
        {
            if (useRb && _playerRb2D) _playerRb2D.position = playerTransform.parent.TransformPoint(target);
            playerTransform.localPosition = target;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Vector3 lp = Vector3.Lerp(start, target, Mathf.Clamp01(t / duration));
            if (useRb && _playerRb2D) _playerRb2D.position = playerTransform.parent.TransformPoint(lp);
            playerTransform.localPosition = lp;
            yield return null;
        }
        if (useRb && _playerRb2D) _playerRb2D.position = playerTransform.parent.TransformPoint(target);
        playerTransform.localPosition = target;
    }

    private void SnapPlayer(Vector3 worldTarget, bool useRb)
    {
        if (preferRigidbodyMove && useRb && _playerRb2D)
        {
            _playerRb2D.linearVelocity = Vector2.zero;  // <- 변경
            _playerRb2D.angularVelocity = 0f;
            _playerRb2D.MovePosition(new Vector2(worldTarget.x, worldTarget.y));
        }
        else
        {
            playerTransform.position = worldTarget;
        }
    }


    private IEnumerator FadeOutInAndReturn()
    {
        // 페이드아웃
        yield return FadeTo(1f, fadeOutDuration);

        // 어두울 때 원위치 스냅
        SnapPlayer(_savedPlayerPosition, useRb: true);

        // 페이드인
        yield return FadeTo(0f, fadeInDuration);
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (!fadeCanvasGroup || duration <= 0f)
        {
            if (fadeCanvasGroup) fadeCanvasGroup.alpha = targetAlpha;
            yield break;
        }
        fadeCanvasGroup.blocksRaycasts = targetAlpha > 0.99f;

        float start = fadeCanvasGroup.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t / duration));
            yield return null;
        }
        fadeCanvasGroup.alpha = targetAlpha;
        fadeCanvasGroup.blocksRaycasts = targetAlpha > 0.99f;
    }

    private void ToggleUICanvases(bool enabled)
    {
        if (uiCanvasesToDisable == null) return;
        foreach (var go in uiCanvasesToDisable)
            if (go) go.SetActive(enabled);
    }

    private void SetPlayerControl(bool enabled)
    {
        // 우선 인터페이스로 토글
        if (_control != null)
        {
            _control.SetControlEnabled(enabled);
            return;
        }

        // 없으면 최소한 물리 제동
        if (!enabled && _playerRb2D)
        {
            _playerRb2D.linearVelocity = Vector2.zero;  // <- 변경
            _playerRb2D.angularVelocity = 0f;
        }
    }


    // ── 파일 로드/캐시/로그 ────────────────────────────────────────
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

        loaded = new LoadedEvent { ownerName = ownerCanonical, eventName = eventCanonical, path = chosen, json = json };
        return true;
    }

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

    // ── PlayerData/Save 탐색 & 바인딩 ───────────────────────────────
    private PlayerData ResolvePlayerData()
    {
        if (playerData != null) return playerData;
        if (dataManager == null) return null;

        var t = dataManager.GetType();

        var f = t.GetFields(_bf).FirstOrDefault(fi => fi.FieldType == typeof(PlayerData));
        if (f != null) return f.GetValue(dataManager) as PlayerData;

        var p = t.GetProperties(_bf).FirstOrDefault(pi => pi.PropertyType == typeof(PlayerData) && pi.CanRead);
        if (p != null) return p.GetValue(dataManager, null) as PlayerData;

        return null;
    }

    private MemberInfo ResolveSaveMember()
    {
        if (dataManager == null) return null;

        var t = dataManager.GetType();
        string[] cands = (saveMethodCandidates != null && saveMethodCandidates.Length > 0)
            ? saveMethodCandidates
            : new[] { "Save", "Commit", "Persist", "Write", "Flush" };

        foreach (var name in cands)
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

        var field = t.GetFields(_bf).FirstOrDefault(fi =>
            fi.FieldType == typeof(bool) &&
            string.Equals(fi.Name, boolName, StringComparison.OrdinalIgnoreCase));
        if (field != null)
        {
            getter = () => (bool)field.GetValue(obj);
            setter = v => field.SetValue(obj, v);
            return true;
        }

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
