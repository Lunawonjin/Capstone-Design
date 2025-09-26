using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DoorTriggerSwitch_LatchedWithKey (Unity 6 / 6000.x)
///
/// 조건
/// - Player가 문과 "충돌 중"일 때 S키를 눌러야(Map 열기) 발동.
///   1) Enter 순간: S가 이미 눌린 상태면 즉시 발동(GetKey)
///   2) Stay 순간: 충돌 유지 중에 S를 "누르는 순간" 발동(GetKeyDown)
///
/// 래치
/// - 발동 후, Player가 문에서 실제로 "벗어날 때(겹침 해제)"까지 재트리거 금지.
/// - Exit 이벤트 유실을 대비해 "실제 겹침 해제"를 검사하는 코루틴을 전용 Runner에서 수행.
///
/// 권장
/// - Door 오브젝트는 characterRoomRoot 바깥 계층에 두는 것을 권장(물리 이벤트 유실 감소).
/// - 그래도 본 스크립트는 자체 겹침 검사로 안전하게 동작.
///
/// 사용법
/// 1) Door 오브젝트에 Collider(2D/3D) 추가(Trigger 권장).
/// 2) Player 오브젝트 Tag="Player".
/// 3) characterRoomRoot(현재 방 루트), mapRoot(맵 루트) 할당.
/// 4) openKey 기본 S.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Game/Door Trigger Switch (Latched + Key)")]
public sealed class DoorTriggerSwitch_LatchedWithKey : MonoBehaviour
{
    // ---------- 코루틴 전용 러너(절대 비활성화되지 않도록 유지) ----------
    private sealed class CoroutineRunner : MonoBehaviour
    {
        public Coroutine Run(IEnumerator r) => StartCoroutine(r);
    }

    private static CoroutineRunner _runner;
    private static CoroutineRunner EnsureRunner()
    {
        if (_runner != null) return _runner;
        var go = new GameObject("__DoorLatchRunner");
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<CoroutineRunner>();
        return _runner;
    }
    // -------------------------------------------------------------------

    [Header("Object References")]
    [SerializeField] private GameObject characterRoomRoot; // OFF 대상(현재 방 루트)
    [SerializeField] private GameObject mapRoot;           // ON 대상(맵 루트)

    [Header("Tags")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string doorTag = "Door";   // 선택 검증용(경고만)

    [Header("Key")]
    [Tooltip("문과 충돌 중 이 키를 눌러야 맵이 켜짐")]
    [SerializeField] private KeyCode openKey = KeyCode.S;

    [Header("Behavior")]
    [Tooltip("true면 최초 1회만 동작, false면 분리 후 재사용 가능")]
    [SerializeField] private bool oneShot = false;

    [Tooltip("전환 전 참조 유효성 검사 수행")]
    [SerializeField] private bool validateBeforeSwitch = true;

    [Header("Latch Options")]
    [Tooltip("스위치 직후, 분리 판정 시작까지 최소 대기(재활성 1~2틱 지나가게) - 실시간 기준")]
    [SerializeField, Min(0f)] private float minSeparationCheckDelay = 0.02f;

    [Tooltip("분리 대기 최대 시간(0=무한 대기). 예외 상황 무한 래치 방지용 - 실시간 기준")]
    [SerializeField, Min(0f)] private float maxWaitUntilSeparated = 0f;

    // 내부 상태
    private bool _switchedOnce = false;
    private bool _waitingForExit = false; // 래치: 실제 분리 전까지 재트리거 금지
    private bool _playerOverlapping = false; // 현재 프레임에 플레이어가 문과 겹침 상태인지(Stay 판단 보조)
    private Coroutine _waitCo = null;

    // 겹침 판정용 캐시
    private readonly List<Collider> _doorCols3D = new();
    private readonly List<Collider2D> _doorCols2D = new();
    private readonly List<Collider> _playerCols3D = new();
    private readonly List<Collider2D> _playerCols2D = new();

    private void Awake()
    {
        if (!string.IsNullOrEmpty(doorTag) && !CompareTag(doorTag))
            Debug.LogWarning($"[DoorSwitch] 이 오브젝트의 Tag가 '{doorTag}'가 아닙니다. 현재 '{tag}'", this);

        if (characterRoomRoot == null) Debug.LogWarning("[DoorSwitch] characterRoomRoot 미할당", this);
        if (mapRoot == null) Debug.LogWarning("[DoorSwitch] mapRoot 미할당", this);
        if (characterRoomRoot != null && mapRoot != null && characterRoomRoot == mapRoot)
            Debug.LogWarning("[DoorSwitch] characterRoomRoot와 mapRoot가 동일", this);
    }

    // ------------------- 2D Physics -------------------
    private void OnTriggerEnter2D(Collider2D other) => TryHandleEnter(other.gameObject);
    private void OnCollisionEnter2D(Collision2D col) => TryHandleEnter(col.gameObject);
    private void OnTriggerStay2D(Collider2D other) => TryHandleStay(other.gameObject);
    private void OnCollisionStay2D(Collision2D col) => TryHandleStay(col.gameObject);

    // ------------------- 3D Physics -------------------
    private void OnTriggerEnter(Collider other) => TryHandleEnter(other.gameObject);
    private void OnCollisionEnter(Collision col) => TryHandleEnter(col.gameObject);
    private void OnTriggerStay(Collider other) => TryHandleStay(other.gameObject);
    private void OnCollisionStay(Collision col) => TryHandleStay(col.gameObject);

    /// <summary>
    /// Enter: S가 이미 눌려 있는 상태(GetKey)라면 즉시 발동 시도.
    /// </summary>
    private void TryHandleEnter(GameObject otherGO)
    {
        if (!IsPlayer(otherGO)) return;
        _playerOverlapping = true; // Enter 프레임에도 겹침 true

        if (_waitingForExit) return;
        if (oneShot && _switchedOnce) return;

        // S가 눌린 채로 진입했을 때만 즉시 발동
        if (!Input.GetKey(openKey)) return;

        TryOpenMap(otherGO);
    }

    /// <summary>
    /// Stay: 겹침 유지 중 S를 "누르는 순간(GetKeyDown)" 발동.
    /// </summary>
    private void TryHandleStay(GameObject otherGO)
    {
        if (!IsPlayer(otherGO)) return;
        _playerOverlapping = true;

        if (_waitingForExit) return;
        if (oneShot && _switchedOnce) return;

        // 충돌 유지 중 S를 "누르는 순간"만 허용
        if (!Input.GetKeyDown(openKey)) return;

        TryOpenMap(otherGO);
    }

    private void LateUpdate()
    {
        // 프레임 끝에서 초기화(다음 프레임에 새로 Stay/Enter 없으면 false가 됨)
        _playerOverlapping = false;
    }

    /// <summary>
    /// 실제 맵 열기 시도(키 조건을 만족하고, 참조 유효하면 스위치 + 래치 코루틴 시작)
    /// </summary>
    private void TryOpenMap(GameObject playerGO)
    {
        if (validateBeforeSwitch && !IsValidToSwitch())
        {
            Debug.LogWarning("[DoorSwitch] 전환 불가: 참조 누락/동일 오브젝트", this);
            return;
        }

        // 래치 선설정 후, 전용 러너에서 코루틴 가동 → 그 다음 스위치
        _waitingForExit = true;
        if (oneShot) _switchedOnce = true;

        CacheDoorColliders();
        CachePlayerCollidersFrom(playerGO);

        if (_waitCo != null && _runner != null) _runner.StopCoroutine(_waitCo);
        _waitCo = EnsureRunner().Run(WaitUntilSeparatedThenUnlock());

        DoSwitch();
    }

    /// <summary>
    /// 외부 강제 전환(API): 충돌 중 S키 로직 없이 강제 오픈해야 할 때 사용.
    /// </summary>
    public void ForceSwitch(GameObject playerGO = null)
    {
        if (validateBeforeSwitch && !IsValidToSwitch())
        {
            Debug.LogWarning("[DoorSwitch] 전환 불가(Force): 참조 누락/동일 오브젝트", this);
            return;
        }

        _waitingForExit = true;
        if (oneShot) _switchedOnce = true;

        CacheDoorColliders();
        if (playerGO != null) CachePlayerCollidersFrom(playerGO);

        if (_waitCo != null && _runner != null) _runner.StopCoroutine(_waitCo);
        _waitCo = EnsureRunner().Run(WaitUntilSeparatedThenUnlock());

        DoSwitch();
    }

    // ---------------- Latch Routine ----------------
    private IEnumerator WaitUntilSeparatedThenUnlock()
    {
        // 실시간 대기: 맵 ON/일시정지 등 timeScale 영향 배제
        if (minSeparationCheckDelay > 0f)
            yield return new WaitForSecondsRealtime(minSeparationCheckDelay);

        float elapsed = 0f;
        while (true)
        {
            if (_playerCols3D.Count == 0 && _playerCols2D.Count == 0)
                TryPopulatePlayerCollidersByTag();

            if (IsStillOverlapping())
            {
                if (maxWaitUntilSeparated > 0f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    if (elapsed >= maxWaitUntilSeparated)
                    {
                        Debug.LogWarning("[DoorSwitch] 최대 대기 초과. 안전 해제.", this);
                        break;
                    }
                }
                yield return null;
            }
            else break;
        }

        _waitingForExit = false;
        _waitCo = null;
    }

    // ---------------- Overlap Checks ----------------
    private bool IsStillOverlapping()
    {
        // 3D: Bounds 교차(간단)
        foreach (var d in _doorCols3D)
        {
            if (!d) continue;
            foreach (var p in _playerCols3D)
            {
                if (!p) continue;
                if (d.bounds.Intersects(p.bounds)) return true;
            }
        }
        // 2D: Bounds 교차(간단)
        foreach (var d in _doorCols2D)
        {
            if (!d) continue;
            foreach (var p in _playerCols2D)
            {
                if (!p) continue;
                if (d.bounds.Intersects(p.bounds)) return true;
            }
        }
        return false;
    }

    // ---------------- Cache ----------------
    private void CacheDoorColliders()
    {
        _doorCols3D.Clear(); _doorCols2D.Clear();
        GetComponentsInChildren(true, _doorCols3D);
        GetComponentsInChildren(true, _doorCols2D);
    }

    private void CachePlayerCollidersFrom(GameObject playerGO)
    {
        _playerCols3D.Clear(); _playerCols2D.Clear();
        if (!playerGO) return;
        playerGO.GetComponentsInChildren(true, _playerCols3D);
        playerGO.GetComponentsInChildren(true, _playerCols2D);
    }

    private void TryPopulatePlayerCollidersByTag()
    {
        var player = GameObject.FindGameObjectWithTag(playerTag);
        if (player) CachePlayerCollidersFrom(player);
    }

    // ---------------- Helpers ----------------
    private bool IsPlayer(GameObject obj) => obj && obj.CompareTag(playerTag);

    private bool IsValidToSwitch()
    {
        if (characterRoomRoot == null || mapRoot == null) return false;
        if (characterRoomRoot == mapRoot) return false;
        return true;
    }

    private void DoSwitch()
    {
        if (characterRoomRoot && characterRoomRoot.activeSelf) characterRoomRoot.SetActive(false);
        if (mapRoot && !mapRoot.activeSelf) mapRoot.SetActive(true);
        Debug.Log("[DoorSwitch] Character Room → OFF, Map → ON", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (characterRoomRoot != null && mapRoot != null && characterRoomRoot == mapRoot)
            Debug.LogWarning("[DoorSwitch] characterRoomRoot와 mapRoot가 동일", this);
    }
#endif
}
