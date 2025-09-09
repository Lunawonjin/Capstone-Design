using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// 생존 타이머 + 스테이지 표시(TMP)
/// - timeText: 경과 시간
/// - stageText: "1스테이지" → (20초) "1스테이지 클리어!" 보여준 뒤 1초 후 "2스테이지"
///              → (추가 30초) "2스테이지 클리어!"
/// </summary>
public class SurvivalStageTimer : MonoBehaviour
{
    [Header("TextMeshPro 연결")]
    public TMP_Text timeText;   // 생존 시간 텍스트
    public TMP_Text stageText;  // 스테이지 상태 텍스트

    [Header("스테이지 시간(초)")]
    public float stage1Duration = 20f;  // 1스테이지 길이
    public float stage2Duration = 30f;  // 2스테이지 길이(1 끝난 뒤 추가)

    [Header("표시 형식")]
    public bool showAsMMSS = false;     // true: mm:ss, false: "초"
    public string secondsSuffix = "초";

    [Header("자동 시작/정지")]
    public bool startOnEnable = true;
    public bool stopAtStage2Clear = false;

    [Header("연출 딜레이")]
    public float stage2LabelDelay = 1f; // 1스테이지 클리어! → 2스테이지 전환까지 딜레이

    // 내부 상태
    private float elapsed = 0f;
    private bool running = false;

    private enum StageState { Stage1, Stage2, Stage2Cleared }
    private StageState state = StageState.Stage1;
    private Coroutine stageLabelRoutine;

    void OnEnable()
    {
        if (startOnEnable) StartTimer();
        else { RenderTime(); ApplyStageTextImmediate(); }
    }

    void Update()
    {
        if (!running) return;

        elapsed += Time.deltaTime;

        var prev = state;
        UpdateStageStateOnly();

        if (prev != state)
            OnStageStateChanged();

        RenderTime();
    }

    /// <summary>타이머 시작(리셋)</summary>
    public void StartTimer()
    {
        elapsed = 0f;
        running = true;
        state = StageState.Stage1;

        // 텍스트 즉시 반영
        RenderTime();
        ApplyStageTextImmediate();
    }

    public void StopTimer() => running = false;
    public void ResumeTimer() => running = true;
    public float ElapsedSeconds => elapsed;

    // ───────────────── 내부 로직 ─────────────────

    private void UpdateStageStateOnly()
    {
        float s1 = stage1Duration;
        float s2 = stage1Duration + stage2Duration;

        if (elapsed >= s2) state = StageState.Stage2Cleared;
        else if (elapsed >= s1) state = StageState.Stage2;
        else state = StageState.Stage1;
    }

    private void OnStageStateChanged()
    {
        if (stageText == null) return;

        // 진행 중인 전환 코루틴 정리
        if (stageLabelRoutine != null)
        {
            StopCoroutine(stageLabelRoutine);
            stageLabelRoutine = null;
        }

        switch (state)
        {
            case StageState.Stage1:
                stageText.text = "1스테이지";
                break;

            case StageState.Stage2:
                // 요구사항: 1스테이지 완료를 1초 보여준 뒤 2스테이지로 변경
                stageText.text = "1스테이지 클리어!";
                stageLabelRoutine = StartCoroutine(SwapLabelAfterDelay(stage2LabelDelay, "2스테이지"));
                break;

            case StageState.Stage2Cleared:
                stageText.text = "2스테이지 클리어!";
                if (stopAtStage2Clear) running = false;
                break;
        }
    }

    private IEnumerator SwapLabelAfterDelay(float delay, string nextLabel)
    {
        yield return new WaitForSeconds(delay);
        if (stageText) stageText.text = nextLabel;
        stageLabelRoutine = null;
    }

    private void ApplyStageTextImmediate()
    {
        if (!stageText) return;
        switch (state)
        {
            case StageState.Stage1: stageText.text = "1스테이지"; break;
            case StageState.Stage2: stageText.text = "2스테이지"; break; // 재시작 상황 대비
            case StageState.Stage2Cleared: stageText.text = "2스테이지 클리어!"; break;
        }
    }

    private void RenderTime()
    {
        if (!timeText) return;

        if (showAsMMSS)
        {
            int t = Mathf.FloorToInt(elapsed);
            int m = t / 60;
            int s = t % 60;
            timeText.text = $"{m:00}:{s:00}";
        }
        else
        {
            int t = Mathf.FloorToInt(elapsed);
            timeText.text = $"{t}{secondsSuffix}";
        }
    }
}
