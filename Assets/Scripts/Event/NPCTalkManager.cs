using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCTalkManager : MonoBehaviour
{
    [Header("Player detection")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private KeyCode talkKey = KeyCode.F;

    [Header("DialogueManager GameObject")]
    [SerializeField] private GameObject dialogueManagerObject;

    [Header("DialogueRunnerStringTables (must be on DialogueManager)")]
    [SerializeField] private DialogueRunnerStringTables dialogueRunner;

    [Header("NPC ID (ex: Sol -> Sol_Talk_01)")]
    [SerializeField] private string npcId = "Sol";

    [Header("Random event auto list")]
    [Tooltip("Auto generate Sol_Talk_01 ~ Sol_Talk_XX")]
    [SerializeField] private bool autoFillFromNpcId = true;

    [Tooltip("Last index of Sol_Talk_XX (01~10 etc)")]
    [SerializeField] private int autoMaxIndex = 10;

    [Header("How many random talks per day")]
    [SerializeField] private int randomEventCountPerDay = 2;

    [Header("Block if already talked today")]
    [SerializeField] private bool ignoreIfAlreadyTalkedToday = true;

    [Header("Debug log")]
    [SerializeField] private bool enableDebugLog = true;

    private bool _playerInRange = false;
    private bool _isTalking = false;
    private readonly Queue<string> _pendingEvents = new Queue<string>();

    private void Awake()
    {
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

    private void HandleRunnerEnded()
    {
        OnDialogueEndedFromRunner();
    }

    private void Update()
    {
        if (!_playerInRange) return;
        if (_isTalking) return;

        if (Input.GetKeyDown(talkKey))
        {
            Log("F pressed -> try talk");
            TryStartTalk();
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            _playerInRange = true;
            Log("Player entered");
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            _playerInRange = false;
            Log("Player exited");
        }
    }

    private void TryStartTalk()
    {
        var dm = DataManager.instance;
        if (dm == null || dm.nowPlayer == null)
        {
            Debug.LogWarning("[NPCTalkManager] DataManager or nowPlayer missing");
            return;
        }

        bool todayFlag = GetTodayTalkFlag(dm.nowPlayer, npcId);
        Log("TodayTalkFlag = " + todayFlag);

        if (ignoreIfAlreadyTalkedToday && todayFlag)
        {
            Log("Already talked today -> skip");
            return;
        }

        List<string> candidates = BuildCandidateEventList();
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[NPCTalkManager] No candidate events");
            return;
        }

        EnqueueRandomEvents(candidates);

        if (dialogueManagerObject != null && !dialogueManagerObject.activeSelf)
        {
            dialogueManagerObject.SetActive(true);
            Log("DialogueManager activated");
        }

        if (dialogueRunner == null && dialogueManagerObject != null)
        {
            dialogueRunner = dialogueManagerObject.GetComponent<DialogueRunnerStringTables>();
            if (dialogueRunner != null)
                dialogueRunner.OnDialogueEnded += HandleRunnerEnded;
        }

        _isTalking = true;
        StartNextEvent();
    }

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
            Log("Auto candidates: " + string.Join(", ", list));
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

        Log("Selected talks today: " + string.Join(", ", _pendingEvents));
    }

    private void StartNextEvent()
    {
        if (_pendingEvents.Count == 0)
        {
            EndTalkSequence();
            return;
        }

        string eventName = _pendingEvents.Dequeue();
        Log("Run talk: " + eventName);

        dialogueRunner.BeginWithEventName(eventName);
    }

    public void OnDialogueEndedFromRunner()
    {
        Log("One dialogue ended");

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
                Log("TodayTalkFlag set true");
                dm.SubSaveCommit();
            }

            EndTalkSequence();
        }
    }

    private void EndTalkSequence()
    {
        Log("All dialogues finished");

        _pendingEvents.Clear();
        _isTalking = false;

        if (dialogueManagerObject != null && dialogueManagerObject.activeSelf)
        {
            dialogueManagerObject.SetActive(false);
            Log("DialogueManager deactivated");
        }
    }

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
