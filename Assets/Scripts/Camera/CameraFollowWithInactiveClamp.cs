// CameraFollowWithInactiveClamp.cs
// 플레이어가 비활성화일 때만 카메라 X를 [-23.5, -14.5]로 제한해서 따라옴.
// 활성화일 때는 제한 없이 일반 추적.
// LateUpdate + SmoothDamp 사용.

using UnityEngine;

[DisallowMultipleComponent]
public class CameraFollowWithInactiveClamp : MonoBehaviour
{
    [Header("대상 (비우면 Tag: Player 자동 탐색)")]
    [SerializeField] private Transform target;
    [SerializeField] private string playerTag = "Player";

    [Header("추적 옵션")]
    [Tooltip("카메라가 대상 위치에 더해줄 오프셋")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 0f, -10f);
    [Tooltip("부드러운 추적 시간(낮을수록 즉각 반응)")]
    [SerializeField, Min(0f)] private float smoothTime = 0.15f;
    [Tooltip("Y축도 따라갈지 여부")]
    [SerializeField] private bool followY = true;

    [Header("플레이어 비활성화 시 X 제한")]
    [Tooltip("플레이어가 비활성화(activeInHierarchy==false)일 때 적용되는 X 최소값")]
    [SerializeField] private float inactiveClampMinX = -23.5f;
    [Tooltip("플레이어가 비활성화(activeInHierarchy==false)일 때 적용되는 X 최대값")]
    [SerializeField] private float inactiveClampMaxX = -14.5f;

    // 내부 상태
    private Vector3 _velocity = Vector3.zero;
    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (target == null && !string.IsNullOrEmpty(playerTag))
        {
            try
            {
                var go = GameObject.FindGameObjectWithTag(playerTag);
                if (go != null) target = go.transform;
            }
            catch { /* 태그가 없을 수도 있으니 조용히 무시 */ }
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // 대상의 활성 여부 판단
        bool isTargetActive = target.gameObject.activeInHierarchy;

        // 기본 목표 위치(대상 + 오프셋)
        Vector3 desired = target.position + followOffset;

        // 플레이어가 비활성화라면 X만 범위로 클램프
        if (!isTargetActive)
        {
            desired.x = Mathf.Clamp(desired.x, inactiveClampMinX, inactiveClampMaxX);
        }

        // Y축 추적 끄기 옵션
        if (!followY)
        {
            desired.y = transform.position.y;
        }

        // Z는 카메라 유지(오프셋 z가 -10 같은 값으로 고정되도록)
        if (_cam != null)
        {
            // orthographic/3D 상관 없이, transform의 z는 desired.z를 사용
            // (followOffset.z로 제어)
        }

        // 부드럽게 이동
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // X 클램프 구간 가시화 (씬 뷰에서 확인용)
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.35f);
        float minX = inactiveClampMinX;
        float maxX = inactiveClampMaxX;

        // 카메라 현재 Y에 맞춘 세로선 두 개
        Vector3 a1 = new Vector3(minX, transform.position.y - 100f, 0f);
        Vector3 a2 = new Vector3(minX, transform.position.y + 100f, 0f);
        Vector3 b1 = new Vector3(maxX, transform.position.y - 100f, 0f);
        Vector3 b2 = new Vector3(maxX, transform.position.y + 100f, 0f);

        Gizmos.DrawLine(a1, a2);
        Gizmos.DrawLine(b1, b2);
    }
#endif
}
