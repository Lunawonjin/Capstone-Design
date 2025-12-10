using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class SolsFinalGame : MonoBehaviour
{
    [Header("핵심 연결 (PlayerMover 확인 필수)")]
    [Tooltip("플레이어 이동 스크립트")]
    [SerializeField] private PlayerMover targetMover;

    [Header("시작 설정")]
    [SerializeField] private Vector3 startPosition = new Vector3(0, 7, 0);

    [Header("대사 설정 (착지 시)")]
    [SerializeField] private GameObject dialogueUIObject;
    [SerializeField] private DialogueRunnerStringTables dialogueRunner;
    [SerializeField] private string landingDialogueEvent = "Boss_Sol_FinalGame_Second";
    [SerializeField] private float landingDialogueDelay = 0.1f;

    [Header("플레이어 참조")]
    [SerializeField] private SpriteRenderer playerSpriteRenderer;
    [SerializeField] private Rigidbody2D playerRb;
    [SerializeField] private Collider2D playerCollider;
    [SerializeField] private Animator playerAnimator;

    [Header("낙하/착지 설정")]
    [SerializeField] private Sprite fallingSprite;
    [SerializeField] private string rightIdleStateName = "Right_Walk";
    [SerializeField] private string groundTag = "Ground";
    [SerializeField] private float groundCheckHeight = 0.08f;
    [SerializeField] private float groundCheckPadding = 0.02f;

    [Header("주사기 및 연출")]
    [SerializeField] private GameObject syringeObject;
    [SerializeField] private Collider2D syringeCollider;
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private SyringePoolShooter syringeShooter;
    [SerializeField] private GameObject wallObject;
    [SerializeField] private Transform targetCamera;
    [SerializeField] private float cameraTargetX = 13f;
    [SerializeField] private float cameraMoveDuration = 1.0f;
    [SerializeField] private float impactDelay = 1.0f;
    [SerializeField] private float impactPunchMagnitude = 0.35f;
    [SerializeField] private float impactPunchDuration = 0.08f;
    [SerializeField] private float impactShakeMagnitude = 0.22f;
    [SerializeField] private float impactShakeDuration = 0.28f;
    [SerializeField] private bool keepControlLockedAfterCamera = true;
    [SerializeField] private SolsFinalGameEnemy[] enemiesToActivate;

    [Header("보스 인트로 대사 설정")]
    [SerializeField] private string bossIntroDialogueKey = "Boss_Sol_FinalGame_Third";

    [Header("보스 카메라 이동 설정")]
    [SerializeField] private float bossCameraTargetX = 30f;
    [SerializeField] private float bossCameraMoveDuration = 1.5f;

    [Header("보스 오브젝트 설정")]
    [Tooltip("보스 UI (HP 바 등)")]
    [SerializeField] public GameObject bossUIObject;
    [Tooltip("보스 오브젝트")]
    [SerializeField] public GameObject bossObject;

    [Header("카메라 추적 설정")]
    [Tooltip("보스전 후 활성화할 카메라 추적 스크립트")]
    [SerializeField] public SimpleCameraFollow cameraFollow;

    [Header("효과음 설정")]
    [SerializeField] private string interactSFXKey = "F";
    [SerializeField] private string bossComeSFXKey = "BossCome";

    private bool moveWasEnabledBeforeAir = true;
    private bool animatorWasEnabledBeforeAir = true;
    private bool isGrounded = true;
    private readonly Collider2D[] groundHits = new Collider2D[16];
    private bool cameraMoving = false;
    private bool forceControlLocked = false;
    private RigidbodyConstraints2D originalConstraints;
    private bool lastCanMoveX = true;
    private bool hasPickedUpSyringe = false;
    private bool isInteractionBlocked = false;
    private bool _hasLandedOnce = false;

    private bool hasPlayedBossIntroDialogue = false;
    private bool isPlayingBossDialogue = false;

    private bool isLandingDialogueActive = false;

    public static SolsFinalGame Instance { get; private set; }

    // ========================================
    // 🔹 Public 메서드 (외부에서 호출)
    // ========================================

    public bool IsPlayingBossDialogue()
    {
        return isPlayingBossDialogue;
    }

    public bool IsDialogueUIActive()
    {
        if (dialogueUIObject == null) return false;
        if (!dialogueUIObject.activeSelf) return false;

        bool anyChildActive = false;
        foreach (Transform child in dialogueUIObject.transform)
        {
            if (child.gameObject.activeSelf)
            {
                anyChildActive = true;
                break;
            }
        }

        return anyChildActive;
    }

    /// <summary>
    /// 보스 소환 시퀀스 트리거 (Enemy에서 호출)
    /// </summary>
    public void TriggerBossSpawnSequence(float delay)
    {
        StartCoroutine(DelayedBossSpawn(delay));
    }

    private IEnumerator DelayedBossSpawn(float delay)
    {
        yield return new WaitForSeconds(delay);

        Debug.Log("[SolsFinalGame] 보스 소환 시퀀스 시작!");

        // 이미 시퀀스가 실행 중인지 확인
        if (FindAnyObjectByType<BossSpawnSequence>() != null)
        {
            Debug.LogWarning("[SolsFinalGame] ⚠️ 이미 보스 소환 시퀀스가 실행 중입니다!");
            yield break;
        }

        GameObject sequenceRunner = new GameObject("BossSpawnSequenceRunner");
        BossSpawnSequence sequence = sequenceRunner.AddComponent<BossSpawnSequence>();
        sequence.StartSequence(0f); // delay는 이미 적용됨
    }

    // ========================================
    // 🔹 Unity 생명주기
    // ========================================

    void Awake()
    {
        // ⭐ 가장 먼저 적 카운트 리셋 (Start보다 먼저 실행되도록)
        SolsFinalGameEnemy.ResetEnemyCount();

        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (playerSpriteRenderer == null) playerSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (playerRb == null) playerRb = GetComponentInChildren<Rigidbody2D>();
        if (playerCollider == null) playerCollider = GetComponentInChildren<Collider2D>();
        if (playerAnimator == null) playerAnimator = GetComponentInChildren<Animator>();

        if (targetMover == null) targetMover = GetComponentInChildren<PlayerMover>();

        if (targetCamera == null && Camera.main != null) targetCamera = Camera.main.transform;
        if (syringeCollider == null && syringeObject != null) syringeCollider = syringeObject.GetComponentInChildren<Collider2D>();
        if (syringeShooter == null) syringeShooter = GetComponentInChildren<SyringePoolShooter>();
        if (syringeShooter != null) syringeShooter.SetShootingEnabled(false);

        if (dialogueRunner == null)
            dialogueRunner = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);

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

        if (bossUIObject != null) bossUIObject.SetActive(false);
        if (bossObject != null) bossObject.SetActive(false);

        if (cameraFollow == null && targetCamera != null)
        {
            cameraFollow = targetCamera.GetComponent<SimpleCameraFollow>();
        }

        if (cameraFollow != null)
        {
            cameraFollow.enabled = false;
            Debug.Log("[SolsFinalGame] SimpleCameraFollow 초기 비활성화");
        }
        else
        {
            Debug.LogWarning("[SolsFinalGame] SimpleCameraFollow를 찾을 수 없습니다!");
        }

        Debug.Log($"[SolsFinalGame] bossUIObject: {(bossUIObject != null ? bossUIObject.name : "NULL!")}");
        Debug.Log($"[SolsFinalGame] bossObject: {(bossObject != null ? bossObject.name : "NULL!")}");
        Debug.Log($"[SolsFinalGame] cameraFollow: {(cameraFollow != null ? "있음" : "NULL!")}");
    }

    void OnDestroy()
    {
        if (dialogueRunner != null)
        {
            dialogueRunner.OnDialogueEnded -= OnLandingDialogueEnded;
        }

        if (Instance == this)
        {
            Instance = null;
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

        // ⭐ 디버그: 실제 적 개수 확인 (1프레임 후)
        StartCoroutine(DelayedEnemyCountCheck());
    }

    private IEnumerator DelayedEnemyCountCheck()
    {
        yield return null; // 모든 적의 Start가 끝날 때까지 대기
        SolsFinalGameEnemy.DebugCountAllEnemies();
    }

    void Update()
    {
        bool syringeOverlapped = CheckSyringeOverlap();

        if (!isInteractionBlocked && !cameraMoving && syringeOverlapped && syringeObject != null && Input.GetKeyDown(interactKey))
        {
            // ⭐ F키 상호작용 효과음 재생
            PlayInteractSFX();

            syringeObject.SetActive(false);
            hasPickedUpSyringe = true;
            if (syringeShooter != null) syringeShooter.SetShootingEnabled(true);
            SetLookRightStatic();
            if (wallObject != null) wallObject.SetActive(false);
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

        // ⭐ 디버그: E키로 적 카운트 확인
        if (Input.GetKeyDown(KeyCode.E))
        {
            SolsFinalGameEnemy.DebugCountAllEnemies();
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

    // ========================================
    // 🔹 착지 및 제어 시스템
    // ========================================

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
        }
        else
        {
            if (!_hasLandedOnce)
            {
                _hasLandedOnce = true;
                forceControlLocked = true;
                isInteractionBlocked = true;

                if (targetMover != null)
                {
                    targetMover.enabled = true;
                    targetMover.SetControlEnabled(false);
                    Debug.Log("★ [SolsFinalGame] 착지 확인! PlayerMover 조작(ControlEnabled) 비활성화 완료.");
                }

                if (playerRb != null) playerRb.linearVelocity = Vector2.zero;
                if (playerAnimator != null) playerAnimator.enabled = animatorWasEnabledBeforeAir;

                StartCoroutine(Co_StartLandingDialogue());
                return;
            }

            if (!hasPickedUpSyringe)
            {
                if (playerAnimator != null) playerAnimator.enabled = animatorWasEnabledBeforeAir;

                if (targetMover != null)
                {
                    targetMover.enabled = true;

                    if (forceControlLocked)
                    {
                        targetMover.SetControlEnabled(false);
                    }
                    else
                    {
                        if (moveWasEnabledBeforeAir) targetMover.SetControlEnabled(true);
                    }
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

    private IEnumerator Co_StartLandingDialogue()
    {
        yield return new WaitForSeconds(landingDialogueDelay);

        isLandingDialogueActive = true;

        if (syringeShooter != null)
        {
            syringeShooter.SetShootingEnabled(false);
            Debug.Log("[SolsFinalGame] 착지 대사 중 공격 비활성화");
        }

        if (dialogueUIObject != null) dialogueUIObject.SetActive(true);
        if (dialogueRunner != null && !string.IsNullOrEmpty(landingDialogueEvent))
        {
            dialogueRunner.BeginWithEventName(landingDialogueEvent);
        }
    }

    private void OnLandingDialogueEnded()
    {
        if (!isLandingDialogueActive)
        {
            Debug.Log("[SolsFinalGame] 착지 대사가 아니므로 OnLandingDialogueEnded 무시");
            return;
        }

        isLandingDialogueActive = false;

        Debug.Log("[SolsFinalGame] 착지 대사 종료 처리");

        if (dialogueUIObject != null) dialogueUIObject.SetActive(false);

        isInteractionBlocked = false;
        forceControlLocked = false;

        if (targetMover != null)
        {
            targetMover.enabled = true;
            targetMover.SetControlEnabled(true);
        }

        if (hasPickedUpSyringe && syringeShooter != null)
        {
            syringeShooter.SetShootingEnabled(true);
            Debug.Log("[SolsFinalGame] 착지 대사 종료 후 공격 활성화");
        }

        if (playerRb != null)
        {
            lastCanMoveX = CanMoveX();
            ApplyRigidbodyXConstraint(lastCanMoveX);
        }
    }

    private bool CanMoveX()
    {
        if (forceControlLocked) return false;
        if (targetMover != null && !targetMover.controlEnabled) return false;
        return targetMover != null && targetMover.enabled;
    }

    private void ApplyRigidbodyXConstraint(bool canMoveX)
    {
        if (playerRb == null) return;
        if (canMoveX) playerRb.constraints = originalConstraints & ~RigidbodyConstraints2D.FreezePositionX;
        else playerRb.constraints = originalConstraints | RigidbodyConstraints2D.FreezePositionX;
    }

    // ========================================
    // 🔹 충돌 및 상호작용
    // ========================================

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
            if (col == null || col == playerCollider || col.transform.IsChildOf(transform)) continue;
            if (HasGroundTagUpwards(col.transform)) return true;
        }
        return false;
    }

    private bool HasGroundTagUpwards(Transform t)
    {
        if (t == null) return false;
        if (t.CompareTag(groundTag)) return true;
        if (t.parent != null) return HasGroundTagUpwards(t.parent);
        return false;
    }

    private bool CheckSyringeOverlap()
    {
        if (syringeObject == null || !syringeObject.activeInHierarchy) return false;
        if (playerCollider == null || !playerCollider.enabled) return false;
        if (syringeCollider == null || !syringeCollider.enabled) return false;
        return Physics2D.Distance(playerCollider, syringeCollider).isOverlapped;
    }

    // ========================================
    // 🔹 카메라 이동 및 적 활성화
    // ========================================

    private void MoveCameraToX(float x)
    {
        if (targetCamera == null || cameraMoving) return;
        StartCoroutine(CameraMoveRoutine(x));
    }

    private IEnumerator CameraMoveRoutine(float x)
    {
        cameraMoving = true;

        if (targetMover != null)
        {
            targetMover.enabled = true;
            targetMover.SetControlEnabled(false);
        }

        if (syringeShooter != null)
        {
            syringeShooter.SetShootingEnabled(false);
            Debug.Log("[SolsFinalGame] 카메라 연출 중 공격 비활성화");
        }

        if (playerRb != null)
        {
            playerRb.linearVelocity = Vector2.zero;
            playerRb.angularVelocity = 0f;
            lastCanMoveX = CanMoveX();
            ApplyRigidbodyXConstraint(lastCanMoveX);
        }

        Vector3 startPos = targetCamera.position;
        Vector3 endPos = new Vector3(x, startPos.y, startPos.z);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, cameraMoveDuration);
            targetCamera.position = Vector3.Lerp(startPos, endPos, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        targetCamera.position = endPos;

        yield return new WaitForSeconds(Mathf.Max(0f, impactDelay));
        yield return StartCoroutine(CameraPunchRoutine(endPos, impactPunchDuration, impactPunchMagnitude));
        yield return StartCoroutine(CameraShakeRoutine(endPos, impactShakeDuration, impactShakeMagnitude));

        targetCamera.position = endPos;

        if (keepControlLockedAfterCamera) forceControlLocked = true;
        ActivateEnemies();

        if (syringeShooter != null)
        {
            syringeShooter.SetShootingEnabled(true);
            Debug.Log("[SolsFinalGame] 카메라 연출 후 공격 활성화");
        }

        cameraMoving = false;
    }

    private void ActivateEnemies()
    {
        if (enemiesToActivate == null) return;
        for (int i = 0; i < enemiesToActivate.Length; i++)
            if (enemiesToActivate[i] != null) enemiesToActivate[i].Activate();
    }

    private IEnumerator CameraPunchRoutine(Vector3 basePos, float duration, float magnitude)
    {
        float d = Mathf.Max(0.01f, duration);
        Vector3 downPos = basePos + new Vector3(0f, -Mathf.Max(0f, magnitude), 0f);
        float t = 0f;
        while (t < 1f) { t += Time.deltaTime / (d * 0.5f); targetCamera.position = Vector3.Lerp(basePos, downPos, t); yield return null; }
        t = 0f;
        while (t < 1f) { t += Time.deltaTime / (d * 0.5f); targetCamera.position = Vector3.Lerp(downPos, basePos, t); yield return null; }
    }

    private IEnumerator CameraShakeRoutine(Vector3 basePos, float duration, float magnitude)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float damper = 1f - (elapsed / duration);
            targetCamera.position = basePos + new Vector3((Random.value * 2f - 1f) * magnitude * damper, (Random.value * 2f - 1f) * magnitude * damper, 0f);
            yield return null;
        }
    }

    private void SetLookRightStatic()
    {
        if (playerAnimator == null || string.IsNullOrEmpty(rightIdleStateName)) return;
        playerAnimator.enabled = true;
        playerAnimator.Play(rightIdleStateName, 0, 0f);
        playerAnimator.speed = 0f;
    }

    // ==========================================
    // ★★★ 보스 등장 시퀀스용 카메라 시스템 ★★★
    // ==========================================

    /// <summary>
    /// 보스로 카메라 이동
    /// </summary>
    public IEnumerator MoveCameraToBoss()
    {
        if (targetCamera == null)
        {
            Debug.LogWarning("[SolsFinalGame] targetCamera가 null!");
            yield break;
        }

        Debug.Log("[SolsFinalGame] 카메라를 보스로 이동 시작");

        // ⭐ BossCome 효과음 재생
        PlayBossComeSFX();

        Vector3 startPos = targetCamera.position;
        Vector3 endPos = new Vector3(bossCameraTargetX, startPos.y, startPos.z);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, bossCameraMoveDuration);
            targetCamera.position = Vector3.Lerp(startPos, endPos, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        targetCamera.position = endPos;
        Debug.Log("[SolsFinalGame] 카메라 보스 도착 완료");
    }

    /// <summary>
    /// 플레이어로 카메라 복귀
    /// </summary>
    public IEnumerator MoveCameraToPlayer()
    {
        if (targetCamera == null || playerRb == null)
        {
            Debug.LogWarning("[SolsFinalGame] targetCamera 또는 playerRb가 null!");
            yield break;
        }

        Debug.Log("[SolsFinalGame] 카메라를 플레이어로 복귀 시작");

        Vector3 startPos = targetCamera.position;
        Vector3 playerPos = playerRb.position;
        Vector3 endPos = new Vector3(playerPos.x, startPos.y, startPos.z);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, bossCameraMoveDuration);
            targetCamera.position = Vector3.Lerp(startPos, endPos, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        targetCamera.position = endPos;
        Debug.Log("[SolsFinalGame] 카메라 플레이어 복귀 완료");
    }

    /// <summary>
    /// 보스 시퀀스용 대사 실행 (제어 잠금 없음)
    /// </summary>
    public IEnumerator PlayBossIntroDialogueForSequence()
    {
        Debug.Log("[SolsFinalGame] 보스 인트로 대사 시작");

        if (dialogueUIObject != null)
        {
            Transform current = dialogueUIObject.transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }
                current = current.parent;
            }
        }

        if (dialogueRunner != null)
        {
            Transform current = dialogueRunner.transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }
                current = current.parent;
            }

            yield return null;

            dialogueRunner.BeginWithEventName(bossIntroDialogueKey);
            Debug.Log($"[SolsFinalGame] 보스 인트로 대사 실행: {bossIntroDialogueKey}");

            bool dialogueEnded = false;
            System.Action onDialogueEnd = () =>
            {
                dialogueEnded = true;
                Debug.Log("[SolsFinalGame] 보스 인트로 대사 종료 감지");
            };

            dialogueRunner.OnDialogueEnded += onDialogueEnd;

            while (!dialogueEnded)
            {
                yield return null;
            }

            dialogueRunner.OnDialogueEnded -= onDialogueEnd;

            if (dialogueUIObject != null)
            {
                dialogueUIObject.SetActive(false);
                Debug.Log("[SolsFinalGame] DialogueUI 비활성화");
            }
        }

        Debug.Log("[SolsFinalGame] 보스 인트로 대사 완료");
    }

    // ==========================================
    // ★★★ 보스 인트로 대사 시스템 (기존) ★★★
    // ==========================================

    public void StartBossIntroDialogue()
    {
        if (!hasPlayedBossIntroDialogue && !isPlayingBossDialogue)
        {
            StartCoroutine(PlayBossIntroDialogue());
        }
        else
        {
            Debug.Log("[SolsFinalGame] 보스 인트로 대사는 이미 재생되었습니다.");
        }
    }

    private IEnumerator PlayBossIntroDialogue()
    {
        isPlayingBossDialogue = true;
        hasPlayedBossIntroDialogue = true;

        Debug.Log("[SolsFinalGame] ========== 보스 인트로 대사 시작 ==========");

        if (targetMover != null)
        {
            targetMover.SetControlEnabled(false);
        }

        if (syringeShooter != null)
        {
            syringeShooter.SetShootingEnabled(false);
            Debug.Log("[SolsFinalGame] 보스 인트로 대사 중 공격 비활성화");
        }

        if (dialogueUIObject != null)
        {
            Transform current = dialogueUIObject.transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }
                current = current.parent;
            }
        }

        if (dialogueRunner != null)
        {
            Transform current = dialogueRunner.transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }
                current = current.parent;
            }

            yield return null;

            dialogueRunner.BeginWithEventName(bossIntroDialogueKey);
            Debug.Log("[SolsFinalGame] 보스 인트로 대사 실행 중");

            bool dialogueEnded = false;
            System.Action onDialogueEnd = () =>
            {
                dialogueEnded = true;
                Debug.Log("[SolsFinalGame] 보스 인트로 대사 종료 감지");
            };

            dialogueRunner.OnDialogueEnded += onDialogueEnd;

            while (!dialogueEnded)
            {
                yield return null;
            }

            dialogueRunner.OnDialogueEnded -= onDialogueEnd;

            if (dialogueUIObject != null)
            {
                dialogueUIObject.SetActive(false);
                Debug.Log("[SolsFinalGame] DialogueUI 비활성화");
            }
        }

        Debug.Log("[SolsFinalGame] ========== 보스 활성화 시작 ==========");

        if (bossUIObject != null)
        {
            bossUIObject.SetActive(true);
            Debug.Log("[SolsFinalGame] ✅ BossUI 활성화!");
        }
        else
        {
            Debug.LogError("[SolsFinalGame] ❌ bossUIObject가 null!");
        }

        if (bossObject != null)
        {
            bossObject.SetActive(true);
            Debug.Log("[SolsFinalGame] ✅ Boss 활성화!");
        }
        else
        {
            Debug.LogError("[SolsFinalGame] ❌ bossObject가 null!");
        }

        if (cameraFollow != null)
        {
            cameraFollow.enabled = true;
            Debug.Log("[SolsFinalGame] ✅ 카메라 추적 활성화!");
        }
        else
        {
            Debug.LogWarning("[SolsFinalGame] ❌ cameraFollow가 null!");
        }

        if (targetMover != null)
        {
            targetMover.SetControlEnabled(true);
            Debug.Log("[SolsFinalGame] 플레이어 제어 복구");
        }

        if (syringeShooter != null)
        {
            syringeShooter.SetShootingEnabled(true);
            Debug.Log("[SolsFinalGame] 공격 활성화");
        }

        Debug.Log("[SolsFinalGame] ========== 보스 활성화 완료 ==========");

        isPlayingBossDialogue = false;
    }

    // ==========================================
    // ★★★ 효과음 시스템 ★★★
    // ==========================================

    /// <summary>
    /// F키 상호작용 효과음 재생
    /// </summary>
    private void PlayInteractSFX()
    {
        if (string.IsNullOrEmpty(interactSFXKey)) return;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(interactSFXKey);
            Debug.Log("[SolsFinalGame] ✅ F키 상호작용 효과음 재생");
        }
    }

    /// <summary>
    /// 보스 등장 카메라 연출 효과음 재생
    /// </summary>
    private void PlayBossComeSFX()
    {
        if (string.IsNullOrEmpty(bossComeSFXKey)) return;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(bossComeSFXKey);
            Debug.Log("[SolsFinalGame] ✅ BossCome 효과음 재생");
        }
    }
}