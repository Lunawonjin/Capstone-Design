using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TogglePanelWithPause (ESC 오프너 단일화/우선순위 + 전역 락 + 워치독 + CanvasGroup 동기화 + Exit 버튼)
///
/// ■ 기능 요약
/// - 버튼 클릭으로 지정된 toggleTargets를 ON/OFF.
/// - ON 동안, 전역 코디네이터(PauseCoordinator)가 시간/오디오/입력/애니메이터를 잠급니다(중첩 안전).
/// - 워치독: 외부에서 대상 active가 바뀌어도 Enter/ExitPause 상태를 자동 동기화.
/// - ESC 글로벌 토글:
///     * 이 패널이 켜져 있으면 ESC로 끄기.
///     * UIExclusiveManager 기준으로 "그룹 전체에 아무 UI도 없을 때만" ESC로 이 패널 열기.
///     * 동시에 여러 인스턴스가 있을 때는 escGroupId 그룹 내 isEscOpener=true 중
///       escPriority가 가장 높은 인스턴스만 ESC 오프너(Owner)로 동작.
///     * escBlockerWhileActive가 활성일 때는 ESC 입력을 무시(모달 보호).
/// - CanvasGroup가 있으면 blocksRaycasts/interactable를 상태에 맞게 자동 세팅.
/// - Exit 버튼 제공: 눌렀을 때 켜져 있으면 안전하게 종료(ESC와 동일한 보호 규칙).
///
/// ■ 권장 세팅
/// - UIExclusiveManager는 ESC 처리를 비활성(escToClose=false)하여 ESC 처리를 본 스크립트로 일원화.
/// - 두 패널 이상이 본 스크립트를 쓸 경우:
///     · 열리고 싶은 패널: isEscOpener=true, 같은 escGroupId, 필요시 escPriority를 더 높게.
///     · 열리고 싶지 않은 패널: isEscOpener=false.
/// </summary>
[DisallowMultipleComponent]
public class TogglePanelWithPause : MonoBehaviour
{
    // =====================================================================
    // 전역 코디네이터(시간/오디오/입력/애니메이터 잠금)
    // =====================================================================
    private static class PauseCoordinator
    {
        // --- Time.timeScale 잠금 ---
        private static int _timeLocks = 0;
        private static float _savedTimeScale = 1f;

        // --- 오디오 잠금 ---
        private static int _audioLocks = 0;
        private static bool _savedAudioPaused = false;

        // --- 임의 Behaviour 잠금 ---
        private class BoolLock { public int count; public bool saved; }
        private static readonly Dictionary<Behaviour, BoolLock> _behaviourLocks = new();

        // --- PlayerMove 잠금 ---
        private static readonly Dictionary<PlayerMove, BoolLock> _playerMoveLocks = new();

        // --- Animator 잠금 ---
        private class AnimLock { public int count; public float savedSpeed; }
        private static readonly Dictionary<Animator, AnimLock> _animLocks = new();

        public static void AcquireTime()
        {
            if (++_timeLocks == 1)
            {
                _savedTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
        }
        public static void ReleaseTime()
        {
            if (_timeLocks <= 0) return;
            if (--_timeLocks == 0) Time.timeScale = _savedTimeScale;
        }

        public static void AcquireAudio()
        {
            if (++_audioLocks == 1)
            {
                _savedAudioPaused = AudioListener.pause;
                AudioListener.pause = true;
            }
        }
        public static void ReleaseAudio()
        {
            if (_audioLocks <= 0) return;
            if (--_audioLocks == 0) AudioListener.pause = _savedAudioPaused;
        }

        public static void LockBehaviour(Behaviour b)
        {
            if (!b) return;
            if (!_behaviourLocks.TryGetValue(b, out var s))
            {
                s = new() { count = 0, saved = b.enabled };
                _behaviourLocks[b] = s;
            }
            s.count++;
            b.enabled = false;
        }
        public static void UnlockBehaviour(Behaviour b)
        {
            if (!b) return;
            if (!_behaviourLocks.TryGetValue(b, out var s)) return;
            s.count = Mathf.Max(0, s.count - 1);
            if (s.count == 0)
            {
                b.enabled = s.saved;
                _behaviourLocks.Remove(b);
            }
        }

        public static void LockPlayer(PlayerMove pm)
        {
            if (!pm) return;
            if (!_playerMoveLocks.TryGetValue(pm, out var s))
            {
                s = new() { count = 0, saved = pm.enabled };
                _playerMoveLocks[pm] = s;
            }
            s.count++;
            pm.enabled = false;
        }
        public static void UnlockPlayer(PlayerMove pm)
        {
            if (!pm) return;
            if (!_playerMoveLocks.TryGetValue(pm, out var s)) return;
            s.count = Mathf.Max(0, s.count - 1);
            if (s.count == 0)
            {
                pm.enabled = s.saved;
                _playerMoveLocks.Remove(pm);
            }
        }

        public static void LockAnimator(Animator a)
        {
            if (!a) return;
            if (!_animLocks.TryGetValue(a, out var s))
            {
                s = new() { count = 0, savedSpeed = a.speed };
                _animLocks[a] = s;
            }
            s.count++;
            a.speed = 0f;
        }
        public static void UnlockAnimator(Animator a)
        {
            if (!a) return;
            if (!_animLocks.TryGetValue(a, out var s)) return;
            s.count = Mathf.Max(0, s.count - 1);
            if (s.count == 0)
            {
                a.speed = s.savedSpeed;
                _animLocks.Remove(a);
            }
        }
    }

    // =====================================================================
    // ESC 오프너 전역 레지스트리
    // =====================================================================
    private struct EscEntry { public TogglePanelWithPause inst; public int priority; }
    private static readonly Dictionary<string, EscEntry> s_EscOwnerByGroup = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics_EscOwner() => s_EscOwnerByGroup.Clear();

    private static void RegisterEscOpener(string groupId, TogglePanelWithPause who, int priority)
    {
        if (string.IsNullOrEmpty(groupId) || who == null) return;

        if (s_EscOwnerByGroup.TryGetValue(groupId, out var cur))
        {
            if (priority > cur.priority)
                s_EscOwnerByGroup[groupId] = new EscEntry { inst = who, priority = priority };
        }
        else
        {
            s_EscOwnerByGroup[groupId] = new EscEntry { inst = who, priority = priority };
        }
    }

    private static void UnregisterEscOpener(string groupId, TogglePanelWithPause who)
    {
        if (string.IsNullOrEmpty(groupId) || who == null) return;
        if (s_EscOwnerByGroup.TryGetValue(groupId, out var cur) && cur.inst == who)
            s_EscOwnerByGroup.Remove(groupId);
    }

    private bool IsEscOwner()
    {
        if (!isEscOpener || string.IsNullOrEmpty(escGroupId)) return false;

        if (s_EscOwnerByGroup.TryGetValue(escGroupId, out var cur))
        {
            if (cur.inst == null)
            {
                s_EscOwnerByGroup.Remove(escGroupId);
                return false;
            }
            return cur.inst == this;
        }
        return false;
    }

    // =====================================================================
    // 인스펙터 필드
    // =====================================================================

    [Header("Required")]
    [Tooltip("이 버튼을 눌렀을 때 토글 동작을 수행합니다(선택). 없으면 코드/ESC로만 토글합니다.")]
    [SerializeField] private Button button;

    [Header("Exit Button")]
    [Tooltip("Exit(닫기) 버튼. 누르면 이 패널을 비활성화합니다.")]
    [SerializeField] private Button exitButton;   // ★ 추가

    [Header("Toggle Targets")]
    [Tooltip("켜고 끌 UI 루트(들). 여러 개면 모두 같은 타이밍에 ON/OFF 됩니다.")]
    [SerializeField] private GameObject[] toggleTargets;

    [Header("Force OFF When Opening")]
    [Tooltip("이 패널을 켤 때 강제로 끌 대상들(서로 배타인 다른 패널들 등)")]
    [SerializeField] private GameObject[] forceOffOnOpen;

    [Header("Pause Options")]
    [Tooltip("ON 동안 게임 시간을 멈춥니다(Time.timeScale=0). 중첩 안전.")]
    [SerializeField] private bool pauseGameTime = true;

    [Tooltip("ON 동안 전역 오디오(AudioListener.pause)를 멈춥니다. 중첩 안전.")]
    [SerializeField] private bool pauseAudioOnPause = true;

    [Tooltip("ON 동안 비활성화할 임의의 Behaviour(입력 스크립트 등)")]
    [SerializeField] private Behaviour[] optionalDisableOnPause;

    [Header("Player Controls Lock")]
    [Tooltip("Player 태그에서 PlayerMove를 자동 수집합니다.")]
    [SerializeField] private bool autoFindPlayerMove = true;

    [Tooltip("수동으로 지정할 PlayerMove들(자동 수집에 추가됨)")]
    [SerializeField] private PlayerMove[] playerMoves;

    [Header("Player Animation Pause")]
    [Tooltip("Player 태그에서 Animator들을 자동 수집합니다.")]
    [SerializeField] private bool autoFindPlayerAnimators = true;

    [Tooltip("수동으로 지정할 Animator들(자동 수집에 추가됨)")]
    [SerializeField] private Animator[] playerAnimators;

    [Header("UI Raycast Blocking")]
    [Tooltip("CanvasGroup가 있으면 blocksRaycasts/interactable를 상태에 맞게 자동 설정")]
    [SerializeField] private bool useCanvasGroupIfAvailable = true;

    [Tooltip("전체 화면 클릭 차단용 오버레이(선택)")]
    [SerializeField] private GameObject fullScreenBlocker;

    [Header("Watchdog")]
    [Tooltip("외부에서 대상 active가 바뀌면 자동으로 Enter/ExitPause를 동기화")]
    [SerializeField] private bool autoSyncWithTargets = true;

    [Header("ESC Global Toggle")]
    [Tooltip("ESC 입력으로 글로벌 토글을 사용할지 여부")]
    [SerializeField] private bool enableEscClose = true;

    [Tooltip("구 Input 시스템 사용 시 ESC로 인식할 키")]
    [SerializeField] private KeyCode escKey = KeyCode.Escape;

    [Tooltip("이 오브젝트가 활성일 땐 ESC로 끄기 금지(예: Warning 모달 등)")]
    [SerializeField] private GameObject escBlockerWhileActive;

    [Header("UI Group Reference (Exclusive)")]
    [Tooltip("UIExclusiveManager가 연결되면, '그룹 내 다른 UI가 하나라도 켜져 있으면' ESC로 이 패널을 열지 않습니다.")]
    [SerializeField] private UIExclusiveManager uiGroup;

    [Header("ESC Opener Selection")]
    [Tooltip("이 인스턴스를 ESC로 '열기' 담당자로 쓸지 여부")]
    [SerializeField] private bool isEscOpener = true;

    [Tooltip("같은 escGroupId 안에서 우선순위가 높은 인스턴스가 ESC 오너가 됩니다.")]
    [SerializeField] private int escPriority = 0;

    [Tooltip("ESC 오프너를 묶는 그룹 ID. 같은 ID 안에서 단 하나만 오너로 동작합니다.")]
    [SerializeField] private string escGroupId = "MainUI";

    // =====================================================================
    // 내부 상태/버퍼
    // =====================================================================

    private bool _isPausedNow = false;

    private readonly List<Animator> _animList = new(8);
    private readonly List<PlayerMove> _playerMoveList = new(2);
    private readonly List<Rigidbody2D> _playerRigidbodies = new(2);

    // =====================================================================
    // 생명주기
    // =====================================================================

    private void Reset()
    {
        pauseGameTime = true;
        pauseAudioOnPause = true;
        useCanvasGroupIfAvailable = true;
        autoFindPlayerAnimators = true;
        autoFindPlayerMove = true;
        autoSyncWithTargets = true;

        enableEscClose = true;
        escKey = KeyCode.Escape;

        isEscOpener = true;
        escPriority = 0;
        escGroupId = "MainUI";
    }

    private void Awake()
    {
        if (button == null)
            Debug.LogWarning("[TogglePanelWithPause] Button not assigned.", this);
        if (toggleTargets == null || toggleTargets.Length == 0)
            Debug.LogWarning("[TogglePanelWithPause] toggleTargets is empty.", this);

        BuildAnimatorList();
        BuildPlayerMoveList();

        if (uiGroup == null)
        {
#if UNITY_2023_1_OR_NEWER
            uiGroup = Object.FindAnyObjectByType<UIExclusiveManager>();
            if (uiGroup == null) uiGroup = Object.FindFirstObjectByType<UIExclusiveManager>();
#else
            uiGroup = Object.FindObjectOfType<UIExclusiveManager>();
#endif
        }
    }

    private void OnEnable()
    {
        if (button != null)
            button.onClick.AddListener(OnButtonClicked);

        // ★ Exit 버튼 리스너 등록
        if (exitButton != null)
            exitButton.onClick.AddListener(OnClickExit);

        if (isEscOpener)
            RegisterEscOpener(escGroupId, this, escPriority);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(OnButtonClicked);

        // ★ Exit 버튼 리스너 해제
        if (exitButton != null)
            exitButton.onClick.RemoveListener(OnClickExit);

        if (isEscOpener)
            UnregisterEscOpener(escGroupId, this);

        if (_isPausedNow)
            ExitPause();
    }

    private void Update()
    {
        // ------------------------------------------------------------
        // ESC 글로벌 토글
        // ------------------------------------------------------------
        if (enableEscClose && !_IsEscBlockedByModal() && _WasEscPressed())
        {
            bool anyThisActive = AnyTargetActive();
            bool anyGroupActive = (uiGroup != null && uiGroup.IsAnyActive);

            if (anyThisActive)
            {
                TurnOff();
            }
            else if (!anyGroupActive && IsEscOwner())
            {
                TurnOn();
            }
        }

        // ------------------------------------------------------------
        // 워치독: 외부 active 변화를 상태와 동기화
        // ------------------------------------------------------------
        if (!autoSyncWithTargets || toggleTargets == null || toggleTargets.Length == 0) return;

        bool anyActive = AnyTargetActive();

        if (_isPausedNow && !anyActive)
        {
            ExitPause();
        }
        else if (!_isPausedNow && anyActive)
        {
            EnterPause();
        }
    }

    // =====================================================================
    // 버튼 핸들러
    // =====================================================================
    private void OnButtonClicked()
    {
        bool toActive = !AnyTargetActive();
        if (toActive) TurnOn();
        else TurnOff();
    }

    // ★ Exit 버튼 핸들러: 켜져 있고 모달이 아니면 끕니다.
    private void OnClickExit()
    {
        if (_IsEscBlockedByModal()) return; // 모달 보호
        if (!AnyTargetActive()) return;     // 이미 꺼져 있으면 무시
        TurnOff();                           // 비활성화 + Pause 해제
    }

    // =====================================================================
    // 토글/상태
    // =====================================================================

    private bool AnyTargetActive()
    {
        if (toggleTargets == null) return false;
        foreach (var go in toggleTargets)
            if (go && go.activeSelf) return true;
        return false;
    }

    private void TurnOn()
    {
        if (toggleTargets != null)
        {
            foreach (var go in toggleTargets)
            {
                if (!go) continue;
                if (!go.activeSelf) go.SetActive(true);
                ApplyCanvasGroup(go, true);
            }
        }

        if (forceOffOnOpen != null)
        {
            foreach (var go in forceOffOnOpen)
            {
                if (!go) continue;
                if (go.activeSelf) go.SetActive(false);
                ApplyCanvasGroup(go, false);
            }
        }

        if (fullScreenBlocker && !fullScreenBlocker.activeSelf)
            fullScreenBlocker.SetActive(true);

        EnterPause();
    }

    private void TurnOff()
    {
        if (toggleTargets != null)
        {
            foreach (var go in toggleTargets)
            {
                if (!go) continue;
                if (go.activeSelf) go.SetActive(false);
                ApplyCanvasGroup(go, false);
            }
        }

        if (fullScreenBlocker && fullScreenBlocker.activeSelf)
            fullScreenBlocker.SetActive(false);

        ExitPause();
    }

    private void EnterPause()
    {
        if (_isPausedNow) return;

        if (pauseGameTime) PauseCoordinator.AcquireTime();
        if (pauseAudioOnPause) PauseCoordinator.AcquireAudio();

        if (optionalDisableOnPause != null)
            foreach (var b in optionalDisableOnPause)
                PauseCoordinator.LockBehaviour(b);

        LockPlayerControls();
        PausePlayerAnimators();

        _isPausedNow = true;
    }

    private void ExitPause()
    {
        if (!_isPausedNow) return;

        ResumePlayerAnimators();
        UnlockPlayerControls();

        if (optionalDisableOnPause != null)
            foreach (var b in optionalDisableOnPause)
                PauseCoordinator.UnlockBehaviour(b);

        if (pauseGameTime) PauseCoordinator.ReleaseTime();
        if (pauseAudioOnPause) PauseCoordinator.ReleaseAudio();

        _isPausedNow = false;
    }

    private void ApplyCanvasGroup(GameObject go, bool on)
    {
        if (!useCanvasGroupIfAvailable || !go) return;
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) return;
        cg.blocksRaycasts = on;
        cg.interactable = on;
        cg.ignoreParentGroups = false;
    }

    // =====================================================================
    // Animator 수집/제어
    // =====================================================================
    private void BuildAnimatorList()
    {
        _animList.Clear();

        if (autoFindPlayerAnimators)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player)
            {
                var found = player.GetComponentsInChildren<Animator>(true);
                if (found != null && found.Length > 0)
                    _animList.AddRange(found);
            }
            else
            {
                Debug.LogWarning("[TogglePanelWithPause] Player tag not found for animators.", this);
            }
        }

        if (playerAnimators != null && playerAnimators.Length > 0)
        {
            foreach (var a in playerAnimators)
                if (a && !_animList.Contains(a))
                    _animList.Add(a);
        }
    }

    private void PausePlayerAnimators()
    {
        if (_animList.Count == 0) BuildAnimatorList();
        foreach (var a in _animList)
            PauseCoordinator.LockAnimator(a);
    }

    private void ResumePlayerAnimators()
    {
        foreach (var a in _animList)
            PauseCoordinator.UnlockAnimator(a);
    }

    // =====================================================================
    // PlayerMove 수집/제어 + Rigidbody 정지
    // =====================================================================
    private void BuildPlayerMoveList()
    {
        _playerMoveList.Clear();
        _playerRigidbodies.Clear();

        if (autoFindPlayerMove)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player)
            {
                var found = player.GetComponentsInChildren<PlayerMove>(true);
                if (found != null && found.Length > 0)
                    _playerMoveList.AddRange(found);

                var rb = player.GetComponentInChildren<Rigidbody2D>(true);
                if (rb) _playerRigidbodies.Add(rb);
            }
            else
            {
                Debug.LogWarning("[TogglePanelWithPause] Player tag not found for PlayerMove.", this);
            }
        }

        if (playerMoves != null && playerMoves.Length > 0)
        {
            foreach (var p in playerMoves)
                if (p && !_playerMoveList.Contains(p))
                    _playerMoveList.Add(p);
        }
    }

    private void LockPlayerControls()
    {
        if (_playerMoveList.Count == 0)
            BuildPlayerMoveList();

        foreach (var pm in _playerMoveList)
            PauseCoordinator.LockPlayer(pm);

        foreach (var rb in _playerRigidbodies)
        {
            if (!rb) continue;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    private void UnlockPlayerControls()
    {
        foreach (var pm in _playerMoveList)
            PauseCoordinator.UnlockPlayer(pm);
    }

    // =====================================================================
    // ESC 입력 헬퍼
    // =====================================================================
    private bool _IsEscBlockedByModal()
        => escBlockerWhileActive && escBlockerWhileActive.activeSelf;

    private bool _WasEscPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return UnityEngine.InputSystem.Keyboard.current != null
            && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(escKey);
#endif
    }
}
