using UnityEngine;
using Vector2 = UnityEngine.Vector2;

[DisallowMultipleComponent]
public class PlayerMove : MonoBehaviour, NpcEventDebugLoader.IPlayerControlToggle
{
    [Header("이동 설정 / Movement")]
    public float moveSpeed = 1f;

    [Header("컨트롤 잠금 / Control Lock")]
    [Tooltip("외부(패널/이벤트)에서 이 값을 false로 만들면 플레이어가 멈춥니다.")]
    public bool controlEnabled = true;

    [Header("UI 잠금 연동 / UI Lock Integration")]
    [SerializeField] private UIExclusiveManager uiLock;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;

    // 외부 연출이 애니를 구동할 때 true
    private bool externalAnimDriving = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (uiLock == null) uiLock = FindFirstObjectByType<UIExclusiveManager>();
        if (rb == null) Debug.LogWarning("[PlayerMove] Rigidbody2D가 없습니다.");
        if (animator == null) Debug.LogWarning("[PlayerMove] Animator가 없습니다.");
    }

    void Update()
    {
        // ✅ 최종 이동 가능 여부를 매 프레임 계산
        bool uiLocked = (uiLock != null && uiLock.IsAnyActive);
        bool effectiveEnabled = controlEnabled && !uiLocked;

        if (!effectiveEnabled)
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

        bool uiLocked = (uiLock != null && uiLock.IsAnyActive);
        bool effectiveEnabled = controlEnabled && !uiLocked;

        if (!effectiveEnabled)
        {
            // Rigidbody2D는 velocity 프로퍼티를 사용합니다.
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(rb.position);
            return;
        }

        rb.MovePosition(rb.position + moveDirection * moveSpeed * Time.fixedDeltaTime);
    }

    // ==== 외부/이벤트 제어용 유틸리티 ====
    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;
        if (!enabled) Freeze();
        else Unfreeze(keepAnimatorState: true);
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

    private void PlayWalkByVector(Vector2 dir)
    {
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
