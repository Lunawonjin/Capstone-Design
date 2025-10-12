using UnityEngine;

/// <summary>
/// 방 <-> 길 거리 전환 컨트롤러
/// - RoomDoor(문) 트리거에 플레이어가 겹쳐있는 동안 S 키 → 길(Road)로 이동
/// - RoadDoor(문) 트리거에 플레이어가 겹쳐있는 동안 F 키 → 방(Room)으로 이동
/// - 전환 시 플레이어 좌표/오브젝트/카메라 활성 상태를 토글
/// - ⚠️ DataManager.nowPlayer.StartGame == true 일 때만 작동
/// </summary>
[DisallowMultipleComponent]
public class DoorTransitionController : MonoBehaviour
{
    [Header("필수 레퍼런스")]
    [Tooltip("플레이어 Transform (비우면 태그 Player로 자동 탐색)")]
    public Transform player;

    [Tooltip("플레이어의 Collider2D (비우면 자동 탐색)")]
    public Collider2D playerCollider;

    [Space(6)]
    [Tooltip("방 -> 길로 나가는 문(트리거 콜라이더)")]
    public Collider2D roomDoor;   // tag: Door (권장)
    [Tooltip("길 -> 방으로 들어오는 문(트리거 콜라이더)")]
    public Collider2D roadDoor;   // tag: Door (권장)

    [Header("전환 시 토글할 오브젝트/카메라")]
    public GameObject roomRoot;      // 방 루트 오브젝트
    public GameObject roadRoot;      // 길 루트 오브젝트
    public GameObject roomCamera;    // 방 카메라 오브젝트
    public GameObject roadCamera;    // 길 카메라 오브젝트

    [Header("이동 좌표")]
    [Tooltip("RoomDoor에서 S를 누를 때 이동시킬 좌표")]
    public Vector2 toRoadPosition = new Vector2(-6f, -18.3f);

    [Tooltip("RoadDoor에서 F를 누를 때 이동시킬 좌표")]
    public Vector2 toRoomPosition = new Vector2(1.5f, -2.3f);

    [Header("키 설정")]
    public KeyCode goRoadKey = KeyCode.S;
    public KeyCode goRoomKey = KeyCode.F;

    [Header("작동 조건")]
    [Tooltip("true면 StartGame이 true일 때만 전환이 동작합니다.")]
    public bool requireStartGame = true;

    [Header("디버그")]
    public bool verbose = false;

    void Reset()
    {
        goRoadKey = KeyCode.S;
        goRoomKey = KeyCode.F;
        toRoadPosition = new Vector2(-6f, -18.3f);
        toRoomPosition = new Vector2(1.5f, -2.3f);
        requireStartGame = true;
    }

    void Awake()
    {
        AutoBind();
        EnsureTriggers();
    }

    void AutoBind()
    {
        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) player = go.transform;
        }
        if (playerCollider == null && player != null)
            playerCollider = player.GetComponent<Collider2D>();
    }

    void EnsureTriggers()
    {
        if (roomDoor != null) roomDoor.isTrigger = true;
        if (roadDoor != null) roadDoor.isTrigger = true;
    }

    void Update()
    {
        if (player == null || playerCollider == null)
        {
            AutoBind();
            if (player == null || playerCollider == null) return;
        }

        // ── 여기가 핵심: StartGame 조건 체크 ──
        if (requireStartGame && !IsStartGameTrue())
            return;

        // RoomDoor 겹침 + S 키 → Road로
        if (roomDoor && IsOverlapping(roomDoor, playerCollider) && Input.GetKey(goRoadKey))
        {
            if (verbose) Debug.Log("[DoorTransition] RoomDoor + S → Road");
            TeleportPlayer(toRoadPosition);
            ToggleGroups(roomActive: false, roadActive: true);
        }

        // RoadDoor 겹침 + F 키 → Room으로
        if (roadDoor && IsOverlapping(roadDoor, playerCollider) && Input.GetKey(goRoomKey))
        {
            if (verbose) Debug.Log("[DoorTransition] RoadDoor + F → Room");
            TeleportPlayer(toRoomPosition);
            ToggleGroups(roomActive: true, roadActive: false);
        }
    }

    bool IsStartGameTrue()
    {
        // DataManager.nowPlayer.StartGame 이 true여야만 이동 허용
        var dm = DataManager.instance;
        if (dm == null || dm.nowPlayer == null) return false;
        return dm.nowPlayer.StartGame == true; // 'StarGame' 오타 방지
    }

    // 두 콜라이더가 겹치는지 간단히 검사(트리거도 동작)
    bool IsOverlapping(Collider2D a, Collider2D b)
    {
        if (!a || !b) return false;
        return a.bounds.Intersects(b.bounds);
    }

    void TeleportPlayer(Vector2 target)
    {
        if (!player) return;
        var p = player.position;
        player.position = new Vector3(target.x, target.y, p.z);
    }

    void ToggleGroups(bool roomActive, bool roadActive)
    {
        if (roomRoot) roomRoot.SetActive(roomActive);
        if (roadRoot) roadRoot.SetActive(roadActive);
        if (roomCamera) roomCamera.SetActive(roomActive);
        if (roadCamera) roadCamera.SetActive(roadActive);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (roomDoor) Gizmos.DrawWireCube(roomDoor.bounds.center, roomDoor.bounds.size);
        Gizmos.color = Color.yellow;
        if (roadDoor) Gizmos.DrawWireCube(roadDoor.bounds.center, roadDoor.bounds.size);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(new Vector3(toRoadPosition.x, toRoadPosition.y, 0f), 0.15f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(new Vector3(toRoomPosition.x, toRoomPosition.y, 0f), 0.15f);
    }
#endif
}
