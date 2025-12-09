using UnityEngine;
using Vector2 = UnityEngine.Vector2;

[DisallowMultipleComponent]
public class PlayerMove : MonoBehaviour, NpcEventDebugLoader.IPlayerControlToggle
{
    [Header("이동 설정 / Movement")]
    public float moveSpeed = 1f;

    [Header("컨트롤 잠금 / Control Lock")]
    [Tooltip("외부(패널/이벤트)에서 이 값을 false로 만들면 플레이어가 멈춥니다.")]
    public bool controlEnabled = true;

    [Header("UI 잠금 연동 / UI Lock Integration")]
    [SerializeField] private UIExclusiveManager uiLock;

    [Header("걷기 효과음 설정")]
    [SerializeField] private bool enableWalkSound = true;
    [SerializeField] private string defaultWalkSFXKey = "StoneRoad"; // 기본 효과음을 StoneRoad로 변경
    [SerializeField] private string stoneRoadSFXKey = "StoneRoad";
    [SerializeField] private string grassRoadSFXKey = "GrassRoad";

    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 moveDirection;

    // 외부 연출이 애니를 구동할 때 true
    private bool externalAnimDriving = false;

    // 걷기 효과음 관련
    private AudioSource currentWalkSFX;
    private string currentGroundTag = ""; // 현재 밟고 있는 지면 태그
    private string lastPlayedSFXKey = ""; // 마지막으로 재생한 효과음 키
    private bool isMoving = false;
    private bool wasMoving = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (uiLock == null) uiLock = FindFirstObjectByType<UIExclusiveManager>();
        if (rb == null) Debug.LogWarning("[PlayerMove] Rigidbody2D가 없습니다.");
        if (animator == null) Debug.LogWarning("[PlayerMove] Animator가 없습니다.");
    }

    void Update()
    {
        // ✅ 최종 이동 가능 여부를 매 프레임 계산
        bool uiLocked = (uiLock != null && uiLock.IsAnyActive);
        bool effectiveEnabled = controlEnabled && !uiLocked;

        if (!effectiveEnabled)
        {
            moveDirection = Vector2.zero;
            isMoving = false;

            if (!externalAnimDriving && animator != null)
            {
                var st = animator.GetCurrentAnimatorStateInfo(0);
                animator.Play(st.shortNameHash, 0, 0f);
                animator.speed = 0f;
            }

            // 멈추면 걷기 효과음 정지
            if (wasMoving)
            {
                StopWalkSFX();
                wasMoving = false;
            }
            return;
        }

        // ===== 입력 처리 =====
        float moveX = 0f, moveY = 0f;
        if (Input.GetKey(KeyCode.W)) moveY += 1f;
        if (Input.GetKey(KeyCode.S)) moveY -= 1f;
        if (Input.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D)) moveX += 1f;

        moveDirection = new Vector2(moveX, moveY).normalized;
        isMoving = (moveDirection != Vector2.zero);

        if (animator == null) return;

        // ===== 입력 기반 애니메이션 =====
        if (moveDirection != Vector2.zero)
        {
            animator.speed = 1f;
            PlayWalkByVector(moveDirection);
        }
        else
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(st.shortNameHash, 0, 0f);
            animator.speed = 0f;
        }

        // ===== 걷기 효과음 처리 =====
        if (enableWalkSound)
        {
            if (isMoving && !wasMoving)
            {
                // 걷기 시작
                UpdateWalkSFX();
            }
            else if (!isMoving && wasMoving)
            {
                // 걷기 멈춤
                StopWalkSFX();
            }
            else if (isMoving)
            {
                // 걷는 중 - 지면이 바뀌었는지 체크
                UpdateWalkSFX();
            }
        }

        wasMoving = isMoving;
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        bool uiLocked = (uiLock != null && uiLock.IsAnyActive);
        bool effectiveEnabled = controlEnabled && !uiLocked;

        if (!effectiveEnabled)
        {
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(rb.position);
            return;
        }

        rb.MovePosition(rb.position + moveDirection * moveSpeed * Time.fixedDeltaTime);
    }

    // ===== 충돌 감지 (지면 태그 체크) =====
    void OnCollisionEnter2D(Collision2D collision)
    {
        CheckGroundTag(collision.gameObject);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        CheckGroundTag(collision.gameObject);
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (string.IsNullOrEmpty(currentGroundTag))
            return;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        CheckGroundTag(other.gameObject);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        CheckGroundTag(other.gameObject);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (string.IsNullOrEmpty(currentGroundTag))
            return;
        {
            currentGroundTag = "";
        }
    }

    private void CheckGroundTag(GameObject ground)
    {
        if (ground.CompareTag("Rock"))
        {
            currentGroundTag = "Rock";
        }
        else if (ground.CompareTag("Grass"))
        {
            currentGroundTag = "Grass";
        }
    }

    // ===== 걷기 효과음 시스템 =====

    /// <summary>
    /// 걷기 효과음 업데이트 (지면에 따라 루프 효과음 전환)
    /// </summary>
    private void UpdateWalkSFX()
    {
        if (!enableWalkSound)
            return;

        // 현재 지면에 맞는 효과음 키 결정
        string sfxKey = defaultWalkSFXKey;

        if (currentGroundTag == "Rock" && !string.IsNullOrEmpty(stoneRoadSFXKey))
        {
            sfxKey = stoneRoadSFXKey;
        }
        else if (currentGroundTag == "Grass" && !string.IsNullOrEmpty(grassRoadSFXKey))
        {
            sfxKey = grassRoadSFXKey;
        }

        // 효과음이 바뀌었으면 재생 전환
        if (lastPlayedSFXKey != sfxKey)
        {
            StopWalkSFX();
            PlayWalkSFX(sfxKey);
        }
        // 효과음이 재생 중이 아니면 시작
        else if (currentWalkSFX == null)
        {
            PlayWalkSFX(sfxKey);
        }
    }

    /// <summary>
    /// 걷기 효과음 시작 (루프)
    /// </summary>
    private void PlayWalkSFX(string sfxKey)
    {
        if (!enableWalkSound || string.IsNullOrEmpty(sfxKey))
            return;

        // 이전 효과음이 있으면 정지
        StopWalkSFX();

        if (SoundManager.Instance != null)
        {
            currentWalkSFX = SoundManager.Instance.PlaySFXLoop(sfxKey);
            lastPlayedSFXKey = sfxKey;
        }
    }

    /// <summary>
    /// 걷기 효과음 정지
    /// </summary>
    private void StopWalkSFX()
    {
        if (currentWalkSFX != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.StopSFXSource(currentWalkSFX);
            currentWalkSFX = null;
            lastPlayedSFXKey = "";
        }
    }

    // ==== 외부/이벤트 제어용 유틸리티 ====
    public void SetControlEnabled(bool enabled)
    {
        controlEnabled = enabled;
        if (!enabled) Freeze();
        else Unfreeze(keepAnimatorState: true);
    }

    public void Freeze()
    {
        controlEnabled = false;
        moveDirection = Vector2.zero;
        isMoving = false;
        if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }

        if (animator != null && !externalAnimDriving)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(st.shortNameHash, 0, 0f);
            animator.speed = 0f;
        }

        // 걷기 효과음 정지
        StopWalkSFX();
        wasMoving = false;
    }

    public void Unfreeze() => Unfreeze(false);

    public void Unfreeze(bool keepAnimatorState)
    {
        externalAnimDriving = false;
        controlEnabled = true;

        if (animator != null && !keepAnimatorState)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(st.shortNameHash, 0, 0f);
            animator.speed = 0f;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Freeze (Lock Controls)")] private void CtxFreeze() => Freeze();
    [ContextMenu("Unfreeze (Unlock Controls)")] private void CtxUnfreeze() => Unfreeze();
#endif

    // ====== 외부(연출) 전용 애니 훅 ======
    public void ExternalAnim_PlayWalk(Vector2 dir, float animSpeed = 0.85f)
    {
        if (animator == null) return;
        externalAnimDriving = true;

        if (dir.sqrMagnitude < 1e-6f)
        {
            ExternalAnim_StopIdle();
            return;
        }

        animator.speed = Mathf.Max(0f, animSpeed);
        PlayWalkByVector(dir.normalized);
    }

    public void ExternalAnim_StopIdle()
    {
        if (animator == null) return;
        externalAnimDriving = false;

        var st = animator.GetCurrentAnimatorStateInfo(0);
        animator.Play(st.shortNameHash, 0, 0f);
        animator.speed = 0f;

        // 외부 애니메이션 종료 시 걷기 효과음 정지
        StopWalkSFX();
    }

    private void PlayWalkByVector(Vector2 dir)
    {
        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
        {
            if (dir.x < 0f) animator.Play("Left_Walk");
            else animator.Play("Right_Walk");
        }
        else
        {
            if (dir.y > 0f) animator.Play("Back_Walk");
            else animator.Play("Front_Walk");
        }
    }

    void OnDisable()
    {
        // 비활성화 시 효과음 정지
        StopWalkSFX();
    }

    void OnDestroy()
    {
        // 파괴 시 효과음 정지
        StopWalkSFX();
    }
}