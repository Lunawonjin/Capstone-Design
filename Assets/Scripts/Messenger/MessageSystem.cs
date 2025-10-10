using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Settings;

[DisallowMultipleComponent]
public class MessageSystem : MonoBehaviour
{
    // ───────────── 로깅 ─────────────
    public enum LogVerbosity { Off, Errors, Warnings, Info, Verbose }

    [Header("로깅")]
    [Tooltip("콘솔 로그 수준을 선택하세요.")]
    public LogVerbosity logLevel = LogVerbosity.Info;

    [Tooltip("로그 메시지 접두사")]
    public string logPrefix = "[MessageSystem] ";

    int _updateTick; // 과도한 Update 로그 방지용

    bool LogEnabled(LogVerbosity level) => logLevel >= level && logLevel != LogVerbosity.Off;
    void LogInfo(string msg) { if (LogEnabled(LogVerbosity.Info)) Debug.Log(logPrefix + msg); }
    void LogVerbose(string msg) { if (LogEnabled(LogVerbosity.Verbose)) Debug.Log(logPrefix + msg); }
    void LogWarn(string msg) { if (LogEnabled(LogVerbosity.Warnings)) Debug.LogWarning(logPrefix + msg); }
    void LogError(string msg) { if (LogEnabled(LogVerbosity.Errors)) Debug.LogError(logPrefix + msg); }

    // ───────────── 조건/정의 ─────────────
    [Serializable]
    public class MessageCondition
    {
        public enum VarType { Bool, Int }
        public VarType varType = VarType.Bool;

        [Tooltip("DataManager.instance.nowPlayer의 필드명 (예: StartGame, Sol_FriendShip 등) 또는 '{메시지}_ReadContent'")]
        public string key;

        [Tooltip("Bool 비교 값")]
        public bool boolValue = true;

        public enum IntOp { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual }
        public IntOp intOp = IntOp.GreaterOrEqual;

        [Tooltip("Int 비교 기준 값")]
        public int intValue = 0;

        public bool EvaluateAgainstDataManager(Func<string, bool?> fallbackBoolGetter = null, Func<string, int?> fallbackIntGetter = null, Action<string> log = null)
        {
            var dm = DataManager.instance;
            if (string.IsNullOrEmpty(key))
            {
                log?.Invoke($"Condition key is empty -> false");
                return false;
            }

            if (varType == VarType.Bool)
            {
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
            }
            else
            {
                if (TryGetInt(dm?.nowPlayer, key, out int vInt))
                {
                    bool res = CompareInt(vInt, intOp, intValue);
                    log?.Invoke($"Eval Int key='{key}' (nowPlayer={vInt}) {intOp} {intValue} -> {res}");
                    return res;
                }

                if (fallbackIntGetter != null)
                {
                    var fb = fallbackIntGetter(key);
                    if (fb.HasValue)
                    {
                        bool res = CompareInt(fb.Value, intOp, intValue);
                        log?.Invoke($"Eval Int key='{key}' (fallback={fb.Value}) {intOp} {intValue} -> {res}");
                        return res;
                    }
                }

                log?.Invoke($"Eval Int key='{key}' not found -> false");
                return false;
            }
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
    }

    [Serializable]
    public class MessageDef
    {
        [Header("식별/표시")]
        public string messageName;     // Localization Entry 키
        public string recipientName;
        public Sprite recipientProfile;
        [TextArea] public string previewText;

        [Header("Localization")]
        public string localizationTable = "Messenger";

        [Header("조건(AND)")]
        public List<MessageCondition> conditions = new List<MessageCondition>();

        [Header("상태(디버그)")]
        public bool delivered = false;
        public bool readContent = false; // 초기 시각적용용(런타임 시 읽음 키에서 다시 가져옴)
    }

    // ───────────── 인스펙터 ─────────────
    [Header("외부 참조")]
    public ScrollRect scrollRect;
    public RectTransform content;
    public MessageItemUI messageItemPrefab;      // 패널 프리팹(에셋)
    public GameObject notReadIndicator;
    public GameObject messengerContent;          // 열릴 창
    public TextMeshProUGUI messengerContentText; // 본문

    [Header("목록 규격")]
    public float panelWidth = 680f;
    public float panelHeight = 200f;

    [Header("메시지 정의")]
    public List<MessageDef> messages = new List<MessageDef>();

    [Header("StartGame 특수 트리거")]
    [Tooltip("StartGame==false면 해당 메시지를 1회 생성 후 StartGame=true로 뒤집습니다.")]
    public string startGameMessageName = "Boss_First_Messenger";
    public bool autoFlipStartGameTrueOnce = true;
    public bool autoSaveAfterFlip = false;

    [Header("종합 읽힘 상태")]
    public bool AllReadContent = true;

    // ───────────── 내부: 읽음 키 저장 ─────────────
    // 규칙: "{messageName}_ReadContent" → true/false
    readonly Dictionary<string, bool> _readFlags = new Dictionary<string, bool>(StringComparer.Ordinal);

    readonly List<MessageItemUI> _spawned = new List<MessageItemUI>();
    Coroutine _blinkRoutine;
    bool _wasAllReadCached;

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

    void Update()
    {
        // 과도한 스팸 방지 : Verbose에서만 N프레임마다 1번 찍기
        if (logLevel == LogVerbosity.Verbose && (++_updateTick % 60 == 0))
        {
            LogVerbose($"Update tick. delivered={DeliveredCount()}/{messages.Count}, allRead={AllReadContent}");
        }

        TriggerStartGameOnceIfNeeded();
        EvaluateAndDispatch();
    }

    // ───────────── Back 버튼 공개 메서드 ─────────────
    public void OnBackFromMessenger()
    {
        LogInfo("Back pressed: clearing texts & disabling messengerContent");

        if (!messengerContent) { LogWarn("OnBackFromMessenger: messengerContent null"); return; }

        // 내부 모든 TMP_Text 비움
        var texts = messengerContent.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++) texts[i].text = string.Empty;

        // 내부 스크롤 맨 위로
        var innerScroll = messengerContent.GetComponentInChildren<ScrollRect>(true);
        if (innerScroll) innerScroll.verticalNormalizedPosition = 1f;

        // 창 비활성
        messengerContent.SetActive(false);
    }

    // ───────────── 트리거/평가/생성 ─────────────
    void TriggerStartGameOnceIfNeeded()
    {
        if (!autoFlipStartGameTrueOnce) return;
        var dm = DataManager.instance;
        if (dm == null || dm.nowPlayer == null || dm.nowPlayer.StartGame) return;

        var def = FindDef(startGameMessageName);
        if (def == null)
        {
            LogWarn($"StartGame 트리거 대상 '{startGameMessageName}' 메시지를 찾지 못했습니다.");
            return;
        }
        if (def.delivered) return;

        LogInfo($"StartGame=false 감지 → '{startGameMessageName}' 도착 생성");
        CreateItemAtTop(def);

        // 읽음 키 초기값 적용(기본 false)
        SetReadFlag(def.messageName, GetReadFlag(def.messageName) ?? false);

        AllReadContent = CalcAllRead();
        BlinkNotReadThenStayOn();

        dm.nowPlayer.StartGame = true;
        LogInfo("StartGame → true 로 전환");
        if (autoSaveAfterFlip && dm.nowSlot >= 0)
        {
            dm.SaveData();
            LogInfo("SaveData 호출(Flip 이후)");
        }
    }

    public void EvaluateAndDispatch()
    {
        bool newUnreadArrived = false;

        for (int i = 0; i < messages.Count; i++)
        {
            var def = messages[i];
            if (def.delivered) continue;

            bool ConditionsLoggerInvoked = false;
            bool ok = CheckAllConditions(def, (s) => { ConditionsLoggerInvoked = true; LogVerbose($"[Cond '{def.messageName}'] {s}"); });

            if (ok)
            {
                // 등장 시점의 읽음 상태는 읽음 키로 결정(없으면 false)
                bool isRead = GetReadFlag(def.messageName) ?? false;
                def.readContent = isRead;
                def.delivered = true;

                LogInfo($"조건 충족 → '{def.messageName}' 도착 (isRead={isRead})");
                CreateItemAtTop(def);
                newUnreadArrived = newUnreadArrived || !isRead;
            }
            else
            {
                if (ConditionsLoggerInvoked == false && LogEnabled(LogVerbosity.Verbose))
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
        if (!messageItemPrefab || !content)
        {
            LogError("CreateItemAtTop: messageItemPrefab 또는 content 누락");
            return;
        }

        var item = Instantiate(messageItemPrefab, content);
        item.transform.SetAsFirstSibling();

        var rt = item.GetComponent<RectTransform>();
        if (rt) rt.sizeDelta = new Vector2(panelWidth, panelHeight);

        bool isRead = GetReadFlag(def.messageName) ?? false;
        item.Setup(def.recipientProfile, def.recipientName, def.previewText, def.messageName, isRead);

        // 프리팹에 Button 배선이 없어도 안전하게: 자동 연결
        var btn = item.GetComponent<Button>() ?? item.GetComponentInChildren<Button>(true);
        if (btn != null) btn.onClick.AddListener(item.OnClick);
        else LogWarn($"'{def.messageName}' 프리팹에서 Button을 찾지 못했습니다. 프리팹 버튼 OnClick → MessageItemUI.OnClick 연결 권장");

        // 클릭 이벤트를 매니저로 전달
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
        // 본문 로드 + 창 열기
        if (messengerContentText)
        {
            string localized = LocalizationSettings.StringDatabase.GetLocalizedString(def.localizationTable, def.messageName);
            messengerContentText.text = localized;
        }
        if (messengerContent) messengerContent.SetActive(true);

        // 읽음 처리: {이름}_ReadContent = true
        if (!IsRead(def.messageName))
        {
            LogInfo($"읽음 처리 → '{def.messageName}'");
            SetReadFlag(def.messageName, true); // 키 규칙에 따라 저장
            def.readContent = true;
            ui.ApplyReadVisual(true);

            AllReadContent = CalcAllRead();
            if (AllReadContent && notReadIndicator)
                notReadIndicator.SetActive(false);
        }
        else
        {
            LogVerbose($"이미 읽은 메시지 클릭 → '{def.messageName}'");
        }
    }

    // ───────────── 읽음 키 유틸 ─────────────
    string KeyOf(string messageName) => $"{messageName}_ReadContent";

    bool IsRead(string messageName) => GetReadFlag(messageName) ?? false;

    bool? GetReadFlag(string messageName)
    {
        if (string.IsNullOrEmpty(messageName)) return null;
        string key = KeyOf(messageName);
        if (_readFlags.TryGetValue(key, out bool v)) return v;
        return null; // 없으면 미정(=기본 false 취급)
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
        bool anyDelivered = false;
        foreach (var m in messages)
        {
            if (m.delivered)
            {
                anyDelivered = true;
                if (!IsRead(m.messageName)) return false;
            }
        }
        return true; // 도착한 메시지가 없으면 읽을 것도 없으니 true
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
