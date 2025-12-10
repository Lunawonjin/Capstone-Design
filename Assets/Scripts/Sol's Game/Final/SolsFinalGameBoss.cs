using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class SolsFinalGameBoss : MonoBehaviour
{
    [Header("보스 설정")]
    [SerializeField] private int maxHp = 50;
    [SerializeField] private int contactDamage = 5;
    [SerializeField] private float moveSpeed = 2f;
    [Tooltip("보스가 둥둥 떠다니는 Y축 이동 범위")]
    [SerializeField] private float floatAmplitude = 0.5f;
    [Tooltip("둥둥 떠다니는 속도")]
    [SerializeField] private float floatSpeed = 1f;

    [Header("HP 슬라이더")]
    [SerializeField] private Slider hpSlider;

    [Header("피격 효과")]
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float hitDuration = 0.5f;

    [Header("넛백 효과")]
    [SerializeField] private float knockbackForce = 5f;
    [SerializeField] private float knockbackDuration = 0.2f;

    [Header("연결")]
    [SerializeField] private SyringePoolShooter player;

    [Header("충돌 데미지 쿨다운")]
    [SerializeField] private float damageCooldown = 0.5f;

    [Header("보스 사망 시 CutScene")]
    [SerializeField] private GameObject cutSceneObject;
    [SerializeField] private string cutAnimationName = "Cut";

    [Header("페이드 설정")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeOutDuration = 1.0f;
    [SerializeField] private float fadeInDuration = 1.0f;
    [SerializeField] private float delayBeforeFadeIn = 0.5f;

    private Rigidbody2D rb;
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private int currentHp;
    private bool isDead = false;
    private Vector3 startPosition;
    private float floatTimer = 0f;
    private Coroutine hitRoutine;
    private bool isKnockback = false;
    private float lastDamageTime = -999f;
    private bool canMove = true;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();

        if (spriteRenderers != null && spriteRenderers.Length > 0)
        {
            originalColors = new Color[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                originalColors[i] = spriteRenderers[i].color;
            }
        }

        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        if (player == null)
        {
            player = FindFirstObjectByType<SyringePoolShooter>();
        }

        if (cutSceneObject != null)
        {
            cutSceneObject.SetActive(false);
            Debug.Log("[SolsFinalGameBoss] CutScene 초기 비활성화");
        }

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.gameObject.SetActive(true);
            Debug.Log("[SolsFinalGameBoss] FadeCanvasGroup 초기화 완료");
        }
        else
        {
            Debug.LogWarning("[SolsFinalGameBoss] ⚠️ FadeCanvasGroup이 연결되지 않았습니다!");
        }
    }

    void Start()
    {
        currentHp = maxHp;
        startPosition = transform.position;
        UpdateHPSlider();

        Debug.Log($"[SolsFinalGameBoss] 보스 생성 완료! HP: {currentHp}/{maxHp}");
    }

    void Update()
    {
        if (isDead || isKnockback || !canMove) return;

        floatTimer += Time.deltaTime * floatSpeed;
        float yOffset = Mathf.Sin(floatTimer) * floatAmplitude;

        Vector3 targetPos = rb.position;
        targetPos.x -= moveSpeed * Time.deltaTime;
        targetPos.y = startPosition.y + yOffset;

        rb.MovePosition(targetPos);
    }

    public void SetMovementEnabled(bool enabled)
    {
        canMove = enabled;
        Debug.Log($"[SolsFinalGameBoss] 보스 움직임: {(enabled ? "활성화" : "비활성화")}");
    }

    public void TakeHit(int damage)
    {
        if (isDead) return;

        currentHp -= damage;
        currentHp = Mathf.Max(0, currentHp);

        Debug.Log($"[SolsFinalGameBoss] 피격! 남은 HP: {currentHp}/{maxHp}");

        UpdateHPSlider();

        if (hitRoutine != null) StopCoroutine(hitRoutine);
        hitRoutine = StartCoroutine(HitFlash());

        StartCoroutine(KnockbackEffect());

        if (currentHp <= 0)
        {
            Die();
        }
    }

    private IEnumerator HitFlash()
    {
        if (spriteRenderers == null) yield break;

        foreach (var sr in spriteRenderers)
        {
            sr.color = hitColor;
        }

        yield return new WaitForSeconds(hitDuration);

        if (!isDead && originalColors != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                spriteRenderers[i].color = originalColors[i];
            }
        }
    }

    private IEnumerator KnockbackEffect()
    {
        isKnockback = true;

        Vector2 knockbackDir = Vector2.right;
        rb.linearVelocity = knockbackDir * knockbackForce;

        yield return new WaitForSeconds(knockbackDuration);

        rb.linearVelocity = Vector2.zero;
        isKnockback = false;
    }

    private void UpdateHPSlider()
    {
        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHp;
            hpSlider.value = currentHp;
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        Debug.Log("[SolsFinalGameBoss] 보스 사망!");

        StartCoroutine(DeathSequence());

        gameObject.SetActive(false);
    }

    private IEnumerator DeathSequence()
    {
        Debug.Log("[SolsFinalGameBoss] ========== 사망 시퀀스 시작 ==========");

        // 1. 페이드 아웃
        yield return StartCoroutine(FadeOut());

        // 2. CutScene 활성화
        if (cutSceneObject != null)
        {
            cutSceneObject.SetActive(true);
            Debug.Log("[SolsFinalGameBoss] ✅ CutScene 활성화!");
        }
        else
        {
            Debug.LogWarning("[SolsFinalGameBoss] ⚠️ cutSceneObject가 연결되지 않았습니다!");
        }

        // 3. 잠시 대기
        yield return new WaitForSeconds(delayBeforeFadeIn);

        // 4. 페이드 인
        yield return StartCoroutine(FadeIn());

        // 5. CutScene 애니메이션 재생
        float animLength = 0f;
        if (cutSceneObject != null)
        {
            Animator cutAnimator = cutSceneObject.GetComponent<Animator>();
            if (cutAnimator == null)
            {
                cutAnimator = cutSceneObject.GetComponentInChildren<Animator>();
            }

            if (cutAnimator != null && !string.IsNullOrEmpty(cutAnimationName))
            {
                cutAnimator.enabled = true;
                cutAnimator.Play(cutAnimationName, -1, 0f);
                Debug.Log($"[SolsFinalGameBoss] ✅ '{cutAnimationName}' 애니메이션 재생!");

                AnimationClip[] clips = cutAnimator.runtimeAnimatorController.animationClips;
                foreach (var clip in clips)
                {
                    if (clip.name == cutAnimationName)
                    {
                        animLength = clip.length;
                        break;
                    }
                }

                if (animLength > 0f)
                {
                    Debug.Log($"[SolsFinalGameBoss] 애니메이션 길이: {animLength}초");
                }
                else
                {
                    animLength = 2f;
                    Debug.LogWarning("[SolsFinalGameBoss] 애니메이션 길이를 찾을 수 없어 기본값(2초) 사용");
                }
            }
            else
            {
                Debug.LogWarning("[SolsFinalGameBoss] ⚠️ CutScene의 Animator를 찾을 수 없거나 애니메이션 이름이 비어있습니다!");
                animLength = 2f;
            }
        }
        else
        {
            animLength = 2f;
        }

        // 6. 애니메이션 재생 대기
        yield return new WaitForSeconds(animLength);

        // 7. StartMenu 씬으로 이동
        Debug.Log("[SolsFinalGameBoss] StartMenu 씬 로드 시작!");
        UnityEngine.SceneManagement.SceneManager.LoadScene("StartMenu");
    }

    private IEnumerator FadeOut()
    {
        if (fadeCanvasGroup == null)
        {
            Debug.LogWarning("[SolsFinalGameBoss] FadeCanvasGroup이 없어 페이드 아웃 건너뜀");
            yield break;
        }

        Debug.Log("[SolsFinalGameBoss] 페이드 아웃 시작");

        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeOutDuration);
            yield return null;
        }

        fadeCanvasGroup.alpha = 1f;
        Debug.Log("[SolsFinalGameBoss] 페이드 아웃 완료");
    }

    private IEnumerator FadeIn()
    {
        if (fadeCanvasGroup == null)
        {
            Debug.LogWarning("[SolsFinalGameBoss] FadeCanvasGroup이 없어 페이드 인 건너뜀");
            yield break;
        }

        Debug.Log("[SolsFinalGameBoss] 페이드 인 시작");

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeInDuration);
            yield return null;
        }

        fadeCanvasGroup.alpha = 0f;
        Debug.Log("[SolsFinalGameBoss] 페이드 인 완료");
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            if (Time.time - lastDamageTime < damageCooldown)
                return;

            if (player != null)
            {
                player.OnDamage(contactDamage, "Boss");
                lastDamageTime = Time.time;
                Debug.Log($"[SolsFinalGameBoss] 플레이어에게 {contactDamage} 데미지!");
            }
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (isDead) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            if (Time.time - lastDamageTime < damageCooldown)
                return;

            if (player != null)
            {
                player.OnDamage(contactDamage, "Boss");
                lastDamageTime = Time.time;
                Debug.Log($"[SolsFinalGameBoss] 플레이어에게 지속 데미지! {contactDamage}");
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isDead) return;

        SyringeProjectile projectile = collision.GetComponent<SyringeProjectile>();
        if (projectile != null)
        {
            int damage = projectile.GetDamage();
            TakeHit(damage);
            Debug.Log($"[SolsFinalGameBoss] 주사 피격! 데미지: {damage}");
            return;
        }

        if (collision.CompareTag("Player"))
        {
            if (Time.time - lastDamageTime < damageCooldown)
                return;

            if (player != null)
            {
                player.OnDamage(contactDamage, "Boss");
                lastDamageTime = Time.time;
                Debug.Log($"[SolsFinalGameBoss] 플레이어에게 {contactDamage} 데미지! (Trigger)");
            }
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (isDead) return;

        if (collision.CompareTag("Player"))
        {
            if (Time.time - lastDamageTime < damageCooldown)
                return;

            if (player != null)
            {
                player.OnDamage(contactDamage, "Boss");
                lastDamageTime = Time.time;
                Debug.Log($"[SolsFinalGameBoss] 플레이어에게 지속 데미지! {contactDamage} (Trigger)");
            }
        }
    }
}