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

    [Header("충돌 데미지")]
    [SerializeField] private int contactDamage = 1;

    private Rigidbody2D rb;
    private Animator anim;
    private int yDir;
    private bool isActive = false;
    private int currentHits = 0;
    private bool isQuitting = false;
    private bool hasCounted = false; // 카운트 중복 방지

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

    void Start()
    {
        // ⭐ Start에서 한 번만 카운트 (활성화된 적만)
        if (CompareTag("Enemy") && gameObject.activeInHierarchy && !hasCounted)
        {
            enemyCount++;
            hasCounted = true;
            Debug.Log($"[SolsFinalGameEnemy] 적 등록! 현재 적 수: {enemyCount}");
        }

        isActive = activateOnStart;
    }

    void OnEnable()
    {
        currentHits = 0;

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

        // 카운트 중복 감소 방지
        if (CompareTag("Enemy") && hasCounted)
        {
            enemyCount--;
            hasCounted = false;
            Debug.Log($"[SolsFinalGameEnemy] 적 비활성화! 남은 적 수: {enemyCount}");

            // ⭐ Coroutine 없이 직접 체크
            if (enemyCount <= 0)
            {
                Debug.Log("[OnDisable] ⚠️ 적 카운트가 0 이하! CheckAllEnemiesDead 호출 예약");
                // SolsFinalGame을 통해 다음 프레임에 체크
                if (SolsFinalGame.Instance != null)
                {
                    SolsFinalGame.Instance.TriggerBossSpawnSequence(bossSpawnDelay);
                }
                else
                {
                    // 백업: 직접 시퀀스 생성
                    CheckAllEnemiesDead();
                }
            }
        }
    }

    private void CheckAllEnemiesDead()
    {
        Debug.Log($"[CheckAllEnemiesDead] 호출됨! 현재 적 수: {enemyCount}");

        if (enemyCount <= 0)
        {
            Debug.Log("[CheckAllEnemiesDead] ✅ 모든 적 처치! 보스 소환 시작");

            // 이미 시퀀스가 실행 중인지 확인
            if (FindAnyObjectByType<BossSpawnSequence>() != null)
            {
                Debug.LogWarning("[CheckAllEnemiesDead] ⚠️ 이미 보스 소환 시퀀스가 실행 중입니다!");
                return;
            }

            // 간단한 시퀀스 실행
            GameObject sequenceRunner = new GameObject("BossSpawnSequenceRunner");
            BossSpawnSequence sequence = sequenceRunner.AddComponent<BossSpawnSequence>();
            sequence.StartSequence(bossSpawnDelay);
        }
        else
        {
            Debug.Log($"[CheckAllEnemiesDead] 남은 적: {enemyCount}");
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

    public void Activate()
    {
        if (!isActive)
        {
            isActive = true;
            Debug.Log($"[SolsFinalGameEnemy] 적 활성화됨! (이름: {gameObject.name})");
        }
    }

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
        Debug.Log("[SolsFinalGameEnemy] 적 비활성화됨!");
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

        Debug.Log($"[SolsFinalGameEnemy] 피격! 현재 피격 횟수: {currentHits}/{hitsToDie}");

        if (anim != null) anim.SetTrigger("Hit");

        // 사망 여부 확인
        bool willDie = currentHits >= hitsToDie;

        // 죽지 않을 경우에만 Coroutine 실행
        if (!willDie)
        {
            if (hitColorRoutine != null) StopCoroutine(hitColorRoutine);
            hitColorRoutine = StartCoroutine(HitColorFlashRoutine());
        }
        else
        {
            // 죽을 경우 즉시 색상 변경 후 비활성화
            if (spriteRenderers != null)
            {
                foreach (var sr in spriteRenderers)
                {
                    sr.color = hitColor;
                }
            }

            Debug.Log("[SolsFinalGameEnemy] 적 사망! 비활성화");
            gameObject.SetActive(false);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isActive || !gameObject.activeInHierarchy) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            SyringePoolShooter player = collision.gameObject.GetComponent<SyringePoolShooter>();
            if (player != null)
            {
                player.OnDamage(contactDamage, gameObject.tag);
                Debug.Log($"[SolsFinalGameEnemy] 플레이어와 충돌! {contactDamage} 데미지");
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!isActive || !gameObject.activeInHierarchy) return;

        if (collision.CompareTag("Player"))
        {
            SyringePoolShooter player = collision.GetComponent<SyringePoolShooter>();
            if (player != null)
            {
                player.OnDamage(contactDamage, gameObject.tag);
                Debug.Log($"[SolsFinalGameEnemy] 플레이어와 트리거 충돌! {contactDamage} 데미지");
            }
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

    /// <summary>
    /// 씬 시작 시 static 변수 초기화용
    /// </summary>
    public static void ResetEnemyCount()
    {
        enemyCount = 0;
        Debug.Log("[SolsFinalGameEnemy] 적 카운트 리셋!");
    }

    /// <summary>
    /// 현재 적 카운트 확인 (디버그용)
    /// </summary>
    public static int GetEnemyCount()
    {
        return enemyCount;
    }

    /// <summary>
    /// 씬의 모든 Enemy 오브젝트 카운트 (실제 개수 확인용)
    /// </summary>
    public static void DebugCountAllEnemies()
    {
        SolsFinalGameEnemy[] allEnemies = FindObjectsByType<SolsFinalGameEnemy>(FindObjectsSortMode.None);
        int activeCount = 0;
        int totalCount = allEnemies.Length;

        foreach (var enemy in allEnemies)
        {
            if (enemy.gameObject.activeInHierarchy && enemy.CompareTag("Enemy"))
            {
                activeCount++;
            }
        }

        Debug.Log($"[DebugCountAllEnemies] 씬의 총 Enemy: {totalCount}개, 활성화된 Enemy: {activeCount}개, static 카운트: {enemyCount}");
    }
}

// ========================================
// ⭐ 보스 소환 시퀀스 (완전 개선)
// ========================================
public class BossSpawnSequence : MonoBehaviour
{
    public void StartSequence(float delay)
    {
        StartCoroutine(SpawnBossRoutine(delay));
    }

    private IEnumerator SpawnBossRoutine(float delay)
    {
        Debug.Log("[BossSpawnSequence] ========== 보스 등장 시퀀스 시작 ==========");

        // 초기 지연
        yield return new WaitForSeconds(delay);

        if (SolsFinalGame.Instance == null)
        {
            Debug.LogError("[BossSpawnSequence] ❌ SolsFinalGame.Instance를 찾을 수 없음!");
            Destroy(gameObject);
            yield break;
        }

        SolsFinalGame game = SolsFinalGame.Instance;

        // ========================================
        // 1단계: 모든 제어 잠금
        // ========================================
        Debug.Log("[BossSpawnSequence] 1단계: 모든 제어 잠금");

        PlayerMover playerMover = FindFirstObjectByType<PlayerMover>();
        SyringePoolShooter shooter = FindFirstObjectByType<SyringePoolShooter>();

        if (playerMover != null)
        {
            playerMover.SetControlEnabled(false);
            Debug.Log("[BossSpawnSequence] ✅ 플레이어 이동 잠금");
        }

        if (shooter != null)
        {
            shooter.SetShootingEnabled(false);
            Debug.Log("[BossSpawnSequence] ✅ 플레이어 공격 잠금");
        }

        // ========================================
        // 2단계: 보스 오브젝트 활성화 (움직임 없음)
        // ========================================
        Debug.Log("[BossSpawnSequence] 2단계: 보스 오브젝트 활성화");

        if (game.bossUIObject != null)
        {
            game.bossUIObject.SetActive(true);
            Debug.Log("[BossSpawnSequence] ✅ BossUI 활성화");
        }

        if (game.bossObject != null)
        {
            game.bossObject.SetActive(true);

            // 보스 움직임 정지
            SolsFinalGameBoss boss = game.bossObject.GetComponent<SolsFinalGameBoss>();
            if (boss != null)
            {
                boss.SetMovementEnabled(false);
                Debug.Log("[BossSpawnSequence] ✅ Boss 활성화 (움직임 정지)");
            }
        }

        yield return new WaitForSeconds(0.5f);

        // ========================================
        // 3단계: 카메라를 보스로 이동
        // ========================================
        Debug.Log("[BossSpawnSequence] 3단계: 카메라를 보스로 이동");

        yield return game.StartCoroutine(game.MoveCameraToBoss());

        // ========================================
        // 4단계: 보스 인트로 대사 실행
        // ========================================
        Debug.Log("[BossSpawnSequence] 4단계: 보스 인트로 대사 실행");

        yield return game.StartCoroutine(game.PlayBossIntroDialogueForSequence());

        // ========================================
        // 5단계: 카메라를 플레이어로 복귀
        // ========================================
        Debug.Log("[BossSpawnSequence] 5단계: 카메라를 플레이어로 복귀");

        yield return game.StartCoroutine(game.MoveCameraToPlayer());

        // ========================================
        // 6단계: 모든 제어 해제 및 전투 시작
        // ========================================
        Debug.Log("[BossSpawnSequence] 6단계: 전투 시작!");

        if (playerMover != null)
        {
            playerMover.SetControlEnabled(true);
            Debug.Log("[BossSpawnSequence] ✅ 플레이어 이동 해제");
        }

        if (shooter != null)
        {
            shooter.SetShootingEnabled(true);
            Debug.Log("[BossSpawnSequence] ✅ 플레이어 공격 해제");
        }

        if (game.bossObject != null)
        {
            SolsFinalGameBoss boss = game.bossObject.GetComponent<SolsFinalGameBoss>();
            if (boss != null)
            {
                boss.SetMovementEnabled(true);
                Debug.Log("[BossSpawnSequence] ✅ Boss 움직임 시작");
            }
        }

        // 카메라 추적 활성화
        if (game.cameraFollow != null)
        {
            game.cameraFollow.enabled = true;
            Debug.Log("[BossSpawnSequence] ✅ 카메라 추적 활성화");
        }

        Debug.Log("[BossSpawnSequence] ========== 보스 등장 시퀀스 완료! ==========");

        // 자기 자신 파괴
        Destroy(gameObject);
    }
}