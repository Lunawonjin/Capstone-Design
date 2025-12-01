using System.Collections;
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

    [Header("피격/생명")]
    [Tooltip("몇 대 맞으면 사라질지(기본 2)")]
    [SerializeField] private int hitsToDie = 2;

    [Header("피격 색 연출")]
    [Tooltip("피격 시 잠깐 바꿀 색")]
    [SerializeField] private Color hitColor = Color.red;
    [Tooltip("피격 색 유지 시간(초)")]
    [SerializeField] private float hitColorDuration = 0.1f;

    private Rigidbody2D rb;
    private int yDir; // +1 up, -1 down
    private bool isActive = false;
    private int currentHits = 0;

    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private Coroutine hitColorRoutine;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // 랜덤 초기 방향
        yDir = (Random.value < 0.5f) ? 1 : -1;

        // 자식 포함 모든 SpriteRenderer 저장
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        if (spriteRenderers != null && spriteRenderers.Length > 0)
        {
            originalColors = new Color[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                originalColors[i] = spriteRenderers[i].color;
            }
        }
    }

    void OnEnable()
    {
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void Update()
    {
        if (!isActive)
            return;

        if (rb == null)
        {
            MoveTransform(Time.deltaTime);
        }
    }

    void FixedUpdate()
    {
        if (!isActive)
            return;

        if (rb != null)
        {
            MoveRigidbody(Time.fixedDeltaTime);
        }
    }

    private void MoveTransform(float dt)
    {
        Vector3 p = transform.position;

        p.x -= xSpeed * dt;
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

        p.x -= xSpeed * dt;
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

    // SolsFinalGame에서 호출하는 활성화 함수
    public void Activate()
    {
        isActive = true;
    }

    public void Deactivate()
    {
        isActive = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    // 주사기에게 맞았을 때 호출
    public void TakeHit(Vector2 hitPosition)
    {
        currentHits++;

        // 피격 색 연출
        if (hitColorRoutine != null)
            StopCoroutine(hitColorRoutine);
        hitColorRoutine = StartCoroutine(HitColorFlashRoutine());

        if (currentHits >= hitsToDie)
        {
            Die();
        }
    }

    private IEnumerator HitColorFlashRoutine()
    {
        if (spriteRenderers == null || spriteRenderers.Length == 0)
            yield break;

        // 빨간색으로 변경
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            spriteRenderers[i].color = hitColor;
        }

        yield return new WaitForSeconds(hitColorDuration);

        // 아직 살아 있으면 원래 색으로 복원
        if (currentHits < hitsToDie && originalColors != null && originalColors.Length == spriteRenderers.Length)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                spriteRenderers[i].color = originalColors[i];
            }
        }
    }

    private void Die()
    {
        // 죽을 때는 그냥 비활성화
        gameObject.SetActive(false);
    }
}
