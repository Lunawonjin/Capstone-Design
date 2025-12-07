using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class ChattingItemView : MonoBehaviour
{
    [Header("상단 프로필 영역")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image profileImage;

    [Header("말풍선 컨테이너 / 템플릿")]
    [Tooltip("말풍선들이 쌓일 부모 RectTransform")]
    [SerializeField] private RectTransform balloonContainer;
    [Tooltip("복제해서 사용할 말풍선 템플릿")]
    [SerializeField] private ChattingBalloonItemView balloonTemplate;

    [Header("타이핑 연출 (... 프리팹)")]
    [SerializeField] private DotSequenceAnimator typingIndicator;

    [Header("말풍선 간 세로 간격")]
    [SerializeField] private float verticalSpacing = 5f;
    [Header("전체 컨테이너 하단 여백")]
    [SerializeField] private float bottomPadding = 20f;

    private const float DefaultTypingWaitSeconds = 1f;

    private string npcName;
    public string NpcName => npcName;

    private ChattingBalloonItemView lastBalloon;
    private RectTransform lastBalloonRect;
    private float lastBalloonHeight;
    private float typingOriginalX = 0f;

    private RectTransform myRectTransform; // 이 컴포넌트 자신의 RectTransform

    private void Awake()
    {
        myRectTransform = GetComponent<RectTransform>();

        if (balloonTemplate == null)
            Debug.LogWarning("[ChattingItemView] balloonTemplate이 비어 있습니다.", this);

        // 템플릿의 부모를 컨테이너로 자동 인식
        if (balloonContainer == null && balloonTemplate != null)
            balloonContainer = balloonTemplate.transform.parent as RectTransform;

        if (typingIndicator != null)
        {
            RectTransform tr = typingIndicator.transform as RectTransform;
            if (tr != null) typingOriginalX = tr.anchoredPosition.x;
            typingIndicator.gameObject.SetActive(false);
        }

        lastBalloon = null;
        lastBalloonRect = null;
        lastBalloonHeight = 0f;
    }

    public void Setup(string name, Sprite sprite)
    {
        npcName = name;
        if (nameText != null) nameText.text = name;
        if (profileImage != null) profileImage.sprite = sprite;
    }

    // ─────────────────────────────────────────────
    // 말풍선 추가 및 높이 재계산 로직
    // ─────────────────────────────────────────────
    public ChattingBalloonItemView AddBalloon(string message, bool showTail)
    {
        if (balloonContainer == null || balloonTemplate == null) return null;

        ChattingBalloonItemView targetBalloon;
        RectTransform targetRect;

        // 1. 말풍선 오브젝트 확보
        if (lastBalloon == null)
        {
            // 첫 번째는 템플릿 사용
            targetBalloon = balloonTemplate;
            targetBalloon.gameObject.SetActive(true);
            if (targetBalloon.transform.parent != balloonContainer.transform)
                targetBalloon.transform.SetParent(balloonContainer, false);
            targetRect = targetBalloon.transform as RectTransform;
        }
        else
        {
            // 두 번째부터는 복제
            GameObject cloneGo = Instantiate(balloonTemplate.gameObject, balloonContainer);
            targetBalloon = cloneGo.GetComponent<ChattingBalloonItemView>();
            if (targetBalloon == null) targetBalloon = cloneGo.AddComponent<ChattingBalloonItemView>();
            targetRect = cloneGo.transform as RectTransform;
        }

        // 2. 내용 설정 (여기서 텍스트 양에 따라 높이가 결정됨)
        targetBalloon.SetText(message, showTail);

        // 3. 레이아웃 갱신 (텍스트 크기 반영을 위해 강제 업데이트)
        LayoutRebuilder.ForceRebuildLayoutImmediate(targetRect);

        // 4. 위치 잡기 (수동 배치)
        float y = 0f;
        if (lastBalloon != null)
        {
            // 이전 말풍선 Y - 이전 말풍선 높이 - 간격
            y = lastBalloonRect.anchoredPosition.y - lastBalloonHeight - verticalSpacing;
        }

        targetRect.anchoredPosition = new Vector2(0f, y);

        // 5. 상태 업데이트
        lastBalloon = targetBalloon;
        lastBalloonRect = targetRect;
        lastBalloonHeight = targetBalloon.RootHeight; // ChattingBalloonItemView에 RootHeight 프로퍼티가 있다고 가정

        // 6. 중요: 내 자신(ChattingItemView)의 높이를 늘려준다
        ResizeRootContainer();

        return targetBalloon;
    }

    /// <summary>
    /// 말풍선이 추가될 때마다 전체 컨테이너의 높이를 갱신
    /// (그래야 Manager가 다음 NPC 뷰를 이 아래에 붙일 수 있음)
    /// </summary>
    private void ResizeRootContainer()
    {
        if (lastBalloonRect == null) return;

        // 마지막 말풍선의 바닥 위치 (음수값)
        float contentBottom = lastBalloonRect.anchoredPosition.y - lastBalloonHeight;

        // 높이는 절대값이므로 부호를 뒤집고 패딩 추가
        float newTotalHeight = Mathf.Abs(contentBottom) + bottomPadding;

        // 현재 헤더(프로필 영역)가 차지하는 기본 높이보다 작다면 늘리지 않음 (기본값 유지)
        // (필요 시 최소 높이 설정 로직 추가)

        myRectTransform.sizeDelta = new Vector2(myRectTransform.sizeDelta.x, newTotalHeight);
    }

    // ... (외부 호출용 메서드들) ...
    public IEnumerator PlayTypingAndAppend(string message, bool asNewBlock)
    {
        // 1. 타이핑 연출
        if (typingIndicator != null && balloonContainer != null)
        {
            RectTransform typingRect = typingIndicator.transform as RectTransform;
            typingRect.SetParent(balloonContainer, false);

            float nextY = 0f;
            if (lastBalloon != null)
                nextY = lastBalloonRect.anchoredPosition.y - lastBalloonHeight - verticalSpacing;

            typingRect.anchoredPosition = new Vector2(typingOriginalX, nextY);

            // 타이핑바가 삐져나가지 않게 임시로 높이 확장
            float tempHeight = Mathf.Abs(nextY - typingRect.rect.height) + bottomPadding;
            if (myRectTransform.sizeDelta.y < tempHeight)
                myRectTransform.sizeDelta = new Vector2(myRectTransform.sizeDelta.x, tempHeight);

            typingIndicator.gameObject.SetActive(true);
            typingIndicator.Play();

            yield return new WaitForSeconds(DefaultTypingWaitSeconds);
            typingIndicator.gameObject.SetActive(false);
        }

        // 2. 실제 말풍선 추가
        AddBalloon(message, asNewBlock); // 내부에서 ResizeRootContainer 호출됨
    }

    public bool TryGetLastBalloonWorldPos(out Vector3 worldPos)
    {
        if (lastBalloonRect == null)
        {
            worldPos = Vector3.zero;
            return false;
        }
        worldPos = lastBalloonRect.TransformPoint(lastBalloonRect.rect.center);
        return true;
    }
}