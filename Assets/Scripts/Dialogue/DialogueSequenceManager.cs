using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

[DisallowMultipleComponent]
public class DialogueSequenceManager_FetchByPrefix : MonoBehaviour
{
    [Header("필수 참조")]
    [SerializeField] private BalloonAutoTyper_LocalizedFX typer;

    [Header("로컬라이즈 테이블/키 설정")]
    [SerializeField] private string tableName = "Prolog_Table";
    [SerializeField] private string keyPrefix = "Dialogue_";
    [SerializeField] private int startIndex = 1;
    [SerializeField] private int endIndex = 17;
    [SerializeField] private int zeroPad = 3;

    [Header("재생 옵션")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool firstLineWithEnterFX = true;

    [Header("입력 키")]
    [SerializeField] private KeyCode advanceKey = KeyCode.Space;
    [SerializeField] private int advanceMouseButton = 0;

    private int curIndex;
    private bool running;
    private StringTable tableObj;

    void Reset()
    {
        if (typer == null)
            typer = FindObjectOfType<BalloonAutoTyper_LocalizedFX>(true);
    }

    void Start()
    {
        if (typer == null)
        {
            Debug.LogError("[DialogueSequenceManager] Typer 참조 없음");
            enabled = false;
            return;
        }
        if (playOnStart)
            StartCoroutine(Co_Run());
    }

    void Update()
    {
        if (!running) return;

        bool pressed = Input.GetKeyDown(advanceKey) ||
                       Input.GetMouseButtonDown(advanceMouseButton);

        if (!pressed) return;

        if (typer.IsTypingNow)
        {
            // 아직 타이핑 중이면 즉시 완성
            typer.CompleteInstant();
        }
        else
        {
            // 다음 줄로 이동
            StartCoroutine(Co_Next());
        }
    }

    public void Begin()
    {
        if (!running)
            StartCoroutine(Co_Run());
    }

    private IEnumerator Co_Run()
    {
        running = true;

        yield return LocalizationSettings.InitializationOperation;

        AsyncOperationHandle<StringTable> handle =
            LocalizationSettings.StringDatabase.GetTableAsync(tableName);
        yield return handle;

        if (!handle.IsValid() ||
            handle.Status != AsyncOperationStatus.Succeeded ||
            handle.Result == null)
        {
            Debug.LogError($"[DialogueSequenceManager] 테이블 '{tableName}' 로드 실패");
            running = false;
            yield break;
        }

        tableObj = handle.Result;

        curIndex = startIndex - 1;
        yield return Co_Next(true);
    }

    private IEnumerator Co_Next(bool first = false)
    {
        curIndex++;
        if (curIndex > endIndex)
        {
            running = false;
            Debug.Log("[DialogueSequenceManager] 대사 종료");
            yield break;
        }

        // 예: Dialogue_001_
        string number = (zeroPad > 0) ? curIndex.ToString("D" + zeroPad) : curIndex.ToString();
        string basePrefix = keyPrefix + number + "_";

        // ✅ 여기서 테이블에서 실제 키 찾아옴 (접미사 포함)
        string resolvedKey = FindFirstKeyStartsWith(tableObj, basePrefix);
        if (string.IsNullOrEmpty(resolvedKey))
        {
            Debug.LogWarning($"[DialogueSequenceManager] '{basePrefix}*' 키 없음 → 스킵");
            yield return null;
            StartCoroutine(Co_Next());
            yield break;
        }

        // ✅ 키에서 Speaker ID 추출 후 리타겟
        string speakerId = ExtractSpeakerIdFromKey(resolvedKey);
        if (!string.IsNullOrEmpty(speakerId))
        {
            WorldBubbleAnchor.BroadcastRetargetSpeaker("default", speakerId, null);
        }

        bool useEnterFx = first ? firstLineWithEnterFX : false;
        typer.ShowLocalized(tableName, resolvedKey, useEnterFx);

        yield return null;
    }

    // ✅ 수정: ICollection<StringTableEntry> 기반 foreach, 캐스팅 필요 없음
    private static string FindFirstKeyStartsWith(StringTable table, string prefix)
    {
        if (table == null) return null;

        foreach (var entry in table.Values)
        {
            if (entry == null) continue;

            string k = entry.Key;
            if (!string.IsNullOrEmpty(k) && k.StartsWith(prefix))
                return k;
        }
        return null;
    }

    // "Dialogue_002_Player" → "Player"
    private static string ExtractSpeakerIdFromKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        int idx = key.LastIndexOf('_');
        if (idx < 0 || idx >= key.Length - 1)
            return null;

        return key.Substring(idx + 1).Trim();
    }
}
