using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

[DisallowMultipleComponent]
public class DialogueRunnerStringTables : MonoBehaviour
{
    // ===== 외부 알림 =====
    public event Action<string> OnKeyShown;
    public event Action OnDialogueEnded;

    // ===== UI =====
    [Header("UI")]
    public TextMeshProUGUI speakerText;  // {Event}_Speaker
    public TextMeshProUGUI bodyText;     // {Event}_Dialogue
    public TextMeshProUGUI promptText;
    public GameObject nextIndicator;
    public Button choiceButtonPrefab;

    [Tooltip("화자 이름 텍스트를 감싸는 UI 패널(GameObject)")]
    public GameObject namePanel; // <-- [추가됨] NamePanel을 연결할 필드

    [Header("선택지 컨테이너(없으면 자동 생성)")]
    public Canvas targetCanvas;
    public Vector2 referenceResolution = new(1920, 1080);
    [Range(0, 1)] public float matchWidthOrHeight = 0.5f;
    public Vector2 choiceContainerSize = new(1100, 520);
    public Vector2 choiceContainerOffset = new(0, 120);
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

    [Header("Custom Choice Layouts")]
    public bool useCustomLayouts = true;

    [Serializable]
    public class ChoiceLayout
    {
        public int optionCount = 2;
        public Vector2[] positions = Array.Empty<Vector2>();
        public Vector2 buttonSize = new(900, 100);
        public bool centerRoot = true;
    }

    public List<ChoiceLayout> layouts = new()
    {
        new ChoiceLayout {
            optionCount = 2,
            positions = new [] { new Vector2(-250, 100), new Vector2( 250, 100) },
            buttonSize = new Vector2(900, 100),
            centerRoot = true
        },
        new ChoiceLayout {
            optionCount = 3,
            positions = new [] { new Vector2(-360, 120), new Vector2(0, 120), new Vector2(360, 120) },
            buttonSize = new Vector2(820, 100),
            centerRoot = true
        },
        new ChoiceLayout {
            optionCount = 4,
            positions = new [] {
                new Vector2(-300, 160), new Vector2(300, 160),
                new Vector2(-300,  40), new Vector2(300,  40),
            },
            buttonSize = new Vector2(740, 92),
            centerRoot = true
        },
    };

    [Header("타이핑/입력")]
    [Range(0f, 0.1f)] public float charDelay = 0.03f;
    public bool respectRichText = true;
    public KeyCode advanceKey = KeyCode.Space;

    [Tooltip("패널/대사가 나타난 직후 광클로 스킵되는 것 방지(초)")]
    [Range(0f, 0.5f)] public float advanceCooldownSec = 0.12f;

    [Tooltip("입력 디버그 로그(키/마우스 감지 시 콘솔 출력)")]
    public bool debugInputLog = false;

    [Header("동작")]
    public bool deactivateOnEnd = true;
    public GameObject toggleDuringChoiceTarget;
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;
    public bool includeInactiveOnFind = true;

    [Header("선택지 입력 제어")]
    public bool blockSpaceSubmitOnChoices = true;
    public bool autoSelectFirstChoice = false;

    [Header("Player Name Token")]
    public string fallbackPlayerName = "Player";

    // ===== 내부 상태 =====
    private RectTransform _choiceRoot;
    private VerticalLayoutGroup _vlg;
    private ContentSizeFitter _csf;

    private readonly List<Button> _activeButtons = new();
    private readonly Stack<Button> _buttonPool = new();

    private StringTable _speakerTable;
    private StringTable _dialogueTable;

    private Coroutine _typingRoutine;
    private bool _isTyping = false;
    private bool _waitingChoice = false;
    private string _currentFullText = "";
    private WaitForSeconds _wait;

    private enum Mode { Linear, ChoiceSelect, AnswerRun, SameRun, Done }
    private Mode _mode = Mode.Linear;

    private string _eventName;
    private int _linearIndex = 1;
    private int _choiceIndex = 1;
    private int _answerPick = -1;
    private int _answerLine = 1;
    private int _sameLine = 1;

    private string _pendingEventName;

    private bool _inputUnlocked = false;
    private float _advanceCooldownLeft = 0f;

    private void Awake()
    {
        if (speakerText) { speakerText.text = ""; speakerText.raycastTarget = false; }
        if (bodyText) { bodyText.text = ""; bodyText.raycastTarget = false; }
        if (promptText) { promptText.text = ""; promptText.raycastTarget = false; }
        if (nextIndicator) nextIndicator.SetActive(false);
        _wait = new WaitForSeconds(charDelay);
    }

    private void Start()
    {
        EnsureCanvas();
        EnsureChoiceRoot();

        if (autoFindPlayerMove && playerMove == null)
        {
            playerMove = includeInactiveOnFind
                ? FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include)
                : FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Exclude);
        }

        if (!string.IsNullOrEmpty(_pendingEventName))
        {
            var ev = _pendingEventName;
            _pendingEventName = null;
            StartCoroutine(Co_InitAndStart(ev));
        }
    }

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(_pendingEventName))
        {
            var ev = _pendingEventName;
            _pendingEventName = null;
            StartCoroutine(Co_InitAndStart(ev));
        }
    }

    private void OnValidate()
    {
        if (charDelay < 0f) charDelay = 0f;
        _wait = new WaitForSeconds(Mathf.Max(0f, charDelay));
        if (advanceCooldownSec < 0f) advanceCooldownSec = 0f;
    }

    private void OnDisable()
    {
        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }
        _isTyping = false;
        _inputUnlocked = false;
        _advanceCooldownLeft = 0f;
    }

    public void BeginWithEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            Debug.LogError("[DialogueRunnerStringTables] eventName이 비었습니다.");
            return;
        }

        if (!gameObject.activeSelf)
        {
            _pendingEventName = eventName.Trim();
            gameObject.SetActive(true);
            return;
        }

        StartCoroutine(Co_InitAndStart(eventName.Trim()));
    }

    private IEnumerator LoadTableMultiTry(string primary, string fallback, Action<StringTable> setter)
    {
        var op1 = LocalizationSettings.StringDatabase.GetTableAsync(primary);
        yield return op1;
        var table = op1.Result;

        if (table == null && !string.IsNullOrEmpty(fallback))
        {
            var op2 = LocalizationSettings.StringDatabase.GetTableAsync(fallback);
            yield return op2;
            table = op2.Result;
        }

        setter?.Invoke(table);
    }

    private IEnumerator Co_InitAndStart(string eventName)
    {
        _eventName = eventName;
        _linearIndex = 1;
        _choiceIndex = 1;
        _answerPick = -1;
        _answerLine = 1;
        _sameLine = 1;
        _mode = Mode.Linear;

        _inputUnlocked = false;
        _advanceCooldownLeft = 0f;

        var initOp = LocalizationSettings.InitializationOperation;
        if (!initOp.IsDone) yield return initOp;

        StringTable sp = null, bo = null;
        yield return LoadTableMultiTry($"{_eventName}_Speaker", $"{_eventName}_Speaker table", t => sp = t);
        yield return LoadTableMultiTry($"{_eventName}_Dialogue", $"{_eventName}_Dialogue table", t => bo = t);

        _speakerTable = sp;
        _dialogueTable = bo;

        if (_speakerTable == null || _dialogueTable == null)
        {
            Debug.LogError($"[DialogueRunnerStringTables] 컬렉션을 찾지 못했습니다.\n" +
                           $"  Speaker 후보:  {_eventName}_Speaker  / {_eventName}_Speaker table\n" +
                           $"  Dialogue 후보: {_eventName}_Dialogue / {_eventName}_Dialogue table");
            yield break;
        }

        OnDialogueBegin();
        Next();
    }

    private void Update()
    {
        if (_advanceCooldownLeft > 0f)
            _advanceCooldownLeft -= Time.unscaledDeltaTime;

        if (_waitingChoice) return;

        if (!_inputUnlocked) return;

        bool pressed =
            Input.GetKeyDown(advanceKey) ||
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetMouseButtonDown(0) ||
            Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;

        if (!pressed) return;

        if (debugInputLog) Debug.Log("[DialogueRunner] advance pressed");

        if (_advanceCooldownLeft > 0f) return;
        _advanceCooldownLeft = advanceCooldownSec;

        if (_isTyping) CompleteTypingInstant();
        else Next();
    }

    private static string KeyLinear(int i) => $"Dialogue_{i:000}";
    private static string KeyChoiceS(int n, int k, int l) => $"Dialogue_Choice{n}_S{k}_{l:000}";
    private static string KeyChoiceA(int n, int k, int l) => $"Dialogue_Choice{n}_A{k}_{l:000}";
    private static string KeyChoiceSame(int n, int l) => $"Dialogue_Choice{n}_Same_{l:000}";

    private bool HasBody(string key)
    {
        if (_dialogueTable == null) return false;
        return _dialogueTable.GetEntry(key) != null;
    }
    private string LBody(string key)
    {
        if (_dialogueTable == null) return key;
        var e = _dialogueTable.GetEntry(key);
        return e == null ? key : ReplaceTokens(e.GetLocalizedString());
    }
    private string LSpeakerRaw(string key)
    {
        if (_speakerTable == null) return "";
        var e = _speakerTable.GetEntry(key);
        return e == null ? "" : ReplaceTokens(e.GetLocalizedString());
    }

    private void Next()
    {
        switch (_mode)
        {
            case Mode.Linear:
                if (TryShowLinear()) return;
                if (TryStartChoice(_choiceIndex)) return;
                EndDialogue();
                return;

            case Mode.AnswerRun:
                if (TryShowAnswer(_choiceIndex, _answerPick)) return;
                _mode = Mode.SameRun;
                Next();
                return;

            case Mode.SameRun:
                if (TryShowSame(_choiceIndex)) return;
                _choiceIndex++;
                _mode = Mode.Linear;
                Next();
                return;

            case Mode.ChoiceSelect:
            case Mode.Done:
                return;
        }
    }

    private bool TryShowLinear()
    {
        string key = KeyLinear(_linearIndex);
        if (!HasBody(key)) return false;
        ShowKey(key);
        _linearIndex++;
        return true;
    }

    private bool TryStartChoice(int n)
    {
        var options = new List<int>();
        for (int k = 1; k <= 9; k++)
            if (HasBody(KeyChoiceS(n, k, 1))) options.Add(k);

        if (options.Count == 0) return false;

        ShowChoiceButtons(n, options);
        return true;
    }

    private void ShowChoiceButtons(int n, List<int> sList)
    {
        _waitingChoice = true;
        _mode = Mode.ChoiceSelect;

        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(false);
        if (bodyText) bodyText.gameObject.SetActive(false);
        if (nextIndicator) nextIndicator.SetActive(false);
        if (promptText)
        {
            promptText.enableAutoSizing = false;
            promptText.fontSize = promptFontSize;
            promptText.text = "";
            promptText.gameObject.SetActive(true);
        }

        EnsureChoiceRoot();
        _choiceRoot.gameObject.SetActive(true);
        ReleaseAllButtons();

        foreach (var k in sList)
        {
            var btn = GetButton();
            btn.transform.SetParent(_choiceRoot, false);
            btn.interactable = true;

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            string optKey = KeyChoiceS(n, k, 1);
            string text = LBody(optKey);

            if (label)
            {
                label.richText = true;
                label.enableAutoSizing = false;
                label.fontSize = choiceFontSize;
                label.text = text;
                label.raycastTarget = false;
            }
            else
            {
                var legacy = btn.GetComponentInChildren<Text>(true);
                if (legacy) { legacy.text = text; legacy.raycastTarget = false; }
            }

            var le = btn.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>();
            float h = choiceButtonHeight > 0f ? choiceButtonHeight : Mathf.Ceil(choiceFontSize * 2f);
            le.preferredHeight = h; le.minHeight = h;
            if (choiceButtonMinWidth > 0f) le.minWidth = choiceButtonMinWidth;

            int capturedK = k;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                SetButtonsInteractable(false);
                _choiceRoot.gameObject.SetActive(false);
                if (promptText) promptText.gameObject.SetActive(false);
                if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);

                _answerPick = capturedK;
                _waitingChoice = false;
                _mode = Mode.AnswerRun;

                ShowAnswerFirstLine(n, _answerPick);
            });

            _activeButtons.Add(btn);
        }

        if (useCustomLayouts && TryApplyCustomLayout(sList.Count))
        {
            // custom layout 적용됨
        }
        else
        {
            EnableDefaultLayout(true);
        }

        if (blockSpaceSubmitOnChoices)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
        else if (autoSelectFirstChoice && EventSystem.current != null && _activeButtons.Count > 0)
        {
            EventSystem.current.SetSelectedGameObject(_activeButtons[0].gameObject);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_choiceRoot);
    }

    private bool TryApplyCustomLayout(int count)
    {
        var layout = layouts.Find(l => l.optionCount == count && l.positions != null && l.positions.Length == count);
        if (layout == null) return false;

        EnableDefaultLayout(false);

        _choiceRoot.sizeDelta = choiceContainerSize;
        _choiceRoot.anchoredPosition = choiceContainerOffset;

        for (int i = 0; i < _activeButtons.Count; i++)
        {
            var btn = _activeButtons[i];
            if (!btn) continue;

            var rt = btn.GetComponent<RectTransform>() ?? btn.gameObject.AddComponent<RectTransform>();

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            rt.sizeDelta = layout.buttonSize;
            rt.anchoredPosition = layout.positions[i];

            var le = btn.GetComponent<LayoutElement>();
            if (le)
            {
                le.minWidth = 0f;
                le.minHeight = 0f;
                le.preferredWidth = -1f;
                le.preferredHeight = -1f;
                le.flexibleWidth = 0f;
                le.flexibleHeight = 0f;
            }
        }
        return true;
    }

    private void EnableDefaultLayout(bool enabled)
    {
        if (_vlg) _vlg.enabled = enabled;
        if (_csf) _csf.enabled = enabled;
    }

    private Button GetButton()
    {
        Button btn = _buttonPool.Count > 0 ? _buttonPool.Pop() : Instantiate(choiceButtonPrefab);
        btn.gameObject.SetActive(true);
        btn.interactable = true;
        btn.onClick.RemoveAllListeners();
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

    private void ShowAnswerFirstLine(int n, int k)
    {
        string k1 = KeyChoiceA(n, k, 1);
        if (!HasBody(k1)) { _mode = Mode.SameRun; Next(); return; }

        ShowKey(k1);
        _answerLine = 2;
    }

    private bool TryShowAnswer(int n, int k)
    {
        string key = KeyChoiceA(n, k, _answerLine);
        if (!HasBody(key)) { _answerLine = 1; return false; }

        ShowKey(key);
        _answerLine++;
        return true;
    }

    private bool TryShowSame(int n)
    {
        string key = KeyChoiceSame(n, _sameLine);
        if (!HasBody(key)) { _sameLine = 1; return false; }

        ShowKey(key);
        _sameLine++;
        return true;
    }

    // [수정됨] NamePanel 제어 로직 변경
    private void ShowKey(string key)
    {
        if (promptText) promptText.gameObject.SetActive(false);
        if (_choiceRoot) _choiceRoot.gameObject.SetActive(false);

        if (bodyText) bodyText.gameObject.SetActive(true);
        if (nextIndicator) nextIndicator.SetActive(false);

        string sp = LSpeakerRaw(key).Trim();
        bool isSystem = string.Equals(sp, "{System}", StringComparison.OrdinalIgnoreCase);

        // NamePanel이 연결되어 있으면 패널 전체를 제어
        if (namePanel != null)
        {
            namePanel.SetActive(!isSystem);
        }
        // NamePanel이 없으면 기존 방식대로 Text만 제어
        else if (speakerText != null)
        {
            speakerText.gameObject.SetActive(!isSystem);
        }

        // speakerText는 항상 업데이트
        if (speakerText != null)
        {
            speakerText.enableAutoSizing = false;
            speakerText.fontSize = speakerFontSize;
            speakerText.text = isSystem ? "" : sp;
        }

        string full = LBody(key);

        OnKeyShown?.Invoke(key);

        if (_typingRoutine != null)
        {
            if (isActiveAndEnabled) StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        _inputUnlocked = false;
        _advanceCooldownLeft = advanceCooldownSec;

        if (!isActiveAndEnabled)
        {
            if (bodyText)
            {
                bodyText.enableAutoSizing = false;
                bodyText.fontSize = bodyFontSize;
                bodyText.text = full;
            }
            _isTyping = false;
            if (nextIndicator) nextIndicator.SetActive(true);
            _inputUnlocked = true;
            return;
        }

        _typingRoutine = StartCoroutine(TypeLine(full));
    }

    private IEnumerator TypeLine(string fullText)
    {
        _isTyping = true;
        _currentFullText = fullText;

        if (bodyText)
        {
            bodyText.enableAutoSizing = false;
            bodyText.fontSize = bodyFontSize;
            bodyText.text = "";
        }

        bool printedOne = false;

        if (!respectRichText)
        {
            for (int i = 0; i < fullText.Length; i++)
            {
                if (bodyText) bodyText.text = fullText.Substring(0, i + 1);
                if (!printedOne) { printedOne = true; yield return null; _inputUnlocked = true; }
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
                    if (bodyText) bodyText.text += fullText.Substring(i, close - i + 1);
                    i = close + 1;
                    if (!printedOne && bodyText && bodyText.text.Length > 0) { printedOne = true; yield return null; _inputUnlocked = true; }
                }
                else
                {
                    if (bodyText) bodyText.text += c;
                    i++;
                    if (!printedOne) { printedOne = true; yield return null; _inputUnlocked = true; }
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
        if (_typingRoutine != null && isActiveAndEnabled)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }
        if (bodyText) bodyText.text = _currentFullText;
        _isTyping = false;
        if (nextIndicator) nextIndicator.SetActive(true);

        _inputUnlocked = true;
        _advanceCooldownLeft = advanceCooldownSec;
    }

    private void EnsureCanvas()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
                targetCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
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

        var bg = go.GetComponent<Image>();
        bg.color = choiceContainerBg;
        bg.raycastTarget = false;

        _vlg = go.AddComponent<VerticalLayoutGroup>();
        _vlg.childAlignment = TextAnchor.UpperLeft;
        _vlg.spacing = choiceSpacing;
        _vlg.childControlWidth = true;
        _vlg.childControlHeight = true;
        _vlg.childForceExpandWidth = true;
        _vlg.childForceExpandHeight = false;
        _vlg.padding = new RectOffset(
            Mathf.RoundToInt(choicePaddingTLBR.y),
            Mathf.RoundToInt(choicePaddingTLBR.w),
            Mathf.RoundToInt(choicePaddingTLBR.x),
            Mathf.RoundToInt(choicePaddingTLBR.z)
        );

        _csf = go.AddComponent<ContentSizeFitter>();
        _csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        _csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _choiceRoot.gameObject.SetActive(false);
    }

    private string ReplaceTokens(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        string name = fallbackPlayerName;

        var dm = DataManager.instance ?? FindFirstObjectByType<DataManager>(FindObjectsInactive.Include);
        if (dm != null && dm.nowPlayer != null && !string.IsNullOrEmpty(dm.nowPlayer.Name))
            name = dm.nowPlayer.Name.Trim();

        return s.Replace("{playerName}", name);
    }

    private void OnDialogueBegin()
    {
        if (playerMove != null) playerMove.controlEnabled = false;
    }

    private void OnDialogueEnd()
    {
        if (playerMove != null) playerMove.controlEnabled = true;
        OnDialogueEnded?.Invoke();
    }

    private void EndDialogue()
    {
        if (nextIndicator) nextIndicator.SetActive(false);
        if (_choiceRoot) _choiceRoot.gameObject.SetActive(false);
        if (promptText) promptText.gameObject.SetActive(false);
        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);
        ReleaseAllButtons();

        _mode = Mode.Done;
        OnDialogueEnd();
        if (deactivateOnEnd) gameObject.SetActive(false);

        _inputUnlocked = false;
        _advanceCooldownLeft = 0f;
    }
}