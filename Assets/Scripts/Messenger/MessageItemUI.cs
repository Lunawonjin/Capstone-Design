using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class MessageItemUI : MonoBehaviour
{
    [Header("UI 바인딩")]
    public Image profileImage;       // 수신자 프로필
    public TextMeshProUGUI recipientNameText;  // 수신자 이름
    public TextMeshProUGUI previewText;        // 미리보기 텍스트

    [Header("읽음 시 회색 처리 대상")]
    public Graphic[] grayTargetGraphics;

    [Header("색상 규칙")]
    public Color unreadTextColor = Color.black;
    public Color readTextColor = new Color(0.6f, 0.6f, 0.6f);

    [Header("로깅")]
    public bool logEnabled = true;
    public string logPrefix = "[MessageItemUI] ";

    [HideInInspector] public string messageName;
    [HideInInspector] public bool isRead;

    // 인스펙터에서도 쓸 수 있는 UnityEvent + 코드에선 += 가능한 C# event 둘 다 지원
    public UnityEvent<MessageItemUI> onClick = new UnityEvent<MessageItemUI>();
    public event Action<MessageItemUI> OnClicked;

    void Awake()
    {
        if (logEnabled) Debug.Log($"{logPrefix}Awake on '{name}'");
    }

    public void Setup(Sprite profile, string recipient, string preview, string msgName, bool read)
    {
        if (profileImage) profileImage.sprite = profile;
        if (recipientNameText) recipientNameText.text = recipient;
        if (previewText) previewText.text = preview;

        messageName = msgName;
        if (logEnabled) Debug.Log($"{logPrefix}Setup -> name='{messageName}', recipient='{recipient}', preview='{preview}', read={read}");
        ApplyReadVisual(read);
    }

    public void ApplyReadVisual(bool read)
    {
        isRead = read;

        foreach (var g in grayTargetGraphics)
        {
            if (!g) continue;
            if (g is TextMeshProUGUI tmp) tmp.color = read ? readTextColor : unreadTextColor;
            else g.color = read ? readTextColor : Color.white;
        }

        if (logEnabled) Debug.Log($"{logPrefix}ApplyReadVisual -> name='{messageName}', isRead={isRead}");
    }

    // Button OnClick에 이 함수를 연결하세요.
    public void OnClick()
    {
        if (logEnabled) Debug.Log($"{logPrefix}OnClick -> name='{messageName}'");
        onClick?.Invoke(this);
        OnClicked?.Invoke(this);
    }
}
