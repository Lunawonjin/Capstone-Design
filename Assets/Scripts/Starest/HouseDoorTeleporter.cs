using System;
using UnityEngine;

[DisallowMultipleComponent]
public class HouseDoorTeleporter : MonoBehaviour
{
    [Header("플레이어 참조(비우면 본 컴-포넌트의 Transform)")]
    [SerializeField] private Transform playerTransform;

    [Header("인덱스 매칭 대상들")]
    [Tooltip("Collider2D(Trigger 꺼짐) 보유, 인덱스 매칭")]
    [SerializeField] private GameObject[] houses = Array.Empty<GameObject>();
    [SerializeField] private Transform[] doors = Array.Empty<Transform>();
    [SerializeField] private GameObject[] characterHouses = Array.Empty<GameObject>();
    [SerializeField] private GameObject[] mapToDisable = Array.Empty<GameObject>();

    [Header("집주인 이름(인덱스별)")]
    [Tooltip("houses/doors/characterHouses와 같은 인덱스로 이름을 넣으세요")]
    [SerializeField] private string[] ownerNames = Array.Empty<string>();

    [Header("배치 오프셋(인덱스별)")]
    [SerializeField] private Vector2[] doorOffsets = Array.Empty<Vector2>();       // House→Door
    [SerializeField] private Vector2[] houseReturnOffsets = Array.Empty<Vector2>(); // Door→House

    [Header("카메라/연출")]
    [SerializeField] private GameObject cameraToDisable;
    [SerializeField] private bool deactivateOtherCharacterHousesFirst = false;

    [Header("입력/물리")]
    [Tooltip("House→Door 이동용 키. None이면 충돌 즉시 이동")]
    [SerializeField] private KeyCode houseActivationKey = KeyCode.F;

    [Tooltip("Door→House 복귀용 키(누른 '상태'여야 함). 기본 S")]
    [SerializeField] private KeyCode doorReturnKey = KeyCode.S;

    [SerializeField] private bool useRigidbodySnap = true;
    [SerializeField] private bool verboseLog = true;

    // 내부 상태(외부 저장 없음)
    public string CurrentOwnerName { get; private set; } = "";
    public int CurrentOwnerIndex { get; private set; } = -1;

    private Rigidbody2D playerRb2D;

    // ===== [추가됨] 현재 접촉 중인 집의 인덱스를 저장하기 위한 변수 =====
    private int currentHouseIndex = -1;

    private void Reset() { playerTransform = transform; }

    private void Awake()
    {
        if (playerTransform == null) playerTransform = transform;
        playerRb2D = playerTransform.GetComponent<Rigidbody2D>();
    }

    // ===== [추가됨] Update에서 키 입력을 감지 =====
    // Update는 매 프레임 호출되므로 키 입력을 놓치지 않습니다.
    private void Update()
    {
        // currentHouseIndex가 유효할 때 (즉, 집과 충돌 중일 때) F키 입력을 확인합니다.
        if (currentHouseIndex != -1 && IsIndexValid(currentHouseIndex))
        {
            if (houseActivationKey == KeyCode.None || Input.GetKeyDown(houseActivationKey))
            {
                Sequence_HouseToDoor(currentHouseIndex);
                // 이동 후에는 다시 충돌하기 전까지 재실행되지 않도록 인덱스를 초기화합니다.
                currentHouseIndex = -1;
            }
        }
    }

    // ===== [추가됨] 집에 처음 충돌했을 때 인덱스를 기록 =====
    private void OnCollisionEnter2D(Collision2D col)
    {
        int hIdx = FindIndexByParents(col.collider.transform, houses);
        if (hIdx >= 0)
        {
            currentHouseIndex = hIdx;
            if (verboseLog) Debug.Log($"[Teleporter] 집과 충돌 시작. 인덱스: {currentHouseIndex}");
        }
    }

    // ===== [추가됨] 집에서 충돌이 끝났을 때 인덱스를 초기화 =====
    private void OnCollisionExit2D(Collision2D col)
    {
        int hIdx = FindIndexByParents(col.collider.transform, houses);
        // 현재 충돌이 끝난 집이 기억하고 있던 집이 맞는지 확인 후 초기화합니다.
        if (hIdx >= 0 && hIdx == currentHouseIndex)
        {
            currentHouseIndex = -1;
            if (verboseLog) Debug.Log($"[Teleporter] 집과 충돌 끝남. 인덱스 초기화.");
        }
    }

    // ===== [수정됨] OnCollisionStay2D는 이제 문(Door)에서 집으로 돌아오는 로직만 처리 =====
    private void OnCollisionStay2D(Collision2D col)
    {
        // Door → House 로직 (S키 누르고 있는 '동안' 이동)
        int dIdx = FindIndexByParents(col.collider.transform, GetDoorGameObjects());
        if (dIdx >= 0 && IsIndexValid(dIdx))
        {
            if (Input.GetKey(doorReturnKey))
            {
                Teleport_DoorToHouse(dIdx);
            }
        }
    }

    // ===== 시퀀스: House → Door =====
    private void Sequence_HouseToDoor(int index)
    {
        if (deactivateOtherCharacterHousesFirst)
        {
            for (int i = 0; i < characterHouses.Length; i++)
                if (characterHouses[i] && characterHouses[i].activeSelf) characterHouses[i].SetActive(false);
        }
        if (index >= 0 && index < characterHouses.Length && characterHouses[index] && !characterHouses[index].activeSelf)
            characterHouses[index].SetActive(true);

        if (cameraToDisable && cameraToDisable.activeSelf) cameraToDisable.SetActive(false);

        var map = GetMapToDisableOrNull(index);
        if (map && map.activeSelf) map.SetActive(false);

        SetCurrentOwner(index);
        TeleportToDoorIndex(index);
    }

    // ===== 저장: 현재 집주인(텔레포터 내부만) =====
    private void SetCurrentOwner(int index)
    {
        CurrentOwnerIndex = index;
        if (ownerNames != null && index >= 0 && index < ownerNames.Length && !string.IsNullOrEmpty(ownerNames[index]))
            CurrentOwnerName = ownerNames[index];
        else
            CurrentOwnerName = "";

        if (verboseLog)
            Debug.Log($"[Teleporter] Owner set: index={CurrentOwnerIndex}, name='{CurrentOwnerName}'");
    }

    // ===== 텔레포트: House → Door =====
    private void TeleportToDoorIndex(int index)
    {
        Transform door = doors[index];
        if (!door) { if (verboseLog) Debug.LogWarning($"[Teleporter] Doors[{index}] 없음"); return; }

        Vector2 offset = (index >= 0 && index < doorOffsets.Length) ? doorOffsets[index] : Vector2.zero;
        Vector3 p = door.position;
        Vector3 target = new Vector3(p.x + offset.x, p.y + offset.y, playerTransform.position.z);
        SnapPlayer(target);

        if (verboseLog) Debug.Log($"[Teleporter] House→Door 완료: idx={index}, final={target}, owner='{CurrentOwnerName}'");
    }

    // ===== 텔레포트: Door → House =====
    private void Teleport_DoorToHouse(int index)
    {
        GameObject house = houses[index];
        if (!house) { if (verboseLog) Debug.LogWarning($"[Teleporter] Houses[{index}] 없음"); return; }

        Vector2 offset = (index >= 0 && index < houseReturnOffsets.Length) ? houseReturnOffsets[index] : Vector2.zero;
        Vector3 p = house.transform.position;
        Vector3 target = new Vector3(p.x + offset.x, p.y + offset.y, playerTransform.position.z);
        SnapPlayer(target);

        if (index >= 0 && index < characterHouses.Length && characterHouses[index] && characterHouses[index].activeSelf)
            characterHouses[index].SetActive(false);

        var map = GetMapToDisableOrNull(index);
        if (map && !map.activeSelf) map.SetActive(true);

        if (cameraToDisable && !cameraToDisable.activeSelf) cameraToDisable.SetActive(true);

        if (verboseLog) Debug.Log($"[Teleporter] Door→House 완료: idx={index}, final={target}");
    }

    // ===== 유틸 =====
    private void SnapPlayer(Vector3 target)
    {
        if (useRigidbodySnap && playerRb2D)
        {
            playerRb2D.position = new Vector2(target.x, target.y);
            playerRb2D.linearVelocity = Vector2.zero; // linearVelocity는 이제 사용되지 않음
            playerRb2D.angularVelocity = 0f;
        }
        playerTransform.position = target;
    }

    private bool IsIndexValid(int index)
    {
        int max = Math.Min(houses.Length, doors.Length);
        return index >= 0 && index < max;
    }

    private GameObject GetMapToDisableOrNull(int index)
    {
        if (index >= 0 && index < mapToDisable.Length) return mapToDisable[index];
        return null;
    }

    private int FindIndexByParents(Transform hit, GameObject[] list)
    {
        if (!hit || list == null) return -1;
        Transform cur = hit;
        while (cur)
        {
            for (int i = 0; i < list.Length; i++)
                if (list[i] == cur.gameObject) return i;
            cur = cur.parent;
        }
        return -1;
    }

    private GameObject[] GetDoorGameObjects()
    {
        var arr = new GameObject[doors.Length];
        for (int i = 0; i < doors.Length; i++) arr[i] = doors[i] ? doors[i].gameObject : null;
        return arr;
    }
}