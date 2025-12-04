using UnityEngine;

[DisallowMultipleComponent]
public class SyringeProjectile : MonoBehaviour
{
    [Header("이동 설정")]
    [SerializeField] private float speed = 8f;      // 이동 속도
    [SerializeField] private float lifeTime = 3f;   // 유지 시간(초)

    [Header("태그 설정")]
    [Tooltip("이 태그는 충돌을 무시합니다.(보통 Player)")]
    [SerializeField] private string ignoreTag = "Player";

    [Tooltip("피격 판정에 사용할 적 태그")]
    [SerializeField] private string enemyTag = "Enemy";

    [Header("스프라이트 회전")]
    [Tooltip("이동 방향에 더해줄 추가 회전각(도 단위). 세로 스프라이트를 가로로 눕히고 싶으면 90 또는 -90")]
    [SerializeField] private float spriteAngleOffset = 90f;

    private float lifeTimer;
    private Vector2 moveDir = Vector2.right;
    private SyringePoolShooter owner;

    public void Init(SyringePoolShooter pool)
    {
        owner = pool;
    }

    private void OnEnable()
    {
        lifeTimer = 0f;

        Vector3 p = transform.position;
        p.z = 0f;
        transform.position = p;
    }

    public void Launch(Vector2 dir)
    {
        if (dir.sqrMagnitude < 1e-6f)
            moveDir = Vector2.right;
        else
            moveDir = dir.normalized;

        float baseAngle = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg;
        float finalAngle = baseAngle + spriteAngleOffset;
        transform.rotation = Quaternion.Euler(0f, 0f, finalAngle);
    }

    private void Update()
    {
        transform.position += (Vector3)(moveDir * speed * Time.deltaTime);

        lifeTimer += Time.deltaTime;
        if (lifeTimer >= lifeTime)
        {
            ReturnToPool();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;

        if (!string.IsNullOrEmpty(ignoreTag) && other.CompareTag(ignoreTag))
            return;

        HandleHit(other.gameObject, other.ClosestPoint(transform.position));
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null) return;

        if (!string.IsNullOrEmpty(ignoreTag) && collision.collider.CompareTag(ignoreTag))
            return;

        Vector2 hitPoint = collision.GetContact(0).point;
        HandleHit(collision.collider.gameObject, hitPoint);
    }

    private void HandleHit(GameObject hitObject, Vector2 hitPoint)
    {
        // 적 태그면 적 스크립트에 피격 전달
        if (!string.IsNullOrEmpty(enemyTag) && hitObject.CompareTag(enemyTag))
        {
            SolsFinalGameEnemy enemy = hitObject.GetComponent<SolsFinalGameEnemy>();
            if (enemy == null)
                enemy = hitObject.GetComponentInParent<SolsFinalGameEnemy>();

            if (enemy != null)
            {
                enemy.TakeHit(hitPoint);
            }
        }

        // 발사체는 항상 회수
        ReturnToPool();
    }

    private void ReturnToPool()
    {
        if (owner != null)
            owner.ReturnProjectile(this);
        else
            gameObject.SetActive(false);
    }
}
