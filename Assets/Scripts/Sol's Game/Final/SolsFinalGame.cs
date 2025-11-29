using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class SolsFinalGame : MonoBehaviour
{
    [Header("플레이어 참조(비우면 자동 탐색)")]
    [SerializeField] private SpriteRenderer playerSpriteRenderer;
    [SerializeField] private Rigidbody2D playerRb;
    [SerializeField] private Collider2D playerCollider;
    [SerializeField] private Animator playerAnimator;

    [Header("이동 스크립트(둘 중 하나 자동 탐색)")]
    [SerializeField] private MonoBehaviour movementComponent; // PlayerMover 또는 PlayerMove

    [Header("낙하 상태 스프라이트")]
    [Tooltip("Ground 태그 바닥과 닿지 않았을 때 플레이어 스프라이트로 바꿀 이미지")]
    [SerializeField] private Sprite fallingSprite;

    [Header("바닥 판정")]
    [Tooltip("바닥 오브젝트는 Tag가 Ground여야 합니다.")]
    [SerializeField] private string groundTag = "Ground";
    [Tooltip("발 밑 검사 박스 높이")]
    [SerializeField] private float groundCheckHeight = 0.08f;
    [Tooltip("발 밑 검사 박스 여유값")]
    [SerializeField] private float groundCheckPadding = 0.02f;

    [Header("주사기 상호작용(Trigger여야 함)")]
    [Tooltip("씬에 있는 주사기 오브젝트를 직접 넣어주세요.")]
    [SerializeField] private GameObject syringeObject;
    [Tooltip("비우면 syringeObject에서 자동 탐색")]
    [SerializeField] private Collider2D syringeCollider;
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("Wall 비활성화")]
    [Tooltip("카메라 연출 시작 전에 비활성화할 Wall 오브젝트")]
    [SerializeField] private GameObject wallObject;

    [Header("카메라 이동")]
    [SerializeField] private Transform targetCamera;
    [SerializeField] private float cameraTargetX = 13f;
    [Tooltip("카메라가 목표 위치까지 이동하는 시간(초)")]
    [SerializeField] private float cameraMoveDuration = 1.0f;

    [Header("카메라 임팩트(더 콰과광)")]
    [Tooltip("카메라 도착 후 임팩트까지 대기 시간(초)")]
    [SerializeField] private float impactDelay = 1.0f;

    [Tooltip("임팩트 '펀치' 강도(먼저 한 번 크게 튐)")]
    [SerializeField] private float impactPunchMagnitude = 0.35f;
    [Tooltip("임팩트 '펀치' 지속 시간(초)")]
    [SerializeField] private float impactPunchDuration = 0.08f;

    [Tooltip("임팩트 쉐이크 강도(그 다음 덜컥거림)")]
    [SerializeField] private float impactShakeMagnitude = 0.22f;
    [Tooltip("임팩트 쉐이크 지속 시간(초)")]
    [SerializeField] private float impactShakeDuration = 0.28f;

    [Header("컨트롤 잠금 유지")]
    [Tooltip("카메라 연출 후에도 이동 컨트롤 잠금을 유지합니다.")]
    [SerializeField] private bool keepControlLockedAfterCamera = true;

    [Header("카메라 연출 후 적 활성화")]
    [Tooltip("카메라 연출이 끝난 뒤 Activate()할 적들")]
    [SerializeField] private SolsFinalGameEnemy[] enemiesToActivate;

    private Sprite originalSprite;
    private bool moveWasEnabledBeforeAir = true;
    private bool animatorWasEnabledBeforeAir = true;

    private bool isGrounded = true;
    private readonly Collider2D[] groundHits = new Collider2D[16];

    private bool cameraMoving = false;

    // 카메라 연출 이후 강제 컨트롤 잠금
    private bool forceControlLocked = false;

    void Awake()
    {
        if (playerSpriteRenderer == null)
            playerSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (playerRb == null)
            playerRb = GetComponentInChildren<Rigidbody2D>();

        if (playerCollider == null)
            playerCollider = GetComponentInChildren<Collider2D>();

        if (playerAnimator == null)
            playerAnimator = GetComponentInChildren<Animator>();

        if (movementComponent == null)
        {
            var pm1 = GetComponentInChildren<PlayerMover>();
            if (pm1 != null) movementComponent = pm1;
            else
            {
                var pm2 = GetComponentInChildren<PlayerMove>();
                if (pm2 != null) movementComponent = pm2;
            }
        }

        if (targetCamera == null && Camera.main != null)
            targetCamera = Camera.main.transform;

        if (playerSpriteRenderer != null)
            originalSprite = playerSpriteRenderer.sprite;

        if (syringeCollider == null && syringeObject != null)
            syringeCollider = syringeObject.GetComponentInChildren<Collider2D>();
    }

    void Update()
    {
        bool syringeOverlapped = CheckSyringeOverlap();

        if (!cameraMoving && syringeOverlapped && syringeObject != null && Input.GetKeyDown(interactKey))
        {
            syringeObject.SetActive(false);

            if (wallObject != null)
                wallObject.SetActive(false);

            MoveCameraToX(cameraTargetX);
        }
    }

    void FixedUpdate()
    {
        bool nowGrounded = CheckGroundedUnderFeetByTag();

        if (nowGrounded != isGrounded)
        {
            isGrounded = nowGrounded;
            ApplyGroundState(isGrounded);
        }

        if (!isGrounded && playerRb != null)
        {
            Vector2 v = playerRb.linearVelocity;
            v.x = 0f;
            playerRb.linearVelocity = v;
        }
    }

    private void ApplyGroundState(bool grounded)
    {
        if (!grounded)
        {
            if (playerSpriteRenderer != null && fallingSprite != null)
                playerSpriteRenderer.sprite = fallingSprite;

            if (playerAnimator != null)
            {
                animatorWasEnabledBeforeAir = playerAnimator.enabled;
                playerAnimator.enabled = false;
            }

            if (movementComponent != null)
            {
                moveWasEnabledBeforeAir = movementComponent.enabled;
                movementComponent.enabled = false;
            }
        }
        else
        {
            if (playerSpriteRenderer != null)
                playerSpriteRenderer.sprite = originalSprite;

            if (playerAnimator != null)
                playerAnimator.enabled = animatorWasEnabledBeforeAir;

            if (movementComponent != null)
            {
                if (forceControlLocked)
                {
                    movementComponent.enabled = false;
                }
                else
                {
                    movementComponent.enabled = moveWasEnabledBeforeAir;
                }
            }
        }
    }

    private bool CheckGroundedUnderFeetByTag()
    {
        if (playerCollider == null) return false;

        Bounds b = playerCollider.bounds;

        Vector2 boxCenter = new Vector2(b.center.x, b.min.y - groundCheckHeight * 0.5f - groundCheckPadding);
        Vector2 boxSize = new Vector2(b.size.x * 0.9f, groundCheckHeight);

        int count = Physics2D.OverlapBoxNonAlloc(boxCenter, boxSize, 0f, groundHits);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = groundHits[i];
            if (col == null) continue;

            if (col == playerCollider || col.transform.IsChildOf(transform)) continue;

            if (HasGroundTagUpwards(col.transform)) return true;
        }

        return false;
    }

    private bool HasGroundTagUpwards(Transform t)
    {
        if (t == null) return false;
        if (t.CompareTag(groundTag)) return true;
        if (t.parent != null && t.parent.CompareTag(groundTag)) return true;
        if (t.root != null && t.root.CompareTag(groundTag)) return true;
        return false;
    }

    private bool CheckSyringeOverlap()
    {
        if (syringeObject == null || !syringeObject.activeInHierarchy) return false;
        if (playerCollider == null || !playerCollider.enabled) return false;

        if (syringeCollider == null)
            syringeCollider = syringeObject.GetComponentInChildren<Collider2D>();

        if (syringeCollider == null || !syringeCollider.enabled) return false;

        ColliderDistance2D dist = Physics2D.Distance(playerCollider, syringeCollider);
        return dist.isOverlapped;
    }

    private void MoveCameraToX(float x)
    {
        if (targetCamera == null) return;
        if (cameraMoving) return;

        StartCoroutine(CameraMoveRoutine(x));
    }

    private IEnumerator CameraMoveRoutine(float x)
    {
        cameraMoving = true;

        if (movementComponent != null)
            movementComponent.enabled = false;

        if (playerRb != null)
        {
            playerRb.linearVelocity = Vector2.zero;
            playerRb.angularVelocity = 0f;
        }

        Vector3 startPos = targetCamera.position;
        Vector3 endPos = new Vector3(x, startPos.y, startPos.z);

        float duration = Mathf.Max(0.01f, cameraMoveDuration);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            targetCamera.position = Vector3.Lerp(startPos, endPos, eased);
            yield return null;
        }

        targetCamera.position = endPos;

        float delay = Mathf.Max(0f, impactDelay);
        if (delay > 0f) yield return new WaitForSeconds(delay);

        yield return StartCoroutine(CameraPunchRoutine(endPos, impactPunchDuration, impactPunchMagnitude));
        yield return StartCoroutine(CameraShakeRoutine(endPos, impactShakeDuration, impactShakeMagnitude));

        targetCamera.position = endPos;

        if (keepControlLockedAfterCamera)
            forceControlLocked = true;

        ApplyGroundState(isGrounded);

        // 카메라 연출 "완전히 끝난 뒤" 적 시작
        ActivateEnemies();

        cameraMoving = false;
    }

    private void ActivateEnemies()
    {
        if (enemiesToActivate == null) return;

        for (int i = 0; i < enemiesToActivate.Length; i++)
        {
            if (enemiesToActivate[i] != null)
                enemiesToActivate[i].Activate();
        }
    }

    private IEnumerator CameraPunchRoutine(Vector3 basePos, float duration, float magnitude)
    {
        float d = Mathf.Max(0.01f, duration);
        float m = Mathf.Max(0f, magnitude);

        Vector3 downPos = basePos + new Vector3(0f, -m, 0f);

        float half = d * 0.5f;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / half;
            float eased = Mathf.Clamp01(t);
            targetCamera.position = Vector3.Lerp(basePos, downPos, eased);
            yield return null;
        }

        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / half;
            float eased = Mathf.Clamp01(t);
            targetCamera.position = Vector3.Lerp(downPos, basePos, eased);
            yield return null;
        }
    }

    private IEnumerator CameraShakeRoutine(Vector3 basePos, float duration, float magnitude)
    {
        float d = Mathf.Max(0.01f, duration);
        float m = Mathf.Max(0f, magnitude);

        float elapsed = 0f;
        while (elapsed < d)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / d);
            float damper = 1f - (p * p);

            float ox = (Random.value * 2f - 1f) * m * damper;
            float oy = (Random.value * 2f - 1f) * m * damper;

            targetCamera.position = basePos + new Vector3(ox, oy, 0f);
            yield return null;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (playerCollider == null) return;
        Bounds b = playerCollider.bounds;

        Vector2 boxCenter = new Vector2(b.center.x, b.min.y - groundCheckHeight * 0.5f - groundCheckPadding);
        Vector2 boxSize = new Vector2(b.size.x * 0.9f, groundCheckHeight);

        Gizmos.DrawWireCube(boxCenter, boxSize);
    }
#endif
}
