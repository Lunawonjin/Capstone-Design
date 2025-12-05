using UnityEngine;

[DisallowMultipleComponent]
public class SyringeProjectile : MonoBehaviour
{
    [Header("기본 이동 설정")]
    [SerializeField] private float defaultSpeed = 12f;      // 기본 속도
    [SerializeField] private float defaultGravity = 9.8f;   // 기본 중력(포물선)
    [SerializeField] private int defaultDamage = 1;         // 기본 데미지
    [SerializeField] private float lifeTime = 4f;           // 유지 시간(조금 더 길게)

    [Header("포물선 샷 설정")]
    [Tooltip("포물선 샷일 때 위로 줄 초기 속도(점프력 느낌)")]
    [SerializeField] private float initialUpVelocity = 4.5f;

    [Header("태그 설정")]
    [Tooltip("이 태그는 충돌을 무시합니다.(보통 Player)")]
    [SerializeField] private string ignoreTag = "Player";

    [Tooltip("피격 판정에 사용할 적 태그")]
    [SerializeField] private string enemyTag = "Enemy";

    [Header("스프라이트 회전")]
    [Tooltip("이동 방향에 더해줄 추가 회전각(도 단위). 세로 스프라이트를 가로로 눕히고 싶으면 90 또는 -90")]
    [SerializeField] private float spriteAngleOffset = 90f;

    [Header("관통 설정")]
    [Tooltip("이 탄이 적을 관통하는지 여부")]
    [SerializeField] private bool canPierce = false;
    [Tooltip("관통 가능한 최대 적 수")]
    [SerializeField] private int maxPierceHits = 0;

    private float lifeTimer;
    private Vector2 velocity;   // 현재 속도 벡터
    private float gravity;      // 현재 적용 중인 중력 값
    private int damage = 1;     // 현재 탄의 데미지

    private int currentPierceHits = 0;
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

        currentPierceHits = 0;
    }

    // 예전 버전과의 호환용
    public void Launch(Vector2 dir)
    {
        Launch(dir, defaultSpeed, defaultGravity, defaultDamage, false, 0);
    }

    /// <summary>
    /// 새 버전: 속도, 중력, 데미지, 관통 여부까지 설정
    /// gravity > 0 이면 포물선, gravity == 0 이면 직선
    /// </summary>
    public void Launch(Vector2 dir, float speed, float gravityValue, int damageAmount, bool piercing, int maxPierce)
    {
        if (dir.sqrMagnitude < 1e-6f)
            dir = Vector2.right;
        dir = dir.normalized;

        gravity = gravityValue;
        damage = Mathf.Max(1, damageAmount);
        canPierce = piercing;
        maxPierceHits = Mathf.Max(0, maxPierce);
        currentPierceHits = 0;

        if (gravity > 0f)
        {
            // 포물선 샷: 수평 방향 + 위로 초기 속도
            Vector2 horiz = dir;
            horiz.y = 0f;
            if (horiz.sqrMagnitude < 1e-6f)
            {
                horiz = Vector2.right;
            }
            horiz = horiz.normalized;

            velocity = horiz * speed + Vector2.up * initialUpVelocity;
        }
        else
        {
            // 직선 샷
            velocity = dir * speed;
        }

        UpdateRotationByVelocity();
    }

    private void Update()
    {
        // 중력 적용
        if (gravity > 0f)
        {
            velocity.y -= gravity * Time.deltaTime;
        }

        // 이동
        transform.position += (Vector3)(velocity * Time.deltaTime);

        // 스프라이트 방향 보정
        UpdateRotationByVelocity();

        // 수명 체크
        lifeTimer += Time.deltaTime;
        if (lifeTimer >= lifeTime)
        {
            ReturnToPool();
        }
    }

    private void UpdateRotationByVelocity()
    {
        if (velocity.sqrMagnitude < 1e-6f) return;

        float baseAngle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        float finalAngle = baseAngle + spriteAngleOffset;
        transform.rotation = Quaternion.Euler(0f, 0f, finalAngle);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;

        // 플레이어는 무시
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
        bool hitEnemy = false;

        if (!string.IsNullOrEmpty(enemyTag) && hitObject.CompareTag(enemyTag))
        {
            // 적 스크립트 찾기
            SolsFinalGameEnemy enemy = hitObject.GetComponent<SolsFinalGameEnemy>();
            if (enemy == null)
                enemy = hitObject.GetComponentInParent<SolsFinalGameEnemy>();

            if (enemy != null)
            {
                // 데미지 수치만큼 여러 번 피격 전달
                for (int i = 0; i < damage; i++)
                {
                    enemy.TakeHit(hitPoint);
                }
                hitEnemy = true;
            }
        }

        if (hitEnemy && canPierce && currentPierceHits < maxPierceHits - 1)
        {
            // 관통 중: 카운트 올리고 그대로 진행
            currentPierceHits++;
            return;
        }

        // 그 외에는 회수
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
