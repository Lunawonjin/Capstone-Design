using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// - StringTable의 Dialogue_* 키를 자동 정렬해 순서대로 재생
/// - 키 형식 예: Dialogue_001_Player / Dialogue_002_President
///   · 마지막 토큰을 화자 ID로 사용(Player, President 등)
/// - 말풍선/앵커는 "공유 1세트"를 사용하고, 매 줄마다 Anchor.Target을 강제 교체+즉시 스냅
/// - Space/좌클릭: 타자 중이면 즉시 완성, 완료 상태면 다음 줄
/// </summary>
[DisallowMultipleComponent]
public class MultiSpeakerDialogueDirector_ForceRetarget : MonoBehaviour
{
    [Serializable]
    public class SpeakerTarget
    {
        [Tooltip("화자 ID (키 접미사). 예: Player, President")]
        public string speakerId;

        [Tooltip("이 화자의 머리 Transform(말풍선이 따라갈 대상)")]
        public Transform head;
    }

    public enum MissingSpeakerBehavior { SkipLine, Stop }

    [Header("String Table 이름")]
    [SerializeField] private string tableName = "Dialogue_Main";

    [Header("공유 말풍선/앵커 (한 세트만 사용)")]
    [Tooltip("타자기 효과(로컬라이즈 포함) 컴포넌트")]
    [SerializeField] private BalloonAutoTyper_LocalizedFX sharedBubble;

    [Tooltip("월드→UI 배치 앵커(여기 Target을 줄마다 바꾼다)")]
    [SerializeField] private WorldBubbleAnchor sharedAnchor;

    [Header("화자 → 타겟 머리 매핑")]
    [SerializeField] private List<SpeakerTarget> speakers = new();

    [Header("재생/예외 옵션")]
    [SerializeField] private bool playOnStart = true;
    [Tooltip("첫 줄에서만 등장 연출 사용")]
    [SerializeField] private bool firstLineWithEnterFX = true;
    [SerializeField] private MissingSpeakerBehavior missingSpeakerBehavior = MissingSpeakerBehavior.SkipLine;

    // 내부 상태
    private readonly List<string> orderedKeys = new();
    private int index = -1;
    private bool isRunning;

    void Reset()
    {
        if (!sharedAnchor) sharedAnchor = GetComponentInChildren<WorldBubbleAnchor>(true);
        if (!sharedBubble) sharedBubble = GetComponentInChildren<BalloonAutoTyper_LocalizedFX>(true);
    }

    void Start()
    {
        if (!sharedAnchor || !sharedBubble)
        {
            Debug.LogError("[DialogueDirector] sharedAnchor / sharedBubble 미지정. 인스펙터에서 연결하세요.");
            enabled = false;
            return;
        }
        StartCoroutine(Co_LoadAndBegin());
    }

    private System.Collections.IEnumerator Co_LoadAndBegin()
    {
        yield return LocalizationSettings.InitializationOperation;

        AsyncOperationHandle<StringTable> h = LocalizationSettings.StringDatabase.GetTableAsync(tableName);
        yield return h;

        if (!h.IsValid() || h.Result == null)
        {
            Debug.LogError($"[DialogueDirector] 테이블 '{tableName}' 로드 실패");
            yield break;
        }

        // Dialogue_* 키 수집 후 숫자 기준 정렬
        foreach (var e in h.Result.SharedData.Entries)
        {
            string k = e.Key;
            if (!string.IsNullOrEmpty(k) && k.StartsWith("Dialogue_"))
                orderedKeys.Add(k);
        }
        orderedKeys.Sort((a, b) => ExtractNum(a).CompareTo(ExtractNum(b)));

        if (orderedKeys.Count == 0)
        {
            Debug.LogError($"[DialogueDirector] 테이블 '{tableName}'에 'Dialogue_*' 키가 없습니다.");
            yield break;
        }

        if (playOnStart) Begin();
    }

    public void Begin()
    {
        index = -1;
        isRunning = true;
        Next();
    }

    void Update()
    {
        if (!isRunning) return;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
        {
            if (sharedBubble != null && sharedBubble.IsTypingNow)
            {
                // 타이핑 중이면 즉시 완성
                sharedBubble.CompleteInstant();
                return;
            }
            // 완료 상태면 다음 줄
            Next();
        }
    }

    private void Next()
    {
        index++;
        if (index >= orderedKeys.Count)
        {
            isRunning = false;
            // 마지막 줄 끝나면 사라지게 하려면 아래 주석 해제
            // sharedBubble.Hide(true);
            return;
        }

        string key = orderedKeys[index];
        string speakerId = ExtractSpeakerId(key);

        // ★ 핵심: 줄마다 타겟 강제 교체 + 즉시 스냅
        var head = ResolveHead(speakerId);
        if (head == null)
        {
            string msg = $"[DialogueDirector] 화자 '{speakerId}'의 머리 Transform 미지정";
            if (missingSpeakerBehavior == MissingSpeakerBehavior.SkipLine)
            {
                Debug.LogWarning(msg + " → 스킵");
                Next();
                return;
            }
            Debug.LogError(msg + " → 중단");
            isRunning = false;
            return;
        }
        sharedAnchor.SetTarget(head, snapNow: true);

        bool withEnter = (index == 0) ? firstLineWithEnterFX : false;
        sharedBubble.ShowLocalized(tableName, key, withEnter);
    }

    // ===== 유틸 =====

    private Transform ResolveHead(string idRaw)
    {
        string id = (idRaw ?? "").Trim();
        foreach (var s in speakers)
        {
            string sid = (s.speakerId ?? "").Trim();
            if (string.Equals(sid, id, StringComparison.OrdinalIgnoreCase))
                return s.head;
        }
        return null;
    }

    private static int ExtractNum(string key)
    {
        // Dialogue_012_Player → 12
        var p = key.Split('_');
        if (p.Length < 2) return int.MaxValue;
        return int.TryParse(p[1], out int n) ? n : int.MaxValue;
    }

    private static string ExtractSpeakerId(string key)
    {
        // Dialogue_001_Player → Player
        int idx = key.LastIndexOf('_');
        if (idx < 0 || idx >= key.Length - 1) return "Unknown";
        return key[(idx + 1)..].Trim();
    }
}
