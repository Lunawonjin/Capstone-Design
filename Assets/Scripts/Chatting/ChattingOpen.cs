using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ChattingOpen : MonoBehaviour
{
    [Header("버튼 참조")]
    [SerializeField] private Button jadyuTalkButton;  // JadyuTalk_BT
    [SerializeField] private Button exitButton;       // Exit 버튼

    [Header("패널 / 오브젝트")]
    [SerializeField] private GameObject jadyuTalk;        // JadyuTalk
    [SerializeField] private GameObject jadyuTalkPanel;   // JadyuTalkPanel
    [SerializeField] private GameObject jadyuTalkStart;   // JadyuTalkStart(이미지 오브젝트)
    [SerializeField] private RectTransform jadyuTalkStartRect; // JadyuTalkStart RectTransform
    [SerializeField] private RectTransform jadyuTalkRect;      // JadyuTalk RectTransform (같이 커지는 대상)

    [Header("로고 / 텍스트 그래픽")]
    [SerializeField] private Graphic logoGraphic;   // 로고 이미지
    [SerializeField] private Graphic textGraphic;   // 텍스트(TMP_Text 포함)

    [Header("스케일 연출 설정")]
    [SerializeField] private float startScale = 0.5f;     // 처음 작을 때 전체 스케일
    [SerializeField] private float endScale = 1.0f;       // 최종 전체 스케일
    [SerializeField] private float scaleDuration = 0.25f; // 작았다가 커지는 시간

    [Header("로고/텍스트 연출 설정")]
    [SerializeField] private float waitBeforeFade = 1.0f; // 활성화 후 1초 기다렸다가
    [SerializeField] private float fadeDuration = 0.3f;   // 로고/텍스트 투명해지는 시간

    [Header("JadyuTalkStart 높이 연출 설정")]
    [SerializeField] private float collapseDuration = 0.35f; // 시각적으로 750 → 0 줄어드는 시간(스케일 Y 사용)

    private Coroutine openingRoutine;

    private void Awake()
    {
        if (jadyuTalkStartRect == null && jadyuTalkStart != null)
        {
            jadyuTalkStartRect = jadyuTalkStart.GetComponent<RectTransform>();
        }

        if (jadyuTalkRect == null && jadyuTalk != null)
        {
            jadyuTalkRect = jadyuTalk.GetComponent<RectTransform>();
        }
    }

    private void OnEnable()
    {
        if (jadyuTalkButton != null)
        {
            jadyuTalkButton.onClick.AddListener(OpenChatting);
        }

        if (exitButton != null)
        {
            exitButton.onClick.AddListener(CloseChatting);
        }
    }

    private void OnDisable()
    {
        if (jadyuTalkButton != null)
        {
            jadyuTalkButton.onClick.RemoveListener(OpenChatting);
        }

        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(CloseChatting);
        }
    }

    // JadyuTalk_BT 클릭 시 호출
    public void OpenChatting()
    {
        if (jadyuTalk != null)
            jadyuTalk.SetActive(true);

        if (jadyuTalkPanel != null)
            jadyuTalkPanel.SetActive(true);

        if (jadyuTalkStart != null)
            jadyuTalkStart.SetActive(true);

        // 시작 상태 초기화: 스케일, 알파 값
        if (jadyuTalkStartRect != null)
        {
            jadyuTalkStartRect.localScale = Vector3.one * startScale;
        }

        if (jadyuTalkRect != null)
        {
            jadyuTalkRect.localScale = Vector3.one * startScale;
        }

        SetGraphicAlpha(logoGraphic, 1f);
        SetGraphicAlpha(textGraphic, 1f);

        if (openingRoutine != null)
        {
            StopCoroutine(openingRoutine);
        }

        openingRoutine = StartCoroutine(OpenSequence());
    }

    // Exit 버튼 클릭 시 호출
    public void CloseChatting()
    {
        if (openingRoutine != null)
        {
            StopCoroutine(openingRoutine);
            openingRoutine = null;
        }

        // 여기에서만 비활성화
        if (jadyuTalkPanel != null)
            jadyuTalkPanel.SetActive(false);

        if (jadyuTalk != null)
            jadyuTalk.SetActive(false);

        if (jadyuTalkStart != null)
            jadyuTalkStart.SetActive(false);
    }

    // 연출 시퀀스:
    // 1) JadyuTalkStart + JadyuTalk 둘 다 작게 시작해서 같이 커지는 스케일 연출
    // 2) 1초 대기 후 로고/텍스트 페이드 아웃
    // 3) JadyuTalkStart의 Y 스케일을 1 → 0으로 줄여서 높이 750 → 0처럼 보이게
    // 4) JadyuTalkStart는 비활성화하지 않고, 그냥 안 보이게만 유지
    private IEnumerator OpenSequence()
    {
        // 1) 스케일 연출 (둘 다 동시에)
        float t = 0f;
        while (t < scaleDuration)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / scaleDuration);

            // 약간 완화된 곡선(EaseOut 느낌)
            float eased = 1f - Mathf.Pow(1f - progress, 2f);
            float scale = Mathf.Lerp(startScale, endScale, eased);

            if (jadyuTalkStartRect != null)
            {
                jadyuTalkStartRect.localScale = Vector3.one * scale;
            }

            if (jadyuTalkRect != null)
            {
                jadyuTalkRect.localScale = Vector3.one * scale;
            }

            yield return null;
        }

        if (jadyuTalkStartRect != null)
        {
            jadyuTalkStartRect.localScale = Vector3.one * endScale;
        }

        if (jadyuTalkRect != null)
        {
            jadyuTalkRect.localScale = Vector3.one * endScale;
        }

        // 2) 1초 대기 후 로고/텍스트 페이드 아웃
        if (waitBeforeFade > 0f)
        {
            float wait = waitBeforeFade;
            while (wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
        }

        if (fadeDuration > 0f)
        {
            float ft = 0f;
            while (ft < fadeDuration)
            {
                ft += Time.deltaTime;
                float progress = Mathf.Clamp01(ft / fadeDuration);

                float alpha = Mathf.Lerp(1f, 0f, progress);
                SetGraphicAlpha(logoGraphic, alpha);
                SetGraphicAlpha(textGraphic, alpha);

                yield return null;
            }
        }
        else
        {
            SetGraphicAlpha(logoGraphic, 0f);
            SetGraphicAlpha(textGraphic, 0f);
        }

        // 3) JadyuTalkStart만 높이 줄어드는 느낌: Y 스케일 1 → 0
        if (jadyuTalkStartRect != null)
        {
            float ct = 0f;
            Vector3 baseScale = jadyuTalkStartRect.localScale;
            float startY = baseScale.y;
            float endY = 0f;

            while (ct < collapseDuration)
            {
                ct += Time.deltaTime;
                float progress = Mathf.Clamp01(ct / collapseDuration);

                float eased = progress * progress; // EaseIn
                float yScale = Mathf.Lerp(startY, endY, eased);

                jadyuTalkStartRect.localScale = new Vector3(baseScale.x, yScale, baseScale.z);

                yield return null;
            }

            jadyuTalkStartRect.localScale = new Vector3(baseScale.x, 0f, baseScale.z);
        }

        // 4) 여기서는 JadyuTalkStart를 끄지 않는다.
        //    이미 스케일 Y=0, 로고/텍스트 알파=0이라 화면상으로는 안 보이는 상태.

        openingRoutine = null;
    }

    private void SetGraphicAlpha(Graphic g, float alpha)
    {
        if (g == null) return;
        Color c = g.color;
        c.a = alpha;
        g.color = c;
    }
}
