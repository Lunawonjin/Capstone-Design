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
    [Tooltip("끄면 입력/이동/애니메이션이 모두 정지합니다")]
    public bool controlEnabled = true;


    [Header("UI 잠금 연동 / UI Lock Integration")]
    [Tooltip("여기에 UIExclusiveManager를 할당하면, UI가 열릴 때 자동으로 이동을 잠급니다")]
    [SerializeField] private UIExclusiveManager uiLock;  // 인스펙터에 드래그 or 자동 탐색
    private bool _lockedByUI = false;                    // 현재 UI 때문에 잠겼는지 추적


    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;

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
        // ▼▼▼ 추가: UI 잠금 상태 변화 감지 → 컨트롤 토글 ▼▼▼
        bool shouldLock = (uiLock != null && uiLock.IsAnyActive);
        if (shouldLock != _lockedByUI)
        {
            SetControlEnabled(!shouldLock);
            _lockedByUI = shouldLock;
        }
        // ▲▲▲ 추가 끝 ▲▲▲

        // 컨트롤 잠금 상태: 입력 무시, 애니메이션 정지
        if (!controlEnabled)
        {
            moveDirection = Vector2.zero;
            if (animator != null) animator.speed = 0f;
            return;
        }

        float moveX = 0f, moveY = 0f;
        if (Input.GetKey(KeyCode.W)) moveY += 1f;
        if (Input.GetKey(KeyCode.S)) moveY -= 1f;
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, moveY).normalized;

        if (animator == null) return;

        string currentAnimation = "";
        if (moveX < 0f) currentAnimation = "Left_Walk";
        else if (moveX > 0f) currentAnimation = "Right_Walk";
        else if (moveY > 0f) currentAnimation = "Back_Walk";
        else if (moveY < 0f) currentAnimation = "Front_Walk";

        if (!string.IsNullOrEmpty(currentAnimation))
        {
            animator.speed = 1f;
            animator.Play(currentAnimation);
        }
        else
        {
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
            if (animator != null) animator.speed = 0f;
        }
    }
    public void Freeze() => SetControlEnabled(false);
    public void Unfreeze() => SetControlEnabled(true);

#if UNITY_EDITOR
    [ContextMenu("Freeze (Lock Controls)")] private void CtxFreeze() => Freeze();
    [ContextMenu("Unfreeze (Unlock Controls)")] private void CtxUnfreeze() => Unfreeze();
#endif
}
