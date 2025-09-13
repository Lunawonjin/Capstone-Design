using UnityEngine;

/// <summary>
/// DoorTriggerSwitch
///
/// 목적
/// - Player가 문(Door)에 "닿는 순간"에만 맵(Map)을 켭니다.
/// - 맵을 닫아도 Player가 여전히 Door에 붙어 있으면 다시 켜지는 문제를 방지합니다.
///   핵심은 "Player가 실제로 문에서 떨어질 때까지는 다시 트리거되지 않도록 잠금(latch) 처리"입니다.
///
/// 핵심 아이디어
/// - _waitingForExit: 한 번 맵을 켠 뒤에는 Player가 문 콜라이더에서 "Exit" 이벤트를 보낼 때까지
///   재트리거를 금지합니다.
/// - 즉, 맵을 껐다 켜더라도 Player가 문 위에 계속 서 있으면 더 이상 발동하지 않습니다.
/// - Player가 문에서 떨어져(OnTriggerExit/OnCollisionExit) _waitingForExit이 풀려야만
///   다시 문에 닿을 때(Enter) 맵이 켜집니다.
///
/// 특징
/// - 2D/3D, Trigger/Collision 모두 지원
/// - Character Room 비활성화, Map 활성화는 그대로 유지
/// - oneShot=false(기본): Player가 문에서 떨어졌다가 다시 닿으면 반복 사용 가능
///
/// 사용법
/// 1) Door 오브젝트에 Collider(2D/3D) 추가(Trigger 권장).
/// 2) Player 오브젝트 Tag="Player".
/// 3) characterRoomRoot=현재 방, mapRoot=맵 UI 루트 할당.
/// </summary>
public class DoorTriggerSwitch : MonoBehaviour
{
    [Header("Object References")]
    [SerializeField] private GameObject characterRoomRoot; // SetActive(false) 대상(현재 방 루트)
    [SerializeField] private GameObject mapRoot;           // SetActive(true) 대상(맵 루트)

    [Header("Tags")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string doorTag = "Door"; // 선택 검증용

    [Header("Behavior")]
    [Tooltip("true면 최초 1회만 동작(전역 1회). false면 '문에서 떨어진 뒤' 다시 닿으면 재실행.")]
    [SerializeField] private bool oneShot = false;

    [Tooltip("전환 전 참조 유효성 검사 수행")]
    [SerializeField] private bool validateBeforeSwitch = true;

    // 내부 상태
    private bool _switchedOnce = false;   // oneShot 모드에서 최초 1회 실행 여부
    private bool _playerInside = false;   // 현재 Player가 문과 접촉 중인지
    private bool _waitingForExit = false; // 맵을 켠 뒤 Player가 실제로 문에서 떨어질 때까지 재트리거 금지

    private void Awake()
    {
        // 태그 확인(선택)
        if (!string.IsNullOrEmpty(doorTag) && !CompareTag(doorTag))
        {
            Debug.LogWarning($"[DoorTriggerSwitch] 이 오브젝트의 Tag가 '{doorTag}'가 아닙니다. 현재 Tag='{tag}'.", this);
        }
        // 참조 확인
        if (characterRoomRoot == null)
            Debug.LogWarning("[DoorTriggerSwitch] characterRoomRoot 미할당.", this);
        if (mapRoot == null)
            Debug.LogWarning("[DoorTriggerSwitch] mapRoot 미할당.", this);
    }

    private void OnEnable()
    {
        // Door가 재활성화되어도 '기다리는 중' 상태는 유지해야 함
        // 그래야 여전히 문 위에 서 있는 플레이어로 인해 즉시 재트리거되지 않음.
        // _playerInside는 상황에 따라 남아있을 수 있으나, 재트리거는 _waitingForExit가 막음.
    }

    #region 2D Enter / Exit
    private void OnTriggerEnter2D(Collider2D other) { TryHandleEnter(other.gameObject); }
    private void OnCollisionEnter2D(Collision2D col) { TryHandleEnter(col.gameObject); }
    private void OnTriggerExit2D(Collider2D other) { TryHandleExit(other.gameObject); }
    private void OnCollisionExit2D(Collision2D col) { TryHandleExit(col.gameObject); }
    #endregion

    #region 3D Enter / Exit
    private void OnTriggerEnter(Collider other) { TryHandleEnter(other.gameObject); }
    private void OnCollisionEnter(Collision col) { TryHandleEnter(col.gameObject); }
    private void OnTriggerExit(Collider other) { TryHandleExit(other.gameObject); }
    private void OnCollisionExit(Collision col) { TryHandleExit(col.gameObject); }
    #endregion

    /// <summary>
    /// Player가 "닿는 순간"에만 실행.
    /// - _waitingForExit=true 이면(아직 문에서 떨어지지 않았다면) 재트리거 금지
    /// - _playerInside=true 이면(이미 접촉 상태로 판정되면) 무시
    /// </summary>
    private void TryHandleEnter(GameObject other)
    {
        if (!other || !other.CompareTag(playerTag)) return;

        // 이미 접촉 중이면(Enter 이후 Exit 전) 재실행 금지
        if (_playerInside) return;

        // 이전에 한 번 켰고 아직 문에서 떨어진 적이 없으면 재트리거 금지
        if (_waitingForExit) return;

        // oneShot 모드에서 이미 실행했다면 더 이상 실행 금지
        if (oneShot && _switchedOnce) return;

        // 유효성 검사
        if (validateBeforeSwitch && !IsValidToSwitch())
        {
            Debug.LogWarning("[DoorTriggerSwitch] 전환 불가: 참조 누락 또는 동일 오브젝트.", this);
            return;
        }

        // 접촉 시작 마킹
        _playerInside = true;

        // 실제 전환
        DoSwitch();

        // 여기서 중요한 점:
        // 이전 버전과 달리 접촉 플래그를 즉시 해제하지 않습니다.
        // 대신 _waitingForExit = true 로 잠금을 걸어
        // Player가 실제로 문에서 떨어질 때까지(Exit 발생) 재트리거를 막습니다.
        _waitingForExit = true;

        if (oneShot) _switchedOnce = true;
    }

    /// <summary>
    /// Player가 문에서 "떨어진 순간".
    /// - 이때 잠금 해제(_waitingForExit=false)하여 다음 Enter에서 다시 실행 가능.
    /// </summary>
    private void TryHandleExit(GameObject other)
    {
        if (!other || !other.CompareTag(playerTag)) return;

        _playerInside = false;

        // 실제로 떨어졌으니 다음 닿을 때 다시 켤 수 있도록 잠금 해제
        _waitingForExit = false;
    }

    private bool IsValidToSwitch()
    {
        if (characterRoomRoot == null || mapRoot == null) return false;
        if (characterRoomRoot == mapRoot) return false;
        return true;
    }

    /// <summary>
    /// 전환 로직:
    /// - characterRoomRoot.SetActive(false)
    /// - mapRoot.SetActive(true)
    /// </summary>
    private void DoSwitch()
    {
        if (characterRoomRoot != null && characterRoomRoot.activeSelf)
            characterRoomRoot.SetActive(false);

        if (mapRoot != null && !mapRoot.activeSelf)
            mapRoot.SetActive(true);

        Debug.Log("[DoorTriggerSwitch] 스위치: Character Room -> OFF, Map -> ON", this);
    }

    /// <summary>
    /// (선택) 외부에서 강제 전환
    /// - 강제 전환 후에도, Player가 문 위에 있다면 _waitingForExit=true를 유지해 재트리거를 막습니다.
    /// </summary>
    public void ForceSwitch()
    {
        if (validateBeforeSwitch && !IsValidToSwitch())
        {
            Debug.LogWarning("[DoorTriggerSwitch] 전환 불가(Force): 참조 누락 또는 동일 오브젝트.", this);
            return;
        }

        DoSwitch();
        _waitingForExit = true; // 강제 전환 시에도 실제 Exit까지 잠금 유지
        if (oneShot) _switchedOnce = true;
    }
}
