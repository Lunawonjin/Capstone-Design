using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))] // 책장에 2D 콜라이더 필수, isTrigger는 끄기
public class BookshelfDiaryInteractor : MonoBehaviour
{
    [Header("플레이어 설정")]
    [SerializeField] private string playerTag = "Player";    // 플레이어 태그
    [SerializeField] private KeyCode interactKey = KeyCode.F;    // 책장 상호작용 키
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;  // 일기 닫기 키

    [Header("UI 참조")]
    [Tooltip("책장과 상호작용 시 켜질 Diary 이미지(UI Image)")]
    [SerializeField] private Image diaryImage;

    [Tooltip("Esc로 Diary를 끌 때 함께 비활성화할 Key 이미지(UI Image)")]
    [SerializeField] private Image keyImage;

    // 플레이어와 책장이 실제로 부딪혀 있는지 여부
    private bool isPlayerColliding = false;

    public static bool HasInteractedWithBookshelf = false;

    // Diary가 현재 열려 있는지 여부
    private bool isDiaryOpen = false;

    private void Awake()
    {
        // 시작할 때 Diary는 꺼 둔다
        if (diaryImage != null)
        {
            diaryImage.gameObject.SetActive(false);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 플레이어가 책장과 충돌 시작
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = true;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        // 플레이어가 책장에서 떨어짐
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = false;
        }
    }

    private void Update()
    {
        // 1) Diary가 열려 있을 때 Esc 입력 → Diary 닫기 + Key 비활성화
        if (isDiaryOpen && Input.GetKeyDown(closeKey))
        {
            CloseDiaryAndKey();
            return;
        }

        // 2) 책장과 부딪힌 상태가 아니면 F 입력은 무시
        if (!isPlayerColliding)
            return;

        // 3) Diary가 닫혀 있을 때만 F로 열 수 있음 (연타 방지)
        if (!isDiaryOpen && Input.GetKeyDown(interactKey))
        {
            OpenDiary();
        }
    }

    // Diary 이미지 켜기
    private void OpenDiary()
    {
        // 책장과 한 번이라도 상호작용했음을 기록
        HasInteractedWithBookshelf = true;

        if (diaryImage == null)
        {
            Debug.LogWarning("[BookshelfDiaryInteractor] Diary Image가 인스펙터에 연결되지 않았습니다.");
            return;
        }

        diaryImage.gameObject.SetActive(true);
        isDiaryOpen = true;
        Debug.Log("[BookshelfDiaryInteractor] Diary 열림.");
    }


    // Diary 끄고 Key도 같이 끄기
    private void CloseDiaryAndKey()
    {
        if (diaryImage != null)
        {
            diaryImage.gameObject.SetActive(false);
        }

        if (keyImage != null)
        {
            keyImage.gameObject.SetActive(false);
        }

        isDiaryOpen = false;
        Debug.Log("[BookshelfDiaryInteractor] Diary 닫힘, Key 비활성화.");
    }
}
