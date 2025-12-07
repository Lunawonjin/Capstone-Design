using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

[DisallowMultipleComponent]
public class ChattingManger : MonoBehaviour
{
    [System.Serializable]
    public class NpcProfileEntry
    {
        [Header("NPC 기본 정보")]
        public string npcName;
        public Sprite profileSprite;

        [Header("조건 이름 리스트")]
        public string[] conditionNames;

        [Header("메신저 미리보기 텍스트")]
        [TextArea] public string messengerPreview;
    }

    [Header("프로필 리스트 영역")]
    [SerializeField] private RectTransform profileContent;

    [Header("프로필 프리팹")]
    [SerializeField] private GameObject talkProfilePrefab;

    [Header("채팅 상세 뷰 영역 (Vertical Layout Group 사용 권장)")]
    [SerializeField] private RectTransform talkDetailContent;

    [Header("NPC 말풍선 그룹 프리팹 (ChattingItemView)")]
    [SerializeField] private GameObject chattingPrefab;

    [Header("선택지 / 플레이어 말풍선 프리팹")]
    [SerializeField] private GameObject choicePrefab;
    [SerializeField] private GameObject playerTalkPrefab;

    [Header("위치 미세 조정 (Y축 오프셋)")]
    [SerializeField] private float choicePrefabOffsetY = 10f;    // 선택지 위치 보정
    [SerializeField] private float playerTalkPrefabOffsetY = -20f; // 플레이어 대화 위치 보정
    [SerializeField] private float nextProfileSpacing = 30f;     // NPC 그룹 간 간격

    [Header("NPC 프로필 설정")]
    [SerializeField] private NpcProfileEntry[] npcProfiles;

    [Header("키보드로 프로필 추가 기능")]
    [SerializeField] private bool useKeyboardAdd = true;
    [SerializeField] private KeyCode addKey = KeyCode.P;

    [Header("설정값")]
    [SerializeField] private bool useLocalization = true;
    [SerializeField] private float dialogueIntervalSeconds = 1.5f;

    // 내부 상태
    private int createdCount = 0;
    private Dictionary<string, ChattingItemView> lastChatViewByNpc = new Dictionary<string, ChattingItemView>();
    private Dictionary<string, int> chatGroupCounterByNpc = new Dictionary<string, int>();
    private Coroutine currentDialogueRoutine;
    private string currentBranchSuffix = "";

    private void Start()
    {
        if (npcProfiles != null)
        {
            foreach (var entry in npcProfiles)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.npcName))
                    CreateProfileItem(entry);
            }
        }
    }

    private void Update()
    {
        if (useKeyboardAdd && Input.GetKeyDown(addKey))
        {
            createdCount++;
            NpcProfileEntry entry = new NpcProfileEntry
            {
                npcName = $"New NPC {createdCount}",
                messengerPreview = "새로운 메시지"
            };
            CreateProfileItem(entry);
        }
    }

    // ─────────────────────────────────────────────
    // 프로필 생성
    // ─────────────────────────────────────────────
    private void CreateProfileItem(NpcProfileEntry entry)
    {
        if (profileContent == null || talkProfilePrefab == null) return;

        GameObject instance = Instantiate(talkProfilePrefab, profileContent);
        instance.name = "TalkProfile_" + entry.npcName;

        // TalkProflie 스크립트가 있다고 가정 (User 코드 기반)
        var profile = instance.GetComponent<TalkProflie>();
        if (profile == null) profile = instance.AddComponent<TalkProflie>();

        string preview = entry.messengerPreview;
        if (string.IsNullOrEmpty(preview) && entry.conditionNames != null && entry.conditionNames.Length > 0)
            preview = entry.conditionNames[0];

        profile.Setup(entry.npcName, entry.profileSprite, preview);
        profile.InitChatting(talkDetailContent, chattingPrefab);
    }

    public string[] GetConditionsForNpc(string npcName)
    {
        foreach (var profile in npcProfiles)
        {
            if (profile.npcName == npcName) return profile.conditionNames;
        }
        return null;
    }

    private NpcProfileEntry FindNpcProfile(string npcName)
    {
        foreach (var profile in npcProfiles)
        {
            if (profile.npcName == npcName) return profile;
        }
        return null;
    }

    // ─────────────────────────────────────────────
    // 대사 재생 로직
    // ─────────────────────────────────────────────
    public void StartNpcDialogueByFirstCondition(string npcName)
    {
        string[] conds = GetConditionsForNpc(npcName);
        if (conds == null || conds.Length == 0) return;

        if (currentDialogueRoutine != null) StopCoroutine(currentDialogueRoutine);
        currentDialogueRoutine = StartCoroutine(PlayDialogueSequence(npcName, conds[0]));
    }

    private IEnumerator PlayDialogueSequence(string npcName, string conditionName)
    {
        if (!useLocalization) yield break;

        string tableName = $"Chatting_Detail_{npcName}";
        int index = 1;
        currentBranchSuffix = "";

        while (true)
        {
            bool anyAction = false;
            string indexStr = index.ToString("000");

            // 1. 분기 확인
            if (!string.IsNullOrEmpty(currentBranchSuffix))
            {
                string branchKey = $"Dialogue_{conditionName}{currentBranchSuffix}_{indexStr}";
                string branchKeyNext = $"Dialogue_{conditionName}{currentBranchSuffix}_Next_{indexStr}";

                if (TryGetLocalizedString(tableName, branchKey, out string localizedText))
                {
                    anyAction = true;
                    yield return PlayOneLocalizedLine(npcName, localizedText, false);
                }
                else if (TryGetLocalizedString(tableName, branchKeyNext, out localizedText))
                {
                    anyAction = true;
                    yield return PlayOneLocalizedLine(npcName, localizedText, true);
                }
                else
                {
                    currentBranchSuffix = ""; // 분기 종료
                }
            }

            // 2. 일반 대사 확인
            if (!anyAction)
            {
                string normalKey = $"Dialogue_{conditionName}_{indexStr}";
                string normalKeyNext = $"Dialogue_{conditionName}_Next_{indexStr}";

                if (TryGetLocalizedString(tableName, normalKey, out string text))
                {
                    anyAction = true;
                    yield return PlayOneLocalizedLine(npcName, text, false);
                }
                else if (TryGetLocalizedString(tableName, normalKeyNext, out text))
                {
                    anyAction = true;
                    yield return PlayOneLocalizedLine(npcName, text, true);
                }
            }

            // 3. 선택지 확인
            if (!anyAction)
            {
                if (TryFindChoiceGroup(tableName, conditionName, index, out int choiceGroup))
                {
                    anyAction = true;
                    yield return PlayChoiceAndSetBranch(npcName, tableName, conditionName, choiceGroup, index);
                }
            }

            if (!anyAction)
            {
                Debug.Log($"[ChattingManger] 대화 종료 index={index}");
                break;
            }
            index++;
        }
        currentDialogueRoutine = null;
    }

    private bool TryGetLocalizedString(string tableName, string key, out string localizedText)
    {
        localizedText = null;
        var handle = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(tableName, key);
        handle.WaitForCompletion();

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            localizedText = handle.Result;
            if (!string.IsNullOrEmpty(localizedText) && !localizedText.StartsWith("No translation found"))
                return true;
        }
        return false;
    }

    private IEnumerator PlayOneLocalizedLine(string npcName, string text, bool isNextType)
    {
        // 여기서 ChattingItemView를 가져오거나 생성
        ChattingItemView view = GetOrCreateChattingViewForNpc(npcName, isNextType);

        if (view != null)
        {
            yield return view.PlayTypingAndAppend(text, isNextType);

            // 말풍선 추가 후, View 높이가 변했으므로 레이아웃 갱신
            LayoutRebuilder.ForceRebuildLayoutImmediate(talkDetailContent);

            if (dialogueIntervalSeconds > 0f) yield return new WaitForSeconds(dialogueIntervalSeconds);
        }
    }

    // ─────────────────────────────────────────────
    // 선택지 및 플레이어 대사
    // ─────────────────────────────────────────────
    private bool TryFindChoiceGroup(string tableName, string conditionName, int index, out int group)
    {
        group = -1;
        string idxStr = index.ToString("000");
        for (int g = 0; g <= 9; g++)
        {
            if (TryGetLocalizedString(tableName, $"Dialogue_{conditionName}_Choice{g}_S1_{idxStr}", out _) ||
                TryGetLocalizedString(tableName, $"Dialogue_{conditionName}_Choice{g}_S2_{idxStr}", out _))
            {
                group = g;
                return true;
            }
        }
        return false;
    }

    private IEnumerator PlayChoiceAndSetBranch(string npcName, string tableName, string conditionName, int group, int currentIndex)
    {
        string idxStr = currentIndex.ToString("000");
        TryGetLocalizedString(tableName, $"Dialogue_{conditionName}_Choice{group}_S1_{idxStr}", out string s1Text);
        TryGetLocalizedString(tableName, $"Dialogue_{conditionName}_Choice{group}_S2_{idxStr}", out string s2Text);

        // 마지막 위치 찾기
        float baseY = GetContentBottomPosition();

        // 선택지 생성
        GameObject choiceGo = Instantiate(choicePrefab, talkDetailContent);
        if (choiceGo.transform is RectTransform rectChoice)
        {
            rectChoice.anchoredPosition = new Vector2(rectChoice.anchoredPosition.x, baseY + choicePrefabOffsetY);
        }

        var choiceView = choiceGo.GetComponent<ChattingChoiceView>();
        choiceView.Setup(string.IsNullOrEmpty(s1Text) ? null : s1Text, string.IsNullOrEmpty(s2Text) ? null : s2Text);

        yield return new WaitUntil(() => choiceView.IsSelected);

        int selectedIndex = choiceView.SelectedIndex;
        string playerText = (selectedIndex == 0) ? s1Text : s2Text;
        Destroy(choiceGo);

        // 플레이어 말풍선 생성
        if (playerTalkPrefab != null && !string.IsNullOrEmpty(playerText))
        {
            GameObject pGo = Instantiate(playerTalkPrefab, talkDetailContent);
            if (pGo.transform is RectTransform pRect)
            {
                // 선택지 제거 후 같은 위치 기준 + 오프셋
                pRect.anchoredPosition = new Vector2(pRect.anchoredPosition.x, baseY + playerTalkPrefabOffsetY);
            }
            if (pGo.GetComponent<PlayerTalkView>() is PlayerTalkView pView)
            {
                pView.Setup(playerText, 0); // Y값은 위에서 설정함
            }
        }

        // 분기 설정
        int nextIndex = currentIndex + 1;
        string nextIdxStr = nextIndex.ToString("000");
        string suffixA = (selectedIndex == 0) ? $"_Choice{group}_A1" : $"_Choice{group}_A2";
        string suffixSame = $"_Choice{group}_Same";

        if (CheckKeyExists(tableName, conditionName, suffixA, nextIdxStr)) currentBranchSuffix = suffixA;
        else if (CheckKeyExists(tableName, conditionName, suffixSame, nextIdxStr)) currentBranchSuffix = suffixSame;
        else currentBranchSuffix = "";

        LayoutRebuilder.ForceRebuildLayoutImmediate(talkDetailContent);
    }

    private bool CheckKeyExists(string tableName, string condName, string suffix, string indexStr)
    {
        string keyNormal = $"Dialogue_{condName}{suffix}_{indexStr}";
        string keyNext = $"Dialogue_{condName}{suffix}_Next_{indexStr}";
        return TryGetLocalizedString(tableName, keyNormal, out _) || TryGetLocalizedString(tableName, keyNext, out _);
    }

    // ─────────────────────────────────────────────
    // View 생성 및 관리 (핵심 수정됨)
    // ─────────────────────────────────────────────

    // 현재 Content의 가장 바닥 Y좌표를 계산하여 반환
    private float GetContentBottomPosition()
    {
        if (talkDetailContent.childCount == 0) return 0f;

        // 마지막 자식 찾기
        RectTransform lastChild = talkDetailContent.GetChild(talkDetailContent.childCount - 1) as RectTransform;
        if (lastChild == null) return 0f;

        // "마지막 자식의 Y 위치" - "그 자식의 높이" = 바닥 좌표
        return lastChild.anchoredPosition.y - lastChild.rect.height;
    }

    private ChattingItemView GetOrCreateChattingViewForNpc(string npcName, bool forceNewGroup)
    {
        // 1. 같은 NPC이고, 새 그룹(Next) 강제가 아니라면 기존 뷰 리턴
        if (!forceNewGroup && lastChatViewByNpc.TryGetValue(npcName, out ChattingItemView view) && view != null)
        {
            return view;
        }

        // 2. 새 그룹 생성
        GameObject go = Instantiate(chattingPrefab, talkDetailContent);

        // 3. 위치 설정 (수동 계산)
        RectTransform rt = go.GetComponent<RectTransform>();

        // 이전에 있던 마지막 요소의 바닥 위치를 가져옴
        float prevBottomY = 0f;
        if (talkDetailContent.childCount > 1) // 방금 만든 go가 있으므로 1보다 커야 이전 자식이 있음
        {
            RectTransform prevChild = talkDetailContent.GetChild(talkDetailContent.childCount - 2) as RectTransform;
            if (prevChild != null)
            {
                prevBottomY = prevChild.anchoredPosition.y - prevChild.rect.height;
            }
        }

        // 첫 요소면 0, 아니면 이전 요소 바닥 - 간격
        float newY = (talkDetailContent.childCount > 1) ? (prevBottomY - nextProfileSpacing) : 0f;

        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, newY);

        // 4. 초기화
        int cnt = chatGroupCounterByNpc.ContainsKey(npcName) ? chatGroupCounterByNpc[npcName] : 0;
        cnt++;
        chatGroupCounterByNpc[npcName] = cnt;
        go.name = $"{npcName}_Chat_{cnt:000}";

        ChattingItemView newView = go.GetComponent<ChattingItemView>();
        NpcProfileEntry profile = FindNpcProfile(npcName);

        if (profile != null) newView.Setup(profile.npcName, profile.profileSprite);
        else newView.Setup(npcName, null);

        lastChatViewByNpc[npcName] = newView;

        return newView;
    }
}