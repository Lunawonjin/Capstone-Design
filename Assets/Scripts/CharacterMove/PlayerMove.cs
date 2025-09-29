using System.Collections.Generic;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

[DisallowMultipleComponent]
public class PlayerMove : MonoBehaviour
{
    [Header("이동 설정 / Movement")]
    [Tooltip("초당 이동 속도")]
    public float moveSpeed = 1f;

    [Header("컨트롤 잠금 / Control Lock")]
    [Tooltip("끄면 입력/이동이 정지합니다(단, 외부 연출용 애니 구동은 허용 가능)")]
    public bool controlEnabled = true;

    [Header("UI 잠금 연동 / UI Lock Integration")]
    [Tooltip("여기에 UIExclusiveManager를 할당하면, UI가 열릴 때 자동으로 이동을 잠급니다")]
    [SerializeField] private UIExclusiveManager uiLock;
    private bool _lockedByUI = false;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;

    // ===== 외부(연출) 애니메이션 구동 플래그 =====
    // controlEnabled=false인 동안에도 외부에서 애니메이션만 재생하도록 허용하는 스위치
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

        // 컨트롤 잠금: 입력 무시
        if (!controlEnabled)
        {
            moveDirection = Vector2.zero;

            // 외부가 애니를 구동 중이면 애니 속도를 0으로 만들지 않음
            if (!externalAnimDriving && animator != null)
                animator.speed = 0f;

            return;
        }

        // 입력 처리
        float moveX = 0f, moveY = 0f;
        if (Input.GetKey(KeyCode.W)) moveY += 1f;
        if (Input.GetKey(KeyCode.S)) moveY -= 1f;
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, moveY).normalized;

        // 입력 기반 애니메이션
        if (animator == null) return;

        if (moveDirection != Vector2.zero)
        {
            animator.speed = 1f;
            PlayWalkByVector(moveDirection);
        }
        else
        {
            // 정지 프레임에서 멈춤(현재 스테이트의 0프레임 유지)
            animator.Play(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, 0, 0f);
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

    // ==== 외부 제어용 유틸리티 ====

    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;
        if (!controlEnabled)
        {
            moveDirection = Vector2.zero;
            if (rb != null) rb.linearVelocity = Vector2.zero;

            // 외부가 애니를 구동 중이면 애니 속도를 0으로 만들지 않음
            if (animator != null && !externalAnimDriving)
                animator.speed = 0f;
        }
    }

    public void Freeze() => SetControlEnabled(false);
    public void Unfreeze()
    {
        externalAnimDriving = false; // 외부 구동 플래그 해제
        SetControlEnabled(true);
    }

#if UNITY_EDITOR
    [ContextMenu("Freeze (Lock Controls)")] private void CtxFreeze() => Freeze();
    [ContextMenu("Unfreeze (Unlock Controls)")] private void CtxUnfreeze() => Unfreeze();
#endif

    // ====== 외부(연출) 전용 애니 훅 ======
    // 연출 쪽에서 방향과 애니 속도를 넘기면, 입력이 잠겨 있어도 워크 애니를 재생
    public void ExternalAnim_PlayWalk(Vector2 dir, float animSpeed = 0.75f)
    {
        if (animator == null) return;
        externalAnimDriving = true;

        // 너무 작은 값은 0으로 처리
        if (dir.sqrMagnitude < 1e-6f)
        {
            ExternalAnim_StopIdle();
            return;
        }

        animator.speed = Mathf.Max(0f, animSpeed);
        PlayWalkByVector(dir.normalized);
    }

    // 외부 구동 종료(정지 자세로 복귀)
    public void ExternalAnim_StopIdle()
    {
        if (animator == null) return;
        externalAnimDriving = false;

        // 현재 스테이트의 0프레임에서 정지
        animator.Play(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, 0, 0f);
        animator.speed = 0f;
    }

    // 내부/외부 공용: 방향 벡터로 워크 애니 선택
    private void PlayWalkByVector(Vector2 dir)
    {
        // 4방 기준: 수평이 우선이냐 수직이 우선이냐 선택 가능
        // 여기서는 절대값이 큰 축을 우선
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
