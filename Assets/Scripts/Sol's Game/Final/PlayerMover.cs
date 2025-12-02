using UnityEngine;
using Vector2 = UnityEngine.Vector2;

[DisallowMultipleComponent]
public class PlayerMover : MonoBehaviour, NpcEventDebugLoader.IPlayerControlToggle
{
    [Header("이동 설정 / Movement")]
    public float moveSpeed = 1f;

    [Header("컨트롤 잠금 / Control Lock")]
    public bool controlEnabled = true;

    [Header("UI 잠금 연동 / UI Lock Integration")]
    [SerializeField] private UIExclusiveManager uiLock;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Vector2 moveDirection;

    private bool externalAnimDriving = false;
    private bool isAttacking = false;

    public Vector2 LastFacingDir { get; private set; } = Vector2.right;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (uiLock == null) uiLock = FindFirstObjectByType<UIExclusiveManager>();
    }

    void Update()
    {
        bool uiLocked = (uiLock != null && uiLock.IsAnyActive);
        bool effectiveEnabled = controlEnabled && !uiLocked;

        if (!effectiveEnabled)
        {
            moveDirection = Vector2.zero;
            if (!externalAnimDriving && !isAttacking && animator != null)
            {
                var st = animator.GetCurrentAnimatorStateInfo(0);
                animator.Play(st.shortNameHash, 0, 0f);
                animator.speed = 0f;
            }
            return;
        }

        float moveX = 0f;
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, 0f);
        if (moveDirection.sqrMagnitude > 1e-6f)
        {
            moveDirection = moveDirection.normalized;
            LastFacingDir = moveDirection;
            UpdateSpriteFacing(moveDirection.x);
        }

        if (animator == null) return;

        if (externalAnimDriving || isAttacking) return;

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
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            return;
        }

        Vector2 v = rb.linearVelocity;
        v.x = moveDirection.x * moveSpeed;
        rb.linearVelocity = v;
    }

    private void UpdateSpriteFacing(float xInput)
    {
        if (spriteRenderer == null) return;

        if (xInput < 0)
        {
            spriteRenderer.flipX = true; // 왼쪽: 뒤집기
        }
        else if (xInput > 0)
        {
            spriteRenderer.flipX = false; // 오른쪽: 원본
        }
    }

    public void SetFaceDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude > 1e-6f)
        {
            LastFacingDir = dir.normalized;
            UpdateSpriteFacing(dir.x);
        }
    }

    public void SetAnimationOverride(bool active)
    {
        isAttacking = active;
    }

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
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        if (animator != null && !externalAnimDriving && !isAttacking)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(st.shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

    public void Unfreeze(bool keepAnimatorState = false)
    {
        externalAnimDriving = false;
        controlEnabled = true;

        if (animator != null && !keepAnimatorState && !isAttacking)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(st.shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

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
        // [핵심 수정] 좌/우 구분 없이 Right_Walk만 재생
        // (UpdateSpriteFacing 함수가 이미지를 뒤집어주므로 왼쪽도 해결됨)

        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
        {
            // 왼쪽이든 오른쪽이든 Right_Walk 재생
            animator.Play("Right_Walk");
        }
        else
        {
            // 위아래는 기존 유지
            if (dir.y > 0f) animator.Play("Back_Walk");
            else animator.Play("Front_Walk");
        }
    }
}