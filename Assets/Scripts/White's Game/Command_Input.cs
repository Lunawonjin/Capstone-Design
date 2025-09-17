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

    [Tooltip("플레이어/교주 최대 HP")]
    public float playerMaxHP = 100f;
    public float leaderMaxHP = 100f;

    [Tooltip("플레이어/교주 현재 HP(에디터 테스트용 초기값)")]
    public float playerHP = 100f;
    public float leaderHP = 100f;

    [Tooltip("HP 30% 이하 임계치(0~1 비율)")]
    [Range(0.05f, 0.5f)] public float lowHPThreshold = 0.3f;

    [Header("UI / 커맨드 생성")]
    [Tooltip("커맨드 블록이 배치될 패널(RectTransform)")]
    public RectTransform commandPanel;

    [Tooltip("커맨드 블록 프리팹(Image + CanvasGroup)")]
    public Image commandBlockPrefab;

    [Tooltip("화살표 스프라이트(Up/Down/Left/Right 순서로 연결 권장)")]
    public Sprite arrowUp;
    public Sprite arrowDown;
    public Sprite arrowLeft;
    public Sprite arrowRight;

    [Tooltip("정답 스파클(ParticleSystem 프리팹)")]
    public ParticleSystem sparklePrefab;

    [Tooltip("오답 X 마크 프리팹(Image)")]
    public Image wrongXPrefab;

    [Header("카메라")]
    public Camera MainCamera;
    public Camera DirectingCamera;

    [Header("화면가 연출 오버레이(Image)")]
    public Image damageVignette; // 빨간색
    public Image holyVignette;   // 노랑/흰

    [Header("승리/패배 연출 기둥 오브젝트들")]
    public GameObject[] columns;

    [Header("게임 규칙")]
    [Tooltip("초기 부여 시간(초)")]
    public float baseTime = 5f;

    [Tooltip("페이즈마다 시간 감소량(초)")]
    public float timeDecreasePerPhase = 0.3f;

    [Tooltip("최소 부여 시간 하한(초)")]
    public float minTime = 2f;

    [Tooltip("시작 커맨드 길이")]
    public int startCommandCount = 5;

    [Tooltip("페이즈마다 커맨드 증가 최소/최대")]
    public Vector2Int commandIncreaseRange = new Vector2Int(1, 2);

    [Tooltip("커맨드 최대 개수")]
    public int maxCommands = 12;

    [Tooltip("시간 내 실패 시 플레이어 HP 감소량")]
    public float playerDamageOnTimeout = 15f;

    [Tooltip("성공 시 교주 HP 감소량")]
    public float leaderDamageOnSuccess = 20f;

    [Tooltip("오입력 시 즉시 깎일 시간(초)")]
    public float wrongPenaltyTime = 0.5f;

    [Tooltip("블록 생성 시 0.2배 -> 1배로 커지는 연출 시간(초)")]
    public float spawnScaleTime = 0.2f;

    [Tooltip("정답 처리 시 블록 비활성까지 대기(초)")]
    public float correctDeactivateDelay = 0.05f;

    [Tooltip("카메라 흔들림 강도/시간")]
    public float shakeIntensity = 8f;
    public float shakeTime = 0.08f;

    [Tooltip("오버레이 페이드 시간(초)")]
    public float overlayFadeTime = 0.6f;

    // 내부 상태
    private float phaseTimer;
    private int phaseIndex = 0;
    private int currentCommandCount;
    private List<CommandDir> currentSequence = new List<CommandDir>();
    private List<Image> spawnedBlocks = new List<Image>();
    private int inputCursor = 0;
    private bool inputLocked = false;
    private Vector3 mainCamOriginalPos;

    // 방향 정의
    private enum CommandDir { Up, Down, Left, Right }

    // 색 매핑
    private readonly Color colUp = new Color(1f, 0.2f, 0.2f, 1f);     // 빨강
    private readonly Color colDown = new Color(1f, 0.9f, 0.2f, 1f);   // 노랑
    private readonly Color colLeft = new Color(0.2f, 1f, 0.4f, 1f);   // 초록
    private readonly Color colRight = new Color(0.2f, 0.6f, 1f, 1f);  // 파랑

    private void Awake()
    {
        if (MainCamera != null) mainCamOriginalPos = MainCamera.transform.localPosition;
        // 오버레이 초기 투명
        SetImageAlpha(damageVignette, 0f);
        SetImageAlpha(holyVignette, 0f);
    }

    private void Start()
    {
        currentCommandCount = Mathf.Clamp(startCommandCount, 1, maxCommands);
        StartNewPhase();
    }

    private void Update()
    {
        if (playerHP <= 0f || leaderHP <= 0f) return;

        // 시간 흐름
        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
        {
            // 시간 내 실패
            OnPhaseTimeout();
            return;
        }

        // 입력 처리
        if (!inputLocked)
        {
            CommandDir? pressed = GetPressedArrow();
            if (pressed.HasValue)
            {
                EvaluateInput(pressed.Value);
            }
        }

        // 오버레이 지속/우선 순위 갱신
        UpdateVignettesPersistent();
    }

    // 페이즈 시작
    private void StartNewPhase()
    {
        phaseIndex++;
        inputCursor = 0;
        inputLocked = true;

        // 시간 계산
        float phaseTime = Mathf.Max(minTime, baseTime - timeDecreasePerPhase * (phaseIndex - 1));
        phaseTimer = phaseTime;

        // 커맨드 길이 증가(1~2 랜덤), 상한 적용
        if (phaseIndex > 1)
        {
            int inc = Random.Range(commandIncreaseRange.x, commandIncreaseRange.y + 1);
            currentCommandCount = Mathf.Clamp(currentCommandCount + inc, 1, maxCommands);
        }

        // 기존 블록 제거
        CleanupBlocks();

        // 시퀀스 생성 + 블록 스폰
        GenerateSequence(currentCommandCount);
        SpawnAndLayoutBlocks();

        // 스폰 애니 끝나면 입력 허용
        StartCoroutine(UnlockInputAfterSpawn());
    }

    private IEnumerator UnlockInputAfterSpawn()
    {
        // 짧은 안전 대기(스폰 애니메이션 시간과 동일)
        yield return new WaitForSeconds(spawnScaleTime);
        inputLocked = false;
    }

    private void GenerateSequence(int count)
    {
        currentSequence.Clear();
        for (int i = 0; i < count; i++)
        {
            int r = Random.Range(0, 4);
            currentSequence.Add((CommandDir)r);
        }
    }

    private void SpawnAndLayoutBlocks()
    {
        if (commandPanel == null || commandBlockPrefab == null) return;

        Rect rect = commandPanel.rect;
        Vector2 panelMin = new Vector2(rect.xMin, rect.yMin);
        Vector2 panelMax = new Vector2(rect.xMax, rect.yMax);

        // 패널 좌표계에서 겹침 최소화를 위해 simple jitter 배치
        for (int i = 0; i < currentSequence.Count; i++)
        {
            Image block = Instantiate(commandBlockPrefab, commandPanel);
            block.name = $"CmdBlock_{i}_{currentSequence[i]}";

            // 스프라이트/색 세팅
            block.sprite = GetSprite(currentSequence[i]);
            block.color = GetColor(currentSequence[i]);

            // 초기 스케일 작게
            block.transform.localScale = Vector3.one * 0.2f;

            // 랜덤 위치 배치
            Vector2 localPos = new Vector2(
                Random.Range(panelMin.x + 40f, panelMax.x - 40f),
                Random.Range(panelMin.y + 40f, panelMax.y - 40f)
            );
            block.rectTransform.anchoredPosition = localPos;

            spawnedBlocks.Add(block);

            // 등장 스케일 애니
            StartCoroutine(ScaleUp(block.transform, spawnScaleTime));
        }
    }

    private IEnumerator ScaleUp(Transform tr, float t)
    {
        float el = 0f;
        Vector3 from = Vector3.one * 0.2f;
        Vector3 to = Vector3.one;
        while (el < t)
        {
            el += Time.deltaTime;
            float k = el / t;
            tr.localScale = Vector3.LerpUnclamped(from, to, EaseOutBack(k));
            yield return null;
        }
        tr.localScale = to;
    }

    // 입력 감지
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

        CommandDir target = currentSequence[inputCursor];
        Image targetBlock = spawnedBlocks[inputCursor];

        if (input == target)
        {
            // 정답 처리
            OnCorrect(targetBlock);
            inputCursor++;

            // 모두 성공
            if (inputCursor >= currentSequence.Count)
            {
                OnPhaseClear();
            }
        }
        else
        {
            // 오답 처리: 시간 즉시 0.5s 감소, X 마킹, 화면 흔들림
            OnWrong(targetBlock);
            phaseTimer = Mathf.Max(0f, phaseTimer - wrongPenaltyTime);
        }
    }

    private void OnCorrect(Image block)
    {
        // 스파클
        if (sparklePrefab != null)
        {
            ParticleSystem fx = Instantiate(sparklePrefab, block.transform.position, Quaternion.identity, block.transform.parent);
            fx.Play();
            Destroy(fx.gameObject, 2f);
        }
        // 잠깐 후 비활성
        StartCoroutine(DisableAfter(block, correctDeactivateDelay));
    }

    private void OnWrong(Image nearBlock)
    {
        // X 마크
        if (wrongXPrefab != null && nearBlock != null)
        {
            Image x = Instantiate(wrongXPrefab, nearBlock.rectTransform.position, Quaternion.identity, nearBlock.transform.parent);
            StartCoroutine(FadeAndDestroy(x, 0.25f));
        }
        // 화면 흔들림
        if (MainCamera != null) StartCoroutine(ShakeCamera(MainCamera, shakeTime, shakeIntensity));
    }

    private void OnPhaseTimeout()
    {
        // 제한시간 실패 -> 플레이어 피해
        ApplyPlayerDamage(playerDamageOnTimeout);

        // 붉은 오버레이 번쩍
        BlinkDamageOverlay();

        // 패배 체크
        if (playerHP <= 0f)
        {
            StartCoroutine(PlayPlayerDefeat());
            return;
        }

        // 다음 페이즈 재시도(다시 생성)
        StartNewPhase();
    }

    private void OnPhaseClear()
    {
        // 교주 피해
        ApplyLeaderDamage(leaderDamageOnSuccess);

        // 성스러운 오버레이 번쩍
        BlinkHolyOverlay();

        if (leaderHP <= 0f)
        {
            StartCoroutine(PlayLeaderDefeat());
            return;
        }

        // 다음 페이즈로
        StartNewPhase();
    }

    private void ApplyPlayerDamage(float dmg)
    {
        playerHP = Mathf.Max(0f, playerHP - dmg);
    }

    private void ApplyLeaderDamage(float dmg)
    {
        leaderHP = Mathf.Max(0f, leaderHP - dmg);
    }

    private void BlinkDamageOverlay()
    {
        if (damageVignette == null) return;
        StartCoroutine(BlinkOverlayOnce(damageVignette, 0.9f, overlayFadeTime));
    }

    private void BlinkHolyOverlay()
    {
        if (holyVignette == null) return;
        StartCoroutine(BlinkOverlayOnce(holyVignette, 0.9f, overlayFadeTime));
    }

    private IEnumerator BlinkOverlayOnce(Image img, float peakAlpha, float fadeTime)
    {
        if (img == null) yield break;

        // 순간 피크
        SetImageAlpha(img, peakAlpha);

        // 30% 이하 지속 규칙은 UpdateVignettesPersistent에서 관리되므로
        // 여기서는 일정 시간에 걸쳐 자연감쇠만 시도
        float el = 0f;
        while (el < fadeTime)
        {
            el += Time.deltaTime;
            // 30% 이하 지속이면 나가지 않고 유지
            if (ShouldHoldOverlay(img)) yield break;

            float a = Mathf.Lerp(peakAlpha, 0f, el / fadeTime);
            SetImageAlpha(img, a);
            yield return null;
        }
        SetImageAlpha(img, ShouldHoldOverlay(img) ? 0.8f : 0f);
    }

    private void UpdateVignettesPersistent()
    {
        bool playerLow = (playerHP <= playerMaxHP * lowHPThreshold);
        bool leaderLow = (leaderHP <= leaderMaxHP * lowHPThreshold);

        // 둘 다 30%↓면 성스러운 느낌 우선
        if (playerLow && leaderLow)
        {
            HoldOverlay(holyVignette, 0.8f);
            ReleaseOverlay(damageVignette, overlayFadeTime);
            return;
        }

        // 플레이어만 30%↓ -> 붉은 지속
        if (playerLow)
        {
            HoldOverlay(damageVignette, 0.8f);
        }
        else
        {
            ReleaseOverlay(damageVignette, overlayFadeTime);
        }

        // 교주만 30%↓ -> 성스러운 지속
        if (leaderLow)
        {
            HoldOverlay(holyVignette, 0.8f);
        }
        else
        {
            ReleaseOverlay(holyVignette, overlayFadeTime);
        }
    }

    private bool ShouldHoldOverlay(Image img)
    {
        if (img == damageVignette)
        {
            bool playerLow = (playerHP <= playerMaxHP * lowHPThreshold);
            bool bothLow = playerHP <= playerMaxHP * lowHPThreshold && leaderHP <= leaderMaxHP * lowHPThreshold;
            // 둘 다 낮으면 성스러움 우선 -> 데미지 오버레이는 홀드하지 않음
            return playerLow && !bothLow;
        }
        if (img == holyVignette)
        {
            bool leaderLow = (leaderHP <= leaderMaxHP * lowHPThreshold);
            // 둘 다 낮아도 성스러움 우선 규칙에 의해 hold
            return leaderLow;
        }
        return false;
    }

    private void HoldOverlay(Image img, float targetAlpha)
    {
        if (img == null) return;
        float a = img.color.a;
        if (a < targetAlpha) SetImageAlpha(img, Mathf.Lerp(a, targetAlpha, Time.deltaTime * 8f));
        else SetImageAlpha(img, targetAlpha);
    }

    private void ReleaseOverlay(Image img, float fadeTime)
    {
        if (img == null) return;
        float a = img.color.a;
        if (a <= 0f) return;
        float newA = Mathf.MoveTowards(a, 0f, Time.deltaTime * (1f / Mathf.Max(0.001f, fadeTime)));
        SetImageAlpha(img, newA);
    }

    private IEnumerator PlayPlayerDefeat()
    {
        inputLocked = true;

        // 플레이어 확대 + 어둠 잠식(DirectingCamera 연출)
        if (DirectingCamera != null && Player != null)
        {
            DirectingCamera.enabled = true;

            Transform pt = Player.transform;
            Vector3 baseScale = pt.localScale;

            float t = 0f;
            while (t < 1.2f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 1.2f);
                pt.localScale = Vector3.Lerp(baseScale, baseScale * 1.6f, k);

                // 화면 어둡게(피 오버레이를 검은색처럼 써도 되지만, 여기서는 데미지 비넷 담금)
                HoldOverlay(damageVignette, Mathf.Lerp(0.3f, 1f, k));
                yield return null;
            }
        }

        // 기둥 연출 비활성(패배 씬이라면 필요 없음)
        yield return new WaitForSeconds(0.5f);

        // 여기서 재시작/결과창 호출 등 후처리
        // 예: SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private IEnumerator PlayLeaderDefeat()
    {
        inputLocked = true;

        // 배경이 어둡고 여러 개의 빛 기둥이 교주를 비추는 연출
        if (DirectingCamera != null) DirectingCamera.enabled = true;

        // 기둥 점등
        foreach (var col in columns)
        {
            if (col != null) col.SetActive(true);
        }

        // 잠시 후 교주 제거
        yield return new WaitForSeconds(1.0f);

        if (Leader != null) Leader.SetActive(false);

        // 화면 밝기 회복(성스러운 오버레이 서서히)
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            HoldOverlay(holyVignette, Mathf.Lerp(0.2f, 0.8f, t / 1.2f));
            yield return null;
        }

        // 여기서 클리어 처리(보상/다음 씬 등)
    }

    private IEnumerator DisableAfter(Image block, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (block != null) block.enabled = false;
    }

    private IEnumerator FadeAndDestroy(Image x, float life)
    {
        if (x == null) yield break;
        float el = 0f;
        while (el < life)
        {
            el += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, el / life);
            SetImageAlpha(x, a);
            yield return null;
        }
        if (x != null) Destroy(x.gameObject);
    }

    private IEnumerator ShakeCamera(Camera cam, float t, float intensity)
    {
        if (cam == null) yield break;
        Transform ct = cam.transform;
        Vector3 basePos = mainCamOriginalPos;

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

    private void CleanupBlocks()
    {
        for (int i = 0; i < spawnedBlocks.Count; i++)
        {
            if (spawnedBlocks[i] != null)
            {
                Destroy(spawnedBlocks[i].gameObject);
            }
        }
        spawnedBlocks.Clear();
    }

    private Sprite GetSprite(CommandDir dir)
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

    private Color GetColor(CommandDir dir)
    {
        switch (dir)
        {
            case CommandDir.Up: return colUp;       // 빨강
            case CommandDir.Down: return colDown;   // 노랑
            case CommandDir.Left: return colLeft;   // 초록
            case CommandDir.Right: return colRight; // 파랑
        }
        return Color.white;
    }

    private void SetImageAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = Mathf.Clamp01(a);
        img.color = c;
    }

    // 부드러운 백 이징(등장 스케일용)
    private float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3) + c1 * Mathf.Pow(x - 1f, 2);
    }

    // 에디터에서 즉시 테스트할 수 있는 헬퍼(선택)
    [ContextMenu("Force Timeout")]
    private void Debug_ForceTimeout()
    {
        phaseTimer = 0f;
    }

    [ContextMenu("Force Clear")]
    private void Debug_ForceClear()
    {
        inputCursor = currentSequence.Count;
        OnPhaseClear();
    }
}
