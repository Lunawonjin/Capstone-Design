using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 플레이어 충돌 처리:
/// - 매 충돌: 어떤 장애물인지 로그, Hunter Y 증가(인스펙터)
/// - 피격 플래시: 빨갛게 번쩍 → 원래색 복귀 (연속 피격 시도 안전)
/// - 3회째 충돌(죽음):
///   · 모든 장애물 비활성화 + 스폰 중단
///   · 입력/스크롤 스크립트 정지
///   · Salt/Hunter 애니메이션 완전 정지
///   · 소금이 스프라이트 교체
///   · 플레이어 Y를 '펑' 튀듯 상승(OutBack 이징)
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerCollisionHandler : MonoBehaviour
{
    [Header("참조")]
    public ObstacleSpawnerManager spawnerManager;   // 장애물 매니저 (필수)
    public Transform saltRoot;                      // 소금이 루트(Animator/SpriteRenderer 포함)
    public Transform hunterRoot;                    // Hunter 루트(Animator 포함)
    public Transform hunter;                        // Hunter Transform (Y 상승 대상)

    [Header("스프라이트 교체(죽음)")]
    public Sprite saltDeadSprite;                   // 죽음 시 교체할 스프라이트
    public SpriteRenderer saltSpriteRenderer;       // 특정 렌더러만 바꾸고 싶으면 지정
    public bool applyToAllChildSprites = false;     // 자식 전부 같은 스프라이트 적용

    [Header("충돌 효과")]
    public float hunterYIncrease = 1.3f;            // 매 충돌 시 Hunter Y 증가량
    public float playerRiseOnDeath = 3f;            // 죽을 때 플레이어 Y 최종 상승량
    public int stopOnHitCount = 3;                // 이 횟수만큼 충돌하면 죽음

    [Header("죽음 연출(튀어 오르기)")]
    public float riseDuration = 0.45f;              // 상승 연출 시간
    [Range(0f, 3f)] public float riseOvershoot = 1.4f; // OutBack 이징 강도

    [Header("정지할 스크립트(타일맵/입력 등)")]
    [Tooltip("게임오버 시 enabled=false로 만들 스크립트들(예: TilemapScroller, PlayerRunMove 등)")]
    public MonoBehaviour[] scriptsToDisableOnStop;

    // 선택: PlayerRunMove가 Freeze(true) 지원하면 연결(없어도 동작)
    [Header("선택: PlayerRunMove 참조(Freeze 지원 시)")]
    public PlayerRunMove playerRunMove;

    [Header("피격 플래시")]
    public bool enableHitFlash = true;                     // 피격 플래시 사용 여부
    public Color flashColor = new Color(1f, 0.2f, 0.2f, 1f);// 빨간 오버레이 색
    public float flashTime = 0.15f;                        // 빨갛게 유지되는 시간(초)

    [Header("점프 컴포넌트(선택)")]
    public PlayerSaltJumpPhysics saltJump;

    // 내부 상태
    private int hitCount = 0;
    private bool isDead = false;

    // 피격 플래시 관리용
    private Coroutine hitFlashRoutine;
    private SpriteRenderer[] flashRenderers;
    private Color[] flashOriginals;

    void Reset()
    {
        var rb = GetComponent<Rigidbody2D>();
        rb.isKinematic = true;
        rb.gravityScale = 0f;
    }

    void OnTriggerEnter2D(Collider2D other) => HandleHit(other.gameObject);
    void OnCollisionEnter2D(Collision2D col) => HandleHit(col.collider.gameObject);

    private void HandleHit(GameObject hit)
    {
        if (isDead) return;

        var mover = hit.GetComponentInParent<ObstacleMover>() ?? hit.GetComponent<ObstacleMover>();
        if (mover == null) return;

        // 1) 어떤 장애물인지 로그
        Debug.Log($"[충돌] 플레이어 ↔ {mover.type} (오브젝트: {hit.name})");

        // 2) Hunter Y 증가
        if (hunter != null)
        {
            var p = hunter.position; p.y += hunterYIncrease; hunter.position = p;
        }

        // 3) 피격 플래시(빨강 → 원래색)
        if (enableHitFlash) TriggerHitFlash();

        // 4) 사망 판정
        hitCount++;
        if (hitCount >= stopOnHitCount)
            DieAndFreezeGame();
    }

    // ───────────── 피격 플래시 ─────────────

    /// <summary>피격 플래시 실행(이전 플래시 진행 중이면 즉시 원복 후 재실행)</summary>
    private void TriggerHitFlash()
    {
        // 이전 플래시가 돌고 있으면 중단하고 원래색 복구
        CancelHitFlash(restore: true);

        // 대상 SpriteRenderer들 수집
        flashRenderers = CollectSaltRenderers();
        if (flashRenderers.Length == 0) return;

        // 원래 색 저장
        flashOriginals = new Color[flashRenderers.Length];
        for (int i = 0; i < flashRenderers.Length; i++)
            flashOriginals[i] = flashRenderers[i].color;

        // 코루틴 시작
        hitFlashRoutine = StartCoroutine(HitFlashOnce());
    }

    private IEnumerator HitFlashOnce()
    {
        // 빨갛게
        for (int i = 0; i < flashRenderers.Length; i++)
            if (flashRenderers[i]) flashRenderers[i].color = flashColor;

        // 유지
        yield return new WaitForSeconds(flashTime);

        // 원래색 복구
        for (int i = 0; i < flashRenderers.Length; i++)
            if (flashRenderers[i]) flashRenderers[i].color = flashOriginals[i];

        // 정리
        hitFlashRoutine = null;
        flashRenderers = null;
        flashOriginals = null;
    }

    /// <summary>플래시 중지. restore=true면 즉시 원래색 복구</summary>
    private void CancelHitFlash(bool restore)
    {
        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }
        if (restore && flashRenderers != null && flashOriginals != null)
        {
            for (int i = 0; i < flashRenderers.Length; i++)
                if (flashRenderers[i]) flashRenderers[i].color = flashOriginals[i];
        }
        flashRenderers = null;
        flashOriginals = null;
    }

    /// <summary>플래시 대상 SpriteRenderer 수집</summary>
    private SpriteRenderer[] CollectSaltRenderers()
    {
        if (saltSpriteRenderer) return new[] { saltSpriteRenderer };
        if (saltRoot) return saltRoot.GetComponentsInChildren<SpriteRenderer>(true);
        var sr = GetComponentInChildren<SpriteRenderer>(true);
        return sr ? new[] { sr } : new SpriteRenderer[0];
    }

    // ───────────── 사망 처리 ─────────────

    private void DieAndFreezeGame()
    {
        if (isDead) return;
        isDead = true;

        // 진행 중 플래시 즉시 종료(원래색 복구)
        CancelHitFlash(restore: true);

        // A) 스폰 중단 + 모든 장애물 비활성화
        if (spawnerManager != null) spawnerManager.StopAndClearAllObstacles();
        else Debug.LogWarning("[PlayerCollisionHandler] spawnerManager가 비었습니다.");

        // B) 외부 스크립트(타일맵/입력 등) 정지
        if (scriptsToDisableOnStop != null)
            foreach (var mb in scriptsToDisableOnStop) if (mb) mb.enabled = false;

        // C) PlayerRunMove가 있으면 Freeze(true) 호출 (입력 완전 차단)
        if (playerRunMove != null) playerRunMove.Freeze(true);
        else { var prm = GetComponent<PlayerRunMove>(); if (prm) prm.enabled = false; }

        // D) Salt/Hunter 애니메이션 완전 정지
        DisableAnimationTree(saltRoot);
        DisableAnimationTree(hunterRoot);

        // E) 소금이 스프라이트 교체 (Animator 끈 뒤 적용해야 덮어쓰지 않음)
        ForceSwapSaltSprite();

        // F) 플레이어를 '펑' 튀듯 위로 올리기
        StartCoroutine(PopRiseThenFinalize());
        if (saltJump)
        {
            saltJump.LockJump();        // 입력 잠금
            saltJump.ForceCancelJump(); // 점프 중이면 즉시 종료
            saltJump.enabled = false;   // 스크립트 자체 비활성화(더 확실)
        }
    }

    private IEnumerator PopRiseThenFinalize()
    {
        yield return StartCoroutine(PopRise(transform, playerRiseOnDeath, riseDuration, riseOvershoot));

        // 더 이상 충돌 안 나게 콜라이더 Off (선택)
        var col = GetComponent<Collider2D>(); if (col) col.enabled = false;

        Debug.Log("[게임 종료] 장애물 정리/입력정지/애니멈춤/스프라이트교체/튀어오르기 완료");
    }

    // ── 애니/스프라이트 유틸 ─────────────────────────────────

    /// <summary>루트 하위의 Animator/Animation/SpriteSkin 전부 비활성화</summary>
    private void DisableAnimationTree(Transform root)
    {
        if (!root) return;

        var anims = root.GetComponentsInChildren<Animator>(true);
        foreach (var a in anims) a.enabled = false;

        var legacy = root.GetComponentsInChildren<Animation>(true);
        foreach (var a in legacy) a.enabled = false;

        // 2D Animation 패키지 컴포넌트도 안전하게 끄기(의존성 없이 이름으로 판별)
        var behaviours = root.GetComponentsInChildren<Behaviour>(true);
        foreach (var b in behaviours)
        {
            if (b == null) continue;
            var tn = b.GetType().Name;
            if (tn == "SpriteSkin" || tn == "BoneRenderer") b.enabled = false;
        }
    }

    /// <summary>Animator/Animation/SpriteSkin 끈 뒤 소금이 스프라이트 교체</summary>
    private void ForceSwapSaltSprite()
    {
        if (!saltRoot) { Debug.LogWarning("[PlayerCollisionHandler] saltRoot 미지정"); return; }
        if (!saltDeadSprite) { Debug.LogWarning("[PlayerCollisionHandler] saltDeadSprite 미지정"); return; }

        if (applyToAllChildSprites)
        {
            var srs = saltRoot.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in srs) if (sr) sr.sprite = saltDeadSprite;
        }
        else
        {
            var sr = saltSpriteRenderer;
            if (!sr) sr = saltRoot.GetComponentInChildren<SpriteRenderer>(true);
            if (sr) sr.sprite = saltDeadSprite;
            else Debug.LogWarning("[PlayerCollisionHandler] SpriteRenderer를 찾지 못했습니다.");
        }
    }

    // ── 이징 연출 ────────────────────────────────────────────
    /// <summary>OutBack 이징으로 '펑' 튀듯 상승</summary>
    private IEnumerator PopRise(Transform tr, float deltaY, float duration, float overshoot)
    {
        if (!tr || duration <= 0f) yield break;

        float startY = tr.position.y;
        float endY = startY + deltaY;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float eased = EaseOutBack(Mathf.Clamp01(t), overshoot);
            float y = Mathf.LerpUnclamped(startY, endY, eased);
            var p = tr.position; p.y = y; tr.position = p;
            yield return null;
        }
        // 최종값 보정
        var fin = tr.position; fin.y = endY; tr.position = fin;
    }

    /// <summary>표준 OutBack easing. overshoot가 클수록 더 튐(1.2~1.6 추천)</summary>
    private float EaseOutBack(float x, float k)
    {
        float c1 = 1.70158f * k;
        float c3 = c1 + 1f;
        float t = x - 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }
}
