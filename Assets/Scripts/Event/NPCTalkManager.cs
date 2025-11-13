using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 플레이어가 NPC와 충돌 중(F 키) → DialogueManager를 활성화하고
/// Sol_Talk_01 같은 이벤트 이름을 그대로 실행한다.
/// "_Dialogue" 접미사는 전혀 사용하지 않는다.
/// 대화 종료하면 DialogueManager를 자동으로 비활성화.
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

    [Tooltip("Sol_Talk_XX의 마지막 번호 (01~10 등)")]
    [SerializeField] private int autoMaxIndex = 10;

    [Header("하루에 몇 개의 랜덤 이벤트 실행할지")]
    [SerializeField] private int randomEventCountPerDay = 2;

    [Header("오늘 이미 대화했다면 막기")]
    [SerializeField] private bool ignoreIfAlreadyTalkedToday = true;

    [Header("디버그 로그")]
    [SerializeField] private bool enableDebugLog = true;

    private bool _playerInRange = false;
    private bool _isTalking = false;
    private readonly Queue<string> _pendingEvents = new Queue<string>();

    private void Awake()
    {
        if (dialogueManagerObject != null && dialogueManagerObject.activeSelf)
            dialogueManagerObject.SetActive(false); // 시작 시 DialogueManager OFF

        if (dialogueRunner == null && dialogueManagerObject != null)
            dialogueRunner = dialogueManagerObject.GetComponent<DialogueRunnerStringTables>();
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

    // 실제 충돌 방식으로 감지
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
        Log("TodayTalkFlag = " + todayFlag);

        if (ignoreIfAlreadyTalkedToday && todayFlag)
        {
            Log("오늘 이미 대화함 → 종료");
            return;
        }

        List<string> candidates = BuildCandidateEventList();
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[NPCTalkManager] 후보 이벤트 없음");
            return;
        }

        EnqueueRandomEvents(candidates);

        // DialogueManager 켜기
        if (dialogueManagerObject != null && !dialogueManagerObject.activeSelf)
        {
            dialogueManagerObject.SetActive(true);
            Log("DialogueManager 활성화됨");
        }

        if (dialogueRunner == null)
            dialogueRunner = dialogueManagerObject.GetComponent<DialogueRunnerStringTables>();

        _isTalking = true;
        StartNextEvent();
    }

    // 자동으로 Sol_Talk_01 ~ Sol_Talk_10 생성
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

    private void EnqueueRandomEvents(List<string> candidates)
    {
        _pendingEvents.Clear();

        List<string> temp = new List<string>(candidates);
        for (int i = 0; i < temp.Count; i++)
        {
            int j = Random.Range(i, temp.Count);
            (temp[i], temp[j]) = (temp[j], temp[i]);
        }

        int count = Mathf.Min(randomEventCountPerDay, temp.Count);
        for (int i = 0; i < count; i++)
            _pendingEvents.Enqueue(temp[i]);

        Log("오늘 선택된 대화: " + string.Join(", ", _pendingEvents));
    }

    private void StartNextEvent()
    {
        if (_pendingEvents.Count == 0)
        {
            EndTalkSequence();
            return;
        }

        string eventName = _pendingEvents.Dequeue();
        Log("대화 실행: " + eventName);

        // ★ 여기서 "_Dialogue"를 붙이지 않음. 그대로 실행함.
        dialogueRunner.BeginWithEventName(eventName);
    }

    // DialogueRunner가 호출해야 하는 함수
    public void OnDialogueEndedFromRunner()
    {
        Log("대화 한 개 종료");

        if (_pendingEvents.Count > 0)
        {
            StartNextEvent();
        }
        else
        {
            var dm = DataManager.instance;
            if (dm != null)
            {
                SetTodayTalkFlag(dm.nowPlayer, npcId, true);
                Log("TodayTalkFlag true로 변경");

                dm.SubSaveCommit();
            }

            EndTalkSequence();
        }
    }

    private void EndTalkSequence()
    {
        Log("전체 대화 종료");

        _pendingEvents.Clear();
        _isTalking = false;

        // DialogueManager OFF
        if (dialogueManagerObject != null && dialogueManagerObject.activeSelf)
        {
            dialogueManagerObject.SetActive(false);
            Log("DialogueManager 비활성화됨");
        }
    }

    // PlayerData 플래그 읽고 쓰기
    private bool GetTodayTalkFlag(PlayerData data, string id)
    {
        string field = id + "_Today_Talk";
        FieldInfo f = typeof(PlayerData).GetField(field);
        if (f == null) return false;

        return (bool)f.GetValue(data);
    }

    private void SetTodayTalkFlag(PlayerData data, string id, bool value)
    {
        string field = id + "_Today_Talk";
        FieldInfo f = typeof(PlayerData).GetField(field);
        if (f == null) return;

        f.SetValue(data, value);
    }

    private void Log(string msg)
    {
        if (!enableDebugLog) return;
        Debug.Log("[NPCTalkManager][" + npcId + "] " + msg);
    }
}
