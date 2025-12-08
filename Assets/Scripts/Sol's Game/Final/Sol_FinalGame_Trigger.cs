using System.Collections;
using UnityEngine;
using UnityEngine.UI; // UI 관련 기능을 위해 필수

[RequireComponent(typeof(Collider2D))]
public class SolFinalGameTrigger : MonoBehaviour
{
    [Header("입력 키")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("페이드 효과 설정")]
    [SerializeField] private CanvasGroup fadeOverlay;
    [SerializeField] private float fadeDuration = 1.0f;
    [SerializeField] private float blackScreenDuration = 2.0f;

    [Header("씬 오브젝트 전환")]
    [SerializeField] private GameObject solsHouse;
    [SerializeField] private GameObject solsLivingRoom;

    [Header("UI 제어")]
    [Tooltip("전환 완료 후 비활성화할 UI (예: 인게임 HUD)")]
    [SerializeField] private GameObject uiPanel;

    [Header("대사 시스템")]
    [SerializeField] private DialogueRunnerStringTables dialogueRunner;
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private string livingRoomDialogueEvent = "Boss_Sol_FinalGame_First";

    [Header("플레이어 및 NPC")]
    [SerializeField] private Color playerFadeColor = new Color(0.49f, 0.49f, 0.49f, 1f);
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private SpriteRenderer playerSpriteRenderer;

    // 자동 찾기 옵션들
    [SerializeField] private bool autoFindReferences = true;

    private bool _playerInside = false;
    private bool _isTransitioning = false;
    private GameObject _playerGO;

    void Start()
    {
        if (autoFindReferences) FindReferences();

        // 시작 시 오버레이 끄기
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (_playerInside && !_isTransitioning && Input.GetKeyDown(interactKey))
        {
            StartCoroutine(TransitionRoutine());
        }
    }

    IEnumerator TransitionRoutine()
    {
        _isTransitioning = true;

        // 1. 플레이어 조작 비활성화
        PlayerMove playerMove = null;
        if (_playerGO != null)
        {
            playerMove = _playerGO.GetComponent<PlayerMove>();
            if (playerMove != null) playerMove.SetControlEnabled(false);
        }

        // 2. 페이드 아웃 (화면 어두워짐)
        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
            fadeOverlay.alpha = 0f;
        }

        yield return StartCoroutine(Fade(0f, 1f));

        // 3. 오브젝트 교체 (검은 화면 상태)
        // 주의: 여기서 solsHouse를 끌 때, 이 스크립트가 solsHouse의 자식이면 코루틴이 멈춥니다!
        SwapObjectsWaitUI();

        // 4. 검은 화면 유지 (Realtime 사용으로 일시정지 무시)
        yield return new WaitForSecondsRealtime(blackScreenDuration);

        // 5. 페이드 인 (화면 밝아짐)
        yield return StartCoroutine(Fade(1f, 0f));

        // 6. 페이드 종료 후 처리
        if (fadeOverlay != null) fadeOverlay.gameObject.SetActive(false);

        // UI 패널은 페이드가 완전히 끝난 뒤에 끕니다. 
        // (만약 fadeOverlay가 uiPanel 자식이라면, 미리 껐을 때 페이드 효과가 사라지기 때문)
        if (uiPanel != null) uiPanel.SetActive(false);

        // 7. 대사 시작
        StartDialogue();

        // 8. 플레이어 컨트롤 복구
        if (playerMove != null)
        {
            playerMove.SetControlEnabled(true);
            playerMove.Unfreeze(keepAnimatorState: false);
        }

        _isTransitioning = false;

        // 트리거 비활성화
        gameObject.SetActive(false);
    }

    private void SwapObjectsWaitUI()
    {
        if (solsHouse != null) solsHouse.SetActive(false);
        if (solsLivingRoom != null) solsLivingRoom.SetActive(true);
        // uiPanel은 여기서 끄지 않고 페이드 인이 끝난 뒤에 끕니다.

        ApplyPlayerColor();

        // NPC 위치 조정
        GameObject bossNpc = GameObject.Find("Boss_Npc") ?? GameObject.Find("Boss");
        if (bossNpc != null) bossNpc.transform.position = new Vector3(31.5f, 20f, bossNpc.transform.position.z);

        GameObject solNpc = GameObject.Find("Sol_Npc") ?? GameObject.Find("Sol");
        if (solNpc != null) solNpc.transform.position = new Vector3(34.24f, 21.1f, solNpc.transform.position.z);
    }

    private void StartDialogue()
    {
        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (dialogueRunner != null && !string.IsNullOrEmpty(livingRoomDialogueEvent))
        {
            dialogueRunner.BeginWithEventName(livingRoomDialogueEvent);
        }
    }

    IEnumerator Fade(float start, float end)
    {
        if (fadeOverlay == null) yield break;

        float timer = 0f;
        fadeOverlay.blocksRaycasts = (end > 0); // 어두워질 때만 클릭 차단

        while (timer < fadeDuration)
        {
            // unscaledDeltaTime을 사용하여 게임 시간이 멈춰도 페이드 진행
            timer += Time.unscaledDeltaTime;
            fadeOverlay.alpha = Mathf.Lerp(start, end, timer / fadeDuration);
            yield return null;
        }
        fadeOverlay.alpha = end;
    }

    // --- 유틸리티 및 초기화 ---
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(playerTag))
        {
            _playerInside = true;
            _playerGO = other.gameObject;
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(playerTag))
        {
            _playerInside = false;
            _playerGO = null;
        }
    }

    private void FindReferences()
    {
        if (solsHouse == null) solsHouse = GameObject.Find("Sol's House");
        if (solsLivingRoom == null) solsLivingRoom = GameObject.Find("Sol's Living Room");
        if (uiPanel == null) uiPanel = GameObject.Find("UIPanel");

        if (dialogueRunner == null) dialogueRunner = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);
        if (dialoguePanel == null && dialogueRunner != null) dialoguePanel = dialogueRunner.gameObject;
    }

    private void ApplyPlayerColor()
    {
        if (_playerGO == null) return;
        var srs = _playerGO.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in srs) sr.color = playerFadeColor;
    }
}