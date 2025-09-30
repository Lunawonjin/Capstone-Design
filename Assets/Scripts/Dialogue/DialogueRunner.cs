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
    // ===== UI =====
    [Header("UI")]
    public TextMeshProUGUI speakerText;  // ← {Event}_Speaker 테이블 동일 Key
    public TextMeshProUGUI bodyText;     // ← {Event}_Dialogue 테이블 동일 Key
    public TextMeshProUGUI promptText;
    public GameObject nextIndicator;
    public Button choiceButtonPrefab;

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

    // ===== 커스텀 버튼 배치 =====
    [Header("Custom Choice Layouts")]
    [Tooltip("켜면 아래 Layouts 설정에 맞춰 버튼을 직접 좌표 배치(수동)합니다. 끄면 VerticalLayoutGroup(자동 정렬) 사용")]
    public bool useCustomLayouts = true;

    [Serializable]
    public class ChoiceLayout
    {
        [Tooltip("이 레이아웃이 적용될 선택지 개수(예: 2,3,4)")]
        public int optionCount = 2;

        [Tooltip("버튼 개수만큼 좌표(anchoredPosition)")]
        public Vector2[] positions = Array.Empty<Vector2>();

        [Tooltip("버튼 sizeDelta")]
        public Vector2 buttonSize = new Vector2(900, 100);

        [Tooltip("선택지 루트를 choiceContainerOffset 기준 중앙에 둘지")]
        public bool centerRoot = true;
    }

    [Tooltip("개수별(2/3/4…) 프리셋")]
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

    // ===== 타이핑/입력 =====
    [Header("타이핑/입력")]
    [Range(0f, 0.1f)] public float charDelay = 0.03f;
    public bool respectRichText = true;
    public KeyCode advanceKey = KeyCode.Space;

    // ===== 동작 =====
    [Header("동작")]
    public bool deactivateOnEnd = true;
    public GameObject toggleDuringChoiceTarget;
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;
    public bool includeInactiveOnFind = true;

    [Header("선택지 입력 제어")]
    [Tooltip("선택지 표시 중 스페이스/엔터로 버튼이 눌리는 것을 차단")]
    public bool blockSpaceSubmitOnChoices = true;
    [Tooltip("선택지 표시 시 첫 번째 버튼을 자동 포커스(키보드/패드 네비용). 켜면 스페이스가 다시 통과될 수 있습니다.")]
    public bool autoSelectFirstChoice = false;

    // ===== 이름 치환 =====
    [Header("Player Name Token")]
    [Tooltip("플레이어 이름 치환 실패 시 쓸 기본 이름")]
    public string fallbackPlayerName = "Player";

    // ===== 내부 상태 =====
    private RectTransform _choiceRoot;
    private VerticalLayoutGroup _vlg;
    private ContentSizeFitter _csf;

    private readonly List<Button> _activeButtons = new();
    private readonly Stack<Button> _buttonPool = new();

    private StringTable _speakerTable;   // "{EventName}_Speaker" 또는 "{EventName}_Speaker table"
    private StringTable _dialogueTable;  // "{EventName}_Dialogue" 또는 "{EventName}_Dialogue table"

    private Coroutine _typingRoutine;
    private bool _isTyping = false;
    private bool _waitingChoice = false;
    private string _currentFullText = "";
    private WaitForSeconds _wait;

    private enum Mode { Linear, ChoiceSelect, AnswerRun, SameRun, Done }
    private Mode _mode = Mode.Linear;

    private string _eventName;           // ex) "Sol_First_Meet"
    private int _linearIndex = 1;        // Dialogue_001부터
    private int _choiceIndex = 1;        // Dialogue_Choice1_...부터
    private int _answerPick = -1;        // 선택된 S{k}의 k
    private int _answerLine = 1;
    private int _sameLine = 1;

    private string _pendingEventName;
    private string _cachedPlayerName;    // {playerName} 치환 캐시

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

        _cachedPlayerName = ResolvePlayerName();

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
    }

    // ===== 외부 진입 =====
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

    // ===== 테이블 로드 (primary 실패 시 fallback 시도) =====
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

        _cachedPlayerName = ResolvePlayerName(); // 매번 최신화

        var initOp = LocalizationSettings.InitializationOperation;
        if (!initOp.IsDone) yield return initOp;

        // "{Event}_Speaker" / "{Event}_Dialogue" → 실패 시 "{Event}_Speaker table" / "{Event}_Dialogue table"
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

    // ===== 입력 =====
    private void Update()
    {
        // 선택지 모드에서는 스페이스로 진행/완료를 막는다(버튼만으로 선택)
        if (_waitingChoice) return;

        if (Input.GetKeyDown(advanceKey))
        {
            if (_isTyping) CompleteTypingInstant();
            else Next();
        }
    }

    // ===== 키 규칙 =====
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

    // ===== 메인 진행 =====
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

    // ===== Linear =====
    private bool TryShowLinear()
    {
        string key = KeyLinear(_linearIndex);
        if (!HasBody(key)) return false;

        ShowKey(key);
        _linearIndex++;
        return true;
    }

    // ===== Choice 시작 =====
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
            promptText.text = ""; // 필요 시 프롬프트 키 지정 가능
            promptText.gameObject.SetActive(true);
        }

        EnsureChoiceRoot();
        _choiceRoot.gameObject.SetActive(true);
        ReleaseAllButtons();

        // 버튼 생성
        foreach (var k in sList)
        {
            var btn = GetButton();
            btn.transform.SetParent(_choiceRoot, false);
            btn.interactable = true; // 안전망

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            string optKey = KeyChoiceS(n, k, 1); // 첫 줄을 버튼 라벨로 사용
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

            // 기본 크기(자동 레이아웃 사용 시)
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

        // 커스텀 좌표 적용 또는 기본 VerticalLayout 사용
        if (useCustomLayouts && TryApplyCustomLayout(sList.Count))
        {
            // 수동 배치 성공
        }
        else
        {
            EnableDefaultLayout(true);
        }

        // ★ 스페이스/엔터 Submit 차단(선택된 UI가 없으면 Submit이 안 넘어감)
        if (blockSpaceSubmitOnChoices)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
        else if (autoSelectFirstChoice && EventSystem.current != null && _activeButtons.Count > 0)
        {
            // 선택지를 키보드/패드로 조작하고 싶다면 켜세요(스페이스가 버튼을 누를 수 있음)
            EventSystem.current.SetSelectedGameObject(_activeButtons[0].gameObject);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_choiceRoot);
    }

    // ===== 커스텀 레이아웃 =====
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

            // 앵커/피벗 중앙
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // 지정 크기/좌표
            rt.sizeDelta = layout.buttonSize;
            rt.anchoredPosition = layout.positions[i];

            // 레이아웃 엘리먼트 값 간섭 제거
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
        btn.interactable = true;              // 재사용 시 복구
        btn.onClick.RemoveAllListeners();     // 이중구독 방지
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

    // ===== Answer =====
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

    // ===== Same =====
    private bool TryShowSame(int n)
    {
        string key = KeyChoiceSame(n, _sameLine);
        if (!HasBody(key)) { _sameLine = 1; return false; }

        ShowKey(key);
        _sameLine++;
        return true;
    }

    // ===== 한 줄 표시(+ {System} 처리, {playerName} 치환) =====
    private void ShowKey(string key)
    {
        if (promptText) promptText.gameObject.SetActive(false);
        if (_choiceRoot) _choiceRoot.gameObject.SetActive(false);

        if (bodyText) bodyText.gameObject.SetActive(true);
        if (nextIndicator) nextIndicator.SetActive(false);

        // 스피커
        string sp = LSpeakerRaw(key).Trim();
        bool isSystem = string.Equals(sp, "{System}", StringComparison.OrdinalIgnoreCase);

        if (speakerText != null)
        {
            if (isSystem)
            {
                // 시스템 메시지: 화자 영역 숨김
                speakerText.text = "";
                speakerText.gameObject.SetActive(false);
            }
            else
            {
                // 일반 화자: 표시 + 이름 출력
                if (!speakerText.gameObject.activeSelf) speakerText.gameObject.SetActive(true);
                speakerText.enableAutoSizing = false;
                speakerText.fontSize = speakerFontSize;
                speakerText.text = sp; // ReplaceTokens 적용됨
            }
        }

        // 본문
        string full = LBody(key); // ReplaceTokens 적용됨
        if (_typingRoutine != null) StopCoroutine(_typingRoutine);
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
                    if (bodyText) bodyText.text += fullText.Substring(i, close - i + 1);
                    i = close + 1;
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

    // ===== Canvas/Choice Root =====
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
        bg.raycastTarget = false; // 배경이 클릭을 먹지 않도록

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

    // ===== 플레이어 이름 치환 =====
    private string ReplaceTokens(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // DataManager.instance에서 바로 가져옴
        string name = fallbackPlayerName;
        var dm = DataManager.instance ?? FindFirstObjectByType<DataManager>(FindObjectsInactive.Include);
        if (dm != null && dm.nowPlayer != null && !string.IsNullOrEmpty(dm.nowPlayer.Name))
            name = dm.nowPlayer.Name.Trim();

        return s.Replace("{playerName}", name);
    }

    // 캐시 갱신용
    private string ResolvePlayerName()
    {
        var dm = DataManager.instance ?? FindFirstObjectByType<DataManager>(FindObjectsInactive.Include);
        if (dm != null && dm.nowPlayer != null && !string.IsNullOrEmpty(dm.nowPlayer.Name))
            return dm.nowPlayer.Name.Trim();
        return fallbackPlayerName;
    }

    // ===== 시작/종료 =====
    private void OnDialogueBegin()
    {
        if (playerMove != null) playerMove.controlEnabled = false;
    }

    private void OnDialogueEnd()
    {
        if (playerMove != null) playerMove.controlEnabled = true;
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
    }
}
