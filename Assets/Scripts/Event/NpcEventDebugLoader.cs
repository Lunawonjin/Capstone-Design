using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class NpcEventDebugLoader : MonoBehaviour
{
    // 외부로부터 플레이어 입력 ON/OFF를 제어할 수 있게 하는 인터페이스(선택)
    public interface IPlayerControlToggle { void SetControlEnabled(bool enabled); }

    #region 데이터 스키마
    [Serializable] public class OwnerEvents { public string ownerName = "Sol"; public string[] eventNames = Array.Empty<string>(); }
    [Serializable] public class LoadedEvent { public string ownerName; public string eventName; public string path; public string json; }
    [Serializable] public class PlayerSection { public bool forceUsePlayerMoveSpeed = false; public float moveSpeed = 1.0f; }
    [Serializable] public class EventStep { public string axis = ""; public float delta = 0f; public float dx = 0f; public float dy = 0f; public float duration = -1f; }
    [Serializable] public class NpcSpawnCmd { public string npcName = ""; public float x = 0f, y = 0f, z = 0f; public bool deactivateExisting = true; public bool relativeToPlayer = false; }

    [Serializable]
    public class EventPostAction
    {
        public string type = "";
        public string npcName = "";
        public float dx = 0f, dy = 0f;
        public float duration = 1f;
        public string message = "";
        public string owner = "";
        public string eventName = "";
        public bool active = true;

        public string dataName = "";
        public float delta = 0f;
        public bool clamp = true;
        public int min = 0;
        public int max = 100;
    }

    [Serializable] public class DialogueReaction { public string onKey = ""; public EventPostAction[] actions = Array.Empty<EventPostAction>(); }
    [Serializable] public class EventScript { public EventStep[] steps = Array.Empty<EventStep>(); public float defaultStepDuration = 0.5f; public bool useWorldSpace = true; public bool useRigidbodyMove = true; public NpcSpawnCmd[] npcSpawns = Array.Empty<NpcSpawnCmd>(); public PlayerSection player = null; public EventPostAction[] afterPlayerActions = Array.Empty<EventPostAction>(); public DialogueReaction[] dialogueReactions = Array.Empty<DialogueReaction>(); }
    #endregion

    #region 인스펙터 필드
    [Header("텔레포터 연동")]
    [SerializeField] private HouseDoorTeleporter teleporter;
    [Header("이벤트 폴더 (Assets/Resources/ 하위 경로)")]
    [SerializeField] private string eventFolderName = "Event";
    [Header("오너/이벤트 구성")]
    [SerializeField] private OwnerEvents[] owners = Array.Empty<OwnerEvents>();
    [Header("자동 실행 트리거")]
    [SerializeField] private bool autoRunOnHouseEnter = true;
    [Header("저장 호출 옵션")]
    [SerializeField] private bool saveAfterWrite = true;
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
    [SerializeField] private Image fadeImage;
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
    #endregion

    #region 런타임 상태
    private float _currentPlayerMoveSpeedForEvent = 0f;
    private readonly Dictionary<string, Dictionary<string, LoadedEvent>> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private int _lastTeleporterOwnerIndex = int.MinValue;
    private bool _eventRunning;
    private Vector3 _savedPlayerPosition;
    private readonly BindingFlags _bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private struct PhysicsBackup { public bool hasRb; public bool simulated; public RigidbodyType2D bodyType; public RigidbodyConstraints2D constraints; public Vector2 linearVelocity; public float angularVelocity; }
    private PhysicsBackup _rbBackup;
    private struct InputBackup { public MonoBehaviour comp; public bool wasEnabled; }
    private readonly List<InputBackup> _inputBackup = new();
    private struct UIBak { public GameObject go; public bool wasActive; }
    private readonly List<UIBak> _uiBackup = new();
    private struct AnimSpriteBackup { public bool hasAnimator; public int stateHash; public float normalizedTime; public float speed; public bool hasSpriteRenderer; public Sprite sprite; public bool flipX; public bool flipY; }
    private AnimSpriteBackup _animBak;
    [Serializable] public class NpcSpec { [Header("기본")] public string ownerName = "Sol"; public string npcName = "Sol_Npc"; public GameObject prefab; public bool spawnOnHouseEnter = true; public Vector3 spawnOffset = Vector3.zero; public Transform parent; public bool reuseIfAlreadySpawned = true; public bool deactivateOnExitHouse = true; public bool destroyOnExitHouse = false; [Header("애니메이터(선택)")] public RuntimeAnimatorController animatorController; public bool addAnimatorIfMissing = true; [Header("애니메이션 설정(선택)")] public bool overrideWalkStates = false; public string stateFront = "Front_Walk"; public string stateBack = "Back_Walk"; public string stateLeft = "Left_Walk"; public string stateRight = "Right_Walk"; public float animSpeedScale = 1f; }
    private readonly Dictionary<string, List<NpcSpec>> _npcSpecsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, GameObject>> _spawnedNpcByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NpcSpec> _npcSpecByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameObject> _spawnedNpcByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameObject> _tempDeactivatedMapNpcs = new();
    private class OwnerDeactivateSnapshot { public string key; public List<GameObject> deactivated = new(); }
    private readonly List<OwnerDeactivateSnapshot> _ownersDeactivatedOnEnter = new();
    private readonly List<GameObject> _spawnedDuringEvent = new();
    private bool _dialoguePanelTemporarilyHidden = false;
    private bool _dialoguePanelAutoReEnableArmed = false;
    private DialogueReaction[] _currentDialogueReactions = Array.Empty<DialogueReaction>();
    private readonly HashSet<string> _reactionsFired = new(StringComparer.OrdinalIgnoreCase);
    private string _ctxOwner = "";
    private string _ctxEvent = "";
    #endregion

    private void Reset() { if (!playerTransform) playerTransform = transform; }

    private void Awake()
    {
        if (!playerTransform) playerTransform = transform;
        _playerRb2D = playerTransform.GetComponent<Rigidbody2D>();
        _anim = playerTransform.GetComponent<Animator>();
        _sr = playerTransform.GetComponent<SpriteRenderer>();
        if (playerMove == null) playerMove = playerTransform.GetComponent<PlayerMove>();
        IndexNpcCatalog();
    }

    private void Start()
    {
        if (teleporter != null) _lastTeleporterOwnerIndex = teleporter.CurrentOwnerIndex;
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

    private void RunBundleIfNeeded(OwnerEvents cfg, string ownerForIO)
    {
        if (_eventRunning) return;
        if (cfg == null || cfg.eventNames == null || cfg.eventNames.Length == 0)
        {
            if (verboseLog) Debug.LogWarning($"[NpcEventDebugLoader] 설정 없음: owner='{ownerForIO}'");
            return;
        }
        var pd = ResolvePlayerData();
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
                if (verboseLog) Debug.LogWarning($"[NpcEventDebugLoader] PlayerData에 '{eventForIO}'(bool)을 찾지 못했습니다.");
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
                    // if (saveAfterWrite) InvokeSave();
                }));
                break;
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
        _ctxOwner = ownerForIO;
        _ctxEvent = eventForIO;
        _spawnedDuringEvent.Clear();
        EnterEventGuard();

        if (!TryParseEventScript(rawJson, out EventScript script, logSanitizedJsonOnError))
        {
            Debug.LogWarning($"[NpcEventDebugLoader] JSON 무효. 즉시 종료: {ownerForIO}/{eventForIO}");
            yield return StartCoroutine(FadeOutInAndReturn());
            ExitEventGuard();
            onComplete?.Invoke();
            _eventRunning = false;
            _ctxOwner = "";
            _ctxEvent = "";
            yield break;
        }

        if (script.player != null && script.player.forceUsePlayerMoveSpeed && script.player.moveSpeed > 1e-4f)
            _currentPlayerMoveSpeedForEvent = script.player.moveSpeed;

        _currentDialogueReactions = script.dialogueReactions ?? Array.Empty<DialogueReaction>();
        _reactionsFired.Clear();
        HandleNpcSpawnCommands(script);

        for (int i = 0; i < (script.steps?.Length ?? 0); i++)
        {
            var step = script.steps[i];
            var (sx, sy) = NormalizeStep(step);
            float speed = 1f;
            bool useFixedSpeed = script.player != null && script.player.forceUsePlayerMoveSpeed;
            if (useFixedSpeed) speed = Mathf.Max(1e-4f, script.player.moveSpeed);
            Vector2 dirAnim = new Vector2(Mathf.Sign(sx), Mathf.Sign(sy));
            if (Mathf.Abs(sx) >= Mathf.Abs(sy)) dirAnim = new Vector2(Mathf.Sign(sx), 0f);
            else dirAnim = new Vector2(0f, Mathf.Sign(sy));
            if (playerMove != null) playerMove.ExternalAnim_PlayWalk(dirAnim, 1.0f);
            if (!Mathf.Approximately(sx, 0f) && !Mathf.Approximately(sy, 0f))
            {
                float durX = useFixedSpeed ? Mathf.Abs(sx) / speed : (step.duration > 0f ? step.duration * Mathf.Abs(sx) / (Mathf.Abs(sx) + Mathf.Abs(sy)) : Mathf.Max(0f, script.defaultStepDuration));
                float durY = useFixedSpeed ? Mathf.Abs(sy) / speed : (step.duration > 0f ? step.duration * Mathf.Abs(sy) / (Mathf.Abs(sx) + Mathf.Abs(sy)) : Mathf.Max(0f, script.defaultStepDuration));
                if (Mathf.Abs(sx) > 0f) yield return (script.useWorldSpace) ? MoveByWorld(new Vector2(sx, 0f), durX, script.useRigidbodyMove) : MoveByLocal(new Vector2(sx, 0f), durX, script.useRigidbodyMove);
                if (Mathf.Abs(sy) > 0f) yield return (script.useWorldSpace) ? MoveByWorld(new Vector2(0f, sy), durY, script.useRigidbodyMove) : MoveByLocal(new Vector2(0f, sy), durY, script.useRigidbodyMove);
            }
            else
            {
                float dist = Mathf.Abs(sx) + Mathf.Abs(sy);
                float dur = useFixedSpeed ? (dist > 1e-6f ? dist / speed : (step.duration > 0f ? step.duration : script.defaultStepDuration)) : (step.duration > 0f ? step.duration : Mathf.Max(0f, script.defaultStepDuration));
                if (Mathf.Approximately(dist, 0f))
                {
                    if (dur > 0f) yield return new WaitForSeconds(dur);
                }
                else
                {
                    Vector2 delta = new Vector2(sx, sy);
                    yield return (script.useWorldSpace) ? MoveByWorld(delta, dur, script.useRigidbodyMove) : MoveByLocal(delta, dur, script.useRigidbodyMove);
                }
            }
            if (playerMove != null)
            {
                bool nextIsZero = (i == (script.steps.Length - 1)) || (Mathf.Approximately(script.steps[i + 1].dx, 0f) && Mathf.Approximately(script.steps[i + 1].dy, 0f) && string.IsNullOrEmpty(script.steps[i + 1].axis));
                if (nextIsZero) playerMove.ExternalAnim_StopIdle();
            }
        }
        if (script.afterPlayerActions != null && script.afterPlayerActions.Length > 0)
        {
            foreach (var act in script.afterPlayerActions)
            {
                if (act == null) continue;
                var actionType = (act.type ?? "").Trim().ToLowerInvariant();
                if (actionType == "npcmove")
                {
                    GameObject targetNpc = ResolveNpc(act.npcName);
                    if (!targetNpc) { Debug.LogWarning($"[NpcEventDebugLoader] npcMove 대상 '{act.npcName}'을(를) 찾을 수 없습니다."); }
                    else
                    {
                        _npcSpecByName.TryGetValue(act.npcName.Trim(), out var specForNpc);
                        float dur = (act.duration > 0f) ? act.duration : 1f;
                        yield return StartCoroutine(NpcMoveByWorld(targetNpc, new Vector2(act.dx, act.dy), dur, specForNpc));
                    }
                }
                else if (actionType == "log") { Debug.Log(string.IsNullOrEmpty(act.message) ? "[NpcEventDebugLoader] (log)" : act.message); }
                else if (actionType == "delay" || actionType == "wait")
                {
                    if (act.duration > 0) yield return new WaitForSeconds(act.duration);
                }
                else if (actionType == "dialogue")
                {
                    string dlgOwner = string.IsNullOrWhiteSpace(act.owner) ? ownerForIO : act.owner.Trim();
                    string dlgEvent = string.IsNullOrWhiteSpace(act.eventName) ? eventForIO : act.eventName.Trim();
                    yield return StartCoroutine(RunDialogueSequence(dlgOwner, dlgEvent));
                    break;
                }
                else if (actionType == "dialoguepanelactive") { HandleDialoguePanelActive(act.active); }
                else if (actionType == "dialoguepanelrestorenow") { ForceActivateDialoguePanelNow(); }
                else if (actionType == "npcsetactive")
                {
                    HandleNpcSetActive(act.npcName, act.active);
                    // Automatic restore removed for explicit control.
                }
                else if (actionType == "affinityup")
                {
                    string dn = ResolveAffinityDataName(act.dataName);
                    if (!AffinityUp(dn, (int)act.delta, act.clamp, act.min, act.max)) Debug.LogWarning($"[NpcEventDebugLoader] affinityUp 실패 — dataName='{act.dataName}' (resolved='{dn}')");
                }
                else if (actionType == "affinitydown")
                {
                    string dn = ResolveAffinityDataName(act.dataName);
                    if (!AffinityDown(dn, (int)act.delta, act.clamp, act.min, act.max)) Debug.LogWarning($"[NpcEventDebugLoader] affinityDown 실패 — dataName='{act.dataName}' (resolved='{dn}')");
                }
                else if (actionType == "datadelta")
                {
                    if (!ApplyDataDeltaInt(act.dataName, (int)act.delta)) Debug.LogWarning($"[NpcEventDebugLoader] dataDelta 실패 — dataName='{act.dataName}'");
                }
                else if (actionType == "eventend") { break; }
            }
        }
        yield return StartCoroutine(FadeOutInAndReturn());
        ExitEventGuard();
        onComplete?.Invoke();
        _eventRunning = false;
        _ctxOwner = "";
        _ctxEvent = "";
    }

    private IEnumerator RunDialogueSequence(string ownerForIO, string eventForIO)
    {
        if (autoFindDialogueManager && dialogueManager == null) dialogueManager = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);
        if (dialogueManager == null)
        {
            Debug.LogWarning("[NpcEventDebugLoader] DialogueManager(StringTables)를 찾지 못해 스킵합니다.");
            yield break;
        }
        if (dialoguePanel) dialoguePanel.SetActive(true);
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
                StartCoroutine(ExecutePostActions(r.actions));
            }
        }
        bool ended = false;
        void OnEndedHandler() { ended = true; }
        dialogueManager.OnKeyShown += OnKeyShownHandler;
        dialogueManager.OnDialogueEnded += OnEndedHandler;
        dialogueManager.BeginWithEventName(eventForIO);
        while (!ended && dialogueManager != null) yield return null;
        if (dialogueManager != null)
        {
            dialogueManager.OnKeyShown -= OnKeyShownHandler;
            dialogueManager.OnDialogueEnded -= OnEndedHandler;
        }
        if (dialoguePanel) dialoguePanel.SetActive(false);
    }

    private IEnumerator ExecutePostActions(EventPostAction[] actions)
    {
        if (actions == null || actions.Length == 0) yield break;
        foreach (var act in actions)
        {
            if (act == null) continue;
            var actionType = (act.type ?? "").Trim().ToLowerInvariant();
            if (actionType == "dialoguepanelactive") { HandleDialoguePanelActive(act.active); }
            else if (actionType == "dialoguepanelrestorenow") { ForceActivateDialoguePanelNow(); }
            else if (actionType == "delay" || actionType == "wait")
            {
                if (act.duration > 0) yield return new WaitForSeconds(act.duration);
            }
            else if (actionType == "npcmove")
            {
                var go = ResolveNpc(act.npcName);
                if (go)
                {
                    _npcSpecByName.TryGetValue(act.npcName.Trim(), out var specForNpc);
                    float dur = DeriveNpcDurationBySpeed(new Vector2(act.dx, act.dy), act.duration);
                    yield return StartCoroutine(NpcMoveByWorld(go, new Vector2(act.dx, act.dy), dur, specForNpc));
                }
                else Debug.LogWarning($"[NpcEventDebugLoader] dialogueReaction npcMove 대상 '{act.npcName}' 없음");
            }
            else if (actionType == "npcsetactive")
            {
                HandleNpcSetActive(act.npcName, act.active);
                // [수정됨] 자동 복구 로직을 제거하여 명시적인 delay가 가능하도록 함
            }
            else if (actionType == "log") { Debug.Log(string.IsNullOrEmpty(act.message) ? "[NpcEventDebugLoader] (reaction log)" : act.message); }
            else if (actionType == "affinityup")
            {
                string dn = ResolveAffinityDataName(act.dataName);
                if (!AffinityUp(dn, (int)act.delta, act.clamp, act.min, act.max)) Debug.LogWarning($"[NpcEventDebugLoader] (reaction) affinityUp 실패 — dataName='{act.dataName}' (resolved='{dn}')");
            }
            else if (actionType == "affinitydown")
            {
                string dn = ResolveAffinityDataName(act.dataName);
                if (!AffinityDown(dn, (int)act.delta, act.clamp, act.min, act.max)) Debug.LogWarning($"[NpcEventDebugLoader] (reaction) affinityDown 실패 — dataName='{act.dataName}' (resolved='{dn}')");
            }
            else if (actionType == "datadelta")
            {
                if (!ApplyDataDeltaInt(act.dataName, (int)act.delta)) Debug.LogWarning($"[NpcEventDebugLoader] (reaction) dataDelta 실패 — dataName='{act.dataName}'");
            }
            else if (actionType == "eventend") { break; }
        }
        if (_dialoguePanelAutoReEnableArmed || _dialoguePanelTemporarilyHidden)
            RestoreDialoguePanelAfterTempHide();
    }

    private float DeriveNpcDurationBySpeed(Vector2 delta, float requestedDuration)
    {
        if (requestedDuration > 0f) return requestedDuration;
        float speed = 0f;
        if (_currentPlayerMoveSpeedForEvent > 1e-4f) speed = _currentPlayerMoveSpeedForEvent;
        if (speed > 1e-4f)
        {
            float dist = delta.magnitude;
            return (dist <= 1e-6f) ? 0f : (dist / speed);
        }
        return 1f;
    }

    private void HandleDialoguePanelActive(bool active)
    {
        if (autoFindDialogueManager && dialogueManager == null) dialogueManager = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);
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

    private void ForceActivateDialoguePanelNow()
    {
        if (autoFindDialogueManager && dialogueManager == null) dialogueManager = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);
        if (!dialoguePanel && dialogueManager) dialoguePanel = dialogueManager.gameObject;
        _dialoguePanelTemporarilyHidden = false;
        _dialoguePanelAutoReEnableArmed = false;
        if (dialoguePanel) dialoguePanel.SetActive(true);
        if (verboseLog) Debug.Log("[NpcEventDebugLoader] Dialogue Panel 강제 활성화");
    }

    private PlayerData ResolvePlayerData()
    {
        if (DataManager.instance == null)
        {
            Debug.LogError("[NpcEventDebugLoader] DataManager.instance를 찾을 수 없습니다! 씬에 DataManager가 있는지 확인하세요.");
            return null;
        }
        return DataManager.instance.nowPlayer;
    }

    private void InvokeSave()
    {
        if (DataManager.instance != null)
        {
            DataManager.instance.SaveData();
        }
        else
        {
            Debug.LogWarning("[NpcEventDebugLoader] DataManager.instance가 없어 저장 호출을 건너뜁니다.");
        }
    }

    // ===== [수정됨] 파일 로드 방식을 Resources.Load로 변경 =====
    private bool TryLoadSingle(string ownerForIO, string eventForIO, out LoadedEvent loaded)
    {
        loaded = null;
        // Resources.Load 경로는 'Resources' 폴더를 기준으로 하며, 확장자를 포함하지 않습니다.
        // 예: "Event/Sol/Sol_First_Meet"
        string resourcePath = Path.Combine(eventFolderName, ownerForIO, eventForIO);

        try
        {
            // .json 파일을 TextAsset으로 불러옵니다.
            TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);

            // 파일을 찾지 못한 경우
            if (textAsset == null)
            {
                // StreamingAssets에서도 한번 더 찾아봅니다 (이전 방식 호환).
                // 이 경로는 PC 빌드에서는 잘 작동하지만, 모바일에서는 추가 처리가 필요할 수 있습니다.
                string streamingPath = Path.Combine(Application.streamingAssetsPath, resourcePath + ".json");
                if (File.Exists(streamingPath))
                {
                    string jsonFromStream = File.ReadAllText(streamingPath);
                    if (!string.IsNullOrEmpty(jsonFromStream))
                    {
                        if (verboseLog) Debug.Log($"[NpcEventDebugLoader] StreamingAssets에서 로드 성공: {streamingPath}");
                        loaded = new LoadedEvent { ownerName = ownerForIO, eventName = eventForIO, path = streamingPath, json = jsonFromStream };
                        return true;
                    }
                }
                return false;
            }

            string json = textAsset.text;
            if (string.IsNullOrEmpty(json)) return false;

            // 성공. 불러온 이벤트 정보 생성
            loaded = new LoadedEvent { ownerName = ownerForIO, eventName = eventForIO, path = "Resources/" + resourcePath, json = json };
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NpcEventDebugLoader] Resources.Load 예외 발생: {resourcePath}, 오류={e}");
            return false;
        }
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

    private static bool TryParseEventScript(string rawJson, out EventScript script, bool logOnError)
    {
        script = null;
        if (string.IsNullOrWhiteSpace(rawJson)) return false;
        string s = rawJson.Trim();
        if (s.StartsWith("[")) s = "{\"steps\":" + s + "}";
        s = Regex.Replace(s, @"//.*?$", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
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
            if (logOnError) Debug.LogError($"[NpcEventDebugLoader] Sanitized JSON parse 실패:\n---SANITIZED---\n{s}\n---ERR---\n{e}");
            return false;
        }
    }

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
                ownerForIO = oe.ownerName;
                return oe;
            }
        }
        return null;
    }

    private bool TryBindBool(object obj, string boolName, out Func<bool> getter, out Action<bool> setter)
    {
        getter = null; setter = null;
        var t = obj.GetType();
        var field = t.GetFields(_bf).FirstOrDefault(fi => fi.FieldType == typeof(bool) && string.Equals(fi.Name, boolName, StringComparison.OrdinalIgnoreCase));
        if (field != null)
        {
            getter = () => (bool)field.GetValue(obj);
            setter = v => field.SetValue(obj, v);
            return true;
        }
        var prop = t.GetProperties(_bf).FirstOrDefault(pi => pi.PropertyType == typeof(bool) && string.Equals(pi.Name, boolName, StringComparison.OrdinalIgnoreCase) && pi.CanRead && pi.CanWrite);
        if (prop != null)
        {
            getter = () => (bool)prop.GetValue(obj, null);
            setter = v => prop.SetValue(obj, v, null);
            return true;
        }
        return false;
    }

    private bool TryBindInt(object obj, string name, out Func<int> getter, out Action<int> setter)
    {
        getter = null; setter = null;
        if (obj == null || string.IsNullOrWhiteSpace(name)) return false;
        var t = obj.GetType();
        var f = t.GetFields(_bf).FirstOrDefault(fi => fi.FieldType == typeof(int) && string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase));
        if (f != null)
        {
            getter = () => (int)f.GetValue(obj);
            setter = v => f.SetValue(obj, v);
            return true;
        }
        var p = t.GetProperties(_bf).FirstOrDefault(pi => pi.PropertyType == typeof(int) && string.Equals(pi.Name, name, StringComparison.OrdinalIgnoreCase) && pi.CanRead && pi.CanWrite);
        if (p != null)
        {
            getter = () => (int)p.GetValue(obj, null);
            setter = v => p.SetValue(obj, v, null);
            return true;
        }
        return false;
    }

    private string ResolveAffinityDataName(string providedOrEmpty)
    {
        if (!string.IsNullOrWhiteSpace(providedOrEmpty)) return providedOrEmpty.Trim();
        if (string.IsNullOrWhiteSpace(_ctxOwner)) return null;
        var pd = ResolvePlayerData();
        if (pd == null) return null;
        string[] candidates = new[] { $"{_ctxOwner}_FriendShip", $"{_ctxOwner}_Affinity", $"{_ctxOwner}_Like" };
        foreach (var name in candidates)
        {
            if (TryBindInt(pd, name, out var _, out var _)) return name;
        }
        if (verboseLog) Debug.LogWarning($"[NpcEventDebugLoader] affinity dataName 자동 탐색 실패 — owner='{_ctxOwner}'. PlayerData에 '{_ctxOwner}_FriendShip' 같은 필드를 준비하거나 JSON에 dataName을 명시하세요.");
        return null;
    }

    private bool ApplyDataDeltaInt(string dataName, int delta, bool clampAffinity = false, int min = 0, int max = 100)
    {
        var pd = ResolvePlayerData();
        if (pd == null)
        {
            Debug.LogWarning($"[NpcEventDebugLoader] PlayerData 없음: '{dataName}' 업데이트 불가");
            return false;
        }
        if (string.IsNullOrWhiteSpace(dataName))
        {
            Debug.LogWarning("[NpcEventDebugLoader] dataName이 비어 있어 int 필드 바인딩 불가");
            return false;
        }
        if (!TryBindInt(pd, dataName, out var get, out var set))
        {
            Debug.LogWarning($"[NpcEventDebugLoader] PlayerData에 int '{dataName}'를 찾지 못했습니다.");
            return false;
        }
        int oldv = get();
        int newv = oldv + delta;
        if (clampAffinity) newv = Mathf.Clamp(newv, min, max);
        set(newv);
        if (verboseLog) Debug.Log($"[NpcEventDebugLoader] {dataName}: {oldv} -> {newv} (delta {delta})");

        // if (saveAfterWrite) InvokeSave();

        return true;
    }

    private bool AffinityUp(string dataName, int delta = 1, bool clamp = true, int min = 0, int max = 100)
    {
        int clampedDelta = Mathf.Clamp(delta, -10, 10);
        return ApplyDataDeltaInt(dataName, Mathf.Abs(clampedDelta), clamp, min, max);
    }

    private bool AffinityDown(string dataName, int delta = 1, bool clamp = true, int min = 0, int max = 100)
    {
        int clampedDelta = Mathf.Clamp(delta, -10, 10);
        return ApplyDataDeltaInt(dataName, -Mathf.Abs(clampedDelta), clamp, min, max);
    }

    #region NPC 및 이동 관련
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
        if (verboseLog) Debug.Log($"[NpcEventDebugLoader] NPC 카탈로그 인덱싱 완료: owners={_npcSpecsByKey.Count}, names={_npcSpecByName.Count}");
    }

    private void SpawnNpcsForOwnerOnEnter(string ownerForIO)
    {
        if (deactivateOtherOwnersNpcsOnEnter) DeactivateOrDestroyNpcsExcept(Key(ownerForIO));
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
        if (verboseLog) Debug.Log($"[NpcEventDebugLoader] NPC 스폰: ownerKey='{ownerKey}', npc='{spec.npcName}', pos={spawnPos}");
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
            if (snap.deactivated.Count > 0) _ownersDeactivatedOnEnter.Add(snap);
        }
    }

    private NpcSpec FindNpcSpecByKey(string ownerKey, string npcName)
    {
        if (_npcSpecsByKey.TryGetValue(ownerKey, out var list))
        {
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s != null && string.Equals(s.npcName, npcName, StringComparison.OrdinalIgnoreCase)) return s;
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
            if (cmd.deactivateExisting) DeactivateMapNpcs(FindSceneNpcsByName(npc));
            Vector3 p = cmd.relativeToPlayer && playerTransform ? playerTransform.position + new Vector3(cmd.x, cmd.y, cmd.z) : new Vector3(cmd.x, cmd.y, cmd.z);
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
        if (verboseLog) Debug.Log($"[NpcEventDebugLoader] NPC 스폰(오너무시): npc='{npcName}', ownerInCatalog='{spec.ownerName}', pos={worldPos}");
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

    private IEnumerator NpcMoveByWorld(GameObject npc, Vector2 delta, float duration, NpcSpec specOrNull)
    {
        if (!npc) yield break;
        var tr = npc.transform;
        if (!tr) yield break;
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
            if (rb) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; rb.MovePosition(new Vector2(target.x, target.y)); }
            else { tr.position = target; }
            if (anim) StopNpcIdle(anim);
            yield break;
        }
        float t = 0f;
        while (t < duration)
        {
            if (!npc || !tr) yield break;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            Vector3 pos = Vector3.Lerp(start, target, u);
            if (rb) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; rb.MovePosition(new Vector2(pos.x, pos.y)); }
            else { tr.position = pos; }
            yield return null;
        }
        if (!npc || !tr) yield break;
        if (rb) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; rb.MovePosition(new Vector2(target.x, target.y)); }
        else { tr.position = target; }
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

    private void EnterEventGuard()
    {
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
            if (freezePhysicsDuringEvent) _playerRb2D.simulated = false;
            else { _playerRb2D.bodyType = RigidbodyType2D.Kinematic; _playerRb2D.constraints = RigidbodyConstraints2D.FreezePosition | RigidbodyConstraints2D.FreezeRotation; }
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
        if (!fadeImage) yield break;
        if (targetAlpha > 0f && !fadeImage.gameObject.activeSelf)
        {
            fadeImage.gameObject.SetActive(true);
        }
        if (duration <= 0f)
        {
            Color instantColor = fadeImage.color;
            instantColor.a = targetAlpha;
            fadeImage.color = instantColor;
            if (targetAlpha <= 0f)
            {
                fadeImage.gameObject.SetActive(false);
            }
            yield break;
        }
        float timer = 0f;
        Color startColor = fadeImage.color;
        Color targetColor = new Color(startColor.r, startColor.g, startColor.b, targetAlpha);
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / duration);
            fadeImage.color = Color.Lerp(startColor, targetColor, progress);
            yield return null;
        }
        fadeImage.color = targetColor;
        if (targetAlpha <= 0f)
        {
            fadeImage.gameObject.SetActive(false);
        }
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
                    if (dict.ContainsKey(k) && dict[k] == go) dict[k] = null;
                }
            }
            foreach (var k in new List<string>(_spawnedNpcByName.Keys))
            {
                if (_spawnedNpcByName.ContainsKey(k) && _spawnedNpcByName[k] == go) _spawnedNpcByName.Remove(k);
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
    #endregion
}

public class NpcIdentity : MonoBehaviour
{
    public string npcName;
}