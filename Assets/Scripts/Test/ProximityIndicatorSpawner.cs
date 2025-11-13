using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HoverIndicatorSpawner2D (Collision-only / Radius 둘 다 지원)
/// - 기본: 플레이어의 Collider2D와 실제 "충돌 중"인 NPC/Door에만 아이콘 표시
/// - 충돌이 끝나면 즉시 제거
/// - parentToTarget=true여도 inheritScaleFromTarget=false면 부모 스케일 무시(찌그러짐 방지)
/// - 예외 목록에 등록된 오브젝트는 생성 안 함
/// - 프리팹에 FloatBob2D가 자동으로 붙어 둥둥 떠다니게 함
/// </summary>
[DisallowMultipleComponent]
public class HoverIndicatorSpawner2D : MonoBehaviour
{
    [Header("플레이어/충돌소스")]
    public Transform player;                    // 비워두면 자신 transform
    public Collider2D playerCollider;           // 충돌 모드에서 사용. 비워두면 자동 GetComponentInParent

    [Header("탐지 모드")]
    public bool detectByCollisionOnly = true;   // true: 실제 충돌 중인 대상만 / false: 반경 스캔

    [Header("반경 스캔 설정 (detectByCollisionOnly=false일 때만 사용)")]
    [Min(0.05f)] public float scanInterval = 0.1f;
    [Min(0.1f)] public float scanRadius = 2.0f;

    [Header("공통 필터")]
    public LayerMask targetLayerMask = ~0;      // 대상 레이어
    public bool includeTriggers = true;         // 트리거 포함

    [Header("대상 태그(문자열 비교)")]
    public string npcTag = "NPC";
    public string doorTag = "Door";

    [Header("인디케이터 프리팹")]
    public GameObject indicatorPrefab;

    [Header("스폰 위치(타겟 기준)")]
    public Vector2 spawnOffset = new Vector2(-2f, 0f);

    [Header("부모/스케일")]
    public bool parentToTarget = true;
    [Tooltip("부모 스케일 그대로 물림. 끄면 1:1 보정(찌그러짐 방지)")]
    public bool inheritScaleFromTarget = false;

    [Header("둥둥 효과(FloatBob2D)")]
    public bool addFloatBob = true;
    public float bobAmplitude = 0.25f;
    public float bobFrequency = 1.2f;
    public bool bobUseUnscaledTime = true;
    public bool bobWorldSpaceWhenNotParented = true;

    [Header("한 타겟당 하나만")]
    public bool onePerTarget = true;

    [Header("생성 예외(이 오브젝트들에선 표시 안 함)")]
    public List<GameObject> exceptions = new();

    [Header("디버그 로그")]
    public bool debugLog = false;

    // 내부 상태
    private readonly Dictionary<Transform, GameObject> spawned = new(); // target -> indicator
    private readonly HashSet<Transform> currentTargets = new();
    private readonly List<Collider2D> hitsBuffer = new(64);
    private readonly List<Transform> toRemove = new();

    private float _scanTimer;

    private void Awake()
    {
        if (!player) player = transform;

        if (!playerCollider)
        {
            // 플레이어의 콜라이더 자동 탐색(자신 또는 부모)
            playerCollider = GetComponent<Collider2D>();
            if (!playerCollider) playerCollider = GetComponentInParent<Collider2D>();
        }

        if (!indicatorPrefab)
            Debug.LogWarning("[HoverIndicatorSpawner2D] indicatorPrefab이 비었습니다.");

        if (debugLog)
        {
            Debug.Log($"[HoverIndicatorSpawner2D] Awake - player={player}, playerCollider={playerCollider}, targetLayerMask={targetLayerMask.value}");
        }
    }

    private void Update()
    {
        if (detectByCollisionOnly)
        {
            ScanByCollision();
        }
        else
        {
            _scanTimer -= Time.unscaledDeltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = scanInterval;
                ScanByRadius();
            }
        }
    }

    // ──────────────────────────────────────────────
    // 충돌 기반 스캔: 실제로 닿아있는 콜라이더만 취급
    // ──────────────────────────────────────────────
    private void ScanByCollision()
    {
        currentTargets.Clear();

        if (!playerCollider)
        {
            // 콜라이더가 없으면 반경 스캔으로 폴백
            if (debugLog)
                Debug.LogWarning("[HoverIndicatorSpawner2D] playerCollider가 없어 Radius 스캔으로 폴백합니다.");
            ScanByRadius();
            return;
        }

        var filter = new ContactFilter2D
        {
            useTriggers = includeTriggers,
            useLayerMask = true,
            layerMask = targetLayerMask
        };

        hitsBuffer.Clear();
        // Unity 6에서는 List 버전 OverlapCollider 지원. 혹시 안 되면 배열 버전으로 변경 필요.
        playerCollider.Overlap(filter, hitsBuffer);

        if (debugLog)
            Debug.Log($"[HoverIndicatorSpawner2D] Collision 스캔 hitCount={hitsBuffer.Count}");

        for (int i = 0; i < hitsBuffer.Count; i++)
        {
            var col = hitsBuffer[i];
            if (!col) continue;

            var t = ResolveTargetRoot(col.transform);
            if (!t) continue;
            if (!HasTargetTag(t)) continue;
            if (IsInExceptions(t.gameObject)) continue;

            if (debugLog)
                Debug.Log($"[HoverIndicatorSpawner2D] Collision hit 대상: {t.name}, tag={t.tag}");

            currentTargets.Add(t);
        }

        SyncSpawnedWithCurrentTargets();
    }

    // ──────────────────────────────────────────────
    // 반경 스캔: 플레이어 주변을 OverlapCircle로 검색
    // ──────────────────────────────────────────────
    private void ScanByRadius()
    {
        currentTargets.Clear();

        if (!player)
        {
            if (debugLog)
                Debug.LogWarning("[HoverIndicatorSpawner2D] player Transform이 없습니다.");
            return;
        }

        var filter = new ContactFilter2D
        {
            useTriggers = includeTriggers,
            useLayerMask = true,
            layerMask = targetLayerMask
        };

        hitsBuffer.Clear();
        Physics2D.OverlapCircle(player.position, scanRadius, filter, hitsBuffer);

        if (debugLog)
            Debug.Log($"[HoverIndicatorSpawner2D] Radius 스캔 hitCount={hitsBuffer.Count}");

        for (int i = 0; i < hitsBuffer.Count; i++)
        {
            var col = hitsBuffer[i];
            if (!col) continue;

            var t = ResolveTargetRoot(col.transform);
            if (!t) continue;
            if (!HasTargetTag(t)) continue;
            if (IsInExceptions(t.gameObject)) continue;

            if (debugLog)
                Debug.Log($"[HoverIndicatorSpawner2D] Radius hit 대상: {t.name}, tag={t.tag}");

            currentTargets.Add(t);
        }

        SyncSpawnedWithCurrentTargets();
    }

    // 공통: currentTargets와 spawned를 동기화
    private void SyncSpawnedWithCurrentTargets()
    {
        // 새로 감지 → 스폰
        foreach (var t in currentTargets)
        {
            if (onePerTarget && spawned.ContainsKey(t)) continue;
            SpawnIndicator(t);
        }

        // 감지 안 된 것 → 디스폰
        toRemove.Clear();
        foreach (var kv in spawned)
            if (!currentTargets.Contains(kv.Key)) toRemove.Add(kv.Key);

        for (int i = 0; i < toRemove.Count; i++)
            DespawnIndicator(toRemove[i]);
    }

    private void SpawnIndicator(Transform target)
    {
        if (!indicatorPrefab || !target) return;

        GameObject inst;

        if (parentToTarget)
        {
            Vector3 worldPos = target.position + (Vector3)spawnOffset;
            inst = Instantiate(indicatorPrefab, worldPos, Quaternion.identity);
            inst.transform.SetParent(target, worldPositionStays: true);

            if (!inheritScaleFromTarget)
                ApplyKeepWorldScale(inst.transform); // 부모 스케일 무시
        }
        else
        {
            Vector3 pos = target.position + (Vector3)spawnOffset;
            inst = Instantiate(indicatorPrefab, pos, Quaternion.identity);
        }

        if (addFloatBob)
        {
            var bob = inst.GetComponent<FloatBob2D>();
            if (!bob) bob = inst.AddComponent<FloatBob2D>();
            bob.amplitude = bobAmplitude;
            bob.frequency = bobFrequency;
            bob.useUnscaledTime = bobUseUnscaledTime;
            bob.worldSpace = parentToTarget ? false : bobWorldSpaceWhenNotParented;
        }

        spawned[target] = inst;

        if (debugLog)
            Debug.Log($"[HoverIndicatorSpawner2D] 인디케이터 스폰: target={target.name}");
    }

    private void DespawnIndicator(Transform target)
    {
        if (spawned.TryGetValue(target, out var inst))
        {
            if (inst) Destroy(inst);
            spawned.Remove(target);

            if (debugLog)
                Debug.Log($"[HoverIndicatorSpawner2D] 인디케이터 제거: target={target.name}");
        }
    }

    /// <summary>
    /// 자식 콜라이더를 때렸더라도, 위로 타고 올라가면서 NPC/Door 태그가 붙은 부모를 찾아서 반환한다.
    /// 못 찾으면 최상위 루트 Transform을 반환한다.
    /// </summary>
    private Transform ResolveTargetRoot(Transform t)
    {
        if (!t) return null;

        Transform cur = t;

        // 먼저 위로 올라가면서 NPC/Door 태그가 있는 부모를 찾는다.
        while (cur != null)
        {
            string tag = cur.tag; // 등록 안 되면 "Untagged"
            bool isNpc = !string.IsNullOrWhiteSpace(npcTag) && tag == npcTag;
            bool isDoor = !string.IsNullOrWhiteSpace(doorTag) && tag == doorTag;

            if (isNpc || isDoor)
                return cur;

            cur = cur.parent;
        }

        // NPC/ Door 태그를 못 찾았다면, 원래 인자로 들어온 트랜스폼의 루트를 반환
        cur = t;
        while (cur.parent != null)
            cur = cur.parent;

        return cur;
    }

    private bool HasTargetTag(Transform t)
    {
        if (!t) return false;
        string tag = t.tag; // 미등록이면 "Untagged"
        bool isNpc = !string.IsNullOrWhiteSpace(npcTag) && tag == npcTag;
        bool isDoor = !string.IsNullOrWhiteSpace(doorTag) && tag == doorTag;
        return isNpc || isDoor;
    }

    private bool IsInExceptions(GameObject go)
    {
        if (!go || exceptions == null) return false;
        return exceptions.Contains(go);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!player) player = transform;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
        if (detectByCollisionOnly)
        {
            // 시각화만: 플레이어 콜라이더 AABB
            if (playerCollider)
            {
                var b = playerCollider.bounds;
                Gizmos.DrawWireCube(b.center, b.size);
            }
        }
        else
        {
            Gizmos.DrawWireSphere(player.position, scanRadius);
        }
    }
#endif

    // 부모 스케일 무시하고 월드 1:1 유지
    private static void ApplyKeepWorldScale(Transform child)
    {
        Vector3 w = child.lossyScale;
        var p = child.parent;
        if (!p) { child.localScale = Vector3.one; return; }

        Vector3 ps = p.lossyScale;
        float ix = Mathf.Approximately(ps.x, 0f) ? 1f : 1f / ps.x;
        float iy = Mathf.Approximately(ps.y, 0f) ? 1f : 1f / ps.y;
        float iz = Mathf.Approximately(ps.z, 0f) ? 1f : 1f / ps.z;
        child.localScale = new Vector3(w.x * ix, w.y * iy, w.z * iz);
    }
}

/// <summary> 간단한 위아래 보빙 </summary>
[DisallowMultipleComponent]
public class FloatBob2D : MonoBehaviour
{
    public float amplitude = 0.25f;
    public float frequency = 1.2f;
    public bool useUnscaledTime = true;
    public bool worldSpace = false;

    private Vector3 _basePos;

    private void OnEnable()
    {
        _basePos = worldSpace ? transform.position : transform.localPosition;
    }

    private void Update()
    {
        float t = useUnscaledTime ? Time.unscaledTime : Time.time;
        float y = Mathf.Sin(t * frequency * Mathf.PI * 2f) * amplitude;

        if (worldSpace)
        {
            var p = _basePos;
            p.y += y;
            transform.position = p;
        }
        else
        {
            var p = _basePos;
            p.y += y;
            transform.localPosition = p;
        }
    }
}
