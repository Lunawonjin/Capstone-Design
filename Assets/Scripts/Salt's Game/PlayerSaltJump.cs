using UnityEngine;

/// <summary>
/// 물리 점프 + 그림자 Y고정(땅에 붙음)
/// - 3초 락 해제는 GameManger에서 UnlockJump 호출
/// - GameManger가 지면을 내릴 때 BeginGroundShift/EndGroundShiftAndRebase 사용
/// </summary>
public class PlayerSaltJumpPhysics : MonoBehaviour
{
    [Header("대상")]
    public Transform salt;
    public Transform shadow;

    [Header("입력")]
    public KeyCode jumpKey = KeyCode.Space;

    [Header("점프 파라미터")]
    public float jumpHeight = 1.8f;
    public float gravity = 20f;

    [Header("레이어 충돌 차단")]
    public string playerLayerName = "Player";
    public string mudLayerName = "Mud";

    [Header("그림자")]
    [Range(0.1f, 1f)] public float shadowMinScale = 0.6f;
    public bool shadowFollowX = true;
    [Tooltip("시작할 때 그림자를 월드로 분리(부모 해제)")]
    public bool detachShadowFromSaltAtStart = true;

    [Header("점프 잠금")]
    public bool jumpEnabled = false;   // 3초 후 GameManger에서 UnlockJump()

    // 외부 지면 이동 중이면 그림자 Y 고정을 잠시 끔
    [System.NonSerialized] private bool suspendShadowLock = false;

    // 내부
    private bool isJumping = false;
    private float groundY;
    private float v;
    private int playerLayer = -1, mudLayer = -1;

    private Vector3 shadowBaseScale;
    private float shadowLockY;

    void Start()
    {
        if (!salt) salt = transform;

        groundY = salt.position.y;
        playerLayer = LayerMask.NameToLayer(playerLayerName);
        mudLayer = LayerMask.NameToLayer(mudLayerName);

        if (shadow)
        {
            if (detachShadowFromSaltAtStart) shadow.SetParent(null, true); // ★ 부모 해제
            shadowBaseScale = shadow.localScale;
            shadowLockY = shadow.position.y; // 땅 Y
        }
    }

    void Update()
    {
        if (jumpEnabled && !isJumping && Input.GetKeyDown(jumpKey))
            StartJump();

        if (isJumping)
        {
            float dt = Time.deltaTime;
            v -= gravity * dt;

            var p = salt.position;
            p.y += v * dt;

            if (p.y <= groundY) { p.y = groundY; salt.position = p; EndJump(); }
            else { salt.position = p; }

            UpdateShadowScale(p.y);
        }
    }

    // ★ 모든 이동이 끝난 뒤 그림자 X만 따라가게(현재 설계 유지)
    void LateUpdate()
    {
        if (!shadow) return;

        var sp = shadow.position;
        if (shadowFollowX) sp.x = salt.position.x;

        shadow.position = sp;
    }

    private void StartJump()
    {
        isJumping = true;
        v = Mathf.Sqrt(Mathf.Max(0f, 2f * gravity * jumpHeight)); // v0 = sqrt(2gh)
        SetPlayerVsMudCollision(false);
    }

    private void EndJump()
    {
        isJumping = false; v = 0f;
        SetPlayerVsMudCollision(true);

        if (shadow)
        {
            shadow.localScale = shadowBaseScale;
            // Y는 LateUpdate에서 shadowLockY로 다시 고정됨(필요 시)
        }
    }

    private void UpdateShadowScale(float currentY)
    {
        if (!shadow) return;
        float height = Mathf.Clamp(currentY - groundY, 0f, jumpHeight);
        float t = jumpHeight > 0f ? height / jumpHeight : 0f; // 0~1
        shadow.localScale = shadowBaseScale * Mathf.Lerp(1f, shadowMinScale, t);
    }

    private void SetPlayerVsMudCollision(bool enabled)
    {
        if (playerLayer >= 0 && mudLayer >= 0)
            Physics2D.IgnoreLayerCollision(playerLayer, mudLayer, !enabled);
    }

    void OnDisable()
    {
        SetPlayerVsMudCollision(true);
        if (shadow) shadow.localScale = shadowBaseScale;
    }

    // ── 외부(매니저)에서 호출 ──
    public void RebaseToCurrentGround()
    {
        groundY = salt ? salt.position.y : transform.position.y;
        if (shadow) { shadowLockY = groundY; shadow.localScale = shadowBaseScale; }
    }

    public void UnlockJump() => jumpEnabled = true;
    public void LockJump() => jumpEnabled = false;   // ★ 점프 입력 차단

    // ★ 죽을 때 안전하게 점프 중단
    public void ForceCancelJump()
    {
        if (!isJumping) return;
        isJumping = false;
        v = 0f;
        SetPlayerVsMudCollision(true);

        // 착지 위치로 스냅
        var p = salt.position; p.y = groundY; salt.position = p;

        // 그림자 원상복구
        if (shadow) shadow.localScale = shadowBaseScale;
    }

    // 지면을 외부에서 움직일 때 호출 (그림자 Y고정 잠시 해제)
    public void BeginGroundShift() => suspendShadowLock = true;

    // 지면 이동 종료 → 고정 재개 + 새 지면 재설정
    public void EndGroundShiftAndRebase()
    {
        suspendShadowLock = false;
        RebaseToCurrentGround();
    }
}
