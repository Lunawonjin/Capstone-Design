using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    [Header("퍼즐 완료 조건")]
    public int totalMatchesNeeded = 6;   // 필요한 매칭 수
    public CountdownTimer timer;         // 타이머 참조

    private int matchedCount = 0;
    private bool completed = false;

    void Awake()
    {
        if (timer == null)
        {
            timer = FindObjectOfType<CountdownTimer>();
            if (timer == null) Debug.LogWarning("[퍼즐] CountdownTimer를 찾지 못했습니다. 타이머 정지 불가");
        }
    }

    public void ReportMatchOnce()
    {
        if (completed) return;

        matchedCount++;
        Debug.Log("[퍼즐] 매칭 성공: " + matchedCount + " / " + totalMatchesNeeded);

        if (matchedCount >= totalMatchesNeeded)
        {
            completed = true;
            Debug.Log("[퍼즐] 모든 매칭 완료, 타이머 정지");
            if (timer != null) timer.StopTimer();
        }
    }
}
