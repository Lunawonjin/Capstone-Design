using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class ChattingItemView : MonoBehaviour
{
    [Header("상단 프로필 영역")]
    [SerializeField] private TextMeshProUGUI nameText;   // 상단 이름 텍스트
    [SerializeField] private Image profileImage;         // 상단 프로필 이미지

    [Header("말풍선 컨테이너 / 템플릿")]
    [Tooltip("말풍선들이 쌓일 부모 RectTransform (BalloonTemplate들의 부모)")]
    [SerializeField] private RectTransform balloonContainer;           // BalloonContainer
    [Tooltip("한 줄짜리 말풍선 템플릿 (BalloonTemplate 루트 오브젝트)")]
    [SerializeField] private ChattingBalloonItemView balloonTemplate;  // BalloonTemplate

    [Header("타이핑 연출 (... 프리팹)")]
    [Tooltip("BalloonContainer의 자식으로 둔 '...' 오브젝트 (DotSequenceAnimator)")]
    [SerializeField] private DotSequenceAnimator typingIndicator;      // ... 오브젝트

    [Header("말풍선 간 세로 간격")]
    [SerializeField] private float verticalSpacing = 5f;               // 풍선 사이 Y 간격

    private const float DefaultTypingWaitSeconds = 1f;                 // ... 보여줄 시간(고정)

    private string npcName;
    public string NpcName => npcName;

    // 마지막으로 추가된 말풍선
    private ChattingBalloonItemView lastBalloon;
    private RectTransform lastBalloonRect;
    private float lastBalloonHeight;

    // ... 프리팹의 원래 X 위치
    private float typingOriginalX = 0f;

    private void Awake()
    {
        if (balloonTemplate == null)
        {
            Debug.LogWarning("[ChattingItemView] balloonTemplate이 비어 있습니다.", this);
        }

        if (balloonContainer == null && balloonTemplate != null)
        {
            balloonContainer = balloonTemplate.transform.parent as RectTransform;
            Debug.Log("[ChattingItemView] balloonContainer 자동 설정: " +
                      balloonContainer?.name, this);
        }

        if (typingIndicator != null)
        {
            RectTransform tr = typingIndicator.transform as RectTransform;
            if (tr != null)
            {
                typingOriginalX = tr.anchoredPosition.x;
            }

            if (typingIndicator.gameObject.activeSelf)
            {
                typingIndicator.gameObject.SetActive(false);
            }
        }

        lastBalloon = null;
        lastBalloonRect = null;
        lastBalloonHeight = 0f;
    }

    /// <summary>
    /// NPC 이름과 프로필 이미지를 세팅
    /// (TalkProflie 등에서 사용)
    /// </summary>
    public void Setup(string name, Sprite sprite)
    {
        npcName = name;

        if (nameText != null)
            nameText.text = name;

        if (profileImage != null)
            profileImage.sprite = sprite;
    }

    /// <summary>
    /// 말풍선 한 줄 추가
    ///  - 첫 번째 줄: 프리팹 안의 BalloonTemplate를 그대로 사용
    ///  - 두 번째 이후: BalloonTemplate를 복제해서 사용
    ///  - showTail: Next인 줄인지 여부 (꼬리 표시)
    /// </summary>
    public ChattingBalloonItemView AddBalloon(string message, bool showTail)
    {
        if (balloonContainer == null || balloonTemplate == null)
        {
            Debug.LogWarning("[ChattingItemView] balloonContainer 또는 balloonTemplate이 설정되어 있지 않습니다.", this);
            return null;
        }

        ChattingBalloonItemView targetBalloon;
        RectTransform targetRect;

        if (lastBalloon == null)
        {
            // 첫 줄: 프리팹에 있는 원본 템플릿 사용
            targetBalloon = balloonTemplate;

            if (!targetBalloon.gameObject.activeSelf)
                targetBalloon.gameObject.SetActive(true);

            if (targetBalloon.transform.parent != balloonContainer.transform)
                targetBalloon.transform.SetParent(balloonContainer, false);

            targetRect = targetBalloon.transform as RectTransform;
        }
        else
        {
            // 두 번째 이후: 새 클론 생성
            GameObject cloneGo = Object.Instantiate(
                balloonTemplate.gameObject,
                balloonContainer
            );

            targetRect = cloneGo.transform as RectTransform;

            targetBalloon = cloneGo.GetComponent<ChattingBalloonItemView>();
            if (targetBalloon == null)
            {
                targetBalloon = cloneGo.AddComponent<ChattingBalloonItemView>();
                Debug.LogWarning("[ChattingItemView] 클론에서 ChattingBalloonItemView를 찾지 못해 새로 추가했습니다.", this);
            }
        }

        // 텍스트, 꼬리, 크기 조정 (여기서 RootHeight 갱신됨)
        targetBalloon.SetText(message, showTail);

        // 위치 계산: 마지막 말풍선 기준으로 한 칸 아래에 배치
        if (targetRect != null)
        {
            float y;

            if (lastBalloon == null)
            {
                // 첫 줄은 템플릿 위치를 무시하고 (0,0)에 고정
                y = 0f;
            }
            else
            {
                // 바로 위 말풍선보다 높이 + 간격만큼 아래
                y = lastBalloonRect.anchoredPosition.y - lastBalloonHeight - verticalSpacing;
            }

            Vector2 pos = targetRect.anchoredPosition;
            pos.x = 0f;
            pos.y = y;
            targetRect.anchoredPosition = pos;

            lastBalloon = targetBalloon;
            lastBalloonRect = targetRect;
            lastBalloonHeight = targetBalloon.RootHeight;
        }

        return targetBalloon;
    }

    /// <summary>
    /// 같은 블록 안에 이어지는 대사 (꼬리 없음)
    /// </summary>
    public void AppendChattingTextSameBlock(string message)
    {
        AddBalloon(message, false);
    }

    /// <summary>
    /// Next 대사처럼 새 블록 느낌 (꼬리 표시)
    /// </summary>
    public void AppendChattingTextAsNewBlock(string message)
    {
        AddBalloon(message, true);
    }

    /// <summary>
    /// 외부에서 사용하는 코루틴 진입점
    /// (ChattingManger에서 StartCoroutine으로 호출)
    /// </summary>
    public IEnumerator PlayTypingAndAppend(string message, bool asNewBlock)
    {
        return PlayTypingAndAppendInternal(message, asNewBlock);
    }

    /// <summary>
    /// 내부용: ... 애니메이션 → 말풍선 추가
    /// </summary>
    private IEnumerator PlayTypingAndAppendInternal(string message, bool asNewBlock)
    {
        // 1) ... 애니메이션
        if (typingIndicator != null && balloonContainer != null)
        {
            RectTransform typingRect = typingIndicator.transform as RectTransform;
            if (typingRect != null)
            {
                typingRect.SetParent(balloonContainer, false);

                float nextY;

                if (lastBalloon == null)
                {
                    nextY = 0f;
                }
                else
                {
                    nextY = lastBalloonRect.anchoredPosition.y - lastBalloonHeight - verticalSpacing;
                }

                Vector2 pos = typingRect.anchoredPosition;
                // X는 프리팹 원본 값 그대로 사용
                pos.x = typingOriginalX;
                pos.y = nextY;
                typingRect.anchoredPosition = pos;

                typingIndicator.gameObject.SetActive(true);
                typingIndicator.Play();

                yield return new WaitForSeconds(DefaultTypingWaitSeconds);

                typingIndicator.gameObject.SetActive(false);
            }
        }

        // 2) 말풍선 한 줄 추가
        if (asNewBlock)
            AppendChattingTextAsNewBlock(message);
        else
            AppendChattingTextSameBlock(message);
    }

    /// <summary>
    /// 마지막 말풍선의 월드 좌표를 돌려준다.
    ///  - ChattingManger에서 TalkDetailContent 기준 좌표로 변환해서 사용
    /// </summary>
    public bool TryGetLastBalloonWorldPos(out Vector3 worldPos)
    {
        if (lastBalloonRect == null)
        {
            worldPos = Vector3.zero;
            return false;
        }

        // Rect 중심 기준
        worldPos = lastBalloonRect.TransformPoint(lastBalloonRect.rect.center);
        return true;
    }
}
