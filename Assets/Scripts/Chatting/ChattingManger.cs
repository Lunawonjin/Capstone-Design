// ChattingManger.cs (Unity 6 LTS)
// 조건이 맞으면 여러 NPC의 TalkProflie를 ProfileScrollView에 추가하는 스크립트
// - 인스펙터에서 NPC 이름, 조건 이름들, 프로필 이미지, 미리보기 텍스트 설정
// - DataManager.instance.nowPlayer의 bool 필드들을 조건으로 사용 (예: SolMeetAfter)
// - 조건이 맞으면 TalkProflie 프리팹을 ProfileScrollView의 Content 밑에 추가
// - 이미 같은 NPC 이름의 TalkProflie가 있다면 추가하지 않음
// - Ctrl + P 입력으로도 조건 검사 및 추가 시도

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class ChattingManger : MonoBehaviour
{
    [Serializable]
    public class NpcChatEntry
    {
        [Header("NPC 설정")]
        [Tooltip("NPC 이름 (TalkProflie의 NameText에 들어갈 값)")]
        public string npcName;

        [Tooltip("조건으로 사용할 DataManager.nowPlayer의 bool 필드 이름들 (모두 true여야 함)")]
        public List<string> conditionKeys = new List<string>();

        [Header("프로필 / 미리보기 텍스트")]
        [Tooltip("TalkProflie의 Profile(Image)에 넣을 Sprite")]
        public Sprite profileSprite;

        [Tooltip("TalkProflie의 MessengerPreview(Text)에 넣을 내용")]
        [TextArea]
        public string previewText;

        [Header("상태(디버그용)")]
        [Tooltip("한 번 생성되었는지 여부 (중복 생성 방지)")]
        public bool spawned = false;
    }

    [Header("UI 참조")]
    [Tooltip("프로필 목록이 들어있는 ScrollRect (ProfileScrollView)")]
    [SerializeField] private ScrollRect profileScrollView;

    [Tooltip("ProfileScrollView의 Content (TalkProflie들이 들어갈 부모)")]
    [SerializeField] private RectTransform profileContent;

    [Tooltip("TalkProflie 프리팹")]
    [SerializeField] private GameObject talkProfilePrefab;

    [Header("TalkProflie 내부 컴포넌트 이름")]
    [Tooltip("프리팹 안에서 NPC 이름을 표시하는 TMP_Text 오브젝트 이름")]
    [SerializeField] private string nameTextObjectName = "NameText";

    [Tooltip("프리팹 안에서 프로필 이미지를 표시하는 Image 오브젝트 이름")]
    [SerializeField] private string profileImageObjectName = "Profile";

    [Tooltip("프리팹 안에서 미리보기 텍스트를 표시하는 TMP_Text 오브젝트 이름")]
    [SerializeField] private string previewTextObjectName = "MessengerPreview";

    [Header("NPC 채팅 정의 리스트")]
    [SerializeField] private List<NpcChatEntry> npcEntries = new List<NpcChatEntry>();

    private void Awake()
    {
        // ScrollRect에서 Content 자동 할당
        if (!profileContent && profileScrollView != null)
        {
            profileContent = profileScrollView.content;
        }

        if (!profileContent)
        {
            Debug.LogWarning("[ChattingManger] profileContent가 비어 있습니다. 인스펙터에서 설정하세요.");
        }

        if (!talkProfilePrefab)
        {
            Debug.LogWarning("[ChattingManger] talkProfilePrefab이 비어 있습니다. 인스펙터에서 설정하세요.");
        }
    }

    private void Update()
    {
        // 매 프레임 조건 검사 (필요하면 호출 빈도 줄여도 됨)
        EvaluateAll();

        // Ctrl + P 입력 시에도 조건 검사 및 추가 시도
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            Input.GetKeyDown(KeyCode.M))
        {
            EvaluateAll();
        }
    }

    /// <summary>
    /// 모든 NPC 엔트리에 대해 조건을 검사하고, 만족하면 TalkProflie를 한 번만 생성한다.
    /// </summary>
    public void EvaluateAll()
    {
        if (npcEntries == null || npcEntries.Count == 0) return;

        for (int i = 0; i < npcEntries.Count; i++)
        {
            var entry = npcEntries[i];
            if (entry == null) continue;

            // 이미 생성된 엔트리는 스킵
            if (entry.spawned) continue;

            // 조건 검사
            if (!IsConditionSatisfied(entry)) continue;

            // 같은 이름의 TalkProflie가 이미 UI에 있으면 생성하지 않음
            if (HasProfileForName(entry.npcName)) continue;

            // TalkProflie 생성
            SpawnTalkProfile(entry);

            // 다시 생성되지 않도록 플래그 설정
            entry.spawned = true;
        }
    }

    /// <summary>
    /// 하나의 NpcChatEntry에 대해 모든 조건이 만족하는지 검사한다.
    /// - conditionKeys가 하나라도 false거나 찾지 못하면 실패
    /// - conditionKeys가 비어 있으면 조건 미충족으로 간주
    /// </summary>
    private bool IsConditionSatisfied(NpcChatEntry entry)
    {
        if (entry == null) return false;

        // 조건이 하나도 없으면 자동으로 뜨는 것을 막기 위해 false 처리
        if (entry.conditionKeys == null || entry.conditionKeys.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < entry.conditionKeys.Count; i++)
        {
            var key = entry.conditionKeys[i];
            if (string.IsNullOrEmpty(key)) return false;

            bool value;
            if (!TryGetNowPlayerBool(key, out value))
            {
                // 해당 이름의 bool 필드를 찾지 못하면 조건 실패
                return false;
            }

            // 모든 조건이 true여야만 통과하도록 AND 처리
            if (!value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// DataManager.instance.nowPlayer의 public bool 필드/프로퍼티에서 값을 읽어온다.
    /// </summary>
    private bool TryGetNowPlayerBool(string fieldName, out bool result)
    {
        result = false;

        var dm = DataManager.instance;
        if (dm == null || dm.nowPlayer == null || string.IsNullOrEmpty(fieldName))
        {
            return false;
        }

        object nowPlayer = dm.nowPlayer;
        var t = nowPlayer.GetType();

        // public 필드 검색
        var f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (f != null && f.FieldType == typeof(bool))
        {
            result = (bool)f.GetValue(nowPlayer);
            return true;
        }

        // public 프로퍼티 검색
        var p = t.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (p != null && p.PropertyType == typeof(bool) && p.CanRead)
        {
            result = (bool)p.GetValue(nowPlayer);
            return true;
        }

        // 찾지 못한 경우
        return false;
    }

    /// <summary>
    /// 프로필 목록에 이미 같은 NPC 이름의 TalkProflie가 있는지 검사한다.
    /// NameText 텍스트와 npcName을 비교한다.
    /// </summary>
    private bool HasProfileForName(string npcName)
    {
        if (profileContent == null) return false;
        if (string.IsNullOrEmpty(npcName)) return false;

        for (int i = 0; i < profileContent.childCount; i++)
        {
            var child = profileContent.GetChild(i);
            if (child == null) continue;

            TMP_Text nameText = FindChildComponentByName<TMP_Text>(child, nameTextObjectName);
            if (nameText == null) continue;

            if (string.Equals(nameText.text, npcName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// TalkProflie 프리팹을 인스턴스화하고, 이름/프로필/미리보기 텍스트를 채운다.
    /// </summary>
    private void SpawnTalkProfile(NpcChatEntry entry)
    {
        if (profileContent == null || talkProfilePrefab == null)
        {
            Debug.LogWarning("[ChattingManger] SpawnTalkProfile 실패: profileContent 또는 talkProfilePrefab이 설정되지 않았습니다.");
            return;
        }

        GameObject go = Instantiate(talkProfilePrefab, profileContent);
        go.transform.SetAsLastSibling();

        // NameText
        TMP_Text nameText = FindChildComponentByName<TMP_Text>(go.transform, nameTextObjectName);
        if (nameText != null)
        {
            nameText.text = entry.npcName;
        }
        else
        {
            Debug.LogWarning($"[ChattingManger] NameText 오브젝트를 찾지 못했습니다. 이름: {nameTextObjectName}");
        }

        // Profile 이미지
        Image profileImage = FindChildComponentByName<Image>(go.transform, profileImageObjectName);
        if (profileImage != null && entry.profileSprite != null)
        {
            profileImage.sprite = entry.profileSprite;
        }
        else if (profileImage == null)
        {
            Debug.LogWarning($"[ChattingManger] Profile 오브젝트를 찾지 못했습니다. 이름: {profileImageObjectName}");
        }

        // MessengerPreview 텍스트
        TMP_Text previewText = FindChildComponentByName<TMP_Text>(go.transform, previewTextObjectName);
        if (previewText != null)
        {
            previewText.text = entry.previewText;
        }
        else
        {
            Debug.LogWarning($"[ChattingManger] MessengerPreview 오브젝트를 찾지 못했습니다. 이름: {previewTextObjectName}");
        }
    }

    /// <summary>
    /// 자식 트리에서 특정 이름을 가진 Transform 밑의 컴포넌트를 찾는다.
    /// </summary>
    private T FindChildComponentByName<T>(Transform root, string childName) where T : Component
    {
        if (root == null || string.IsNullOrEmpty(childName)) return null;
        return FindChildComponentByNameRecursive<T>(root, childName);
    }

    private T FindChildComponentByNameRecursive<T>(Transform current, string childName) where T : Component
    {
        if (current.name == childName)
        {
            return current.GetComponent<T>();
        }

        for (int i = 0; i < current.childCount; i++)
        {
            var child = current.GetChild(i);
            T found = FindChildComponentByNameRecursive<T>(child, childName);
            if (found != null) return found;
        }

        return null;
    }
}
