using UnityEngine;

/// <summary>
/// 플레이어를 부드럽게 따라다니는 간단한 카메라 스크립트
/// </summary>
public class SimpleCameraFollow : MonoBehaviour
{
    [Header("추적 대상")]
    [Tooltip("카메라가 따라다닐 대상 (보통 플레이어)")]
    [SerializeField] private Transform target;

    [Header("카메라 설정")]
    [Tooltip("카메라 추적 속도 (높을수록 빠름, 5~15 권장)")]
    [SerializeField] private float followSpeed = 10f;

    [Tooltip("카메라 오프셋 (플레이어 기준 상대 위치)")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("제한 설정")]
    [Tooltip("X축 이동 제한 활성화")]
    [SerializeField] private bool limitX = false;
    [SerializeField] private float minX = -100f;
    [SerializeField] private float maxX = 100f;

    [Tooltip("Y축 이동 제한 활성화")]
    [SerializeField] private bool limitY = false;
    [SerializeField] private float minY = -100f;
    [SerializeField] private float maxY = 100f;

    [Header("추적 모드")]
    [Tooltip("X축만 따라다니기 (Y축 고정)")]
    [SerializeField] private bool followXOnly = true; // ⭐ 기본값 true

    [Tooltip("Y축만 따라다니기 (X축 고정)")]
    [SerializeField] private bool followYOnly = false;

    private Vector3 velocity = Vector3.zero;
    private float fixedY; // ⭐ Y축 고정값 저장

    void Start()
    {
        // 타겟이 없으면 플레이어 자동 찾기
        if (target == null)
        {
            PlayerMover playerMover = FindFirstObjectByType<PlayerMover>();
            if (playerMover != null)
            {
                target = playerMover.transform;
                Debug.Log($"[SimpleCameraFollow] 플레이어 자동 찾기 성공: {target.name}");
            }
            else
            {
                Debug.LogWarning("[SimpleCameraFollow] 추적할 대상을 찾을 수 없습니다!");
            }
        }

        // ⭐ 현재 카메라 Y 위치 저장 (고정용)
        fixedY = transform.position.y;
        Debug.Log($"[SimpleCameraFollow] 카메라 Y축 고정: {fixedY}");
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 목표 위치 계산
        Vector3 targetPos = target.position + offset;

        // 추적 모드에 따라 축 고정
        if (followXOnly)
        {
            targetPos.y = fixedY; // ⭐ 저장된 Y 위치 사용
        }
        else if (followYOnly)
        {
            targetPos.x = transform.position.x;
        }

        // 부드럽게 이동 (SmoothDamp 사용)
        Vector3 smoothPos = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref velocity,
            1f / followSpeed
        );

        // 제한 적용
        if (limitX)
        {
            smoothPos.x = Mathf.Clamp(smoothPos.x, minX, maxX);
        }

        if (limitY)
        {
            smoothPos.y = Mathf.Clamp(smoothPos.y, minY, maxY);
        }

        // ⭐ Y축은 항상 고정 (X축만 추적 모드)
        if (followXOnly)
        {
            smoothPos.y = fixedY;
        }

        // 카메라 위치 업데이트
        transform.position = smoothPos;
    }

    // ========================================
    // 🔹 Public 메서드
    // ========================================

    /// <summary>
    /// 추적 대상 변경
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    /// <summary>
    /// 추적 속도 변경
    /// </summary>
    public void SetFollowSpeed(float speed)
    {
        followSpeed = Mathf.Max(0.1f, speed);
    }

    /// <summary>
    /// 오프셋 변경
    /// </summary>
    public void SetOffset(Vector3 newOffset)
    {
        offset = newOffset;
    }

    /// <summary>
    /// X축 제한 설정
    /// </summary>
    public void SetXLimit(bool enable, float min, float max)
    {
        limitX = enable;
        minX = min;
        maxX = max;
    }

    /// <summary>
    /// Y축 제한 설정
    /// </summary>
    public void SetYLimit(bool enable, float min, float max)
    {
        limitY = enable;
        minY = min;
        maxY = max;
    }

    /// <summary>
    /// 카메라를 즉시 타겟 위치로 이동 (부드러운 이동 없이)
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null) return;
        Vector3 snapPos = target.position + offset;

        if (followXOnly)
        {
            snapPos.y = fixedY;
        }

        transform.position = snapPos;
    }

    /// <summary>
    /// Y축 고정값 재설정
    /// </summary>
    public void ResetFixedY()
    {
        fixedY = transform.position.y;
        Debug.Log($"[SimpleCameraFollow] Y축 고정값 재설정: {fixedY}");
    }
}