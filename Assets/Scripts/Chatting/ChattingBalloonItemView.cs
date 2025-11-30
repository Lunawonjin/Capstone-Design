using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class ChattingBalloonItemView : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private Image tailImage;                // 말풍선 꼬다리
    [SerializeField] private RectTransform balloonRect;      // ChattingBallon (배경)
    [SerializeField] private TextMeshProUGUI chattingText;   // Chatting_Text

    [Header("가로/세로 규칙")]
    [SerializeField] private float textMaxWidth = 520f;      // 텍스트 최대 가로
    [SerializeField] private float balloonPaddingX = 30f;    // 풍선 가로 여유(양쪽 합)
    [SerializeField] private float textLineHeight = 30f;     // 텍스트 한 줄 높이
    [SerializeField] private float balloonBaseHeight = 50f;  // 최소 풍선 높이
    [SerializeField] private float balloonExtraPerLine = 30f;// 줄 증가 시 추가 높이

    private RectTransform rootRect;
    private float cachedHeight = 50f;

    /// <summary>
    /// 이 말풍선 한 줄의 최종 높이(루트 RectTransform 기준)
    /// </summary>
    public float RootHeight => cachedHeight;

    private void Awake()
    {
        rootRect = transform as RectTransform;
        if (rootRect == null)
        {
            Debug.LogWarning("[ChattingBalloonItemView] RectTransform을 찾을 수 없습니다.", this);
        }
    }

    /// <summary>
    /// 텍스트와 꼬리 표시 여부 설정 + 크기 재계산
    /// </summary>
    public void SetText(string message, bool showTail)
    {
        if (chattingText == null)
            return;

        chattingText.enableWordWrapping = true;
        chattingText.text = message;

        // 텍스트 레이아웃 갱신
        chattingText.ForceMeshUpdate();

        // 실제 렌더링된 텍스트의 가로/세로
        Vector2 boundsSize = chattingText.textBounds.size;

        // 가로 길이 제한
        float clampedTextWidth = Mathf.Min(boundsSize.x, textMaxWidth);
        float balloonWidth = clampedTextWidth + balloonPaddingX;

        // 라인 수 (최소 1줄)
        int lineCount = Mathf.Max(1, chattingText.textInfo.lineCount);
        float textHeight = textLineHeight * lineCount;
        float balloonHeight = balloonBaseHeight + balloonExtraPerLine * (lineCount - 1);

        // 텍스트 Rect 높이 조정
        RectTransform textRt = chattingText.rectTransform;
        if (textRt != null)
        {
            Vector2 tSize = textRt.sizeDelta;
            tSize.y = textHeight;
            textRt.sizeDelta = tSize;
        }

        // 풍선 배경 Rect 크기 조정
        if (balloonRect != null)
        {
            Vector2 bSize = balloonRect.sizeDelta;
            bSize.x = balloonWidth;
            bSize.y = balloonHeight;
            balloonRect.sizeDelta = bSize;
        }

        // 루트 RectTransform 크기도 풍선 배경과 맞춰주기
        if (rootRect != null && balloonRect != null)
        {
            Vector2 rSize = rootRect.sizeDelta;
            rSize.x = balloonRect.sizeDelta.x;
            rSize.y = balloonRect.sizeDelta.y;
            rootRect.sizeDelta = rSize;
            cachedHeight = rSize.y;
        }

        // 꼬리 On/Off
        if (tailImage != null)
        {
            tailImage.gameObject.SetActive(showTail);
        }
    }
}
