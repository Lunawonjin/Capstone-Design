using System.Collections;
using System.Collections.Generic;
//using System.Numerics;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

public class PlayerMove : MonoBehaviour
{
    public float moveSpeed = 1f;

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        float moveX = 0f;
        float moveY = 0f;

        // 동시에 여러 키 입력 가능하도록 수정
        if (Input.GetKey(KeyCode.W)) moveY += 1f;
        if (Input.GetKey(KeyCode.S)) moveY -= 1f;
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, moveY).normalized;

        string currentAnimation = "";

        // 수평이 우선
        if (moveX < 0)
        {
            currentAnimation = "Left_Walk";
        }
        else if (moveX > 0)
        {
            currentAnimation = "Right_Walk";
        }
        else if (moveY > 0)
        {
            currentAnimation = "Back_Walk";
        }
        else if (moveY < 0)
        {
            currentAnimation = "Front_Walk";
        }

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
        rb.MovePosition(rb.position + moveDirection * moveSpeed * Time.fixedDeltaTime);
    }
}