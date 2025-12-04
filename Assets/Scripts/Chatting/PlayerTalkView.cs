using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class PlayerTalkView : MonoBehaviour
{
    [Header("루트 / 말풍선")]
    [SerializeField] private RectTransform rootRect;          // PlayerTalk
    [SerializeField] private Image balloonImage;              // PlayerBallon 이미지
    [SerializeField] private TextMeshProUGUI balloonText;     // PlayerBallon_Text

    [Header("텍스트 / 말풍선 크기 설정")]
    [SerializeField] private float maxTextWidth = 230f;       // 텍스트 최대 가로
    [SerializeField] private float textLineHeight = 30f;      // 한 줄 높이
    [SerializeField] private float balloonPaddingX = 30f;     // 말풍선 가로 여유분
    [SerializeField] private float balloonBaseHeight = 50f;   // 말풍선 최소 높이

    [Header("Y 오프셋 규칙")]
    [Tooltip("마지막 NPC 말풍선 Y에서 줄 수당 빼 줄 값(기본 30)")]
    [SerializeField] private float perLineYOffset = 30f;      // 한 줄당 Y 오프셋
    [Tooltip("최소 한 줄 기준 계수(기본 1줄)")]
    [SerializeField] private int minLineCountForOffset = 1;   // 최소 줄수

    private void Reset()
    {
        rootRect = transform as RectTransform;
    }

    /// <summary>
    /// 플레이어 말풍선 텍스트 설정 + 크기 조정
    /// lastNpcBalloonY: TalkDetailContent 기준 마지막 NPC 말풍선 Y
    /// </summary>
    public void Setup(string message, float lastNpcBalloonY)
    {
        if (balloonText == null)
        {
            Debug.LogWarning("[PlayerTalkView] balloonText가 비어 있습니다.", this);
            return;
        }

        if (rootRect == null)
            rootRect = transform as RectTransform;

        balloonText.text = message;

        // 레이아웃 강제 갱신
        LayoutRebuilder.ForceRebuildLayoutImmediate(balloonText.rectTransform);

        // 1) 텍스트 가로 길이
        float preferredWidth = balloonText.preferredWidth;
        float clampedWidth = Mathf.Min(preferredWidth, maxTextWidth);

        Vector2 textSize = balloonText.rectTransform.sizeDelta;
        textSize.x = clampedWidth;
        balloonText.rectTransform.sizeDelta = textSize;

        // 2) 줄 수 계산 → 세로 길이
        int lineCount = Mathf.Max(
            minLineCountForOffset,
            Mathf.CeilToInt(preferredWidth / maxTextWidth)
        );
        float textHeight = lineCount * textLineHeight;

        textSize = balloonText.rectTransform.sizeDelta;
        textSize.y = textHeight;
        balloonText.rectTransform.sizeDelta = textSize;

        // 3) 말풍선 배경 크기
        if (balloonImage != null)
        {
            RectTransform bRect = balloonImage.rectTransform;
            Vector2 bSize = bRect.sizeDelta;
            bSize.x = clampedWidth + balloonPaddingX;
            bSize.y = Mathf.Max(balloonBaseHeight, textHeight + 20f);
            bRect.sizeDelta = bSize;
        }

        // 4) 루트 높이 보정
        if (rootRect != null)
        {
            float targetHeight = balloonImage != null
                ? balloonImage.rectTransform.sizeDelta.y
                : textHeight + 20f;

            Vector2 rSize = rootRect.sizeDelta;
            rSize.y = Mathf.Max(rSize.y, targetHeight);
            rootRect.sizeDelta = rSize;
        }

        // 5) Y 위치: 마지막 NPC 말풍선 Y에서 줄 수 * 30만큼 아래
        if (rootRect != null)
        {
            float offsetLines = Mathf.Max(minLineCountForOffset, lineCount);
            float y = lastNpcBalloonY - perLineYOffset * offsetLines; // 1줄: -30, 2줄: -60 ...

            Vector2 pos = rootRect.anchoredPosition;
            pos.y = y;
            rootRect.anchoredPosition = pos;

            Debug.Log($"[PlayerTalkView] Setup 완료: \"{message}\", lineCount={lineCount}, y={y}", this);
        }
    }

    /// <summary>
    /// 이전 코드 호환용: Y 기준 없이 0에서 계산하고 싶을 때
    /// </summary>
    public void Setup(string message)
    {
        Setup(message, 0f);
    }
}
