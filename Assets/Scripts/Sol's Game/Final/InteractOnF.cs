using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))] // 콜라이더는 필요하지만 isTrigger는 건드리지 않음
public class InteractOnF_Collision2D : MonoBehaviour
{
    [Header("플레이어 태그 이름")]
    [SerializeField] private string playerTag = "Player";

    [Header("디버그용 오브젝트 이름")]
    [SerializeField] private string objectName = "InteractObject";

    // 플레이어와 부딪혀 있는 중인지 여부
    private bool isPlayerColliding = false;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 플레이어랑 처음 부딪혔을 때
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = true;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        // 플레이어가 떨어져 나갔을 때
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = false;
        }
    }

    private void Update()
    {
        // 안 부딪힌 상태면 무시
        if (!isPlayerColliding)
            return;

        // 부딪힌 상태에서 F를 눌렀을 때
        if (Input.GetKeyDown(KeyCode.F))
        {
            Debug.Log("상호작용성공 : " + objectName);
            // 여기 아래에 실제 상호작용 로직 추가
        }
    }
}
