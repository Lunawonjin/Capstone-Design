using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.Localization.Settings;

[DisallowMultipleComponent]
public class MessageSystem : MonoBehaviour
{
    // ───────────── 로깅 ─────────────
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

    // ───────────── 조건/정의 ─────────────
    [Serializable]
    public class MessageCondition
    {
        public enum VarType { Bool, Int, String, SceneName }   // String, SceneName 포함
        public VarType varType = VarType.Bool;

        [Tooltip("DataManager.instance.nowPlayer의 필드명, 혹은 '{메시지}_ReadContent' 키")]
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
        public string messageName;     // Localization Entry 키
        public string recipientName;   // 폴백용(키 없을 때만 사용)
        public Sprite recipientProfile;

        [Header("Localization")]
        public string localizationTable = "Messenger_Content";

        [Header("조건(AND)")]
        public List<MessageCondition> conditions = new List<MessageCondition>();

        [Header("상태(디버그)")]
        public bool delivered = false;
        public bool readContent = false; // 실제 판정은 읽음 키 참조
    }

    // ───────────── 인스펙터 ─────────────
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
    [Tooltip("MessengerContent 하위의 모든 TMP_Text에 토큰을 적용할지 여부")]
    public bool applyTokensToAllTextsInMessengerContent = true;

    [Header("미리보기(Preview) 옵션")]
    [Tooltip("미리보기에 허용할 최대 글자 수(치환 후 기준). 초과 시 공백 기준으로 잘라 ' ...'을 붙입니다.")]
    public int previewMaxChars = 32;
    [Tooltip("미리보기 앞뒤·중복 공백 정리")]
    public bool previewNormalizeWhitespace = true;
    [Tooltip("말줄임표 문자열(앞에 공백을 포함해 ' ...' 권장)")]
    public string previewEllipsis = " ...";

    [Header("종합 읽힘 상태")]
    public bool AllReadContent = true;

    [Header("자동 프리팹 로드(선택)")]
    [Tooltip("Resources/<이 경로>에서 MessageItemUI 프리팹을 자동 로드합니다. 예: UI/Messenger")]
    public string autoLoadPrefabPath = "UI/Messenger";

    // ───────────── 내부 상태 ─────────────
    // 규칙: "{messageName}_ReadContent" → true/false
    readonly Dictionary<string, bool> _readFlags = new(StringComparer.Ordinal);
    readonly List<MessageItemUI> _spawned = new();
    Coroutine _blinkRoutine;
    bool _wasAllReadCached;

    // 로케일 변경 시 본문도 다시 로드하기 위해 마지막으로 연 메시지 이름 기억
    private string _lastOpenedMessageName = null;

    // ───────────── 라이프사이클 ─────────────
    void Awake()
    {
        if (scrollRect && !content) content = scrollRect.content;
        _wasAllReadCached = CalcAllRead();
        ApplyNotReadIndicator(_wasAllReadCached);

        LogInfo("Awake");
        if (!messageItemPrefab) LogWarn("messageItemPrefab가 비어 있습니다.");
        if (!content) LogWarn("content가 비어 있습니다.");
        if (!messengerContent) LogWarn("messengerContent가 비어 있습니다.");
        if (!messengerContentText) LogWarn("messengerContentText가 비어 있습니다.");
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    // UI가 모두 올라온 다음 한 프레임 뒤에 자동 바인딩 및 복원
    IEnumerator Start()
    {
        yield return null;
        AutoBindUI();

        if (!messageItemPrefab)
            LogError("Start: messageItemPrefab이 여전히 null입니다. Resources 경로 또는 프리팹 구성을 확인하세요.");

        RestoreFromSaveAndRebuild();
    }

    void Update()
    {
        if (logLevel == LogVerbosity.Verbose && (++_updateTick % 60 == 0))
            LogVerbose($"Update tick. delivered={DeliveredCount()}/{messages.Count}, allRead={AllReadContent}");

        EvaluateAndDispatch();
    }

    // ───────────── 로케일 변경 처리 ─────────────
    private void OnLocaleChanged(UnityEngine.Localization.Locale _)
    {
        LogInfo("Locale changed → RefreshAllSpawnedItemTexts & refresh opened content");

        // 리스트 아이템(수신자/프리뷰) 전부 새 언어로 갱신
        RefreshAllSpawnedItemTexts();

        // 메시지 본문 창이 열려 있으면 그 내용도 갱신
        if (messengerContent && messengerContent.activeSelf && messengerContentText && !string.IsNullOrEmpty(_lastOpenedMessageName))
        {
            var def = FindDef(_lastOpenedMessageName);
            if (def != null)
            {
                string localized = LocalizationSettings.StringDatabase
                    .GetLocalizedString(def.localizationTable, def.messageName);
                messengerContentText.text = ReplaceTokens(localized);
                ApplyTokensToAllTextsUnderMessenger();
            }
        }
    }

    private void RefreshAllSpawnedItemTexts()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            var ui = _spawned[i];
            if (!ui) continue;

            var def = FindDef(ui.messageName);
            if (def == null) continue;

            // 수신자 이름: <테이블>/<키: {MessageName}_Name>
            string recipientLoc = LocalizationSettings.StringDatabase
                .GetLocalizedString(def.localizationTable, def.messageName + "_Name");
            if (string.IsNullOrEmpty(recipientLoc)) recipientLoc = def.recipientName;
            string recipient = ReplaceTokens(recipientLoc);

            // 프리뷰: 본문에서 잘라 생성
            string localizedFull = LocalizationSettings.StringDatabase
                .GetLocalizedString(def.localizationTable, def.messageName);
            string preview = BuildPreviewFromLocalized(localizedFull);

            if (ui.recipientNameText) ui.recipientNameText.text = recipient;
            if (ui.previewText) ui.previewText.text = preview;
        }
    }

    // ───────────── 자동 바인딩 & 진단 ─────────────
    [ContextMenu("MessageSystem/AutoBindUI")]
    void AutoBindUI()
    {
        // 1) ScrollRect 자동 탐색
        if (!scrollRect)
        {
            scrollRect = GetComponentInChildren<ScrollRect>(true);
            if (!scrollRect) LogWarn("AutoBindUI: ScrollRect를 찾지 못했습니다.");
            else LogInfo($"AutoBindUI: ScrollRect='{scrollRect.name}' 바인딩");
        }

        // 2) Content 자동 탐색
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

        // 3) Prefab 확인 + 자동 로드
        if (!messageItemPrefab && !string.IsNullOrEmpty(autoLoadPrefabPath))
        {
            // MessageItemUI 타입으로 직접 로드 시도
            messageItemPrefab = Resources.Load<MessageItemUI>(autoLoadPrefabPath);

            // 루트에 MessageItemUI가 없으면 GameObject 로드 후 컴포넌트 탐색
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
                LogWarn($"AutoBindUI: Resources에서 '{autoLoadPrefabPath}' 프리팹을 찾지 못했습니다. 경로/파일 확인 요망.");
        }

        if (messageItemPrefab)
            LogInfo($"AutoBindUI: MessageItemPrefab='{messageItemPrefab.name}' OK");
        else
            LogError("AutoBindUI: Message Item Prefab이 비어 있습니다. (MessageItemUI 컴포넌트가 붙은 프리팹 에셋을 할당하거나 autoLoadPrefabPath를 사용하세요)");
    }

    static string GetPath(Transform t)
    {
        if (!t) return "(null)";
        var stack = new Stack<string>();
        while (t) { stack.Push(t.name); t = t.parent; }
        return string.Join("/", stack);
    }

    // ───────────── Back 버튼 ─────────────
    public void OnBackFromMessenger()
    {
        LogInfo("Back pressed: clearing texts & disabling messengerContent");

        if (!messengerContent) { LogWarn("OnBackFromMessenger: messengerContent null"); return; }

        var texts = messengerContent.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++) texts[i].text = string.Empty;

        var innerScroll = messengerContent.GetComponentInChildren<ScrollRect>(true);
        if (innerScroll) innerScroll.verticalNormalizedPosition = 1f;

        messengerContent.SetActive(false);

        // 현재 열린 본문 추적 초기화
        _lastOpenedMessageName = null;
    }

    // ───────────── 토큰 치환 & 미리보기 ─────────────
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

    // ───────────── 메인 평가/전달 ─────────────
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

                // StartGame == false 조건을 포함했다면 1회만 true로 전환
                MaybeFlipStartGameTrue(def);

                // 도착 기록 + SubSave 커밋
                RecordDeliveredAndSubSave(def.messageName);

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
            if (AllReadContent) { if (notReadIndicator) notReadIndicator.SetActive(false); }
            else { BlinkNotReadThenStayOn(); }
        }
        else
        {
            if (newUnreadArrived && !AllReadContent)
            {
                LogInfo("새 미읽음 도착 → NotRead 점멸");
                BlinkNotReadThenStayOn();
            }
        }
    }

    // 메시지 '도착' 직후, 이 메시지의 조건에 StartGame==false가 포함되어 있었다면 1회만 true로 전환
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
        // 필요 시: if (dm.nowSlot >= 0) dm.SaveData();
    }

    bool CheckAllConditions(MessageDef def, Action<string> log = null)
    {
        if (def.conditions == null || def.conditions.Count == 0)
        {
            log?.Invoke("조건 없음 -> true");
            return true;
        }

        bool? FallbackBoolGetter(string k) => GetReadFlagByRawKey(k);

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
        var item = Instantiate(messageItemPrefab, content); // Content 하위에 붙음
        item.transform.SetAsFirstSibling();

        var rt = item.GetComponent<RectTransform>();
        if (rt) rt.sizeDelta = new Vector2(panelWidth, panelHeight);

        bool isRead = GetReadFlag(def.messageName) ?? false;

        // 1) 수신자 이름: localizationTable에서 {MessageName}_Name 키 사용 (없으면 폴백)
        string recipientLoc = LocalizationSettings.StringDatabase
            .GetLocalizedString(def.localizationTable, def.messageName + "_Name");
        if (string.IsNullOrEmpty(recipientLoc)) recipientLoc = def.recipientName;
        string recipient = ReplaceTokens(recipientLoc);

        // 2) 미리보기: 본문에서 생성
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

        // 마지막으로 연 메시지 이름 기억 (로케일 변경 시 본문 재로딩)
        _lastOpenedMessageName = def.messageName;

        ApplyTokensToAllTextsUnderMessenger();

        if (!IsRead(def.messageName))
        {
            LogInfo($"읽음 처리 → '{def.messageName}'");
            SetReadFlag(def.messageName, true);
            def.readContent = true;
            ui.ApplyReadVisual(true);

            // 읽음 기록 + SubSave 커밋
            RecordReadAndSubSave(def.messageName);

            AllReadContent = CalcAllRead();
            if (AllReadContent && notReadIndicator)
                notReadIndicator.SetActive(false);
        }
        else
        {
            LogVerbose($"이미 읽은 메시지 클릭 → '{def.messageName}'");
        }
    }

    // ───────────── SubSave 기록 함수 ─────────────
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
        dm.CommitDataToTempFile(); // SubSave 즉시 커밋
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
        dm.CommitDataToTempFile(); // SubSave 즉시 커밋
    }

    // 저장된 상태 기반으로 UI 재구성
    void RestoreFromSaveAndRebuild()
    {
        var dm = DataManager.instance;
        if (dm?.nowPlayer == null) { LogWarn("Restore: DataManager.nowPlayer가 없습니다."); return; }

        // 기존 UI/캐시 정리
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
            LogInfo("Restore: delivered 기록 없음");
            return;
        }

        // 읽음 캐시 셋업
        HashSet<string> readSet = new(readList ?? new List<string>(), StringComparer.Ordinal);

        // delivered 저장 순서를 유지하고, 최신이 위로 오도록 뒤에서부터 생성
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
        LogInfo($"Restore: delivered={delivered.Count}, allRead={AllReadContent}");
    }

    // ───────────── 읽음 키 유틸 ─────────────
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

    // ───────────── 보조 ─────────────
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

    void BlinkNotReadThenStayOn()
    {
        if (!notReadIndicator) return;
        if (_blinkRoutine != null) StopCoroutine(_blinkRoutine);
        _blinkRoutine = StartCoroutine(CoBlink3TimesThenOn(notReadIndicator, 0.2f));
        LogVerbose("BlinkNotReadThenStayOn 시작");
    }

    IEnumerator CoBlink3TimesThenOn(GameObject go, float interval)
    {
        for (int i = 0; i < 3; i++)
        {
            go.SetActive(true); yield return new WaitForSecondsRealtime(interval);
            go.SetActive(false); yield return new WaitForSecondsRealtime(interval);
        }
        go.SetActive(true);
        _blinkRoutine = null;
        LogVerbose("BlinkNotReadThenStayOn 종료 -> ON 유지");
    }

    public void ForceRefreshIndicators()
    {
        bool nowAllRead = CalcAllRead();
        AllReadContent = nowAllRead;
        ApplyNotReadIndicator(nowAllRead);
        LogInfo($"ForceRefreshIndicators -> AllRead={AllReadContent}");
    }
}
