// DialogueRunnerAdvanced.cs
// Unity 6 (LTS) / TextMeshPro 분기형 대화
// - PromptText는 별도 폰트 크기(promptFontSize)로 출력
// - 선택지 동안 speakerText는 끄지 않고, 인스펙터에서 지정한 오브젝트(toggleDuringChoiceTarget)만 비/활성
// - 내부 선택지 컨테이너 + 버튼 풀링(최소 할당), FindFirstObjectByType 사용

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class DialogueRunnerAdvanced : MonoBehaviour
{
    // ===== JSON Model =====
    [Serializable] public class NumericVar { public string name; public float value; }
    [Serializable] public class FlagVar { public string name; public bool value; }

    [Serializable]
    public class Condition
    {
        public string type;  // "num" or "flag"
        public string var;
        public string op;    // num: ==, !=, >, <, >=, <= / flag: ==, !=
        public string value;
    }

    [Serializable]
    public class SetVar
    {
        public string type;  // "num" or "flag"
        public string var;
        public string op;    // num: "=", "+=", "-=", "floor0" / flag: "setTrue","setFalse","toggle","="
        public float num;
        public bool flag;
    }

    [Serializable]
    public class ChoiceOption
    {
        [TextArea] public string text;
        public Condition[] ifAll;
        public SetVar[] set;
        public string nextNodeId;
    }

    [Serializable]
    public class Step
    {
        public string kind; // "line", "choice", "set", "goto"

        // line
        public string speaker;
        [TextArea] public string text;
        public Condition[] ifAll;

        // choice
        public string prompt;
        public ChoiceOption[] options;

        // set
        public SetVar[] set;

        // goto
        public string gotoNode;
    }

    [Serializable] public class Node { public string id; public Step[] steps; }

    [Serializable]
    public class DialogueScript
    {
        public string id;
        public string startNodeId;
        public NumericVar[] numericVars;
        public FlagVar[] flagVars;
        public Node[] nodes;
    }

    // ===== Inspector: UI =====
    [Header("UI References")]
    public TextMeshProUGUI speakerText;    // 화자 이름 (선택지 중에도 끄지 않음)
    public TextMeshProUGUI bodyText;       // 본문
    public TextMeshProUGUI promptText;     // 선택 프롬프트 전용
    public GameObject nextIndicator;

    [Header("Choice Button Prefab")]
    [Tooltip("UGUI Button + 자식에 TMP_Text 필수")]
    public Button choiceButtonPrefab;

    // ===== Data / Player =====
    [Header("Data Source")]
    public TextAsset jsonTextAsset;
    public string playerName = "플레이어";

    // ===== Typing / Input =====
    [Header("Typing & Input")]
    [Range(0f, 0.1f)] public float charDelay = 0.03f;
    public bool respectRichText = true;
    public KeyCode advanceKey = KeyCode.Space;

    // ===== Behavior =====
    [Header("Behavior")]
    public bool deactivateOnEnd = true;

    // ===== 화면 요소 토글 대상 =====
    [Header("선택지 동안 비활성화할 오브젝트")]
    [Tooltip("선택지가 열릴 때 비활성화하고, 선택 후 다시 활성화할 오브젝트(예: 본문 말풍선 그룹). speakerText는 끄지 않습니다.")]
    public GameObject toggleDuringChoiceTarget;

    // ===== Canvas / Choices 컨테이너 =====
    [Header("Canvas / Runtime Container")]
    public Canvas targetCanvas;                  // 비면 자동
    public Vector2 referenceResolution = new(1920, 1080);
    [Range(0, 1)] public float matchWidthOrHeight = 0.5f;

    [Tooltip("선택지 컨테이너 크기/위치(하단 중앙)")]
    public Vector2 choiceContainerSize = new(1100f, 520f);
    public Vector2 choiceContainerOffset = new(0f, 120f);
    public float choiceSpacing = 14f;
    public Vector4 choicePaddingTLBR = new(24, 24, 24, 24); // Top, Left, Bottom, Right
    public Color choiceContainerBg = new(0, 0, 0, 0);

    // ===== 폰트/버튼 크기 =====
    [Header("Font & Button Size")]
    public float bodyFontSize = 52f;
    public float speakerFontSize = 46f;
    public float promptFontSize = 52f;    // ← NEW: PromptText 전용 크기
    public float choiceFontSize = 44f;
    public float choiceButtonHeight = 88f;
    public float choiceButtonMinWidth = 0f;

    // ===== 내부 상태 =====
    private DialogueScript _script;
    private readonly Dictionary<string, float> _num = new();
    private readonly Dictionary<string, bool> _flag = new();
    private readonly Dictionary<string, Node> _nodeById = new();

    private Node _node;
    private int _stepIndex = -1;

    private Coroutine _typingRoutine;
    private bool _isTyping = false;
    private bool _waitingChoice = false;

    private string _currentFullText = "";
    private WaitForSeconds _wait;

    private RectTransform _choiceRoot; // 내부 컨테이너
    private readonly List<Button> _activeButtons = new();
    private readonly Stack<Button> _buttonPool = new();

    // ===== Unity =====
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

        if (jsonTextAsset == null) { Debug.LogError("[DialogueRunnerAdvanced] jsonTextAsset is null."); return; }
        LoadFromText(jsonTextAsset.text);
        StartDialogue();
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

    // ===== 보장 =====
    private void EnsureCanvas()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null) targetCanvas = FindFirstObjectByType<Canvas>();
            if (targetCanvas == null)
            {
                var go = new GameObject("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                targetCanvas = go.GetComponent<Canvas>();
                targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = referenceResolution;
                scaler.matchWidthOrHeight = matchWidthOrHeight;

                if (FindFirstObjectByType<EventSystem>() == null)
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
            Mathf.RoundToInt(choicePaddingTLBR.y), // Left
            Mathf.RoundToInt(choicePaddingTLBR.w), // Right
            Mathf.RoundToInt(choicePaddingTLBR.x), // Top
            Mathf.RoundToInt(choicePaddingTLBR.z)  // Bottom
        );

        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _choiceRoot.gameObject.SetActive(false);
    }

    // ===== 로드/시작 =====
    public void LoadFromText(string json)
    {
        _script = null; _node = null; _nodeById.Clear(); _num.Clear(); _flag.Clear();
        try { _script = JsonUtility.FromJson<DialogueScript>(json); }
        catch (Exception e) { Debug.LogError("[DialogueRunnerAdvanced] JSON parse failed: " + e.Message); return; }

        if (_script.numericVars != null) foreach (var v in _script.numericVars) _num[v.name] = v.value;
        if (_script.flagVars != null) foreach (var f in _script.flagVars) _flag[f.name] = f.value;
        if (_script.nodes != null) foreach (var n in _script.nodes) _nodeById[n.id] = n;

        if (!_nodeById.TryGetValue(_script.startNodeId, out _node))
        { Debug.LogError("[DialogueRunnerAdvanced] startNodeId not found: " + _script.startNodeId); return; }

        _stepIndex = -1;
    }

    public void StartDialogue()
    {
        if (_script == null || _node == null)
        { Debug.LogError("[DialogueRunnerAdvanced] Missing script or start node."); return; }
        _stepIndex = -1;
        NextStepOrLine();
    }

    // ===== 진행 =====
    private void NextStepOrLine()
    {
        if (_waitingChoice) return;

        while (true)
        {
            _stepIndex++;

            if (_node == null || _node.steps == null || _stepIndex >= _node.steps.Length)
            { EndDialogue(); return; }

            var step = _node.steps[_stepIndex];
            if (!CheckConditions(step.ifAll)) continue;

            switch (step.kind)
            {
                case "line": ShowLine(step.speaker, step.text); return;
                case "choice": ShowChoice(step.prompt, step.options); return;
                case "set": ApplySet(step.set); continue;
                case "goto":
                    if (!string.IsNullOrEmpty(step.gotoNode) && _nodeById.TryGetValue(step.gotoNode, out var to))
                    { _node = to; _stepIndex = -1; continue; }
                    Debug.LogWarning("[DialogueRunnerAdvanced] goto target not found: " + step.gotoNode);
                    continue;
                default:
                    Debug.LogWarning("[DialogueRunnerAdvanced] Unknown kind: " + step.kind);
                    continue;
            }
        }
    }

    // ===== Line =====
    private void ShowLine(string speaker, string text)
    {
        // 선택지 UI 닫기
        if (promptText) promptText.gameObject.SetActive(false);
        if (_choiceRoot) _choiceRoot.gameObject.SetActive(false);

        // 선택지 동안 꺼뒀던 오브젝트 원복
        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);
        if (bodyText) bodyText.gameObject.SetActive(true);
        if (nextIndicator) nextIndicator.SetActive(false);

        if (speakerText)
        {
            speakerText.enableAutoSizing = false;
            speakerText.fontSize = speakerFontSize;
            speakerText.text = speaker ?? "";
            // 요구: speakerText는 끄지 않음 (항상 활성 상태 유지)
        }

        _currentFullText = text == null ? "" : (playerName.Length > 0 ? text.Replace("<이름>", playerName) : text);

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
                    if (close == -1) { if (bodyText) bodyText.text += fullText.Substring(i); break; }
                    else { if (bodyText) bodyText.text += fullText.Substring(i, close - i + 1); i = close + 1; }
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

    // ===== Choice (PromptText 사용, speakerText는 유지, 지정 오브젝트만 토글) =====
    private void ShowChoice(string prompt, ChoiceOption[] options)
    {
        if (nextIndicator) nextIndicator.SetActive(false);

        // 선택지 동안 끌 대상 오브젝트
        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(false);
        if (bodyText) bodyText.gameObject.SetActive(false);
            // speakerText는 끄지 않음

            // PromptText 표시
            if (promptText)
        {
            promptText.enableAutoSizing = false;
            promptText.fontSize = promptFontSize; // ← 별도 크기
            promptText.text = (prompt ?? "").Replace("<이름>", playerName);
            promptText.gameObject.SetActive(true);
        }

        EnsureChoiceRoot();
        _choiceRoot.gameObject.SetActive(true);

        // 기존 버튼 반환
        ReleaseAllButtons();

        // 노출 옵션 수집
        var visible = ListPool<ChoiceOption>.Get();
        if (options != null)
            for (int i = 0; i < options.Length; i++)
                if (CheckConditions(options[i].ifAll)) visible.Add(options[i]);

        if (visible.Count == 0)
        {
            _waitingChoice = false;
            _choiceRoot.gameObject.SetActive(false);
            if (promptText) promptText.gameObject.SetActive(false);
            if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);
            NextStepOrLine();
            ListPool<ChoiceOption>.Release(visible);
            return;
        }

        _waitingChoice = true;

        for (int i = 0; i < visible.Count; i++)
        {
            var opt = visible[i];
            var btn = GetButton();
            btn.transform.SetParent(_choiceRoot, false);

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label)
            {
                label.richText = true;
                label.enableAutoSizing = false;
                label.fontSize = choiceFontSize;
                label.text = opt.text.Replace("<이름>", playerName);
            }
            else
            {
                var legacy = btn.GetComponentInChildren<Text>(true);
                if (legacy) legacy.text = opt.text.Replace("<이름>", playerName);
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
                ApplySet(opt.set);

                _waitingChoice = false;

                _choiceRoot.gameObject.SetActive(false);
                if (promptText) promptText.gameObject.SetActive(false);
                if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);

                if (!string.IsNullOrEmpty(opt.nextNodeId) && _nodeById.TryGetValue(opt.nextNodeId, out var to))
                { _node = to; _stepIndex = -1; NextStepOrLine(); }
                else
                { NextStepOrLine(); }
            });

            _activeButtons.Add(btn);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_choiceRoot);
        ListPool<ChoiceOption>.Release(visible);
    }

    // ===== 버튼 풀 =====
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

    // ===== 조건/변수 =====
    private bool CheckConditions(Condition[] conds)
    {
        if (conds == null || conds.Length == 0) return true;

        for (int i = 0; i < conds.Length; i++)
        {
            var c = conds[i];
            if (c == null) continue;

            if (c.type == "num")
            {
                if (!_num.TryGetValue(c.var, out float cur)) cur = 0f;
                if (!float.TryParse(c.value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float rhs)) rhs = 0f;

                switch (c.op)
                {
                    case "==": if (!Mathf.Approximately(cur, rhs)) return false; break;
                    case "!=": if (Mathf.Approximately(cur, rhs)) return false; break;
                    case ">": if (!(cur > rhs)) return false; break;
                    case "<": if (!(cur < rhs)) return false; break;
                    case ">=": if (!(cur >= rhs)) return false; break;
                    case "<=": if (!(cur <= rhs)) return false; break;
                    default: return false;
                }
            }
            else if (c.type == "flag")
            {
                _flag.TryGetValue(c.var, out bool cur);
                if (!bool.TryParse(c.value, out bool rhs)) rhs = false;

                switch (c.op)
                {
                    case "==": if (cur != rhs) return false; break;
                    case "!=": if (cur == rhs) return false; break;
                    default: return false;
                }
            }
            else return false;
        }
        return true;
    }

    private void ApplySet(SetVar[] sets)
    {
        if (sets == null) return;

        for (int i = 0; i < sets.Length; i++)
        {
            var s = sets[i];
            if (s == null) continue;

            if (s.type == "num")
            {
                if (!_num.TryGetValue(s.var, out float cur)) cur = 0f;
                switch (s.op)
                {
                    case "=": cur = s.num; break;
                    case "+=": cur += s.num; break;
                    case "-=": cur -= s.num; break;
                    case "floor0": if (cur < 0f) cur = 0f; break;
                    default: Debug.LogWarning("[SetVar] Unknown num op: " + s.op); break;
                }
                _num[s.var] = cur;
            }
            else if (s.type == "flag")
            {
                _flag.TryGetValue(s.var, out bool cur);
                switch (s.op)
                {
                    case "=": cur = s.flag; break;
                    case "setTrue": cur = true; break;
                    case "setFalse": cur = false; break;
                    case "toggle": cur = !cur; break;
                    default: Debug.LogWarning("[SetVar] Unknown flag op: " + s.op); break;
                }
                _flag[s.var] = cur;
            }
        }
    }

    // ===== End =====
    private void EndDialogue()
    {
        if (nextIndicator) nextIndicator.SetActive(false);
        if (_choiceRoot) _choiceRoot.gameObject.SetActive(false);
        if (promptText) promptText.gameObject.SetActive(false);
        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);
        ReleaseAllButtons();

        if (deactivateOnEnd) gameObject.SetActive(false);
    }

    // ===== 유틸 =====
    private void OnValidate()
    {
        if (charDelay < 0f) charDelay = 0f;
        _wait = new WaitForSeconds(Mathf.Max(0f, charDelay));
    }

    // 가벼운 List Pool
    static class ListPool<T>
    {
        static readonly Stack<List<T>> _pool = new();
        public static List<T> Get() => _pool.Count > 0 ? _pool.Pop() : new List<T>(8);
        public static void Release(List<T> list) { list.Clear(); _pool.Push(list); }
    }
}
