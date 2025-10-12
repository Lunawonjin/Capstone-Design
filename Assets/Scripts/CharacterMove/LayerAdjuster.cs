using UnityEngine;
using UnityEngine.Rendering; // SortingGroup 대응(있으면 사용)

/// <summary>
/// LayerAdjuster
/// - 가장 가까운 NPC/Object를 기준으로: 내가 더 아래(Y가 작음)이면 그 대상보다 위에 보이도록 sortingOrder를 조정.
/// - 비교 기준은 SpriteRenderer.bounds.min.y(스프라이트 하단) → pivot 차이로 인한 어색함 방지.
/// - 성능: 대상 재탐색은 interval로 제한.
/// </summary>
[DisallowMultipleComponent]
public class LayerAdjuster : MonoBehaviour
{
    [Header("탐색 설정")]
    [SerializeField] private string npcTag = "NPC";
    [SerializeField] private string objectTag = "Object";   // 이 태그가 프로젝트에 등록 안되어 있어도 안전하게 동작
    [SerializeField, Min(0.05f)] private float findInterval = 0.25f; // 대상 재탐색 주기
    [SerializeField, Min(0f)] private float searchRadius = 0f;       // 0이면 전역 탐색

    [Header("정렬 옵션")]
    [SerializeField] private int aboveOffset = 1;   // 내가 더 아래면: 대상 + aboveOffset
    [SerializeField] private int belowOffset = -1;  // 내가 더 위면: 대상 + belowOffset
    [SerializeField] private int baseOrder = 0;     // 전체 보정(타일맵/배경과 간극 벌릴 때)

    private SpriteRenderer selfSR;
    private Transform closestTarget;
    private float findTimer;

    // objectTag가 미등록이면 한 번만 경고 띄우고 이후 조용히 스킵
    private bool _skipObjectTagGlobalSearch = false;

    void Awake()
    {
        selfSR = GetComponent<SpriteRenderer>();
        if (!selfSR) Debug.LogWarning("[LayerAdjuster] SpriteRenderer가 필요합니다.", this);
    }

    void Update()
    {
        // 주기적으로만 대상 재탐색
        findTimer -= Time.unscaledDeltaTime;
        if (findTimer <= 0f)
        {
            findTimer = findInterval;
            FindClosestTarget();
        }

        if (!selfSR || !closestTarget) return;

        // 비교 기준: 스프라이트 "하단" y
        float myY = GetBottomY(selfSR);
        var (tgtOrder, tgtBottomY) = GetTargetOrderAndBottomY(closestTarget);

        // 내가 더 아래(작은 y)면 위에 보이도록 대상보다 큰 order 부여
        if (myY < tgtBottomY)
            selfSR.sortingOrder = tgtOrder + aboveOffset + baseOrder;
        else
            selfSR.sortingOrder = tgtOrder + belowOffset + baseOrder;
    }

    private void FindClosestTarget()
    {
        Transform nearest = null;
        float best = float.PositiveInfinity;

        if (searchRadius > 0f)
        {
            // 반경 내 Collider2D만 훑기(있으면 성능 유리)
            var hits = Physics2D.OverlapCircleAll(transform.position, searchRadius);
            foreach (var h in hits)
            {
                if (!h) continue;
                if (!IsTargetTag(h.transform)) continue;

                float d = (h.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = h.transform; }
            }
        }
        else
        {
            // 전역 탐색(주기 제한으로 비용 절감)
            // NPC 모음
            if (!string.IsNullOrEmpty(npcTag))
            {
                // npcTag는 보통 등록되어 있다고 가정
                TryAccumulateByTag(npcTag, ref nearest, ref best);
            }

            // Object 모음 — 미등록일 수 있으므로 안전하게 시도
            if (!string.IsNullOrEmpty(objectTag) && !_skipObjectTagGlobalSearch)
            {
                if (!TryAccumulateByTag(objectTag, ref nearest, ref best))
                {
                    // 태그 미등록이면 이후부터는 조용히 스킵
                    _skipObjectTagGlobalSearch = true;
#if UNITY_EDITOR
                    Debug.LogWarning($"[LayerAdjuster] Tag '{objectTag}' is not defined. Global search for this tag will be skipped.");
#endif
                }
            }
        }

        closestTarget = nearest;
    }

    /// <summary>
    /// 주어진 태그로 전역 검색을 시도. 태그 미등록이면 false 리턴(예외 내부 처리).
    /// </summary>
    private bool TryAccumulateByTag(string tag, ref Transform nearest, ref float bestSqrDist)
    {
        try
        {
            var gos = GameObject.FindGameObjectsWithTag(tag);
            foreach (var go in gos)
            {
                if (!go) continue;
                float d = (go.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqrDist) { bestSqrDist = d; nearest = go.transform; }
            }
            return true;
        }
        catch (UnityException)
        {
            // 태그 미등록
            return false;
        }
    }

    private bool IsTargetTag(Transform t)
    {
        if (!t) return false;
        // CompareTag 대신 문자열 비교 → 태그 미등록이어도 안전
        var tg = t.tag;
        return (!string.IsNullOrEmpty(npcTag) && tg == npcTag)
            || (!string.IsNullOrEmpty(objectTag) && tg == objectTag);
    }

    // 대상의 표시 order와 하단 y를 가져온다(SortingGroup 우선)
    private (int order, float bottomY) GetTargetOrderAndBottomY(Transform target)
    {
        // SortingGroup이 있으면 그 값을 따르기
        var grp = target.GetComponentInChildren<SortingGroup>(true);
        if (grp)
        {
            // 하단 y는 대표 SpriteRenderer에서 구함
            var sr = target.GetComponentInChildren<SpriteRenderer>(true);
            float by = sr ? GetBottomY(sr) : target.position.y;
            return (grp.sortingOrder, by);
        }

        // 없으면 SpriteRenderer 기준
        var tgtSR = target.GetComponentInChildren<SpriteRenderer>(true);
        if (tgtSR)
            return (tgtSR.sortingOrder, GetBottomY(tgtSR));

        // 그래도 없으면 Transform.y로 폴백
        return (0, target.position.y);
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
