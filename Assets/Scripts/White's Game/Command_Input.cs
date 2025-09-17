using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시간안에 커맨드 블럭을 순서대로 입력해 교주를 물리치는 게임
/// 게임이 시작되면 교주와 천하얀의 대화 후
/// 커맨드 블럭이 랜덤으로 5개정도 배치되고 시간은 5초 부여
/// 단계를 깰때 마다 교주의 체력이 감소되고 다음 페이즈에서 부여되는 시간초가 줄어들고 커맨드 블럭이 1~2개 늘어남 (최대 12개)
/// 커맨드 실패를 하면 부여된 시간초가 즉시 0.5초 감소
/// 시간초안에 클리어 하지 못하면 플레이어의 체력 감소
/// 클리어하면 (미정)
/// 
/// 시스템
/// 게임 커맨드가 등장할 UI 패널을 연결해 패널의 크기만큼 커맨드블럭의 크기 및 위치를 설정
/// 등장할 때 엄청 작았다가 커지는 식으로 구성
/// 커맨드 블럭의 방향키 위치에 따라 커맨드 블럭의 색 변경 
/// ↑(빨강), ↓(노랑), ←(초록), →(파랑)
/// 올바른 커맨드를 누르면 스파클이 튀면서 올바른 커맨드를 비활성화
/// 틀린 커맨드를 누르면 커맨드에 X가 나오면서 화면이 약간 움찔?하는 상황 연출
/// 시간안에 실패하면 플레이어의 체력을 감소하고 화면 가에 빨간색(피)가 잠깐 보였다 천천히 사라지는 연출
/// 플레이어의 HP가 30%(미정) 이하라면 화면 가에 빨간색(피)가 사라지지 않고 남아있는 연출
/// 시간안에 성공하면 교주를 공격하고 플레이어의 주변에 노란색,흰색이 섞인 성스러운 느낌을 화면 가에 잠깐 보였다 사라지는 연출
/// 교주의 HP가 30%(미정) 이하라면 화면 가에 성스러운 느낌의 색이 사라지지 않고 남아있는 연출
/// 만약 교주와 플레이어 둘 다 30%(미정) 이하라면 성스러운 느낌의 색이 사라지지않고 남아있는 연출을 우선함
/// 플레이어의  HP가 0이 되면 플레이어를 확대 시키고 점점 어둠이 플레이어를 잠식하는 연출
/// 플레이어가 교주를 쓰러트리게 된다면 어두웠던 배경에 빛이 한줄기 내려오더니 갑자기 여러개의 빛의 기둥이 교주를 비춤
/// 그렇게 교주는 사라지고 밝은 되찾고 자신의 삶을 찾는 연출
/// </summary>
public class Command_Input : MonoBehaviour
{
    [Header("플레이어/교주")]
    public GameObject Player;
    public GameObject Leader;
    public float playerMaxHP = 100f, leaderMaxHP = 100f;
    public float playerHP = 100f, leaderHP = 100f;
    [Range(0.05f, 0.5f)] public float lowHPThreshold = 0.3f;

    [Header("UI / 커맨드 라인 패널 & 프리팹")]
    public RectTransform commandPanel;     // 슬롯 부모
    public Image commandBlockPrefab;       // 동그란 BG 프리팹(Image)

    [Header("네온 테마(옵션)")]
    public Sprite slotRingSprite;
    public Material additiveUIMaterial;    // UI/Particles/Additive
    public float ringScale = 1.12f;
    [Range(0f, 1f)] public float ringAlpha = 0.8f;

    [Header("방향별 색(인스펙터 변경)")]
    public Color colorUp = new Color(1.00f, 0.40f, 0.40f, 1f);
    public Color colorDown = new Color(1.00f, 0.75f, 0.30f, 1f);
    public Color colorLeft = new Color(0.60f, 1.00f, 0.50f, 1f);
    public Color colorRight = new Color(0.40f, 0.85f, 1.00f, 1f);

    [Header("슬롯 배치/스타일")]
    public Color emptySlotColor = new Color(0.55f, 0.93f, 1f, 0.95f); // 정답 후 비워질 색
    [Range(0f, 1f)] public float slotColorAlpha = 1f; // 슬롯 배경 투명도(방향색과 동일)
    public float slotPaddingX = 80f;
    public float minSlotSpacing = 40f;
    public float spawnScaleTime = 0.2f;

    [Header("화살표 스프라이트")]
    public Sprite arrowUp, arrowDown, arrowLeft, arrowRight;

    [Header("이펙트 프리팹")]
    public ParticleSystem sparklePrefab; // 정답 스파클
    public Image wrongXPrefab;           // 오답 X(Image 프리팹)

    [Header("카메라/오버레이")]
    public Camera MainCamera, DirectingCamera;
    public Image damageVignette, holyVignette;

    [Header("승리/패배 연출")]
    public GameObject[] columns;

    // ====== 스테이지 플랜 ======
    [Header("스테이지 플랜(인스펙터에서 순서 지정)")]
    public bool useStagePlan = true;     // 켜면 아래 플랜 그대로 진행
    public bool loopStagePlan = false;   // 끝나면 다시 처음으로
    [System.Serializable]
    public class StageEntry
    {
        [Range(5, 12)] public int count = 5;    // 커맨드 개수
        [Min(1)] public int repeats = 1;   // 몇 번 반복할지
        public bool customTime = false;        // 시간 직접 지정?
        [Min(0.1f)] public float time = 5f;    // customTime=true일 때 사용
    }
    public List<StageEntry> stagePlan = new List<StageEntry>()
    {
        new StageEntry{count=5, repeats=3},
        new StageEntry{count=6, repeats=2},
    };

    // ====== 자동 진행 모드(플랜 끌 때 사용) ======
    [Header("자동 진행 모드(플랜 OFF일 때만 사용)")]
    [Range(5, 12)] public int startCommandCount = 5;
    public Vector2Int commandIncreaseRange = new Vector2Int(1, 2);
    [Range(5, 12)] public int maxCommands = 12;

    [System.Serializable]
    public struct CountTime { [Range(5, 12)] public int count; [Min(0.1f)] public float time; }
    public List<CountTime> timeByCount = new List<CountTime>()
    {
        new CountTime{count=5,  time=5.0f},
        new CountTime{count=6,  time=4.6f},
        new CountTime{count=7,  time=4.2f},
        new CountTime{count=8,  time=3.8f},
        new CountTime{count=9,  time=3.5f},
        new CountTime{count=10, time=3.2f},
        new CountTime{count=11, time=3.0f},
        new CountTime{count=12, time=2.8f},
    };
    [Tooltip("전역 난이도 배수(시간에 곱). 1=기본, 0.8=빡셈, 1.2=쉬움")]
    public float difficultyTimeScale = 1f;

    [Header("피해/패널티")]
    public float playerDamageOnTimeout = 15f;
    public float leaderDamageOnSuccess = 20f;
    public float wrongPenaltyTime = 0.5f;

    [Header("카메라/오버레이 옵션")]
    public float shakeIntensity = 8f;
    public float shakeTime = 0.08f;
    public float overlayFadeTime = 0.6f;

    // 내부 상태
    private float phaseTimer;
    private int phaseIndex = 0;
    private int currentCommandCount;
    private List<CommandDir> currentSequence = new List<CommandDir>();
    private readonly List<SlotUI> slots = new List<SlotUI>();
    private int inputCursor = 0;
    private bool inputLocked = false;
    private Vector3 mainCamOriginalPos;

    // 스테이지 플랜 진행 포인터
    private int planIndex = 0;        // stagePlan 인덱스
    private int planRepeatProgress = 0; // 현재 항목에서 몇 번 소비했는지

    private enum CommandDir { Up, Down, Left, Right }
    private class SlotUI { public Image bg, ring, arrow; }

    // ====== Unity lifecycle ======
    private void Awake()
    {
        if (MainCamera != null) mainCamOriginalPos = MainCamera.transform.localPosition;
        SetImageAlpha(damageVignette, 0f);
        SetImageAlpha(holyVignette, 0f);
    }

    private void Start()
    {
        currentCommandCount = Mathf.Clamp(startCommandCount, 5, 12);
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

        UpdateVignettesPersistent();
    }

    // ====== Phase control ======
    private void StartNewPhase()
    {
        phaseIndex++;
        inputCursor = 0;
        inputLocked = true;

        // 이번 페이즈용 count/time 선택(플랜 우선)
        ChooseCountAndTimeForThisPhase(out int count, out float time);
        currentCommandCount = count;
        phaseTimer = Mathf.Max(0.1f, time * Mathf.Max(0.01f, difficultyTimeScale));

        GenerateSequence(currentCommandCount);
        BuildSlotLineAndFill(currentCommandCount);

        StartCoroutine(UnlockInputAfterSpawn());
    }

    private void ChooseCountAndTimeForThisPhase(out int count, out float time)
    {
        if (useStagePlan && stagePlan != null && stagePlan.Count > 0)
        {
            // 현재 항목
            var entry = stagePlan[Mathf.Clamp(planIndex, 0, stagePlan.Count - 1)];
            count = Mathf.Clamp(entry.count, 5, 12);
            time = entry.customTime ? entry.time : GetTimeForCount(count);

            // 다음 페이즈를 위해 진행 포인터 갱신
            planRepeatProgress++;
            int need = Mathf.Max(1, entry.repeats);
            if (planRepeatProgress >= need)
            {
                planRepeatProgress = 0;
                planIndex++;
                if (planIndex >= stagePlan.Count)
                {
                    planIndex = loopStagePlan ? 0 : stagePlan.Count - 1; // 루프 안 하면 마지막 상태 유지
                }
            }
        }
        else
        {
            // 자동 증가 모드
            if (phaseIndex > 1)
            {
                int inc = Random.Range(commandIncreaseRange.x, commandIncreaseRange.y + 1);
                currentCommandCount = Mathf.Clamp(currentCommandCount + inc, 5, maxCommands);
            }
            count = currentCommandCount;
            time = GetTimeForCount(count);
        }
    }

    private IEnumerator UnlockInputAfterSpawn()
    {
        yield return new WaitForSeconds(spawnScaleTime);
        inputLocked = false;
    }

    private float GetTimeForCount(int count)
    {
        float? exact = null; float nearest = -1f; int nd = 999;
        foreach (var ct in timeByCount)
        {
            if (ct.count == count) exact = ct.time;
            int d = Mathf.Abs(ct.count - count);
            if (d < nd) { nd = d; nearest = ct.time; }
        }
        return exact.HasValue ? exact.Value : (nearest > 0f ? nearest : 3f);
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

            // 링
            Image ring = null;
            if (slotRingSprite != null && additiveUIMaterial != null)
            {
                var ringGO = new GameObject($"Ring_{i}", typeof(RectTransform), typeof(Image));
                ringGO.transform.SetParent(bg.transform, false);
                var rr = (RectTransform)ringGO.transform;
                rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
                rr.sizeDelta = bg.rectTransform.sizeDelta * ringScale;
                rr.anchoredPosition = Vector2.zero;
                ring = ringGO.GetComponent<Image>();
                ring.sprite = slotRingSprite;
                ring.material = additiveUIMaterial;
                ring.preserveAspect = true;
                ring.color = GetDirColor(currentSequence[i], ringAlpha);
                ring.raycastTarget = false;
            }

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
            slots.Add(new SlotUI { bg = bg, ring = ring, arrow = arrowImg });
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
        if (sparklePrefab != null)
        {
            var fx = Instantiate(sparklePrefab, slot.bg.rectTransform.position, Quaternion.identity, slot.bg.transform);
            fx.Play(); Destroy(fx.gameObject, 2f);
        }
        StartCoroutine(DisableArrowAfter(slot, 0.05f));
    }

    private IEnumerator DisableArrowAfter(SlotUI slot, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (slot != null && slot.arrow != null)
        {
            slot.arrow.enabled = false;
            slot.bg.color = emptySlotColor;
            if (slot.ring != null) slot.ring.enabled = false;
        }
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
            x.transform.SetAsLastSibling();
            StartCoroutine(FadeAndDestroy(x, 0.25f));
        }
        if (MainCamera != null) StartCoroutine(ShakeCamera(MainCamera, shakeTime, shakeIntensity));
    }

    // ====== Results ======
    private void OnPhaseTimeout()
    {
        playerHP = Mathf.Max(0f, playerHP - playerDamageOnTimeout);
        BlinkDamageOverlay();
        if (playerHP <= 0f) { StartCoroutine(PlayPlayerDefeat()); return; }
        StartNewPhase();
    }

    private void OnPhaseClear()
    {
        leaderHP = Mathf.Max(0f, leaderHP - leaderDamageOnSuccess);
        BlinkHolyOverlay();
        if (leaderHP <= 0f) { StartCoroutine(PlayLeaderDefeat()); return; }
        StartNewPhase();
    }

    // ====== Overlays ======
    private void BlinkDamageOverlay() { if (damageVignette != null) StartCoroutine(BlinkOverlayOnce(damageVignette, 0.9f, overlayFadeTime)); }
    private void BlinkHolyOverlay() { if (holyVignette != null) StartCoroutine(BlinkOverlayOnce(holyVignette, 0.9f, overlayFadeTime)); }

    private IEnumerator BlinkOverlayOnce(Image img, float peakAlpha, float fadeTime)
    {
        if (img == null) yield break;
        SetImageAlpha(img, peakAlpha);
        float el = 0f;
        while (el < fadeTime)
        {
            el += Time.deltaTime;
            if (ShouldHoldOverlay(img)) yield break;
            SetImageAlpha(img, Mathf.Lerp(peakAlpha, 0f, el / fadeTime));
            yield return null;
        }
        SetImageAlpha(img, ShouldHoldOverlay(img) ? 0.8f : 0f);
    }

    private void UpdateVignettesPersistent()
    {
        bool playerLow = (playerHP <= playerMaxHP * lowHPThreshold);
        bool leaderLow = (leaderHP <= leaderMaxHP * lowHPThreshold);

        if (playerLow && leaderLow) { HoldOverlay(holyVignette, 0.8f); ReleaseOverlay(damageVignette, overlayFadeTime); return; }
        if (playerLow) HoldOverlay(damageVignette, 0.8f); else ReleaseOverlay(damageVignette, overlayFadeTime);
        if (leaderLow) HoldOverlay(holyVignette, 0.8f); else ReleaseOverlay(holyVignette, overlayFadeTime);
    }

    private bool ShouldHoldOverlay(Image img)
    {
        if (img == damageVignette)
        {
            bool playerLow = (playerHP <= playerMaxHP * lowHPThreshold);
            bool bothLow = playerLow && (leaderHP <= leaderMaxHP * lowHPThreshold);
            return playerLow && !bothLow;
        }
        if (img == holyVignette) return (leaderHP <= leaderMaxHP * lowHPThreshold);
        return false;
    }

    private void HoldOverlay(Image img, float targetAlpha)
    {
        if (img == null) return;
        var a = img.color.a;
        SetImageAlpha(img, a < targetAlpha ? Mathf.Lerp(a, targetAlpha, Time.deltaTime * 8f) : targetAlpha);
    }
    private void ReleaseOverlay(Image img, float fadeTime)
    {
        if (img == null) return;
        SetImageAlpha(img, Mathf.MoveTowards(img.color.a, 0f, Time.deltaTime * (1f / Mathf.Max(0.001f, fadeTime))));
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
                HoldOverlay(damageVignette, Mathf.Lerp(0.3f, 1f, k));
                yield return null;
            }
        }
        yield return new WaitForSeconds(0.5f);
        // TODO: 패배 후 처리
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
            HoldOverlay(holyVignette, Mathf.Lerp(0.2f, 0.8f, t / 1.2f));
            yield return null;
        }
        // TODO: 클리어 처리
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
        if (x == null) yield break;
        var c0 = x.color; c0.a = 1f; x.color = c0;
        float el = 0f;
        while (el < life) { el += Time.deltaTime; SetImageAlpha(x, Mathf.Lerp(1f, 0f, el / life)); yield return null; }
        if (x != null) Destroy(x.gameObject);
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
        for (int i = 0; i < slots.Count; i++) if (slots[i].bg != null) Destroy(slots[i].bg.gameObject);
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

    private void SetImageAlpha(Image img, float a)
    {
        if (img == null) return; var c = img.color; c.a = Mathf.Clamp01(a); img.color = c;
    }

    private float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3) + c1 * Mathf.Pow(x - 1f, 2);
    }

    // 에디터 디버그
    [ContextMenu("Force Timeout")] private void Debug_ForceTimeout() { phaseTimer = 0f; }
    [ContextMenu("Force Clear")] private void Debug_ForceClear() { inputCursor = currentSequence.Count; OnPhaseClear(); }
}
