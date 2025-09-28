using System.Collections;
using UnityEngine;

/// <summary>
/// 발판을 순서대로 진행:
/// - 현재 발판이 불투명(알파=1)이면 밟고 다음 발판 검사
/// - 다음 발판이 불투명하지 않으면 그 발판까지 점프해 착지 즉시 낙하
/// - 모든 발판 통과 시 Animator의 "Clear" 재생
///
/// 추가:
/// - 착지 스냅(윗면 + 보정)으로 파고듦 방지
/// - 낙하 시작 시 fallingSprite로 교체(Animator 덮어쓰기 방지 옵션)
/// - 낙하가 "멈추는 순간" stoppedSprite로 재교체 + 지정 오브젝트 활성화
/// - 낙하 종료 후 Animator 복구 정책(유지/지연 복구) 설정 가능
/// </summary>
public class CharacterJumpOnEnd : MonoBehaviour
{
    [Header("발판 리스트(진행 순서대로)")]
    [Tooltip("좌→우 등 진행 순서대로 발판을 등록")]
    public GameObject[] platforms;

    [Header("한 칸 점프 설정")]
    [Tooltip("발판 하나를 건너는 데 걸리는 시간(초)")]
    public float hopDuration = 0.35f;
    [Tooltip("한 칸 점프 최고 높이")]
    public float hopHeight = 1.0f;

    [Header("착지 높이 보정")]
    [Tooltip("캐릭터 피벗에서 발바닥까지의 세로 거리(+면 위). 보통 SpriteRenderer.bounds.extents.y 근사값")]
    public float footOffset = 0.5f;
    [Tooltip("발판 윗면에서 아주 살짝 띄우는 여유값(파고듦 방지)")]
    public float landingPadding = 0.01f;

    [Header("낙하 연출")]
    [Tooltip("낙하 속도(유닛/초)")]
    public float fallSpeed = 6f;
    [Tooltip("얼마나 아래로 떨어질지(거리)")]
    public float fallDistance = 6f;

    [Header("스프라이트 교체")]
    [Tooltip("기본(평소) 스프라이트. 비워두면 현재 SpriteRenderer의 스프라이트 사용")]
    public Sprite normalSprite;
    [Tooltip("낙하 시작 시 교체할 스프라이트")]
    public Sprite fallingSprite;
    [Tooltip("낙하가 '멈춘 순간'에 표시할 스프라이트(있으면 이것이 우선 적용됨)")]
    public Sprite stoppedSprite;
    [Tooltip("stoppedSprite가 비어있을 때만, 낙하 종료 후 기본 스프라이트로 복구")]
    public bool restoreSpriteAfterFall = false;

    [Header("낙하 종료 시 오브젝트 활성화")]
    [Tooltip("낙하가 멈출 때 SetActive(true)로 켤 오브젝트(선택)")]
    public GameObject objectToActivateOnFallStop;
    public GameObject objectToActivateOnFallStop1;

    [Header("스프라이트 타깃(선택)")]
    [Tooltip("스프라이트를 바꿀 정확한 SpriteRenderer(자식일 수 있음). 비우면 자동 탐색")]
    public SpriteRenderer targetRenderer;

    [Header("애니메이션")]
    [Tooltip("애니메이터(선택). 마지막 패널 통과 시 'Clear' 재생")]
    public Animator animator;
    [Tooltip("낙하 중 Animator를 잠시 꺼서 스프라이트 교체가 덮어씌워지지 않도록 함")]
    public bool disableAnimatorDuringFall = true;

    [Header("Animator 복구 정책")]
    [Tooltip("낙하 멈춘 직후에도 Animator를 계속 꺼서 stoppedSprite가 유지되도록 함")]
    public bool keepAnimatorDisabledAfterFallStop = true;
    [Tooltip("keepAnimatorDisabledAfterFallStop=false일 때, 이 딜레이(초) 후 Animator를 다시 켬")]
    public float animatorReactivateDelay = 0.25f;

    // 내부 상태
    private bool isRunning;             // 중복 실행 방지
    private Rigidbody2D rb;             // 물리 간섭 차단용
    private bool prevKinematic;
    private bool hadRb;
    private SpriteRenderer sr;          // 스프라이트 교체 대상
    private bool prevAnimatorEnabled;

    void Awake()
    {
        // Rigidbody2D(선택) 확보: 점프 연출 중 물리 간섭 방지에 사용
        rb = GetComponent<Rigidbody2D>();
        if (rb != null) { hadRb = true; prevKinematic = rb.isKinematic; }

        // SpriteRenderer는 우선 targetRenderer, 없으면 자식까지 포함 탐색
        sr = (targetRenderer != null) ? targetRenderer : GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null && normalSprite == null) normalSprite = sr.sprite;

        // Animator 연결(인스펙터에서 비워두면 자기 자신에서 찾기)
        if (animator == null) animator = GetComponent<Animator>();
    }

    /// <summary>
    /// 외부(퍼즐 완료/타임아웃 등)에서 호출해 점프 시퀀스 시작
    /// </summary>
    public void TriggerJump()
    {
        if (isRunning || platforms == null || platforms.Length == 0) return;
        StartCoroutine(RunSequence());
    }

    /// <summary>
    /// 전체 진행: 발판별 점프 → 조건에 따라 낙하 또는 마지막 통과 시 클리어
    /// </summary>
    private IEnumerator RunSequence()
    {
        isRunning = true;

        // 점프 연출 동안 물리 간섭(충돌/중력)을 비활성화
        if (hadRb) { rb.isKinematic = true; rb.linearVelocity = Vector2.zero; }

        Vector3 cur = transform.position;

        for (int i = 0; i < platforms.Length; i++)
        {
            var curPf = platforms[i];
            if (curPf == null) continue;

            // 1) 현재 발판의 윗면 착지 지점 계산 후, 그 지점까지 한 칸 점프
            Vector3 curLand = GetLandingPoint(curPf);
            yield return StartCoroutine(JumpOneHop(cur, curLand));
            cur = curLand;

            // 현재 발판이 불투명(안전) 아니면 → 즉시 낙하(실패)
            if (!IsOpaque(curPf))
            {
                yield return StartCoroutine(FallDown(cur));   // 낙하 종료 시 stoppedSprite/오브젝트 활성화 처리
                Finish(false);
                yield break;
            }

            // 2) 다음 발판이 불투명(안전) 아니면 → 다음 발판까지 점프 후 그 자리에서 낙하(실패)
            if (i + 1 < platforms.Length)
            {
                var nextPf = platforms[i + 1];
                if (nextPf != null && !IsOpaque(nextPf))
                {
                    Vector3 nextLand = GetLandingPoint(nextPf);
                    yield return StartCoroutine(JumpOneHop(cur, nextLand));
                    cur = nextLand;

                    yield return StartCoroutine(FallDown(cur)); // 낙하 종료 시 stoppedSprite/오브젝트 활성화 처리
                    Finish(false);
                    yield break;
                }
            }
        }

        // 모든 발판이 불투명 → 끝까지 통과(클리어)
        Finish(true);
    }

    /// <summary>
    /// 한 칸 포물선 점프 (startPos → endPos)
    /// </summary>
    private IEnumerator JumpOneHop(Vector3 startPos, Vector3 endPos)
    {
        float t = 0f;
        while (t < hopDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / hopDuration);

            // 포물선 높이(중간에서 최대)
            float height = Mathf.Sin(Mathf.PI * u) * hopHeight;

            Vector3 pos = Vector3.Lerp(startPos, endPos, u);
            pos.y += height;

            transform.position = pos;
            yield return null;
        }

        // 착지 스냅(발판 윗면 + 보정 값)
        transform.position = endPos;
    }

    /// <summary>
    /// 현재 위치에서 아래로 낙하(연출).
    /// - 시작 시: fallingSprite 교체 / (옵션)Animator 비활성화
    /// - 끝  시: stoppedSprite 우선 적용, 없으면 restore 옵션에 따라 normalSprite 복구
    /// - 끝  시: objectToActivateOnFallStop를 활성화
    /// - Animator 복구 정책(유지/지연 복구) 적용
    /// </summary>
    private IEnumerator FallDown(Vector3 startPos)
    {
        // 1) 낙하 동안 Animator 비활성화(옵션) → 스프라이트 덮어쓰기 방지
        if (disableAnimatorDuringFall && animator != null)
        {
            prevAnimatorEnabled = animator.enabled;
            animator.enabled = false;
        }

        // 2) 낙하 시작: 스프라이트 교체
        if (sr != null && fallingSprite != null)
        {
            sr.sprite = fallingSprite;
        }

        // 3) 실제 낙하 이동
        float fallen = 0f;
        Vector3 pos = startPos;

        while (fallen < fallDistance)
        {
            float step = fallSpeed * Time.deltaTime;
            fallen += step;
            pos.y -= step;
            transform.position = pos;
            yield return null;
        }

        // 4) 낙하가 "멈추는 순간" 처리
        //    stoppedSprite가 있으면 최우선 적용 → Animator를 곧바로 켜지 않으면 유지됨
        if (sr != null)
        {
            if (stoppedSprite != null)
            {
                sr.sprite = stoppedSprite;            // 최종 정지 스프라이트 고정
            }
            else if (restoreSpriteAfterFall && normalSprite != null)
            {
                sr.sprite = normalSprite;             // 대체 복구
            }
        }

        // 5) 오브젝트 활성화
        if (objectToActivateOnFallStop != null)
        {
            objectToActivateOnFallStop.SetActive(true);
        }

        // 5) 오브젝트 활성화
        if (objectToActivateOnFallStop1 != null)
        {
            objectToActivateOnFallStop1.SetActive(true);
        }

        // 6) Animator 복구 정책
        if (disableAnimatorDuringFall && animator != null)
        {
            if (keepAnimatorDisabledAfterFallStop)
            {
                // 그대로 꺼둔다 → stoppedSprite 유지 확실
                // 이후 다른 로직에서 필요할 때 animator.enabled = true; 호출
            }
            else
            {
                // 약간의 지연 후 복구(레이스 컨디션 방지)
                yield return new WaitForSeconds(animatorReactivateDelay);
                animator.enabled = prevAnimatorEnabled;
            }
        }
    }

    /// <summary>
    /// 발판의 "윗면 y" + 보정값으로 착지 목표점을 계산
    /// </summary>
    private Vector3 GetLandingPoint(GameObject platform)
    {
        float topY = GetPlatformTopY(platform);
        Vector3 p = platform.transform.position;
        p.y = topY + footOffset + landingPadding; // 윗면 + 발바닥 오프셋 + 여유
        return p;
    }

    /// <summary>
    /// 발판 윗면 y값: Collider2D.bounds.max.y > SpriteRenderer.bounds.max.y > transform.y 순
    /// </summary>
    private float GetPlatformTopY(GameObject platform)
    {
        var col = platform.GetComponent<Collider2D>();
        if (col != null) return col.bounds.max.y;

        var srPlat = platform.GetComponent<SpriteRenderer>();
        if (srPlat != null) return srPlat.bounds.max.y;

        // 둘 다 없으면 Transform y 사용(최후 보정)
        return platform.transform.position.y;
    }

    /// <summary>
    /// 발판이 불투명(알파=1)인지 확인
    /// </summary>
    private bool IsOpaque(GameObject platform)
    {
        var srPlat = platform.GetComponent<SpriteRenderer>();
        if (srPlat == null) return true; // SR이 없으면 불투명 취급(프로젝트 룰에 맞춰 변경 가능)
        return Mathf.Approximately(srPlat.color.a, 1f);
    }

    /// <summary>
    /// 시퀀스 종료 처리
    /// cleared == true  → 마지막 패널까지 도착(클리어) → Animator "Clear" 재생
    /// cleared == false → 중간 낙하(실패)
    /// </summary>
    private void Finish(bool cleared)
    {
        // 물리 설정 복구
        if (hadRb) rb.isKinematic = prevKinematic;

        isRunning = false;

        // 클리어 연출
        if (cleared && animator != null)
        {
            // 애니메이터에 "Clear" 스테이트가 존재해야 합니다.
            // 트리거 방식이라면 아래 주석 해제:
            animator.Play("Clear", 0, 0f);
            // animator.SetTrigger("Clear");
        }
    }
}
