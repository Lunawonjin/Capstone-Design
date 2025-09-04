using UnityEngine;

/// <summary>
/// ĳ���� �ֺ� Ÿ�� ������ �����,
/// NPC�� BoxCollider2D�� "�����̶�" Ÿ���� ������ �����ϴ� ��ũ��Ʈ.
/// (2D Ÿ�ϸʿ�, Collider �ʼ�)
/// </summary>
public class CharacterSpace : MonoBehaviour
{
    [Header("Ÿ�� ����")]
    [Tooltip("���� ���� ������ (����: �ȼ� �Ǵ� ����)")]
    public float xRadius = 5.0f;

    [Tooltip("���� ���� ������ (����: �ȼ� �Ǵ� ����)")]
    public float yRadius = 2.5f;

    [Header("Ÿ�� ��ġ ���� (Offset)")]
    [Tooltip("ĳ���� �������� Ÿ�� �߽��� �󸶳� �̵����� ���� (��: �߹� �̵�)")]
    public Vector2 offset = new Vector2(0f, -0.5f);

    private Vector2 focus1, focus2; // Ÿ���� �� ���� ��ǥ

    void Update()
    {
        UpdateFoci();

        // F Ű�� ������ ���� ����
        if (Input.GetKeyDown(KeyCode.F))
        {
            Collider2D[] colliders = FindObjectsOfType<Collider2D>();

            foreach (var col in colliders)
            {
                if (col == null) continue;
                if (!col.CompareTag("NPC")) continue; // "NPC" �±� ���� �͸�

                if (IsAnyPointInsideEllipse(col))
                {
                    Debug.Log($"{col.name}��(��) ��ȣ�ۿ�Ǿ����ϴ�!");
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
