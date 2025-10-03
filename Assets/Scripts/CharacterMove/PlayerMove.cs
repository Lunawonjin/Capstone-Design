using UnityEngine;
using Vector2 = UnityEngine.Vector2;

[DisallowMultipleComponent]
public class PlayerMove : MonoBehaviour, NpcEventDebugLoader.IPlayerControlToggle
{
    [Header("이동 설정 / Movement")]
    [Tooltip("초당 이동 속도")]
    public float moveSpeed = 1f;

    [Header("컨트롤 잠금 / Control Lock")]
    [Tooltip("끄면 입력/이동이 정지합니다(단, 외부 연출용 애니는 허용 가능)")]
    public bool controlEnabled = true;

    [Header("UI 잠금 연동 / UI Lock Integration")]
    [Tooltip("여기에 UIExclusiveManager를 할당하면, UI가 열릴 때 자동으로 이동을 잠급니다")]
    [SerializeField] private UIExclusiveManager uiLock;
    private bool _lockedByUI = false;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;

    // 외부(연출) 애니 구동 허용 플래그
    private bool externalAnimDriving = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (rb == null) Debug.LogWarning("[PlayerMove] Rigidbody2D가 없습니다.");
        if (animator == null) Debug.LogWarning("[PlayerMove] Animator가 없습니다.");

        if (uiLock == null)
            uiLock = FindFirstObjectByType<UIExclusiveManager>();
    }

    void Update()
    {
        // UI 잠금 변화 감지 → 컨트롤 토글
        bool shouldLock = (uiLock != null && uiLock.IsAnyActive);
        if (shouldLock != _lockedByUI)
        {
            SetControlEnabled(!shouldLock);
            _lockedByUI = shouldLock;
        }

        // 컨트롤 잠금: 입력/애니 차단(외부 구동 제외)
        if (!controlEnabled)
        {
            moveDirection = Vector2.zero;

            if (!externalAnimDriving && animator != null)
            {
                var st = animator.GetCurrentAnimatorStateInfo(0);
                animator.Play(st.shortNameHash, 0, 0f);
                animator.speed = 0f;
            }
            return;
        }

        // ===== 입력 처리 =====
        float moveX = 0f, moveY = 0f;
        if (Input.GetKey(KeyCode.W)) moveY += 1f;
        if (Input.GetKey(KeyCode.S)) moveY -= 1f;
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, moveY).normalized;

        if (animator == null) return;

        // ===== 입력 기반 애니메이션 =====
        if (moveDirection != Vector2.zero)
        {
            animator.speed = 1f;
            PlayWalkByVector(moveDirection);
        }
        else
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(st.shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        if (!controlEnabled)
        {
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(rb.position);
            return;
        }

        rb.MovePosition(rb.position + moveDirection * moveSpeed * Time.fixedDeltaTime);
    }

    // ==== 외부/이벤트 제어용 유틸리티 ====

    // IPlayerControlToggle 구현
    public void SetControlEnabled(bool enabled)
    {
        if (enabled) Unfreeze(keepAnimatorState: true); // 복원 시 애니 상태 유지
        else Freeze();
    }

    public void Freeze()
    {
        controlEnabled = false;
        moveDirection = Vector2.zero;
        if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }

        if (animator != null && !externalAnimDriving)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(st.shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

    public void Unfreeze() => Unfreeze(false);

    // keepAnimatorState=true면 현재 애니 상태/속도를 건드리지 않음
    public void Unfreeze(bool keepAnimatorState)
    {
        externalAnimDriving = false;
        controlEnabled = true;

        if (animator != null && !keepAnimatorState)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(st.shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Freeze (Lock Controls)")] private void CtxFreeze() => Freeze();
    [ContextMenu("Unfreeze (Unlock Controls)")] private void CtxUnfreeze() => Unfreeze();
#endif

    // ====== 외부(연출) 전용 애니 훅 ======
    public void ExternalAnim_PlayWalk(Vector2 dir, float animSpeed = 0.85f)
    {
        if (animator == null) return;
        externalAnimDriving = true;

        if (dir.sqrMagnitude < 1e-6f)
        {
            ExternalAnim_StopIdle();
            return;
        }

        animator.speed = Mathf.Max(0f, animSpeed);
        PlayWalkByVector(dir.normalized);
    }

    public void ExternalAnim_StopIdle()
    {
        if (animator == null) return;
        externalAnimDriving = false;

        var st = animator.GetCurrentAnimatorStateInfo(0);
        animator.Play(st.shortNameHash, 0, 0f);
        animator.speed = 0f;
    }

    // 내부/외부 공용: 방향 벡터로 워크 애니 선택
    private void PlayWalkByVector(Vector2 dir)
    {
        // X↓ Left_Walk, X↑ Right_Walk, Y↓ Front_Walk, Y↑ Back_Walk
        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
        {
            if (dir.x < 0f) animator.Play("Left_Walk");
            else animator.Play("Right_Walk");
        }
        else
        {
            if (dir.y > 0f) animator.Play("Back_Walk");
            else animator.Play("Front_Walk");
        }
    }
}
