using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
        public string npcName;                  // 예: "Sero" → 테이블: Chatting_Detail_Sero
        public Sprite profileSprite;

        [Header("조건 이름 리스트")]
        public string[] conditionNames;        // 예: "SolMeetAfter"

        [Header("메신저 미리보기 텍스트")]
        [TextArea] public string messengerPreview;
    }

    [Header("프로필 리스트 영역")]
    [SerializeField] private RectTransform profileContent;   // ProflieScrollView Content

    [Header("프로필 프리팹")]
    [SerializeField] private GameObject talkProfilePrefab;   // TalkProflie 프리팹

    [Header("채팅 상세 뷰 영역")]
    [Tooltip("TalkDetailView의 Viewport/Content")]
    [SerializeField] private RectTransform talkDetailContent;

    [Header("NPC 말풍선 프리팹")]
    [Tooltip("오른쪽 채팅 영역 프리팹 (상단 프로필 + BalloonContainer 포함)")]
    [SerializeField] private GameObject chattingPrefab;

    [Header("선택지 / 플레이어 말풍선 프리팹")]
    [SerializeField] private GameObject choicePrefab;        // 선택지 UI (ChattingChoiceView)
    [SerializeField] private GameObject playerTalkPrefab;    // 플레이어 말풍선 (PlayerTalkView)

    [Header("NPC 프로필 설정")]
    [SerializeField] private NpcProfileEntry[] npcProfiles;

    [Header("키보드로 프로필 추가 기능")]
    [SerializeField] private bool useKeyboardAdd = true;
    [SerializeField] private KeyCode addKey = KeyCode.P;

    [Header("P 키 추가용 기본값")]
    [SerializeField] private string defaultNpcNameForKeyAdd = "New NPC";
    [SerializeField] private Sprite defaultProfileSpriteForKeyAdd;
    [SerializeField] private string[] defaultConditionsForKeyAdd;
    [TextArea]
    [SerializeField] private string defaultPreviewForKeyAdd = "새로운 메시지가 있습니다.";

    [Header("로컬라이제이션 사용 여부")]
    [SerializeField] private bool useLocalization = true;

    [Header("대사 사이 딜레이")]
    [SerializeField] private float dialogueIntervalSeconds = 1.5f;

    // 내부 상태
    private int createdCount = 0;

    // NPC 이름 → 마지막으로 사용한 ChattingItemView
    private readonly Dictionary<string, ChattingItemView> lastChatViewByNpc =
        new Dictionary<string, ChattingItemView>();

    // NPC 이름 → 생성된 ChattingPrefab 그룹 수
    private readonly Dictionary<string, int> chatGroupCounterByNpc =
        new Dictionary<string, int>();

    // 현재 실행 중인 대사 시퀀스
    private Coroutine currentDialogueRoutine;

    // ─────────────────────────────────────────────
    // Unity 생명주기
    // ─────────────────────────────────────────────

    private void Start()
    {
        Debug.Log("[ChattingManger] Start 호출");

        if (npcProfiles != null && npcProfiles.Length > 0)
        {
            for (int i = 0; i < npcProfiles.Length; i++)
            {
                NpcProfileEntry entry = npcProfiles[i];
                if (entry != null && !string.IsNullOrEmpty(entry.npcName))
                {
                    Debug.Log($"[ChattingManger] 프로필 생성: {entry.npcName}");
                    CreateProfileItem(entry);
                }
            }
        }
        else
        {
            Debug.LogWarning("[ChattingManger] npcProfiles가 비어 있습니다.");
        }
    }

    private void Update()
    {
        if (useKeyboardAdd && Input.GetKeyDown(addKey))
        {
            createdCount++;

            NpcProfileEntry entry = new NpcProfileEntry();
            entry.npcName = string.Format("{0} {1}", defaultNpcNameForKeyAdd, createdCount);
            entry.profileSprite = defaultProfileSpriteForKeyAdd;
            entry.conditionNames = defaultConditionsForKeyAdd;
            entry.messengerPreview = defaultPreviewForKeyAdd;

            Debug.Log($"[ChattingManger] P 키로 프로필 추가: {entry.npcName}");
            CreateProfileItem(entry);
        }
    }

    // ─────────────────────────────────────────────
    // 프로필 생성
    // ─────────────────────────────────────────────

    private void CreateProfileItem(NpcProfileEntry entry)
    {
        if (profileContent == null || talkProfilePrefab == null)
        {
            Debug.LogWarning("[ChattingManger] profileContent 또는 talkProfilePrefab이 비어 있습니다.");
            return;
        }

        if (entry == null || string.IsNullOrEmpty(entry.npcName))
        {
            Debug.LogWarning("[ChattingManger] NPC 정보가 비어 있습니다.");
            return;
        }

        GameObject instance = Instantiate(talkProfilePrefab, profileContent);
        instance.name = "TalkProfile_" + entry.npcName;

        TalkProflie profile = instance.GetComponent<TalkProflie>();
        if (profile == null)
        {
            profile = instance.AddComponent<TalkProflie>();
            Debug.Log("[ChattingManger] TalkProflie 컴포넌트를 자동으로 추가했습니다.");
        }

        string preview = entry.messengerPreview;
        if (string.IsNullOrEmpty(preview))
        {
            if (entry.conditionNames != null && entry.conditionNames.Length > 0)
            {
                preview = entry.conditionNames[0];
            }
        }

        profile.Setup(entry.npcName, entry.profileSprite, preview);
        profile.InitChatting(talkDetailContent, chattingPrefab);

        Debug.Log($"[ChattingManger] TalkProfile 생성 완료: {entry.npcName}");
    }

    public string[] GetConditionsForNpc(string npcName)
    {
        if (npcProfiles == null || string.IsNullOrEmpty(npcName))
        {
            return null;
        }

        for (int i = 0; i < npcProfiles.Length; i++)
        {
            if (npcProfiles[i] != null && npcProfiles[i].npcName == npcName)
            {
                return npcProfiles[i].conditionNames;
            }
        }

        return null;
    }

    private NpcProfileEntry FindNpcProfile(string npcName)
    {
        if (npcProfiles == null || string.IsNullOrEmpty(npcName))
        {
            return null;
        }

        for (int i = 0; i < npcProfiles.Length; i++)
        {
            if (npcProfiles[i] != null && npcProfiles[i].npcName == npcName)
            {
                return npcProfiles[i];
            }
        }

        return null;
    }

    public void SetKeyboardAddEnabled(bool enabled)
    {
        useKeyboardAdd = enabled;
    }

    // ─────────────────────────────────────────────
    // 대사 시작 (조건 첫 줄부터 자동 재생)
    // ─────────────────────────────────────────────

    public void StartNpcDialogueByFirstCondition(string npcName)
    {
        Debug.Log($"[ChattingManger] StartNpcDialogueByFirstCondition 호출: npcName={npcName}");

        string[] conds = GetConditionsForNpc(npcName);
        if (conds == null || conds.Length == 0)
        {
            Debug.LogWarning($"[ChattingManger] NPC({npcName})에 등록된 조건 이름이 없습니다.");
            return;
        }

        string conditionName = conds[0];

        if (currentDialogueRoutine != null)
        {
            StopCoroutine(currentDialogueRoutine);
            currentDialogueRoutine = null;
        }

        currentDialogueRoutine = StartCoroutine(PlayDialogueSequence(npcName, conditionName));
    }

    // ─────────────────────────────────────────────
    // 대사 시퀀스: 001, 002, ... / Next / Choice
    // ─────────────────────────────────────────────

    private IEnumerator PlayDialogueSequence(string npcName, string conditionName)
    {
        if (!useLocalization)
        {
            Debug.LogWarning("[ChattingManger] useLocalization=false 상태입니다. 대사를 출력하지 않습니다.");
            yield break;
        }

        string tableName = $"Chatting_Detail_{npcName}";
        int index = 1;

        while (true)
        {
            bool anyLine = false;
            string indexStr = index.ToString("000");

            // 1) 일반 줄: Dialogue_조건명_001
            string keyNormal = $"Dialogue_{conditionName}_{indexStr}";
            string localizedText;

            if (TryGetLocalizedString(tableName, keyNormal, out localizedText))
            {
                anyLine = true;
                Debug.Log($"[ChattingManger] 시퀀스 일반 줄: {keyNormal} → \"{localizedText}\"");
                yield return PlayOneLocalizedLine(npcName, localizedText, false);
            }

            // 2) Next 줄: Dialogue_조건명_Next_001
            string keyNext = $"Dialogue_{conditionName}_Next_{indexStr}";
            if (TryGetLocalizedString(tableName, keyNext, out localizedText))
            {
                anyLine = true;
                Debug.Log($"[ChattingManger] 시퀀스 Next 줄: {keyNext} → \"{localizedText}\"");
                yield return PlayOneLocalizedLine(npcName, localizedText, true);
            }

            // 3) Choice 줄: Dialogue_조건명_ChoiceX_S1_001, S2_001 ...
            int choiceGroup;
            if (TryFindChoiceGroup(tableName, conditionName, index, out choiceGroup))
            {
                anyLine = true;
                Debug.Log($"[ChattingManger] index={index} 에 Choice 그룹 {choiceGroup} 발견");
                yield return PlayChoiceForIndex(npcName, conditionName, choiceGroup, index);
            }

            if (!anyLine)
            {
                Debug.Log($"[ChattingManger] index={index} 에 해당하는 대사가 없어 시퀀스를 종료합니다.");
                break;
            }

            index++;
        }

        currentDialogueRoutine = null;
    }

    /// <summary>
    /// 로컬라이즈 문자열 한 줄 가져오기
    ///  - 존재하지 않으면 false 반환
    /// </summary>
    private bool TryGetLocalizedString(string tableName, string key, out string localizedText)
    {
        localizedText = null;

        AsyncOperationHandle<string> handle =
            LocalizationSettings.StringDatabase.GetLocalizedStringAsync(tableName, key);

        handle.WaitForCompletion();

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            return false;
        }

        localizedText = handle.Result;

        if (string.IsNullOrEmpty(localizedText) ||
            localizedText.StartsWith("No translation found"))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 한 줄 재생: ... 애니메이션 → 말풍선 추가 → interval 만큼 대기
    /// </summary>
    private IEnumerator PlayOneLocalizedLine(string npcName, string localizedText, bool isNextType)
    {
        ChattingItemView chattingView = GetOrCreateChattingViewForNpc(npcName, isNextType);
        if (chattingView == null)
        {
            Debug.LogWarning($"[ChattingManger] NPC({npcName})용 ChattingItemView를 만들지 못했습니다.");
            yield break;
        }

        yield return chattingView.PlayTypingAndAppend(localizedText, isNextType);

        if (dialogueIntervalSeconds > 0f)
        {
            yield return new WaitForSeconds(dialogueIntervalSeconds);
        }
    }

    // ─────────────────────────────────────────────
    // Choice 탐색 / 실행
    // ─────────────────────────────────────────────

    /// <summary>
    /// 현재 index에서 어떤 ChoiceX가 있는지 찾는다.
    ///  - Dialogue_{cond}_Choice0_S1_003 또는 Choice1_S1_003 등
    /// </summary>
    private bool TryFindChoiceGroup(string tableName, string conditionName, int index, out int choiceGroup)
    {
        choiceGroup = -1;
        string indexStr = index.ToString("000");

        // Choice0 ~ Choice9 까지 검사
        for (int g = 0; g <= 9; g++)
        {
            string dummy;

            bool hasS1 = TryGetLocalizedString(
                tableName,
                $"Dialogue_{conditionName}_Choice{g}_S1_{indexStr}",
                out dummy
            );

            bool hasS2 = TryGetLocalizedString(
                tableName,
                $"Dialogue_{conditionName}_Choice{g}_S2_{indexStr}",
                out dummy
            );

            if (hasS1 || hasS2)
            {
                choiceGroup = g;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ChoiceX_S1/S2/Same/A1/A2 처리
    ///  - group: Choice 뒤 숫자 (예: 1 → Choice1)
    ///  - index: 뒤의 000 숫자
    /// </summary>
    private IEnumerator PlayChoiceForIndex(string npcName, string conditionName, int choiceGroup, int index)
    {
        if (choicePrefab == null)
        {
            Debug.LogWarning("[ChattingManger] choicePrefab이 비어 있습니다.");
            yield break;
        }

        string tableName = $"Chatting_Detail_{npcName}";
        string indexStr = index.ToString("000");

        string s1Text;
        string s2Text;

        bool hasS1 = TryGetLocalizedString(
            tableName,
            $"Dialogue_{conditionName}_Choice{choiceGroup}_S1_{indexStr}",
            out s1Text
        );

        bool hasS2 = TryGetLocalizedString(
            tableName,
            $"Dialogue_{conditionName}_Choice{choiceGroup}_S2_{indexStr}",
            out s2Text
        );

        if (!hasS1 && !hasS2)
        {
            yield break;
        }

        // 마지막 NPC 말풍선 Y (TalkDetailContent 기준) 구하기
        float lastNpcBalloonY;
        if (!TryGetLastBalloonYInContent(npcName, out lastNpcBalloonY))
        {
            lastNpcBalloonY = 0f;
        }

        // 1) 선택지 프리팹 생성
        GameObject choiceGo = Instantiate(choicePrefab, talkDetailContent);
        ChattingChoiceView choiceView = choiceGo.GetComponent<ChattingChoiceView>();

        if (choiceView == null)
        {
            Debug.LogWarning("[ChattingManger] choicePrefab에 ChattingChoiceView가 없습니다.");
            yield break;
        }

        // 위치 조정: 마지막 말풍선 Y에서 -140, 최소 -375
        RectTransform choiceRect = choiceGo.transform as RectTransform;
        if (choiceRect != null)
        {
            Vector2 pos = choiceRect.anchoredPosition;
            float targetY = lastNpcBalloonY - 140f;
            if (targetY < -375f) targetY = -375f;
            pos.y = targetY;
            choiceRect.anchoredPosition = pos;
        }

        choiceView.Setup(hasS1 ? s1Text : null, hasS2 ? s2Text : null);

        // 플레이어 선택 기다리기
        yield return new WaitUntil(() => choiceView.IsSelected);

        int selected = choiceView.SelectedIndex; // 0 = S1, 1 = S2
        string selectedText = (selected == 0) ? s1Text : s2Text;

        // 선택지 UI 제거
        Destroy(choiceGo);

        // 2) 플레이어 말풍선 출력 (마지막 NPC 말풍선 Y 기준)
        if (playerTalkPrefab != null && !string.IsNullOrEmpty(selectedText))
        {
            GameObject playerGo = Instantiate(playerTalkPrefab, talkDetailContent);
            PlayerTalkView playerView = playerGo.GetComponent<PlayerTalkView>();

            if (playerView != null)
            {
                float npcY;
                if (!TryGetLastBalloonYInContent(npcName, out npcY))
                    npcY = lastNpcBalloonY;

                playerView.Setup(selectedText, npcY);
            }
        }

        // 3) NPC 대답 키 확인
        string a1Text;
        string a2Text;
        string sameText;

        bool hasA1 = TryGetLocalizedString(
            tableName,
            $"Dialogue_{conditionName}_Choice{choiceGroup}_A1_{indexStr}",
            out a1Text
        );

        bool hasA2 = TryGetLocalizedString(
            tableName,
            $"Dialogue_{conditionName}_Choice{choiceGroup}_A2_{indexStr}",
            out a2Text
        );

        bool hasSame = TryGetLocalizedString(
            tableName,
            $"Dialogue_{conditionName}_Choice{choiceGroup}_Same_{indexStr}",
            out sameText
        );

        string npcReply = null;

        if (hasA1 || hasA2)
        {
            if (selected == 0 && hasA1)
            {
                npcReply = a1Text;
            }
            else if (selected == 1 && hasA2)
            {
                npcReply = a2Text;
            }
            else if (hasSame)
            {
                npcReply = sameText;
            }
        }
        else if (hasSame)
        {
            npcReply = sameText;
        }

        if (!string.IsNullOrEmpty(npcReply))
        {
            // 답변은 새 블록처럼 보이게 Next 타입으로 처리
            yield return PlayOneLocalizedLine(npcName, npcReply, true);
        }
    }

    // ─────────────────────────────────────────────
    // ChattingPrefab 선택/생성
    // ─────────────────────────────────────────────

    private ChattingItemView GetOrCreateChattingViewForNpc(string npcName, bool forceNewGroup)
    {
        if (talkDetailContent == null || chattingPrefab == null)
        {
            Debug.LogWarning("[ChattingManger] talkDetailContent 또는 chattingPrefab이 비어 있습니다.");
            return null;
        }

        ChattingItemView view;

        if (!forceNewGroup &&
            lastChatViewByNpc.TryGetValue(npcName, out view) &&
            view != null)
        {
            Debug.Log($"[ChattingManger] 기존 ChattingItemView 재사용: {view.gameObject.name}");
            return view;
        }

        if (!forceNewGroup)
        {
            ChattingItemView[] allViews =
                talkDetailContent.GetComponentsInChildren<ChattingItemView>(true);

            for (int i = 0; i < allViews.Length; i++)
            {
                if (allViews[i] != null && allViews[i].NpcName == npcName)
                {
                    lastChatViewByNpc[npcName] = allViews[i];
                    Debug.Log($"[ChattingManger] 자식에서 ChattingItemView 발견: {allViews[i].gameObject.name}");
                    return allViews[i];
                }
            }
        }

        Debug.Log("[ChattingManger] 새 ChattingPrefab 생성");

        GameObject go = Instantiate(chattingPrefab, talkDetailContent);

        int counter = 0;
        chatGroupCounterByNpc.TryGetValue(npcName, out counter);

        int nextIndex = counter + 1;
        chatGroupCounterByNpc[npcName] = nextIndex;

        string name = $"{npcName}_Chat_{nextIndex:000}";

        go.name = name;
        go.SetActive(true);

        view = go.GetComponent<ChattingItemView>();
        if (view == null)
        {
            Debug.LogWarning("[ChattingManger] ChattingPrefab에 ChattingItemView가 없습니다.");
            return null;
        }

        NpcProfileEntry profile = FindNpcProfile(npcName);
        if (profile != null)
        {
            view.Setup(profile.npcName, profile.profileSprite);
        }
        else
        {
            view.Setup(npcName, null);
        }

        lastChatViewByNpc[npcName] = view;
        Debug.Log($"[ChattingManger] 새 ChattingItemView 생성: {go.name}");
        return view;
    }

    /// <summary>
    /// 마지막 NPC 말풍선의 Y를 TalkDetailContent 기준 로컬 좌표로 가져옴
    /// </summary>
    private bool TryGetLastBalloonYInContent(string npcName, out float y)
    {
        y = 0f;

        ChattingItemView view;
        if (!lastChatViewByNpc.TryGetValue(npcName, out view) || view == null)
        {
            return false;
        }

        Vector3 worldPos;
        if (!view.TryGetLastBalloonWorldPos(out worldPos))
        {
            return false;
        }

        if (talkDetailContent == null)
        {
            return false;
        }

        Vector3 local = talkDetailContent.InverseTransformPoint(worldPos);
        y = local.y;
        return true;
    }
}
