using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// - 플레이어와 NPC가 2D 충돌 중일 때 F 키를 누르면
///   · Sol_Talk_01 ~ Sol_Talk_XX 중에서 랜덤으로 "한 줄"만 플레이
///   · 대화가 끝나면 자동으로 다음 대사를 실행하지 않고 종료
/// - 하루에 이 NPC가 줄 수 있는 대사는 최대 randomEventCountPerDay번
///   · 그 이상이면 {NPC}_Today_Talk 를 true로 바꾸고 더 이상 대화 시작하지 않음
/// - Today_Talk 플래그는 PlayerData의 "{npcId}_Today_Talk" bool 필드를 사용
/// </summary>
[DisallowMultipleComponent]
public class NPCTalkManager : MonoBehaviour
{
    [Header("플레이어 인식 설정")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private KeyCode talkKey = KeyCode.F;

    [Header("DialogueManager 오브젝트 (끄고 켜기)")]
    [SerializeField] private GameObject dialogueManagerObject;

    [Header("DialogueRunnerStringTables (DialogueManager 안에 있어야 함)")]
    [SerializeField] private DialogueRunnerStringTables dialogueRunner;

    [Header("NPC ID (예: Sol → Sol_Talk_01)")]
    [SerializeField] private string npcId = "Sol";

    [Header("랜덤 대사 자동 생성 옵션")]
    [Tooltip("자동으로 Sol_Talk_01 ~ Sol_Talk_XX 생성")]
    [SerializeField] private bool autoFillFromNpcId = true;

    [Tooltip("Sol_Talk_XX의 마지막 번호 (01~27 등)")]
    [SerializeField] private int autoMaxIndex = 10;

    [Header("하루에 몇 번까지 대사 허용할지")]
    [Tooltip("오늘 이 NPC가 말해 줄 수 있는 최대 횟수")]
    [SerializeField] private int randomEventCountPerDay = 2;

    [Header("오늘 이미 최대치면 막기")]
    [SerializeField] private bool ignoreIfAlreadyTalkedToday = true;

    [Header("디버그 로그")]
    [SerializeField] private bool enableDebugLog = true;

    private bool _playerInRange = false;
    private bool _isTalking = false;
    private string _currentEventName = null;

    // 런타임 동안 오늘 몇 번 말했는지 저장 (npcId 기준)
    private static readonly Dictionary<string, int> s_todayTalkCount = new Dictionary<string, int>();
    // 이미 사용한 키 기록 (중복 방지용)
    private static readonly Dictionary<string, HashSet<string>> s_usedKeysToday = new Dictionary<string, HashSet<string>>();
    // 마지막으로 본 Today_Talk 플래그 (날짜 변경 감지용)
    private static readonly Dictionary<string, bool> s_lastTodayFlag = new Dictionary<string, bool>();

    private void Awake()
    {
        // 시작 시 DialogueManager는 꺼 둔다
        if (dialogueManagerObject != null && dialogueManagerObject.activeSelf)
            dialogueManagerObject.SetActive(false);

        if (dialogueRunner == null && dialogueManagerObject != null)
            dialogueRunner = dialogueManagerObject.GetComponent<DialogueRunnerStringTables>();

        if (dialogueRunner != null)
            dialogueRunner.OnDialogueEnded += HandleRunnerEnded;
    }

    private void OnDestroy()
    {
        if (dialogueRunner != null)
            dialogueRunner.OnDialogueEnded -= HandleRunnerEnded;
    }

    // DialogueRunner 쪽에서 "이번 대화 한 번 끝났다"라고 알려줄 때
    private void HandleRunnerEnded()
    {
        if (_isTalking)
            OnDialogueEndedFromRunner();
    }

    private void Update()
    {
        if (!_playerInRange) return;
        if (_isTalking) return;

        if (Input.GetKeyDown(talkKey))
        {
            Log("F 입력 감지 → 대화 시도");
            TryStartTalk();
        }
    }

    // 2D 충돌 감지(둘 다 isTrigger 꺼져 있어야 함)
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            _playerInRange = true;
            Log("플레이어 충돌 진입");
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            _playerInRange = false;
            Log("플레이어 충돌 해제");
        }
    }

    private void TryStartTalk()
    {
        var dm = DataManager.instance;
        if (dm == null || dm.nowPlayer == null)
        {
            Debug.LogWarning("[NPCTalkManager] DataManager 또는 nowPlayer 없음");
            return;
        }

        bool todayFlag = GetTodayTalkFlag(dm.nowPlayer, npcId);
        bool lastFlag = false;
        s_lastTodayFlag.TryGetValue(npcId, out lastFlag);

        // 어제까지 true였다가 오늘 false로 바뀌면 "날짜가 바뀐 것"으로 보고 카운트 리셋
        if (lastFlag && !todayFlag)
        {
            Log("Today_Talk 플래그가 true → false 로 변경됨, 새 날짜로 판단하고 카운트 리셋");
            s_todayTalkCount.Remove(npcId);
            s_usedKeysToday.Remove(npcId);
        }

        s_lastTodayFlag[npcId] = todayFlag;

        if (ignoreIfAlreadyTalkedToday && todayFlag)
        {
            Log("오늘 이미 최대 횟수 도달(플래그 true) → 대화 시작 안 함");
            return;
        }

        int currentCount = GetTodayTalkCount(npcId);
        Log($"현재 TodayTalkCount = {currentCount}");

        if (currentCount >= randomEventCountPerDay)
        {
            Log("로컬 카운트 기준으로 오늘 한도 도달 → 플래그 true 설정 후 종료");
            SetTodayTalkFlag(dm.nowPlayer, npcId, true);
            dm.SubSaveCommit();
            return;
        }

        // 오늘 쓸 수 있는 키 후보 생성
        List<string> candidates = BuildCandidateEventList();
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[NPCTalkManager] 후보 이벤트 없음");
            return;
        }

        // 이미 사용한 키는 가능한 한 피한다
        HashSet<string> usedSet = GetUsedSet(npcId);
        List<string> available = new List<string>();
        foreach (string k in candidates)
        {
            if (!usedSet.Contains(k))
                available.Add(k);
        }

        if (available.Count == 0)
        {
            // 전부 사용했으면 그냥 전체 후보 중에서 다시 뽑는다
            available = candidates;
        }

        // 한 번에 하나만 뽑는다
        string selected = available[Random.Range(0, available.Count)];
        _currentEventName = selected;

        Log("이번에 선택된 대사 키: " + selected);

        // DialogueManager 켜기
        if (dialogueManagerObject != null && !dialogueManagerObject.activeSelf)
        {
            dialogueManagerObject.SetActive(true);
            Log("DialogueManager 활성화됨");
        }

        if (dialogueRunner == null && dialogueManagerObject != null)
        {
            dialogueRunner = dialogueManagerObject.GetComponent<DialogueRunnerStringTables>();
            if (dialogueRunner != null)
                dialogueRunner.OnDialogueEnded += HandleRunnerEnded;
        }

        if (dialogueRunner == null)
        {
            Debug.LogWarning("[NPCTalkManager] DialogueRunnerStringTables 참조 없음");
            return;
        }

        _isTalking = true;
        dialogueRunner.BeginWithEventName(_currentEventName);
    }

    // Sol_Talk_01 ~ Sol_Talk_XX 리스트 생성
    private List<string> BuildCandidateEventList()
    {
        List<string> list = new List<string>();

        if (autoFillFromNpcId)
        {
            for (int i = 1; i <= autoMaxIndex; i++)
            {
                string key = $"{npcId}_Talk_{i:00}";
                list.Add(key);
            }
            Log("자동 생성 후보: " + string.Join(", ", list));
        }

        return list;
    }

    // 러너에서 "대사 1번"이 끝났다고 알려줬을 때 호출
    public void OnDialogueEndedFromRunner()
    {
        Log("대사 한 번 종료");

        if (string.IsNullOrEmpty(_currentEventName))
        {
            Log("현재 이벤트 이름이 없음 → 그냥 종료");
            EndTalkSequence(false);
            return;
        }

        var dm = DataManager.instance;
        if (dm == null || dm.nowPlayer == null)
        {
            Log("DataManager 또는 nowPlayer 없음 → 카운트만 정리");
            EndTalkSequence(false);
            return;
        }

        // 오늘 사용 횟수 +1
        int currentCount = GetTodayTalkCount(npcId);
        currentCount++;
        SetTodayTalkCount(npcId, currentCount);

        // 이번에 사용한 키 기록
        HashSet<string> used = GetUsedSet(npcId);
        used.Add(_currentEventName);

        Log($"이번 키 '{_currentEventName}' 사용, 오늘 누적 횟수 = {currentCount}");

        // 한도를 초과하거나 딱 맞게 되면 Today_Talk 플래그 true 설정
        if (currentCount >= randomEventCountPerDay)
        {
            SetTodayTalkFlag(dm.nowPlayer, npcId, true);
            dm.SubSaveCommit();
            Log("오늘 최대 횟수 도달 → Today_Talk = true 로 설정 후 저장");
        }

        EndTalkSequence(true);
    }

    private void EndTalkSequence(bool fromRunner)
    {
        Log("대화 시퀀스 종료");

        _currentEventName = null;
        _isTalking = false;

        // DialogueManager 끄기
        if (dialogueManagerObject != null && dialogueManagerObject.activeSelf)
        {
            dialogueManagerObject.SetActive(false);
            Log("DialogueManager 비활성화됨");
        }
    }

    // PlayerData 플래그 읽고 쓰기
    private bool GetTodayTalkFlag(PlayerData data, string id)
    {
        string fieldName = id + "_Today_Talk";
        FieldInfo f = typeof(PlayerData).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (f == null || f.FieldType != typeof(bool))
        {
            Debug.LogWarning($"[NPCTalkManager] PlayerData에 bool 필드 '{fieldName}' 이(가) 없습니다.");
            return false;
        }

        object v = f.GetValue(data);
        return v is bool b && b;
    }

    private void SetTodayTalkFlag(PlayerData data, string id, bool value)
    {
        string fieldName = id + "_Today_Talk";
        FieldInfo f = typeof(PlayerData).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (f == null || f.FieldType != typeof(bool))
        {
            Debug.LogWarning($"[NPCTalkManager] PlayerData에 bool 필드 '{fieldName}' 이(가) 없습니다.");
            return;
        }

        f.SetValue(data, value);
    }

    // 오늘 사용 횟수 로컬 관리
    private static int GetTodayTalkCount(string id)
    {
        if (!s_todayTalkCount.TryGetValue(id, out int c))
            c = 0;
        return c;
    }

    private static void SetTodayTalkCount(string id, int value)
    {
        s_todayTalkCount[id] = value;
    }

    private static HashSet<string> GetUsedSet(string id)
    {
        if (!s_usedKeysToday.TryGetValue(id, out var set))
        {
            set = new HashSet<string>();
            s_usedKeysToday[id] = set;
        }
        return set;
    }

    private void Log(string msg)
    {
        if (!enableDebugLog) return;
        Debug.Log("[NPCTalkManager][" + npcId + "] " + msg);
    }
}
