using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SyringePoolShooter : MonoBehaviour
{
    [Header("발사 설정")]
    [Tooltip("발사 간 최소 간격(초)")]
    [SerializeField] private float fireCooldown = 1.0f;

    [Tooltip("발사 키 (기본: 마우스 좌클릭)")]
    [SerializeField] private KeyCode fireKey = KeyCode.Mouse0;

    [Tooltip("발사 시작 위치")]
    [SerializeField] private Transform firePoint;

    [Tooltip("발사 방향 (기본 오른쪽)")]
    [SerializeField] private Vector2 shootDirection = Vector2.right;

    [Header("풀 설정")]
    [SerializeField] private SyringeProjectile projectilePrefab;
    [SerializeField] private int poolSize = 10;

    [Header("공격 애니메이션")]
    [Tooltip("플레이어 Animator (비워두면 상위에서 자동 탐색)")]
    [SerializeField] private Animator attackAnimator;
    [Tooltip("주사기를 던질 때 재생할 상태 이름(예: Attack)")]
    [SerializeField] private string attackStateName = "Attack";

    [Tooltip("Attack 재생 후 자동으로 되돌릴 상태 이름(예: Right_Walk, 없으면 사용 안 함)")]
    [SerializeField] private string returnStateName = "Right_Walk";

    [Tooltip("Attack 애니메이션 길이(초). 이 시간이 지나면 returnStateName으로 되돌립니다.")]
    [SerializeField] private float attackDuration = 0.3f;

    private readonly List<SyringeProjectile> pool = new List<SyringeProjectile>();
    private float fireTimer = 0f;

    // 외부에서 주사기 먹기 전까지 false, 먹고 나면 true로 변경
    private bool shootingEnabled = false;
    private bool attackPlaying = false;
    private float attackTimer = 0f;

    public void SetShootingEnabled(bool enabled)
    {
        shootingEnabled = enabled;
    }

    void Awake()
    {
        if (firePoint == null)
            firePoint = transform;

        if (projectilePrefab == null)
        {
            Debug.LogWarning("[SyringePoolShooter] projectilePrefab이 비어 있습니다.");
            return;
        }

        // 풀 초기화
        for (int i = 0; i < Mathf.Max(1, poolSize); i++)
        {
            SyringeProjectile proj = Instantiate(projectilePrefab, transform);
            proj.gameObject.SetActive(false);
            proj.Init(this);
            pool.Add(proj);
        }

        // 공격 애니메이터 자동 탐색
        if (attackAnimator == null)
            attackAnimator = GetComponentInParent<Animator>();

        // 시작 시에는 발사 비활성화 (주사기 줍기 전까지)
        shootingEnabled = false;
    }

    void Update()
    {
        fireTimer += Time.deltaTime;

        // Attack 재생 중이면 타이머 체크해서 원래 상태로 돌리기
        if (attackPlaying)
        {
            attackTimer += Time.deltaTime;
            if (attackTimer >= attackDuration)
            {
                attackPlaying = false;
                attackTimer = 0f;
                PlayReturnState();
            }
        }

        // 아직 발사 권한이 없으면 리턴
        if (!shootingEnabled)
            return;

        if (Input.GetKeyDown(fireKey) && fireTimer >= fireCooldown)
        {
            Fire();
        }
    }

    private void Fire()
    {
        SyringeProjectile proj = GetFromPool();
        if (proj == null) return;

        fireTimer = 0f;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        proj.transform.position = spawnPos;
        proj.gameObject.SetActive(true);

        proj.Launch(shootDirection);

        PlayAttackAnimation();
    }

    private void PlayAttackAnimation()
    {
        if (attackAnimator == null) return;
        if (string.IsNullOrEmpty(attackStateName)) return;

        // 혹시 꺼져 있으면 켠다
        if (!attackAnimator.enabled)
            attackAnimator.enabled = true;

        // Attack 상태를 강제로 처음부터 재생
        attackAnimator.Play(attackStateName, 0, 0f);

        // Attack 애니가 도는 동안에는 속도 1로
        attackAnimator.speed = 1f;

        attackPlaying = true;
        attackTimer = 0f;
    }

    private void PlayReturnState()
    {
        if (attackAnimator == null) return;
        if (string.IsNullOrEmpty(returnStateName)) return;

        // Right_Walk 같은 기본 포즈 상태로 돌리고, 그 상태는 정지(속도 0)
        attackAnimator.Play(returnStateName, 0, 0f);
        attackAnimator.speed = 0f;
    }

    private SyringeProjectile GetFromPool()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (!pool[i].gameObject.activeInHierarchy)
                return pool[i];
        }

        // 모두 사용 중이면 하나 더 생성
        SyringeProjectile extra = Instantiate(projectilePrefab, transform);
        extra.gameObject.SetActive(false);
        extra.Init(this);
        pool.Add(extra);
        return extra;
    }

    public void ReturnProjectile(SyringeProjectile proj)
    {
        if (proj == null) return;
        proj.gameObject.SetActive(false);
    }
}
