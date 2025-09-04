using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

public class PlayerMove : MonoBehaviour
{
    public float moveSpeed = 5f;     // �¿� �̵� �ӵ�
    public float jumpForce = 10f;    // ���� �� (���޽�)

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;
    private bool isGrounded = true;  // ���� ��� �ִ��� ����

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

    }

    void Update()
    {
        float moveX = 0f;

        // �¿� �̵� �Է�
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, 0f).normalized;

        // �����̽��� ����
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            // ���� ���� Y�ӵ��� 0���� �ʱ�ȭ (�ϰ��� �ִ� ������ ����)
            rb.velocity = new Vector2(rb.velocity.x, 0f);
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            isGrounded = false;
            //Debug.Log("���� �����!");
        }

        // �ִϸ��̼� ó��
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
            // Idle ó�� (���� �����ӿ��� ����)
            animator.Play(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

    void FixedUpdate()
    {
        // Rigidbody2D�� velocity�� ����� �¿� �̵�
        rb.velocity = new Vector2(moveDirection.x * moveSpeed, rb.velocity.y);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Ground �±װ� �ִ� ������Ʈ�� ������ ���� ó��
        if (collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            //Debug.Log("���� �Ϸ�");
        }
    }
}
