using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DoorTriggerSwitch_OpenOnTouchOrM (Unity 6 / 6000.x)
///
/// 목적
/// - 플레이어가 문과 "닿기만 하면" 맵을 연다(키 불필요).
/// - 또한 어디서든 M 키를 누르면 맵을 연다(전역 단축키).
/// - 맵 열기는 MapMenuController 있으면 OpenMap() 호출, 없으면 루트 ON/OFF로 폴백.
/// - 발동 뒤, 플레이어가 문에서 "실제 분리"될 때까지 재트리거 금지(래치).
/// - UIExclusiveManager와 연동해 폴백 전환 시에도 겹침 방지.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Game/Door Trigger Switch (Open On Touch or M)")]
public sealed class DoorTriggerSwitch : MonoBehaviour
{
    // 코루틴 전용 러너(씬 전환/비활성 영향 받지 않도록)
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

    [Header("References")]
    [Tooltip("맵 열기 애니메이션/논리를 담당하는 컨트롤러(있으면 이를 우선 사용)")]
    [SerializeField] private MapMenuController mapController;

    [Tooltip("MapMenuController가 없을 때 쓰는 폴백: 현재 방 루트(OFF 대상)")]
    [SerializeField] private GameObject characterRoomRoot;

    [Tooltip("MapMenuController가 없을 때 쓰는 폴백: 맵 루트(ON 대상)")]
    [SerializeField] private GameObject mapRoot;

    [Header("UI Exclusive Group (선택: 폴백 전환 시 겹침 방지)")]
    [SerializeField] private UIExclusiveManager uiGroup;

    [Header("Tags")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string doorTag = "Door"; // 선택적 검증용

    [Header("Global Open Key")]
    [Tooltip("전역 단축키(M). 어디서든 누르면 맵을 연다")]
    [SerializeField] private KeyCode globalOpenKey = KeyCode.M;

    [Header("Behavior")]
    [Tooltip("true면 최초 1회만 동작, false면 분리 후 재사용 가능")]
    [SerializeField] private bool oneShot = false;

    [Tooltip("전환 전 참조 유효성 검사 수행(MapMenuController 없을 때만 의미)")]
    [SerializeField] private bool validateBeforeSwitch = true;

    [Header("Latch Options")]
    [Tooltip("스위치 직후, 분리 판정 시작까지 최소 대기(실시간 기준)")]
    [SerializeField, Min(0f)] private float minSeparationCheckDelay = 0.02f;

    [Tooltip("분리 대기 최대 시간(0=무한 대기, 실시간 기준)")]
    [SerializeField, Min(0f)] private float maxWaitUntilSeparated = 0f;

    // 내부 상태
    private bool _switchedOnce = false;
    private bool _waitingForExit = false; // 실제 분리 전까지 재트리거 금지
    private Coroutine _waitCo = null;

    // 겹침 판정 캐시(래치 해제를 위해 필요)
    private readonly List<Collider> _doorCols3D = new();
    private readonly List<Collider2D> _doorCols2D = new();
    private readonly List<Collider> _playerCols3D = new();
    private readonly List<Collider2D> _playerCols2D = new();

    private void Awake()
    {
        if (!string.IsNullOrEmpty(doorTag) && !CompareTag(doorTag))
            Debug.LogWarning($"[DoorSwitch] 문 오브젝트 Tag가 '{doorTag}'가 아닙니다. 현재 '{tag}'", this);

        if (mapController == null && (characterRoomRoot == null || mapRoot == null))
            Debug.LogWarning("[DoorSwitch] MapMenuController 미할당 상태에서는 characterRoomRoot/mapRoot가 필요합니다.", this);

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

    private void Update()
    {
        // 전역 단축키(M)로 항상 맵 열기
        if (Input.GetKeyDown(globalOpenKey))
            TryOpenMapGlobal();
    }

    // ------------------- 2D Physics -------------------
    private void OnTriggerEnter2D(Collider2D other) => TryOpenOnTouch(other.gameObject);
    private void OnCollisionEnter2D(Collision2D col) => TryOpenOnTouch(col.gameObject);

    // ------------------- 3D Physics -------------------
    private void OnTriggerEnter(Collider other) => TryOpenOnTouch(other.gameObject);
    private void OnCollisionEnter(Collision col) => TryOpenOnTouch(col.gameObject);

    /// <summary>
    /// 플레이어가 문에 "닿는 순간" 맵 열기(키 요구 없음).
    /// 래치 활성 상태면 무시.
    /// </summary>
    private void TryOpenOnTouch(GameObject otherGO)
    {
        if (!IsPlayer(otherGO)) return;
        if (_waitingForExit) return;
        if (oneShot && _switchedOnce) return;

        TryOpenMapCore(otherGO);
    }

    /// <summary>
    /// 전역 M 키로 맵 열기(충돌 조건 무시).
    /// </summary>
    private void TryOpenMapGlobal()
    {
        if (_waitingForExit) return;
        if (oneShot && _switchedOnce) return;

        TryOpenMapCore(null);
    }

    /// <summary>
    /// 공통 맵 열기 루틴: 래치 설정 → 코루틴으로 분리 대기 → 맵 오픈 실행.
    /// </summary>
    private void TryOpenMapCore(GameObject playerGO)
    {
        _waitingForExit = true;
        if (oneShot) _switchedOnce = true;

        CacheDoorColliders();
        if (playerGO != null) CachePlayerCollidersFrom(playerGO);

        if (_waitCo != null && _runner != null) _runner.StopCoroutine(_waitCo);
        _waitCo = EnsureRunner().Run(WaitUntilSeparatedThenUnlock());

        DoOpen();
    }

    // ---------------- Latch Routine ----------------
    private IEnumerator WaitUntilSeparatedThenUnlock()
    {
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
                        Debug.LogWarning("[DoorSwitch] 분리 대기 최대 시간 초과. 래치 안전 해제.", this);
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
        foreach (var d in _doorCols3D)
        {
            if (!d) continue;
            foreach (var p in _playerCols3D)
            {
                if (!p) continue;
                if (d.bounds.Intersects(p.bounds)) return true;
            }
        }
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

    private void DoOpen()
    {
        // 1) MapMenuController가 있으면 그쪽에서 겹침을 이미 막아줌
        if (mapController != null)
        {
            mapController.OpenMap();
            return;
        }

        // 2) 폴백: 직접 SetActive로 켤 때도 겹침 방지
        if (validateBeforeSwitch)
        {
            if (characterRoomRoot == null || mapRoot == null || characterRoomRoot == mapRoot)
            {
                Debug.LogWarning("[DoorSwitch] 폴백 전환 불가: 참조 누락/동일 오브젝트", this);
                return;
            }
        }

        if (uiGroup != null)
        {
            if (!uiGroup.TryActivate(mapRoot))
            {
                // 다른 UI가 이미 켜져 있으므로 맵 오픈 거절
                return;
            }
        }

        if (characterRoomRoot && characterRoomRoot.activeSelf) characterRoomRoot.SetActive(false);
        if (mapRoot && !mapRoot.activeSelf) mapRoot.SetActive(true);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (mapController == null && characterRoomRoot != null && mapRoot != null && characterRoomRoot == mapRoot)
            Debug.LogWarning("[DoorSwitch] 폴백 전환에서 characterRoomRoot와 mapRoot가 동일합니다.", this);
    }
#endif
}
