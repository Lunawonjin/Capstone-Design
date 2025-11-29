using UnityEngine;

[DisallowMultipleComponent]
public class SolsFinalGameEnemy : MonoBehaviour
{
    [Header("Move Settings")]
    [Tooltip("X decreases slowly (move left).")]
    [SerializeField] private float xSpeed = 1.0f;

    [Tooltip("Y moves up/down between bounds.")]
    [SerializeField] private float ySpeed = 1.0f;

    [Header("Y Bounds")]
    [Tooltip("Upper Y limit.")]
    [SerializeField] private float yMax = 1.0f;

    [Tooltip("Lower Y limit.")]
    [SerializeField] private float yMin = -1.8f;

    [Header("Activation")]
    [Tooltip("처음부터 움직일지 여부. 보통 false로 두고 SolsFinalGame에서 Activate()로 시작합니다.")]
    [SerializeField] private bool startActive = false;

    private Rigidbody2D rb;
    private int yDir; // +1 up, -1 down

    private bool activeMove = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // Random initial direction: + or -
        yDir = (Random.value < 0.5f) ? 1 : -1;

        activeMove = startActive;
    }

    public void Activate()
    {
        activeMove = true;
    }

    public void Deactivate()
    {
        activeMove = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void Update()
    {
        if (!activeMove) return;

        if (rb == null)
        {
            MoveTransform(Time.deltaTime);
        }
    }

    void FixedUpdate()
    {
        if (!activeMove) return;

        if (rb != null)
        {
            MoveRigidbody(Time.fixedDeltaTime);
        }
    }

    private void MoveTransform(float dt)
    {
        Vector3 p = transform.position;

        // X decreases
        p.x -= xSpeed * dt;

        // Y oscillates between yMin and yMax
        p.y += ySpeed * yDir * dt;

        if (yDir > 0 && p.y >= yMax)
        {
            p.y = yMax;
            yDir = -1;
        }
        else if (yDir < 0 && p.y <= yMin)
        {
            p.y = yMin;
            yDir = 1;
        }

        transform.position = p;
    }

    private void MoveRigidbody(float dt)
    {
        Vector2 p = rb.position;

        // X decreases
        p.x -= xSpeed * dt;

        // Y oscillates between yMin and yMax
        p.y += ySpeed * yDir * dt;

        if (yDir > 0 && p.y >= yMax)
        {
            p.y = yMax;
            yDir = -1;
        }
        else if (yDir < 0 && p.y <= yMin)
        {
            p.y = yMin;
            yDir = 1;
        }

        rb.MovePosition(p);
    }
}
