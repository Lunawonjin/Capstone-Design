using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class TalkProflie : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image profileImage;
    [SerializeField] private TextMeshProUGUI messengerPreviewText;

    // NPC 정보
    private string npcName;
    private Sprite npcProfileSprite;

    // 채팅 뷰 참조 (ChattingManger가 InitChatting으로 넣어줌)
    private RectTransform chattingContent;
    private GameObject chattingPrefab;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnClickProfile);
        }
        else
        {
            Debug.LogWarning("[TalkProflie] Button 컴포넌트가 없습니다.");
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClickProfile);
        }
    }

    /// <summary>
    /// ChattingManger에서 NPC 기본 정보 세팅
    /// </summary>
    public void Setup(string npcName, Sprite profileSprite, string previewText)
    {
        this.npcName = npcName;
        this.npcProfileSprite = profileSprite;

        if (nameText != null) nameText.text = npcName;
        if (profileImage != null) profileImage.sprite = profileSprite;
        if (messengerPreviewText != null) messengerPreviewText.text = previewText;

        Debug.Log($"[TalkProflie] Setup 완료: npcName={npcName}");
    }

    /// <summary>
    /// ChattingManger에서 채팅 뷰 정보 세팅
    /// </summary>
    public void InitChatting(RectTransform chattingContent, GameObject chattingPrefab)
    {
        this.chattingContent = chattingContent;
        this.chattingPrefab = chattingPrefab;

        Debug.Log($"[TalkProflie] InitChatting: chattingContent={(chattingContent != null)}, chattingPrefab={(chattingPrefab != null)}");
    }

    private void OnClickProfile()
    {
        Debug.Log($"[TalkProflie] 클릭됨: npcName={npcName}");

        if (chattingContent == null || chattingPrefab == null)
        {
            Debug.LogWarning("[TalkProflie] chattingContent 또는 chattingPrefab이 비어 있어서 대화를 생성할 수 없습니다.");
            return;
        }

        string baseName = string.IsNullOrEmpty(npcName) ? "Unknown" : npcName;
        string targetName = baseName + "_ChattingPrefab";

        Transform found = null;

        int childCount = chattingContent.childCount;
        Debug.Log($"[TalkProflie] 현재 TalkDetail Content 자식 수 = {childCount}");

        for (int i = 0; i < childCount; i++)
        {
            Transform child = chattingContent.GetChild(i);
            if (child == null) continue;

            Debug.Log($"[TalkProflie] 자식 {i} 이름 = {child.name}");

            if (child.name == targetName)
            {
                found = child;
            }
        }

        GameObject targetObject;

        if (found != null)
        {
            Debug.Log($"[TalkProflie] 기존 ChattingPrefab 재사용: {targetName}");
            targetObject = found.gameObject;
        }
        else
        {
            Debug.Log($"[TalkProflie] 새 ChattingPrefab 생성: {targetName}");
            targetObject = Object.Instantiate(chattingPrefab, chattingContent);
            targetObject.name = targetName;
        }

        targetObject.SetActive(true);

        ChattingItemView view = targetObject.GetComponent<ChattingItemView>();
        if (view != null)
        {
            view.Setup(npcName, npcProfileSprite);
            Debug.Log("[TalkProflie] ChattingItemView.Setup 호출 완료");
        }
        else
        {
            Debug.LogWarning("[TalkProflie] ChattingItemView를 찾지 못했습니다.");
        }

        // 여기서 ChattingManger 찾아서 대사 시작
        ChattingManger mgr = FindObjectOfType<ChattingManger>();
        if (mgr != null)
        {
            Debug.Log("[TalkProflie] ChattingManger 발견, StartNpcDialogueByFirstCondition 호출");
            mgr.StartNpcDialogueByFirstCondition(npcName);
        }
        else
        {
            Debug.LogWarning("[TalkProflie] 씬에서 ChattingManger를 찾지 못했습니다.");
        }
    }

    public string NpcName => npcName;
}
