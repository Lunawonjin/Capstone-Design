using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

[DisallowMultipleComponent]
public class MessageSystem : MonoBehaviour
{
    // ───────────────── 로깅 ─────────────────
    public enum LogVerbosity { Off, Errors, Warnings, Info, Verbose }

    [Header("로깅")]
    public LogVerbosity logLevel = LogVerbosity.Info;
    public string logPrefix = "[MessageSystem] ";

    int _updateTick;
    bool LogEnabled(LogVerbosity level) => logLevel >= level && logLevel != LogVerbosity.Off;
    void LogInfo(string msg) { if (LogEnabled(LogVerbosity.Info)) Debug.Log(logPrefix + msg); }
    void LogVerbose(string msg) { if (LogEnabled(LogVerbosity.Verbose)) Debug.Log(logPrefix + msg); }
    void LogWarn(string msg) { if (LogEnabled(LogVerbosity.Warnings)) Debug.LogWarning(logPrefix + msg); }
    void LogError(string msg) { if (LogEnabled(LogVerbosity.Errors)) Debug.LogError(logPrefix + msg); }

    // ───────────────── 조건/정의 ─────────────────
    [Serializable]
    public class MessageCondition
    {
        public enum VarType { Bool, Int, String, SceneName }
        public VarType varType = VarType.Bool;

        [Tooltip("DataManager.instance.nowPlayer의 필드명, Messenger내부읽음키, 또는 HouseDoorTeleporter Bool키")]
        public string key;

        // Bool
        public bool boolValue = true;

        // Int
        public enum IntOp { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual }
        public IntOp intOp = IntOp.GreaterOrEqual;
        public int intValue = 0;

        // String / SceneName
        public enum StringOp { Equal, NotEqual, Contains, StartsWith, EndsWith }
        public StringOp stringOp = StringOp.Equal;

        [Tooltip("문자열 비교 기대 값 (VarType=String / SceneName에서 사용)")]
        public string stringValue = "";

        [Tooltip("문자열 비교 시 대소문자 무시 여부")]
        public bool stringIgnoreCase = true;

        public bool EvaluateAgainstDataManager(
            Func<string, bool?> fallbackBoolGetter = null,
            Func<string, int?> fallbackIntGetter = null,
            Action<string> log = null)
        {
            var dm = DataManager.instance;

            switch (varType)
            {
                case VarType.Bool:
                    if (TryGetBool(dm?.nowPlayer, key, out bool vDM))
                    {
                        bool res = vDM == boolValue;
                        log?.Invoke($"Eval Bool key='{key}' (nowPlayer={vDM}) expect={boolValue} -> {res}");
                        return res;
                    }
                    if (fallbackBoolGetter != null)
                    {
                        var fb = fallbackBoolGetter(key);
                        if (fb.HasValue)
                        {
                            bool res = fb.Value == boolValue;
                            log?.Invoke($"Eval Bool key='{key}' (fallback={fb.Value}) expect={boolValue} -> {res}");
                            return res;
                        }
                    }
                    log?.Invoke($"Eval Bool key='{key}' not found -> false");
                    return false;

                case VarType.Int:
                    if (TryGetInt(dm?.nowPlayer, key, out int vi))
                    {
                        bool res = CompareInt(vi, intOp, intValue);
                        log?.Invoke($"Eval Int key='{key}' (nowPlayer={vi}) {intOp} {intValue} -> {res}");
                        return res;
                    }
                    if (fallbackIntGetter != null)
                    {
                        var fbi = fallbackIntGetter(key);
                        if (fbi.HasValue)
                        {
                            bool res = CompareInt(fbi.Value, intOp, intValue);
                            log?.Invoke($"Eval Int key='{key}' (fallback={fbi.Value}) {intOp} {intValue} -> {res}");
                            return res;
                        }
                    }
                    log?.Invoke($"Eval Int key='{key}' not found -> false");
                    return false;

                case VarType.String:
                    if (TryGetString(dm?.nowPlayer, key, out string vs))
                    {
                        bool res = CompareString(vs, stringOp, stringValue, stringIgnoreCase);
                        log?.Invoke($"Eval String key='{key}' (nowPlayer='{vs}') {stringOp} '{stringValue}' -> {res}");
                        return res;
                    }
                    log?.Invoke($"Eval String key='{key}' not found -> false");
                    return false;

                case VarType.SceneName:
                    {
                        string cur = SceneManager.GetActiveScene().name;
                        bool res = CompareString(cur, stringOp, stringValue, stringIgnoreCase);
                        log?.Invoke($"Eval SceneName '{cur}' {stringOp} '{stringValue}' -> {res}");
                        return res;
                    }
            }
            return false;
        }

        static bool CompareInt(int v, IntOp op, int rhs)
        {
            switch (op)
            {
                case IntOp.Equal: return v == rhs;
                case IntOp.NotEqual: return v != rhs;
                case IntOp.Greater: return v > rhs;
                case IntOp.GreaterOrEqual: return v >= rhs;
                case IntOp.Less: return v < rhs;
                case IntOp.LessOrEqual: return v <= rhs;
            }
            return false;
        }

        static bool CompareString(string left, StringOp op, string right, bool ignoreCase)
        {
            var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            switch (op)
            {
                case StringOp.Equal: return string.Equals(left, right, cmp);
                case StringOp.NotEqual: return !string.Equals(left, right, cmp);
                case StringOp.Contains: return left?.IndexOf(right ?? "", cmp) >= 0;
                case StringOp.StartsWith: return left?.StartsWith(right ?? "", cmp) == true;
                case StringOp.EndsWith: return left?.EndsWith(right ?? "", cmp) == true;
            }
            return false;
        }

        static bool TryGetBool(object obj, string name, out bool value)
        {
            value = default;
            if (obj == null) return false;
            var t = obj.GetType();

            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(bool)) { value = (bool)f.GetValue(obj); return true; }

            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(bool) && p.CanRead) { value = (bool)p.GetValue(obj); return true; }

            return false;
        }

        static bool TryGetInt(object obj, string name, out int value)
        {
            value = default;
            if (obj == null) return false;
            var t = obj.GetType();

            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(int)) { value = (int)f.GetValue(obj); return true; }

            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(int) && p.CanRead) { value = (int)p.GetValue(obj); return true; }

            return false;
        }

        static bool TryGetString(object obj, string name, out string value)
        {
            value = default;
            if (obj == null) return false;
            var t = obj.GetType();

            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(string)) { value = (string)f.GetValue(obj); return true; }

            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(string) && p.CanRead) { value = (string)p.GetValue(obj); return true; }

            return false;
        }
    }

    [Serializable]
    public class MessageDef
    {
        [Header("식별/표시")]
        public string messageName;
        public string recipientName;
        public Sprite recipientProfile;

        [Header("Localization")]
        public string localizationTable = "Messenger_Content";

        [Header("조건(AND)")]
        public List<MessageCondition> conditions = new List<MessageCondition>();

        [Header("상태(디버그)")]
        public bool delivered = false;
        public bool readContent = false;
    }

    // ───────────────── 인스펙터 ─────────────────
    [Header("외부 참조")]
    public ScrollRect scrollRect;
    public RectTransform content;
    public MessageItemUI messageItemPrefab;
    public GameObject notReadIndicator;
    public GameObject messengerContent;
    public TextMeshProUGUI messengerContentText;

    [Header("목록 규격")]
    public float panelWidth = 680f;
    public float panelHeight = 200f;

    [Header("메시지 정의")]
    public List<MessageDef> messages = new List<MessageDef>();

    [Header("토큰 치환 옵션")]
    public bool applyTokensToAllTextsInMessengerContent = true;

    [Header("미리보기(Preview) 옵션")]
    public int previewMaxChars = 32;
    public bool previewNormalizeWhitespace = true;
    public string previewEllipsis = " ...";

    [Header("종합 읽힘 상태")]
    public bool AllReadContent = true;

    [Header("자동 프리팹 로드(선택)")]
    public string autoLoadPrefabPath = "UI/Messenger";

    [Header("메시지 아이콘(흔들기)")]
    public RectTransform messageIcon;
    public bool shakeByRotation = true;
    public float shakeFrequency = 4.0f;
    public float shakeAmplitude = 8f;
    public float shakeDuration = 0.9f;
    public int shakeBursts = 2;
    public float shakeInterBurstDelay = 0.7f;
    public Vector2 shakeInterBurstRandomJitter = new Vector2(0.15f, 0.35f);
    public AnimationCurve shakeEnvelope = AnimationCurve.EaseInOut(0, 1, 1, 0);

    // ─────────── 텔레포터 연동 ───────────
    [Header("텔레포터(HouseDoorTeleporter) 연동")]
    [SerializeField] private HouseDoorTeleporter teleporter;
    [SerializeField] private bool autoFindTeleporter = true;

    // ───────────────── 내부 상태 ─────────────────
    readonly Dictionary<string, bool> _readFlags = new(StringComparer.Ordinal);
    readonly List<MessageItemUI> _spawned = new();

    bool _wasAllReadCached;

    private Coroutine _iconShakeRoutine;
    private Coroutine _iconShakeLoopRoutine;

    private Vector2 _iconBaseAnchoredPos;
    private float _iconBaseRotZ;
    private bool _iconBaseCaptured = false;

    private string _openMessageName = null;

    void Awake()
    {
        if (scrollRect && !content) content = scrollRect.content;
        _wasAllReadCached = CalcAllRead();
        ApplyNotReadIndicator(_wasAllReadCached);

        if (messageIcon)
        {
            _iconBaseAnchoredPos = messageIcon.anchoredPosition;
            _iconBaseRotZ = messageIcon.localEulerAngles.z;
            _iconBaseCaptured = true;
        }

        if (autoFindTeleporter && !teleporter)
            teleporter = FindFirstObjectByType<HouseDoorTeleporter>(FindObjectsInactive.Include);

        LogInfo("Awake");
        if (!messageItemPrefab) LogWarn("messageItemPrefab가 비어 있습니다.");
        if (!content) LogWarn("content가 비어 있습니다.");
        if (!messengerContent) LogWarn("messengerContent가 비어 있습니다.");
        if (!messengerContentText) LogWarn("messengerContentText가 비어 있습니다.");
        if (!teleporter) LogVerbose("teleporter가 비어 있음(필수는 아님).");
        if (!notReadIndicator) LogWarn("notReadIndicator가 비어있습니다(읽지 않음 아이콘이 안 켜질 수 있음).");
        if (!messageIcon) LogWarn("messageIcon이 비어있습니다(흔들기 효과 미작동).");
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    IEnumerator Start()
    {
        yield return null;
        AutoBindUI();

        if (!messageItemPrefab)
            LogError("Start: messageItemPrefab이 여전히 null. Resources 경로 또는 프리팹 구성을 확인하세요.");

        if (autoFindTeleporter && !teleporter)
            teleporter = FindFirstObjectByType<HouseDoorTeleporter>(FindObjectsInactive.Include);

        RestoreFromSaveAndRebuild();
    }

    void Update()
    {
        if (logLevel == LogVerbosity.Verbose && (++_updateTick % 60 == 0))
            LogVerbose($"Update tick. delivered={DeliveredCount()}/{messages.Count}, allRead={AllReadContent}");

        EvaluateAndDispatch();
    }

    private void OnLocaleChanged(Locale _)
    {
        LogInfo("Locale changed → refreshing localized texts");
        RefreshSpawnedItemLocalizedTexts();
        RefreshOpenContentLocalizedText();
    }

    private void RefreshSpawnedItemLocalizedTexts()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            var ui = _spawned[i];
            if (!ui) continue;

            var def = FindDef(ui.messageName);
            if (def == null) continue;

            string recipientLoc = LocalizationSettings.StringDatabase
                .GetLocalizedString(def.localizationTable, def.messageName + "_Name");
            if (string.IsNullOrEmpty(recipientLoc)) recipientLoc = def.recipientName;
            string recipient = ReplaceTokens(recipientLoc);
            if (ui.recipientNameText) ui.recipientNameText.text = recipient;

            string localizedFull = LocalizationSettings.StringDatabase
                .GetLocalizedString(def.localizationTable, def.messageName);
            string preview = BuildPreviewFromLocalized(localizedFull);
            if (ui.previewText) ui.previewText.text = preview;
        }
    }

    private void RefreshOpenContentLocalizedText()
    {
        if (!messengerContent || !messengerContent.activeInHierarchy) return;
        if (string.IsNullOrEmpty(_openMessageName)) return;

        var def = FindDef(_openMessageName);
        if (def == null) return;

        if (messengerContentText)
        {
            string localized = LocalizationSettings.StringDatabase
                .GetLocalizedString(def.localizationTable, def.messageName);
            messengerContentText.text = ReplaceTokens(localized);
        }

        ApplyTokensToAllTextsUnderMessenger();
    }

    [ContextMenu("MessageSystem/AutoBindUI")]
    void AutoBindUI()
    {
        if (!scrollRect)
        {
            scrollRect = GetComponentInChildren<ScrollRect>(true);
            if (!scrollRect) LogWarn("AutoBindUI: ScrollRect를 찾지 못했습니다.");
            else LogInfo($"AutoBindUI: ScrollRect='{scrollRect.name}' 바인딩");
        }

        if (!content)
        {
            if (scrollRect && scrollRect.content) content = scrollRect.content;

            if (!content && scrollRect)
            {
                var tf = scrollRect.transform.Find("Viewport/Content") as RectTransform;
                if (tf) content = tf;
            }

            if (!content)
            {
                foreach (var rt in Resources.FindObjectsOfTypeAll<RectTransform>())
                {
                    if (!rt) continue;
                    if (rt.name != "Content") continue;
                    if (rt.GetComponentInParent<ScrollRect>(true))
                    {
                        content = rt;
                        break;
                    }
                }
            }

            if (!content) LogError("AutoBindUI: Content(RectTransform)를 찾지 못했습니다. 인스펙터에 직접 할당하세요.");
            else LogInfo($"AutoBindUI: Content='{content.name}' 경로='{GetPath(content)}'");
        }

        if (!messageItemPrefab && !string.IsNullOrEmpty(autoLoadPrefabPath))
        {
            messageItemPrefab = Resources.Load<MessageItemUI>(autoLoadPrefabPath);

            if (!messageItemPrefab)
            {
                var go = Resources.Load<GameObject>(autoLoadPrefabPath);
                if (go)
                {
                    messageItemPrefab = go.GetComponent<MessageItemUI>();
                    if (!messageItemPrefab)
                        messageItemPrefab = go.GetComponentInChildren<MessageItemUI>(true);
                }
            }

            if (messageItemPrefab)
                LogInfo($"AutoBindUI: Resources.Load(\"{autoLoadPrefabPath}\") 성공 → '{messageItemPrefab.name}'");
            else
                LogWarn($"AutoBindUI: Resources에서 '{autoLoadPrefabPath}' 프리팹을 찾지 못했습니다.");
        }

        if (messageItemPrefab)
            LogInfo($"AutoBindUI: MessageItemPrefab='{messageItemPrefab.name}' OK");
        else
            LogError("AutoBindUI: Message Item Prefab 누락");
    }

    static string GetPath(Transform t)
    {
        if (!t) return "(null)";
        var stack = new Stack<string>();
        while (t) { stack.Push(t.name); t = t.parent; }
        return string.Join("/", stack);
    }

    public void OnBackFromMessenger()
    {
        LogInfo("Back pressed: clearing texts & disabling messengerContent");

        if (!messengerContent) { LogWarn("OnBackFromMessenger: messengerContent null"); return; }

        var texts = messengerContent.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++) texts[i].text = string.Empty;

        var innerScroll = messengerContent.GetComponentInChildren<ScrollRect>(true);
        if (innerScroll) innerScroll.verticalNormalizedPosition = 1f;

        messengerContent.SetActive(false);
        _openMessageName = null;

        if (!AllReadContent || HasAnyUnread())
            EnsureIconShakeLoopIfUnread();
    }

    private string ReplaceTokens(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        string playerName = "No Name";
        var dm = DataManager.instance;
        if (dm != null && dm.nowPlayer != null && !string.IsNullOrEmpty(dm.nowPlayer.Name))
            playerName = dm.nowPlayer.Name;

        return Regex.Replace(input, @"\{playerName\}", playerName, RegexOptions.IgnoreCase);
    }

    private static string NormalizeSpaces(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    private string BuildPreviewFromLocalized(string localizedFullText)
    {
        if (string.IsNullOrEmpty(localizedFullText)) return localizedFullText;

        string s = ReplaceTokens(localizedFullText);
        if (previewNormalizeWhitespace) s = NormalizeSpaces(s);

        if (string.IsNullOrEmpty(s) || s.Length <= previewMaxChars)
            return s;

        int cut = Mathf.Clamp(previewMaxChars, 0, s.Length - 1);
        int lastSpace = s.LastIndexOf(' ', cut);
        if (lastSpace <= 0) lastSpace = cut;

        string head = s.Substring(0, lastSpace)
                       .TrimEnd(' ', '.', '…', ',', '·', '~', '！', '!', '？', '?', '，', '。');

        return head + previewEllipsis;
    }

    private void ApplyTokensToAllTextsUnderMessenger()
    {
        if (!applyTokensToAllTextsInMessengerContent || messengerContent == null) return;
        var texts = messengerContent.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null) continue;
            texts[i].text = ReplaceTokens(texts[i].text);
        }
    }

    public void EvaluateAndDispatch()
    {
        bool newUnreadArrived = false;

        for (int i = 0; i < messages.Count; i++)
        {
            var def = messages[i];
            if (def.delivered) continue;

            bool condLogged = false;
            bool ok = CheckAllConditions(def, (s) => { condLogged = true; LogVerbose($"[Cond '{def.messageName}'] {s}"); });

            if (ok)
            {
                bool isRead = GetReadFlag(def.messageName) ?? false;
                def.readContent = isRead;
                def.delivered = true;

                LogInfo($"조건 충족 → '{def.messageName}' 도착 (isRead={isRead})");
                CreateItemAtTop(def);

                MaybeFlipStartGameTrue(def);
                RecordDeliveredAndSubSave(def.messageName);

                // ── 즉시 UI 반영: 읽지 않음 ON + 흔들기
                if (!isRead)
                {
                    AllReadContent = false;               // 읽지 않은 게 확실히 존재
                    ApplyNotReadIndicator(false);         // 표시 ON
                    EnsureIconShakeLoopIfUnread();        // 루프 보장
                    TriggerMessageIconShakeOnce();        // 도착 피드백 1회 버스트
                }

                newUnreadArrived = newUnreadArrived || !isRead;
            }
            else
            {
                if (!condLogged && LogEnabled(LogVerbosity.Verbose))
                    LogVerbose($"'{def.messageName}' 조건 미충족");
            }
        }

        bool nowAllRead = CalcAllRead();
        if (_wasAllReadCached != nowAllRead)
        {
            _wasAllReadCached = nowAllRead;
            AllReadContent = nowAllRead;

            LogInfo($"AllReadContent 갱신 → {AllReadContent}");
            ApplyNotReadIndicator(AllReadContent);

            if (AllReadContent)
                StopMessageIconShake();
            else
                EnsureIconShakeLoopIfUnread();
        }
        else
        {
            if (newUnreadArrived && !AllReadContent)
            {
                EnsureIconShakeLoopIfUnread();
            }
        }
    }

    void MaybeFlipStartGameTrue(MessageDef def)
    {
        var dm = DataManager.instance;
        if (dm?.nowPlayer == null) return;

        bool hadStartGameFalse = false;
        if (def.conditions != null)
        {
            for (int i = 0; i < def.conditions.Count; i++)
            {
                var c = def.conditions[i];
                if (string.Equals(c.key, "StartGame", StringComparison.Ordinal)
                    && c.varType == MessageCondition.VarType.Bool
                    && c.boolValue == false)
                {
                    hadStartGameFalse = true;
                    break;
                }
            }
        }

        if (!hadStartGameFalse) return;
        if (dm.nowPlayer.StartGame) return;

        dm.nowPlayer.StartGame = true;
        LogInfo("StartGame -> true (message delivered & StartGame==false condition matched)");
    }

    bool CheckAllConditions(MessageDef def, Action<string> log = null)
    {
        if (def.conditions == null || def.conditions.Count == 0)
        {
            log?.Invoke("조건 없음 -> true");
            return true;
        }

        // Messenger 내부 읽음키 → HouseDoorTeleporter Bool 순으로 폴백 조회
        bool? FallbackBoolGetter(string k)
        {
            // 1) Messenger 내부 읽음 키(e.g., "{MessageName}_ReadContent")
            var rf = GetReadFlagByRawKey(k);
            if (rf.HasValue) return rf;

            // 2) HouseDoorTeleporter Bool들 (IsVillage / <Owner>_InHouse / <Owner>_ExitedToVillage)
            var v = GetTeleporterFlagNullable(k);
            if (v.HasValue) return v;

            return null;
        }

        for (int i = 0; i < def.conditions.Count; i++)
        {
            var c = def.conditions[i];
            bool pass = c.EvaluateAgainstDataManager(FallbackBoolGetter, null, log);
            if (!pass)
            {
                log?.Invoke($"조건 {i} 실패");
                return false;
            }
        }
        log?.Invoke("모든 조건 통과");
        return true;
    }

    void CreateItemAtTop(MessageDef def)
    {
        if (!messageItemPrefab)
        {
            LogError("CreateItemAtTop: messageItemPrefab == null");
            return;
        }
        if (!content)
        {
            LogError("CreateItemAtTop: content == null (ScrollRect/Content 바인딩 확인)");
            return;
        }

        LogVerbose($"CreateItemAtTop: parent='{GetPath(content)}' def='{def.messageName}'");
        var item = Instantiate(messageItemPrefab, content);
        item.transform.SetAsFirstSibling();

        var rt = item.GetComponent<RectTransform>();
        if (rt) rt.sizeDelta = new Vector2(panelWidth, panelHeight);

        bool isRead = GetReadFlag(def.messageName) ?? false;

        string recipientLoc = LocalizationSettings.StringDatabase
            .GetLocalizedString(def.localizationTable, def.messageName + "_Name");
        if (string.IsNullOrEmpty(recipientLoc)) recipientLoc = def.recipientName;
        string recipient = ReplaceTokens(recipientLoc);

        string localizedFull = LocalizationSettings.StringDatabase
            .GetLocalizedString(def.localizationTable, def.messageName);
        string preview = BuildPreviewFromLocalized(localizedFull);

        item.Setup(def.recipientProfile, recipient, preview, def.messageName, isRead);

        var btn = item.GetComponent<Button>() ?? item.GetComponentInChildren<Button>(true);
        if (btn != null) btn.onClick.AddListener(item.OnClick);
        else LogWarn($"'{def.messageName}' 프리팹에서 Button을 찾지 못했습니다. OnClick → MessageItemUI.OnClick 연결 권장");

        item.onClick.AddListener(OnClickMessageItem);
        item.OnClicked += OnClickMessageItem;

        _spawned.Insert(0, item);

        Canvas.ForceUpdateCanvases();
        if (scrollRect) scrollRect.verticalNormalizedPosition = 1f;
        Canvas.ForceUpdateCanvases();

        LogVerbose($"CreateItemAtTop → '{def.messageName}', read={isRead}, spawnedCount={_spawned.Count}");
    }

    void OnClickMessageItem(MessageItemUI ui)
    {
        if (!ui) return;
        var def = FindDef(ui.messageName);
        if (def == null) { LogWarn($"OnClick: '{ui.messageName}' 정의를 찾지 못했습니다."); return; }

        LogInfo($"OnClick → '{def.messageName}' 열기");

        if (messengerContentText)
        {
            string localized = LocalizationSettings.StringDatabase.GetLocalizedString(def.localizationTable, def.messageName);
            messengerContentText.text = ReplaceTokens(localized);
        }
        if (messengerContent) messengerContent.SetActive(true);

        _openMessageName = def.messageName;

        ApplyTokensToAllTextsUnderMessenger();

        StopMessageIconShake();

        if (!IsRead(def.messageName))
        {
            LogInfo($"읽음 처리 → '{def.messageName}'");
            SetReadFlag(def.messageName, true);
            def.readContent = true;
            ui.ApplyReadVisual(true);

            RecordReadAndSubSave(def.messageName);

            AllReadContent = CalcAllRead();
            ApplyNotReadIndicator(AllReadContent);

            if (AllReadContent)
                StopMessageIconShake();
            else
                EnsureIconShakeLoopIfUnread();
        }
        else
        {
            LogVerbose($"이미 읽은 메시지 클릭 → '{def.messageName}'");
        }
    }

    void RecordDeliveredAndSubSave(string messageName)
    {
        var dm = DataManager.instance;
        if (dm?.nowPlayer == null) return;

        if (dm.nowPlayer.MessengerDelivered == null)
            dm.nowPlayer.MessengerDelivered = new List<string>();
        if (!dm.nowPlayer.MessengerDelivered.Contains(messageName))
        {
            dm.nowPlayer.MessengerDelivered.Add(messageName);
            LogVerbose($"Delivered 기록 추가: {messageName}");
        }
        dm.CommitDataToTempFile();
    }

    void RecordReadAndSubSave(string messageName)
    {
        var dm = DataManager.instance;
        if (dm?.nowPlayer == null) return;

        if (dm.nowPlayer.MessengerReadList == null)
            dm.nowPlayer.MessengerReadList = new List<string>();
        if (!dm.nowPlayer.MessengerReadList.Contains(messageName))
        {
            dm.nowPlayer.MessengerReadList.Add(messageName);
            LogVerbose($"Read 기록 추가: {messageName}");
        }
        dm.CommitDataToTempFile();
    }

    void RestoreFromSaveAndRebuild()
    {
        var dm = DataManager.instance;
        if (dm?.nowPlayer == null) { LogWarn("Restore: DataManager.nowPlayer가 없습니다."); return; }

        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i]) Destroy(_spawned[i].gameObject);
        _spawned.Clear();
        _readFlags.Clear();
        foreach (var m in messages) { m.delivered = false; m.readContent = false; }

        var delivered = dm.nowPlayer.MessengerDelivered;
        var readList = dm.nowPlayer.MessengerReadList;

        if (delivered == null || delivered.Count == 0)
        {
            AllReadContent = true;
            ApplyNotReadIndicator(AllReadContent);
            StopMessageIconShake();
            LogInfo("Restore: delivered 기록 없음");
            return;
        }

        HashSet<string> readSet = new(readList ?? new List<string>(), StringComparer.Ordinal);

        for (int i = delivered.Count - 1; i >= 0; i--)
        {
            string name = delivered[i];
            var def = FindDef(name);
            if (def == null) continue;

            def.delivered = true;

            bool isRead = readSet.Contains(name);
            SetReadFlag(name, isRead);
            def.readContent = isRead;

            CreateItemAtTop(def);
        }

        AllReadContent = CalcAllRead();
        ApplyNotReadIndicator(AllReadContent);

        if (!AllReadContent || HasAnyUnread())
            EnsureIconShakeLoopIfUnread();
        else
            StopMessageIconShake();

        LogInfo($"Restore: delivered={delivered.Count}, allRead={AllReadContent}");
    }

    string KeyOf(string messageName) => $"{messageName}_ReadContent";
    bool IsRead(string messageName) => GetReadFlag(messageName) ?? false;

    bool? GetReadFlag(string messageName)
    {
        if (string.IsNullOrEmpty(messageName)) return null;
        string key = KeyOf(messageName);
        if (_readFlags.TryGetValue(key, out bool v)) return v;
        return null;
    }

    bool? GetReadFlagByRawKey(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey)) return null;
        if (_readFlags.TryGetValue(rawKey, out bool v)) return v;
        return null;
    }

    void SetReadFlag(string messageName, bool value)
    {
        if (string.IsNullOrEmpty(messageName)) return;
        string key = KeyOf(messageName);
        _readFlags[key] = value;
        LogVerbose($"SetReadFlag → key='{key}', value={value}");
    }

    MessageDef FindDef(string name)
    {
        for (int i = 0; i < messages.Count; i++)
            if (messages[i].messageName == name) return messages[i];
        return null;
    }

    int DeliveredCount()
    {
        int c = 0;
        for (int i = 0; i < messages.Count; i++)
            if (messages[i].delivered) c++;
        return c;
    }

    bool CalcAllRead()
    {
        foreach (var m in messages)
        {
            if (m.delivered && !IsRead(m.messageName))
                return false;
        }
        return true;
    }

    void ApplyNotReadIndicator(bool allRead)
    {
        if (!notReadIndicator) return;
        notReadIndicator.SetActive(!allRead);
        LogVerbose($"ApplyNotReadIndicator → setActive={!allRead}");
    }

    void TriggerMessageIconShakeOnce()
    {
        if (!messageIcon) return;
        if (_iconShakeRoutine != null) StopCoroutine(_iconShakeRoutine);
        _iconShakeRoutine = StartCoroutine(CoShakeBursts());
    }

    void EnsureIconShakeLoopIfUnread()
    {
        if (!messageIcon) return;
        if (_iconShakeLoopRoutine != null) return;
        if (!HasAnyUnread()) return;
        _iconShakeLoopRoutine = StartCoroutine(CoShakeLoopWhileUnread());
    }

    void StopMessageIconShake()
    {
        if (!messageIcon) return;

        if (_iconShakeRoutine != null) { StopCoroutine(_iconShakeRoutine); _iconShakeRoutine = null; }
        if (_iconShakeLoopRoutine != null) { StopCoroutine(_iconShakeLoopRoutine); _iconShakeLoopRoutine = null; }

        if (!_iconBaseCaptured)
        {
            _iconBaseAnchoredPos = messageIcon.anchoredPosition;
            _iconBaseRotZ = messageIcon.localEulerAngles.z;
            _iconBaseCaptured = true;
        }

        messageIcon.anchoredPosition = _iconBaseAnchoredPos;
        var e = messageIcon.localEulerAngles; e.z = _iconBaseRotZ; messageIcon.localEulerAngles = e;
    }

    IEnumerator CoShakeLoopWhileUnread()
    {
        _iconBaseAnchoredPos = messageIcon.anchoredPosition;
        _iconBaseRotZ = messageIcon.localEulerAngles.z;
        _iconBaseCaptured = true;

        while (HasAnyUnread())
        {
            if (messengerContent && messengerContent.activeInHierarchy)
            {
                yield return null;
                continue;
            }

            yield return CoShakeBursts();

            float jitter = UnityEngine.Random.Range(shakeInterBurstRandomJitter.x, shakeInterBurstRandomJitter.y);
            float wait = Mathf.Max(0f, shakeInterBurstDelay + jitter);
            yield return new WaitForSecondsRealtime(wait);
        }

        _iconShakeLoopRoutine = null;
    }

    IEnumerator CoShakeBursts()
    {
        if (!messageIcon) yield break;

        _iconBaseAnchoredPos = messageIcon.anchoredPosition;
        _iconBaseRotZ = messageIcon.localEulerAngles.z;
        _iconBaseCaptured = true;

        int bursts = Mathf.Max(1, shakeBursts);
        for (int b = 0; b < bursts; b++)
        {
            float t = 0f;
            while (t < shakeDuration)
            {
                t += Time.unscaledDeltaTime;

                float norm = Mathf.Clamp01(t / shakeDuration);
                float env = shakeEnvelope != null ? Mathf.Clamp01(shakeEnvelope.Evaluate(norm)) : (1f - norm);

                float phase = t * shakeFrequency * Mathf.PI * 2f;
                float s = Mathf.Sin(phase) * shakeAmplitude * env;

                if (shakeByRotation)
                {
                    var e = messageIcon.localEulerAngles;
                    e.z = _iconBaseRotZ + s;
                    messageIcon.localEulerAngles = e;
                }
                else
                {
                    var pos = messageIcon.anchoredPosition;
                    pos.x = _iconBaseAnchoredPos.x + s;
                    messageIcon.anchoredPosition = pos;
                }

                yield return null;
            }

            messageIcon.anchoredPosition = _iconBaseAnchoredPos;
            var e1 = messageIcon.localEulerAngles; e1.z = _iconBaseRotZ; messageIcon.localEulerAngles = e1;

            if (b < bursts - 1)
            {
                float jitter = UnityEngine.Random.Range(shakeInterBurstRandomJitter.x, shakeInterBurstRandomJitter.y);
                float wait = Mathf.Max(0f, shakeInterBurstDelay + jitter);
                yield return new WaitForSecondsRealtime(wait);
            }
        }

        _iconShakeRoutine = null;
    }

    bool HasAnyUnread()
    {
        for (int i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            if (m.delivered && !IsRead(m.messageName))
                return true;
        }
        return false;
    }

    public void ForceRefreshIndicators()
    {
        bool nowAllRead = CalcAllRead();
        AllReadContent = nowAllRead;
        ApplyNotReadIndicator(nowAllRead);

        if (!AllReadContent || HasAnyUnread())
            EnsureIconShakeLoopIfUnread();
        else
            StopMessageIconShake();

        LogInfo($"ForceRefreshIndicators -> AllRead={AllReadContent}");
    }

    // ───────────────── 외부 트리거용: 조건 만족 시 즉시 전달 ─────────────────
    public bool TryDeliver(string messageName)
    {
        var def = FindDef(messageName);
        if (def == null) { LogWarn($"TryDeliver: '{messageName}' 정의 없음"); return false; }
        if (def.delivered) { LogVerbose($"TryDeliver: '{messageName}' 이미 전달됨"); return false; }

        if (!CheckAllConditions(def, null))
        {
            LogVerbose($"TryDeliver: '{messageName}' 조건 미충족");
            return false;
        }

        def.delivered = true;

        CreateItemAtTop(def);
        RecordDeliveredAndSubSave(def.messageName);

        // 즉시 갱신/흔들기
        AllReadContent = CalcAllRead();
        ApplyNotReadIndicator(AllReadContent);
        if (!AllReadContent || HasAnyUnread())
        {
            EnsureIconShakeLoopIfUnread();
            TriggerMessageIconShakeOnce();
        }
        else
        {
            StopMessageIconShake();
        }

        LogInfo($"TryDeliver → '{messageName}' 전달 완료");
        return true;
    }

    // ─────────── HouseDoorTeleporter Bool 폴백 조회 ───────────
    private bool? GetTeleporterFlagNullable(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        if (!teleporter && autoFindTeleporter)
            teleporter = FindFirstObjectByType<HouseDoorTeleporter>(FindObjectsInactive.Include);

        if (!teleporter) return null;

        if (teleporter.TryGetFlag(key, out var v))
            return v;

        return null;
    }
}
