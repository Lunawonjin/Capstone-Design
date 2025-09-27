using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TogglePanelWithPause (ESC 오프너 단일화/우선순위 + 전역 락 + 워치독 + CanvasGroup 동기화)
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
    // 전역 코디네이터
    // - PauseCoordinator는 "전역 잠금"을 레퍼런스 카운트로 관리합니다.
    // - 여러 UI가 동시에 열려도 Acquire/Release가 균형만 맞으면 안전합니다.
    // =====================================================================
    private static class PauseCoordinator
    {
        // --- Time.timeScale 잠금 ---
        private static int _timeLocks = 0;
        private static float _savedTimeScale = 1f;

        // --- 오디오 잠금 ---
        private static int _audioLocks = 0;
        private static bool _savedAudioPaused = false;

        // --- 임의의 Behaviour 잠금(입력 스크립트 등) ---
        private class BoolLock { public int count; public bool saved; }
        private static readonly Dictionary<Behaviour, BoolLock> _behaviourLocks = new();

        // --- PlayerMove 잠금(별도로 분리해 추적) ---
        private static readonly Dictionary<PlayerMove, BoolLock> _playerMoveLocks = new();

        // --- Animator 잠금(속도 저장 후 0으로) ---
        private class AnimLock { public int count; public float savedSpeed; }
        private static readonly Dictionary<Animator, AnimLock> _animLocks = new();

        // ■ 시간 잠금 획득
        public static void AcquireTime()
        {
            if (++_timeLocks == 1)
            {
                _savedTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
        }
        // ■ 시간 잠금 해제
        public static void ReleaseTime()
        {
            if (_timeLocks <= 0) return;
            if (--_timeLocks == 0)
                Time.timeScale = _savedTimeScale;
        }

        // ■ 오디오 잠금 획득
        public static void AcquireAudio()
        {
            if (++_audioLocks == 1)
            {
                _savedAudioPaused = AudioListener.pause;
                AudioListener.pause = true;
            }
        }
        // ■ 오디오 잠금 해제
        public static void ReleaseAudio()
        {
            if (_audioLocks <= 0) return;
            if (--_audioLocks == 0)
                AudioListener.pause = _savedAudioPaused;
        }

        // ■ 임의의 Behaviour 잠금
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
        // ■ 임의의 Behaviour 잠금 해제
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

        // ■ PlayerMove 잠금
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
        // ■ PlayerMove 잠금 해제
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

        // ■ Animator 잠금
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
        // ■ Animator 잠금 해제
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
    // - 같은 escGroupId 그룹 내에서 "단 하나"의 인스턴스만 ESC 오프너로 인정.
    // - isEscOpener=true 중 escPriority가 가장 높은 쪽이 오너가 됩니다.
    // - Domain Reload OFF 대비: 플레이 시작 시 정적 초기화.
    // =====================================================================
    private struct EscEntry { public TogglePanelWithPause inst; public int priority; }
    private static readonly Dictionary<string, EscEntry> s_EscOwnerByGroup = new();

    /// <summary>
    /// 플레이 시작마다 정적 레지스트리를 초기화하여
    /// Domain Reload OFF 환경에서 이전 플레이의 오너가 남는 문제를 방지합니다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics_EscOwner() => s_EscOwnerByGroup.Clear();

    private static void RegisterEscOpener(string groupId, TogglePanelWithPause who, int priority)
    {
        if (string.IsNullOrEmpty(groupId) || who == null) return;

        if (s_EscOwnerByGroup.TryGetValue(groupId, out var cur))
        {
            // 더 높은 우선순위가 들어오면 교체(같으면 기존 유지)
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

    /// <summary>
    /// 내가 현재 escGroupId의 오너인지 확인.
    /// 유령 참조가 남아 있으면 정리하고 false 반환.
    /// </summary>
    private bool IsEscOwner()
    {
        if (!isEscOpener || string.IsNullOrEmpty(escGroupId)) return false;

        if (s_EscOwnerByGroup.TryGetValue(escGroupId, out var cur))
        {
            if (cur.inst == null)
            {
                // 씬 리로드/오브젝트 파괴로 유령 참조가 남았을 경우 청소
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

    /// <summary>현재 이 패널이 Pause 상태인지(ON으로 간주)</summary>
    private bool _isPausedNow = false;

    // Animator/PlayerMove/Rigidbody2D 캐시(중복 선언 금지!)
    private readonly List<Animator> _animList = new(8);
    private readonly List<PlayerMove> _playerMoveList = new(2);
    private readonly List<Rigidbody2D> _playerRigidbodies = new(2);

    // =====================================================================
    // 생명주기
    // =====================================================================

    private void Reset()
    {
        // 합리적 기본값
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
        // 필수 참조 체크
        if (button == null)
            Debug.LogWarning("[TogglePanelWithPause] Button not assigned.", this);
        if (toggleTargets == null || toggleTargets.Length == 0)
            Debug.LogWarning("[TogglePanelWithPause] toggleTargets is empty.", this);

        // 초기 수집
        BuildAnimatorList();
        BuildPlayerMoveList();

        // UI 그룹 자동 연결(에디터 경고 피하기 위해 최신 API 사용)
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

        // ESC 오프너로 등록(우선순위 반영)
        if (isEscOpener)
            RegisterEscOpener(escGroupId, this, escPriority);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(OnButtonClicked);

        // ESC 오프너 해제
        if (isEscOpener)
            UnregisterEscOpener(escGroupId, this);

        // 켜진 상태에서 비활성화되면 복구
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
            bool anyThisActive = AnyTargetActive();                        // 이 패널의 대상들
            bool anyGroupActive = (uiGroup != null && uiGroup.IsAnyActive); // 그룹 전체(다른 UI 포함)

            if (anyThisActive)
            {
                // 이 패널이 켜져 있으면 ESC로 끕니다.
                TurnOff();
            }
            else if (!anyGroupActive && IsEscOwner())
            {
                // 그룹 전체가 비어 있고, 내가 ESC 오너인 경우에만 ESC로 켭니다.
                TurnOn();
            }
            // else: 다른 UI가 이미 켜져 있거나, 내가 오너가 아니면 무시
        }

        // ------------------------------------------------------------
        // 워치독: 외부 active 변화를 상태와 동기화
        // ------------------------------------------------------------
        if (!autoSyncWithTargets || toggleTargets == null || toggleTargets.Length == 0) return;

        bool anyActive = AnyTargetActive();

        if (_isPausedNow && !anyActive)
        {
            // 실제론 꺼졌는데 내부상태가 ON이면 복구
            ExitPause();
        }
        else if (!_isPausedNow && anyActive)
        {
            // 실제론 켜졌는데 내부상태가 OFF면 획득
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

    // =====================================================================
    // 토글/상태
    // =====================================================================

    /// <summary>토글 대상 중 하나라도 켜져 있는가</summary>
    private bool AnyTargetActive()
    {
        if (toggleTargets == null) return false;
        foreach (var go in toggleTargets)
            if (go && go.activeSelf) return true;
        return false;
    }

    /// <summary>토글: 켜기</summary>
    private void TurnOn()
    {
        // 대상 ON + CanvasGroup 동기화
        if (toggleTargets != null)
        {
            foreach (var go in toggleTargets)
            {
                if (!go) continue;
                if (!go.activeSelf) go.SetActive(true);
                ApplyCanvasGroup(go, true);
            }
        }

        // 배타 대상 강제 OFF
        if (forceOffOnOpen != null)
        {
            foreach (var go in forceOffOnOpen)
            {
                if (!go) continue;
                if (go.activeSelf) go.SetActive(false);
                ApplyCanvasGroup(go, false);
            }
        }

        // 전체 화면 블로커 ON
        if (fullScreenBlocker && !fullScreenBlocker.activeSelf)
            fullScreenBlocker.SetActive(true);

        // 일시정지/잠금 진입
        EnterPause();
    }

    /// <summary>토글: 끄기</summary>
    private void TurnOff()
    {
        // 대상 OFF + CanvasGroup 동기화
        if (toggleTargets != null)
        {
            foreach (var go in toggleTargets)
            {
                if (!go) continue;
                if (go.activeSelf) go.SetActive(false);
                ApplyCanvasGroup(go, false);
            }
        }

        // 전체 화면 블로커 OFF
        if (fullScreenBlocker && fullScreenBlocker.activeSelf)
            fullScreenBlocker.SetActive(false);

        // 일시정지/잠금 해제
        ExitPause();
    }

    /// <summary>일시정지/잠금 진입(중첩 안전)</summary>
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

    /// <summary>일시정지/잠금 해제(중첩 안전)</summary>
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

    /// <summary>
    /// CanvasGroup가 있으면 blocksRaycasts/interactable을 on/off에 맞게 조절.
    /// 부모 그룹을 무시하지 않도록 ignoreParentGroups=false로 둡니다.
    /// </summary>
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

        // 자동 수집: Player 태그 내부의 Animator
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

        // 수동 추가
        if (playerAnimators != null && playerAnimators.Length > 0)
        {
            foreach (var a in playerAnimators)
                if (a && !_animList.Contains(a))
                    _animList.Add(a);
        }
    }

    /// <summary>Animator 모두 일시정지(속도 0)</summary>
    private void PausePlayerAnimators()
    {
        if (_animList.Count == 0) BuildAnimatorList();
        foreach (var a in _animList)
            PauseCoordinator.LockAnimator(a);
    }

    /// <summary>Animator 모두 재개(저장된 속도로)</summary>
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

        // 자동 수집: Player 태그 내부의 PlayerMove / Rigidbody2D
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

        // 수동 추가(PlayerMove)
        if (playerMoves != null && playerMoves.Length > 0)
        {
            foreach (var p in playerMoves)
                if (p && !_playerMoveList.Contains(p))
                    _playerMoveList.Add(p);
        }
    }

    /// <summary>
    /// 플레이어 입력/이동 스크립트 비활성 + Rigidbody 정지.
    /// (주의) Rigidbody2D.bodyType 변경은 여기서 하지 않습니다. 필요 시 프로젝트 정책에 맞춰 추가하세요.
    /// </summary>
    private void LockPlayerControls()
    {
        if (_playerMoveList.Count == 0)
            BuildPlayerMoveList();

        // PlayerMove 비활성
        foreach (var pm in _playerMoveList)
            PauseCoordinator.LockPlayer(pm);

        // 물리 정지
        foreach (var rb in _playerRigidbodies)
        {
            if (!rb) continue;
            rb.linearVelocity = Vector2.zero; // Unity 6: velocity/linearVelocity 병행 가능
            rb.angularVelocity = 0f;
        }
    }

    /// <summary>플레이어 입력/이동 스크립트 재개.</summary>
    private void UnlockPlayerControls()
    {
        foreach (var pm in _playerMoveList)
            PauseCoordinator.UnlockPlayer(pm);
    }

    // =====================================================================
    // ESC 입력 헬퍼
    // =====================================================================
    /// <summary>
    /// 모달(escBlockerWhileActive)이 활성일 때는 ESC 입력을 무시합니다.
    /// </summary>
    private bool _IsEscBlockedByModal()
        => escBlockerWhileActive && escBlockerWhileActive.activeSelf;

    /// <summary>
    /// ESC가 이번 프레임에 눌렸는지 확인.
    /// 새 Input System 사용 시 ENABLE_INPUT_SYSTEM 상수를 기반으로 처리합니다.
    /// </summary>
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
