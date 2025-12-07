using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Settings;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class CallingSystem : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // 로깅 & 조건 클래스
    // ─────────────────────────────────────────────────────────────
    public enum LogVerbosity { Off, Errors, Warnings, Info, Verbose }
    [Header("로깅")] public LogVerbosity logLevel = LogVerbosity.Info;
    string Pfx => "[CallingSystem] ";
    bool L(LogVerbosity lv) => logLevel >= lv && logLevel != LogVerbosity.Off;
    void LogI(string m) { if (L(LogVerbosity.Info)) Debug.Log(Pfx + m); }
    void LogW(string m) { if (L(LogVerbosity.Warnings)) Debug.LogWarning(Pfx + m); }
    void LogE(string m) { if (L(LogVerbosity.Errors)) Debug.LogError(Pfx + m); }

    [Serializable]
    public class Condition
    {
        public enum VarType { Bool, Int, String, SceneName }
        public VarType varType = VarType.Bool;
        public string key;
        public bool boolValue = true;

        public enum IntOp { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual }
        public IntOp intOp = IntOp.Equal;
        public int intValue = 0;

        public enum StringOp { Equal, NotEqual, Contains, StartsWith, EndsWith }
        public StringOp stringOp = StringOp.Equal;
        public string stringValue = "";
        public bool stringIgnoreCase = true;

        public bool Evaluate()
        {
            var dm = DataManager.instance;
            switch (varType)
            {
                case VarType.Bool:
                    if (TryBool(dm?.nowPlayer, key, out var b)) return b == boolValue;
                    return false;
                case VarType.Int:
                    if (TryInt(dm?.nowPlayer, key, out var i))
                        return intOp switch
                        {
                            IntOp.Equal => i == intValue,
                            IntOp.NotEqual => i != intValue,
                            IntOp.Greater => i > intValue,
                            IntOp.GreaterOrEqual => i >= intValue,
                            IntOp.Less => i < intValue,
                            IntOp.LessOrEqual => i <= intValue,
                            _ => false
                        };
                    return false;
                case VarType.String:
                    if (TryString(dm?.nowPlayer, key, out var s))
                    {
                        var cmp = stringIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                        return stringOp switch
                        {
                            StringOp.Equal => string.Equals(s, stringValue, cmp),
                            StringOp.NotEqual => !string.Equals(s, stringValue, cmp),
                            StringOp.Contains => (s ?? "").IndexOf(stringValue ?? "", cmp) >= 0,
                            StringOp.StartsWith => (s ?? "").StartsWith(stringValue ?? "", cmp),
                            StringOp.EndsWith => (s ?? "").EndsWith(stringValue ?? "", cmp),
                            _ => false
                        };
                    }
                    return false;
                case VarType.SceneName:
                    var cur = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    var C = stringIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    return stringOp switch
                    {
                        StringOp.Equal => string.Equals(cur, stringValue, C),
                        StringOp.NotEqual => !string.Equals(cur, stringValue, C),
                        StringOp.Contains => cur.IndexOf(stringValue ?? "", C) >= 0,
                        StringOp.StartsWith => cur.StartsWith(stringValue ?? "", C),
                        StringOp.EndsWith => cur.EndsWith(stringValue ?? "", C),
                        _ => false
                    };
            }
            return false;
        }

        static bool TryBool(object o, string n, out bool v)
        {
            v = default; if (o == null) return false;
            var t = o.GetType(); var f = t.GetField(n);
            if (f != null && f.FieldType == typeof(bool)) { v = (bool)f.GetValue(o); return true; }
            var p = t.GetProperty(n);
            if (p != null && p.PropertyType == typeof(bool) && p.CanRead) { v = (bool)p.GetValue(o); return true; }
            return false;
        }
        static bool TryInt(object o, string n, out int v)
        {
            v = default; if (o == null) return false;
            var t = o.GetType(); var f = t.GetField(n);
            if (f != null && f.FieldType == typeof(int)) { v = (int)f.GetValue(o); return true; }
            var p = t.GetProperty(n);
            if (p != null && p.PropertyType == typeof(int) && p.CanRead) { v = (int)p.GetValue(o); return true; }
            return false;
        }
        static bool TryString(object o, string n, out string v)
        {
            v = default; if (o == null) return false;
            var t = o.GetType(); var f = t.GetField(n);
            if (f != null && f.FieldType == typeof(string)) { v = (string)f.GetValue(o); return true; }
            var p = t.GetProperty(n);
            if (p != null && p.PropertyType == typeof(string) && p.CanRead) { v = (string)p.GetValue(o); return true; }
            return false;
        }
    }

    [Serializable]
    public class CallDef
    {
        [Header("식별/표시")]
        public string callingName;      // 이벤트/테이블 접두
        public string callerName;       // 폴백 표시 이름
        public Sprite callerProfile;    // 발신자 아이콘

        [Header("조건(AND)")]
        public List<Condition> conditions = new();

        [Header("상태(디버그)")]
        public bool ringing = false;
        public bool answered = false;
        public bool firedOnce = false;
    }

    [Header("전화 정의들")]
    public List<CallDef> calls = new();

    // ─────────────────────────────────────────────────────────────
    // 아이콘/진동
    // ─────────────────────────────────────────────────────────────
    [Header("아이콘/버튼 자동연결")]
    public Button phoneIconButton;
    public string phoneIconButtonName = "Phone_icon_BT";

    [Header("아이콘 흔들기(진동)")]
    public bool shakeByRotation = true;
    public float shakeFrequency = 4.0f;
    public float shakeAmplitude = 8f;
    public AnimationCurve shakeEnvelope = AnimationCurve.Linear(0, 1, 1, 1);
    public float shakeOffscreenJitterMin = 0.15f;
    public float shakeOffscreenJitterMax = 0.35f;

    // ─────────────────────────────────────────────────────────────
    // Dialogue
    // ─────────────────────────────────────────────────────────────
    [Header("Dialogue 자동연결/활성")]
    public GameObject dialoguePanel;
    public string dialoguePanelName = "Dialogue";
    public DialogueRunnerStringTables dialogueRunner;
    public string dialogueManagerObjectName = "DialogueManager";

    [Header("통화용 폰트 오버라이드")]
    public bool usePhoneFontOverride = true;
    public float phoneBodyFontSize = 48f;
    public float phoneSpeakerFontSize = 40f;

    // ─────────────────────────────────────────────────────────────
    // PhonePanel
    // ─────────────────────────────────────────────────────────────
    [Header("PhonePanel UI")]
    public GameObject phonePanel;
    public RectTransform phone;
    public float slideFromY = -800f;
    public float slideToY = 0f;
    public float slideDuration = 0.35f;
    public AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    public Image callerProfileImage;
    public TMP_Text callerNameText;

    public Button hangUpButton;
    public Button answerButton;

    public GameObject selectObject;
    public GameObject callingObject;
    public TMP_Text callingTimeText;
    public GameObject callingEndObject;

    [Header("PhonePanel 활성 중 끄는 오브젝트들")]
    public GameObject[] disableWhilePhoneActive;

    [Header("탐색 옵션")]
    public bool autoFindInactive = true;

    // ─────────────────────────────────────────────────────────────
    // Player 제어
    // ─────────────────────────────────────────────────────────────
    [Header("Player Move Freeze (PhonePanel 활성 시)")]
    public PlayerMove playerMove;
    public bool autoFindPlayerMove = true;
    public bool includeInactiveOnFind = true;
    public bool hardLockWhilePhoneOpen = true;
    public bool alsoDisableAnimatorWhilePhoneOpen = true;

    bool _frozenByPhone = false;
    bool _prevControlEnabled = true;
    Animator _playerAnimator;
    bool _animWasEnabled = false;

    // ─────────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────────
    bool _autoBound;
    Coroutine _shakeLoop;
    Coroutine _slideCoroutine;

    RectTransform _iconRT;
    Vector2 _iconBasePos;
    float _iconBaseRot;

    int _currentCallIndex = -1;

    float _prevBodySize, _prevSpeakerSize;
    bool _prevUseLangSize;
    bool _fontsOverridden = false;

    bool _callingTimerOn = false;
    float _callingStartTime = 0f;

    void Awake() { AutoBindIfNeeded(); }
    void Start() { AutoBindIfNeeded(); }

    void OnDestroy()
    {
        if (dialogueRunner != null)
            dialogueRunner.OnDialogueEnded -= OnRunnerEnded;
    }

    // ─────────────────────────────────────────────────────────────
    // Update / LateUpdate
    // ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (_callingTimerOn && callingTimeText)
        {
            float t = Mathf.Max(0f, Time.time - _callingStartTime);
            int sec = Mathf.FloorToInt(t);
            callingTimeText.text = $"{sec / 60:D2}:{sec % 60:D2}";
        }

        for (int i = 0; i < calls.Count; i++)
        {
            var c = calls[i];
            if (c.ringing || c.answered || c.firedOnce) continue;

            bool ok = true;
            for (int j = 0; j < c.conditions.Count; j++)
                if (!c.conditions[j].Evaluate()) { ok = false; break; }

            if (ok) { BeginRinging(i); break; }
        }
    }

    void LateUpdate()
    {
        if (!hardLockWhilePhoneOpen || playerMove == null) return;

        bool phoneOpen = phonePanel && phonePanel.activeInHierarchy;

        if (phoneOpen)
        {
            if (!_frozenByPhone)
            {
                _prevControlEnabled = playerMove.controlEnabled;
                _frozenByPhone = true;
            }

            if (playerMove.controlEnabled) playerMove.controlEnabled = false;
            playerMove.Freeze();

            if (alsoDisableAnimatorWhilePhoneOpen)
            {
                if (_playerAnimator == null && playerMove)
                    _playerAnimator = playerMove.GetComponentInChildren<Animator>(true);

                if (_playerAnimator && _playerAnimator.enabled)
                {
                    _animWasEnabled = true;
                    _playerAnimator.enabled = false;
                }
            }
        }
        else if (_frozenByPhone)
        {
            playerMove.controlEnabled = _prevControlEnabled;
            playerMove.Unfreeze(keepAnimatorState: true);
            _frozenByPhone = false;

            if (alsoDisableAnimatorWhilePhoneOpen && _playerAnimator && _animWasEnabled)
            {
                _playerAnimator.enabled = true;
                _animWasEnabled = false;

                var st = _playerAnimator.GetCurrentAnimatorStateInfo(0);
                _playerAnimator.Play(st.shortNameHash, 0, 0f);
                _playerAnimator.speed = 0f;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // RING 시작
    // ─────────────────────────────────────────────────────────────
    void BeginRinging(int callIndex)
    {
        _currentCallIndex = callIndex;
        var call = calls[callIndex];
        call.ringing = true;
        call.answered = false;
        LogI($"RING RING… '{call.callingName}'");

        EnsurePhonePanelObjects();

        if (selectObject) selectObject.SetActive(true);
        if (callingObject) callingObject.SetActive(false);
        if (callingEndObject) callingEndObject.SetActive(false);

        _callingTimerOn = false;
        _callingStartTime = 0f;
        if (callingTimeText)
        {
            callingTimeText.text = "00:00";
            callingTimeText.gameObject.SetActive(false);
        }

        if (phoneIconButton == null) TryAutoFindPhoneButton();
        if (phoneIconButton)
        {
            var go = phoneIconButton.gameObject;
            if (!go.activeSelf) go.SetActive(true);
            StartShake();

            phoneIconButton.onClick.RemoveListener(OnPhoneIconClicked_Internal);
            phoneIconButton.onClick.AddListener(OnPhoneIconClicked_Internal);
        }
    }

    void OnPhoneIconClicked_Internal()
    {
        if (_currentCallIndex < 0 || _currentCallIndex >= calls.Count) return;
        var call = calls[_currentCallIndex];
        if (!call.ringing) return;

        if (phoneIconButton) phoneIconButton.gameObject.SetActive(false);
        StopShake();

        EnsurePhonePanelObjects();
        SetupPhonePanel(call);

        ShowPhonePanel();
    }

    void SetupPhonePanel(CallDef call)
    {
        if (callerProfileImage) callerProfileImage.sprite = call.callerProfile;

        string localizedCaller = null;
        try
        {
            localizedCaller = LocalizationSettings.StringDatabase
                .GetLocalizedString($"{call.callingName}_Dialogue", "CallerName");
        }
        catch { }

        if (callerNameText)
            callerNameText.text = !string.IsNullOrEmpty(localizedCaller) ? localizedCaller : call.callerName;

        if (selectObject) selectObject.SetActive(true);
        if (callingObject) callingObject.SetActive(false);
        if (callingEndObject) callingEndObject.SetActive(false);
        if (callingTimeText) { callingTimeText.text = "00:00"; callingTimeText.gameObject.SetActive(false); }

        if (hangUpButton)
        {
            hangUpButton.onClick.RemoveAllListeners();
            hangUpButton.onClick.AddListener(OnClickHangUp);
        }
        if (answerButton)
        {
            answerButton.onClick.RemoveAllListeners();
            answerButton.onClick.AddListener(OnClickAnswer);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 패널 Show / Hide
    // ─────────────────────────────────────────────────────────────
    void ShowPhonePanel()
    {
        if (!phonePanel || !phone) return;

        SetExtraObjectsActive(false);
        phonePanel.SetActive(true);

        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(CoSlide(phone, slideFromY, slideToY, slideDuration, keepActiveAtEnd: true));
    }

    void HidePhonePanelAndDeactivate()
    {
        if (!phonePanel || !phone) return;

        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(CoSlide(phone, slideToY, slideFromY, slideDuration, keepActiveAtEnd: false));
    }

    IEnumerator CoSlide(RectTransform rt, float fromY, float toY, float dur, bool keepActiveAtEnd)
    {
        Vector2 a = rt.anchoredPosition; a.y = fromY; rt.anchoredPosition = a;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = dur > 0f ? Mathf.Clamp01(t / dur) : 1f;
            float e = slideCurve != null ? slideCurve.Evaluate(k) : k;
            float y = Mathf.LerpUnclamped(fromY, toY, e);
            var pos = rt.anchoredPosition; pos.y = y; rt.anchoredPosition = pos;
            yield return null;
        }
        var posF = rt.anchoredPosition; posF.y = toY; rt.anchoredPosition = posF;

        if (!keepActiveAtEnd)
        {
            phonePanel.SetActive(false);
            SetExtraObjectsActive(true);
        }
        _slideCoroutine = null;
    }

    void OnClickHangUp()
    {
        HidePhonePanelAndDeactivate();

        if (_currentCallIndex >= 0 && _currentCallIndex < calls.Count)
        {
            var call = calls[_currentCallIndex];
            call.ringing = true;
            call.answered = false;
            call.firedOnce = false;
        }

        StartCoroutine(CoReenableIconWithShake(1f));
    }

    void OnClickAnswer()
    {
        if (_currentCallIndex < 0 || _currentCallIndex >= calls.Count) return;
        var call = calls[_currentCallIndex];
        if (!call.ringing) return;

        call.answered = true;
        call.ringing = false;

        if (selectObject) selectObject.SetActive(false);
        if (callingObject) callingObject.SetActive(true);
        if (callingTimeText) { callingTimeText.gameObject.SetActive(true); callingTimeText.text = "00:00"; }

        _callingStartTime = Time.time;
        _callingTimerOn = true;

        EnsureDialogueObjects();
        if (!dialogueRunner) { LogE("DialogueRunnerStringTables not found."); return; }

        ApplyPhoneFontOverrideIfNeeded();

        dialogueRunner.OnDialogueEnded -= OnRunnerEnded;
        dialogueRunner.OnDialogueEnded += OnRunnerEnded;

        if (dialoguePanel && !dialoguePanel.activeSelf) dialoguePanel.SetActive(true);
        dialogueRunner.gameObject.SetActive(true);
        dialogueRunner.BeginWithEventName(call.callingName);

        call.firedOnce = true;
    }

    void OnRunnerEnded()
    {
        RestoreFontsIfOverridden();

        if (dialoguePanel && dialoguePanel.activeSelf)
            dialoguePanel.SetActive(false);

        _callingTimerOn = false;

        if (callingEndObject) callingEndObject.SetActive(true);

        StopAllCoroutines();
        StartCoroutine(CoEndAndDismissPhone());
    }

    IEnumerator CoEndAndDismissPhone()
    {
        // 1초 대기
        yield return new WaitForSecondsRealtime(1f);

        // 입력 대기 (최대 0.5초)
        float timeout = 0.5f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (Input.anyKeyDown ||
                Input.GetMouseButtonDown(0) ||
                (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
            {
                break;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // 패널 내려가기
        HidePhonePanelAndDeactivate();

        // 슬라이드 시간만큼 대기
        yield return new WaitForSecondsRealtime(slideDuration);

        // 미션 패널 및 후속 처리
        if (_currentCallIndex >= 0 && _currentCallIndex < calls.Count)
        {
            var endedCall = calls[_currentCallIndex];

            // 1) 미션 패널 호출
            if (endedCall != null && MissionPanel.Instance != null)
            {
                MissionPanel.Instance.ShowByKey(endedCall.callingName);
            }

            // 2) Boss_First_Calling 처리
            if (endedCall != null && string.Equals(endedCall.callingName, "Boss_First_Calling", StringComparison.Ordinal))
            {
                var dm = DataManager.instance;
                if (dm?.nowPlayer != null)
                {
                    dm.nowPlayer.StartGame = true;
                    try { dm.CommitDataToTempFile(); } catch { }
                    LogI("StartGame -> true (after Boss_First_Calling)");
                }

#if UNITY_2023_1_OR_NEWER
                var map = UnityEngine.Object.FindAnyObjectByType<MapMenuController>(FindObjectsInactive.Include);
#else
                var map = UnityEngine.Object.FindObjectOfType<MapMenuController>(true);
#endif
                if (map != null)
                {
                    map.PlayerGoStarest = true;
                    LogI("MapMenuController.PlayerGoStarest -> true");
                }
            }
            // 3) Boss_Second_Calling 처리: 버스 도착 후 이벤트 예약
            else if (endedCall != null && string.Equals(endedCall.callingName, "Boss_Second_Calling", StringComparison.Ordinal))
            {
                MapMenuController.PendingNpcEventKeyAfterArrival = "Boss_Seconday_Busstop";
                LogI("Boss_Second_Calling ended -> schedule 'Boss_Seconday_Busstop' after bus arrival.");
            }
        }

        _currentCallIndex = -1;
    }

    // ─────────────────────────────────────────────────────────────
    // 기타 유틸 및 폰트 오버라이드
    // ─────────────────────────────────────────────────────────────
    void ApplyPhoneFontOverrideIfNeeded()
    {
        if (!usePhoneFontOverride || dialogueRunner == null) return;

        _prevBodySize = dialogueRunner.bodyFontSize;
        _prevSpeakerSize = dialogueRunner.speakerFontSize;
        _prevUseLangSize = dialogueRunner.useLanguageFontSizes;

        dialogueRunner.useLanguageFontSizes = false;
        dialogueRunner.bodyFontSize = phoneBodyFontSize;
        dialogueRunner.speakerFontSize = phoneSpeakerFontSize;
        _fontsOverridden = true;

        if (dialogueRunner.bodyText)
        {
            dialogueRunner.bodyText.enableAutoSizing = false;
            dialogueRunner.bodyText.fontSize = dialogueRunner.bodyFontSize;
        }
        if (dialogueRunner.speakerText)
        {
            dialogueRunner.speakerText.enableAutoSizing = false;
            dialogueRunner.speakerText.fontSize = dialogueRunner.speakerFontSize;
        }
    }

    void RestoreFontsIfOverridden()
    {
        if (!_fontsOverridden || dialogueRunner == null) return;

        dialogueRunner.useLanguageFontSizes = _prevUseLangSize;
        dialogueRunner.bodyFontSize = _prevBodySize;
        dialogueRunner.speakerFontSize = _prevSpeakerSize;

        if (dialogueRunner.bodyText) dialogueRunner.bodyText.fontSize = dialogueRunner.bodyFontSize;
        if (dialogueRunner.speakerText) dialogueRunner.speakerText.fontSize = dialogueRunner.speakerFontSize;

        _fontsOverridden = false;
    }

    void StartShake()
    {
        if (!phoneIconButton) return;

        _iconRT = (phoneIconButton.targetGraphic ? phoneIconButton.targetGraphic.rectTransform
                                                 : phoneIconButton.GetComponent<RectTransform>());
        if (!_iconRT) return;

        _iconBasePos = _iconRT.anchoredPosition;
        _iconBaseRot = _iconRT.localEulerAngles.z;

        if (_shakeLoop != null) StopCoroutine(_shakeLoop);
        _shakeLoop = StartCoroutine(CoShakeLoop());
    }

    void StopShake()
    {
        if (_shakeLoop != null) { StopCoroutine(_shakeLoop); _shakeLoop = null; }
        if (_iconRT)
        {
            _iconRT.anchoredPosition = _iconBasePos;
            var e = _iconRT.localEulerAngles; e.z = _iconBaseRot; _iconRT.localEulerAngles = e;
        }
    }

    IEnumerator CoShakeLoop()
    {
        while (HasAnyRinging())
        {
            float t = 0f;
            while (t < 1f && HasAnyRinging())
            {
                t += Time.unscaledDeltaTime;
                float phase = t * shakeFrequency * Mathf.PI * 2f;
                float env = shakeEnvelope != null ? Mathf.Clamp01(shakeEnvelope.Evaluate(t)) : 1f;
                float s = Mathf.Sin(phase) * shakeAmplitude * env;

                if (shakeByRotation)
                {
                    var e = _iconRT.localEulerAngles; e.z = _iconBaseRot + s; _iconRT.localEulerAngles = e;
                }
                else
                {
                    var p = _iconRT.anchoredPosition; p.x = _iconBasePos.x + s; _iconRT.anchoredPosition = p;
                }
                yield return null;
            }

            float wait = UnityEngine.Random.Range(shakeOffscreenJitterMin, shakeOffscreenJitterMax);
            float end = Time.unscaledTime + Mathf.Max(0f, wait);
            while (Time.unscaledTime < end && HasAnyRinging()) yield return null;
        }

        StopShake();
    }

    bool HasAnyRinging()
    {
        for (int i = 0; i < calls.Count; i++)
            if (calls[i].ringing && !calls[i].answered) return true;
        return false;
    }

    IEnumerator CoReenableIconWithShake(float delaySec)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delaySec));
        if (phoneIconButton)
        {
            phoneIconButton.gameObject.SetActive(true);
            StartShake();
        }
    }

    void AutoBindIfNeeded()
    {
        if (_autoBound) return;
        TryAutoFindPhoneButton();
        TryAutoFindDialogueObjects();
        TryAutoFindPhonePanelObjects();
        EnsurePlayerMove();
        _autoBound = true;
    }

    void TryAutoFindPhoneButton()
    {
        if (phoneIconButton) return;
        var all = Resources.FindObjectsOfTypeAll<Button>();
        foreach (var b in all)
        {
            if (!b || b.name != phoneIconButtonName) continue;
            if (!b.gameObject.scene.IsValid()) continue;
            phoneIconButton = b;
            LogI($"Phone button auto-bound → {GetPath(b.transform)}");
            break;
        }
    }

    void TryAutoFindDialogueObjects() => EnsureDialogueObjects();

    void EnsureDialogueObjects()
    {
        if (!dialoguePanel)
        {
            var go = FindActiveInScene(dialoguePanelName) ?? FindByHierarchy("Dialogue UI/Dialogue");
            if (go) { dialoguePanel = go; LogI($"Dialogue panel auto-bound → {GetPath(go.transform)}"); }
        }

        if (!dialogueRunner)
        {
            GameObject mngrGO = FindActiveInScene(dialogueManagerObjectName) ?? FindByHierarchy("Dialogue UI/DialogueManager");
            if (mngrGO)
            {
                dialogueRunner = mngrGO.GetComponent<DialogueRunnerStringTables>()
                                ?? mngrGO.GetComponentInChildren<DialogueRunnerStringTables>(true);
                LogI($"DialogueManager auto-bound → {GetPath(mngrGO.transform)}");
            }
        }
    }

    void TryAutoFindPhonePanelObjects() => EnsurePhonePanelObjects();

    void EnsurePhonePanelObjects()
    {
        if (!phonePanel) phonePanel = FindActiveInScene("PhonePanel");
        if (phonePanel && !phone)
        {
            var t = phonePanel.transform.Find("Phone");
            if (t) phone = t as RectTransform;
        }
    }

    void EnsurePlayerMove()
    {
        if (playerMove || !autoFindPlayerMove) return;

#if UNITY_2023_1_OR_NEWER
        playerMove = includeInactiveOnFind
            ? UnityEngine.Object.FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Include)
            : UnityEngine.Object.FindFirstObjectByType<PlayerMove>(FindObjectsInactive.Exclude);
#else
        playerMove = includeInactiveOnFind
            ? UnityEngine.Object.FindObjectOfType<PlayerMove>()
            : UnityEngine.Object.FindObjectOfType<PlayerMove>();
#endif

        if (playerMove)
        {
            LogI($"PlayerMove auto-bound → {GetPath(playerMove.transform)}");
            _playerAnimator = playerMove.GetComponentInChildren<Animator>(true);
        }
    }

    void SetExtraObjectsActive(bool active)
    {
        if (disableWhilePhoneActive == null) return;
        for (int i = 0; i < disableWhilePhoneActive.Length; i++)
            if (disableWhilePhoneActive[i]) disableWhilePhoneActive[i].SetActive(active);
    }

    static GameObject FindActiveInScene(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var go in all)
        {
            if (!go || go.name != name) continue;
            if (!go.scene.IsValid()) continue;
            return go;
        }
        return null;
    }

    static GameObject FindByHierarchy(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var parts = path.Split('/');
        Transform cur = null;
        var roots = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var r in roots)
        {
            if (!r || !r.gameObject.scene.IsValid()) continue;
            if (r.parent != null) continue;
            if (r.name != parts[0]) continue;

            cur = r;
            for (int i = 1; i < parts.Length && cur; i++)
                cur = cur.Find(parts[i]);

            if (cur) return cur.gameObject;
        }
        return null;
    }

    static string GetPath(Transform t)
    {
        if (!t) return "(null)";
        var st = new Stack<string>();
        while (t) { st.Push(t.name); t = t.parent; }
        return string.Join("/", st);
    }
}
