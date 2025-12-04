using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class SolsFinalGameEnemy : MonoBehaviour
{
    public static int enemyCount = 0;

    [Header("Active Settings")]
    [SerializeField] private bool activateOnStart = true;

    [Header("Enemy Type")]
    [SerializeField] private bool isBoss = false;

    [Header("Boss Settings")]
    [SerializeField] private float bossFallSpeed = 5.0f;
    [SerializeField] private float bossTargetY = 1.2f;

    [Header("Boss: Phase 2")]
    [SerializeField] private int phase2HpThreshold = 20;
    [SerializeField] private GameObject minionPrefab;
    [SerializeField] private float summonInterval = 5.0f;
    [SerializeField] private int summonCount = 2;

    [Header("Boss: Landing Effect")]
    [SerializeField] private float landShakeMagnitude = 0.5f;
    [SerializeField] private float landShakeDuration = 0.2f;
    [SerializeField] private AudioClip landSound;

    [Header("Move Settings")]
    [SerializeField] private bool canMove = true;
    [SerializeField] private float xSpeed = 1.0f;
    [SerializeField] private float ySpeed = 1.0f;
    [SerializeField] private float yMax = 1.0f;
    [SerializeField] private float yMin = -1.8f;

    [Header("Health & Hit")]
    [SerializeField] private int hitsToDie = 30;
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float hitColorDuration = 0.1f;

    // 보스 전용 변수
    [Header("Boss Jump Settings")]
    [SerializeField] private float bossRiseSpeed = 15.0f;
    [SerializeField] private float bossStompWaitTime = 3.0f;

    private enum BossState { Falling, Landed, Rising }
    private BossState bossState = BossState.Falling;
    private float stompTimer = 0f;
    private float initialSpawnY;
    private bool isPhase2Active = false;

    private Rigidbody2D rb;
    private Animator anim;
    private int yDir;
    private bool isActive = false;
    private int currentHits = 0;
    private bool isQuitting = false;
    private bool hasLanded = false;

    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private Coroutine hitColorRoutine;
    private AudioSource audioSource;
    private Transform playerTransform;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();
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
        isPhase2Active = false;
        bossState = BossState.Falling;
        hasLanded = false;
        stompTimer = 0f;
        initialSpawnY = transform.position.y;

        if (isBoss)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }

        if (CompareTag("Enemy")) enemyCount++;

        if (originalColors != null && spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
                spriteRenderers[i].color = originalColors[i];
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
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
            if (BossSpawnManager.Instance != null) BossSpawnManager.Instance.TriggerBossSpawn();

            PlayerMover playerMover = FindFirstObjectByType<PlayerMover>();
            if (playerMover != null)
            {
                playerMover.enabled = true;
                playerMover.SetControlEnabled(true);
            }
        }
    }

    void Update()
    {
        if (!isActive) return;
        MoveTransform(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (!isActive) return;
        if (!isBoss && rb != null) MoveRigidbody(Time.fixedDeltaTime);
    }

    private void MoveTransform(float dt)
    {
        if (isBoss)
        {
            Vector3 p = transform.position;
            switch (bossState)
            {
                case BossState.Falling:
                    if (p.y > bossTargetY)
                    {
                        p.y -= bossFallSpeed * dt;
                        if (p.y <= bossTargetY)
                        {
                            p.y = bossTargetY;
                            bossState = BossState.Landed;
                            stompTimer = 0f;
                            PlayLandingEffect();
                        }
                    }
                    break;
                case BossState.Landed:
                    stompTimer += dt;
                    if (stompTimer >= bossStompWaitTime)
                    {
                        bossState = BossState.Rising;
                        hasLanded = false;
                        if (playerTransform == null)
                        {
                            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                            if (playerObj != null) playerTransform = playerObj.transform;
                        }
                    }
                    break;
                case BossState.Rising:
                    if (p.y < initialSpawnY)
                    {
                        p.y += bossRiseSpeed * dt;
                        if (p.y >= initialSpawnY)
                        {
                            p.y = initialSpawnY;
                            if (playerTransform != null)
                            {
                                float targetX = playerTransform.position.x;
                                p.x = Mathf.Clamp(targetX, -8f, 8f);
                            }
                            bossState = BossState.Falling;
                        }
                    }
                    break;
            }
            transform.position = p;
            return;
        }

        if (!canMove || rb != null) return;
        Vector3 pos = transform.position;
        pos.x -= xSpeed * dt;
        pos.y += ySpeed * yDir * dt;
        if (yDir > 0 && pos.y >= yMax) { pos.y = yMax; yDir = -1; }
        else if (yDir < 0 && pos.y <= yMin) { pos.y = yMin; yDir = 1; }
        transform.position = pos;
    }

    private void MoveRigidbody(float dt)
    {
        if (isBoss || !canMove) { rb.linearVelocity = Vector2.zero; return; }
        Vector2 p = rb.position;
        p.x -= xSpeed * dt;
        p.y += ySpeed * yDir * dt;
        if (yDir > 0 && p.y >= yMax) { p.y = yMax; yDir = -1; }
        else if (yDir < 0 && p.y <= yMin) { p.y = yMin; yDir = 1; }
        rb.MovePosition(p);
    }

    private void PlayLandingEffect()
    {
        Debug.Log("보스 착지! 쿠궁!");
        if (audioSource != null && landSound != null) audioSource.PlayOneShot(landSound);
        if (Camera.main != null) StartCoroutine(CameraShakeRoutine(Camera.main.transform, landShakeDuration, landShakeMagnitude));
    }

    private IEnumerator CameraShakeRoutine(Transform camTransform, float duration, float magnitude)
    {
        Vector3 originalPos = camTransform.position;
        float elapsed = 0.0f;
        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            camTransform.position = new Vector3(originalPos.x + x, originalPos.y + y, originalPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }
        camTransform.position = originalPos;
    }

    public void Activate() { isActive = true; }
    public void Deactivate() { isActive = false; if (rb != null) rb.linearVelocity = Vector2.zero; }

    public void TakeHit(Vector2 hitPosition)
    {
        currentHits++;
        int currentHp = hitsToDie - currentHits;

        // [추가됨] 보스 체력 로그 출력
        Debug.Log($"<color=yellow>보스 피격! 남은 체력: {currentHp} / {hitsToDie}</color>");

        if (anim != null) anim.SetTrigger("Hit");

        if (hitColorRoutine != null) StopCoroutine(hitColorRoutine);
        hitColorRoutine = StartCoroutine(HitColorFlashRoutine());

        if (isBoss && currentHp <= phase2HpThreshold && !isPhase2Active)
        {
            StartCoroutine(Phase2SummonRoutine());
        }

        if (currentHits >= hitsToDie)
            StartCoroutine(DieWithDelay());
    }

    private IEnumerator Phase2SummonRoutine()
    {
        isPhase2Active = true;
        Debug.Log("⚠️ 보스 페이즈 2 시작! 소환 패턴 발동!");
        while (isActive && currentHits < hitsToDie)
        {
            if (minionPrefab != null)
            {
                for (int i = 0; i < summonCount; i++)
                {
                    float randomX = Random.Range(-8f, 8f);
                    float spawnY = 5.5f;
                    Vector3 spawnPos = new Vector3(randomX, spawnY, 0);
                    Instantiate(minionPrefab, spawnPos, Quaternion.identity);
                }
                Debug.Log($"쫄병 {summonCount}마리 소환됨!");
            }
            yield return new WaitForSeconds(summonInterval);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            SyringePoolShooter player = collision.gameObject.GetComponent<SyringePoolShooter>();
            if (player != null) player.OnDamage(1, gameObject.tag);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            SyringePoolShooter player = collision.GetComponent<SyringePoolShooter>();
            if (player != null) player.OnDamage(1, gameObject.tag);
            if (!isBoss) gameObject.SetActive(false);
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

    private IEnumerator DieWithDelay()
    {
        isActive = false;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        yield return new WaitForSeconds(0.5f);
        gameObject.SetActive(false);
    }
}