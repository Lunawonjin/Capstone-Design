using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

/// <summary>
/// PlayerMove
/// - WASD 동시 입력 지원 (정규화)
/// - 수평 우선 애니메이션 선택
/// - controlEnabled=false 일 때:
///     입력 무시, 이동 정지, 애니메이터 정지(현재 포즈 유지)
/// - 외부에서 Freeze/Unfreeze 또는 SetControlEnabled로 제어 가능
/// </summary>
[DisallowMultipleComponent]
public class PlayerMove : MonoBehaviour
{
    [Header("이동 설정 / Movement")]
    [Tooltip("초당 이동 속도")]
    public float moveSpeed = 1f;

    [Header("컨트롤 잠금 / Control Lock")]
    [Tooltip("끄면 입력/이동/애니메이션이 모두 정지합니다")]
    public bool controlEnabled = true;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        // 안전 장치
        if (rb == null) Debug.LogWarning("[PlayerMove] Rigidbody2D가 없습니다.");
        if (animator == null) Debug.LogWarning("[PlayerMove] Animator가 없습니다.");
    }

    void Update()
    {
        // 컨트롤 잠금 상태: 입력 무시, 애니메이션 정지
        if (!controlEnabled)
        {
            moveDirection = Vector2.zero;
            if (animator != null)
            {
                // 현재 프레임 포즈 유지
                animator.speed = 0f;
            }
            return;
        }

        float moveX = 0f;
        float moveY = 0f;

        // 동시에 여러 키 입력 가능
        if (Input.GetKey(KeyCode.W)) moveY += 1f;
        if (Input.GetKey(KeyCode.S)) moveY -= 1f;
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, moveY).normalized;

        if (animator == null) return;

        string currentAnimation = "";

        // 수평 우선
        if (moveX < 0f)
        {
            currentAnimation = "Left_Walk";
        }
        else if (moveX > 0f)
        {
            currentAnimation = "Right_Walk";
        }
        else if (moveY > 0f)
        {
            currentAnimation = "Back_Walk";
        }
        else if (moveY < 0f)
        {
            currentAnimation = "Front_Walk";
        }

        if (!string.IsNullOrEmpty(currentAnimation))
        {
            animator.speed = 1f; // 재생
            animator.Play(currentAnimation);
        }
        else
        {
            // 입력 없음: 현재 상태를 0프레임으로 고정하고 정지
            animator.Play(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        if (!controlEnabled)
        {
            // 위치 고정
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(rb.position); // no-op로 안전 고정
            return;
        }

        rb.MovePosition(rb.position + moveDirection * moveSpeed * Time.fixedDeltaTime);
    }

    // ==== 외부 제어용 유틸리티 / Utility methods ====

    /// <summary>
    /// 컨트롤 가능 여부 설정
    /// </summary>
    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;

        // 즉시 반영
        if (!controlEnabled)
        {
            moveDirection = Vector2.zero;
            if (rb != null) rb.linearVelocity = Vector2.zero;
            if (animator != null) animator.speed = 0f;
        }
    }

    /// <summary>
    /// 컨트롤 잠금(정지)
    /// </summary>
    public void Freeze()
    {
        SetControlEnabled(false);
    }

    /// <summary>
    /// 컨트롤 해제(재개)
    /// </summary>
    public void Unfreeze()
    {
        SetControlEnabled(true);
    }

#if UNITY_EDITOR
    // 인스펙터에서 테스트하기 편하도록 컨텍스트 메뉴 제공
    [ContextMenu("Freeze (Lock Controls)")]
    private void CtxFreeze() => Freeze();

    [ContextMenu("Unfreeze (Unlock Controls)")]
    private void CtxUnfreeze() => Unfreeze();
#endif
}
