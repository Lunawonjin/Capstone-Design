// PlayerJump.cs
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 스페이스 점프(계단식 상승 후 제어된 낙하).
/// - 스페이스: 빠르게 Y를 정확히 riseAmount 만큼 상승(riseDuration)
/// - 상승 완료 → preFallDelay 대기 → 동적 하강 시작
/// - 하강 동안 y속도 직접 제어(fallStartSpeed → fallAccel, 최대 |fallMaxSpeed|)
/// - "Block" 착지(법선 위쪽) 시: 점프 중이었다면 BlockSpawnManager에 카메라 스텝 트리거
/// - "NoBlock" 충돌 시: (±3, +3)로 '한 번만' 튕긴 뒤 **완전히 떨어지면** Fail Panel 표시
///   · Yes → 처음부터 재시작(restartSceneName 규칙)
///   · No  → goToSceneOnNo로 이동
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerJump : MonoBehaviour
{
    [Header("입력")]
    public KeyCode jumpKey = KeyCode.Space;

    [Header("상승(정확한 +Y)")]
    public float riseAmount = 3f;
    public float riseDuration = 0.10f;

    [Header("상승 후 대기")]
    public float preFallDelay = 0.5f;

    [Header("하강(속도 직접 제어)")]
    public float fallStartSpeed = 2.5f;
    public float fallAccel = 18f;
    public float fallMaxSpeed = 12f;

    [Header("점프 조건(선택)")]
    public bool onlyJumpOnBlock = false;

    [Header("카메라 트리거(선택)")]
    public BlockSpawnManager spawnManager;

    [Header("카메라 연출 단순화")]
    public bool useSimpleCameraPulse = true;
    public float simplePunchOvershoot = 0.12f;
    public float simpleDownKick = 0.06f;
    public float simpleUpDuration = 0.07f;
    public float simpleReturnDuration = 0.07f;

    [Header("실패 패널")]
    public GameObject failPanel;
    public Button yesButton;
    public Button noButton;

    [Header("실패 후 씬 이동 설정")]
    [Tooltip("Yes: 처음부터. 비워두면 현재 씬 재시작. '__BUILD_INDEX_0__'은 빌드 0번")]
    public string restartSceneName = "";
    [Tooltip("No: 여기 지정한 씬으로 이동(비면 패널만 닫고 계속 플레이)")]
    public string goToSceneOnNo = "";

    // ── 실패 트리거(‘완전히 떨어진 뒤’ 기준) ─────────────────────
    [Header("Fail 트리거(완전 낙하 조건)")]
    [Tooltip("카메라 하단을 동적으로 기준으로 사용할지 여부")]
    public bool useCameraBottomThreshold = true;
    [Tooltip("카메라 하단에서 추가로 더 내려가야 하는 여유(월드 단위, 음수면 하단보다 더 아래)")]
    public float cameraBottomOffset = -0.5f;
    [Tooltip("고정 임계 Y. useCameraBottomThreshold=false일 때 사용")]
    public float fixedFailY = -10f;
    [Tooltip("낙하를 확실히 유도하기 위한 최소 하강 초기 속도")]
    public float forceDropMinSpeed = 10f;

    // 내부 상태
    Rigidbody2D rb;
    bool onBlock = false;
    bool inJump = false;
    bool controlFall = false;
    bool jumpedThisAir = false;
    bool _didKnockbackOnce = false; // NoBlock 1회만 튕김
    bool _failShown = false;        // Fail Panel 노출 여부
    Coroutine jumpCo;
    Coroutine _waitFallCo;

    float _savedTimeScale = 1f;
    bool _savedSimulated;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;

        if (failPanel) failPanel.SetActive(false);
        if (yesButton) yesButton.onClick.AddListener(OnClickYesRestart);
        if (noButton) noButton.onClick.AddListener(OnClickNoGoToScene);

        // 카메라 1펄스 느낌으로 단순화
        if (useSimpleCameraPulse && spawnManager != null)
        {
            spawnManager.punchBounces = 0;
            spawnManager.shakeAmplitude = 0f;
            spawnManager.punchOvershoot = simplePunchOvershoot;
            spawnManager.punchDownKick = simpleDownKick;
            spawnManager.punchUpDuration = simpleUpDuration;
            spawnManager.punchBounceDuration = simpleReturnDuration;
        }
    }

    void OnDestroy()
    {
        if (yesButton) yesButton.onClick.RemoveListener(OnClickYesRestart);
        if (noButton) noButton.onClick.RemoveListener(OnClickNoGoToScene);
    }

    void Update()
    {
        if (_failShown) return; // 실패 상태면 입력 무시

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
        if (_failShown) return;

        if (controlFall)
        {
            float vy = rb.linearVelocity.y;
            vy -= fallAccel * Time.fixedDeltaTime;
            float cap = -Mathf.Abs(fallMaxSpeed);
            if (vy < cap) vy = cap;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, vy);
        }
    }

    IEnumerator CoJump()
    {
        inJump = true;
        controlFall = false;
        onBlock = false;
        jumpedThisAir = true;
        _didKnockbackOnce = false; // 새 점프에서 1회 다시 허용

        // 1) 정확상승
        var prevType = rb.bodyType;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;

        Vector3 start = transform.position;
        Vector3 target = new Vector3(start.x, start.y + riseAmount, start.z);
        float dur = Mathf.Max(0.001f, riseDuration);
        float t = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            float e = 1f - (1f - u) * (1f - u); // EaseOutQuad
            rb.MovePosition(Vector3.LerpUnclamped(start, target, e));
            yield return null;
        }
        rb.MovePosition(target);

        // 2) 대기
        if (preFallDelay > 0f)
            yield return new WaitForSeconds(preFallDelay);

        // 3) 하강 시작
        rb.bodyType = prevType; // 대개 Dynamic
        var v = rb.linearVelocity; v.y = -Mathf.Abs(fallStartSpeed);
        rb.linearVelocity = v;

        controlFall = true;
        inJump = false;
        jumpCo = null;
    }

    // ───────── 충돌 처리 ─────────
    void OnCollisionEnter2D(Collision2D col) { HandleCollision_Enter(col); }
    void OnCollisionStay2D(Collision2D col) { HandleCollision_Stay(col); }
    void OnCollisionExit2D(Collision2D col)
    {
        if (col.collider.CompareTag("Block")) onBlock = false;
    }

    void HandleCollision_Enter(Collision2D col)
    {
        if (_failShown) return;

        // Block 착지
        if (col.collider.CompareTag("Block"))
        {
            foreach (var c in col.contacts)
            {
                if (c.normal.y > 0.5f)
                {
                    onBlock = true;
                    controlFall = false;
                    var v = rb.linearVelocity; v.y = 0f; rb.linearVelocity = v;

                    if (jumpedThisAir && spawnManager != null)
                        spawnManager.TriggerCameraStep();

                    jumpedThisAir = false;
                    break;
                }
            }
            return;
        }

        // NoBlock: 한 번만 튕기고, '완전 낙하'까지 기다렸다가 Fail
        if (col.collider.CompareTag("NoBlock") && !_didKnockbackOnce)
        {
            _didKnockbackOnce = true;

            Vector3 center = col.collider.bounds.center;
            bool fromLeft = transform.position.x < center.x;
            float dx = fromLeft ? +3f : -3f;

            // 1회 튕김
            transform.position += new Vector3(dx, +3f, 0f);

            // 강제 낙하 유도(즉시 큰 음수 vy 부여)
            controlFall = true;
            var v = rb.linearVelocity;
            v.y = -Mathf.Max(fallStartSpeed, forceDropMinSpeed);
            rb.linearVelocity = v;

            // 이미 대기 중인 Fail 코루틴이 없으면 시작
            if (_waitFallCo == null)
                _waitFallCo = StartCoroutine(CoWaitFallThenFail());
        }
    }

    void HandleCollision_Stay(Collision2D col)
    {
        if (_failShown) return;

        if (col.collider.CompareTag("Block"))
        {
            foreach (var c in col.contacts)
            {
                if (c.normal.y > 0.5f) { onBlock = true; break; }
            }
        }
    }

    // ───────── ‘완전 낙하’ 대기 후 Fail ─────────
    IEnumerator CoWaitFallThenFail()
    {
        // 살짝 유예(충돌 직후 프레임 흔들림 방지)
        yield return null;

        // 임계 Y 계산 함수
        float GetThresholdY()
        {
            if (useCameraBottomThreshold && Camera.main != null)
            {
                var cam = Camera.main;
                float bottom = cam.transform.position.y - cam.orthographicSize;
                return bottom + cameraBottomOffset;
            }
            return fixedFailY;
        }

        // 플레이어가 임계선 아래로 내려갈 때까지 대기
        while (true)
        {
            float thr = GetThresholdY();
            if (transform.position.y <= thr) break;
            yield return null;
        }

        // 도달 시 Fail Panel 표시 + 게임 정지
        EnterFailState();
        _waitFallCo = null;
    }

    // ───────── 실패 상태 ─────────
    void EnterFailState()
    {
        if (_failShown) return;
        _failShown = true;

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        _savedSimulated = rb.simulated;
        rb.simulated = false;

        if (failPanel) failPanel.SetActive(true);
    }

    void ExitFailState_Unfreeze()
    {
        Time.timeScale = _savedTimeScale;
        rb.simulated = _savedSimulated;
        _failShown = false;
    }

    // 버튼: Yes → 처음부터
    void OnClickYesRestart()
    {
        if (failPanel) failPanel.SetActive(false);

        string sceneToLoad = restartSceneName;
        if (string.IsNullOrWhiteSpace(sceneToLoad))
        {
            sceneToLoad = SceneManager.GetActiveScene().name;
        }
        else if (sceneToLoad == "__BUILD_INDEX_0__")
        {
            ExitFailState_Unfreeze();
            SceneManager.LoadScene(0);
            return;
        }

        ExitFailState_Unfreeze();
        SceneManager.LoadScene(sceneToLoad);
    }

    // 버튼: No → 지정 씬
    void OnClickNoGoToScene()
    {
        if (failPanel) failPanel.SetActive(false);

        if (string.IsNullOrWhiteSpace(goToSceneOnNo))
        {
            ExitFailState_Unfreeze();
            return;
        }

        ExitFailState_Unfreeze();
        SceneManager.LoadScene(goToSceneOnNo);
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

        simplePunchOvershoot = Mathf.Max(0f, simplePunchOvershoot);
        simpleDownKick = Mathf.Max(0f, simpleDownKick);
        simpleUpDuration = Mathf.Max(0.01f, simpleUpDuration);
        simpleReturnDuration = Mathf.Max(0.01f, simpleReturnDuration);
    }
#endif
}
