using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class SyringePoolShooter : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("플레이어 이동 스크립트 (방향 제어용)")]
    [SerializeField] private PlayerMover playerMover;
    [SerializeField] private GameOverUIManager gameOverManager;

    [Header("체력 설정")]
    [SerializeField] private int maxHp = 5;
    [SerializeField] private int currentHp;

    [Header("피격 효과")]
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float hitDuration = 0.1f;

    [Header("발사 설정")]
    [SerializeField] private float fireCooldown = 1.0f;
    [Tooltip("애니메이션 시작 후 발사체가 나갈 때까지의 지연 시간")]
    [SerializeField] private float projectileLaunchDelay = 0.5f; // ★ 추가된 지연 시간 변수
    [SerializeField] private KeyCode fireKey = KeyCode.Mouse0;
    [SerializeField] private Transform firePoint;

    [Header("풀 설정")]
    [SerializeField] private SyringeProjectile projectilePrefab;
    [SerializeField] private int poolSize = 10;

    [Header("공격 애니메이션")]
    [SerializeField] private Animator attackAnimator;
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private float attackDuration = 0.3f; // 애니메이션 전체 길이 (PlayerMover 제어용)

    private readonly List<SyringeProjectile> pool = new List<SyringeProjectile>();
    private float fireTimer = 0f;
    private bool shootingEnabled = false;
    private bool attackPlaying = false;
    private float attackTimer = 0f;

    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private Coroutine hitRoutine;

    public void SetShootingEnabled(bool enabled) { shootingEnabled = enabled; }

    void Awake()
    {
        currentHp = maxHp;
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) originalColor = spriteRenderer.color;
        if (firePoint == null) firePoint = transform;

        if (playerMover == null) playerMover = GetComponent<PlayerMover>();

        if (projectilePrefab != null)
        {
            for (int i = 0; i < Mathf.Max(1, poolSize); i++)
            {
                SyringeProjectile proj = Instantiate(projectilePrefab, transform);
                proj.gameObject.SetActive(false);
                proj.Init(this);
                pool.Add(proj);
            }
        }
        if (attackAnimator == null) attackAnimator = GetComponentInParent<Animator>();
        shootingEnabled = false;
    }

    void Update()
    {
        if (currentHp <= 0) return;

        fireTimer += Time.deltaTime;

        // 애니메이션 재생 중 PlayerMover 제어 관리
        if (attackPlaying)
        {
            attackTimer += Time.deltaTime;
            // attackDuration은 플레이어 움직임을 멈추는 용도로 사용됨
            // 실제 발사와는 별개로 동작
            if (attackTimer >= attackDuration)
            {
                attackPlaying = false;
                attackTimer = 0f;
                if (playerMover != null) playerMover.SetAnimationOverride(false);
            }
        }

        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            OnDamage(currentHp, "Boss");
            return;
        }

        if (!shootingEnabled) return;

        // 쿨타임 체크 후 발사 시퀀스 시작
        if (Input.GetKeyDown(fireKey) && fireTimer >= fireCooldown)
        {
            StartCoroutine(CoFireSequence());
        }
    }

    // ★ 발사 시퀀스 코루틴: 애니메이션 -> 대기 -> 발사체 생성
    private IEnumerator CoFireSequence()
    {
        // 1. 쿨타임 초기화
        fireTimer = 0f;

        // 2. 마우스 방향 계산 (클릭 시점 기준)
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = (mousePos - transform.position).normalized;

        // 3. 플레이어 방향 전환
        if (playerMover != null)
        {
            playerMover.SetFaceDirection(dir);
        }

        // 4. 애니메이션 재생 (즉시)
        PlayAttackAnimation();

        // 5. 설정된 시간(0.5초) 만큼 대기
        yield return new WaitForSeconds(projectileLaunchDelay);

        // 대기 중에 죽었으면 발사하지 않음
        if (currentHp <= 0) yield break;

        // 6. 실제 발사체 생성 및 날리기
        LaunchProjectile(dir);
    }

    // 실제 발사체를 날리는 로직 분리
    private void LaunchProjectile(Vector2 dir)
    {
        SyringeProjectile proj = GetFromPool();
        if (proj == null) return;

        proj.transform.position = firePoint.position;
        proj.gameObject.SetActive(true);
        proj.Launch(dir);
    }

    public void OnDamage(int damageAmount, string attackerTag = "Unknown")
    {
        if (currentHp <= 0) return;
        currentHp -= damageAmount;
        if (hitRoutine != null) StopCoroutine(hitRoutine);
        hitRoutine = StartCoroutine(HitFlash());
        if (currentHp <= 0) Die(attackerTag);
    }

    private IEnumerator HitFlash()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = hitColor;
        yield return new WaitForSeconds(hitDuration);
        if (currentHp > 0) spriteRenderer.color = originalColor;
    }

    private void Die(string killerTag)
    {
        currentHp = 0;
        shootingEnabled = false;
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        if (gameOverManager != null) gameOverManager.ShowDeadUI(killerTag);
    }

    private void PlayAttackAnimation()
    {
        if (attackAnimator == null) return;

        // PlayerMover에게 애니메이션 제어권 가져옴
        if (playerMover != null) playerMover.SetAnimationOverride(true);

        if (!attackAnimator.enabled) attackAnimator.enabled = true;

        // 애니메이션 강제 처음부터 재생 (한 번만 나가게 함)
        if (!string.IsNullOrEmpty(attackStateName))
        {
            attackAnimator.Play(attackStateName, -1, 0f);
            attackAnimator.speed = 1f;
        }

        attackPlaying = true;
        attackTimer = 0f;
    }

    private SyringeProjectile GetFromPool()
    {
        foreach (var p in pool) if (!p.gameObject.activeInHierarchy) return p;
        if (projectilePrefab != null)
        {
            SyringeProjectile extra = Instantiate(projectilePrefab, transform);
            extra.gameObject.SetActive(false);
            extra.Init(this);
            pool.Add(extra);
            return extra;
        }
        return null;
    }

    public void ReturnProjectile(SyringeProjectile proj)
    {
        if (proj) proj.gameObject.SetActive(false);
    }
}