// DialogueRunnerStringTables.cs
// Common runner for localized string tables.
// Updated: added "Re" choice flow.
//  - Re choice S keys: Dialogue_Choice{n}_S{k}_Re_{l:000}
//  - Re choice A keys: Dialogue_Choice{n}_A{k}_Re_{l:000}
// Behavior:
//  - If any Re S{k}_Re_001 exists for a choice n, that choice becomes Re-choice set.
//  - Picking a Re option shows all its Re answers, then shows remaining Re options only.
//  - After all Re options are exhausted, continues with normal Same lines and linear flow.

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

[DisallowMultipleComponent]
public class DialogueRunnerStringTables : MonoBehaviour
{
    // ===== External events =====
    public event Action<string> OnKeyShown;
    public event Action OnDialogueEnded;

    // ===== UI =====
    [Header("UI")]
    public TextMeshProUGUI speakerText;
    public TextMeshProUGUI bodyText;
    public TextMeshProUGUI promptText;
    public GameObject nextIndicator;
    public Button choiceButtonPrefab;

    [Tooltip("Panel that contains speakerText")]
    public GameObject namePanel;

    [Header("Choice container (runtime created if null)")]
    public Canvas targetCanvas;
    public Vector2 referenceResolution = new(1920, 1080);
    [Range(0, 1)] public float matchWidthOrHeight = 0.5f;
    public Vector2 choiceContainerSize = new(1100, 520);
    public Vector2 choiceContainerOffset = new(0, 120);
    public float choiceSpacing = 14f;
    public Vector4 choicePaddingTLBR = new(24, 24, 24, 24);
    public Color choiceContainerBg = new(0, 0, 0, 0);

    [Header("Default font/button sizes")]
    public float bodyFontSize = 52f;
    public float speakerFontSize = 46f;
    public float promptFontSize = 52f;
    public float choiceFontSize = 44f;
    public float choiceButtonHeight = 88f;
    public float choiceButtonMinWidth = 0f;

    [Header("Per-language font sizes")]
    public bool useLanguageFontSizes = true;
    [Serializable] public class LangFont { public string localeCode = "ko"; public float speakerSize = 46f; public float bodySize = 52f; }
    public List<LangFont> languageFonts = new()
    {
        new LangFont { localeCode = "ko", speakerSize = 46f, bodySize = 52f },
        new LangFont { localeCode = "en", speakerSize = 42f, bodySize = 48f },
        new LangFont { localeCode = "ja", speakerSize = 44f, bodySize = 50f },
    };

    [Header("Typing / input")]
    [Range(0f, 0.1f)] public float charDelay = 0.03f;
    public bool respectRichText = true;
    public KeyCode advanceKey = KeyCode.Space;
    [Range(0f, 0.5f)] public float advanceCooldownSec = 0.12f;
    public bool debugInputLog = false;

    [Header("Behaviour")]
    public bool deactivateOnEnd = true;
    public GameObject toggleDuringChoiceTarget;
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;
    public bool includeInactiveOnFind = true;

    [Header("Choice input control")]
    public bool blockSpaceSubmitOnChoices = true;
    public bool autoSelectFirstChoice = false;

    [Header("Player name token")]
    public string fallbackPlayerName = "Player";

    public enum SpeakerLoadMode { Auto, ForceOn, ForceOff }

    [Header("Speaker table mode")]
    public SpeakerLoadMode speakerMode = SpeakerLoadMode.Auto;

    // ===== Internal state =====
    private RectTransform _choiceRoot;
    private VerticalLayoutGroup _vlg;
    private ContentSizeFitter _csf;

    private StringTable _speakerTable;
    private StringTable _dialogueTable;

    private bool _speakerAvailable = false;

    private Coroutine _typingRoutine;
    private bool _isTyping = false;
    private bool _waitingChoice = false;
    private string _currentFullText = "";
    private WaitForSeconds _wait;

    private enum Mode { Linear, ChoiceSelect, AnswerRun, SameRun, ReChoiceSelect, ReAnswerRun, Done }
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

    private bool _localeHooked = false;

    [Header("Resume when re-enabled")]
    public bool retypeOnResume = true;
    private bool _resumePending = false;
    private bool _wasTypingWhenHidden = false;
    private string _lastKeyShown = "";

    // NPC talk-table mode: eventName is key, table is "{Npc}'s_Talk_Dialogue"
    private bool _useNpcTalkTableMode = false;

    // Re-choice runtime
    private bool _inReChoice = false;
    private readonly List<int> _reRemainingOptions = new();
    private int _reChoiceN = -1;

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

        HookLocaleChange();
        ApplyCurrentFontSizes();

        if (!string.IsNullOrEmpty(_pendingEventName))
        {
            var ev = _pendingEventName;
            _pendingEventName = null;
            StartCoroutine(Co_InitAndStart(ev));
        }
    }

    private void OnEnable()
    {
        HookLocaleChange();
        ApplyCurrentFontSizes();

        ResumeFromHiddenIfNeeded();

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

        _resumePending = (_mode != Mode.Done);
        _wasTypingWhenHidden = _isTyping;

        _isTyping = false;
        _inputUnlocked = false;
        _advanceCooldownLeft = 0f;
    }

    private void OnDestroy()
    {
        UnhookLocaleChange();
    }

    // ===== Start / table loading =====
    public void BeginWithEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            Debug.LogError("[DialogueRunnerStringTables] eventName is empty.");
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

    private IEnumerator LoadTable(string tableName, Action<StringTable> setter)
    {
        var op = LocalizationSettings.StringDatabase.GetTableAsync(tableName);
        yield return op;
        setter?.Invoke(op.Result);
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

        _speakerTable = null;
        _dialogueTable = null;
        _speakerAvailable = false;
        _useNpcTalkTableMode = false;

        _inReChoice = false;
        _reRemainingOptions.Clear();
        _reChoiceN = -1;

        var initOp = LocalizationSettings.InitializationOperation;
        if (!initOp.IsDone) yield return initOp;

        // 1) Default: "{EventName}_Dialogue"
        string defaultTableName = $"{_eventName}_Dialogue";
        yield return LoadTable(defaultTableName, t => _dialogueTable = t);

        if (_dialogueTable != null)
        {
            if (speakerMode == SpeakerLoadMode.ForceOff)
            {
                _speakerTable = null;
                _speakerAvailable = false;
            }
            else
            {
                string speakerTableName = $"{_eventName}_Speaker";
                yield return LoadTable(speakerTableName, t => _speakerTable = t);
                _speakerAvailable = (_speakerTable != null);

                if (speakerMode == SpeakerLoadMode.ForceOn && !_speakerAvailable)
                    Debug.LogWarning($"[DialogueRunnerStringTables] SpeakerLoadMode=ForceOn, but '{speakerTableName}' is missing.");
            }

            if (debugInputLog)
                Debug.Log($"[DialogueRunnerStringTables] Using default table '{defaultTableName}'.");

            OnDialogueBegin();
            Next();
            yield break;
        }

        // 2) NPC talk table: "{Npc}'s_Talk_Dialogue" with key = eventName
        string npcId = ExtractNpcIdFromEventName(_eventName);
        if (!string.IsNullOrEmpty(npcId))
        {
            string[] candidates =
            {
                $"{npcId}'s_Talk_Dialogue",
                $"{npcId}_Talk_Dialogue",
                $"{npcId}_Dialogue",
                $"{npcId}Talk_Dialogue"
            };

            StringTable found = null;
            string usedName = null;

            foreach (var name in candidates)
            {
                yield return LoadTable(name, t => found = t);
                if (found != null)
                {
                    usedName = name;
                    break;
                }
            }

            _dialogueTable = found;

            if (_dialogueTable != null)
            {
                _useNpcTalkTableMode = true;
                _speakerTable = null;
                _speakerAvailable = false;

                if (debugInputLog)
                    Debug.Log($"[DialogueRunnerStringTables] NPC talk-table mode: table='{usedName}', key='{_eventName}'.");

                if (!HasBody(_eventName))
                    Debug.LogWarning($"[DialogueRunnerStringTables] Table '{usedName}' does not contain key '{_eventName}'. Text will show the key itself.");

                OnDialogueBegin();
                ShowKey(_eventName);
                yield break;
            }

            Debug.LogError($"[DialogueRunnerStringTables] Could not find any NPC table for id='{npcId}'. Tried: {string.Join(", ", candidates)}");
            yield break;
        }

        Debug.LogError($"[DialogueRunnerStringTables] Missing dialogue table: '{defaultTableName}' and NPC id could not be extracted (eventName='{_eventName}').");
    }

    // ===== Update / input =====
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
            (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);

        if (!pressed) return;

        if (debugInputLog) Debug.Log("[DialogueRunnerStringTables] advance pressed.");

        if (_advanceCooldownLeft > 0f) return;
        _advanceCooldownLeft = advanceCooldownSec;

        if (_isTyping)
        {
            CompleteTypingInstant();
        }
        else
        {
            if (_useNpcTalkTableMode)
            {
                EndDialogue();
            }
            else
            {
                Next();
            }
        }
    }

    private static string KeyLinear(int i) => $"Dialogue_{i:000}";
    private static string KeyChoiceS(int n, int k, int l) => $"Dialogue_Choice{n}_S{k}_{l:000}";
    private static string KeyChoiceA(int n, int k, int l) => $"Dialogue_Choice{n}_A{k}_{l:000}";
    private static string KeyChoiceSame(int n, int l) => $"Dialogue_Choice{n}_Same_{l:000}";

    private static string KeyChoiceSRe(int n, int k, int l) => $"Dialogue_Choice{n}_S{k}_Re_{l:000}";
    private static string KeyChoiceARe(int n, int k, int l) => $"Dialogue_Choice{n}_A{k}_Re_{l:000}";

    private bool HasBody(string key)
    {
        if (_dialogueTable == null) return false;
        if (_dialogueTable.GetEntry(key) != null) return true;
        return FindEntryLoose(key) != null;
    }

    private StringTableEntry FindEntryLoose(string key)
    {
        if (_dialogueTable == null || string.IsNullOrEmpty(key)) return null;

        string target = key.Trim();

        foreach (var entry in _dialogueTable.Values)
        {
            if (entry == null) continue;
            string k = entry.Key;
            if (string.IsNullOrEmpty(k)) continue;

            if (string.Equals(k.Trim(), target, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    private string LBody(string key)
    {
        if (_dialogueTable == null) return key;

        var e = _dialogueTable.GetEntry(key);

        if (e == null)
        {
            e = FindEntryLoose(key);

            if (e == null)
            {
                string tableName = _dialogueTable.TableCollectionName;
                List<string> keys = new List<string>();
                foreach (var entry in _dialogueTable.Values)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.Key))
                        keys.Add(entry.Key);
                }

                Debug.LogWarning(
                    $"[DialogueRunnerStringTables] Table '{tableName}' has no entry for key '{key}'. Existing keys: {string.Join(", ", keys)}");

                return key;
            }
        }

        return ReplaceTokens(e.GetLocalizedString());
    }

    private string LSpeakerRaw(string key)
    {
        if (!_speakerAvailable || _speakerTable == null) return "";
        var e = _speakerTable.GetEntry(key);
        if (e == null) return "";
        return ReplaceTokens(e.GetLocalizedString());
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

            case Mode.ReAnswerRun:
                if (TryShowReAnswer(_choiceIndex, _answerPick)) return;

                _answerLine = 1;

                if (_reRemainingOptions.Count > 0)
                {
                    ShowReChoiceButtons(_choiceIndex, _reRemainingOptions);
                    return;
                }

                _inReChoice = false;
                _reChoiceN = -1;
                _mode = Mode.SameRun;
                _sameLine = 1;
                Next();
                return;

            case Mode.ChoiceSelect:
            case Mode.ReChoiceSelect:
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
        // Re-choice detection first
        var reOptions = new List<int>();
        for (int k = 1; k <= 9; k++)
            if (HasBody(KeyChoiceSRe(n, k, 1))) reOptions.Add(k);

        if (reOptions.Count > 0)
        {
            StartReChoice(n, reOptions);
            return true;
        }

        // Normal choices
        var options = new List<int>();
        for (int k = 1; k <= 9; k++)
            if (HasBody(KeyChoiceS(n, k, 1))) options.Add(k);

        if (options.Count == 0) return false;

        ShowChoiceButtons(n, options);
        return true;
    }

    private void StartReChoice(int n, List<int> reOptions)
    {
        _inReChoice = true;
        _reChoiceN = n;
        _reRemainingOptions.Clear();
        _reRemainingOptions.AddRange(reOptions);

        ShowReChoiceButtons(n, _reRemainingOptions);
    }

    private void ShowChoiceButtons(int n, List<int> sList)
    {
        _waitingChoice = true;
        _mode = Mode.ChoiceSelect;

        SetSpeakerUI(false);

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

    private void ShowReChoiceButtons(int n, List<int> reList)
    {
        _waitingChoice = true;
        _mode = Mode.ReChoiceSelect;

        SetSpeakerUI(false);

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

        foreach (var k in reList)
        {
            var btn = GetButton();
            btn.transform.SetParent(_choiceRoot, false);
            btn.interactable = true;

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            string optKey = KeyChoiceSRe(n, k, 1);
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
                _mode = Mode.ReAnswerRun;

                _reRemainingOptions.Remove(capturedK);

                ShowReAnswerFirstLine(n, _answerPick);
            });

            _activeButtons.Add(btn);
        }

        if (useCustomLayouts && TryApplyCustomLayout(reList.Count))
        {
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

    [Header("Custom Choice Layouts")]
    public bool useCustomLayouts = true;
    [Serializable] public class ChoiceLayout { public int optionCount = 2; public Vector2[] positions = Array.Empty<Vector2>(); public Vector2 buttonSize = new(900, 100); public bool centerRoot = true; }
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

    private readonly List<Button> _activeButtons = new();
    private readonly Stack<Button> _buttonPool = new();

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

    private void ShowReAnswerFirstLine(int n, int k)
    {
        string k1 = KeyChoiceARe(n, k, 1);
        if (!HasBody(k1))
        {
            if (_reRemainingOptions.Count > 0)
            {
                ShowReChoiceButtons(n, _reRemainingOptions);
            }
            else
            {
                _inReChoice = false;
                _reChoiceN = -1;
                _mode = Mode.SameRun;
                _sameLine = 1;
                Next();
            }
            return;
        }

        ShowKey(k1);
        _answerLine = 2;
    }

    private bool TryShowReAnswer(int n, int k)
    {
        string key = KeyChoiceARe(n, k, _answerLine);
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

    private static bool IsSystemSpeakerString(string sp)
    {
        if (string.IsNullOrWhiteSpace(sp)) return false;
        sp = sp.Trim();
        if (sp.Equals("{System}", StringComparison.OrdinalIgnoreCase)) return true;
        if (sp.Equals("System", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool ShouldShowSpeakerUI(string sp)
    {
        if (_mode == Mode.ChoiceSelect || _mode == Mode.ReChoiceSelect) return false;
        if (!_speakerAvailable) return false;
        if (string.IsNullOrWhiteSpace(sp)) return false;
        if (IsSystemSpeakerString(sp)) return false;
        return true;
    }

    private void SetSpeakerUI(bool visible, string text = "")
    {
        if (namePanel) namePanel.SetActive(visible);
        if (speakerText) speakerText.gameObject.SetActive(visible);
        if (speakerText)
        {
            speakerText.enableAutoSizing = false;
            speakerText.fontSize = GetSpeakerFontSize();
            speakerText.text = visible ? text : "";
        }
    }

    private void ShowKey(string key)
    {
        if (promptText) promptText.gameObject.SetActive(false);
        if (_choiceRoot) _choiceRoot.gameObject.SetActive(false);

        if (bodyText) bodyText.gameObject.SetActive(true);
        if (nextIndicator) nextIndicator.SetActive(false);

        string spRaw = _speakerAvailable ? LSpeakerRaw(key).Trim() : "";
        bool showSpeaker = ShouldShowSpeakerUI(spRaw);
        SetSpeakerUI(showSpeaker, spRaw);

        string full = LBody(key);

        _lastKeyShown = key;
        _currentFullText = full;

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
                bodyText.fontSize = GetBodyFontSize();
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
            bodyText.fontSize = GetBodyFontSize();
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
                    if (close == -1)
                    {
                        if (bodyText) bodyText.text += fullText.Substring(i);
                        break;
                    }
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

    // ===== Canvas / choice root =====
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

    // ===== Token replace =====
    private string ReplaceTokens(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        string name = fallbackPlayerName;

        var dm = DataManager.instance ?? FindFirstObjectByType<DataManager>(FindObjectsInactive.Include);
        if (dm != null && dm.nowPlayer != null && !string.IsNullOrEmpty(dm.nowPlayer.Name))
            name = dm.nowPlayer.Name.Trim();

        return s.Replace("{playerName}", name);
    }

    // ===== Begin / end callbacks =====
    private void OnDialogueBegin()
    {
        if (playerMove != null) playerMove.controlEnabled = false;
        SetSpeakerUI(false);
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
        if (promptText) { promptText.gameObject.SetActive(false); promptText.text = ""; }
        if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(true);
        ReleaseAllButtons();

        SetSpeakerUI(false);
        if (bodyText) { bodyText.text = ""; bodyText.ForceMeshUpdate(); }

        _mode = Mode.Done;

        _resumePending = false;
        _wasTypingWhenHidden = false;
        _lastKeyShown = "";

        _inReChoice = false;
        _reRemainingOptions.Clear();
        _reChoiceN = -1;

        OnDialogueEnd();

        if (deactivateOnEnd) gameObject.SetActive(false);

        _inputUnlocked = false;
        _advanceCooldownLeft = 0f;
    }

    // ===== Locale hooks / font size =====
    private void HookLocaleChange()
    {
        if (_localeHooked) return;
        try
        {
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            _localeHooked = true;
        }
        catch { }
    }

    private void UnhookLocaleChange()
    {
        if (!_localeHooked) return;
        try
        {
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
            _localeHooked = false;
        }
        catch { }
    }

    private void OnLocaleChanged(Locale loc)
    {
        ApplyCurrentFontSizes();
        if (_isTyping && bodyText != null)
        {
            bodyText.fontSize = GetBodyFontSize();
        }
        if (speakerText != null)
        {
            speakerText.fontSize = GetSpeakerFontSize();
        }
    }

    private void ApplyCurrentFontSizes()
    {
        if (speakerText)
        {
            speakerText.enableAutoSizing = false;
            speakerText.fontSize = GetSpeakerFontSize();
        }
        if (bodyText)
        {
            bodyText.enableAutoSizing = false;
            bodyText.fontSize = GetBodyFontSize();
        }
    }

    private float GetSpeakerFontSize()
    {
        if (!useLanguageFontSizes) return speakerFontSize;

        string code = GetCurrentLocaleCode();
        var f = languageFonts.Find(x => string.Equals(x.localeCode, code, StringComparison.OrdinalIgnoreCase));
        return f != null ? f.speakerSize : speakerFontSize;
    }

    private float GetBodyFontSize()
    {
        if (!useLanguageFontSizes) return bodyFontSize;

        string code = GetCurrentLocaleCode();
        var f = languageFonts.Find(x => string.Equals(x.localeCode, code, StringComparison.OrdinalIgnoreCase));
        return f != null ? f.bodySize : bodyFontSize;
    }

    private static string GetCurrentLocaleCode()
    {
        var loc = LocalizationSettings.SelectedLocale;
        return loc != null ? loc.Identifier.Code : "";
    }

    // ===== Resume from hidden =====
    private void ResumeFromHiddenIfNeeded()
    {
        if (!_resumePending) return;

        if (_mode == Mode.ChoiceSelect || _mode == Mode.ReChoiceSelect)
        {
            SetSpeakerUI(false);

            if (_choiceRoot) _choiceRoot.gameObject.SetActive(true);
            if (promptText) promptText.gameObject.SetActive(true);
            if (toggleDuringChoiceTarget) toggleDuringChoiceTarget.SetActive(false);
            _inputUnlocked = false;
            _resumePending = false;
            _wasTypingWhenHidden = false;
            return;
        }

        if (!string.IsNullOrEmpty(_lastKeyShown))
        {
            string spRaw = _speakerAvailable ? LSpeakerRaw(_lastKeyShown).Trim() : "";
            bool showSpeaker = ShouldShowSpeakerUI(spRaw);
            SetSpeakerUI(showSpeaker, spRaw);

            if (retypeOnResume)
            {
                if (_typingRoutine != null) StopCoroutine(_typingRoutine);
                _typingRoutine = StartCoroutine(TypeLine(_currentFullText ?? ""));
            }
            else
            {
                if (bodyText)
                {
                    bodyText.enableAutoSizing = false;
                    bodyText.fontSize = GetBodyFontSize();
                    bodyText.text = _currentFullText ?? "";
                }
                _isTyping = false;
                if (nextIndicator) nextIndicator.SetActive(true);
                _inputUnlocked = true;
            }
        }

        _resumePending = false;
        _wasTypingWhenHidden = false;
    }

    public void RefreshPlayerNameNow()
    {
        if (_waitingChoice || string.IsNullOrEmpty(_lastKeyShown)) return;

        string spRaw = _speakerAvailable ? LSpeakerRaw(_lastKeyShown).Trim() : "";
        string latestBody = LBody(_lastKeyShown);

        bool showSpeaker = ShouldShowSpeakerUI(spRaw);
        SetSpeakerUI(showSpeaker, spRaw);

        if (_typingRoutine != null && isActiveAndEnabled)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        _currentFullText = latestBody;

        if (retypeOnResume && isActiveAndEnabled)
        {
            _typingRoutine = StartCoroutine(TypeLine(_currentFullText));
            _isTyping = true;
            _inputUnlocked = false;
            if (nextIndicator) nextIndicator.SetActive(false);
        }
        else
        {
            if (bodyText)
            {
                bodyText.enableAutoSizing = false;
                bodyText.fontSize = GetBodyFontSize();
                bodyText.text = _currentFullText;
            }
            _isTyping = false;
            _inputUnlocked = true;
            if (nextIndicator) nextIndicator.SetActive(true);
        }
    }

    // ===== NPC id extraction ("Sol_Talk_08" -> "Sol") =====
    private string ExtractNpcIdFromEventName(string eventName)
    {
        if (string.IsNullOrEmpty(eventName)) return null;
        int idx = eventName.IndexOf("_Talk_", StringComparison.Ordinal);
        if (idx <= 0) return null;
        return eventName.Substring(0, idx);
    }
}
