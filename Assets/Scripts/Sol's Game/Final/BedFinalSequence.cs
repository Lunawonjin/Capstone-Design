using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class BedFinalSequence : MonoBehaviour
{
    [Header("플레이어 설정")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private PlayerMove playerMove;

    [Header("조건: 팔찌 획득 여부")]
    [Tooltip("서랍에서 비활성화시키는 팔찌 오브젝트")]
    [SerializeField] private GameObject braceletFromDrawer;

    [Header("팔찌 연출 설정")]
    [Tooltip("둥근 팔찌 이펙트 프리팹 (SpriteRenderer 포함)")]
    [SerializeField] private GameObject braceletEffectPrefab;

    [Tooltip("팔찌 시작 오프셋 (플레이어 기준)")]
    [SerializeField] private Vector3 braceletStartOffset = new Vector3(0f, 0.5f, 0f);

    [Tooltip("팔찌가 떠오르는 높이")]
    [SerializeField] private float braceletRiseHeight = 2.5f;

    [Tooltip("1단계: 천천히 떠오르는 시간")]
    [SerializeField] private float braceletRiseDuration = 1.2f;

    [Tooltip("2단계: 회전하며 빛나는 시간")]
    [SerializeField] private float braceletGlowDuration = 1.0f;

    [Tooltip("3단계: 빠르게 플레이어에게 들어가는 시간")]
    [SerializeField] private float braceletAbsorbDuration = 0.4f;

    [Header("팔찌 색상")]
    [SerializeField] private Color braceletStartColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color braceletGlowColor = new Color(2f, 2f, 2.5f, 1f); // 밝은 푸른빛
    [SerializeField] private Color braceletFinalColor = new Color(3f, 3f, 3.5f, 1f); // 더 강렬한 빛

    [Header("팔찌 연출 효과")]
    [Tooltip("회전 속도 (도/초)")]
    [SerializeField] private float rotationSpeed = 360f;

    [Tooltip("맥동 효과 강도")]
    [SerializeField] private float pulseIntensity = 0.2f;

    [Tooltip("맥동 속도")]
    [SerializeField] private float pulseSpeed = 3f;

    [Header("파티클 효과 (선택)")]
    [Tooltip("팔찌 주변 반짝이 파티클 프리팹")]
    [SerializeField] private GameObject sparkleParticlePrefab;

    [Header("솔 타겟")]
    [SerializeField] private Transform solTargetTransform;
    [SerializeField] private Vector2 fixedSolPosition = new Vector2(29.53173f, 20.22195f);
    [SerializeField] private GameObject solPrefab;

    [Header("플레이어 흡수 연출")]
    [SerializeField] private float absorbDuration = 1.5f;
    [SerializeField] private float playerMinScaleFactor = 0.05f;

    [Header("페이드 아웃")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeOutDuration = 1.2f;

    [Header("다음 씬")]
    [SerializeField] private string nextSceneName = "Sol's Game Final";

    private bool isPlayerColliding = false;
    private bool isPlayingSequence = false;
    private Transform playerTransform;
    private SpriteRenderer playerRenderer;

    private void Awake()
    {
        if (playerMove == null)
        {
            playerMove = FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include);
        }

        if (playerMove != null)
        {
            playerTransform = playerMove.transform;
            playerRenderer = playerMove.GetComponentInChildren<SpriteRenderer>();
        }

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            if (!fadeCanvasGroup.gameObject.activeSelf)
                fadeCanvasGroup.gameObject.SetActive(true);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = true;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = false;
        }
    }

    private void Update()
    {
        if (!isPlayerColliding || isPlayingSequence)
            return;

        if (Input.GetKeyDown(interactKey))
        {
            if (braceletFromDrawer != null && braceletFromDrawer.activeSelf)
                return;

            if (playerTransform == null)
            {
                Debug.LogError("[BedFinalSequence] 플레이어 Transform을 찾을 수 없습니다.");
                return;
            }

            StartCoroutine(Co_PlayEnhancedFinalSequence());
        }
    }

    private IEnumerator Co_PlayEnhancedFinalSequence()
    {
        isPlayingSequence = true;

        if (playerMove != null)
            playerMove.controlEnabled = false;

        // ========== 팔찌 연출 시작 ==========
        GameObject braceletFx = null;
        SpriteRenderer braceletSr = null;
        GameObject sparkleEffect = null;

        if (braceletEffectPrefab != null)
        {
            Vector3 startPos = playerTransform.position + braceletStartOffset;
            braceletFx = Instantiate(braceletEffectPrefab, startPos, Quaternion.identity);
            braceletSr = braceletFx.GetComponentInChildren<SpriteRenderer>();

            if (braceletSr != null)
            {
                braceletSr.color = braceletStartColor;
            }

            // 파티클 효과 생성
            if (sparkleParticlePrefab != null)
            {
                sparkleEffect = Instantiate(sparkleParticlePrefab, startPos, Quaternion.identity);
                sparkleEffect.transform.SetParent(braceletFx.transform);
            }

            // ===== 1단계: 천천히 떠오르기 =====
            float t = 0f;
            Vector3 riseStartPos = startPos;
            Vector3 riseEndPos = startPos + new Vector3(0f, braceletRiseHeight, 0f);

            while (t < braceletRiseDuration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / braceletRiseDuration);
                float ease = EaseOutCubic(u);

                if (braceletFx != null)
                {
                    braceletFx.transform.position = Vector3.Lerp(riseStartPos, riseEndPos, ease);
                }

                if (braceletSr != null)
                {
                    // 서서히 밝아지기
                    braceletSr.color = Color.Lerp(braceletStartColor, braceletGlowColor, ease);
                }

                yield return null;
            }

            // ===== 2단계: 회전하며 빛나기 =====
            t = 0f;
            Vector3 glowPos = riseEndPos;
            Vector3 baseScale = braceletFx.transform.localScale;

            while (t < braceletGlowDuration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / braceletGlowDuration);

                if (braceletFx != null)
                {
                    // 회전
                    braceletFx.transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

                    // 맥동 효과 (크기 변화)
                    float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseIntensity;
                    braceletFx.transform.localScale = baseScale * pulse;

                    // 위아래로 살짝 흔들리기
                    float bobbing = Mathf.Sin(Time.time * 2f) * 0.1f;
                    braceletFx.transform.position = glowPos + new Vector3(0f, bobbing, 0f);
                }

                if (braceletSr != null)
                {
                    // 점점 더 밝아지기
                    braceletSr.color = Color.Lerp(braceletGlowColor, braceletFinalColor, u);
                }

                yield return null;
            }

            // ===== 3단계: 플레이어에게 빠르게 흡수 =====
            t = 0f;
            Vector3 absorbStartPos = braceletFx.transform.position;
            Vector3 absorbEndPos = playerTransform.position + new Vector3(0f, 0.5f, 0f);

            while (t < braceletAbsorbDuration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / braceletAbsorbDuration);
                float ease = EaseInCubic(u);

                if (braceletFx != null)
                {
                    // 플레이어를 향해 빠르게 이동
                    braceletFx.transform.position = Vector3.Lerp(absorbStartPos, playerTransform.position, ease);

                    // 크기 축소
                    braceletFx.transform.localScale = Vector3.Lerp(baseScale, baseScale * 0.1f, ease);

                    // 회전 가속
                    braceletFx.transform.Rotate(0f, 0f, rotationSpeed * 3f * Time.deltaTime);
                }

                if (braceletSr != null)
                {
                    // 페이드 아웃
                    Color currentColor = braceletSr.color;
                    currentColor.a = Mathf.Lerp(1f, 0f, ease);
                    braceletSr.color = currentColor;
                }

                yield return null;
            }

            // 팔찌 이펙트 제거
            if (braceletFx != null)
                Destroy(braceletFx);
            if (sparkleEffect != null)
                Destroy(sparkleEffect);
        }

        yield return new WaitForSeconds(0.3f);

        // ========== 솔로 흡수되는 연출 ==========
        Vector3 playerStartPos = playerTransform.position;
        Vector3 playerTargetPos;

        if (solTargetTransform != null)
        {
            Vector3 solPos = solTargetTransform.position;
            playerTargetPos = new Vector3(solPos.x, solPos.y, playerStartPos.z);
        }
        else
        {
            playerTargetPos = new Vector3(fixedSolPosition.x, fixedSolPosition.y, playerStartPos.z);
        }

        GameObject solObj = null;
        if (solPrefab != null)
        {
            solObj = Instantiate(solPrefab, playerTargetPos, Quaternion.identity);
        }

        Vector3 playerStartScale = playerTransform.localScale;
        Vector3 playerEndScale = playerStartScale * playerMinScaleFactor;

        float tAbsorb = 0f;
        while (tAbsorb < absorbDuration)
        {
            tAbsorb += Time.deltaTime;
            float u = Mathf.Clamp01(tAbsorb / absorbDuration);
            float ease = EaseInCubic(u);

            playerTransform.position = Vector3.Lerp(playerStartPos, playerTargetPos, ease);
            playerTransform.localScale = Vector3.Lerp(playerStartScale, playerEndScale, ease);

            yield return null;
        }

        playerTransform.position = playerTargetPos;
        playerTransform.localScale = playerEndScale;

        if (playerRenderer != null)
            playerRenderer.enabled = false;

        yield return new WaitForSeconds(0.3f);

        // ========== 페이드 아웃 ==========
        if (fadeCanvasGroup != null)
        {
            float tFade = 0f;
            while (tFade < fadeOutDuration)
            {
                tFade += Time.deltaTime;
                float u = Mathf.Clamp01(tFade / fadeOutDuration);
                fadeCanvasGroup.alpha = u;
                yield return null;
            }

            fadeCanvasGroup.alpha = 1f;
        }

        // ========== 다음 씬 ==========
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            Debug.LogError("[BedFinalSequence] nextSceneName이 비어 있습니다.");
        }
    }

    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        t = t - 1f;
        return t * t * t + 1f;
    }

    private float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }
}