using UnityEngine;
using TMPro;

/// <summary>
/// 초:센티초(SS:CC) 카운트다운.
/// - startPaused가 true면 게임 시작 시 멈춘 상태로 대기.
/// - StopTimer/ResumeTimer로 외부에서 제어.
/// </summary>
public class CountdownTimer : MonoBehaviour
{
    [Header("타이머 설정")]
    [Tooltip("시작 시간(초). 예: 20 -> 20:00에서 시작")]
    public float startSeconds = 20f;

    [Tooltip("시작 시 일시정지 상태로 둘지 여부")]
    public bool startPaused = false;

    [Header("UI 연결")]
    [Tooltip("카운트다운 표시용 TextMeshProUGUI")]
    public TextMeshProUGUI timerText;

    private float remainingTime;
    private bool isRunning;

    private void Start()
    {
        remainingTime = Mathf.Max(0f, startSeconds);
        isRunning = !startPaused;        // 시작을 멈춰두기 가능
        UpdateText(remainingTime);
        Debug.Log($"[타이머] 시작: {remainingTime}초, 실행중:{isRunning}");
    }

    private void Update()
    {
        if (!isRunning) return;

        if (remainingTime > 0f)
        {
            remainingTime -= Time.deltaTime;
            if (remainingTime < 0f) remainingTime = 0f;
            UpdateText(remainingTime);
        }
        else
        {
            isRunning = false;
            Debug.Log("[타이머] 0초 도달, 자동 정지");
        }
    }

    private void UpdateText(float t)
    {
        int s = Mathf.FloorToInt(t);
        int cs = Mathf.FloorToInt((t - s) * 100f);
        if (timerText != null) timerText.text = $"{s:00}:{cs:00}";
    }

    public void StopTimer()
    {
        if (!isRunning) return;
        isRunning = false;
        Debug.Log("[타이머] 외부 정지 호출");
    }

    public void ResumeTimer()
    {
        if (remainingTime <= 0f) return;
        isRunning = true;
        Debug.Log("[타이머] 외부 재개 호출");
    }

    public void ResetAndStart(float seconds)
    {
        remainingTime = Mathf.Max(0f, seconds);
        isRunning = true;
        UpdateText(remainingTime);
        Debug.Log($"[타이머] 리셋 및 시작: {remainingTime}초");
    }

    public float GetRemainingSeconds() => remainingTime;
    public bool IsRunning() => isRunning;
}
