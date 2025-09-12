using UnityEngine;

/// <summary>
/// DoorTriggerSwitch
/// 
/// 목적:
/// - 태그가 "Door"인 오브젝트(문)에 이 스크립트를 붙입니다.
/// - "Player" 태그를 가진 캐릭터가 문과 "닿을 때" (Trigger 또는 Collision) 
///   캐릭터 방(Character Room)을 비활성화하고, 맵(Map)을 활성화합니다.
/// 
/// 특징:
/// - 2D/3D 모두 지원: OnTriggerEnter2D / OnCollisionEnter2D / OnTriggerEnter / OnCollisionEnter
/// - Inspector에서 캐릭터방과 맵의 루트 GameObject를 연결하면 동작
/// - 안전장치: 중복 스위칭 방지, 누락 참조시 경고 로그
/// 
/// 사용법(Setup):
/// 1) 문 오브젝트에 Tag를 "Door"로 설정합니다.
/// 2) 문 오브젝트에 Collider(2D 또는 3D)를 붙입니다. 
///    - 권장: isTrigger 체크(Trigger) / Rigidbody는 선택(Trigger 안정성 위해 Kinematic 권장)
/// 3) 이 스크립트를 문 오브젝트에 추가합니다.
/// 4) Inspector에서 Character Room 루트, Map 루트에 각각 해당 오브젝트를 할당합니다.
/// 5) 플레이어 오브젝트의 Tag가 "Player"인지 확인합니다.
/// 
/// 주의:
/// - Character Room 및 Map은 "서로 다른 루트"여야 하며, SetActive로 전체 묶음을 켜고 끕니다.
/// - 한 번 스위치된 후에는 기본적으로 다시 실행되지 않습니다(oneShot).
/// </summary>
public class DoorTriggerSwitch : MonoBehaviour
{
    [Header("Object References")]
    [Tooltip("캐릭터방(현재 공간)의 루트 GameObject. 스위치 시 SetActive(false).")]
    [SerializeField] private GameObject characterRoomRoot;

    [Tooltip("Map(전환할 공간)의 루트 GameObject. 스위치 시 SetActive(true).")]
    [SerializeField] private GameObject mapRoot;

    [Header("Tags")]
    [Tooltip("플레이어에 사용할 태그명. 보통 기본값 'Player'.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("이 문 오브젝트가 가져야 할 태그명. 보통 기본값 'Door'.")]
    [SerializeField] private string doorTag = "Door";

    [Header("Behavior")]
    [Tooltip("true면 첫 실행 이후 재실행하지 않습니다. 중복 전환 방지용.")]
    [SerializeField] private bool oneShot = true;

    [Tooltip("전환 전에 간단한 유효성 검사를 수행합니다. 누락시 경고 로그 출력.")]
    [SerializeField] private bool validateBeforeSwitch = true;

    // 내부 상태: 이미 스위치했는지
    private bool _switched;

    private void Awake()
    {
        // 문 오브젝트 Tag 확인(선택적). 태그 불일치 시 작동은 가능하지만, 설정 오류를 빠르게 찾기 위해 경고 출력.
        if (!string.IsNullOrEmpty(doorTag) && !CompareTag(doorTag))
        {
            Debug.LogWarning($"[DoorTriggerSwitch] 이 오브젝트의 Tag가 '{doorTag}'가 아닙니다. 현재 Tag='{tag}'. " +
                             "의도한 문 오브젝트에 붙었는지 확인하세요.", this);
        }

        // 참조 누락 경고
        if (characterRoomRoot == null)
        {
            Debug.LogWarning("[DoorTriggerSwitch] Character Room Root 참조가 비었습니다. Inspector에서 할당하세요.", this);
        }
        if (mapRoot == null)
        {
            Debug.LogWarning("[DoorTriggerSwitch] Map Root 참조가 비었습니다. Inspector에서 할당하세요.", this);
        }
    }

    #region 2D Physics
    // 2D Trigger 진입
    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHandleEnter(other.gameObject);
    }

    // 2D Collision 진입
    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryHandleEnter(collision.gameObject);
    }
    #endregion

    #region 3D Physics
    // 3D Trigger 진입
    private void OnTriggerEnter(Collider other)
    {
        TryHandleEnter(other.gameObject);
    }

    // 3D Collision 진입
    private void OnCollisionEnter(Collision collision)
    {
        TryHandleEnter(collision.gameObject);
    }
    #endregion

    /// <summary>
    /// 공통 진입 처리: Player 태그인지 판별 후 전환 시도
    /// </summary>
    /// <param name="other">문에 닿은 상대 오브젝트</param>
    private void TryHandleEnter(GameObject other)
    {
        // 이미 전환했다면(oneShot) 재실행 막기
        if (_switched && oneShot) return;

        // Player 태그 판정
        if (!other || !other.CompareTag(playerTag)) return;

        // 전환 전 유효성 검사(선택)
        if (validateBeforeSwitch && !IsValidToSwitch())
        {
            Debug.LogWarning("[DoorTriggerSwitch] 전환 불가: 참조가 누락되었거나 동일 오브젝트입니다.", this);
            return;
        }

        // 실제 전환
        DoSwitch();

        // 상태 갱신
        if (oneShot) _switched = true;
    }

    /// <summary>
    /// 전환 가능한지 간단 검증
    /// - characterRoomRoot와 mapRoot가 유효한지
    /// - 동일 오브젝트를 가리키지 않는지
    /// </summary>
    private bool IsValidToSwitch()
    {
        if (characterRoomRoot == null || mapRoot == null) return false;
        if (characterRoomRoot == mapRoot) return false;
        return true;
    }

    /// <summary>
    /// 핵심 전환 로직:
    /// - 캐릭터방 비활성화(SetActive(false))
    /// - 맵 활성화(SetActive(true))
    /// </summary>
    private void DoSwitch()
    {
        // 캐릭터방 비활성화
        if (characterRoomRoot != null && characterRoomRoot.activeSelf)
        {
            characterRoomRoot.SetActive(false);
        }

        // 맵 활성화
        if (mapRoot != null && !mapRoot.activeSelf)
        {
            mapRoot.SetActive(true);
        }

        // 디버그 로그로 상태 확인
        Debug.Log("[DoorTriggerSwitch] 스위치 완료: Character Room -> OFF, Map -> ON", this);
    }

    /// <summary>
    /// (선택적) 코드/버튼으로 수동 트리거하고 싶을 때 호출
    /// </summary>
    public void ForceSwitch()
    {
        if (validateBeforeSwitch && !IsValidToSwitch())
        {
            Debug.LogWarning("[DoorTriggerSwitch] 전환 불가(Force): 참조 누락 또는 동일 오브젝트.", this);
            return;
        }
        DoSwitch();
        if (oneShot) _switched = true;
    }
}
