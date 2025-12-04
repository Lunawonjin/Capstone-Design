using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class SolFinalBalloonInteractor : MonoBehaviour
{
    [Header("플레이어 설정")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("Dialogue UI 루트")]
    [Tooltip("대사 UI 전체를 담고 있는 Panel/Canvas 오브젝트 (필요하면 연결)")]
    [SerializeField] private GameObject dialogueUIRoot;

    [Tooltip("대사 종료 시 Dialogue UI 루트도 함께 비활성화할지 여부")]
    [SerializeField] private bool deactivateDialogueUIOnEnd = false;

    [Header("UI 참조")]
    [Tooltip("대사 진행 동안 켜질 배경 이미지(말풍선 배경)")]
    [SerializeField] private Image backGroundImage;

    [Tooltip("DialogueRunnerStringTables 컴포넌트")]
    [SerializeField] private DialogueRunnerStringTables dialogueRunner;

    [Header("대사 이벤트 이름(테이블 앞부분 이름)")]
    [Tooltip("Sol_FinalGame_Dialogue 테이블을 쓰려면 여기에는 Sol_FinalGame 으로 설정해야 함")]
    [SerializeField] private string eventName = "Sol_FinalGame";

    // 플레이어와 충돌 중인지 여부
    private bool isPlayerColliding = false;

    // 대사가 이미 시작되었는지 여부(중복 시작 방지)
    private bool dialogueStarted = false;

    private void Awake()
    {
        // 시작 시 배경 이미지는 꺼 두기
        if (backGroundImage != null)
        {
            backGroundImage.gameObject.SetActive(false);
        }

        if (dialogueRunner != null)
        {
            // 이벤트 중복 등록 방지 후 재등록
            dialogueRunner.OnDialogueEnded -= HandleDialogueEnded;
            dialogueRunner.OnDialogueEnded += HandleDialogueEnded;

            dialogueRunner.OnKeyShown -= HandleKeyShown;
            dialogueRunner.OnKeyShown += HandleKeyShown;

            Debug.Log("[SolFinalBalloonInteractor] DialogueRunnerStringTables 연결 완료.");
        }
        else
        {
            Debug.LogError("[SolFinalBalloonInteractor] DialogueRunnerStringTables 참조가 비어 있습니다. 인스펙터에서 연결해야 합니다.");
        }
    }

    private void OnDestroy()
    {
        if (dialogueRunner != null)
        {
            dialogueRunner.OnDialogueEnded -= HandleDialogueEnded;
            dialogueRunner.OnKeyShown -= HandleKeyShown;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = true;
            Debug.Log("[SolFinalBalloonInteractor] 플레이어와 충돌 시작.");
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            isPlayerColliding = false;
            Debug.Log("[SolFinalBalloonInteractor] 플레이어와 충돌 종료.");
        }
    }

    private void Update()
    {
        // 플레이어와 충돌 중이 아니면 무시
        if (!isPlayerColliding)
            return;

        // 이미 대사가 진행 중이면 다시 시작하지 않음
        if (dialogueStarted)
            return;

        // F 키 입력으로 상호작용
        if (Input.GetKeyDown(interactKey))
        {
            Debug.Log("[SolFinalBalloonInteractor] F 키 입력 감지, 대사 시작 시도.");
            StartDialogue();
        }
    }

    private void StartDialogue()
    {
        dialogueStarted = true;

        // 1) 먼저 Dialogue UI 루트를 활성화
        if (dialogueUIRoot != null && !dialogueUIRoot.activeSelf)
        {
            dialogueUIRoot.SetActive(true);
            Debug.Log("[SolFinalBalloonInteractor] Dialogue UI Root 활성화.");
        }

        // 2) 배경 이미지 활성화
        if (backGroundImage != null)
        {
            backGroundImage.gameObject.SetActive(true);
            Debug.Log("[SolFinalBalloonInteractor] BackGround Image 활성화.");
        }

        // 3) 대사 러너 활성화 및 시작
        if (dialogueRunner != null)
        {
            if (!dialogueRunner.gameObject.activeSelf)
            {
                Debug.Log("[SolFinalBalloonInteractor] DialogueRunner 오브젝트 비활성 상태, 활성화합니다.");
                dialogueRunner.gameObject.SetActive(true);
            }

            if (string.IsNullOrWhiteSpace(eventName))
            {
                Debug.LogError("[SolFinalBalloonInteractor] eventName 이 비어 있습니다. 예: Sol_FinalGame");
                return;
            }

            Debug.Log("[SolFinalBalloonInteractor] BeginWithEventName 호출: " + eventName);
            // Sol_FinalGame_Dialogue 테이블 사용
            // eventName = "Sol_FinalGame" 이면 내부에서 "Sol_FinalGame_Dialogue"를 찾음
            dialogueRunner.BeginWithEventName(eventName);
        }
        else
        {
            Debug.LogError("[SolFinalBalloonInteractor] DialogueRunnerStringTables 컴포넌트가 없습니다.");
        }
    }

    // 각 키가 출력될 때마다 호출
    private void HandleKeyShown(string key)
    {
        Debug.Log("[SolFinalBalloonInteractor] 대사 키 출력: " + key);
    }

    // DialogueRunnerStringTables.OnDialogueEnded 이벤트에서 호출됨
    private void HandleDialogueEnded()
    {
        Debug.Log("[SolFinalBalloonInteractor] 대사 종료 이벤트 수신.");

        // 대사가 끝나면 배경 이미지 비활성화
        if (backGroundImage != null)
        {
            backGroundImage.gameObject.SetActive(false);
            Debug.Log("[SolFinalBalloonInteractor] BackGround Image 비활성화.");
        }

        // 옵션: Dialogue UI 루트까지 같이 끄고 싶으면 true로 설정
        if (deactivateDialogueUIOnEnd && dialogueUIRoot != null)
        {
            dialogueUIRoot.SetActive(false);
            Debug.Log("[SolFinalBalloonInteractor] Dialogue UI Root 비활성화.");
        }

        // 다시 상호작용 가능하게 리셋
        dialogueStarted = false;
    }
}
