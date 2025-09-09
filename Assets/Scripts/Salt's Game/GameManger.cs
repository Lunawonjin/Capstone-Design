using System.Collections;
using UnityEngine;

public class GameManger : MonoBehaviour
{
    [Header("캐릭터 할당")]
    public GameObject saltCharacter;
    public GameObject hunterCharacter;

    [Header("이동 설정")]
    public float saltShift = -1.8f;
    public float hunterShift = -3.8f;
    public float duration = 1f;

    [Header("타일맵 설정")]
    public GameObject tilemap;
    public float tilemapSpeed = 1f;

    [Header("점프 제어(필수 아님)")]
    [Tooltip("Salt에 붙은 PlayerSaltJumpPhysics. 비워두면 자동 검색.")]
    public PlayerSaltJumpPhysics saltJump;

    [Header("그림자(선택)")]
    [Tooltip("Salt의 그림자 Transform (여기에 넣으면 Salt 이동과 함께 그림자도 부드럽게 이동)")]
    public Transform saltShadow;

    void Start()
    {
        // saltJump 자동 연결
        if (!saltJump && saltCharacter)
            saltJump = saltCharacter.GetComponent<PlayerSaltJumpPhysics>();

        StartCoroutine(ShiftAfterDelay());
    }

    void Update()
    {
        if (tilemap != null)
            tilemap.transform.Translate(Vector3.down * tilemapSpeed * Time.deltaTime);
    }

    private IEnumerator ShiftAfterDelay()
    {
        // 3초 대기
        yield return new WaitForSeconds(3f);

        // ★ 이동 동안 그림자 Y 고정 해제 (점프 스크립트가 잠깐 손 떼게)
        if (saltJump) saltJump.BeginGroundShift();

        // Salt/Hunter 이동 시작 (Salt는 그림자 포함해서 이동)
        if (saltCharacter) StartCoroutine(SmoothMoveY(saltCharacter.transform, saltShift, saltShadow));
        if (hunterCharacter) StartCoroutine(SmoothMoveY(hunterCharacter.transform, hunterShift, null));

        // 이동 시간만큼 대기
        yield return new WaitForSeconds(duration);

        // ★ 이동 끝: 새 지면으로 재설정 + 그림자 Y 고정 재개 + 점프 허용
        if (saltJump)
        {
            saltJump.EndGroundShiftAndRebase(); // 새 groundY/그림자 잠금Y 반영
            saltJump.UnlockJump();              // 이제 점프 가능
        }
        else
        {
            Debug.LogWarning("[GameManger] saltJump가 비어 있어 Rebase/Unlock을 못했습니다. PlayerSaltJumpPhysics를 Salt에 붙이세요.");
        }
    }

    /// <summary>
    /// 대상 tr을 yShift만큼 duration 동안 Lerp. shadow가 있으면 같은 비율로 함께 이동.
    /// </summary>
    private IEnumerator SmoothMoveY(Transform tr, float yShift, Transform shadow)
    {
        Vector3 startPos = tr.position;
        Vector3 targetPos = new Vector3(startPos.x, startPos.y + yShift, startPos.z);

        Vector3 shStart = Vector3.zero, shTarget = Vector3.zero;
        bool moveShadow = shadow != null;
        if (moveShadow)
        {
            shStart = shadow.position;
            shTarget = new Vector3(shStart.x, shStart.y + yShift, shStart.z);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            tr.position = Vector3.Lerp(startPos, targetPos, t);

            if (moveShadow)
                shadow.position = Vector3.Lerp(shStart, shTarget, t);

            yield return null;
        }

        tr.position = targetPos;
        if (moveShadow) shadow.position = shTarget;
    }
}
