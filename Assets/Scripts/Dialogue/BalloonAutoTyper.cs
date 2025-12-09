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
    [SerializeField] private Vector2 padding = new Vector2(40f, 20f); // 말풍선 기본 여백(좌우/상하 합산 개념)
    [SerializeField] private float minWidth = 120f;
    [SerializeField] private float maxWidth = 600f;
    [SerializeField] private bool autoWrap = true;

    [Header("텍스트 위치 보정")]
    [Tooltip("텍스트 박스를 X,Y로 평행 이동(말풍선 안에서 미세 위치 조정)")]
    [SerializeField] private float textOffsetX = 0f;
    [SerializeField] private float textOffsetY = 0f;
    [Tooltip("텍스트 박스의 추가 안쪽 여백: Left, Top, Right, Bottom")]
    [SerializeField] private Vector4 textExtraPadding = new Vector4(0f, 0f, 0f, 0f);

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

    [Header("로컬라이즈 입력 (테이블/키)")]
    [SerializeField] private string tableName = "Dialogue_Main";
    [SerializeField] private string entryKey = "Dialogue_001";

    [Header("대사 효과음")]
    [SerializeField] private bool playDialogueSFX = true;
    [SerializeField] private string dialogueSFXKey = "Dialogue";

    [Header("디버그 로그 옵션")]
    [SerializeField] private bool debugLog = false;

    // 상태
    private bool isTyping = false;        // 현재 타자기 진행 중 여부
    private bool forceComplete = false;   // 스킵 플래그
    private string fullMessage = "";      // 완전한 문장
    private Coroutine resizeCR;           // 리사이즈 코루틴 핸들
    private Vector2 lastTargetSize;       // 마지막 목표 사이즈 캐시

    // 새로 추가: 현재 실행 중인 표시/타이핑 코루틴 핸들
    private Coroutine showCR;
    private Coroutine typeCR;

    // ⭐ 루프 SFX용 AudioSource 핸들
    private AudioSource currentDialogueSFX;

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
            bodyText.enableWordWrapping = true;
            bodyText.enableAutoSizing = false;
            bodyText.overflowMode = TextOverflowModes.Overflow;
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        // 텍스트 RectTransform을 Stretch로 정렬하고 인셋/오프셋 적용
        ApplyTextFrameLayout();
    }

    // ===== 외부 제어 =====

    // 출력 중이면 즉시 완성
    public void CompleteInstant()
    {
        if (!isTyping) return;
        if (debugLog) Debug.Log("[BalloonAutoTyper] CompleteInstant 호출");
        forceComplete = true;
    }

    // 로컬라이즈 대사 표시 (등장 연출 포함)
    public void ShowLocalized(string table, string key, bool withEnterFX = true)
    {
        if (debugLog) Debug.Log($"[BalloonAutoTyper] ShowLocalized 호출: table={table}, key={key}");
        tableName = table;
        entryKey = key;

        StopCurrentCoroutines(true);
        showCR = StartCoroutine(Co_LoadAndShow(withEnterFX));
    }

    // 비로컬 문자열 직접 표시 (등장 연출 포함)
    public void ShowMessage(string message, bool withEnterFX = true)
    {
        if (debugLog) Debug.Log("[BalloonAutoTyper] ShowMessage 호출");
        fullMessage = message ?? string.Empty;

        StopCurrentCoroutines(true);
        showCR = StartCoroutine(Co_ShowInternal(withEnterFX));
    }

    // 퇴장 연출
    public void Hide(bool withExitFX = true)
    {
        if (debugLog) Debug.Log($"[BalloonAutoTyper] Hide 호출, withExitFX={withExitFX}");
        // 현재 타이핑/리사이즈는 정리하고, 퇴장 연출만 별도로 수행
        StopCurrentCoroutines(false);
        StartCoroutine(Co_Exit(withExitFX));
    }

    // 인스펙터에서 값 바꾸면 즉시 반영하고 싶을 때 호출
    public void ApplyTextFrameLayout()
    {
        if (bodyText == null) return;

        var tr = bodyText.rectTransform;

        // Stretch 정렬
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.pivot = new Vector2(0.5f, 0.5f);

        // 추가 패딩(좌/상/우/하)을 인셋으로 적용
        tr.offsetMin = new Vector2(textExtraPadding.x, textExtraPadding.w);
        tr.offsetMax = new Vector2(-textExtraPadding.z, -textExtraPadding.y);

        // 평행 이동(가운데 기준)
        tr.anchoredPosition = new Vector2(textOffsetX, textOffsetY);
    }

    // ===== 내부 구현 =====

    private void StopCurrentCoroutines(bool stopResize)
    {
        if (showCR != null)
        {
            StopCoroutine(showCR);
            showCR = null;
        }

        if (typeCR != null)
        {
            StopCoroutine(typeCR);
            typeCR = null;
        }

        if (stopResize && resizeCR != null)
        {
            StopCoroutine(resizeCR);
            resizeCR = null;
        }

        isTyping = false;
        forceComplete = false;

        // ⭐ 코루틴 정리 시 효과음도 정지
        StopDialogueSFX();

        if (debugLog) Debug.Log("[BalloonAutoTyper] 현재 코루틴 정리 완료");
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
        if (debugLog) Debug.Log($"[BalloonAutoTyper] 로컬라이즈 텍스트 로드 완료: '{fullMessage}'");

        yield return StartCoroutine(Co_ShowInternal(withEnterFX));
    }

    // 공통 표시: 등장 → 사이즈 애니메이션 → 타자기
    private IEnumerator Co_ShowInternal(bool withEnterFX)
    {
        gameObject.SetActive(true);

        // 텍스트 인셋/오프셋 항상 재적용(인스펙터에서 값 조정했을 수 있음)
        ApplyTextFrameLayout();

        if (withEnterFX && useEnterFX)
            yield return StartCoroutine(Co_Enter());
        else
            canvasGroup.alpha = 1f;

        RecalcAndAnimateSize(fullMessage);

        // ⭐ 대사 시작 시 효과음 재생
        PlayDialogueSFX();

        typeCR = StartCoroutine(Co_Type());
        yield return typeCR;
        typeCR = null;
    }

    /// <summary>
    /// 대사 효과음 재생 (루프)
    /// </summary>
    private void PlayDialogueSFX()
    {
        if (!playDialogueSFX || string.IsNullOrEmpty(dialogueSFXKey))
            return;

        // 이전 효과음이 있으면 정지
        StopDialogueSFX();

        if (SoundManager.Instance != null)
        {
            currentDialogueSFX = SoundManager.Instance.PlaySFXLoop(dialogueSFXKey);
            if (debugLog) Debug.Log($"[BalloonAutoTyper] ✅ 대사 효과음 루프 재생 시작: {dialogueSFXKey}");
        }
        else
        {
            if (debugLog) Debug.LogWarning($"[BalloonAutoTyper] ⚠️ SoundManager를 찾을 수 없습니다! SFX '{dialogueSFXKey}' 재생 실패");
        }
    }

    /// <summary>
    /// 대사 효과음 정지
    /// </summary>
    private void StopDialogueSFX()
    {
        if (currentDialogueSFX != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.StopSFXSource(currentDialogueSFX);
            if (debugLog) Debug.Log($"[BalloonAutoTyper] ✅ 대사 효과음 정지");
            currentDialogueSFX = null;
        }
    }

    private void RecalcAndAnimateSize(string message)
    {
        if (bodyText == null || balloonBg == null) return;

        bodyText.ForceMeshUpdate();
        Vector2 pref = bodyText.GetPreferredValues(message);

        float extraX = textExtraPadding.x + textExtraPadding.z; // L+R
        float extraY = textExtraPadding.y + textExtraPadding.w; // T+B

        float w = Mathf.Clamp(pref.x + padding.x + extraX, minWidth, maxWidth);
        float h = pref.y + padding.y + extraY;

        Vector2 targetSize = new Vector2(w, h);
        lastTargetSize = targetSize;

        if (resizeCR != null) StopCoroutine(resizeCR);
        resizeCR = StartCoroutine(Co_ResizeBalloon(targetSize, resizeDuration, resizeOvershoot));
    }

    private IEnumerator Co_Type()
    {
        if (debugLog) Debug.Log("[BalloonAutoTyper] Co_Type 시작");

        isTyping = true;
        forceComplete = false;
        bodyText.text = "";

        float interval = Mathf.Max(0.0001f, 1f / Mathf.Max(1f, charsPerSecond));

        for (int i = 0; i < fullMessage.Length; i++)
        {
            if (forceComplete)
            {
                if (debugLog) Debug.Log("[BalloonAutoTyper] forceComplete 플래그로 즉시 완료");
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

        // ⭐ 타이핑 종료 시 효과음 정지
        StopDialogueSFX();

        if (debugLog) Debug.Log("[BalloonAutoTyper] Co_Type 종료");
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
            gameObject.SetActive(false);
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
        gameObject.SetActive(false);
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