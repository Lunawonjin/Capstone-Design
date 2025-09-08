using UnityEngine;
using System.Collections;

/// <summary>
/// Salt와 Hunter를 A/D로 3레인(-2,0,2)에서만 스무스 이동
/// - Hunter는 Salt보다 DelayTime(초) 늦게 같은 레인으로 이동
/// - 연타 시 이전 Hunter 딜레이 코루틴을 취소하여 최신 입력만 반영
/// - Freeze() 호출 시 입력/이동이 정지되며 코루틴도 정리됨
/// </summary>
public class PlayerRunMove : MonoBehaviour
{
    [Header("캐릭터 할당")]
    public Transform saltCharacter;    // Salt 캐릭터
    public Transform hunterCharacter;  // Hunter 캐릭터

    [Header("Lane 설정")]
    public float laneStep = 2f;        // 한 칸 간격(기본 2 → -2, 0, 2)
    public float moveSpeed = 5f;       // 스무스 이동 속도(Lerp 계수)

    [Header("Hunter 딜레이")]
    [Tooltip("Hunter가 Salt보다 얼마나 늦게 같은 레인으로 이동할지(초)")]
    public float DelayTime = 0.5f;     // 요구사항: 기본 0.5초

    [Header("스냅/정밀도")]
    [Tooltip("목표 X에 이 값 이하로 가까워지면 정확히 스냅")]
    public float snapEpsilon = 0.01f;

    [Header("입력 제어")]
    [Tooltip("false면 입력/이동을 모두 정지 (GameOver 등에서 Freeze() 호출)")]
    public bool inputEnabled = true;

    // 내부 상태
    private int laneIndex = 0;         // 현재 레인(-1, 0, 1)
    private float saltTargetX;         // Salt 목표 X
    private float hunterTargetX;       // Hunter 목표 X
    private Coroutine hunterDelayRoutine; // Hunter 딜레이 코루틴 핸들

    void Start()
    {
        // 안전 가드: 미할당 시 자기 자신을 Salt로 간주
        if (saltCharacter == null) saltCharacter = this.transform;

        // 시작 레인 스냅(Salt 기준)
        laneIndex = Mathf.RoundToInt(saltCharacter.position.x / laneStep);
        laneIndex = Mathf.Clamp(laneIndex, -1, 1);

        saltTargetX = laneIndex * laneStep;
        saltCharacter.position = new Vector3(saltTargetX, saltCharacter.position.y, saltCharacter.position.z);

        // Hunter도 같은 레인에서 시작
        if (hunterCharacter != null)
        {
            hunterTargetX = laneIndex * laneStep;
            hunterCharacter.position = new Vector3(hunterTargetX, hunterCharacter.position.y, hunterCharacter.position.z);
        }
    }

    void Update()
    {
        if (!inputEnabled) return; // 입력/이동 차단

        // A/D 입력
        if (Input.GetKeyDown(KeyCode.A)) MoveLane(-1);
        else if (Input.GetKeyDown(KeyCode.D)) MoveLane(+1);

        // Salt 이동 (스무스 + 스냅)
        if (saltCharacter != null)
        {
            float curX = saltCharacter.position.x;
            float newX = Mathf.Lerp(curX, saltTargetX, Time.deltaTime * moveSpeed);
            if (Mathf.Abs(newX - saltTargetX) <= snapEpsilon) newX = saltTargetX;
            saltCharacter.position = new Vector3(newX, saltCharacter.position.y, saltCharacter.position.z);
        }

        // Hunter 이동 (스무스 + 스냅)
        if (hunterCharacter != null)
        {
            float curX = hunterCharacter.position.x;
            float newX = Mathf.Lerp(curX, hunterTargetX, Time.deltaTime * moveSpeed);
            if (Mathf.Abs(newX - hunterTargetX) <= snapEpsilon) newX = hunterTargetX;
            hunterCharacter.position = new Vector3(newX, hunterCharacter.position.y, hunterCharacter.position.z);
        }
    }

    /// <summary>
    /// 레인을 delta(-1 또는 +1)만큼 이동 (최종 범위 -1~1)
    /// </summary>
    private void MoveLane(int delta)
    {
        int newIndex = Mathf.Clamp(laneIndex + delta, -1, 1);
        if (newIndex == laneIndex) return; // 더 못 움직이면 무시

        laneIndex = newIndex;

        // Salt 즉시 목표 갱신
        saltTargetX = laneIndex * laneStep;

        // Hunter는 딜레이 후 목표 갱신 (이전 코루틴 취소)
        if (hunterDelayRoutine != null)
        {
            StopCoroutine(hunterDelayRoutine);
            hunterDelayRoutine = null;
        }
        hunterDelayRoutine = StartCoroutine(DelayHunterMove(laneIndex));
    }

    /// <summary>
    /// Hunter 목표를 DelayTime 뒤에 갱신
    /// </summary>
    private IEnumerator DelayHunterMove(int targetLane)
    {
        yield return new WaitForSeconds(DelayTime);
        hunterTargetX = targetLane * laneStep;
        hunterDelayRoutine = null;
    }

    /// <summary>
    /// 외부(게임오버 등)에서 호출: 입력/이동 정지 + 딜레이 코루틴 정리
    /// </summary>
    public void Freeze(bool freeze = true)
    {
        inputEnabled = !freeze;

        // 코루틴 정지 및 현재 위치를 목표로 고정 → 더 이상 미끄러지지 않음
        if (freeze)
        {
            if (hunterDelayRoutine != null)
            {
                StopCoroutine(hunterDelayRoutine);
                hunterDelayRoutine = null;
            }
            if (saltCharacter != null)
                saltTargetX = saltCharacter.position.x;
            if (hunterCharacter != null)
                hunterTargetX = hunterCharacter.position.x;
        }
    }

    void OnDisable()
    {
        // 스크립트가 비활성화될 때도 코루틴 안전 정리
        if (hunterDelayRoutine != null)
        {
            StopCoroutine(hunterDelayRoutine);
            hunterDelayRoutine = null;
        }
    }
}
