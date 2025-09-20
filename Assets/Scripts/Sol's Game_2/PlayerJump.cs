// PlayerJump.cs
using System.Collections;
using UnityEngine;

/// <summary>
/// 스페이스 점프(계단식 상승 후 제어된 낙하).
/// - 스페이스: 빠르게 Y를 정확히 riseAmount 만큼 상승(riseDuration)
/// - 상승 완료 → preFallDelay 대기 → 동적 하강 시작
/// - 하강 동안 y속도 직접 제어(fallStartSpeed → fallAccel, 최대 |fallMaxSpeed|)
/// - "Block" 착지(법선 위쪽) 시: 점프 중이었다면 BlockSpawnManager에 카메라 스텝 트리거
/// - "NoBlock" 충돌 시: 좌/우 판정하여 (±3, +3) 튕김 → 1초 뒤 급추락
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerJump : MonoBehaviour
{
    [Header("입력")]
    public KeyCode jumpKey = KeyCode.Space;

    [Header("상승(정확한 +Y)")]
    public float riseAmount = 3f;         // 정확히 +3 (인스펙터에서 조절)
    public float riseDuration = 0.10f;    // 빠르게 치고 오를 시간

    [Header("상승 후 대기")]
    public float preFallDelay = 0.5f;     // 0.5초 대기

    [Header("하강(속도 직접 제어)")]
    public float fallStartSpeed = 2.5f;   // 시작 하강 속도
    public float fallAccel = 18f;         // 하강 가속도
    public float fallMaxSpeed = 12f;      // 최대 하강 속도(|vy| 상한)

    [Header("점프 조건(선택)")]
    public bool onlyJumpOnBlock = false;  // 필요한 경우만 켜세요

    [Header("카메라 트리거")]
    [Tooltip("카메라 '쾅쾅' 스텝을 실행할 매니저 참조")]
    public BlockSpawnManager spawnManager; // 씬의 BlockSpawnManager Drag&Drop

    // 내부 상태
    Rigidbody2D rb;
    bool onBlock = false;     // 현재 'Block' 위 접지?
    bool inJump = false;      // 점프 루틴 진행 중(재입력 방지)
    bool controlFall = false; // 하강 수동 제어 활성화
    bool jumpedThisAir = false; // 이번 공중 상황이 점프 기인인지
    Coroutine jumpCo;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;
    }

    void Update()
    {
        if (Input.GetKeyDown(jumpKey))
        {
            if (inJump) return;
            if (onlyJumpOnBlock && !onBlock) return;

            if (jumpCo != null) StopCoroutine(jumpCo);
            jumpCo = StartCoroutine(CoJump());
        }
    }

    void FixedUpdate()
    {
        if (controlFall)
        {
            float vy = rb.velocity.y;
            vy -= fallAccel * Time.fixedDeltaTime;                // 아래로 가속
            float cap = -Mathf.Abs(fallMaxSpeed);                 // 최대 하강속도 캡
            if (vy < cap) vy = cap;
            rb.velocity = new Vector2(rb.velocity.x, vy);
        }
    }

    IEnumerator CoJump()
    {
        inJump = true;
        controlFall = false;
        onBlock = false;
        jumpedThisAir = true;

        // 1) Kinematic으로 정확히 +riseAmount 만큼 빠르게 상승
        var prevType = rb.bodyType;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.velocity = Vector2.zero;

        Vector3 start = transform.position;
        Vector3 target = new Vector3(start.x, start.y + riseAmount, start.z);
        float dur = Mathf.Max(0.001f, riseDuration);
        float t = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            // 빠르게 치고 오르게(EaseOutQuad)
            float e = 1f - (1f - u) * (1f - u);
            rb.MovePosition(Vector3.LerpUnclamped(start, target, e));
            yield return null;
        }
        rb.MovePosition(target);

        // 2) 상승 종료 후 대기
        if (preFallDelay > 0f)
            yield return new WaitForSeconds(preFallDelay);

        // 3) Dynamic 전환 + 하강 제어 시작
        rb.bodyType = prevType; // 보통 Dynamic
        var v = rb.velocity; v.y = -Mathf.Abs(fallStartSpeed);
        rb.velocity = v;

        controlFall = true; // 낙하 y속도 직접 제어

        inJump = false;
        jumpCo = null;
    }

    // ───────── 충돌 처리 ─────────
    void OnCollisionEnter2D(Collision2D col) { HandleCollision(col); }
    void OnCollisionStay2D(Collision2D col) { HandleCollision(col); }
    void OnCollisionExit2D(Collision2D col)
    {
        if (col.collider.CompareTag("Block")) onBlock = false;
    }

    void HandleCollision(Collision2D col)
    {
        // 1) Block 착지 판정(법선이 위쪽)
        if (col.collider.CompareTag("Block"))
        {
            foreach (var c in col.contacts)
            {
                if (c.normal.y > 0.5f)
                {
                    onBlock = true;
                    controlFall = false; // 착지 시 낙하 제어 종료
                    // 착지 순간, 바로 다음 입력을 위해 vy 정리(선택)
                    var v = rb.velocity; v.y = 0f; rb.velocity = v;

                    // "점프 후 착지"라면 카메라 스텝 트리거
                    if (jumpedThisAir && spawnManager != null)
                    {
                        spawnManager.TriggerCameraStep();
                    }
                    jumpedThisAir = false;
                    break;
                }
            }
        }

        // 2) NoBlock 충돌: 좌/우 판정하여 (±3, +3) 튕김 → 1초 뒤 급추락
        if (col.collider.CompareTag("NoBlock"))
        {
            Vector3 center = col.collider.bounds.center;
            bool fromLeft = transform.position.x < center.x;
            float dx = fromLeft ? +3f : -3f;

            // 즉시 위치 튕김
            transform.position += new Vector3(dx, +3f, 0f);

            // 1초 뒤 급추락
            StartCoroutine(CoForceDropAfter(1f));
        }
    }

    IEnumerator CoForceDropAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        controlFall = true; // 낙하 제어 켜고
        var v = rb.velocity; v.y = -Mathf.Max(fallStartSpeed, 10f); // 확 떨어지게
        rb.velocity = v;
        jumpedThisAir = false;
        onBlock = false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        riseAmount = Mathf.Max(0.001f, riseAmount);
        riseDuration = Mathf.Max(0.001f, riseDuration);
        preFallDelay = Mathf.Max(0f, preFallDelay);
        fallStartSpeed = Mathf.Max(0f, fallStartSpeed);
        fallAccel = Mathf.Max(0f, fallAccel);
        fallMaxSpeed = Mathf.Max(0.01f, fallMaxSpeed);
    }
#endif
}
