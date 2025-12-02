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
    [Tooltip("플레이어 이동을 막기 위한 PlayerMove 참조(비워두면 자동 검색)")]
    [SerializeField] private PlayerMove playerMove;

    [Header("조건: 팔찌 획득 여부 확인용")]
    [Tooltip("서랍에서 비활성화시키는 팔찌 오브젝트 (activeSelf == false 이면 획득으로 간주)")]
    [SerializeField] private GameObject braceletFromDrawer;

    [Header("둥근 팔찌 연출")]
    [Tooltip("둥근 팔찌 이펙트 프리팹 (SpriteRenderer 포함 가정)")]
    [SerializeField] private GameObject braceletEffectPrefab;
    [Tooltip("팔찌가 시작할 때 기준이 되는 오프셋 (플레이어 위치 기준)")]
    [SerializeField] private Vector3 braceletStartOffset = new Vector3(0f, 0.5f, 0f);
    [Tooltip("팔찌가 떠오르는 높이")]
    [SerializeField] private float braceletRiseHeight = 2.5f;
    [Tooltip("팔찌가 떠오르는 연출 시간(초)")]
    [SerializeField] private float braceletRiseDuration = 1.5f;
    [Tooltip("팔찌 시작 색")]
    [SerializeField] private Color braceletStartColor = new Color(1f, 1f, 1f, 0.3f);
    [Tooltip("팔찌 최종 색(아주 밝은 흰색)")]
    [SerializeField] private Color braceletEndColor = new Color(1.5f, 1.5f, 1.5f, 1f);

    [Header("솔 오브젝트 흡수 연출")]
    [Tooltip("솔 오브젝트 프리팹(스프라이트 포함)")]
    [SerializeField] private GameObject solPrefab;
    [Tooltip("솔이 플레이어 기준 어느 위치에 생성될지 오프셋")]
    [SerializeField] private Vector3 solOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("플레이어가 솔 안으로 빨려 들어가는 연출 시간(초)")]
    [SerializeField] private float absorbDuration = 1.5f;
    [Tooltip("흡수되는 동안 플레이어 스케일이 줄어드는 최소값 비율")]
    [SerializeField] private float playerMinScaleFactor = 0.05f;

    [Header("페이드 아웃")]
    [Tooltip("전체 화면을 덮는 검은 이미지에 CanvasGroup을 붙인 오브젝트")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [Tooltip("페이드 아웃 시간(초)")]
    [SerializeField] private float fadeOutDuration = 1.2f;

    [Header("다음 씬 이름")]
    [Tooltip("엔딩 이후 이동할 씬 이름")]
    [SerializeField] private string nextSceneName = "Sol's Game Final";

    // 내부 상태
    private bool isPlayerColliding = false;
    private bool isPlayingSequence = false;
    private Transform playerTransform;
    private SpriteRenderer playerRenderer;

    private void Awake()
    {
        // 플레이어 자동 검색
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
            // 시작 시 페이드는 투명
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
        if (!isPlayerColliding)
            return;

        if (isPlayingSequence)
            return;

        if (Input.GetKeyDown(interactKey))
        {
            // 조건: 팔찌 오브젝트가 비활성화(activeSelf == false)일 때만 연출 시작
            if (braceletFromDrawer != null && braceletFromDrawer.activeSelf)
            {
                // 아직 팔찌를 못 얻었다면 아무것도 안 함
                return;
            }

            if (playerTransform == null)
            {
                Debug.LogError("[BedFinalSequence] 플레이어 Transform을 찾을 수 없습니다.");
                return;
            }

            StartCoroutine(Co_PlayFinalSequence());
        }
    }

    private IEnumerator Co_PlayFinalSequence()
    {
        isPlayingSequence = true;

        // 플레이어 조작 잠금
        if (playerMove != null)
            playerMove.controlEnabled = false;

        // 1) 둥근 팔찌 이펙트 생성 및 위로 떠오르면서 밝아지는 연출
        GameObject braceletFx = null;
        SpriteRenderer braceletSr = null;

        if (braceletEffectPrefab != null)
        {
            Vector3 startPos = playerTransform.position + braceletStartOffset;
            braceletFx = Instantiate(braceletEffectPrefab, startPos, Quaternion.identity);
            braceletSr = braceletFx.GetComponentInChildren<SpriteRenderer>();

            if (braceletSr != null)
            {
                braceletSr.color = braceletStartColor;
            }

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
                    braceletSr.color = Color.Lerp(braceletStartColor, braceletEndColor, ease);
                }

                yield return null;
            }

            if (braceletFx != null)
            {
                braceletFx.transform.position = riseEndPos;
                if (braceletSr != null)
                    braceletSr.color = braceletEndColor;
            }
        }

        yield return new WaitForSeconds(0.3f);

        // 2) 솔 오브젝트 생성 후, 플레이어를 솔 쪽으로 빨려 들어가듯이 이동 + 스케일 축소
        GameObject solObj = null;
        Transform solTr = null;

        if (solPrefab != null)
        {
            Vector3 solPos = playerTransform.position + solOffset;
            solObj = Instantiate(solPrefab, solPos, Quaternion.identity);
            solTr = solObj.transform;
        }

        Vector3 playerStartPos = playerTransform.position;
        Vector3 playerTargetPos = solTr != null ? solTr.position : playerStartPos;
        Vector3 playerStartScale = playerTransform.localScale;
        Vector3 playerEndScale = playerStartScale * playerMinScaleFactor;

        float tAbsorb = 0f;
        while (tAbsorb < absorbDuration)
        {
            tAbsorb += Time.deltaTime;
            float u = Mathf.Clamp01(tAbsorb / absorbDuration);
            float ease = EaseInCubic(u);

            // 플레이어 위치를 솔 위치로 보간
            playerTransform.position = Vector3.Lerp(playerStartPos, playerTargetPos, ease);
            // 플레이어 스케일 축소
            playerTransform.localScale = Vector3.Lerp(playerStartScale, playerEndScale, ease);

            yield return null;
        }

        // 완전히 빨려 들어간 상태처럼 보이게 처리
        playerTransform.position = playerTargetPos;
        playerTransform.localScale = playerEndScale;

        // 플레이어 렌더러를 잠깐 꺼서 완전히 사라진 느낌
        if (playerRenderer != null)
            playerRenderer.enabled = false;

        // 팔찌 이펙트는 더 이상 필요 없으면 제거
        if (braceletFx != null)
            Destroy(braceletFx);

        yield return new WaitForSeconds(0.3f);

        // 3) 페이드 아웃
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

        // 4) 다음 씬으로 이동
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            Debug.LogError("[BedFinalSequence] nextSceneName 이 비어 있습니다.");
        }
    }

    // 부드러운 감속(위로 떠오르기)용 이징
    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        t = t - 1f;
        return t * t * t + 1f;
    }

    // 부드러운 가속(빨려들어가기)용 이징
    private float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }
}
