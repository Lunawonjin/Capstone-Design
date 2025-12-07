using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class SyringePoolShooter : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("플레이어 이동/방향 제어 스크립트")]
    [SerializeField] private PlayerMover playerMover;
    [SerializeField] private GameOverUIManager gameOverManager;

    [Header("체력 설정")]
    [SerializeField] private int maxHp = 5;
    [SerializeField] private int currentHp;

    [Header("피격 효과")]
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float hitDuration = 0.1f;

    [Header("발사 공통 설정")]
    [SerializeField] private float fireCooldown = 0.5f;
    [Tooltip("애니메이션 시작 후 발사체가 나갈 때까지의 지연 시간")]
    [SerializeField] private float projectileLaunchDelay = 0.2f;
    [SerializeField] private KeyCode fireKey = KeyCode.Mouse0;
    [SerializeField] private Transform firePoint;

    [Header("풀 설정")]
    [SerializeField] private SyringeProjectile projectilePrefab;
    [SerializeField] private int poolSize = 10;

    [Header("기본 샷 설정")]
    [Tooltip("기본 샷 이동 속도 (멀리 날아가게 하려면 값을 키우면 됨)")]
    [SerializeField] private float normalSpeed = 14f;      // 이전보다 조금 빠르게
    [Tooltip("기본 샷에 적용되는 중력 (포물선 궤적)")]
    [SerializeField] private float normalGravity = 9.8f;
    [Tooltip("기본 샷 데미지")]
    [SerializeField] private int normalDamage = 1;

    [Header("차지 샷 설정")]
    [Tooltip("이 시간 이상 키를 누르고 있으면 차지 샷 발사")]
    [SerializeField] private float chargeTime = 1.0f;
    [Tooltip("차지 샷 이동 속도 (직선, 빠르게)")]
    [SerializeField] private float chargedSpeed = 22f;
    [Tooltip("차지 샷 중력 (0이면 완전 직선)")]
    [SerializeField] private float chargedGravity = 0f;
    [Tooltip("차지 샷 데미지 (한 번 맞으면 3회 피격 판정)")]
    [SerializeField] private int chargedDamage = 3;
    [Tooltip("차지 샷이 관통할 수 있는 최대 적 수")]
    [SerializeField] private int chargedPierceCount = 2;

    [Header("공격 애니메이션")]
    [SerializeField] private Animator attackAnimator;
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private float attackDuration = 0.3f; // 애니메이션 전체 길이 (PlayerMover 제어용)

    private readonly List<SyringeProjectile> pool = new List<SyringeProjectile>();
    private float fireTimer = 0f;
    private bool shootingEnabled = false;

    // 애니메이션 제어
    private bool attackPlaying = false;
    private float attackTimer = 0f;

    // 차지 샷 제어
    private bool isCharging = false;
    private float chargeTimer = 0f;

    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private Coroutine hitRoutine;

    public void SetShootingEnabled(bool enabled)
    {
        shootingEnabled = enabled;
    }

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

        // 공격 애니메이션 구간 동안 PlayerMover 애니 제어
        if (attackPlaying)
        {
            attackTimer += Time.deltaTime;
            if (attackTimer >= attackDuration)
            {
                attackPlaying = false;
                attackTimer = 0f;
                if (playerMover != null) playerMover.SetAnimationOverride(false);
            }
        }

        // 디버그용: 0번 키로 즉사
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            OnDamage(currentHp, "Boss");
            return;
        }

        if (!shootingEnabled) return;

        // 차지 시작/유지
        if (Input.GetKey(fireKey))
        {
            isCharging = true;
            chargeTimer += Time.deltaTime;
        }

        // 키 뗐을 때 발사
        if (Input.GetKeyUp(fireKey) && fireTimer >= fireCooldown)
        {
            bool isChargedShot = isCharging && chargeTimer >= chargeTime;

            Vector3 mousePosWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dir = mousePosWorld - transform.position;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            dir.Normalize();

            StartCoroutine(CoFireSequence(dir, isChargedShot));

            fireTimer = 0f;
            isCharging = false;
            chargeTimer = 0f;
        }
    }

    // 공격 시퀀스: 방향/차지 여부 전달
    private IEnumerator CoFireSequence(Vector2 dir, bool isChargedShot)
    {
        // 방향 맞추기
        if (playerMover != null)
        {
            playerMover.SetFaceDirection(dir);
        }

        // 공격 애니메이션
        PlayAttackAnimation();

        // 발사체 나가기 전까지 딜레이
        yield return new WaitForSeconds(projectileLaunchDelay);

        // 그 사이 죽었으면 발사 안 함
        if (currentHp <= 0) yield break;

        LaunchProjectile(dir, isChargedShot);
    }

    private void LaunchProjectile(Vector2 dir, bool isChargedShot)
    {
        SyringeProjectile proj = GetFromPool();
        if (proj == null) return;

        proj.transform.position = firePoint.position;
        proj.gameObject.SetActive(true);

        if (isChargedShot)
        {
            // 차지 샷: 직선, 빠른 속도, 관통 2회, 데미지 3
            proj.Launch(
                dir,
                chargedSpeed,
                chargedGravity,
                chargedDamage,
                true,
                chargedPierceCount
            );
        }
        else
        {
            // 기본 샷: 포물선, 조금 더 멀리 날아가도록 속도↑
            proj.Launch(
                dir,
                normalSpeed,
                normalGravity,
                normalDamage,
                false,
                0
            );
        }
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

        if (playerMover != null) playerMover.SetAnimationOverride(true);

        if (!attackAnimator.enabled) attackAnimator.enabled = true;

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
        foreach (var p in pool)
        {
            if (!p.gameObject.activeInHierarchy) return p;
        }

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
