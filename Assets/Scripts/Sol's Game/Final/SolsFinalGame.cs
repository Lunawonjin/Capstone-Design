using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class SolsFinalGame : MonoBehaviour
{
    [Header("시작 설정")]
    [Tooltip("게임 시작 시 플레이어가 위치할 좌표")]
    [SerializeField] private Vector3 startPosition = new Vector3(0, 7, 0);

    [Header("대사 설정 (착지 시)")]
    [Tooltip("활성화할 Dialogue UI 오브젝트")]
    [SerializeField] private GameObject dialogueUIObject;
    [SerializeField] private DialogueRunnerStringTables dialogueRunner;
    [Tooltip("착지 후 출력할 대사 이벤트 키")]
    [SerializeField] private string landingDialogueEvent = "Boss_Sol_FinalGame_Second";
    [Tooltip("착지 후 대사 시작 전 대기 시간")]
    [SerializeField] private float landingDialogueDelay = 0.1f; // [추가] 0.1초 딜레이

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

    // [추가] 대사 관련 상호작용(F키) 차단 플래그
    private bool isInteractionBlocked = false;

    // 착지 이벤트 발생 여부 체크용
    private bool _hasLandedOnce = false;

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

        // 대사 실행기 자동 탐색
        if (dialogueRunner == null)
            dialogueRunner = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);

        // [추가] 대사 종료 이벤트 구독
        if (dialogueRunner != null)
        {
            dialogueRunner.OnDialogueEnded += OnLandingDialogueEnded;
        }

        if (playerRb != null)
        {
            originalConstraints = playerRb.constraints;
            lastCanMoveX = CanMoveX();
            ApplyRigidbodyXConstraint(lastCanMoveX);
        }
    }

    // [추가] 이벤트 구독 해제 (안전장치)
    void OnDestroy()
    {
        if (dialogueRunner != null)
        {
            dialogueRunner.OnDialogueEnded -= OnLandingDialogueEnded;
        }
    }

    void Start()
    {
        if (playerRb != null)
        {
            playerRb.position = startPosition;
            playerRb.transform.position = startPosition;
            playerRb.linearVelocity = Vector2.zero;
        }
        else
        {
            transform.position = startPosition;
        }
    }

    void Update()
    {
        bool syringeOverlapped = CheckSyringeOverlap();

        // [변경] isInteractionBlocked가 false일 때만 F키 상호작용 허용
        if (!isInteractionBlocked && !cameraMoving && syringeOverlapped && syringeObject != null && Input.GetKeyDown(interactKey))
        {
            syringeObject.SetActive(false);
            hasPickedUpSyringe = true;

            if (syringeShooter != null)
                syringeShooter.SetShootingEnabled(true);

            SetLookRightStatic();

            if (wallObject != null)
                wallObject.SetActive(false);

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
            // --- 공중에 있을 때 ---
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
            // --- 바닥에 닿았을 때 ---

            // [변경] 첫 착지 로직 수정
            if (!_hasLandedOnce)
            {
                _hasLandedOnce = true;

                // 1. 강제 컨트롤 잠금 & F키 상호작용 차단
                forceControlLocked = true;
                isInteractionBlocked = true;

                // 2. 물리 속도 정지 및 이동 컴포넌트 비활성
                if (playerRb != null) playerRb.linearVelocity = Vector2.zero;
                if (movementComponent != null) movementComponent.enabled = false;

                // 애니메이터 복구 (착지 모션 등을 위해)
                if (playerAnimator != null)
                    playerAnimator.enabled = animatorWasEnabledBeforeAir;

                // 3. [변경] 0.1초 딜레이 후 대사 시작 코루틴 호출
                StartCoroutine(Co_StartLandingDialogue());

                return;
            }

            // --- 일반적인 착지 처리 ---
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

    // [추가] 0.1초 대기 후 UI를 켜고 대사 시작
    private IEnumerator Co_StartLandingDialogue()
    {
        yield return new WaitForSeconds(landingDialogueDelay); // 0.1초 대기

        // UI 활성화
        if (dialogueUIObject != null)
        {
            dialogueUIObject.SetActive(true);
        }

        // 대사 시작
        if (dialogueRunner != null && !string.IsNullOrEmpty(landingDialogueEvent))
        {
            dialogueRunner.BeginWithEventName(landingDialogueEvent);
        }
    }

    // [추가] 대사가 끝났을 때 호출되는 콜백 (잠금 해제)
    private void OnLandingDialogueEnded()
    {
        // 1. Dialogue UI 비활성화
        if (dialogueUIObject != null)
        {
            dialogueUIObject.SetActive(false);
        }

        // 2. F키 상호작용 잠금 해제
        isInteractionBlocked = false;

        // 3. 플레이어 이동 컨트롤 잠금 해제
        forceControlLocked = false;

        // 4. 이동 컴포넌트 다시 켜기 (공중에서 꺼진 것 복구)
        if (movementComponent != null)
        {
            movementComponent.enabled = true;
        }

        // 물리 제약 업데이트
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

        // 2. 대기
        float delay = Mathf.Max(0f, impactDelay);
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // 3. 쾅!
        yield return StartCoroutine(CameraPunchRoutine(endPos, impactPunchDuration, impactPunchMagnitude));

        // 4. 쉐이크
        yield return StartCoroutine(CameraShakeRoutine(endPos, impactShakeDuration, impactShakeMagnitude));

        targetCamera.position = endPos;

        if (keepControlLockedAfterCamera)
            forceControlLocked = true;

        ApplyGroundState(isGrounded);

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