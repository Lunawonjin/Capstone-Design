using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManger : MonoBehaviour
{
    [Header("캐릭터 할당")]
    public GameObject saltCharacter;   // Salt 캐릭터 오브젝트
    public GameObject hunterCharacter; // Hunter 캐릭터 오브젝트

    [Header("이동 설정")]
    public float saltShift = -1.8f;    // Salt 캐릭터 Y 이동량
    public float hunterShift = -3.8f;  // Hunter 캐릭터 Y 이동량
    public float duration = 1f;        // 이동에 걸리는 시간 (초)

    [Header("타일맵 설정")]
    public GameObject tilemap;         // 움직일 타일맵
    public float tilemapSpeed = 1f;    // 타일맵 내려가는 속도

    void Start()
    {
        // 게임 시작 시 캐릭터 코루틴 실행
        StartCoroutine(ShiftAfterDelay());
    }

    void Update()
    {
        // 타일맵이 계속 일정한 속도로 아래로 이동
        if (tilemap != null)
        {
            tilemap.transform.Translate(Vector3.down * tilemapSpeed * Time.deltaTime);
        }
    }

    private IEnumerator ShiftAfterDelay()
    {
        // 3초 대기
        yield return new WaitForSeconds(3f);

        // Salt 캐릭터 이동 코루틴 실행
        if (saltCharacter != null)
        {
            StartCoroutine(SmoothMoveY(saltCharacter, saltShift));
        }

        // Hunter 캐릭터 이동 코루틴 실행
        if (hunterCharacter != null)
        {
            StartCoroutine(SmoothMoveY(hunterCharacter, hunterShift));
        }
    }

    /// <summary>
    /// 대상 캐릭터를 지정한 Y만큼 duration 동안 부드럽게 이동
    /// </summary>
    private IEnumerator SmoothMoveY(GameObject character, float yShift)
    {
        // 시작 위치
        Vector3 startPos = character.transform.position;
        // 목표 위치 (Y만큼 이동)
        Vector3 targetPos = new Vector3(startPos.x, startPos.y + yShift, startPos.z);

        float elapsed = 0f;

        // duration 동안 서서히 이동
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration); // 0~1 비율
            character.transform.position = Vector3.Lerp(startPos, targetPos, t);
            yield return null;
        }

        // 마지막에 목표 위치 정확히 맞춤
        character.transform.position = targetPos;
    }
}
