using System.Collections;
using UnityEngine;

/// <summary>
/// 발판을 순서대로 진행:
/// - 현재 발판이 불투명(알파=1)이면 밟고 다음 발판 검사
/// - 다음 발판이 불투명하지 않으면 그 발판까지 점프해 착지 즉시 낙하
/// 착지 시 발판의 윗면(top)에 발바닥이 정확히 얹히도록 y좌표를 계산하여
/// 스프라이트가 발판에 파고들지 않게 처리.
/// 낙하 시작 시 캐릭터 스프라이트를 교체(옵션으로 낙하 끝났을 때 복구 가능)
/// 마지막 패널까지 무사 통과 시 Animator의 "Clear" 애니메이션 재생.
/// </summary>
public class CharacterJumpOnEnd : MonoBehaviour
{
    [Header("발판 리스트(진행 순서대로)")]
    public GameObject[] platforms;

    [Header("한 칸 점프 설정")]
    public float hopDuration = 0.35f;
    public float hopHeight = 1.0f;

    [Header("착지 높이 보정")]
    [Tooltip("캐릭터 피벗에서 발바닥까지의 세로 거리(+면 위)")]
    public float footOffset = 0.5f;

    [Tooltip("발판 윗면에서 살짝 띄우는 여유값")]
    public float landingPadding = 0.01f;

    [Header("낙하 연출")]
    public float fallSpeed = 6f;
    public float fallDistance = 6f;

    [Header("스프라이트 교체(낙하 시)")]
    public Sprite normalSprite;     // 기본 스프라이트
    public Sprite fallingSprite;    // 낙하 시작 시 스프라이트
    public bool restoreSpriteAfterFall = false;

    [Header("애니메이션")]
    [Tooltip("마지막 패널 통과 시 재생할 Animator (선택). 'Clear' 스테이트/트리거가 있어야 함")]
    public Animator animator;

    // 내부 상태
    private bool isRunning;
    private Rigidbody2D rb;
    private bool prevKinematic;
    private bool hadRb;
    private SpriteRenderer sr;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null) { hadRb = true; prevKinematic = rb.isKinematic; }

        sr = GetComponent<SpriteRenderer>();
        if (sr != null && normalSprite == null) normalSprite = sr.sprite;

        // 인스펙터에서 안 넣었으면 자기 자신에서 시도
        if (animator == null) animator = GetComponent<Animator>();
    }

    /// <summary>외부에서 호출해서 시퀀스 시작</summary>
    public void TriggerJump()
    {
        if (isRunning || platforms == null || platforms.Length == 0) return;
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        isRunning = true;

        // 점프 동안 물리 간섭(충돌/중력) 방지
        if (hadRb) { rb.isKinematic = true; rb.velocity = Vector2.zero; }

        Vector3 cur = transform.position;

        for (int i = 0; i < platforms.Length; i++)
        {
            var curPf = platforms[i];
            if (curPf == null) continue;

            // 1) 현재 발판 착지 지점(윗면 + 보정) 계산 후 점프
            Vector3 curLand = GetLandingPoint(curPf);
            yield return StartCoroutine(JumpOneHop(cur, curLand));
            cur = curLand;

            // 현재 발판이 불투명(안전) 아니면 → 즉시 낙하(실패)
            if (!IsOpaque(curPf))
            {
                yield return StartCoroutine(FallDown(cur));
                Finish(false);
                yield break;
            }

            // 2) 다음 발판이 불투명(안전) 아니면 → 다음까지 점프 후 그 자리에서 낙하(실패)
            if (i + 1 < platforms.Length)
            {
                var nextPf = platforms[i + 1];
                if (nextPf != null && !IsOpaque(nextPf))
                {
                    Vector3 nextLand = GetLandingPoint(nextPf);
                    yield return StartCoroutine(JumpOneHop(cur, nextLand));
                    cur = nextLand;
                    yield return StartCoroutine(FallDown(cur));
                    Finish(false);
                    yield break;
                }
            }
        }

        // 모든 발판이 불투명으로 통과 → 클리어
        Finish(true);
    }

    // 한 칸 포물선 점프
    private IEnumerator JumpOneHop(Vector3 startPos, Vector3 endPos)
    {
        float t = 0f;
        while (t < hopDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / hopDuration);

            float height = Mathf.Sin(Mathf.PI * u) * hopHeight;
            Vector3 pos = Vector3.Lerp(startPos, endPos, u);
            pos.y += height;

            transform.position = pos;
            yield return null;
        }
        transform.position = endPos; // 스냅
    }

    // 단순 낙하(연출) — 시작 시 스프라이트 변경
    private IEnumerator FallDown(Vector3 startPos)
    {
        if (sr != null && fallingSprite != null) sr.sprite = fallingSprite;

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

        if (restoreSpriteAfterFall && sr != null && normalSprite != null)
            sr.sprite = normalSprite;
    }

    // 착지 목표점(발판 윗면 + 보정)
    private Vector3 GetLandingPoint(GameObject platform)
    {
        float topY = GetPlatformTopY(platform);
        Vector3 p = platform.transform.position;
        p.y = topY + footOffset + landingPadding;
        return p;
    }

    private float GetPlatformTopY(GameObject platform)
    {
        var col = platform.GetComponent<Collider2D>();
        if (col != null) return col.bounds.max.y;

        var srPlat = platform.GetComponent<SpriteRenderer>();
        if (srPlat != null) return srPlat.bounds.max.y;

        return platform.transform.position.y;
    }

    private bool IsOpaque(GameObject platform)
    {
        var srPlat = platform.GetComponent<SpriteRenderer>();
        if (srPlat == null) return true; // SR 없으면 불투명 취급
        return Mathf.Approximately(srPlat.color.a, 1f);
    }

    /// <summary>
    /// 시퀀스 종료 처리
    /// cleared == true  → 마지막 패널까지 도착(클리어)
    /// cleared == false → 중간 낙하(실패)
    /// </summary>
    private void Finish(bool cleared)
    {
        if (hadRb) rb.isKinematic = prevKinematic;
        isRunning = false;

        if (cleared && animator != null)
        {
            // 1) 스테이트 이름이 "Clear"라면:
            animator.Play("Clear", 0, 0f);

            // 2) 트리거 방식으로 쓰고 싶으면 위 줄 대신 아래 사용:
            // animator.SetTrigger("Clear");
        }
    }
}
