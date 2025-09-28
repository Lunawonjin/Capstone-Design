using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class Command_Input : MonoBehaviour
{
    [Header("플레이어/교주")]
    public GameObject Player;
    public GameObject Leader;
    public float playerMaxHP = 100f, leaderMaxHP = 100f;
    public float playerHP = 100f, leaderHP = 100f;
    [Range(0.05f, 0.5f)] public float lowHPThreshold = 0.3f;

    [Header("UI / 커맨드 라인")]
    public RectTransform commandPanel;     // 슬롯 부모
    public Image commandBlockPrefab;       // 동그란 BG 프리팹(Image)

    [Header("방향별 색(인스펙터 변경)")]
    public Color colorUp = new Color(1.00f, 0.40f, 0.40f, 1f);
    public Color colorDown = new Color(1.00f, 0.75f, 0.30f, 1f);
    public Color colorLeft = new Color(0.60f, 1.00f, 0.50f, 1f);
    public Color colorRight = new Color(0.40f, 0.85f, 1.00f, 1f);

    [Header("슬롯 배치/스타일")]
    [Range(0f, 1f)] public float slotColorAlpha = 1f; // 슬롯 배경 투명도(방향색 기반)
    public float slotPaddingX = 80f;
    public float minSlotSpacing = 40f;
    public float spawnScaleTime = 0.2f;

    [Header("화살표 스프라이트")]
    public Sprite arrowUp, arrowDown, arrowLeft, arrowRight;

    [Header("이펙트 프리팹")]
    public ParticleSystem sparklePrefab; // 정답 스파클
    public Image wrongXPrefab;           // 오답 X(Image 프리팹)

    [Header("카메라")]
    public Camera MainCamera, DirectingCamera;

    [Header("승리/패배 연출")]
    public GameObject[] columns;

    // ====== 슬라이더 UI ======
    [Header("슬라이더 UI")]
    public Slider leaderHPSlider;  // 0~1
    public Slider timeSlider;      // 0~1
    public float sliderSmooth = 12f;

    // ====== 스테이지 플랜만 사용 ======
    [Header("스테이지 플랜(순서대로 진행)")]
    public bool loopStagePlan = false;
    [System.Serializable]
    public class StageEntry
    {
        [Range(5, 12)] public int count = 5;   // 커맨드 개수
        [Min(1)] public int repeats = 1; // 반복 횟수
        public bool customTime = false;       // 시간 오버라이드?
        [Min(0.1f)] public float time = 5f; // customTime=true일 때 사용
    }
    public List<StageEntry> stagePlan = new List<StageEntry>() {
        new StageEntry{count=5, repeats=3},
        new StageEntry{count=6, repeats=2},
    };
    [Tooltip("전역 난이도 배수(시간에 곱)")]
    public float difficultyTimeScale = 1f;

    [Header("피해/패널티")]
    public float playerDamageOnTimeout = 15f;
    public float leaderDamageOnSuccess = 20f;
    public float wrongPenaltyTime = 0.5f;

    [Header("카메라 옵션")]
    public float shakeIntensity = 8f;
    public float shakeTime = 0.08f;

    // ====== URP Vignette + Color Flash ======
    [Header("Vignette (URP)")]
    public Volume globalVolume;                   // 없으면 런타임 생성
    public Color damageColor = new Color(1f, 0.1f, 0.1f, 1f);
    public Color holyColor = new Color(1f, 0.95f, 0.5f, 1f);

    [Tooltip("일반 블링크 피크(진하기)")]
    [Range(0f, 1f)] public float blinkPeakNormal = 0.9f;

    [Tooltip("플레이어 HP 30%↓일 때 데미지 블링크 피크")]
    [Range(0f, 1f)] public float blinkPeakLowHP = 1.0f;

    [Tooltip("블링크가 사라지는 시간(길수록 오래 감)")]
    [Min(0.05f)] public float blinkFadeTime = 1.6f;

    [Tooltip("저HP 지속 세기(30%↓ 또는 성스러움 우선 조건일 때 유지)")]
    [Range(0f, 1f)] public float holdIntensity = 0.6f;

    [Tooltip("비네트 모서리 부드러움(URP)")]
    [Range(0f, 1f)] public float vignetteSmoothness = 0.7f;
    public bool vignetteRounded = true;

    [Header("Color Flash (URP ColorAdjustments)")]
    public bool useColorFlash = true;
    [Range(0f, 1f)] public float flashAmountNormal = 0.45f; // 일반 플래시 세기
    [Range(0f, 1f)] public float flashAmountLowHP = 0.70f;  // 30%↓일 때 플래시 세기
    [Min(0.05f)] public float flashFadeTime = 0.7f;      // 플래시 사라지는 시간
    public float flashExposure = -0.3f;                  // 플래시 때 노출 보정(살짝 어둡게)

    // 내부 상태
    private float phaseTimer, phaseTimeMax;
    private int phaseIndex = 0;
    private int currentCommandCount;
    private readonly List<CommandDir> currentSequence = new List<CommandDir>();
    private readonly List<SlotUI> slots = new List<SlotUI>();
    private int inputCursor = 0;
    private bool inputLocked = false;
    private Vector3 mainCamOriginalPos;
    private float hpSliderVal = 1f, timeSliderVal = 1f;

    // Stage Plan 진행 포인터
    private int planIndex = 0;
    private int planRepeatProgress = 0;

    private enum CommandDir { Up, Down, Left, Right }
    private class SlotUI { public Image bg, arrow; }

    // Post FX 핸들
    private Vignette _vignette;
    private ColorAdjustments _colorAdj;
    private Coroutine _blinkCR;
    private bool _isBlinking;

    private void Awake()
    {
        if (MainCamera != null) mainCamOriginalPos = MainCamera.transform.localPosition;
        EnsurePostFX();
        SetVignette(0f, damageColor); // 시작은 꺼둠
        if (useColorFlash) SetColorFlash(0f, Color.white);
    }

    private void Start()
    {
        if (leaderHPSlider != null) { leaderHPSlider.minValue = 0; leaderHPSlider.maxValue = 1; leaderHPSlider.value = leaderHP / Mathf.Max(0.0001f, leaderMaxHP); hpSliderVal = leaderHPSlider.value; }
        if (timeSlider != null) { timeSlider.minValue = 0; timeSlider.maxValue = 1; timeSlider.value = 1; timeSliderVal = 1; }

        StartNewPhase();
    }

    private void Update()
    {
        if (playerHP <= 0f || leaderHP <= 0f) return;

        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f) { OnPhaseTimeout(); return; }

        if (!inputLocked)
        {
            var p = GetPressedArrow();
            if (p.HasValue) EvaluateInput(p.Value);
        }

        UpdateSliders();
        UpdateVignettePersistent();
    }

    // ====== Post FX ======
    private void EnsurePostFX()
    {
        if (globalVolume == null)
        {
            globalVolume = FindObjectOfType<Volume>();
            if (globalVolume == null)
            {
                var go = new GameObject("Global Volume (Auto)");
                globalVolume = go.AddComponent<Volume>();
                globalVolume.isGlobal = true;
                globalVolume.priority = 999f;
                globalVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            }
        }
        var profile = globalVolume.profile ?? (globalVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>());

        if (!profile.TryGet(out _vignette))
            _vignette = profile.Add<Vignette>();
        _vignette.active = true;
        _vignette.intensity.overrideState = true;
        _vignette.smoothness.overrideState = true;
        _vignette.rounded.overrideState = true;
        _vignette.color.overrideState = true;
        _vignette.smoothness.value = vignetteSmoothness;
        _vignette.rounded.value = vignetteRounded;

        if (!profile.TryGet(out _colorAdj))
            _colorAdj = profile.Add<ColorAdjustments>();
        _colorAdj.active = true;
        _colorAdj.colorFilter.overrideState = true;
        _colorAdj.postExposure.overrideState = true;
        _colorAdj.colorFilter.value = Color.white;
        _colorAdj.postExposure.value = 0f;
    }

    private void SetVignette(float intensity, Color color)
    {
        if (_vignette == null) return;
        _vignette.color.value = color;
        _vignette.intensity.value = Mathf.Clamp01(intensity);
    }

    private void SetColorFlash(float amt, Color col)
    {
        if (!useColorFlash || _colorAdj == null) return;
        amt = Mathf.Clamp01(amt);
        _colorAdj.colorFilter.value = Color.Lerp(Color.white, col, amt);
        _colorAdj.postExposure.value = Mathf.Lerp(0f, flashExposure, amt);
    }

    private void BlinkVignette(Color color, float peak, bool treatAsDamage)
    {
        if (_vignette == null) return;
        if (_blinkCR != null) StopCoroutine(_blinkCR);
        _blinkCR = StartCoroutine(VignetteBlinkRoutine(color, peak, treatAsDamage));
    }

    private IEnumerator VignetteBlinkRoutine(Color color, float peak, bool treatAsDamage)
    {
        _isBlinking = true;
        _vignette.color.value = color;
        _vignette.intensity.value = peak; // 바로 피크

        // 컬러 플래시 피크
        float flashPeak = (treatAsDamage && IsPlayerLow()) ? flashAmountLowHP : flashAmountNormal;
        SetColorFlash(flashPeak, color);

        float t = 0f;
        while (t < blinkFadeTime)
        {
            t += Time.deltaTime;

            bool holdDamage = IsPlayerLowOnly();
            bool holdHoly = IsLeaderLow() || (IsPlayerLow() && IsLeaderLow());

            float target = treatAsDamage
                ? (holdDamage ? holdIntensity : Mathf.Lerp(peak, 0f, t / blinkFadeTime))
                : (holdHoly ? holdIntensity : Mathf.Lerp(peak, 0f, t / blinkFadeTime));

            // 비네트 진하기 보간
            _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, target, 0.45f);

            // 컬러 플래시도 함께 감쇠/유지
            float flashTarget = treatAsDamage
                ? (holdDamage ? holdIntensity : Mathf.Lerp(flashPeak, 0f, t / flashFadeTime))
                : (holdHoly ? holdIntensity : Mathf.Lerp(flashPeak, 0f, t / flashFadeTime));
            SetColorFlash(flashTarget, color);

            yield return null;
        }

        // 유지 조건이 없으면 원상 복귀
        if (!(IsPlayerLow() || IsLeaderLow()))
            SetColorFlash(0f, Color.white);

        _isBlinking = false;
    }

    private bool IsPlayerLow() => playerHP <= playerMaxHP * lowHPThreshold;
    private bool IsLeaderLow() => leaderHP <= leaderMaxHP * lowHPThreshold;
    private bool IsPlayerLowOnly() => IsPlayerLow() && !IsLeaderLow();

    private void UpdateVignettePersistent()
    {
        if (_vignette == null || _isBlinking) return;

        if (IsPlayerLow() && IsLeaderLow())
        {
            _vignette.color.value = holyColor;
            _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, holdIntensity, Time.deltaTime * 6f);
            SetColorFlash(holdIntensity, holyColor);
            return;
        }
        if (IsPlayerLowOnly())
        {
            _vignette.color.value = damageColor;
            _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, holdIntensity, Time.deltaTime * 6f);
            SetColorFlash(holdIntensity, damageColor);
            return;
        }
        if (IsLeaderLow())
        {
            _vignette.color.value = holyColor;
            _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, holdIntensity, Time.deltaTime * 6f);
            SetColorFlash(holdIntensity, holyColor);
            return;
        }

        _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, 0f, Time.deltaTime * 4f);
        if (useColorFlash && _colorAdj != null)
        {
            _colorAdj.colorFilter.value = Color.Lerp(_colorAdj.colorFilter.value, Color.white, Time.deltaTime * 4f);
            _colorAdj.postExposure.value = Mathf.Lerp(_colorAdj.postExposure.value, 0f, Time.deltaTime * 4f);
        }
    }

    // ====== Sliders ======
    private void UpdateSliders()
    {
        if (leaderHPSlider != null)
        {
            float target = leaderHP / Mathf.Max(0.0001f, leaderMaxHP);
            hpSliderVal = Mathf.Lerp(hpSliderVal, target, Time.deltaTime * sliderSmooth);
            leaderHPSlider.value = hpSliderVal;
        }
        if (timeSlider != null)
        {
            float target = (phaseTimeMax > 0f) ? Mathf.Clamp01(phaseTimer / phaseTimeMax) : 0f;
            timeSliderVal = Mathf.Lerp(timeSliderVal, target, Time.deltaTime * sliderSmooth);
            timeSlider.value = timeSliderVal;
        }
    }

    // ====== Phase ======
    private void StartNewPhase()
    {
        phaseIndex++;
        inputCursor = 0;
        inputLocked = true;

        ChooseFromStagePlan(out int count, out float time);
        currentCommandCount = count;

        float t = Mathf.Max(0.1f, time * Mathf.Max(0.01f, difficultyTimeScale));
        phaseTimer = t;
        phaseTimeMax = t;
        if (timeSlider != null) { timeSlider.value = 1f; timeSliderVal = 1f; }

        GenerateSequence(currentCommandCount);
        BuildSlotLineAndFill(currentCommandCount);

        StartCoroutine(UnlockInputAfterSpawn());
    }

    private void ChooseFromStagePlan(out int count, out float time)
    {
        if (stagePlan == null || stagePlan.Count == 0)
        {
            count = 5; time = 5f; return;
        }

        var e = stagePlan[Mathf.Clamp(planIndex, 0, stagePlan.Count - 1)];
        count = Mathf.Clamp(e.count, 5, 12);
        time = e.customTime ? e.time : DefaultTimeForCount(count);

        planRepeatProgress++;
        if (planRepeatProgress >= Mathf.Max(1, e.repeats))
        {
            planRepeatProgress = 0;
            planIndex++;
            if (planIndex >= stagePlan.Count)
                planIndex = loopStagePlan ? 0 : stagePlan.Count - 1; // 루프 Off면 마지막 단계 유지
        }
    }

    private float DefaultTimeForCount(int count)
    {
        switch (Mathf.Clamp(count, 5, 12))
        {
            case 5: return 5.0f;
            case 6: return 4.6f;
            case 7: return 4.2f;
            case 8: return 3.8f;
            case 9: return 3.5f;
            case 10: return 3.2f;
            case 11: return 3.0f;
            case 12: return 2.8f;
        }
        return 3.0f;
    }

    private IEnumerator UnlockInputAfterSpawn()
    {
        yield return new WaitForSeconds(spawnScaleTime);
        inputLocked = false;
    }

    private void GenerateSequence(int count)
    {
        currentSequence.Clear();
        for (int i = 0; i < count; i++) currentSequence.Add((CommandDir)Random.Range(0, 4));
    }

    // ====== Slots ======
    private void BuildSlotLineAndFill(int count)
    {
        CleanupSlots();
        if (commandPanel == null || commandBlockPrefab == null) return;

        float panelW = commandPanel.rect.width;
        float usableW = Mathf.Max(0f, panelW - slotPaddingX * 2f);
        float spacing = (count <= 1) ? 0f : Mathf.Max(minSlotSpacing, usableW / (count - 1));
        float startX = -usableW * 0.5f;
        float y = 0f;

        for (int i = 0; i < count; i++)
        {
            // BG
            Image bg = Instantiate(commandBlockPrefab, commandPanel);
            bg.name = $"SlotBG_{i}";
            bg.rectTransform.anchoredPosition = new Vector2(startX + spacing * i, y);
            bg.transform.localScale = Vector3.one * 0.2f;
            bg.color = GetDirColor(currentSequence[i], slotColorAlpha); // 슬롯=화살표색

            // 화살표
            var arrowGO = new GameObject($"Arrow_{i}", typeof(RectTransform), typeof(Image));
            arrowGO.transform.SetParent(bg.transform, false);
            var ar = (RectTransform)arrowGO.transform;
            ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 0.5f);
            ar.sizeDelta = bg.rectTransform.sizeDelta * 0.62f;
            ar.anchoredPosition = Vector2.zero;
            var arrowImg = arrowGO.GetComponent<Image>();
            arrowImg.preserveAspect = true;
            arrowImg.sprite = GetArrowSprite(currentSequence[i]);
            arrowImg.color = GetDirColor(currentSequence[i], 1f);

            StartCoroutine(ScaleUp(bg.transform, spawnScaleTime));
            slots.Add(new SlotUI { bg = bg, arrow = arrowImg });
        }
    }

    // ====== Input ======
    private CommandDir? GetPressedArrow()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow)) return CommandDir.Up;
        if (Input.GetKeyDown(KeyCode.DownArrow)) return CommandDir.Down;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) return CommandDir.Left;
        if (Input.GetKeyDown(KeyCode.RightArrow)) return CommandDir.Right;
        return null;
    }

    private void EvaluateInput(CommandDir input)
    {
        if (inputCursor >= currentSequence.Count) return;

        var target = currentSequence[inputCursor];
        var slot = slots[inputCursor];

        if (input == target)
        {
            OnCorrect(slot);
            inputCursor++;
            if (inputCursor >= currentSequence.Count) OnPhaseClear();
        }
        else
        {
            OnWrong(slot);
            phaseTimer = Mathf.Max(0f, phaseTimer - wrongPenaltyTime);
        }
    }

    private void OnCorrect(SlotUI slot)
    {
        if (slot == null) return;

        // 스파클은 슬롯 자식이 아닌 패널 밑으로 생성해 안전하게 유지
        if (sparklePrefab != null)
        {
            var fx = Instantiate(sparklePrefab, slot.bg.rectTransform.position, Quaternion.identity, commandPanel);
            fx.Play(); Destroy(fx.gameObject, 2f);
        }

        StartCoroutine(DisableArrowAfter(slot, 0.05f)); // 화살표만 꺼짐(배경색 유지)
    }

    private IEnumerator DisableArrowAfter(SlotUI slot, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (slot != null && slot.arrow != null) slot.arrow.enabled = false;
    }

    private void OnWrong(SlotUI slot)
    {
        if (wrongXPrefab != null && slot != null)
        {
            Image x = Instantiate(wrongXPrefab, slot.bg.transform);
            x.gameObject.SetActive(true); x.enabled = true;
            var xr = x.rectTransform;
            xr.anchorMin = xr.anchorMax = new Vector2(0.5f, 0.5f);
            xr.anchoredPosition = Vector2.zero;
            xr.sizeDelta = slot.bg.rectTransform.sizeDelta * 0.9f;
            x.preserveAspect = true; x.raycastTarget = false;
            var c = x.color; c.a = 1f; x.color = c;

            // 슬롯이 파괴되어도 안전하도록 패널로 분리
            x.transform.SetParent(commandPanel, true);
            x.transform.SetAsLastSibling();

            StartCoroutine(FadeAndDestroy(x, 0.25f));
        }
        if (MainCamera != null) StartCoroutine(ShakeCamera(MainCamera, shakeTime, shakeIntensity));
    }

    // ====== Results ======
    private void OnPhaseTimeout()
    {
        playerHP = Mathf.Max(0f, playerHP - playerDamageOnTimeout);

        float peak = IsPlayerLow() ? blinkPeakLowHP : blinkPeakNormal;
        BlinkVignette(damageColor, peak, true);

        if (playerHP <= 0f) { StartCoroutine(PlayPlayerDefeat()); return; }
        StartNewPhase();
    }

    private void OnPhaseClear()
    {
        leaderHP = Mathf.Max(0f, leaderHP - leaderDamageOnSuccess);
        BlinkVignette(holyColor, blinkPeakNormal, false);

        if (leaderHP <= 0f) { StartCoroutine(PlayLeaderDefeat()); return; }
        StartNewPhase();
    }

    // ====== Defeat/Victory ======
    private IEnumerator PlayPlayerDefeat()
    {
        inputLocked = true;
        if (DirectingCamera != null && Player != null)
        {
            DirectingCamera.enabled = true;
            Transform pt = Player.transform; Vector3 baseScale = pt.localScale;
            float t = 0f;
            while (t < 1.2f)
            {
                t += Time.deltaTime; float k = Mathf.Clamp01(t / 1.2f);
                pt.localScale = Vector3.Lerp(baseScale, baseScale * 1.6f, k);
                SetVignette(Mathf.Lerp(_vignette.intensity.value, 1f, Time.deltaTime * 4f), damageColor);
                yield return null;
            }
        }
        yield return new WaitForSeconds(0.5f);
    }

    private IEnumerator PlayLeaderDefeat()
    {
        inputLocked = true;
        if (DirectingCamera != null) DirectingCamera.enabled = true;
        foreach (var col in columns) if (col != null) col.SetActive(true);
        yield return new WaitForSeconds(1.0f);
        if (Leader != null) Leader.SetActive(false);
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            SetVignette(Mathf.Lerp(_vignette.intensity.value, 0.7f, Time.deltaTime * 4f), holyColor);
            yield return null;
        }
    }

    // ====== Utils ======
    private IEnumerator ScaleUp(Transform tr, float t)
    {
        float el = 0f; Vector3 from = Vector3.one * 0.2f, to = Vector3.one;
        while (el < t) { el += Time.deltaTime; float k = el / t; tr.localScale = Vector3.LerpUnclamped(from, to, EaseOutBack(k)); yield return null; }
        tr.localScale = to;
    }

    private IEnumerator FadeAndDestroy(Image x, float life)
    {
        if (!x) yield break;
        var col = x.color; col.a = 1f; x.color = col;

        float el = 0f;
        while (el < life)
        {
            if (!x) yield break; // 중간에 파괴되면 종료
            el += Time.deltaTime;
            var c = x.color; c.a = Mathf.Lerp(1f, 0f, el / life);
            x.color = c;
            yield return null;
        }
        if (x) Destroy(x.gameObject);
    }

    private IEnumerator ShakeCamera(Camera cam, float t, float intensity)
    {
        if (cam == null) yield break;
        Transform ct = cam.transform; Vector3 basePos = mainCamOriginalPos;
        float el = 0f;
        while (el < t)
        {
            el += Time.deltaTime;
            float dx = (Random.value * 2f - 1f) * intensity;
            float dy = (Random.value * 2f - 1f) * intensity;
            ct.localPosition = basePos + new Vector3(dx, dy, 0f) * 0.0025f;
            yield return null;
        }
        ct.localPosition = basePos;
    }

    private void CleanupSlots()
    {
        foreach (var s in slots) if (s.bg != null) Destroy(s.bg.gameObject);
        slots.Clear();
    }

    private Sprite GetArrowSprite(CommandDir dir)
    {
        switch (dir)
        {
            case CommandDir.Up: return arrowUp;
            case CommandDir.Down: return arrowDown;
            case CommandDir.Left: return arrowLeft;
            case CommandDir.Right: return arrowRight;
        }
        return null;
    }

    private Color GetDirColor(CommandDir dir, float alpha = 1f)
    {
        Color c = dir switch
        {
            CommandDir.Up => colorUp,
            CommandDir.Down => colorDown,
            CommandDir.Left => colorLeft,
            CommandDir.Right => colorRight,
            _ => Color.white
        };
        c.a = alpha; return c;
    }

    private float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3) + c1 * Mathf.Pow(x - 1f, 2);
    }

    // 디버그
    [ContextMenu("Force Timeout")] private void Debug_ForceTimeout() { phaseTimer = 0f; }
    [ContextMenu("Force Clear")] private void Debug_ForceClear() { inputCursor = currentSequence.Count; OnPhaseClear(); }
}
