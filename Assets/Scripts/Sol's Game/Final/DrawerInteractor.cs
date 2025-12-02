using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))] // 서랍장에 2D 콜라이더 필수, isTrigger는 끄기
public class DrawerInteractor : MonoBehaviour
{
    [Header("플레이어 설정")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("대화 패널(DPanel)")]
    [Tooltip("대화용 패널(이미지 루트 오브젝트, 보통 Image가 붙어있는 Panel)")]
    [SerializeField] private GameObject dPanelRoot;

    [Tooltip("DPanel 안에 있는 텍스트 (TMP)")]
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("서랍 / 팔찌 오브젝트")]
    [Tooltip("서랍이 열릴 때 비활성화할 오브젝트 (예: 서랍장_열림)")]
    [SerializeField] private GameObject drawerOpenObject;

    [Tooltip("팔찌 오브젝트 (팔찌를 얻으면 비활성화)")]
    [SerializeField] private GameObject braceletObject;

    [Header("사운드")]
    [Tooltip("철컥 소리를 재생할 AudioSource")]
    [SerializeField] private AudioSource sfxSource;

    [Tooltip("철컥 사운드 클립")]
    [SerializeField] private AudioClip unlockClip;

    // 내부 상태
    private bool isPlayerColliding = false; // 플레이어가 서랍장과 실제로 부딪혀 있는지
    private bool isPanelOpen = false;       // DPanel이 켜져 있는지

    // 서랍 상태
    private enum DrawerState
    {
        Locked,         // 열쇠 필요 (책장 전)
        Unlocked,       // 책장 상호작용 후, 서랍만 열린 상태(팔찌 아직)
        BraceletTaken   // 팔찌까지 이미 가져간 상태
    }

    private DrawerState state = DrawerState.Locked;

    private void Awake()
    {
        // 시작할 때 패널은 꺼둔다
        if (dPanelRoot != null)
        {
            dPanelRoot.SetActive(false);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = true;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = false;
        }
    }

    private void Update()
    {
        // 1) 패널이 열려 있을 때 마우스 클릭 또는 스페이스바 → 패널 닫기
        if (isPanelOpen)
        {
            bool click = Input.GetMouseButtonDown(0);
            bool space = Input.GetKeyDown(KeyCode.Space);

            if (click || space)
            {
                ClosePanel();
            }

            // 패널 열려 있을 때는 서랍 상호작용 입력은 더 이상 처리하지 않음
            return;
        }

        // 2) 플레이어가 서랍장과 부딪힌 상태가 아니면 F 입력 무시
        if (!isPlayerColliding)
            return;

        // 3) F로 상태에 따라 상호작용
        if (Input.GetKeyDown(interactKey))
        {
            HandleInteract();
        }
    }

    private void HandleInteract()
    {
        switch (state)
        {
            case DrawerState.Locked:
                HandleLockedState();
                break;

            case DrawerState.Unlocked:
                HandleUnlockedState_FirstBracelet();
                break;

            case DrawerState.BraceletTaken:
                // 팔찌까지 이미 가져간 이후에는 별도 행동이 필요 없으면 아무 것도 안 함
                // 필요하면 여기서 "이제 더 이상 남은 것이 없다" 같은 연출 추가 가능
                break;
        }
    }

    // 상태 1: 아직 열쇠를 얻지 못했을 때
    private void HandleLockedState()
    {
        // 책장과 상호작용한 적이 없다면: "열쇠가 필요한 모양이다. 열쇠를 찾아보자"
        if (!BookshelfDiaryInteractor.HasInteractedWithBookshelf)
        {
            OpenPanel("열쇠가 필요한 모양이다.\n열쇠를 찾아보자");
            return;
        }

        // 책장과 상호작용한 이후라면: 서랍을 여는 처리
        // 철컥 사운드
        if (sfxSource != null && unlockClip != null)
        {
            sfxSource.PlayOneShot(unlockClip);
        }

        // 서랍장_열림 오브젝트 비활성화
        if (drawerOpenObject != null)
        {
            drawerOpenObject.SetActive(false);
        }

        // 서랍이 이제 열린 상태가 됨
        state = DrawerState.Unlocked;
        Debug.Log("[DrawerInteractor] 서랍이 열렸습니다.");
    }

    // 상태 2: 서랍은 열려 있고, 팔찌는 아직 안 가져간 상태
    private void HandleUnlockedState_FirstBracelet()
    {
        // 팔찌 오브젝트 비활성화(플레이어가 가져간 것으로 처리)
        if (braceletObject != null)
        {
            braceletObject.SetActive(false);
        }

        // DPanel에 팔찌 설명 출력
        OpenPanel("소원이 이루어진다는 팔찌.\n솔이에게 가져다 주자");

        // 이제 팔찌까지 획득한 상태로 전환
        state = DrawerState.BraceletTaken;
    }

    // DPanel 열기 + 텍스트 설정
    private void OpenPanel(string message)
    {
        if (dPanelRoot != null)
        {
            dPanelRoot.SetActive(true);
        }

        if (dialogueText != null)
        {
            dialogueText.text = message;
        }

        isPanelOpen = true;
    }

    // DPanel 닫기
    private void ClosePanel()
    {
        if (dPanelRoot != null)
        {
            dPanelRoot.SetActive(false);
        }

        isPanelOpen = false;
    }
}
