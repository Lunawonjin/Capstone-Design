using System.Collections;
using UnityEngine;

/// <summary>
/// Boss_Sol_FinalGame 이벤트 후 활성화
/// 플레이어가 트리거 안에서 F키를 누르면 거실로 전환
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SolFinalGameTrigger : MonoBehaviour
{
    [Header("입력 키")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("씬 오브젝트 전환")]
    [SerializeField] private GameObject solsHouse;
    [SerializeField] private GameObject solsLivingRoom;
    [Tooltip("자동으로 'Sol's House' 오브젝트 찾기")]
    [SerializeField] private bool autoFindSolsHouse = true;
    [Tooltip("자동으로 'Sol's Living Room' 오브젝트 찾기")]
    [SerializeField] private bool autoFindLivingRoom = true;

    [Header("플레이어 색상")]
    [SerializeField] private Color playerFadeColor = new Color(0.49f, 0.49f, 0.49f, 1f); // #7D7D7D
    [SerializeField] private string playerTag = "Player";

    [Header("제거할 NPC")]
    [SerializeField] private string bossNpcName = "Boss_Npc";

    [Header("디버그")]
    [SerializeField] private bool verboseLog = true;

    private bool _playerInside = false;
    private GameObject _playerGO;
    private Collider2D _triggerCollider;

    void Awake()
    {
        _triggerCollider = GetComponent<Collider2D>();
        if (_triggerCollider)
        {
            _triggerCollider.isTrigger = true;
        }
    }

    void Start()
    {
        // 자동으로 오브젝트 찾기
        if (autoFindSolsHouse && solsHouse == null)
        {
            solsHouse = GameObject.Find("Sol's House");
            if (solsHouse && verboseLog)
                Debug.Log($"[SolFinalGameTrigger] Sol's House 자동 탐색 성공: {solsHouse.name}");
        }

        if (autoFindLivingRoom && solsLivingRoom == null)
        {
            solsLivingRoom = GameObject.Find("Sol's Living Room");
            if (solsLivingRoom && verboseLog)
                Debug.Log($"[SolFinalGameTrigger] Sol's Living Room 자동 탐색 성공: {solsLivingRoom.name}");
        }

        // 시작 시 비활성화 (이벤트 후 활성화됨)
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (_playerInside && Input.GetKeyDown(interactKey))
        {
            ExecuteFinalTransition();
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(playerTag))
        {
            _playerInside = true;
            _playerGO = other.gameObject;

            if (verboseLog)
                Debug.Log("[SolFinalGameTrigger] 플레이어 진입 - F키를 눌러 거실로 이동하세요.");
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(playerTag))
        {
            _playerInside = false;
            _playerGO = null;

            if (verboseLog)
                Debug.Log("[SolFinalGameTrigger] 플레이어 퇴장");
        }
    }

    /// <summary>
    /// 거실 전환 실행
    /// </summary>
    void ExecuteFinalTransition()
    {
        if (verboseLog)
            Debug.Log("[SolFinalGameTrigger] 거실 전환 시작!");

        // 1. Sol's House 비활성화
        if (solsHouse != null)
        {
            solsHouse.SetActive(false);
            if (verboseLog)
                Debug.Log("[SolFinalGameTrigger] Sol's House 비활성화");
        }
        else if (verboseLog)
        {
            Debug.LogWarning("[SolFinalGameTrigger] Sol's House를 찾을 수 없습니다.");
        }

        // 2. Sol's Living Room 활성화
        if (solsLivingRoom != null)
        {
            solsLivingRoom.SetActive(true);
            if (verboseLog)
                Debug.Log("[SolFinalGameTrigger] Sol's Living Room 활성화");
        }
        else if (verboseLog)
        {
            Debug.LogWarning("[SolFinalGameTrigger] Sol's Living Room을 찾을 수 없습니다.");
        }

        // 3. 플레이어 색상 변경 (#7D7D7D)
        if (_playerGO != null)
        {
            var spriteRenderer = _playerGO.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = _playerGO.GetComponentInChildren<SpriteRenderer>();

            if (spriteRenderer != null)
            {
                spriteRenderer.color = playerFadeColor;
                if (verboseLog)
                    Debug.Log($"[SolFinalGameTrigger] 플레이어 색상 변경: {ColorUtility.ToHtmlStringRGBA(playerFadeColor)}");
            }
            else if (verboseLog)
            {
                Debug.LogWarning("[SolFinalGameTrigger] 플레이어의 SpriteRenderer를 찾을 수 없습니다.");
            }
        }

        // 4. Boss NPC 제거
        GameObject bossNpc = GameObject.Find(bossNpcName);
        if (bossNpc != null)
        {
            Destroy(bossNpc);
            if (verboseLog)
                Debug.Log($"[SolFinalGameTrigger] {bossNpcName} 제거됨");
        }
        else if (verboseLog)
        {
            Debug.LogWarning($"[SolFinalGameTrigger] {bossNpcName}를 찾을 수 없습니다.");
        }

        // 5. 플레이어 컨트롤 해제
        if (_playerGO != null)
        {
            var playerMove = _playerGO.GetComponent<PlayerMove>();
            if (playerMove != null)
            {
                playerMove.SetControlEnabled(true);
                playerMove.Unfreeze(keepAnimatorState: false);
                if (verboseLog)
                    Debug.Log("[SolFinalGameTrigger] 플레이어 컨트롤 해제");
            }
        }

        // 6. 트리거 자체 비활성화 (1회만 실행)
        gameObject.SetActive(false);
    }
}