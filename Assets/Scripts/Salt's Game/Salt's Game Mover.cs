using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

public class SaltGameMover : MonoBehaviour
{
    public float moveSpeed = 5f;     // 좌우 이동 속도
    public float jumpForce = 10f;    // 점프 힘 (임펄스)

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;
    private bool isGrounded = true;  // 땅에 닿아 있는지 여부

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

    }

    void Update()
    {
        float moveX = 0f;

        // 좌우 이동 입력
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, 0f).normalized;

        // 스페이스바 점프
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            // 점프 전에 Y속도를 0으로 초기화 (일관성 있는 점프를 위해)
            rb.velocity = new Vector2(rb.velocity.x, 0f);
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            isGrounded = false;
            //Debug.Log("점프 실행됨!");
        }

        // 애니메이션 처리
        string currentAnimation = "";
        if (moveX < 0f) currentAnimation = "Left_Walk";
        else if (moveX > 0f) currentAnimation = "Right_Walk";

        if (!string.IsNullOrEmpty(currentAnimation))
        {
            animator.speed = 1f;
            animator.Play(currentAnimation);
        }
        else
        {
            // Idle 처리 (현재 프레임에서 정지)
            animator.Play(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

    void FixedUpdate()
    {
        // Rigidbody2D의 velocity를 사용한 좌우 이동
        rb.velocity = new Vector2(moveDirection.x * moveSpeed, rb.velocity.y);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Ground 태그가 있는 오브젝트에 닿으면 착지 처리
        if (collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            //Debug.Log("착지 완료");
        }
    }
}
