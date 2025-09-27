using UnityEngine;
using UnityEngine.Rendering; // SortingGroup 대응(있으면 사용)

/// <summary>
/// LayerAdjuster
/// - 가장 가까운 NPC를 기준으로: 내가 더 아래(Y가 작음)이면 그 NPC보다 위에 보이도록 sortingOrder를 조정.
/// - 비교 기준은 SpriteRenderer.bounds.min.y(스프라이트 하단) → pivot 차이로 인한 어색함 방지.
/// - 성능: NPC 재탐색은 interval로 제한.
/// </summary>
[DisallowMultipleComponent]
public class LayerAdjuster : MonoBehaviour
{
    [Header("탐색 설정")]
    [SerializeField] private string npcTag = "NPC";
    [SerializeField, Min(0.05f)] private float findInterval = 0.25f; // NPC 재탐색 주기
    [SerializeField, Min(0f)] private float searchRadius = 0f;       // 0이면 전역 탐색

    [Header("정렬 옵션")]
    [SerializeField] private int aboveOffset = 1;   // 내가 더 아래면: NPC + aboveOffset
    [SerializeField] private int belowOffset = -1;  // 내가 더 위면: NPC + belowOffset
    [SerializeField] private int baseOrder = 0;     // 전체 보정(타일맵/배경과 간극 벌릴 때)

    private SpriteRenderer selfSR;
    private Transform closestNpc;
    private float findTimer;

    void Awake()
    {
        selfSR = GetComponent<SpriteRenderer>();
        if (!selfSR) Debug.LogWarning("[LayerAdjuster] SpriteRenderer가 필요합니다.", this);
    }

    void Update()
    {
        // 주기적으로만 NPC 재탐색
        findTimer -= Time.unscaledDeltaTime;
        if (findTimer <= 0f)
        {
            findTimer = findInterval;
            FindClosestNPC();
        }

        if (!selfSR || !closestNpc) return;

        // 비교 기준: 스프라이트 "하단" y
        float myY = GetBottomY(selfSR);
        var (npcOrder, npcBottomY) = GetNpcOrderAndBottomY(closestNpc);

        // 내가 더 아래(작은 y)면 위에 보이도록 NPC보다 큰 order 부여
        if (myY < npcBottomY)
            selfSR.sortingOrder = npcOrder + aboveOffset + baseOrder;
        else
            selfSR.sortingOrder = npcOrder + belowOffset + baseOrder;
    }

    private void FindClosestNPC()
    {
        Transform nearest = null;
        float best = float.PositiveInfinity;

        if (searchRadius > 0f)
        {
            // 반경 내 Collider2D만 훑기(있으면 성능 유리)
            var hits = Physics2D.OverlapCircleAll(transform.position, searchRadius);
            foreach (var h in hits)
            {
                if (!h || !h.CompareTag(npcTag)) continue;
                float d = (h.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = h.transform; }
            }
        }
        else
        {
            // 전역 탐색(주기 제한으로 비용 절감)
            var npcs = GameObject.FindGameObjectsWithTag(npcTag);
            foreach (var go in npcs)
            {
                if (!go) continue;
                float d = (go.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = go.transform; }
            }
        }

        closestNpc = nearest;
    }

    // NPC의 표시 order와 하단 y를 가져온다(SortingGroup 우선)
    private (int order, float bottomY) GetNpcOrderAndBottomY(Transform npc)
    {
        // SortingGroup이 있으면 그 값을 따르기
        var grp = npc.GetComponentInChildren<SortingGroup>(true);
        if (grp)
        {
            // 하단 y는 대표 SpriteRenderer에서 구함
            var sr = npc.GetComponentInChildren<SpriteRenderer>(true);
            float by = sr ? GetBottomY(sr) : npc.position.y;
            return (grp.sortingOrder, by);
        }

        // 없으면 SpriteRenderer 기준
        var npcSR = npc.GetComponentInChildren<SpriteRenderer>(true);
        if (npcSR)
            return (npcSR.sortingOrder, GetBottomY(npcSR));

        // 그래도 없으면 Transform.y로 폴백
        return (0, npc.position.y);
    }

    private static float GetBottomY(SpriteRenderer sr)
    {
        return sr ? sr.bounds.min.y : 0f;
    }

#if UNITY_EDITOR
    // 탐색 반경 시각화
    private void OnDrawGizmosSelected()
    {
        if (searchRadius > 0f)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, searchRadius);
        }
    }
#endif
}
