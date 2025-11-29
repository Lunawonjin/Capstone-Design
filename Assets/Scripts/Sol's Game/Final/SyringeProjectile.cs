using UnityEngine;

[DisallowMultipleComponent]
public class SyringeProjectile : MonoBehaviour
{
    [Header("Move")]
    [Tooltip("발사체 속도")]
    [SerializeField] private float speed = 8f;

    [Tooltip("살아있는 시간(초). 시간이 지나면 풀로 복귀")]
    [SerializeField] private float lifeTime = 3f;

    [Tooltip("Rigidbody2D가 있으면 물리로 이동")]
    [SerializeField] private bool useRigidbody = true;

    [Header("Hit")]
    [Tooltip("이 레이어와 충돌하면 풀로 복귀")]
    [SerializeField] private LayerMask hitLayers;

    private Rigidbody2D rb;
    private Vector2 dir;
    private float aliveTimer;

    private SyringePoolShooter ownerPool;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void OnEnable()
    {
        aliveTimer = 0f;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void Update()
    {
        aliveTimer += Time.deltaTime;
        if (aliveTimer >= lifeTime)
        {
            ReturnToPool();
            return;
        }

        if (!useRigidbody || rb == null)
        {
            transform.position += (Vector3)(dir * speed * Time.deltaTime);
        }
    }

    void FixedUpdate()
    {
        if (useRigidbody && rb != null)
        {
            rb.linearVelocity = dir * speed;
        }
    }

    public void Launch(Vector2 direction, SyringePoolShooter pool)
    {
        ownerPool = pool;

        if (direction.sqrMagnitude < 1e-6f)
            dir = Vector2.right;
        else
            dir = direction.normalized;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;
        if (IsInLayerMask(other.gameObject.layer, hitLayers))
        {
            ReturnToPool();
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null) return;
        if (IsInLayerMask(collision.collider.gameObject.layer, hitLayers))
        {
            ReturnToPool();
        }
    }

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        int bit = 1 << layer;
        return (mask.value & bit) != 0;
    }

    private void ReturnToPool()
    {
        if (ownerPool != null)
            ownerPool.ReturnProjectile(this);
        else
            gameObject.SetActive(false);
    }
}
