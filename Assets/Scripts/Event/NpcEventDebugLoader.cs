// Unity 6 (LTS)
// Cutscene/Event System (JSON-driven)
// - JSON path: StreamingAssets/Event/{owner}/{event}.json (fallback: Assets/Event/...)
// - Script supports: npcSpawns / steps / afterPlayerActions (npcMove, log, dialogue, dialoguePanelActive, npcSetActive, eventEnd)
// - Dialogue: DialogueRunnerStringTables.BeginWithEventName("{Event}")
//   Uses StringTables: "{Event}_Speaker" / "{Event}_Dialogue" (fallback: "... table")
// - dialogueReactions: fire actions when a specific StringTable key is shown
// - Temporary hide dialogue panel -> move/deactivate boss -> auto restore panel
//
// Notes:
// - 이벤트 종료 대기는 GameObject 활성 여부 대신 OnDialogueEnded 이벤트로 변경(비활성 코루틴 예외 방지)
// - NpcMoveByWorld는 단일 정의 보장(중복/모호성 제거)

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;

[DisallowMultipleComponent]
public class NpcEventDebugLoader : MonoBehaviour
{
    // 외부로부터 플레이어 입력 ON/OFF를 제어할 수 있게 하는 인터페이스(선택)
    public interface IPlayerControlToggle { void SetControlEnabled(bool enabled); }

    // ───────────── 데이터 스키마 ─────────────
    [Serializable] public class OwnerEvents { public string ownerName = "Sol"; public string[] eventNames = Array.Empty<string>(); }
    [Serializable] public class LoadedEvent { public string ownerName; public string eventName; public string path; public string json; }

    [Serializable]
    public class PlayerSection
    {
        public bool forceUsePlayerMoveSpeed = false;
        public float moveSpeed = 1.0f;
    }

    [Serializable]
    public class EventStep
    {
        public string axis = "";   // "x" or "y"
        public float delta = 0f;   // axis에 따라 적용
        public float dx = 0f;      // 직접 지정
        public float dy = 0f;      // 직접 지정
        public float duration = -1f; // <=0면 defaultStepDuration 사용
    }

    [Serializable]
    public class NpcSpawnCmd
    {
        public string npcName = "";
        public float x = 0f, y = 0f, z = 0f;
        public bool deactivateExisting = true;
        public bool relativeToPlayer = false;
    }

    [Serializable]
    public class EventPostAction
    {
        // "npcMove" | "log" | "dialogue" | "dialoguePanelActive" | "npcSetActive" | "eventEnd"
        public string type = "";
        public string npcName = "";
        public float dx = 0f, dy = 0f;
        public float duration = 1f;
        public string message = "";

        // dialogue 전용(비우면 현재 owner/event)
        public string owner = "";
        public string eventName = "";

        // dialoguePanelActive / npcSetActive 전용
        public bool active = true;
    }

    [Serializable]
    public class DialogueReaction
    {
        public string onKey = ""; // ex) "Dialogue_Choice1_Same_001"
        public EventPostAction[] actions = Array.Empty<EventPostAction>();
    }

    [Serializable]
    public class EventScript
    {
        public EventStep[] steps = Array.Empty<EventStep>();
        public float defaultStepDuration = 0.5f;
        public bool useWorldSpace = true;
        public bool useRigidbodyMove = true;

        public NpcSpawnCmd[] npcSpawns = Array.Empty<NpcSpawnCmd>();
        public PlayerSection player = null;
        public EventPostAction[] afterPlayerActions = Array.Empty<EventPostAction>();

        public DialogueReaction[] dialogueReactions = Array.Empty<DialogueReaction>();
    }

    // ───────────── 인스펙터 ─────────────
    [Header("텔레포터 연동")]
    [SerializeField] private HouseDoorTeleporter_BiDirectional2D teleporter;

    [Header("이벤트 폴더 (Event/{owner}/{event}.json)")]
    [SerializeField] private string eventFolderName = "Event";

    [Header("오너/이벤트 구성")]
    [SerializeField] private OwnerEvents[] owners = Array.Empty<OwnerEvents>();

    [Header("자동 실행 트리거")]
    [SerializeField] private bool autoRunOnHouseEnter = true;

    [Header("DataManager/PlayerData 연결")]
    [SerializeField] private PlayerData playerData;
    [SerializeField] private MonoBehaviour dataManager;

    [Header("저장 호출 옵션")]
    [SerializeField] private bool saveAfterWrite = true;
    [SerializeField] private string[] saveMethodCandidates = new[] { "Save", "Commit", "Persist", "Write", "Flush" };

    [Header("플레이어/애니 연동")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private PlayerMove playerMove;
    private Rigidbody2D _playerRb2D;
    private Animator _anim;
    private SpriteRenderer _sr;

    [Header("이벤트 중 끌 것들")]
    [SerializeField] private GameObject[] uiCanvasesToDisable = Array.Empty<GameObject>();
    [SerializeField] private MonoBehaviour[] inputComponents = Array.Empty<MonoBehaviour>();
    [SerializeField] private bool freezePhysicsDuringEvent = true;
    [SerializeField] private bool preferRigidbodyMove = true;

    [Header("페이드(어둡게했다가 복귀)")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeOutDuration = 0.25f;
    [SerializeField] private float fadeInDuration = 0.25f;

    [Header("NPC 카탈로그(오너별 다수 등록 가능)")]
    [SerializeField] private NpcSpec[] npcCatalog = Array.Empty<NpcSpec>();

    [Header("NPC 스폰 정책")]
    [Tooltip("집 들어갈 때, 해당 오너 외 NPC를 먼저 끄거나 파괴")]
    [SerializeField] private bool deactivateOtherOwnersNpcsOnEnter = true;

    [Header("Dialogue 연동")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private DialogueRunnerStringTables dialogueManager;
    [SerializeField] private bool autoFindDialogueManager = true;

    [Header("로그")]
    [SerializeField] private bool logWhenRun = true;
    [SerializeField] private bool logWhenSkip = true;
    [SerializeField] private bool verboseLog = true;
    [SerializeField] private bool logSanitizedJsonOnError = true;

    // 클래스 필드로 추가
    private float _currentPlayerMoveSpeedForEvent = 0f;



    // ───────────── 런타임 상태 ─────────────
    private readonly Dictionary<string, Dictionary<string, LoadedEvent>> _loaded =
        new(StringComparer.OrdinalIgnoreCase);
    private int _lastTeleporterOwnerIndex = int.MinValue;
    private bool _eventRunning;
    private Vector3 _savedPlayerPosition;

    private PlayerData _cachedPlayer;
    private MemberInfo _cachedSaveMember;
    private readonly BindingFlags _bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private struct PhysicsBackup
    {
        public bool hasRb;
        public bool simulated;
        public RigidbodyType2D bodyType;
        public RigidbodyConstraints2D constraints;
        public Vector2 linearVelocity;
        public float angularVelocity;
    }
    private PhysicsBackup _rbBackup;

    private struct InputBackup { public MonoBehaviour comp; public bool wasEnabled; }
    private readonly List<InputBackup> _inputBackup = new();
    private struct UIBak { public GameObject go; public bool wasActive; }
    private readonly List<UIBak> _uiBackup = new();

    private struct AnimSpriteBackup
    {
        public bool hasAnimator;
        public int stateHash;
        public float normalizedTime;
        public float speed;
        public bool hasSpriteRenderer;
        public Sprite sprite;
        public bool flipX;
        public bool flipY;
    }
    private AnimSpriteBackup _animBak;

    [Serializable]
    public class NpcSpec
    {
        [Header("기본")]
        public string ownerName = "Sol";
        public string npcName = "Sol_Npc";
        public GameObject prefab;

        public bool spawnOnHouseEnter = true;
        public Vector3 spawnOffset = Vector3.zero;
        public Transform parent;

        public bool reuseIfAlreadySpawned = true;
        public bool deactivateOnExitHouse = true;
        public bool destroyOnExitHouse = false;

        [Header("애니메이터(선택)")]
        public RuntimeAnimatorController animatorController;
        public bool addAnimatorIfMissing = true;

        [Header("애니메이션 설정(선택)")]
        public bool overrideWalkStates = false;
        public string stateFront = "Front_Walk";
        public string stateBack = "Back_Walk";
        public string stateLeft = "Left_Walk";
        public string stateRight = "Right_Walk";
        public float animSpeedScale = 1f;
    }

    private readonly Dictionary<string, List<NpcSpec>> _npcSpecsByKey =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, GameObject>> _spawnedNpcByKey =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NpcSpec> _npcSpecByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameObject> _spawnedNpcByName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<GameObject> _tempDeactivatedMapNpcs = new();

    private class OwnerDeactivateSnapshot
    {
        public string key;
        public List<GameObject> deactivated = new();
    }
    private readonly List<OwnerDeactivateSnapshot> _ownersDeactivatedOnEnter = new();

    private readonly List<GameObject> _spawnedDuringEvent = new();

    // 다이얼로그 패널 임시 비활용 복구 플래그
    private bool _dialoguePanelTemporarilyHidden = false;
    private bool _dialoguePanelAutoReEnableArmed = false;

    // 대사 키 리액션 관리
    private DialogueReaction[] _currentDialogueReactions = Array.Empty<DialogueReaction>();
    private readonly HashSet<string> _reactionsFired = new(StringComparer.OrdinalIgnoreCase);

    // ───────────── Unity 수명주기 ─────────────
    private void Reset()
    {
        if (!playerTransform) playerTransform = transform;
    }

    private void Awake()
    {
        if (!playerTransform) playerTransform = transform;

        _playerRb2D = playerTransform.GetComponent<Rigidbody2D>();
        _anim = playerTransform.GetComponent<Animator>();
        _sr = playerTransform.GetComponent<SpriteRenderer>();
        if (playerMove == null) playerMove = playerTransform.GetComponent<PlayerMove>();

        _cachedPlayer = ResolvePlayerData();
        if (_cachedPlayer == null)
            Debug.LogWarning("[NpcEventDebugLoader] PlayerData를 찾지 못했습니다. playerData 또는 dataManager 연결을 확인하십시오.");
        if (saveAfterWrite)
            _cachedSaveMember = ResolveSaveMember();

        IndexNpcCatalog();
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

            string ownerInput = teleporter.CurrentOwnerName;
            var cfg = FindOwnerCI(ownerInput, out string ownerForIO);
            if (cfg != null && !string.IsNullOrEmpty(ownerForIO))
            {
                SpawnNpcsForOwnerOnEnter(ownerForIO);
                RunBundleIfNeeded(cfg, ownerForIO);
            }
            else if (verboseLog)
            {
                Debug.LogWarning($"[NpcEventDebugLoader] Owner 매칭 실패: '{ownerInput}'");
            }
        }
    }

    // ───────────── 오너 이벤트 실행 ─────────────
    private void RunBundleIfNeeded(OwnerEvents cfg, string ownerForIO)
    {
        if (_eventRunning) return;

        if (cfg == null || cfg.eventNames == null || cfg.eventNames.Length == 0)
        {
            if (verboseLog) Debug.LogWarning($"[NpcEventDebugLoader] 설정 없음: owner='{ownerForIO}'");
            return;
        }

        var pd = _cachedPlayer ?? ResolvePlayerData();
        if (pd == null)
        {
            Debug.LogWarning("[NpcEventDebugLoader] PlayerData가 없어 실행을 건너뜁니다.");
            return;
        }

        foreach (var evRaw in cfg.eventNames)
        {
            string eventForIO = evRaw?.Trim();
            if (string.IsNullOrEmpty(eventForIO)) continue;

            if (!TryBindBool(pd, eventForIO, out Func<bool> getter, out Action<bool> setter))
            {
                if (verboseLog)
                    Debug.LogWarning($"[NpcEventDebugLoader] PlayerData에 '{eventForIO}'(bool)을 찾지 못했습니다.");
                continue;
            }

            if (getter())
            {
                if (logWhenSkip) Debug.Log($"[NpcEventDebugLoader] 건너뜀(이미 true): {ownerForIO}/{eventForIO}");
                continue;
            }

            if (TryLoadSingle(ownerForIO, eventForIO, out var le))
            {
                Cache(le);
                if (logWhenRun) LogLoaded(le);

                StartCoroutine(RunEventCoroutine(ownerForIO, eventForIO, le.json, () =>
                {
                    setter(true);
                    if (saveAfterWrite) InvokeSave();
                }));
                break; // 한 번에 하나만 실행
            }
            else
            {
                Debug.LogError($"[NpcEventDebugLoader] 로드 실패: {ownerForIO}/{eventForIO}");
            }
        }
    }

    private IEnumerator RunEventCoroutine(string ownerForIO, string eventForIO, string rawJson, Action onComplete)
    {
        if (_eventRunning) yield break;
        _eventRunning = true;

        _spawnedDuringEvent.Clear();
        EnterEventGuard();

        if (!TryParseEventScript(rawJson, out EventScript script, logSanitizedJsonOnError))
        {
            Debug.LogWarning($"[NpcEventDebugLoader] JSON 무효. 즉시 종료: {ownerForIO}/{eventForIO}");
            yield return StartCoroutine(FadeOutInAndReturn());
            ExitEventGuard();
            onComplete?.Invoke();
            _eventRunning = false;
            yield break;
        }

        if (script.player != null && script.player.forceUsePlayerMoveSpeed && script.player.moveSpeed > 1e-4f)
            _currentPlayerMoveSpeedForEvent = script.player.moveSpeed;
        // 현재 이벤트의 리액션 준비
        _currentDialogueReactions = script.dialogueReactions ?? Array.Empty<DialogueReaction>();
        _reactionsFired.Clear();

        // NPC 스폰 명령 수행
        HandleNpcSpawnCommands(script);

        for (int i = 0; i < (script.steps?.Length ?? 0); i++)
        {
            var step = script.steps[i];
            var (sx, sy) = NormalizeStep(step);

            // 속도 고정(기본 1). JSON의 player.forceUsePlayerMoveSpeed=true, moveSpeed=1.0 권장
            float speed = 1f;
            bool useFixedSpeed = script.player != null && script.player.forceUsePlayerMoveSpeed;
            if (useFixedSpeed)
                speed = Mathf.Max(1e-4f, script.player.moveSpeed);

            // 애니 방향 미리 계산
            Vector2 dirAnim = new Vector2(Mathf.Sign(sx), Mathf.Sign(sy));
            if (Mathf.Abs(sx) >= Mathf.Abs(sy)) dirAnim = new Vector2(Mathf.Sign(sx), 0f);
            else dirAnim = new Vector2(0f, Mathf.Sign(sy));

            // 애니 재생
            if (playerMove != null)
            {
                // 속도 기반 애니 스케일을 1.0 근방으로 고정
                playerMove.ExternalAnim_PlayWalk(dirAnim, 1.0f);
            }

            // 대각선 금지: 두 축으로 쪼개서 순차 이동
            if (!Mathf.Approximately(sx, 0f) && !Mathf.Approximately(sy, 0f))
            {
                // X 먼저, 그 다음 Y(필요 시 순서 바꿔도 무방)
                float durX = useFixedSpeed ? Mathf.Abs(sx) / speed
                                           : (step.duration > 0f ? step.duration * Mathf.Abs(sx) / (Mathf.Abs(sx) + Mathf.Abs(sy)) : Mathf.Max(0f, script.defaultStepDuration));
                float durY = useFixedSpeed ? Mathf.Abs(sy) / speed
                                           : (step.duration > 0f ? step.duration * Mathf.Abs(sy) / (Mathf.Abs(sx) + Mathf.Abs(sy)) : Mathf.Max(0f, script.defaultStepDuration));

                if (Mathf.Abs(sx) > 0f)
                    yield return (script.useWorldSpace)
                        ? MoveByWorld(new Vector2(sx, 0f), durX, script.useRigidbodyMove)
                        : MoveByLocal(new Vector2(sx, 0f), durX, script.useRigidbodyMove);

                if (Mathf.Abs(sy) > 0f)
                    yield return (script.useWorldSpace)
                        ? MoveByWorld(new Vector2(0f, sy), durY, script.useRigidbodyMove)
                        : MoveByLocal(new Vector2(0f, sy), durY, script.useRigidbodyMove);
            }
            else
            {
                // 단일 축 이동
                float dist = Mathf.Abs(sx) + Mathf.Abs(sy); // 둘 중 하나만 0이 아님
                float dur = useFixedSpeed
                            ? dist / speed
                            : (step.duration > 0f ? step.duration : Mathf.Max(0f, script.defaultStepDuration));

                if (Mathf.Approximately(dist, 0f))
                {
                    if (dur > 0f) yield return new WaitForSeconds(dur);
                }
                else
                {
                    Vector2 delta = new Vector2(sx, sy);
                    yield return (script.useWorldSpace)
                        ? MoveByWorld(delta, dur, script.useRigidbodyMove)
                        : MoveByLocal(delta, dur, script.useRigidbodyMove);
                }
            }

            // 다음 스텝이 정지면 애니 정지
            if (playerMove != null)
            {
                bool nextIsZero =
                    (i == (script.steps.Length - 1)) ||
                    (
                        Mathf.Approximately(script.steps[i + 1].dx, 0f) &&
                        Mathf.Approximately(script.steps[i + 1].dy, 0f) &&
                        string.IsNullOrEmpty(script.steps[i + 1].axis)
                    );
                if (nextIsZero) playerMove.ExternalAnim_StopIdle();
            }
        }

        // afterPlayerActions
        if (script.afterPlayerActions != null && script.afterPlayerActions.Length > 0)
        {
            foreach (var act in script.afterPlayerActions)
            {
                if (act == null) continue;
                string type = (act.type ?? "").Trim().ToLowerInvariant();

                if (type == "npcmove")
                {
                    GameObject targetNpc = ResolveNpc(act.npcName);
                    if (!targetNpc)
                    {
                        Debug.LogWarning($"[NpcEventDebugLoader] npcMove 대상 '{act.npcName}'을(를) 찾을 수 없습니다.");
                    }
                    else
                    {
                        _npcSpecByName.TryGetValue(act.npcName.Trim(), out var specForNpc);
                        float dur = (act.duration > 0f) ? act.duration : 1f;
                        yield return StartCoroutine(NpcMoveByWorld(targetNpc, new Vector2(act.dx, act.dy), dur, specForNpc));
                    }
                }
                else if (type == "log")
                {
                    Debug.Log(string.IsNullOrEmpty(act.message) ? "[NpcEventDebugLoader] (log)" : act.message);
                }
                else if (type == "dialogue")
                {
                    string dlgOwner = string.IsNullOrWhiteSpace(act.owner) ? ownerForIO : act.owner.Trim();
                    string dlgEvent = string.IsNullOrWhiteSpace(act.eventName) ? eventForIO : act.eventName.Trim();
                    yield return StartCoroutine(RunDialogueSequence(dlgOwner, dlgEvent));
                    break; // dialogue 끝나면 종료 루틴으로
                }
                else if (type == "dialoguepanelactive")
                {
                    HandleDialoguePanelActive(act.active);
                }
                else if (type == "npcsetactive")
                {
                    HandleNpcSetActive(act.npcName, act.active);
                    if (_dialoguePanelAutoReEnableArmed && act.active == false)
                    {
                        RestoreDialoguePanelAfterTempHide();
                    }
                }
                else if (type == "eventend")
                {
                    break;
                }
            }
        }

        // 종료 처리
        yield return StartCoroutine(FadeOutInAndReturn());
        ExitEventGuard();
        onComplete?.Invoke();
        _eventRunning = false;
        yield break;
    }

    // ── 대각선 금지 + 속도 고정으로 NPC 이동을 래핑 ──
    private IEnumerator NpcMoveAxisSplit(GameObject npc, float dx, float dy, float speed, NpcSpec specOrNull)
    {
        speed = Mathf.Max(1e-4f, speed);

        // X 축
        if (!Mathf.Approximately(dx, 0f))
        {
            float durX = Mathf.Abs(dx) / speed;
            yield return StartCoroutine(NpcMoveByWorld(npc, new Vector2(dx, 0f), durX, specOrNull));
        }
        // Y 축
        if (!Mathf.Approximately(dy, 0f))
        {
            float durY = Mathf.Abs(dy) / speed;
            yield return StartCoroutine(NpcMoveByWorld(npc, new Vector2(0f, dy), durY, specOrNull));
        }
    }

    // ───────────── Dialogue 실행 (+ 리액션/종료 이벤트 대기) ─────────────
    private IEnumerator RunDialogueSequence(string ownerForIO, string eventForIO)
    {
        if (autoFindDialogueManager && dialogueManager == null)
            dialogueManager = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);

        if (dialogueManager == null)
        {
            Debug.LogWarning("[NpcEventDebugLoader] DialogueManager(StringTables)를 찾지 못해 스킵합니다.");
            yield break;
        }

        if (dialoguePanel) dialoguePanel.SetActive(true);

        // 리액션 구독
        void OnKeyShownHandler(string key)
        {
            if (_currentDialogueReactions == null || _currentDialogueReactions.Length == 0) return;
            for (int i = 0; i < _currentDialogueReactions.Length; i++)
            {
                var r = _currentDialogueReactions[i];
                if (r == null || string.IsNullOrEmpty(r.onKey)) continue;
                if (!string.Equals(r.onKey, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (_reactionsFired.Contains(r.onKey)) continue;
                _reactionsFired.Add(r.onKey);
                StartCoroutine(ExecutePostActions(r.actions)); // 액션 실행
            }
        }

        bool ended = false;
        void OnEndedHandler() { ended = true; }

        dialogueManager.OnKeyShown += OnKeyShownHandler;
        dialogueManager.OnDialogueEnded += OnEndedHandler;

        // 시작
        dialogueManager.BeginWithEventName(eventForIO);

        // 대화 종료 이벤트까지 대기
        while (!ended && dialogueManager != null)
            yield return null;

        // 구독 해제
        if (dialogueManager != null)
        {
            dialogueManager.OnKeyShown -= OnKeyShownHandler;
            dialogueManager.OnDialogueEnded -= OnEndedHandler;
        }

        if (dialoguePanel) dialoguePanel.SetActive(false);
    }
    private float DeriveNpcDurationBySpeed(Vector2 delta, float requestedDuration)
    {
        // 1) JSON이 duration을 명시했다면 그대로 사용
        if (requestedDuration > 0f) return requestedDuration;

        // 2) 그렇지 않다면 ‘플레이어 moveSpeed’를 기본값으로 공유
        //    (원한다면 script 전용의 npcMoveSpeedDefault를 추가해도 좋습니다)
        float speed = 0f;
        if (_currentPlayerMoveSpeedForEvent > 1e-4f) speed = _currentPlayerMoveSpeedForEvent; // 아래 필드 참고

        if (speed > 1e-4f)
        {
            float dist = delta.magnitude;
            return (dist <= 1e-6f) ? 0f : (dist / speed);
        }

        // 3) 아무 정보도 없으면 1초 기본
        return 1f;
    }
    private IEnumerator ExecutePostActions(EventPostAction[] actions)
    {
        if (actions == null || actions.Length == 0) yield break;

        foreach (var act in actions)
        {
            if (act == null) continue;
            string type = (act.type ?? "").Trim().ToLowerInvariant();

            if (type == "dialoguepanelactive")
            {
                HandleDialoguePanelActive(act.active);
            }
            else if (type == "npcmove")
            {
                var go = ResolveNpc(act.npcName);
                if (go)
                {
                    _npcSpecByName.TryGetValue(act.npcName.Trim(), out var specForNpc);

                    // duration 명시 없으면 현재 이벤트의 moveSpeed로 자동 산출
                    float dur = DeriveNpcDurationBySpeed(new Vector2(act.dx, act.dy), act.duration);

                    // ★ 여기! 기존의 'targetNpc' 오타를 'go'로 교체
                    yield return StartCoroutine(NpcMoveByWorld(go, new Vector2(act.dx, act.dy), dur, specForNpc));
                }
                else
                {
                    Debug.LogWarning($"[NpcEventDebugLoader] dialogueReaction npcMove 대상 '{act.npcName}' 없음");
                }
            }
            else if (type == "npcsetactive")
            {
                HandleNpcSetActive(act.npcName, act.active);
                if (_dialoguePanelAutoReEnableArmed && act.active == false)
                    RestoreDialoguePanelAfterTempHide();
            }
            else if (type == "log")
            {
                Debug.Log(string.IsNullOrEmpty(act.message) ? "[NpcEventDebugLoader] (reaction log)" : act.message);
            }
            else if (type == "eventend")
            {
                break;
            }
        }

        // 마무리 훅: 임시 비활성 상태가 남아 있으면 무조건 복구
        if (_dialoguePanelAutoReEnableArmed || _dialoguePanelTemporarilyHidden)
            RestoreDialoguePanelAfterTempHide();
    }



    // 다이얼로그 패널 토글(+ 임시 비활 → 첫 npcSetActive:false 직후 자동 복구)
    private void HandleDialoguePanelActive(bool active)
    {
        if (autoFindDialogueManager && dialogueManager == null)
            dialogueManager = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);
        if (!dialoguePanel && dialogueManager) dialoguePanel = dialogueManager.gameObject;

        if (!dialoguePanel) return;

        if (!active)
        {
            _dialoguePanelTemporarilyHidden = true;
            _dialoguePanelAutoReEnableArmed = true;
            dialoguePanel.SetActive(false);
            if (verboseLog) Debug.Log("[NpcEventDebugLoader] Dialogue Panel 임시 비활성화");
        }
        else
        {
            _dialoguePanelTemporarilyHidden = false;
            _dialoguePanelAutoReEnableArmed = false;
            dialoguePanel.SetActive(true);
            if (verboseLog) Debug.Log("[NpcEventDebugLoader] Dialogue Panel 활성화");
        }
    }

    private void RestoreDialoguePanelAfterTempHide()
    {
        if (!_dialoguePanelTemporarilyHidden) return;
        if (dialoguePanel) dialoguePanel.SetActive(true);
        _dialoguePanelTemporarilyHidden = false;
        _dialoguePanelAutoReEnableArmed = false;
        if (verboseLog) Debug.Log("[NpcEventDebugLoader] Dialogue Panel 자동 복구");
    }

    // ───────────── 파일 로드/캐시/유틸 ─────────────
    private bool TryLoadSingle(string ownerForIO, string eventForIO, out LoadedEvent loaded)
    {
        loaded = null;
        string file = eventForIO + ".json";
        string p1 = Path.Combine(Application.streamingAssetsPath, eventFolderName, ownerForIO, file);
        string p2 = Path.Combine(Application.dataPath, eventFolderName, ownerForIO, file);

        string chosen = null;
        string json = null;
        try
        {
            if (File.Exists(p1)) { chosen = p1; json = File.ReadAllText(p1); }
            else if (File.Exists(p2)) { chosen = p2; json = File.ReadAllText(p2); }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NpcEventDebugLoader] JSON 예외: {ownerForIO}/{eventForIO}, err={e}");
            return false;
        }

        if (string.IsNullOrEmpty(chosen) || string.IsNullOrEmpty(json)) return false;

        loaded = new LoadedEvent { ownerName = ownerForIO, eventName = eventForIO, path = chosen, json = json };
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
        var cands = (saveMethodCandidates != null && saveMethodCandidates.Length > 0)
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
            catch (Exception e) { Debug.LogWarning($"[NpcEventDebugLoader] 저장 호출 실패: {e}"); }
        }
    }

    private static bool TryParseEventScript(string rawJson, out EventScript script, bool logOnError)
    {
        script = null;
        if (string.IsNullOrWhiteSpace(rawJson)) return false;

        string s = rawJson.Trim();

        // Array 최상위 호환
        if (s.StartsWith("[")) s = "{\"steps\":" + s + "}";

        // 주석 제거
        s = Regex.Replace(s, @"//.*?$", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);

        // 꼬리 콤마 제거
        s = Regex.Replace(s, @",\s*(\})", "$1");
        s = Regex.Replace(s, @",\s*(\])", "$1");

        s = s.Trim();

        try
        {
            script = JsonUtility.FromJson<EventScript>(s);
            if (script == null) return false;
            if (script.steps == null) script.steps = Array.Empty<EventStep>();
            if (script.defaultStepDuration < 0f) script.defaultStepDuration = 0.5f;
            if (script.npcSpawns == null) script.npcSpawns = Array.Empty<NpcSpawnCmd>();
            if (script.afterPlayerActions == null) script.afterPlayerActions = Array.Empty<EventPostAction>();
            if (script.dialogueReactions == null) script.dialogueReactions = Array.Empty<DialogueReaction>();
            return true;
        }
        catch (Exception e)
        {
            if (logOnError)
                Debug.LogError($"[NpcEventDebugLoader] Sanitized JSON parse 실패:\n---SANITIZED---\n{s}\n---ERR---\n{e}");
            return false;
        }
    }

    // ───────────── 이름/키 유틸 ─────────────
    private static string Key(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToLowerInvariant();

    private OwnerEvents FindOwnerCI(string ownerInput, out string ownerForIO)
    {
        ownerForIO = "";
        if (owners == null || string.IsNullOrWhiteSpace(ownerInput)) return null;

        string k = Key(ownerInput);
        foreach (var oe in owners)
        {
            if (oe == null) continue;
            if (Key(oe.ownerName) == k)
            {
                ownerForIO = oe.ownerName; // I/O용: 원본 대소문자 보존
                return oe;
            }
        }
        return null;
    }

    private bool TryBindBool(object obj, string boolName, out Func<bool> getter, out Action<bool> setter)
    {
        getter = null; setter = null;
        var t = obj.GetType();

        // case-insensitive
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

    // ───────────── NPC 인덱싱/스폰/정리 ─────────────
    private void IndexNpcCatalog()
    {
        _npcSpecsByKey.Clear();
        _npcSpecByName.Clear();

        if (npcCatalog == null) return;

        foreach (var spec in npcCatalog)
        {
            if (spec == null || spec.prefab == null) continue;

            var key = Key(spec.ownerName);
            if (!string.IsNullOrEmpty(key))
            {
                if (!_npcSpecsByKey.TryGetValue(key, out var list))
                {
                    list = new List<NpcSpec>();
                    _npcSpecsByKey[key] = list;
                }
                list.Add(spec);
            }

            if (!string.IsNullOrWhiteSpace(spec.npcName))
                _npcSpecByName[spec.npcName.Trim()] = spec;
        }

        if (verboseLog)
            Debug.Log($"[NpcEventDebugLoader] NPC 카탈로그 인덱싱 완료: owners={_npcSpecsByKey.Count}, names={_npcSpecByName.Count}");
    }

    private void SpawnNpcsForOwnerOnEnter(string ownerForIO)
    {
        if (deactivateOtherOwnersNpcsOnEnter)
            DeactivateOrDestroyNpcsExcept(Key(ownerForIO));

        if (!_npcSpecsByKey.TryGetValue(Key(ownerForIO), out var list) || list.Count == 0) return;

        foreach (var spec in list)
        {
            if (!spec.spawnOnHouseEnter) continue;
            SpawnOrReuseNpc(Key(ownerForIO), spec);
        }
    }

    private GameObject SpawnOrReuseNpc(string ownerKey, NpcSpec spec)
    {
        if (spec.prefab == null) return null;

        if (!_spawnedNpcByKey.TryGetValue(ownerKey, out var byName))
        {
            byName = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            _spawnedNpcByKey[ownerKey] = byName;
        }

        if (spec.reuseIfAlreadySpawned && byName.TryGetValue(spec.npcName, out var existed) && existed)
        {
            if (!existed.activeSelf) existed.SetActive(true);
            return existed;
        }

        Vector3 basePos = playerTransform ? playerTransform.position : Vector3.zero;
        Vector3 spawnPos = basePos + spec.spawnOffset;

        var go = Instantiate(spec.prefab, spawnPos, Quaternion.identity, spec.parent ? spec.parent : null);
        go.name = string.IsNullOrEmpty(spec.npcName) ? spec.prefab.name : spec.npcName;

        var anim = go.GetComponent<Animator>();
        if (!anim && spec.addAnimatorIfMissing) anim = go.AddComponent<Animator>();
        if (anim && spec.animatorController) anim.runtimeAnimatorController = spec.animatorController;

        byName[spec.npcName] = go;

        if (verboseLog)
            Debug.Log($"[NpcEventDebugLoader] NPC 스폰: ownerKey='{ownerKey}', npc='{spec.npcName}', pos={spawnPos}");

        return go;
    }

    private void DeactivateOrDestroyNpcsExcept(string keepOwnerKey)
    {
        _ownersDeactivatedOnEnter.Clear();

        foreach (var kv in _spawnedNpcByKey)
        {
            var ownerKey = kv.Key;
            if (ownerKey == keepOwnerKey) continue;

            var byName = kv.Value;
            if (byName == null) continue;

            var snap = new OwnerDeactivateSnapshot { key = ownerKey, deactivated = new List<GameObject>() };

            foreach (var pair in byName)
            {
                var go = pair.Value;
                if (!go) continue;

                var spec = FindNpcSpecByKey(ownerKey, pair.Key);
                if (spec != null && spec.destroyOnExitHouse)
                {
                    Destroy(go);
                    byName[pair.Key] = null;
                }
                else
                {
                    if (go.activeSelf)
                    {
                        go.SetActive(false);
                        snap.deactivated.Add(go);
                    }
                }
            }

            if (snap.deactivated.Count > 0)
                _ownersDeactivatedOnEnter.Add(snap);
        }
    }

    private NpcSpec FindNpcSpecByKey(string ownerKey, string npcName)
    {
        if (_npcSpecsByKey.TryGetValue(ownerKey, out var list))
        {
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s != null && string.Equals(s.npcName, npcName, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
        }
        return null;
    }

    private void HandleNpcSpawnCommands(EventScript script)
    {
        if (script.npcSpawns == null || script.npcSpawns.Length == 0) return;

        foreach (var cmd in script.npcSpawns)
        {
            if (cmd == null || string.IsNullOrWhiteSpace(cmd.npcName)) continue;
            string npc = cmd.npcName.Trim();

            if (cmd.deactivateExisting)
                DeactivateMapNpcs(FindSceneNpcsByName(npc));

            Vector3 p = cmd.relativeToPlayer && playerTransform
                        ? playerTransform.position + new Vector3(cmd.x, cmd.y, cmd.z)
                        : new Vector3(cmd.x, cmd.y, cmd.z);

            var go = SpawnNpcByNameAnyOwner(npc, p);
            if (go == null)
            {
                Debug.LogWarning($"[NpcEventDebugLoader] '{npc}' 스폰 실패(오너 무시). 카탈로그에 npcName이 없거나 prefab이 비었습니다.");
            }
            else
            {
                _spawnedDuringEvent.Add(go);
                _spawnedNpcByName[npc] = go;
            }
        }
    }

    private List<GameObject> FindSceneNpcsByName(string npcName)
    {
        var list = new List<GameObject>();

        var identities = FindObjectsOfType<NpcIdentity>(includeInactive: true);
        foreach (var id in identities)
            if (id && string.Equals(id.npcName, npcName, StringComparison.OrdinalIgnoreCase))
                list.Add(id.gameObject);

        var all = FindObjectsOfType<Transform>(includeInactive: true);
        foreach (var t in all)
        {
            if (!t) continue;
            var go = t.gameObject;
            if (string.Equals(go.name, npcName, StringComparison.OrdinalIgnoreCase) && !list.Contains(go))
                list.Add(go);
        }
        return list;
    }

    private void DeactivateMapNpcs(IEnumerable<GameObject> gos)
    {
        foreach (var go in gos)
        {
            if (!go) continue;
            if (go.activeSelf)
            {
                go.SetActive(false);
                _tempDeactivatedMapNpcs.Add(go);
            }
        }
    }

    private GameObject SpawnNpcByNameAnyOwner(string npcName, Vector3 worldPos)
    {
        if (string.IsNullOrWhiteSpace(npcName)) return null;

        if (!_npcSpecByName.TryGetValue(npcName.Trim(), out var spec) || spec == null || spec.prefab == null)
        {
            Debug.LogWarning($"[NpcEventDebugLoader] 전역 카탈로그에서 '{npcName}' 프리팹을 찾지 못했습니다.");
            return null;
        }

        var parent = spec.parent ? spec.parent : null;
        var go = Instantiate(spec.prefab, worldPos, Quaternion.identity, parent);
        go.name = string.IsNullOrEmpty(spec.npcName) ? spec.prefab.name : spec.npcName;

        var anim = go.GetComponent<Animator>();
        if (!anim && spec.addAnimatorIfMissing) anim = go.AddComponent<Animator>();
        if (anim && spec.animatorController) anim.runtimeAnimatorController = spec.animatorController;

        var ownerKey = Key(spec.ownerName);
        if (!_spawnedNpcByKey.TryGetValue(ownerKey, out var byName))
        {
            byName = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            _spawnedNpcByKey[ownerKey] = byName;
        }
        byName[spec.npcName] = go;
        _spawnedNpcByName[spec.npcName] = go;

        if (verboseLog)
            Debug.Log($"[NpcEventDebugLoader] NPC 스폰(오너무시): npc='{npcName}', ownerInCatalog='{spec.ownerName}', pos={worldPos}");
        return go;
    }

    private static void PlayNpcWalkByVector(Animator anim, Vector2 dir, NpcSpec specOrNull)
    {
        if (!anim) return;
        if (dir.sqrMagnitude < 1e-6f) return;

        bool useOverride = (specOrNull != null && specOrNull.overrideWalkStates);
        string sLeft = useOverride ? specOrNull.stateLeft : "Left_Walk";
        string sRight = useOverride ? specOrNull.stateRight : "Right_Walk";
        string sFront = useOverride ? specOrNull.stateFront : "Front_Walk";
        string sBack = useOverride ? specOrNull.stateBack : "Back_Walk";

        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
        {
            if (dir.x < 0f) anim.Play(sLeft);
            else anim.Play(sRight);
        }
        else
        {
            if (dir.y > 0f) anim.Play(sBack);
            else anim.Play(sFront);
        }
        anim.speed = Mathf.Max(0.01f, anim.speed);
    }

    private static void StopNpcIdle(Animator anim)
    {
        if (!anim) return;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        anim.Play(st.shortNameHash, 0, 0f);
        anim.speed = 0f;
    }

    // ───────────── NPC 이동 (단일 정의) ─────────────
    private IEnumerator NpcMoveByWorld(GameObject npc, Vector2 delta, float duration, NpcSpec specOrNull)
    {
        if (!npc) yield break;

        var tr = npc.transform;
        if (!tr) yield break; // 파괴된 경우 방지

        var rb = npc.GetComponent<Rigidbody2D>();
        var anim = npc.GetComponent<Animator>();

        Vector3 start = tr.position;
        Vector3 target = start + new Vector3(delta.x, delta.y, 0f);

        Vector2 dir = delta;
        float dist = dir.magnitude;
        float baseAnimSpeed = (duration > 1e-4f) ? Mathf.Clamp01(dist / duration) : 1f;
        float speedScale = (specOrNull != null) ? Mathf.Max(0f, specOrNull.animSpeedScale) : 1f;
        float finalAnimSpd = Mathf.Lerp(0.7f, 1.1f, baseAnimSpeed) * speedScale;

        if (anim)
        {
            anim.speed = finalAnimSpd;
            if (dir.sqrMagnitude > 1e-6f) PlayNpcWalkByVector(anim, dir.normalized, specOrNull);
        }

        if (duration <= 0f)
        {
            if (!npc || !tr) yield break;
            if (rb)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.MovePosition(new Vector2(target.x, target.y));
            }
            else
            {
                tr.position = target;
            }
            if (anim) StopNpcIdle(anim);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            if (!npc || !tr) yield break; // 매 프레임 안전 체크

            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            Vector3 pos = Vector3.Lerp(start, target, u);

            if (rb)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.MovePosition(new Vector2(pos.x, pos.y));
            }
            else
            {
                tr.position = pos;
            }

            yield return null;
        }

        if (!npc || !tr) yield break;

        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.MovePosition(new Vector2(target.x, target.y));
        }
        else
        {
            tr.position = target;
        }

        if (anim) StopNpcIdle(anim);
    }

    private void HandleNpcSetActive(string npcName, bool active)
    {
        if (string.IsNullOrEmpty(npcName)) return;

        var go = ResolveNpc(npcName);
        if (!go)
        {
            Debug.LogWarning($"[NpcEventDebugLoader] npcSetActive: '{npcName}' 대상 없음");
            return;
        }
        go.SetActive(active);
    }

    private GameObject ResolveNpc(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName)) return null;

        if (!_spawnedNpcByName.TryGetValue(npcName.Trim(), out var go) || !go)
        {
            var found = FindSceneNpcsByName(npcName.Trim());
            if (found.Count > 0) go = found[0];
        }
        return go;
    }

    // ───────────── 가드/복구 ─────────────
    private void EnterEventGuard()
    {
        // UI 잠금
        _uiBackup.Clear();
        foreach (var go in uiCanvasesToDisable)
        {
            if (!go) continue;
            _uiBackup.Add(new UIBak { go = go, wasActive = go.activeSelf });
            go.SetActive(false);
        }

        _inputBackup.Clear();
        foreach (var comp in inputComponents)
        {
            if (!comp) continue;
            _inputBackup.Add(new InputBackup { comp = comp, wasEnabled = comp.enabled });
            comp.enabled = false;
        }

        if (playerMove != null) playerMove.Freeze();

        _savedPlayerPosition = playerTransform.position;

        _animBak = new AnimSpriteBackup();
        if (_anim)
        {
            var st = _anim.GetCurrentAnimatorStateInfo(0);
            _animBak.hasAnimator = true;
            _animBak.stateHash = st.shortNameHash;
            _animBak.normalizedTime = st.normalizedTime;
            _animBak.speed = _anim.speed;
        }
        if (_sr)
        {
            _animBak.hasSpriteRenderer = true;
            _animBak.sprite = _sr.sprite;
            _animBak.flipX = _sr.flipX;
            _animBak.flipY = _sr.flipY;
        }

        _rbBackup = new PhysicsBackup { hasRb = _playerRb2D != null };
        if (_playerRb2D)
        {
            _rbBackup.simulated = _playerRb2D.simulated;
            _rbBackup.bodyType = _playerRb2D.bodyType;
            _rbBackup.constraints = _playerRb2D.constraints;
            _rbBackup.linearVelocity = _playerRb2D.linearVelocity;
            _rbBackup.angularVelocity = _playerRb2D.angularVelocity;

            _playerRb2D.linearVelocity = Vector2.zero;
            _playerRb2D.angularVelocity = 0f;

            if (freezePhysicsDuringEvent)
                _playerRb2D.simulated = false;
            else
            {
                _playerRb2D.bodyType = RigidbodyType2D.Kinematic;
                _playerRb2D.constraints = RigidbodyConstraints2D.FreezePosition | RigidbodyConstraints2D.FreezeRotation;
            }
        }
    }

    private void ExitEventGuard()
    {
        if (_rbBackup.hasRb && _playerRb2D)
        {
            _playerRb2D.simulated = _rbBackup.simulated;
            _playerRb2D.bodyType = _rbBackup.bodyType;
            _playerRb2D.constraints = _rbBackup.constraints;
            _playerRb2D.linearVelocity = _rbBackup.linearVelocity;
            _playerRb2D.angularVelocity = _rbBackup.angularVelocity;
        }

        if (playerMove != null) playerMove.ExternalAnim_StopIdle();

        if (_animBak.hasAnimator && _anim)
        {
            _anim.Play(_animBak.stateHash, 0, Mathf.Repeat(_animBak.normalizedTime, 1f));
            _anim.speed = _animBak.speed;
        }
        if (_animBak.hasSpriteRenderer && _sr && !_anim)
        {
            _sr.sprite = _animBak.sprite;
            _sr.flipX = _animBak.flipX;
            _sr.flipY = _animBak.flipY;
        }

        foreach (var bak in _inputBackup) if (bak.comp) bak.comp.enabled = bak.wasEnabled;
        foreach (var bak in _uiBackup) if (bak.go) bak.go.SetActive(bak.wasActive);

        if (playerMove != null) playerMove.Unfreeze();

        for (int i = 0; i < _tempDeactivatedMapNpcs.Count; i++)
        {
            var go = _tempDeactivatedMapNpcs[i];
            if (go) go.SetActive(true);
        }
        _tempDeactivatedMapNpcs.Clear();

        DestroyEventSpawnedNpcs();
        ReactivateOwnersDeactivatedOnEnter();
    }

    private IEnumerator FadeOutInAndReturn()
    {
        yield return FadeTo(1f, fadeOutDuration);
        SnapPlayerWorld(_savedPlayerPosition, useRbIntent: false);
        yield return FadeTo(0f, fadeInDuration);
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (!fadeCanvasGroup) yield break;

        if (duration <= 0f)
        {
            fadeCanvasGroup.alpha = targetAlpha;
            fadeCanvasGroup.blocksRaycasts = targetAlpha > 0.99f;
            yield break;
        }

        float start = fadeCanvasGroup.alpha;
        float t = 0f;
        fadeCanvasGroup.blocksRaycasts = targetAlpha > 0.99f;

        while (t < duration)
        {
            t += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t / duration));
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
        fadeCanvasGroup.blocksRaycasts = targetAlpha > 0.99f;
    }

    // ───────────── 이동 ─────────────
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

    private IEnumerator MoveByWorld(Vector2 delta, float duration, bool useRbIntent)
    {
        Vector3 start = playerTransform.position;
        Vector3 target = start + new Vector3(delta.x, delta.y, 0f);

        if (duration <= 0f)
        {
            SnapPlayerWorld(target, useRbIntent);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Vector3 pos = Vector3.Lerp(start, target, Mathf.Clamp01(t / duration));
            SnapPlayerWorld(pos, useRbIntent);
            yield return null;
        }
        SnapPlayerWorld(target, useRbIntent);
    }

    private IEnumerator MoveByLocal(Vector2 delta, float duration, bool useRbIntent)
    {
        Vector3 start = playerTransform.localPosition;
        Vector3 target = start + new Vector3(delta.x, delta.y, 0f);

        if (duration <= 0f)
        {
            SnapPlayerLocal(target, useRbIntent);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Vector3 lp = Vector3.Lerp(start, target, Mathf.Clamp01(t / duration));
            SnapPlayerLocal(lp, useRbIntent);
            yield return null;
        }
        SnapPlayerLocal(target, useRbIntent);
    }

    private void SnapPlayerWorld(Vector3 worldTarget, bool useRbIntent)
    {
        if (freezePhysicsDuringEvent && _playerRb2D && !_playerRb2D.simulated)
        {
            playerTransform.position = worldTarget;
            return;
        }

        if (preferRigidbodyMove && useRbIntent && _playerRb2D)
        {
            _playerRb2D.linearVelocity = Vector2.zero;
            _playerRb2D.angularVelocity = 0f;
            _playerRb2D.MovePosition(new Vector2(worldTarget.x, worldTarget.y));
        }
        else
        {
            playerTransform.position = worldTarget;
        }
    }

    private void SnapPlayerLocal(Vector3 localTarget, bool useRbIntent)
    {
        if (freezePhysicsDuringEvent && _playerRb2D && !_playerRb2D.simulated)
        {
            playerTransform.localPosition = localTarget;
            return;
        }

        if (preferRigidbodyMove && useRbIntent && _playerRb2D)
        {
            Vector3 world = playerTransform.parent ? playerTransform.parent.TransformPoint(localTarget) : localTarget;
            _playerRb2D.linearVelocity = Vector2.zero;
            _playerRb2D.angularVelocity = 0f;
            _playerRb2D.MovePosition(new Vector2(world.x, world.y));
        }
        else
        {
            playerTransform.localPosition = localTarget;
        }
    }

    // ───────────── 정리 ─────────────
    private void DestroyEventSpawnedNpcs()
    {
        if (_spawnedDuringEvent.Count == 0) return;

        foreach (var go in _spawnedDuringEvent)
        {
            if (!go) continue;

            foreach (var kv in _spawnedNpcByKey)
            {
                var dict = kv.Value;
                if (dict == null) continue;
                foreach (var k in new List<string>(dict.Keys))
                {
                    if (dict[k] == go) dict[k] = null;
                }
            }
            foreach (var k in new List<string>(_spawnedNpcByName.Keys))
            {
                if (_spawnedNpcByName[k] == go) _spawnedNpcByName.Remove(k);
            }

            Destroy(go);
        }
        _spawnedDuringEvent.Clear();
    }

    private void ReactivateOwnersDeactivatedOnEnter()
    {
        if (_ownersDeactivatedOnEnter.Count == 0) return;

        foreach (var snap in _ownersDeactivatedOnEnter)
        {
            if (snap == null || snap.deactivated == null) continue;
            foreach (var go in snap.deactivated)
            {
                if (go) go.SetActive(true);
            }
        }
        _ownersDeactivatedOnEnter.Clear();
    }

    // ───────────── 보조 ─────────────
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
}

// 맵 NPC 식별용 보조 컴포넌트
public class NpcIdentity : MonoBehaviour
{
    public string npcName;
}
