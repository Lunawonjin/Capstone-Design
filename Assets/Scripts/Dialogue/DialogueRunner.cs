// DialogueRunnerCSVLocalized.cs
// Unity 6 (LTS) / TextMeshPro / Localization 사용
// - JSON 제거, 단일 CSV(TextAsset)로 노드/스텝을 정의
// - CSV에는 "문자열 그 자체"가 아니라 String Table의 Key만 적는다
// - 현재 선택된 Locale(에디터 드롭다운/런타임 변경)에 맞춰 텍스트를 가져와
//   한 글자씩 타이핑 출력한다(스페이스: 스킵/다음 진행)
// - 선택지 프롬프트/옵션도 모두 Localization 키로 지정
// - 선택지 동안 특정 오브젝트를 끄고(speaker는 유지하고 싶을 때) 종료 시 복귀
// - PlayerMove 유무에 따라 입력 잠금/복귀 지원(선택)
// - EventSystem/Canvas가 없으면 자동 생성

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

[DisallowMultipleComponent]
public class DialogueRunnerCSVLocalized : MonoBehaviour
{
    // ====== UI 레퍼런스 ======
    [Header("UI")]
    [Tooltip("화자 이름 출력용 TMP")]
    public TextMeshProUGUI speakerText;
    [Tooltip("본문 대사 출력용 TMP")]
    public TextMeshProUGUI bodyText;
    [Tooltip("선택지 프롬프트 출력용 TMP")]
    public TextMeshProUGUI promptText;
    [Tooltip("다음 진행 표시 아이콘(점멸 화살표 등)")]
    public GameObject nextIndicator;

    [Header("선택지 버튼 프리팹 (UGUI Button + 자식에 TMP_Text 필수)")]
    public Button choiceButtonPrefab;

    [Header("선택지 컨테이너(없으면 자동 생성)")]
    public Canvas targetCanvas;
    public Vector2 referenceResolution = new(1920, 1080);
    [Range(0, 1)] public float matchWidthOrHeight = 0.5f;

    public Vector2 choiceContainerSize = new(1100f, 520f);
    public Vector2 choiceContainerOffset = new(0f, 120f);
    public float choiceSpacing = 14f;
    public Vector4 choicePaddingTLBR = new(24, 24, 24, 24);
    public Color choiceContainerBg = new(0, 0, 0, 0);

    [Header("폰트/버튼 크기")]
    public float bodyFontSize = 52f;
    public float speakerFontSize = 46f;
    public float promptFontSize = 52f;
    public float choiceFontSize = 44f;
    public float choiceButtonHeight = 88f;
    public float choiceButtonMinWidth = 0f;

    // ====== 타이핑/입력 ======
    [Header("타이핑/입력")]
    [Range(0f, 0.1f)] public float charDelay = 0.03f;
    public bool respectRichText = true;
    public KeyCode advanceKey = KeyCode.Space;

    // ====== 동작 옵션 ======
    [Header("동작 옵션")]
    [Tooltip("대화 종료 시 이 오브젝트를 비활성화")]
    public bool deactivateOnEnd = true;

    [Tooltip("선택지 표시 동안 비활성화할 오브젝트(본문 말풍선 그룹 등). speakerText는 끄지 않음.")]
    public GameObject toggleDuringChoiceTarget;

    [Header("플레이어 입력 잠금(선택)")]
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;
    public bool includeInactiveOnFind = true;

    [Header("대화 종료 시 비활성화할 오브젝트들(선택)")]
    public GameObject[] endDeactivateTargets = new GameObject[2];

    // ====== 데이터 소스 ======
    [Header("CSV / String Table")]
    [Tooltip("UTF-8 CSV(TextAsset). 헤더 고정: nodeId,stepIndex,kind,speakerKey,bodyKey,promptKey,optionKeys,optionNextNodes")]
    public TextAsset csvTextAsset;

    [Tooltip("String Table Collection 이름(예: \"Dialogue Table\")")]
    public string stringTableCollectionName = "Dialogue Table";

    [Tooltip("시작 노드 ID(비우면 CSV의 첫 번째 노드가 시작점)")]
    public string startNodeId = "";

    // ====== 내부 모델 ======
    [Serializable]
    public class Step
    {
        public string kind;          // "line" 또는 "choice"
        public string speakerKey;    // 화자 이름 키
        public string bodyKey;       // 본문 대사 키
        public string promptKey;     // 선택지 프롬프트 키
        public string[] optionKeys;  // 선택지 텍스트 키 배열
        public string[] optionNext;  // 각 선택지 클릭 시 이동할 노드 id 배열
    }

    // nodeId -> 순서 리스트
    private readonly Dictionary<string, List<Step>> _nodes = new();

    // 진행 상태
    private string _currentNodeId;
    private int _stepIndex = -1;

    // 타이핑 상태
    private Coroutine _typingRoutine;
    private bool _isTyping = false;
    private bool _waitingChoice = false;
    private string _currentFullText = "";
    private WaitForSeconds _wait;

    // 선택지 UI
    private RectTransform _choiceRoot;
    private readonly List<Button> _activeButtons = new();
    private readonly Stack<Button> _buttonPool = new();

    // String Table 핸들
    private StringTable _stringTable;

    // ====== Unity 수명주기 ======
    private void Awake()
    {
        if (speakerText) speakerText.text = "";
        if (bodyText) bodyText.text = "";
        if (promptText) { promptText.text = ""; promptText.gameObject.SetActive(false); }
        if (nextIndicator) nextIndicator.SetActive(false);
        _wait = new WaitForSeconds(charDelay);
    }

    private void Start()
    {
        EnsureCanvas();
        EnsureChoiceRoot();

        if (bodyText) { bodyText.enableAutoSizing = false; bodyText.fontSize = bodyFontSize; }
        if (speakerText) { speakerText.enableAutoSizing = false; speakerText.fontSize = speakerFontSize; }

        // PlayerMove 자동 탐색
        if (autoFindPlayerMove && playerMove == null)
        {
            playerMove = includeInactiveOnFind
                ? FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include)
                : FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Exclude);
        }

        if (csvTextAsset == null)
        {
            Debug.LogError("[DialogueRunnerCSVLocalized] CSV가 비어 있습니다.");
            return;
        }

        // CSV 파싱
        try
        {
            ParseCsv(csvTextAsset.text, out string firstNodeId);
            if (string.IsNullOrEmpty(startNodeId))
                _currentNodeId = firstNodeId;
            else
                _currentNodeId = startNodeId;
        }
        catch (Exception e)
        {
            Debug.LogError("[DialogueRunnerCSVLocalized] CSV 파싱 실패: " + e.Message);
            return;
        }

        // Localization 초기화 및 String Table 로드 후 시작
        StartCoroutine(Co_InitAndStart());
    }

    private IEnumerator Co_InitAndStart()
    {
        // Localization Settings 준비 대기
        var initOp = LocalizationSettings.InitializationOperation;
        if (!initOp.IsDone) yield return initOp;

        // 지정된 String Table 로드
        var tableOp = LocalizationSettings.StringDatabase.GetTableAsync(stringTableCollectionName);
        yield return tableOp;
        _stringTable = tableOp.Result;

        if (_stringTable == null)
        {
            Debug.LogError("[DialogueRunnerCSVLocalized] String Table을 찾을 수 없습니다: " + stringTableCollectionName);
            yield break;
        }

        // 시작 훅
        OnDialogueBegin();

        _stepIndex = -1;
        NextStepOrLine();
    }

    private void Update()
    {
        if (_waitingChoice) return;

        if (Input.GetKeyDown(advanceKey))
        {
            if (_isTyping) CompleteTypingInstant();
            else NextStepOrLine();
        }
    }

    private void OnValidate()
    {
        if (charDelay < 0f) charDelay = 0f;
        _wait = new WaitForSeconds(Mathf.Max(0f, charDelay));
    }

    // ====== 캔버스/선택지 컨테이너 보장 ======
    private void EnsureCanvas()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
            {
                targetCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            }
            if (targetCanvas == null)
            {
                var go = new GameObject("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                targetCanvas = go.GetComponent<Canvas>();
                targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = referenceResolution;
                scaler.matchWidthOrHeight = matchWidthOrHeight;

                if (FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
                    new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }
    }

    private void EnsureChoiceRoot()
    {
        if (_choiceRoot != null) return;

        var go = new GameObject("ChoicesRuntime", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _choiceRoot = go.GetComponent<RectTransform>();
        _choiceRoot.SetParent(targetCanvas.transform, false);
        _choiceRoot.anchorMin = new Vector2(0.5f, 0f);
        _choiceRoot.anchorMax = new Vector2(0.5f, 0f);
        _choiceRoot.pivot = new Vector2(0.5f, 0f);
        _choiceRoot.sizeDelta = choiceContainerSize;
        _choiceRoot.anchoredPosition = choiceContainerOffset;

        var bg = go.GetComponent<Image>(); bg.color = choiceContainerBg;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = choiceSpacing;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(
            Mathf.RoundToInt(choicePaddingTLBR.y),
            Mathf.RoundToInt(choicePaddingTLBR.w),
            Mathf.RoundToInt(choicePaddingTLBR.x),
            Mathf.RoundToInt(choicePaddingTLBR.z)
        );

        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _choiceRoot.gameObject.SetActive(false);
    }

    // ====== CSV 파서 ======
    // 헤더 고정:
    // nodeId,stepIndex,kind,speakerKey,bodyKey,promptKey,optionKeys,optionNextNodes
    // optionKeys / optionNextNodes는 세미콜론(;)로 구분
    private void ParseCsv(string csv, out string firstNodeId)
    {
        firstNodeId = "";

        using (var sr = new StringReader(csv))
        {
            string? header = sr.ReadLine();
            if (header == null) throw new Exception("빈 CSV");

            // 헤더가 정규 포맷인지 판정
            bool headerLooksStructured = header.Contains("nodeId") && header.Contains("stepIndex") && header.Contains("kind");

            var tempLines = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line)) tempLines.Add(line);
            }

            if (tempLines.Count == 0) throw new Exception("데이터 행이 없습니다.");

            if (headerLooksStructured)
            {
                // ===== A포맷: 엄격 8컬럼 =====
                firstNodeId = ParseStructuredCsvRows(tempLines);
            }
            else
            {
                // ===== B포맷: 간단/Localization 내보내기 유사 =====
                // 기대: 첫 컬럼이 String Table의 키(예: Dialog_0001). 나머지 열은 무시.
                // 모든 행을 nodeId="Start" 아래의 line으로 직렬 배치.
                firstNodeId = BuildSimpleSequenceFromKeys(tempLines);
            }
        }
    }

    // A포맷: 정규 8컬럼 라인들을 파싱
    private string ParseStructuredCsvRows(List<string> rows)
    {
        string firstNodeId = "";
        bool firstAssigned = false;

        for (int r = 0; r < rows.Count; r++)
        {
            var cols = SplitCsvLine(rows[r]);
            if (cols.Count < 8)
                throw new Exception("컬럼 수가 부족합니다. 라인: " + rows[r]);

            string nodeId = cols[0].Trim();
            string stepIndexStr = cols[1].Trim();
            string kind = cols[2].Trim();
            string speakerKey = cols[3].Trim();
            string bodyKey = cols[4].Trim();
            string promptKey = cols[5].Trim();
            string optionKeysRaw = cols[6].Trim();
            string optionNextRaw = cols[7].Trim();

            if (!firstAssigned)
            {
                firstNodeId = nodeId;
                firstAssigned = true;
            }

            if (!_nodes.TryGetValue(nodeId, out var list))
            {
                list = new List<Step>(8);
                _nodes[nodeId] = list;
            }

            var step = new Step
            {
                kind = kind,
                speakerKey = speakerKey,
                bodyKey = bodyKey,
                promptKey = promptKey,
                optionKeys = string.IsNullOrEmpty(optionKeysRaw) ? Array.Empty<string>() : optionKeysRaw.Split(';'),
                optionNext = string.IsNullOrEmpty(optionNextRaw) ? Array.Empty<string>() : optionNextRaw.Split(';')
            };

            if (!int.TryParse(stepIndexStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int idx))
                idx = list.Count;

            while (list.Count <= idx) list.Add(null);
            list[idx] = step;
        }

        // null 제거
        foreach (var kv in _nodes)
        {
            var compact = new List<Step>(kv.Value.Count);
            for (int i = 0; i < kv.Value.Count; i++)
                if (kv.Value[i] != null) compact.Add(kv.Value[i]);
            _nodes[kv.Key] = compact;
        }

        return firstNodeId;
    }

    // B포맷: "키,..." 형태의 라인들을 단일 노드 Start 아래에 순서대로 line으로 생성
    private string BuildSimpleSequenceFromKeys(List<string> rows)
    {
        const string nodeId = "Start";
        var list = new List<Step>(rows.Count);
        _nodes[nodeId] = list;

        for (int r = 0; r < rows.Count; r++)
        {
            var cols = SplitCsvLine(rows[r]);
            if (cols.Count < 1) continue;

            // 첫 컬럼을 String Table 키로 사용
            string key = cols[0].Trim();
            if (string.IsNullOrEmpty(key)) continue;

            list.Add(new Step
            {
                kind = "line",
                speakerKey = "",   // 간단 모드에서는 화자 생략
                bodyKey = key,     // 본문 키로 바로 사용
                promptKey = "",
                optionKeys = Array.Empty<string>(),
                optionNext = Array.Empty<string>()
            });
        }

        return nodeId;
    }

    // 따옴표/콤마 처리 CSV 분리기(기존 것 그대로 사용)
    private List<string> SplitCsvLine(string line)
    {
        var result = new List<string>(8);
        bool inQuotes = false;
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"') { i++; }
                else { inQuotes = !inQuotes; }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(Unquote(line.Substring(start, i - start)));
                start = i + 1;
            }
        }
        result.Add(Unquote(line.Substring(start)));
        return result;
    }

    private string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '\"' && s[s.Length - 1] == '\"')
            s = s.Substring(1, s.Length - 2).Replace("\"\"", "\"");
        return s;
    }

    // ====== 진행 ======
    private void NextStepOrLine()
    {
        if (_waitingChoice) return;
        if (string.IsNullOrEmpty(_currentNodeId) || !_nodes.TryGetValue(_currentNodeId, out var steps) || steps == null)
        {
            EndDialogue();
            return;
        }

        _stepIndex++;
        if (_stepIndex >= steps.Count)
        {
            EndDialogue();
            return;
        }

        var step = steps[_stepIndex];
        if (step == null)
        {
            NextStepOrLine();
            return;
        }

        switch (step.kind)
        {
            case "line":
                ShowLine(step.speakerKey, step.bodyKey);
                return;
            case "choice":
                ShowChoice(step.promptKey, step.optionKeys, step.optionNext);
                return;
            default:
                Debug.LogWarning("[DialogueRunnerCSVLocalized] 알 수 없는 kind: " + step.kind);
                NextStepOrLine();
                return;
        }
    }

    // ====== 로컬라이즈 유틸 ======
    private string L(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (_stringTable == null) return key; // 안전장치
        var entry = _stringTable.GetEntry(key);
        if (entry == null) return key;        // 키 누락 시 키 그대로
        return entry.GetLocalizedString();
    }

    // ====== 한 줄 표시 ======
    private void ShowLine(string speakerKey, string bodyKey)
    {
        if (promptText) promptText.gameObject.SetActive(false);
        if (_choiceRoot) _choiceRoot.gameObject.SetActive(false);

        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);
        if (bodyText) bodyText.gameObject.SetActive(true);
        if (nextIndicator) nextIndicator.SetActive(false);

        if (speakerText)
        {
            speakerText.enableAutoSizing = false;
            speakerText.fontSize = speakerFontSize;
            speakerText.text = L(speakerKey);
        }

        _currentFullText = L(bodyKey);

        if (_typingRoutine != null) StopCoroutine(_typingRoutine);
        _typingRoutine = StartCoroutine(TypeLine(_currentFullText));
    }

    private IEnumerator TypeLine(string fullText)
    {
        _isTyping = true;
        if (bodyText)
        {
            bodyText.enableAutoSizing = false;
            bodyText.fontSize = bodyFontSize;
            bodyText.text = "";
        }

        if (!respectRichText)
        {
            for (int i = 0; i < fullText.Length; i++)
            {
                if (bodyText) bodyText.text = fullText.Substring(0, i + 1);
                yield return _wait;
            }
        }
        else
        {
            int i = 0;
            while (i < fullText.Length)
            {
                char c = fullText[i];
                if (c == '<')
                {
                    int close = fullText.IndexOf('>', i);
                    if (close == -1)
                    {
                        if (bodyText) bodyText.text += fullText.Substring(i);
                        break;
                    }
                    else
                    {
                        if (bodyText) bodyText.text += fullText.Substring(i, close - i + 1);
                        i = close + 1;
                    }
                }
                else
                {
                    if (bodyText) bodyText.text += c;
                    i++;
                    yield return _wait;
                }
            }
        }

        _isTyping = false;
        if (nextIndicator) nextIndicator.SetActive(true);
        _typingRoutine = null;
    }

    private void CompleteTypingInstant()
    {
        if (!_isTyping) return;
        if (_typingRoutine != null) { StopCoroutine(_typingRoutine); _typingRoutine = null; }
        if (bodyText) bodyText.text = _currentFullText;
        _isTyping = false;
        if (nextIndicator) nextIndicator.SetActive(true);
    }

    // ====== 선택지 표시 ======
    private void ShowChoice(string promptKey, string[] optionKeys, string[] optionNext)
    {
        if (nextIndicator) nextIndicator.SetActive(false);

        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(false);
        if (bodyText) bodyText.gameObject.SetActive(false);

        if (promptText)
        {
            promptText.enableAutoSizing = false;
            promptText.fontSize = promptFontSize;
            promptText.text = L(promptKey);
            promptText.gameObject.SetActive(true);
        }

        EnsureChoiceRoot();
        _choiceRoot.gameObject.SetActive(true);

        ReleaseAllButtons();
        _waitingChoice = true;

        int count = optionKeys != null ? optionKeys.Length : 0;
        for (int i = 0; i < count; i++)
        {
            string optKey = optionKeys[i];
            string optNext = (optionNext != null && i < optionNext.Length) ? optionNext[i] : "";

            var btn = GetButton();
            btn.transform.SetParent(_choiceRoot, false);

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label)
            {
                label.richText = true;
                label.enableAutoSizing = false;
                label.fontSize = choiceFontSize;
                label.text = L(optKey);
            }
            else
            {
                var legacy = btn.GetComponentInChildren<Text>(true);
                if (legacy) legacy.text = L(optKey);
            }

            var le = btn.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>();
            float h = choiceButtonHeight > 0f ? choiceButtonHeight : Mathf.Ceil(choiceFontSize * 2.0f);
            le.preferredHeight = h; le.minHeight = h;
            if (choiceButtonMinWidth > 0f) le.minWidth = choiceButtonMinWidth;

            btn.interactable = true;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                SetButtonsInteractable(false);
                _waitingChoice = false;

                _choiceRoot.gameObject.SetActive(false);
                if (promptText) promptText.gameObject.SetActive(false);
                if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);

                if (!string.IsNullOrEmpty(optNext) && _nodes.ContainsKey(optNext))
                {
                    _currentNodeId = optNext;
                    _stepIndex = -1;
                    NextStepOrLine();
                }
                else
                {
                    NextStepOrLine();
                }
            });

            _activeButtons.Add(btn);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_choiceRoot);
    }

    // ====== 버튼 풀 ======
    private Button GetButton()
    {
        Button btn = _buttonPool.Count > 0 ? _buttonPool.Pop() : Instantiate(choiceButtonPrefab);
        btn.gameObject.SetActive(true);
        return btn;
    }

    private void ReleaseAllButtons()
    {
        for (int i = 0; i < _activeButtons.Count; i++)
        {
            var b = _activeButtons[i];
            if (b)
            {
                b.onClick.RemoveAllListeners();
                b.gameObject.SetActive(false);
                b.transform.SetParent(transform, false);
                _buttonPool.Push(b);
            }
        }
        _activeButtons.Clear();
    }

    private void SetButtonsInteractable(bool value)
    {
        for (int i = 0; i < _activeButtons.Count; i++)
            if (_activeButtons[i]) _activeButtons[i].interactable = value;
    }

    // ====== 시작/종료 훅 ======
    private void OnDialogueBegin()
    {
        if (playerMove != null) playerMove.controlEnabled = false;
    }

    private void OnDialogueEnd()
    {
        if (endDeactivateTargets != null)
        {
            for (int i = 0; i < endDeactivateTargets.Length; i++)
            {
                var go = endDeactivateTargets[i];
                if (go != null) go.SetActive(false);
            }
        }
        if (playerMove != null) playerMove.controlEnabled = true;
    }

    private void EndDialogue()
    {
        if (nextIndicator) nextIndicator.SetActive(false);
        if (_choiceRoot) _choiceRoot.gameObject.SetActive(false);
        if (promptText) promptText.gameObject.SetActive(false);
        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);
        ReleaseAllButtons();

        OnDialogueEnd();

        if (deactivateOnEnd) gameObject.SetActive(false);
    }
}
