using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    [Header("퍼즐 완료 조건")]
    [Tooltip("퍼즐 완료로 인정하기 위한 총 매칭 수")]
    public int totalMatchesNeeded = 6;

    [Header("참조")]
    [Tooltip("20초 타이머")]
    public CountdownTimer timer;

    [Tooltip("캐릭터 포물선 점프 연출 (선택)")]
    public CharacterJumpOnEnd jumper;

    [Tooltip("완료 시 끌 오브젝트(안내 UI 등, 선택)")]
    public GameObject Go;

    [Header("패널/드래그 제어")]
    [Tooltip("퍼즐 패널들(드래그 대상). 타임아웃 시 알파<1인 것만 비활성화")]
    public GameObject[] panels;

    [Tooltip("타이머 0초가 되었을 때도 점프 연출을 실행할지 여부")]
    public bool triggerJumpOnTimeout = true;

    private int matchedCount = 0;     // 현재까지 맞춘 개수
    private bool completed = false;   // 완료(퍼즐 or 타이머) 처리 플래그

    void Awake()
    {
        if (timer == null)
        {
            timer = FindObjectOfType<CountdownTimer>();
            if (timer == null) Debug.LogWarning("[퍼즐] CountdownTimer 없음");
        }
        if (jumper == null)
        {
            jumper = FindObjectOfType<CharacterJumpOnEnd>();
            // 없어도 동작은 가능 (점프 연출만 생략)
        }
    }

    void Update()
    {
        if (completed) return;

        // 타이머 0초 → 실패 처리 (게임 전체 정지 아님)
        if (timer != null && timer.GetRemainingSeconds() <= 0f)
        {
            Debug.Log("[퍼즐] 타이머 종료 → 드래그 차단 + 못 맞춘 패널 비활성화");
            LockDragging();           // 드래그 못 하게
            DisableUnmatchedPanels(); // 못 맞춘 패널만 끄기

            // 필요 시 점프 연출
            if (triggerJumpOnTimeout && jumper != null) jumper.TriggerJump();

            // 타이머는 멈춰 둔다(표시 고정)
            timer.StopTimer();

            completed = true; // 중복 실행 방지
        }
    }

    /// <summary>드래그 조각이 매칭 성공했을 때 호출</summary>
    public void ReportMatchOnce()
    {
        if (completed) return;

        matchedCount++;
        Debug.Log($"[퍼즐] 매칭 성공: {matchedCount} / {totalMatchesNeeded}");

        if (matchedCount >= totalMatchesNeeded)
        {
            CompletePuzzleByMatching();
        }
    }

    /// <summary>모든 매칭 완료 처리</summary>
    private void CompletePuzzleByMatching()
    {
        completed = true;

        if (Go != null) Go.SetActive(false);
        if (timer != null) timer.StopTimer();

        // 퍼즐 완료 시에도 드래그는 더 못 하게 잠금
        LockDragging();

        // 모든 패널은 이미 맞춰졌으니 굳이 끌 필요 없지만,
        // 필요하면 여기서 후처리 추가 가능

        if (jumper != null) jumper.TriggerJump();

        Debug.Log("[퍼즐] 모든 매칭 완료 → 타이머 정지 & 점프(선택)");
    }

    /// <summary>
    /// 드래그 차단: 모든 MouseDragSpringCheck를 찾아 입력을 막는다
    /// </summary>
    private void LockDragging()
    {
        var drags = FindObjectsOfType<MouseDragSpringCheck>(true);
        foreach (var d in drags)
        {
            d.dragEnabled = false; // 내부 체크용
            d.enabled = false;     // 스크립트 자체 비활성화 (OnMouse 이벤트도 차단)
        }
    }

    /// <summary>
    /// 못 맞춘 패널만 비활성화(알파<1)
    /// </summary>
    private void DisableUnmatchedPanels()
    {
        if (panels == null) return;

        foreach (var panel in panels)
        {
            if (panel == null) continue;

            var sr = panel.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                panel.SetActive(false);
            }
        }
    }

    public bool IsCompleted() => completed;
}
