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
    [SerializeField] private MonoBehaviour movementComponent;

    [Header("낙하 상태 스프라이트")]
    [SerializeField] private Sprite fallingSprite;

    [Header("주사기 줍고 나서 모습(애니메이터용)")]
    [SerializeField] private string rightIdleStateName = "Right_Walk";

    [Header("바닥 판정")]
    [SerializeField] private string groundTag = "Ground";
    [SerializeField] private float groundCheckHeight = 0.08f;
    [SerializeField] private float groundCheckPadding = 0.02f;

    [Header("주사기 상호작용(Trigger여야 함)")]
    [SerializeField] private GameObject syringeObject;
    [SerializeField] private Collider2D syringeCollider;
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("주사기 발사 컨트롤")]
    [SerializeField] private SyringePoolShooter syringeShooter;

    [Header("Wall 비활성화")]
    [SerializeField] private GameObject wallObject;

    [Header("카메라 이동")]
    [SerializeField] private Transform targetCamera;
    [SerializeField] private float cameraTargetX = 13f;
    [SerializeField] private float cameraMoveDuration = 1.0f;

    [Header("카메라 임팩트(더 콰과광)")]
    [SerializeField] private float impactDelay = 1.0f;
    [SerializeField] private float impactPunchMagnitude = 0.35f;
    [SerializeField] private float impactPunchDuration = 0.08f;
    [SerializeField] private float impactShakeMagnitude = 0.22f;
    [SerializeField] private float impactShakeDuration = 0.28f;

    [Header("컨트롤 잠금 유지")]
    [Tooltip("카메라 연출 후에도 이동 컨트롤 잠금을 유지할지 여부")]
    [SerializeField] private bool keepControlLockedAfterCamera = true;

    [Header("카메라 연출 후 적 활성화")]
    [SerializeField] private SolsFinalGameEnemy[] enemiesToActivate;

    private bool moveWasEnabledBeforeAir = true;
    private bool animatorWasEnabledBeforeAir = true;

    private bool isGrounded = true;
    private readonly Collider2D[] groundHits = new Collider2D[16];

    private bool cameraMoving = false;
    private bool forceControlLocked = false;
    private RigidbodyConstraints2D originalConstraints;
    private bool lastCanMoveX = true;
    private bool hasPickedUpSyringe = false;

    void Awake()
    {
        if (playerSpriteRenderer == null) playerSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (playerRb == null) playerRb = GetComponentInChildren<Rigidbody2D>();
        if (playerCollider == null) playerCollider = GetComponentInChildren<Collider2D>();
        if (playerAnimator == null) playerAnimator = GetComponentInChildren<Animator>();

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

        if (targetCamera == null && Camera.main != null) targetCamera = Camera.main.transform;
        if (syringeCollider == null && syringeObject != null) syringeCollider = syringeObject.GetComponentInChildren<Collider2D>();
        if (syringeShooter == null) syringeShooter = GetComponentInChildren<SyringePoolShooter>();

        if (syringeShooter != null) syringeShooter.SetShootingEnabled(false);

        if (playerRb != null)
        {
            originalConstraints = playerRb.constraints;
            lastCanMoveX = CanMoveX();
            ApplyRigidbodyXConstraint(lastCanMoveX);
        }
    }

    void Update()
    {
        bool syringeOverlapped = CheckSyringeOverlap();

        // [주사기 획득 시점]
        if (!cameraMoving && syringeOverlapped && syringeObject != null && Input.GetKeyDown(interactKey))
        {
            syringeObject.SetActive(false);
            hasPickedUpSyringe = true;

            if (syringeShooter != null)
                syringeShooter.SetShootingEnabled(true);

            SetLookRightStatic();

            if (wallObject != null)
                wallObject.SetActive(false);

            // 주의: 여기서 적을 활성화하지 않고 카메라 이동 코루틴 시작만 함
            MoveCameraToX(cameraTargetX);
        }

        if (playerRb != null)
        {
            bool canMoveXNow = CanMoveX();
            if (canMoveXNow != lastCanMoveX)
            {
                lastCanMoveX = canMoveXNow;
                ApplyRigidbodyXConstraint(lastCanMoveX);
            }
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
            if (!hasPickedUpSyringe)
            {
                if (playerAnimator != null)
                    playerAnimator.enabled = animatorWasEnabledBeforeAir;

                if (movementComponent != null)
                {
                    if (forceControlLocked)
                        movementComponent.enabled = false;
                    else
                        movementComponent.enabled = moveWasEnabledBeforeAir;
                }
            }
            else
            {
                SetLookRightStatic();
            }
        }

        if (playerRb != null)
        {
            lastCanMoveX = CanMoveX();
            ApplyRigidbodyXConstraint(lastCanMoveX);
        }
    }

    private bool CanMoveX()
    {
        return movementComponent != null && movementComponent.enabled && !forceControlLocked;
    }

    private void ApplyRigidbodyXConstraint(bool canMoveX)
    {
        if (playerRb == null) return;

        if (canMoveX)
            playerRb.constraints = originalConstraints & ~RigidbodyConstraints2D.FreezePositionX;
        else
            playerRb.constraints = originalConstraints | RigidbodyConstraints2D.FreezePositionX;
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
        if (syringeCollider == null) syringeCollider = syringeObject.GetComponentInChildren<Collider2D>();
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
            lastCanMoveX = CanMoveX();
            ApplyRigidbodyXConstraint(lastCanMoveX);
        }

        Vector3 startPos = targetCamera.position;
        Vector3 endPos = new Vector3(x, startPos.y, startPos.z);

        // 1. 카메라 이동
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

        // 2. 대기 (임팩트 전 긴장감)
        float delay = Mathf.Max(0f, impactDelay);
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // 3. 쾅! (펀치)
        yield return StartCoroutine(CameraPunchRoutine(endPos, impactPunchDuration, impactPunchMagnitude));

        // 4. 덜덜덜 (쉐이크)
        yield return StartCoroutine(CameraShakeRoutine(endPos, impactShakeDuration, impactShakeMagnitude));

        targetCamera.position = endPos;

        if (keepControlLockedAfterCamera)
            forceControlLocked = true;

        ApplyGroundState(isGrounded);

        // [변경] 모든 카메라 연출이 끝난 "지금" 적들을 활성화!
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

    private void SetLookRightStatic()
    {
        if (playerAnimator == null) return;
        if (string.IsNullOrEmpty(rightIdleStateName)) return;

        playerAnimator.enabled = true;
        playerAnimator.Play(rightIdleStateName, 0, 0f);
        playerAnimator.speed = 0f;
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