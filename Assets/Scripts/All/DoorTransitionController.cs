using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방 <-> 길 거리 전환 컨트롤러
/// - RoomDoor(문) 트리거에 플레이어가 겹쳐있는 동안 S 키 → 길(Road)로 이동
/// - RoadDoor(문) 트리거에 플레이어가 겹쳐있는 동안 F 키 → 방(Room)으로 이동
/// - 전환 시 페이드: 페이드 인(검정) → 좌표/루트/카메라 토글 → 즉시(또는 매우 빠르게) 페이드 아웃
/// - ⚠️ DataManager.nowPlayer.StartGame == true 일 때만 작동(옵션)
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

    // ── 텔레포트 페이드 옵션 ─────────────────────────────
    [Header("텔레포트 페이드")]
    [Tooltip("플레이어 강제 이동 시 화면을 페이드로 덮고 걷어냅니다.")]
    public bool useTeleportFade = true;

    [Tooltip("페이드 인(0→1) 시간(초). 검은 화면을 올리는 시간.")]
    [Min(0.01f)] public float fadeInDuration = 0.25f;

    [Tooltip("페이드 아웃(1→0) 시간(초). 즉시 걷어내려면 0 또는 매우 작게.")]
    [Min(0f)] public float fadeOutDuration = 0.001f;

    [Tooltip("페이드 색상")]
    public Color fadeColor = Color.black;
    // ───────────────────────────────────────────────────

    [Header("디버그")]
    public bool verbose = false;

    // 내부 상태
    bool _isTeleporting = false;

    void Reset()
    {
        goRoadKey = KeyCode.S;
        goRoomKey = KeyCode.F;
        toRoadPosition = new Vector2(-6f, -18.3f);
        toRoomPosition = new Vector2(1.5f, -2.3f);
        requireStartGame = true;

        useTeleportFade = true;
        fadeInDuration = 0.25f;
        fadeOutDuration = 0.001f; // 요청: 즉시 걷어내기
        fadeColor = Color.black;
    }

    void Awake()
    {
        AutoBind();
        EnsureTriggers();

        if (useTeleportFade)
            ScreenFader.Initialize(fadeColor);
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
        if (_isTeleporting) return;

        if (requireStartGame && !IsStartGameTrue()) return;

        // 겹침 판정
        bool overlapRoom = roomDoor && IsOverlapping(roomDoor, playerCollider);
        bool overlapRoad = roadDoor && IsOverlapping(roadDoor, playerCollider);

        // 겹치고 있는 동안 누르고 있으면 처리(GetKey) — 타이밍 문제 방지
        if (overlapRoom && Input.GetKey(goRoadKey))
        {
            if (verbose) Debug.Log("[DoorTransition] RoomDoor + S → Road");
            StartCoroutine(Co_Teleport(toRoadPosition, roomActive: false, roadActive: true));
        }

        if (overlapRoad && Input.GetKey(goRoomKey))
        {
            if (verbose) Debug.Log("[DoorTransition] RoadDoor + F → Room");
            StartCoroutine(Co_Teleport(toRoomPosition, roomActive: true, roadActive: false));
        }
    }

    bool IsStartGameTrue()
    {
        var dm = DataManager.instance;
        if (dm == null || dm.nowPlayer == null) return false;
        return dm.nowPlayer.StartGame == true; // 오타 방지
    }

    // 트리거-겹침 간이 판정(트리거/일반 모두 지원)
    bool IsOverlapping(Collider2D a, Collider2D b)
    {
        if (!a || !b) return false;
        return a.bounds.Intersects(b.bounds);
    }

    // 물리 안전 텔레포트: Rigidbody2D 우선, 속도 초기화
    void TeleportPlayer(Vector2 target)
    {
        if (!player) return;

        var rb = player.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.position = target;   // 고정 프레임 반영
            rb.WakeUp();

            // Z 유지 보정(카메라 레이어 등)
            var pz = player.position.z;
            player.position = new Vector3(target.x, target.y, pz);
        }
        else
        {
            var pz = player.position.z;
            player.position = new Vector3(target.x, target.y, pz);
        }
    }

    // 활성화 토글: 도착지 먼저 켜고 → 이동 → 출발지 끄기(깜빡임/유실 방지)
    System.Collections.IEnumerator Co_Teleport(Vector2 targetPos, bool roomActive, bool roadActive)
    {
        _isTeleporting = true;

        if (useTeleportFade)
            yield return ScreenFader.Instance.FadeTo(1f, Mathf.Max(0.01f, fadeInDuration), fadeColor);

        // 도착지 루트/카메라 먼저 ON
        if (roadActive && roadRoot && !roadRoot.activeSelf) roadRoot.SetActive(true);
        if (roomActive && roomRoot && !roomRoot.activeSelf) roomRoot.SetActive(true);
        if (roadActive && roadCamera && !roadCamera.activeSelf) roadCamera.SetActive(true);
        if (roomActive && roomCamera && !roomCamera.activeSelf) roomCamera.SetActive(true);

        // 좌표 이동
        TeleportPlayer(targetPos);

        // 물리/카메라 갱신 여유
        yield return new WaitForFixedUpdate();
        yield return null;

        // 출발지 OFF
        if (!roomActive && roomRoot) roomRoot.SetActive(false);
        if (!roadActive && roadRoot) roadRoot.SetActive(false);
        if (!roomActive && roomCamera) roomCamera.SetActive(false);
        if (!roadActive && roadCamera) roadCamera.SetActive(false);

        if (useTeleportFade)
            yield return ScreenFader.Instance.FadeTo(0f, Mathf.Max(0f, fadeOutDuration), fadeColor);

        _isTeleporting = false;
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

