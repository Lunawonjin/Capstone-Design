using UnityEngine;

/// <summary>
/// 장애물 공통 이동 및 소멸(풀 반환) 담당 스크립트
/// </summary>
public class ObstacleMover : MonoBehaviour
{
    public enum ObstacleType { Log, Mud, Rock }   // ★ Rock 추가

    [HideInInspector] public ObstacleSpawnerManager owner; // 자신을 관리하는 매니저
    [HideInInspector] public float moveSpeed = 2f;         // 아래로 흐르는 속도(매니저가 설정)
    [HideInInspector] public ObstacleType type;            // 자신의 타입(Log/Mud/Rock)

    public float despawnY = -11f;                          // 이 y 이하로 내려가면 비활성화

    private Transform tr;

    void Awake()
    {
        tr = transform;
    }

    void Update()
    {
        // 매 프레임 아래로 이동
        tr.Translate(Vector3.down * moveSpeed * Time.deltaTime, Space.World);

        // 화면 아래로 벗어나면 풀로 반환
        if (tr.position.y <= despawnY)
        {
            if (owner != null) owner.Despawn(this);
            else gameObject.SetActive(false);
        }
    }
}
