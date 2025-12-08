using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class SolsFinalGameBoss : MonoBehaviour
{
    [Header("보스 설정")]
    [SerializeField] private int maxHp = 50;
    [SerializeField] private int contactDamage = 5;
    [SerializeField] private float moveSpeed = 2f;
    [Tooltip("보스가 둥둥 떠다니는 Y축 이동 범위")]
    [SerializeField] private float floatAmplitude = 0.5f;
    [Tooltip("둥둥 떠다니는 속도")]
    [SerializeField] private float floatSpeed = 1f;

    [Header("HP 슬라이더")]
    [SerializeField] private Slider hpSlider;

    [Header("피격 효과")]
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float hitDuration = 0.5f;

    [Header("넛백 효과")]
    [SerializeField] private float knockbackForce = 5f;
    [SerializeField] private float knockbackDuration = 0.2f;

    [Header("연결")]
    [SerializeField] private SyringePoolShooter player;

    private Rigidbody2D rb;
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private int currentHp;
    private bool isDead = false;
    private Vector3 startPosition;
    private float floatTimer = 0f;
    private Coroutine hitRoutine;
    private bool isKnockback = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();

        if (spriteRenderers != null && spriteRenderers.Length > 0)
        {
            originalColors = new Color[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                originalColors[i] = spriteRenderers[i].color;
            }
        }

        // Rigidbody2D 설정
        if (rb != null)
        {
            rb.gravityScale = 0f; // 중력 무시
            rb.constraints = RigidbodyConstraints2D.FreezeRotation; // 회전 고정
        }

        // 플레이어 자동 찾기
        if (player == null)
        {
            player = FindFirstObjectByType<SyringePoolShooter>();
        }
    }

    void Start()
    {
        currentHp = maxHp;
        startPosition = transform.position;
        UpdateHPSlider();

        Debug.Log($"[SolsFinalGameBoss] 보스 생성 완료! HP: {currentHp}/{maxHp}");
    }

    void Update()
    {
        if (isDead || isKnockback) return;

        // 둥둥 떠다니는 효과 (사인파 이용)
        floatTimer += Time.deltaTime * floatSpeed;
        float yOffset = Mathf.Sin(floatTimer) * floatAmplitude;

        // X축으로 천천히 이동하면서 Y축 진동
        Vector3 targetPos = rb.position;
        targetPos.x -= moveSpeed * Time.deltaTime;
        targetPos.y = startPosition.y + yOffset;

        rb.MovePosition(targetPos);
    }

    /// <summary>
    /// 주사 피격 처리
    /// </summary>
    public void TakeHit(int damage)
    {
        if (isDead) return;

        currentHp -= damage;
        currentHp = Mathf.Max(0, currentHp);

        Debug.Log($"[SolsFinalGameBoss] 피격! 남은 HP: {currentHp}/{maxHp}");

        UpdateHPSlider();

        // 피격 효과
        if (hitRoutine != null) StopCoroutine(hitRoutine);
        hitRoutine = StartCoroutine(HitFlash());

        // 넛백 효과
        StartCoroutine(KnockbackEffect());

        // HP 0이면 사망
        if (currentHp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 피격 시 빨간색 깜빡임
    /// </summary>
    private IEnumerator HitFlash()
    {
        if (spriteRenderers == null) yield break;

        // 빨간색으로 변경
        foreach (var sr in spriteRenderers)
        {
            sr.color = hitColor;
        }

        yield return new WaitForSeconds(hitDuration);

        // 원래 색으로 복구
        if (!isDead && originalColors != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                spriteRenderers[i].color = originalColors[i];
            }
        }
    }

    /// <summary>
    /// 넛백 효과 (뒤로 밀려남)
    /// </summary>
    private IEnumerator KnockbackEffect()
    {
        isKnockback = true;

        // 오른쪽으로 밀려남 (주사가 왼쪽에서 날아온다고 가정)
        Vector2 knockbackDir = Vector2.right;
        rb.linearVelocity = knockbackDir * knockbackForce;

        yield return new WaitForSeconds(knockbackDuration);

        rb.linearVelocity = Vector2.zero;
        isKnockback = false;
    }

    /// <summary>
    /// HP 슬라이더 업데이트
    /// </summary>
    private void UpdateHPSlider()
    {
        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHp;
            hpSlider.value = currentHp;
        }
    }

    /// <summary>
    /// 보스 사망 처리
    /// </summary>
    private void Die()
    {
        if (isDead) return;
        isDead = true;

        Debug.Log("[SolsFinalGameBoss] 보스 사망!");

        // 엔딩 대사 시작
        if (SolsFinalGame.Instance != null)
        {
            SolsFinalGame.Instance.StartBossEndingSequence();
        }

        // 오브젝트 비활성화
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 플레이어와 충돌 시 데미지
    /// </summary>
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            if (player != null)
            {
                player.OnDamage(contactDamage, gameObject.tag);
                Debug.Log($"[SolsFinalGameBoss] 플레이어에게 {contactDamage} 데미지!");
            }
        }
    }

    /// <summary>
    /// 주사와 충돌 감지 (Trigger)
    /// ⭐ 태그 대신 GetComponent 사용
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isDead) return;

        // ⭐ GetComponent로 SyringeProjectile 직접 확인
        SyringeProjectile projectile = collision.GetComponent<SyringeProjectile>();
        if (projectile != null)
        {
            int damage = projectile.GetDamage();
            TakeHit(damage);

            Debug.Log($"[SolsFinalGameBoss] 주사 피격! 데미지: {damage}");
        }
    }
}