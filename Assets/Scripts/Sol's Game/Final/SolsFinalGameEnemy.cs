using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class SolsFinalGameEnemy : MonoBehaviour
{
    public static int enemyCount = 0;

    [Header("Active Settings")]
    [SerializeField] private bool activateOnStart = true;

    [Header("Move Settings")]
    [SerializeField] private bool canMove = true;
    [SerializeField] private float xSpeed = 1.0f;
    [SerializeField] private float ySpeed = 1.0f;
    [SerializeField] private float yMax = 1.0f;
    [SerializeField] private float yMin = -1.8f;

    [Header("Health & Hit")]
    [SerializeField] private int hitsToDie = 5;
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float hitColorDuration = 0.1f;

    [Header("Boss Spawn Settings")]
    [SerializeField] private float bossSpawnDelay = 1f;

    private Rigidbody2D rb;
    private Animator anim;
    private int yDir;
    private bool isActive = false;
    private int currentHits = 0;
    private bool isQuitting = false;

    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private Coroutine hitColorRoutine;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        yDir = (Random.value < 0.5f) ? 1 : -1;

        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        if (spriteRenderers != null && spriteRenderers.Length > 0)
        {
            originalColors = new Color[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
                originalColors[i] = spriteRenderers[i].color;
        }
    }

    void OnEnable()
    {
        isActive = activateOnStart;
        currentHits = 0;

        if (CompareTag("Enemy")) enemyCount++;

        if (originalColors != null && spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
                spriteRenderers[i].color = originalColors[i];
        }

        if (rb != null)
        {
#if UNITY_2022_2_OR_NEWER
            rb.linearVelocity = Vector2.zero;
#else
            rb.velocity = Vector2.zero;
#endif
            rb.angularVelocity = 0f;
            rb.gravityScale = 0f;
        }
    }

    void OnApplicationQuit() => isQuitting = true;

    void OnDisable()
    {
        if (isQuitting || !gameObject.scene.isLoaded) return;
        if (CompareTag("Enemy"))
        {
            enemyCount--;
            CheckAllEnemiesDead();
        }
    }

    private void CheckAllEnemiesDead()
    {
        if (enemyCount <= 0)
        {
            Debug.Log("[CheckAllEnemiesDead] 모든 적 처치! 보스 소환 시작");

            // 간단한 시퀀스 실행
            GameObject sequenceRunner = new GameObject("BossSpawnSequenceRunner");
            BossSpawnSequence sequence = sequenceRunner.AddComponent<BossSpawnSequence>();
            sequence.StartSequence(bossSpawnDelay);
        }
    }

    void Update()
    {
        if (!isActive) return;
    }

    void FixedUpdate()
    {
        if (!isActive) return;
        if (rb != null) MoveRigidbody(Time.fixedDeltaTime);
    }

    private void MoveRigidbody(float dt)
    {
        if (!canMove)
        {
#if UNITY_2022_2_OR_NEWER
            rb.linearVelocity = Vector2.zero;
#else
            rb.velocity = Vector2.zero;
#endif
            return;
        }

        Vector2 p = rb.position;
        p.x -= xSpeed * dt;
        p.y += ySpeed * yDir * dt;
        if (yDir > 0 && p.y >= yMax) { p.y = yMax; yDir = -1; }
        else if (yDir < 0 && p.y <= yMin) { p.y = yMin; yDir = 1; }
        rb.MovePosition(p);
    }

    public void Activate() { isActive = true; }

    public void Deactivate()
    {
        isActive = false;
        if (rb != null)
        {
#if UNITY_2022_2_OR_NEWER
            rb.linearVelocity = Vector2.zero;
#else
            rb.velocity = Vector2.zero;
#endif
        }
    }

    public void TakeHit(Vector2 hitPosition)
    {
        TakeHit(hitPosition, 1);
    }

    public void TakeHit(Vector2 hitPosition, int damage)
    {
        if (!isActive) return;

        int add = Mathf.Max(1, damage);
        currentHits += add;

        if (anim != null) anim.SetTrigger("Hit");

        if (hitColorRoutine != null) StopCoroutine(hitColorRoutine);
        hitColorRoutine = StartCoroutine(HitColorFlashRoutine());

        if (currentHits >= hitsToDie)
            gameObject.SetActive(false);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isActive) return;
        if (collision.gameObject.CompareTag("Player"))
        {
            SyringePoolShooter player = collision.gameObject.GetComponent<SyringePoolShooter>();
            if (player != null) player.OnDamage(1, gameObject.tag);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!isActive) return;
        if (collision.CompareTag("Player"))
        {
            SyringePoolShooter player = collision.GetComponent<SyringePoolShooter>();
            if (player != null) player.OnDamage(1, gameObject.tag);
            gameObject.SetActive(false);
        }
    }

    private IEnumerator HitColorFlashRoutine()
    {
        if (spriteRenderers == null) yield break;
        foreach (var sr in spriteRenderers) sr.color = hitColor;
        yield return new WaitForSeconds(hitColorDuration);
        if (isActive && originalColors != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
                spriteRenderers[i].color = originalColors[i];
        }
    }
}

// ========================================
// ⭐ 간단한 보스 소환 시퀀스
// ========================================
public class BossSpawnSequence : MonoBehaviour
{
    public void StartSequence(float delay)
    {
        StartCoroutine(SpawnBossRoutine(delay));
    }

    private IEnumerator SpawnBossRoutine(float delay)
    {
        Debug.Log("[BossSpawnSequence] 보스 소환 시퀀스 시작!");

        // 지연 시간
        yield return new WaitForSeconds(delay);

        // ✅ SolsFinalGame에서 보스 관련 필드 직접 접근
        if (SolsFinalGame.Instance != null)
        {
            // bossUIObject 활성화
            if (SolsFinalGame.Instance.bossUIObject != null)
            {
                SolsFinalGame.Instance.bossUIObject.SetActive(true);
                Debug.Log("[BossSpawnSequence] ✅ BossUI 활성화!");
            }
            else
            {
                Debug.LogWarning("[BossSpawnSequence] ⚠️ bossUIObject가 null!");
            }

            // bossObject 활성화
            if (SolsFinalGame.Instance.bossObject != null)
            {
                SolsFinalGame.Instance.bossObject.SetActive(true);
                Debug.Log("[BossSpawnSequence] ✅ Boss 활성화!");
            }
            else
            {
                Debug.LogWarning("[BossSpawnSequence] ⚠️ bossObject가 null!");
            }

            // cameraFollow 활성화
            if (SolsFinalGame.Instance.cameraFollow != null)
            {
                SolsFinalGame.Instance.cameraFollow.enabled = true;
                Debug.Log("[BossSpawnSequence] ✅ 카메라 추적 활성화!");
            }
            else
            {
                Debug.LogWarning("[BossSpawnSequence] ⚠️ cameraFollow가 null!");
            }

            // 플레이어 제어 복구
            PlayerMover playerMover = FindFirstObjectByType<PlayerMover>();
            if (playerMover != null)
            {
                playerMover.SetControlEnabled(true);
                Debug.Log("[BossSpawnSequence] ✅ 플레이어 제어 복구!");
            }
        }
        else
        {
            Debug.LogError("[BossSpawnSequence] ❌ SolsFinalGame.Instance를 찾을 수 없음!");
        }

        Debug.Log("[BossSpawnSequence] ✅ 시퀀스 완료!");

        // 자기 자신 파괴
        Destroy(gameObject);
    }
}
