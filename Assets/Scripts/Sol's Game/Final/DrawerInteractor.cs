using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class DrawerInteractor : MonoBehaviour
{
    [Header("플레이어 설정")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("대화 패널(DPanel)")]
    [Tooltip("대화용 패널 루트 오브젝트 (Image가 붙은 Panel GameObject)")]
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
    private bool isPlayerColliding = false; // 플레이어가 서랍과 부딪혀 있는지
    private bool isPanelOpen = false;       // DPanel이 켜져 있는지

    private enum DrawerState
    {
        Locked,         // 잠겨 있음 (열쇠 필요)
        Unlocked,       // 열렸음(팔찌 아직)
        BraceletTaken   // 팔찌까지 가져간 상태
    }

    private DrawerState state = DrawerState.Locked;

    private void Awake()
    {
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
        // 패널 켜져 있을 때: 마우스 클릭 / 스페이스 누르면 닫기
        if (isPanelOpen)
        {
            bool click = Input.GetMouseButtonDown(0);
            bool space = Input.GetKeyDown(KeyCode.Space);

            if (click || space)
            {
                ClosePanel();
            }
            return;
        }

        // 플레이어가 서랍이랑 안 부딪혀 있으면 F 무시
        if (!isPlayerColliding)
            return;

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
                // 팔찌까지 얻은 이후 추가 연출이 필요하면 여기서 처리
                break;
        }
    }

    // 잠긴 상태에서 F 눌렀을 때
    private void HandleLockedState()
    {
        // 책장과 아직 상호작용하지 않았다면: 텍스트만 띄우고 진짜로는 안 열림
        if (!BookshelfDiaryInteractor.HasInteractedWithBookshelf)
        {
            OpenPanel("열쇠가 필요한 모양이다.\n열쇠를 찾아보자");
            Debug.Log("[DrawerInteractor] 열쇠 없음: 안내 텍스트만 출력.");
            // state는 그대로 Locked 유지
            return;
        }

        // 책장과 상호작용한 이후라면: 텍스트 없이 바로 열림 처리만
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

        state = DrawerState.Unlocked;
        Debug.Log("[DrawerInteractor] 서랍이 열렸습니다.(책장 상호작용 후)");
    }

    // 서랍 열린 상태에서 F 한 번 더 눌렀을 때(팔찌 획득)
    private void HandleUnlockedState_FirstBracelet()
    {
        if (braceletObject != null)
        {
            braceletObject.SetActive(false);
        }

        OpenPanel("소원이 이루어진다는 팔찌.\n솔이에게 가져다 주자");

        state = DrawerState.BraceletTaken;
        Debug.Log("[DrawerInteractor] 팔찌를 획득했습니다.");
    }

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

    private void ClosePanel()
    {
        if (dPanelRoot != null)
        {
            dPanelRoot.SetActive(false);
        }

        isPanelOpen = false;
    }
}