using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// 게임 시작 전 3초 동안:
/// - 플레이어 이동 비활성화
/// - 드래그 불가
/// - 20초 카운트다운 정지(시작 일시정지 상태 권장)
/// - 랜덤 색/태그/ID 적용도 보류
/// 3초가 지나면:
/// - 랜덤 할당 실행
/// - 드래그/플레이 해제
/// - 20초 타이머 시작(재개)
/// </summary>
public class GameStartCountdown : MonoBehaviour
{
    [Header("카운트다운 설정")]
    public float countdownTime = 3f;          // 대기 시간(초)
    public TextMeshProUGUI countdownText;     // 중앙에 보여줄 "3,2,1,START!"

    [Header("제어 대상")]
    public PlayerMove player;                 // 플레이어 이동 스크립트
    public MouseDragSpringCheck[] draggablePieces;   // 드래그 가능한 퍼즐 조각
    public CountdownTimer mainTimer;          // 20초 타이머(시작시 startPaused=true 권장)
    public PieceRandomizer randomizer;        // 랜덤 색/태그/ID 적용 매니저

    private void Start()
    {
        // 1) 입력/드래그 막기
        if (player != null) player.enabled = false;
        if (draggablePieces != null)
        {
            foreach (var p in draggablePieces)
                if (p != null) p.dragEnabled = false;
        }

        // 2) 20초 타이머 정지(안전망). 인스펙터에서 startPaused=true로 둔 상태면 그대로 유지.
        if (mainTimer != null) mainTimer.StopTimer();

        // 3) 랜덤 적용 보류: randomizer.autoAssignOnStart = false 로 둔다.
        //    (Start에서 이미 실행되지 않았을 것)

        // 카운트다운 코루틴 시작
        StartCoroutine(DoCountdownThenStart());
    }

    private IEnumerator DoCountdownThenStart()
    {
        float remain = Mathf.Max(0f, countdownTime);

        while (remain > 0f)
        {
            if (countdownText != null)
                countdownText.text = Mathf.CeilToInt(remain).ToString();

            yield return new WaitForSeconds(1f);
            remain -= 1f;
        }

        // "START!" 잠깐 표시
        if (countdownText != null) countdownText.text = "Go!";
        yield return new WaitForSeconds(0.5f);
        if (countdownText != null) countdownText.gameObject.SetActive(false);

        // ---------- 여기서 모든 것 해제/실행 ----------
        // A) 랜덤 색/태그/ID 적용 (이제 보이게)
        if (randomizer != null) randomizer.AssignRandomSpecs();

        // B) 드래그 허용
        if (draggablePieces != null)
        {
            foreach (var p in draggablePieces)
                if (p != null) p.dragEnabled = true;
        }

        // C) 플레이어 이동 허용
        if (player != null) player.enabled = true;

        // D) 20초 타이머 시작(재개)
        if (mainTimer != null) mainTimer.ResumeTimer();

        Debug.Log("[시작카운트다운] 해제 완료 → 게임 시작");
    }
}
