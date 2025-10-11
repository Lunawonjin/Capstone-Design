using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Localization.Settings;

[DisallowMultipleComponent]
public class MessengerEventManager : MonoBehaviour
{
    public enum LogVerbosity { Off, Errors, Warnings, Info, Verbose }

    [Header("로그")]
    [SerializeField] private LogVerbosity logLevel = LogVerbosity.Info;
    [SerializeField] private string logPrefix = "[MessengerEventManager] ";

    [Header("트리거 조건")]
    [SerializeField] private string targetMessageName = "Boss_First_Messenger";
    [SerializeField] private GameObject messengerRoot;

    [Header("대화 UI")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private RectTransform dialogueAnimRoot;

    [Header("로컬라이즈 소스")]
    [SerializeField] private string localizationTable = "Messenger_Content";
    [SerializeField] private string noticeKey = "Boss_First_Messenger_Notice";

    [Header("타자기(타이핑) 효과")]
    [SerializeField, Min(1f)] private float charsPerSecond = 30f;
    [SerializeField] private bool typeRichTextPerChar = false;

    [Header("입력/닫기")]
    [SerializeField] private bool useClickOrSpaceToSkipThenClose = true;
    [SerializeField] private bool anyMouseButton = true;
    [SerializeField] private KeyCode[] extraKeys = new KeyCode[] { KeyCode.Return, KeyCode.KeypadEnter };

    [Header("동작 옵션")]
    [SerializeField, Min(0f)] private float showDelay = 0.0f;
    [SerializeField] private bool replaceTokens = true;
    [SerializeField] private bool triggerOnce = true;
    [SerializeField] private bool autoStart = true;
    [SerializeField, Min(0.05f)] private float pollInterval = 0.2f;

    [Header("열림 트윈 (UnscaledTime)")]
    [SerializeField] private Vector3 openStartScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField] private Vector3 openEndScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float openDuration = 0.14f;
    [SerializeField] private bool openUseAlphaFade = true;
    [SerializeField, Range(0f, 1f)] private float openStartAlpha = 0f;
    [SerializeField, Range(0f, 1f)] private float openEndAlpha = 1f;
    [SerializeField] private bool startTypingAfterOpenTween = true;

    [Header("닫힘 트윈 (UnscaledTime)")]
    [SerializeField] private Vector3 closeStartScale = Vector3.one;
    [SerializeField] private Vector3 closeEndScale = new Vector3(0.9f, 0.9f, 1f);
    [SerializeField, Min(0.01f)] private float closeDuration = 0.12f;
    [SerializeField] private bool closeUseAlphaFade = true;
    [SerializeField, Range(0f, 1f)] private float closeStartAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float closeEndAlpha = 0f;

    [Header("Map 연동(옵션)")]
    [SerializeField] private MapMenuController mapMenu;

    [Header("재발동 방지(씬 직후 억제만 유지)")]
    [SerializeField, Min(0f)] private float suppressOnSceneLoadSeconds = 1.5f;

    // ===== 여기부터: 플레이어 움직임 잠금 옵션 =====
    [Header("플레이어 잠금")]
    [Tooltip("패널 열려있는 동안 PlayerMove 컨트롤을 잠급니다.")]
    [SerializeField] private bool lockPlayerWhileOpen = true;

    [Tooltip("Player 태그에서 PlayerMove를 자동 수집합니다.")]
    [SerializeField] private bool autoFindPlayers = true;

    [Tooltip("직접 지정할 PlayerMove들(자동 수집에 추가)")]
    [SerializeField] private List<PlayerMove> extraPlayers = new List<PlayerMove>();
    // ===========================================

    // 내부 상태
    private bool _fired;
    private Coroutine _watcher, _typingCo, _openTweenCo, _closeTweenCo;
    private bool _isShowing, _isTyping, _isAnimPlaying;
    private string _fullText = "";
    private CanvasGroup _cg;
    private RectTransform _animRT;

    // 플레이어 잠금 내부 상태
    private readonly List<PlayerMove> _cachedPlayers = new List<PlayerMove>(4);
    private bool _playersLocked = false;

    void Awake()
    {
        if (!dialogueText && dialoguePanel)
            dialogueText = dialoguePanel.GetComponentInChildren<TMP_Text>(true);

        if (!dialogueAnimRoot && dialoguePanel)
            dialogueAnimRoot = dialoguePanel.GetComponent<RectTransform>();
        _animRT = dialogueAnimRoot;

        if (_animRT) _cg = _animRT.GetComponent<CanvasGroup>() ?? _animRT.gameObject.GetComponent<CanvasGroup>();
        if (dialoguePanel && dialoguePanel.activeSelf) dialoguePanel.SetActive(false);

        // 자동 MapMenuController 바인딩
        if (!mapMenu)
        {
#if UNITY_2023_1_OR_NEWER
            mapMenu = Object.FindAnyObjectByType<MapMenuController>() ?? Object.FindFirstObjectByType<MapMenuController>();
#else
            mapMenu = Object.FindObjectOfType<MapMenuController>();
#endif
        }

        // 플레이어 캐시
        BuildPlayerList();
    }

    void OnEnable()
    {
        if (autoStart && _watcher == null)
            _watcher = StartCoroutine(CoWatch());
    }

    void OnDisable()
    {
        if (_watcher != null) { StopCoroutine(_watcher); _watcher = null; }
        if (_typingCo != null) { StopCoroutine(_typingCo); _typingCo = null; }
        if (_openTweenCo != null) { StopCoroutine(_openTweenCo); _openTweenCo = null; }
        if (_closeTweenCo != null) { StopCoroutine(_closeTweenCo); _closeTweenCo = null; }

        // 혹시 켜진 채 비활성화되면 잠금 해제 보장
        if (_playersLocked) UnlockPlayers();
    }

    void Update()
    {
        if (!_isShowing || !useClickOrSpaceToSkipThenClose) return;
        if (_isAnimPlaying) return;

        if (Pressed())
        {
            if (_isTyping) SkipTypewriterToEnd();
            else CloseDialoguePanel();
        }
    }

    [ContextMenu("MessengerEventManager/ForceCheckNow")]
    public void ForceCheckNow() => TryTriggerIfConditionsMet();

    IEnumerator CoWatch()
    {
        yield return null; // 첫 프레임 대기
        while (true)
        {
            TryTriggerIfConditionsMet();

            if (triggerOnce && _fired)
            {
                LogInfo("Triggered once. Stop watching.");
                _watcher = null;
                yield break;
            }
            yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollInterval));
        }
    }

    private void TryTriggerIfConditionsMet()
    {
        // 0) 씬 로드 직후 억제
        if (suppressOnSceneLoadSeconds > 0f && Time.timeSinceLevelLoad < suppressOnSceneLoadSeconds)
        {
            LogVerbose($"Suppressing trigger for {suppressOnSceneLoadSeconds - Time.timeSinceLevelLoad:0.00}s after scene load.");
            return;
        }

        // 1) 세션 내 중복 방지
        if (triggerOnce && _fired)
        {
            LogVerbose("Already fired in this session.");
            return;
        }

        // 2) 목표 메시지 읽음?
        if (!IsTargetMessageRead())
        {
            LogVerbose($"Waiting: '{targetMessageName}' not read yet.");
            return;
        }

        // 3) 메신저 현재 꺼져 있는가?
        bool messengerOff = (messengerRoot == null) || !messengerRoot.activeInHierarchy;
        if (!messengerOff)
        {
            LogVerbose("Waiting: messenger is active.");
            return;
        }

        // 4) DataManager.nowPlayer.Starest_First_Visit == false 일 때만 허용
        if (!IsStarestFirstVisitFalse())
        {
            LogVerbose("Blocked: DataManager.nowPlayer.Starest_First_Visit is true (not first visit).");
            return;
        }

        // 5) 실행
        StartCoroutine(CoShowDialogueOnce());
    }

    IEnumerator CoShowDialogueOnce()
    {
        _fired = true;

        if (showDelay > 0f) yield return new WaitForSecondsRealtime(showDelay);

        _fullText = GetLocalizedNoticeText();
        if (replaceTokens) _fullText = ReplaceTokens(_fullText);

        OpenDialoguePanel();

        if (startTypingAfterOpenTween)
        {
            while (_isAnimPlaying) yield return null;
            StartTyping(_fullText);
        }
        else
        {
            StartTyping(_fullText);
        }
    }

    // ───────── 타이핑 ─────────
    private void StartTyping(string fullText)
    {
        if (dialogueText) dialogueText.text = string.Empty;
        if (_typingCo != null) StopCoroutine(_typingCo);
        _typingCo = StartCoroutine(CoTypewriter(fullText));
    }

    private IEnumerator CoTypewriter(string fullText)
    {
        _isTyping = true;

        if (!typeRichTextPerChar && dialogueText != null)
            dialogueText.richText = true;

        float delayPerChar = 1f / Mathf.Max(1f, charsPerSecond);

        if (string.IsNullOrEmpty(fullText))
        {
            _isTyping = false;
            _typingCo = null;
            yield break;
        }

        if (typeRichTextPerChar)
        {
            for (int i = 0; i < fullText.Length; i++)
            {
                if (!_isTyping) { _typingCo = null; yield break; }
                dialogueText.text += fullText[i];
                yield return new WaitForSecondsRealtime(delayPerChar);
            }
        }
        else
        {
            var sb = new System.Text.StringBuilder(fullText.Length);
            bool inTag = false;
            for (int i = 0; i < fullText.Length; i++)
            {
                if (!_isTyping) { _typingCo = null; yield break; }
                char c = fullText[i];
                sb.Append(c);
                if (c == '<') inTag = true;
                if (c == '>') inTag = false;

                dialogueText.text = sb.ToString();
                if (!inTag) yield return new WaitForSecondsRealtime(delayPerChar);
            }
        }

        _isTyping = false;
        _typingCo = null;
    }

    private void SkipTypewriterToEnd()
    {
        if (!_isTyping) return;
        if (_typingCo != null) { StopCoroutine(_typingCo); _typingCo = null; }
        _isTyping = false;
        if (dialogueText) dialogueText.text = _fullText;
        LogVerbose("Typewriter skipped to end.");
    }

    // ───────── 열림/닫힘 트윈 ─────────
    private void OpenDialoguePanel()
    {
        if (!dialoguePanel) return;

        if (!dialoguePanel.activeSelf) dialoguePanel.SetActive(true);
        _isShowing = true;

        // 패널 열릴 때: Map 가이드 플래그(옵션)
        if (mapMenu != null)
        {
            mapMenu.PlayerGoStarest = true;
            LogVerbose("Map flag set: PlayerGoStarest = true");
        }

        // ★ 플레이어 잠금
        if (lockPlayerWhileOpen) LockPlayers();

        if (_openTweenCo != null) StopCoroutine(_openTweenCo);
        if (_closeTweenCo != null) { StopCoroutine(_closeTweenCo); _closeTweenCo = null; }

        if (_animRT) _animRT.localScale = openStartScale;
        if (_cg && openUseAlphaFade)
        {
            _cg.alpha = openStartAlpha;
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
        }

        _openTweenCo = StartCoroutine(Co_OpenTween());
    }

    private void CloseDialoguePanel()
    {
        if (_typingCo != null) { StopCoroutine(_typingCo); _typingCo = null; }
        _isTyping = false;

        if (!dialoguePanel || !dialoguePanel.activeSelf)
        {
            _isShowing = false;
            // 패널이 이미 꺼져 있어도 잠금 해제는 보장
            if (_playersLocked) UnlockPlayers();
            return;
        }

        if (_closeTweenCo != null) StopCoroutine(_closeTweenCo);
        if (_openTweenCo != null) { StopCoroutine(_openTweenCo); _openTweenCo = null; }

        if (_animRT) _animRT.localScale = closeStartScale;
        if (_cg && closeUseAlphaFade)
        {
            _cg.alpha = closeStartAlpha;
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
        }

        _closeTweenCo = StartCoroutine(Co_CloseTween());
    }

    private IEnumerator Co_OpenTween()
    {
        _isAnimPlaying = true;
        float t = 0f, d = Mathf.Max(0.01f, openDuration);
        while (t < d)
        {
            float u = t / d; float e = 1f - Mathf.Pow(1f - u, 3f);
            if (_animRT) _animRT.localScale = Vector3.LerpUnclamped(openStartScale, openEndScale, e);
            if (_cg && openUseAlphaFade) _cg.alpha = Mathf.LerpUnclamped(openStartAlpha, openEndAlpha, e);
            t += Time.unscaledDeltaTime; yield return null;
        }
        if (_animRT) _animRT.localScale = openEndScale;
        if (_cg && openUseAlphaFade) { _cg.alpha = openEndAlpha; _cg.interactable = true; _cg.blocksRaycasts = true; }
        _isAnimPlaying = false; _openTweenCo = null;
    }

    private IEnumerator Co_CloseTween()
    {
        _isAnimPlaying = true;
        float t = 0f, d = Mathf.Max(0.01f, closeDuration);
        float startAlpha = _cg ? _cg.alpha : 1f;
        while (t < d)
        {
            float u = t / d; float e = Mathf.Pow(u, 3f);
            if (_animRT) _animRT.localScale = Vector3.LerpUnclamped(closeStartScale, closeEndScale, e);
            if (_cg && closeUseAlphaFade) _cg.alpha = Mathf.LerpUnclamped(startAlpha, closeEndAlpha, e);
            t += Time.unscaledDeltaTime; yield return null;
        }
        if (_animRT) _animRT.localScale = closeEndScale;
        if (_cg && closeUseAlphaFade) _cg.alpha = closeEndAlpha;

        if (dialoguePanel && dialoguePanel.activeSelf) dialoguePanel.SetActive(false);
        _isAnimPlaying = false; _isShowing = false; _closeTweenCo = null;

        // ★ 트윈 완료 후 잠금 해제
        if (_playersLocked) UnlockPlayers();

        LogInfo("Dialogue closed with tween.");
    }

    // ───────── 유틸 ─────────
    private bool IsTargetMessageRead()
    {
        var dm = DataManager.instance;
        var pd = dm != null ? dm.nowPlayer : null;
        if (pd == null) { LogWarn("DataManager.nowPlayer is null."); return false; }

        List<string> readList = pd.MessengerReadList;
        if (readList == null || readList.Count == 0) return false;

        for (int i = 0; i < readList.Count; i++)
            if (readList[i] == targetMessageName) return true;
        return false;
    }

    // DataManager.nowPlayer.Starest_First_Visit == false 일 때만 true
    private bool IsStarestFirstVisitFalse()
    {
        var dm = DataManager.instance;
        var pd = dm != null ? dm.nowPlayer : null;
        if (pd == null)
        {
            LogWarn("IsStarestFirstVisitFalse: DataManager.nowPlayer is null.");
            return false;
        }

        try
        {
            var t = pd.GetType();
            var f = t.GetField("Starest_First_Visit");
            if (f != null && f.FieldType == typeof(bool))
                return ((bool)f.GetValue(pd)) == false;

            var p = t.GetProperty("Starest_First_Visit");
            if (p != null && p.PropertyType == typeof(bool) && p.CanRead)
                return ((bool)p.GetValue(pd)) == false;

            LogWarn("IsStarestFirstVisitFalse: nowPlayer.Starest_First_Visit not found. Treat as false.");
            return false;
        }
        catch
        {
            LogWarn("IsStarestFirstVisitFalse: reflection error.");
            return false;
        }
    }

    private string GetLocalizedNoticeText()
    {
        string s = LocalizationSettings.StringDatabase.GetLocalizedString(localizationTable, noticeKey);
        if (string.IsNullOrEmpty(s))
        {
            LogWarn($"Localized text not found. table='{localizationTable}', key='{noticeKey}'");
            s = noticeKey; // 폴백
        }
        return s;
    }

    private string ReplaceTokens(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        string playerName = "No Name";
        var dm = DataManager.instance;
        if (dm != null && dm.nowPlayer != null && !string.IsNullOrEmpty(dm.nowPlayer.Name))
            playerName = dm.nowPlayer.Name;

        return System.Text.RegularExpressions.Regex.Replace(
            input, @"\{playerName\}", playerName,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private bool Pressed()
    {
        if (anyMouseButton)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) return true;
        }
        else if (Input.GetMouseButtonDown(0)) return true;

        if (Input.GetKeyDown(KeyCode.Space)) return true;
        for (int i = 0; i < extraKeys.Length; i++)
            if (Input.GetKeyDown(extraKeys[i])) return true;

        return false;
    }

    // ───────── 플레이어 잠금 구현 ─────────
    private void BuildPlayerList()
    {
        _cachedPlayers.Clear();

        if (autoFindPlayers)
        {
            var playerRoot = GameObject.FindGameObjectWithTag("Player");
            if (playerRoot)
            {
                var found = playerRoot.GetComponentsInChildren<PlayerMove>(true);
                if (found != null && found.Length > 0) _cachedPlayers.AddRange(found);
            }
        }

        if (extraPlayers != null && extraPlayers.Count > 0)
        {
            foreach (var p in extraPlayers)
            {
                if (p && !_cachedPlayers.Contains(p)) _cachedPlayers.Add(p);
            }
        }
    }

    private void LockPlayers()
    {
        if (_playersLocked) return;
        if (_cachedPlayers.Count == 0) BuildPlayerList();

        foreach (var pm in _cachedPlayers)
        {
            if (!pm) continue;
            pm.SetControlEnabled(false);
        }
        _playersLocked = true;
        LogVerbose("Players locked.");
    }

    private void UnlockPlayers()
    {
        foreach (var pm in _cachedPlayers)
        {
            if (!pm) continue;
            pm.SetControlEnabled(true);
        }
        _playersLocked = false;
        LogVerbose("Players unlocked.");
    }

    // ───────── 로깅 ─────────
    private bool LogEnabled(LogVerbosity level) => logLevel >= level && logLevel != LogVerbosity.Off;
    private void LogInfo(string msg) { if (LogEnabled(LogVerbosity.Info)) Debug.Log(logPrefix + msg, this); }
    private void LogVerbose(string msg) { if (LogEnabled(LogVerbosity.Verbose)) Debug.Log(logPrefix + msg, this); }
    private void LogWarn(string msg) { if (LogEnabled(LogVerbosity.Warnings)) Debug.LogWarning(logPrefix + msg, this); }
    private void LogError(string msg) { if (LogEnabled(LogVerbosity.Errors)) Debug.LogError(logPrefix + msg, this); }
}
