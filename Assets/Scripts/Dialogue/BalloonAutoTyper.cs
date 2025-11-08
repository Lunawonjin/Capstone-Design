using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

[DisallowMultipleComponent]
public class BalloonAutoTyper_LocalizedFX : MonoBehaviour
{
    [Header("텍스트 / 말풍선 배경")]
    [SerializeField] private TextMeshProUGUI bodyText;     // TMP 텍스트
    [SerializeField] private RectTransform balloonBg;      // 말풍선 배경(RectTransform)
    [Tooltip("페이드 인/아웃용 CanvasGroup (없으면 자동 추가)")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("레이아웃 설정")]
    [SerializeField] private Vector2 padding = new Vector2(40f, 20f);
    [SerializeField] private float minWidth = 120f;
    [SerializeField] private float maxWidth = 600f;
    [SerializeField] private bool autoWrap = true;

    [Header("텍스트 위치/간격")]
    [Tooltip("텍스트를 위로 올릴 픽셀 단위 오프셋(+면 위로 이동)")]
    [SerializeField] private float textYOffset = 6f;
    [Tooltip("TMP 줄 간격(필요 시 미세 조정)")]
    [SerializeField] private float lineSpacing = 0f;

    [Header("타자기 효과")]
    [SerializeField] private float charsPerSecond = 30f;    // 초당 출력 글자 수
    [SerializeField] private bool extraPauseOnPunct = true; // .,!?… , 에서 잠깐 멈춤
    [SerializeField] private float punctPause = 0.06f;

    [Header("사이즈 애니메이션")]
    [Tooltip("말풍선이 목표 크기로 변할 때의 기본 지속 시간")]
    [SerializeField] private float resizeDuration = 0.12f;
    [Tooltip("약간 더 크게 갔다가 돌아오는 튕김 비율(1.0 = 없음, 1.05 = 5% 오버슈트)")]
    [SerializeField] private float resizeOvershoot = 1.04f;

    [Header("등장/퇴장 연출")]
    [Tooltip("등장 시 스케일 팝업과 페이드 인을 적용")]
    [SerializeField] private bool useEnterFX = true;
    [SerializeField] private float enterDuration = 0.15f;
    [SerializeField] private float enterStartScale = 0.90f;

    [Tooltip("퇴장 시 스케일 다운과 페이드 아웃을 적용")]
    [SerializeField] private bool useExitFX = true;
    [SerializeField] private float exitDuration = 0.12f;
    [SerializeField] private float exitEndScale = 0.90f;

    [Header("비활성화 동작")]
    [Tooltip("Hide() 호출 시 GameObject를 비활성화까지 할지 여부(끄면 알파 0으로만 숨김)")]
    [SerializeField] private bool hideOnEndDeactivate = false;

    [Header("로컬라이즈 입력 (테이블/키)")]
    [SerializeField] private string tableName = "Dialogue_Main";
    [SerializeField] private string entryKey = "Dialogue_001";

    // 상태
    private bool isTyping = false;        // 현재 타자기 진행 중 여부
    private bool forceComplete = false;   // 스킵 플래그
    private string fullMessage = "";      // 완전한 문장
    private Coroutine resizeCR;           // 리사이즈 코루틴 핸들
    private Vector2 lastTargetSize;       // 마지막 목표 사이즈 캐시

    // 외부 접근용
    public bool IsTypingNow => isTyping;

    void Reset()
    {
        if (bodyText == null) bodyText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (balloonBg == null)
        {
            var all = GetComponentsInChildren<RectTransform>(true);
            foreach (var rt in all)
            {
                if (rt != transform && rt != bodyText?.rectTransform)
                {
                    balloonBg = rt;
                    break;
                }
            }
        }
    }

    void Awake()
    {
        if (autoWrap && bodyText != null)
        {
            bodyText.enableWordWrapping = true;              // 줄바꿈은 여기서
            bodyText.enableAutoSizing = false;
            bodyText.overflowMode = TextOverflowModes.Overflow; // Wrap enum은 없음 → Overflow/Truncate 중 택1
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        // Text RectTransform을 BG에 Stretch로 정렬 (삐져나감 방지)
        if (bodyText != null)
        {
            var tr = bodyText.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            tr.pivot = new Vector2(0.5f, 0.5f);

            // 초기 오프셋/간격 적용
            tr.anchoredPosition = new Vector2(0f, textYOffset);
            bodyText.lineSpacing = lineSpacing;
        }
    }

    // 외부에서 텍스트 오프셋을 런타임에 바꾸고 싶을 때
    public void SetTextYOffset(float y)
    {
        textYOffset = y;
        if (bodyText != null)
            bodyText.rectTransform.anchoredPosition = new Vector2(0f, textYOffset);
    }

    // 외부: 출력 중이면 즉시 완성
    public void CompleteInstant()
    {
        if (!isTyping) return;
        forceComplete = true;
    }

    // 외부: 로컬라이즈 대사 표시 (등장 연출 포함)
    public void ShowLocalized(string table, string key, bool withEnterFX = true)
    {
        tableName = table;
        entryKey = key;
        StartCoroutine(Co_LoadAndShow(withEnterFX));
    }

    // 외부: 비로컬 문자열 직접 표시 (등장 연출 포함)
    public void ShowMessage(string message, bool withEnterFX = true)
    {
        fullMessage = message ?? string.Empty;
        StartCoroutine(Co_ShowInternal(withEnterFX));
    }

    // 외부: 퇴장 연출
    public void Hide(bool withExitFX = true)
    {
        StartCoroutine(Co_Exit(withExitFX));
    }

    private IEnumerator Co_LoadAndShow(bool withEnterFX)
    {
        yield return LocalizationSettings.InitializationOperation;

        AsyncOperationHandle<StringTable> handle =
            LocalizationSettings.StringDatabase.GetTableAsync(tableName);
        yield return handle;

        if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogWarning($"[BalloonAutoTyper_LocalizedFX] 테이블 '{tableName}' 로드 실패");
            yield break;
        }

        StringTable tableObj = handle.Result;
        StringTableEntry entry = tableObj.GetEntry(entryKey);
        if (entry == null)
        {
            Debug.LogWarning($"[BalloonAutoTyper_LocalizedFX] 테이블 '{tableName}'에 키 '{entryKey}' 없음");
            yield break;
        }

        fullMessage = entry.LocalizedValue ?? string.Empty;
        yield return StartCoroutine(Co_ShowInternal(withEnterFX));
    }

    // 공통 표시: 등장 → 사이즈 애니메이션 → 타자기
    private IEnumerator Co_ShowInternal(bool withEnterFX)
    {
        // 비활성화 모드가 아니면 Active 유지 + 알파로만 제어
        gameObject.SetActive(true);

        if (withEnterFX && useEnterFX)
            yield return StartCoroutine(Co_Enter());
        else
            canvasGroup.alpha = 1f;

        RecalcAndAnimateSize(fullMessage);

        // 텍스트 오프셋/줄간격을 다시 보장
        if (bodyText != null)
        {
            bodyText.rectTransform.anchoredPosition = new Vector2(0f, textYOffset);
            bodyText.lineSpacing = lineSpacing;
        }

        yield return StartCoroutine(Co_Type());
    }

    private void RecalcAndAnimateSize(string message)
    {
        if (bodyText == null || balloonBg == null) return;

        bodyText.ForceMeshUpdate();
        Vector2 pref = bodyText.GetPreferredValues(message);
        float w = Mathf.Clamp(pref.x + padding.x, minWidth, maxWidth);
        float h = pref.y + padding.y;
        Vector2 targetSize = new Vector2(w, h);
        lastTargetSize = targetSize;

        if (resizeCR != null) StopCoroutine(resizeCR);
        resizeCR = StartCoroutine(Co_ResizeBalloon(targetSize, resizeDuration, resizeOvershoot));
    }

    private IEnumerator Co_Type()
    {
        isTyping = true;
        forceComplete = false;
        bodyText.text = "";

        float interval = Mathf.Max(0.0001f, 1f / Mathf.Max(1f, charsPerSecond));

        for (int i = 0; i < fullMessage.Length; i++)
        {
            if (forceComplete)
            {
                bodyText.text = fullMessage;
                break;
            }

            char c = fullMessage[i];
            bodyText.text += c;

            if (extraPauseOnPunct && ".!?…,".Contains(c))
            {
                float t = punctPause;
                while (t > 0f && !forceComplete)
                {
                    t -= Time.deltaTime;
                    yield return null;
                }
            }

            float itv = interval;
            while (itv > 0f && !forceComplete)
            {
                itv -= Time.deltaTime;
                yield return null;
            }
        }

        bodyText.text = fullMessage;
        isTyping = false;
        forceComplete = false;
    }

    private IEnumerator Co_ResizeBalloon(Vector2 targetSize, float duration, float overshoot)
    {
        Vector2 start = balloonBg.sizeDelta;

        if (overshoot <= 1.0001f)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.0001f, duration);
                float s = Mathf.SmoothStep(0f, 1f, t);
                balloonBg.sizeDelta = Vector2.Lerp(start, targetSize, s);
                yield return null;
            }
            balloonBg.sizeDelta = targetSize;
            yield break;
        }

        Vector2 big = Vector2.LerpUnclamped(targetSize, targetSize * overshoot, 1f);
        float half = duration * 0.6f;
        float t1 = 0f;
        while (t1 < 1f)
        {
            t1 += Time.deltaTime / Mathf.Max(0.0001f, half);
            float s = EaseOutQuad(t1);
            balloonBg.sizeDelta = Vector2.Lerp(start, big, s);
            yield return null;
        }

        float t2 = 0f;
        float remain = Mathf.Max(0.0001f, duration - half);
        while (t2 < 1f)
        {
            t2 += Time.deltaTime / remain;
            float s = EaseOutCubic(t2);
            balloonBg.sizeDelta = Vector2.Lerp(big, targetSize, s);
            yield return null;
        }

        balloonBg.sizeDelta = targetSize;
    }

    private IEnumerator Co_Enter()
    {
        if (balloonBg != null)
            balloonBg.localScale = Vector3.one * Mathf.Max(0.01f, enterStartScale);
        canvasGroup.alpha = 0f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, enterDuration);
            float s = EaseOutBack(t);
            float a = Mathf.SmoothStep(0f, 1f, t);
            if (balloonBg != null) balloonBg.localScale = Vector3.LerpUnclamped(Vector3.one * enterStartScale, Vector3.one, s);
            canvasGroup.alpha = a;
            yield return null;
        }
        if (balloonBg != null) balloonBg.localScale = Vector3.one;
        canvasGroup.alpha = 1f;
    }

    private IEnumerator Co_Exit(bool withExitFX)
    {
        if (!withExitFX || !useExitFX)
        {
            canvasGroup.alpha = 0f;
            if (hideOnEndDeactivate) gameObject.SetActive(false);
            yield break;
        }

        float t = 0f;
        Vector3 startScale = (balloonBg != null) ? balloonBg.localScale : Vector3.one;
        Vector3 endScale = Vector3.one * Mathf.Max(0.01f, exitEndScale);
        float startAlpha = canvasGroup.alpha;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, exitDuration);
            float s = EaseInQuad(t);
            float a = Mathf.Lerp(startAlpha, 0f, s);
            if (balloonBg != null) balloonBg.localScale = Vector3.Lerp(startScale, endScale, s);
            canvasGroup.alpha = a;
            yield return null;
        }

        if (balloonBg != null) balloonBg.localScale = endScale;
        canvasGroup.alpha = 0f;
        if (hideOnEndDeactivate) gameObject.SetActive(false);
    }

    private static float EaseOutQuad(float x) { return 1f - (1f - x) * (1f - x); }
    private static float EaseOutCubic(float x) { float p = 1f - x; return 1f - p * p * p; }
    private static float EaseInQuad(float x) { return x * x; }
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1 + c3 * Mathf.Pow(x - 1f, 3) + c1 * Mathf.Pow(x - 1f, 2);
    }
}
