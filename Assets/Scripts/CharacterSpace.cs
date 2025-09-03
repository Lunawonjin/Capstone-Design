using UnityEngine;

/// <summary>
/// 캐릭터 주변 타원 공간을 만들고,
/// NPC의 BoxCollider2D가 "조금이라도" 타원에 닿으면 감지하는 스크립트.
/// (2D 타일맵용, Collider 필수)
/// </summary>
public class CharacterSpace : MonoBehaviour
{
    [Header("타원 설정")]
    [Tooltip("가로 방향 반지름 (단위: 픽셀 또는 유닛)")]
    public float xRadius = 5.0f;

    [Tooltip("세로 방향 반지름 (단위: 픽셀 또는 유닛)")]
    public float yRadius = 2.5f;

    [Header("타원 위치 보정 (Offset)")]
    [Tooltip("캐릭터 기준으로 타원 중심을 얼마나 이동할지 설정 (예: 발밑 이동)")]
    public Vector2 offset = new Vector2(0f, -0.5f);

    private Vector2 focus1, focus2; // 타원의 두 초점 좌표

    void Update()
    {
        UpdateFoci();

        // F 키가 눌렸을 때만 실행
        if (Input.GetKeyDown(KeyCode.F))
        {
            Collider2D[] colliders = FindObjectsOfType<Collider2D>();

            foreach (var col in colliders)
            {
                if (col == null) continue;
                if (!col.CompareTag("NPC")) continue; // "NPC" 태그 가진 것만

                if (IsAnyPointInsideEllipse(col))
                {
                    Debug.Log($"{col.name}와(과) 상호작용되었습니다!");
                }
            }
        }
    }

    private void UpdateFoci()
    {
        Vector2 center = (Vector2)transform.position + offset;
        float focalDistance = Mathf.Sqrt(Mathf.Abs(xRadius * xRadius - yRadius * yRadius));

        focus1 = center + new Vector2(-focalDistance, 0);
        focus2 = center + new Vector2(focalDistance, 0);
    }

    private bool IsAnyPointInsideEllipse(Collider2D col)
    {
        Bounds bounds = col.bounds;

        Vector2 topLeft = new Vector2(bounds.min.x, bounds.max.y);
        Vector2 topRight = new Vector2(bounds.max.x, bounds.max.y);
        Vector2 bottomLeft = new Vector2(bounds.min.x, bounds.min.y);
        Vector2 bottomRight = new Vector2(bounds.max.x, bounds.min.y);

        return IsInsideEllipse(topLeft) ||
               IsInsideEllipse(topRight) ||
               IsInsideEllipse(bottomLeft) ||
               IsInsideEllipse(bottomRight);
    }

    private bool IsInsideEllipse(Vector2 point)
    {
        Vector2 point2D = new Vector2(point.x, point.y);
        Vector2 center = (Vector2)transform.position + offset;

        float distanceSum = Vector2.Distance(point2D, focus1) + Vector2.Distance(point2D, focus2);
        return distanceSum <= 2f * xRadius;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        DrawApproxEllipse2D((Vector2)transform.position + offset, xRadius, yRadius);
    }

    private void DrawApproxEllipse2D(Vector2 center, float xRadius, float yRadius, int segments = 60)
    {
        Vector3 oldPoint = center + new Vector2(xRadius, 0);
        float angleDelta = 2 * Mathf.PI / segments;

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleDelta;
            Vector3 newPoint = center + new Vector2(Mathf.Cos(angle) * xRadius, Mathf.Sin(angle) * yRadius);

            Gizmos.DrawLine(oldPoint, newPoint);
            oldPoint = newPoint;
        }
    }
}
