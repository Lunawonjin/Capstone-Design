using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TogglePanelWithPause (fixed with global lock counting + target watchdog)
///
/// - 전역 락 카운트로 시간/오디오/입력/애니메이터를 관리(여러 버튼 혼용 안전)
/// - 워치독: 외부에서 토글 대상이 꺼지거나 켜져도 상태를 자동 동기화
/// </summary>
[DisallowMultipleComponent]
public class TogglePanelWithPause : MonoBehaviour
{
    // ===== 전역 코디네이터 =====
    private static class PauseCoordinator
    {
        private static int _timeLocks = 0;
        private static float _savedTimeScale = 1f;

        private static int _audioLocks = 0;
        private static bool _savedAudioPaused = false;

        private class BoolLock { public int count; public bool saved; }
        private static readonly Dictionary<Behaviour, BoolLock> _behaviourLocks = new Dictionary<Behaviour, BoolLock>();
        private static readonly Dictionary<PlayerMove, BoolLock> _playerMoveLocks = new Dictionary<PlayerMove, BoolLock>();

        private class AnimLock { public int count; public float savedSpeed; }
        private static readonly Dictionary<Animator, AnimLock> _animLocks = new Dictionary<Animator, AnimLock>();

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
            if (b == null) return;
            if (!_behaviourLocks.TryGetValue(b, out var s))
            {
                s = new BoolLock { count = 0, saved = b.enabled };
                _behaviourLocks[b] = s;
            }
            s.count++;
            b.enabled = false;
        }
        public static void UnlockBehaviour(Behaviour b)
        {
            if (b == null) return;
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
            if (pm == null) return;
            if (!_playerMoveLocks.TryGetValue(pm, out var s))
            {
                s = new BoolLock { count = 0, saved = pm.enabled };
                _playerMoveLocks[pm] = s;
            }
            s.count++;
            pm.enabled = false;
        }
        public static void UnlockPlayer(PlayerMove pm)
        {
            if (pm == null) return;
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
            if (a == null) return;
            if (!_animLocks.TryGetValue(a, out var s))
            {
                s = new AnimLock { count = 0, savedSpeed = a.speed };
                _animLocks[a] = s;
            }
            s.count++;
            a.speed = 0f;
        }
        public static void UnlockAnimator(Animator a)
        {
            if (a == null) return;
            if (!_animLocks.TryGetValue(a, out var s)) return;
            s.count = Mathf.Max(0, s.count - 1);
            if (s.count == 0)
            {
                a.speed = s.savedSpeed;
                _animLocks.Remove(a);
            }
        }
    }
    // ===== 전역 코디네이터 끝 =====

    [Header("Required")]
    [SerializeField] private Button button;

    [Header("Toggle Targets")]
    [SerializeField] private GameObject[] toggleTargets;

    [Header("Force OFF When Opening")]
    [SerializeField] private GameObject[] forceOffOnOpen;

    [Header("Pause Options")]
    [SerializeField] private bool pauseGameTime = true;
    [SerializeField] private bool pauseAudioOnPause = true;

    [Tooltip("토글 ON 동안 비활성화할 컴포넌트(입력 스크립트 등)")]
    [SerializeField] private Behaviour[] optionalDisableOnPause;

    [Header("Player Controls Lock")]
    [SerializeField] private bool autoFindPlayerMove = true;
    [SerializeField] private PlayerMove[] playerMoves;

    [Header("Player Animation Pause")]
    [SerializeField] private bool autoFindPlayerAnimators = true;
    [SerializeField] private Animator[] playerAnimators;

    [Header("UI Raycast Blocking")]
    [SerializeField] private bool useCanvasGroupIfAvailable = true;
    [SerializeField] private GameObject fullScreenBlocker;

    [Header("Watchdog")]
    [Tooltip("외부에서 대상 켜짐/꺼짐이 바뀌면 자동으로 Enter/ExitPause 동기화")]
    [SerializeField] private bool autoSyncWithTargets = true;

    private bool _isPausedNow = false;

    private readonly List<Animator> _animList = new List<Animator>(8);
    private readonly List<PlayerMove> _playerMoveList = new List<PlayerMove>(2);
    private readonly List<Rigidbody2D> _playerRigidbodies = new List<Rigidbody2D>(2);

    private void Reset()
    {
        pauseGameTime = true;
        pauseAudioOnPause = true;
        useCanvasGroupIfAvailable = true;
        autoFindPlayerAnimators = true;
        autoFindPlayerMove = true;
        autoSyncWithTargets = true;
    }

    private void Awake()
    {
        if (button == null)
            Debug.LogWarning("[TogglePanelWithPause] Button not assigned.", this);

        if (toggleTargets == null || toggleTargets.Length == 0)
            Debug.LogWarning("[TogglePanelWithPause] toggleTargets is empty.", this);

        BuildAnimatorList();
        BuildPlayerMoveList();
    }

    private void OnEnable()
    {
        if (button != null)
            button.onClick.AddListener(OnButtonClicked);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(OnButtonClicked);

        // 켜진 채 비활성화되면 안전 복구
        if (_isPausedNow)
            ExitPause();
    }

    // ★ 워치독: 외부에서 대상의 active 상태가 바뀌어도 자동 동기화
    private void Update()
    {
        if (!autoSyncWithTargets || toggleTargets == null || toggleTargets.Length == 0) return;

        bool anyActive = AnyTargetActive();

        // 대상이 모두 꺼졌는데 아직 잠금 상태면 해제
        if (_isPausedNow && !anyActive)
        {
            ExitPause();
        }
        // 대상이 켜졌는데 아직 잠금 안 걸렸으면 획득
        else if (!_isPausedNow && anyActive)
        {
            EnterPause();
        }
    }

    private void OnButtonClicked()
    {
        bool toActive = !AnyTargetActive();
        if (toActive) TurnOn();
        else TurnOff();
    }

    private bool AnyTargetActive()
    {
        if (toggleTargets == null) return false;
        foreach (var go in toggleTargets)
        {
            if (go != null && go.activeSelf) return true;
        }
        return false;
    }

    private void TurnOn()
    {
        if (toggleTargets != null)
        {
            foreach (var go in toggleTargets)
            {
                if (go == null) continue;
                if (!go.activeSelf) go.SetActive(true);
                ApplyCanvasGroup(go, true);
            }
        }

        if (forceOffOnOpen != null)
        {
            foreach (var go in forceOffOnOpen)
            {
                if (go == null) continue;
                if (go.activeSelf) go.SetActive(false);
                ApplyCanvasGroup(go, false);
            }
        }

        if (fullScreenBlocker != null && !fullScreenBlocker.activeSelf)
            fullScreenBlocker.SetActive(true);

        EnterPause();
    }

    private void TurnOff()
    {
        if (toggleTargets != null)
        {
            foreach (var go in toggleTargets)
            {
                if (go == null) continue;
                if (go.activeSelf) go.SetActive(false);
                ApplyCanvasGroup(go, false);
            }
        }

        if (fullScreenBlocker != null && fullScreenBlocker.activeSelf)
            fullScreenBlocker.SetActive(false);

        ExitPause();
    }

    private void EnterPause()
    {
        if (_isPausedNow) return;

        if (pauseGameTime) PauseCoordinator.AcquireTime();
        if (pauseAudioOnPause) PauseCoordinator.AcquireAudio();

        if (optionalDisableOnPause != null)
        {
            foreach (var b in optionalDisableOnPause)
                PauseCoordinator.LockBehaviour(b);
        }

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
        {
            foreach (var b in optionalDisableOnPause)
                PauseCoordinator.UnlockBehaviour(b);
        }

        if (pauseGameTime) PauseCoordinator.ReleaseTime();
        if (pauseAudioOnPause) PauseCoordinator.ReleaseAudio();

        _isPausedNow = false;
    }

    private void ApplyCanvasGroup(GameObject go, bool on)
    {
        if (!useCanvasGroupIfAvailable || go == null) return;
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.blocksRaycasts = on;
        cg.interactable = on;
        cg.ignoreParentGroups = false;
    }

    // ===== Animator =====
    private void BuildAnimatorList()
    {
        _animList.Clear();

        if (autoFindPlayerAnimators)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
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
                if (a != null && !_animList.Contains(a))
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

    // ===== PlayerMove =====
    private void BuildPlayerMoveList()
    {
        _playerMoveList.Clear();
        _playerRigidbodies.Clear();

        if (autoFindPlayerMove)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var found = player.GetComponentsInChildren<PlayerMove>(true);
                if (found != null && found.Length > 0)
                    _playerMoveList.AddRange(found);

                var rb = player.GetComponentInChildren<Rigidbody2D>(true);
                if (rb != null) _playerRigidbodies.Add(rb);
            }
            else
            {
                Debug.LogWarning("[TogglePanelWithPause] Player tag not found for PlayerMove.", this);
            }
        }

        if (playerMoves != null && playerMoves.Length > 0)
        {
            foreach (var p in playerMoves)
                if (p != null && !_playerMoveList.Contains(p))
                    _playerMoveList.Add(p);
        }
    }

    private void LockPlayerControls()
    {
        if (_playerMoveList.Count == 0) BuildPlayerMoveList();

        foreach (var pm in _playerMoveList)
            PauseCoordinator.LockPlayer(pm);

        foreach (var rb in _playerRigidbodies)
        {
            if (rb == null) continue;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    private void UnlockPlayerControls()
    {
        foreach (var pm in _playerMoveList)
            PauseCoordinator.UnlockPlayer(pm);
    }
}
