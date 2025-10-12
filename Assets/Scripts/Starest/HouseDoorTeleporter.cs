using System;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class HouseDoorTeleporter : MonoBehaviour
{
    [Header("플레이어 참조(비우면 본 컴포넌트의 Transform)")]
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
    [SerializeField] private Vector2[] doorOffsets = Array.Empty<Vector2>();        // House→Door
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

    // ─────────────────────────────────────────────
    // ★ Starest 한정 불린 상태 (총 1 + 2 * ownerNames.Length)
    // ─────────────────────────────────────────────
    [Header("Starest 전용 상태 플래그")]
    [SerializeField] private string starestSceneName = "Starest";
    [SerializeField] private bool starestOnly = true;

    [Tooltip("마을에 있는가? (Starest에서만 의미)")]
    public bool IsVillage;

    [Serializable]
    public class OwnerFlags
    {
        public string ownerName;
        [Tooltip("현재 이 집 내부인가?")]
        public bool InHouse;
        [Tooltip("이 집에서 막 마을로 나온 직후 상태인가? (문 통해 나왔을 때 true, 다음 입장/이동 시 초기화)")]
        public bool ExitedToVillage;
    }

    [Tooltip("ownerNames와 같은 길이로 자동 정렬됩니다.")]
    public OwnerFlags[] ownerFlags = Array.Empty<OwnerFlags>();

    // ─────────────────────────────────────────────
    // 상태/런타임
    // ─────────────────────────────────────────────
    public string CurrentOwnerName { get; private set; } = "";
    public int CurrentOwnerIndex { get; private set; } = -1;

    private Rigidbody2D playerRb2D;

    // 현재 접촉 중인 집 인덱스(F키 처리용)
    private int currentHouseIndex = -1;

    private void Reset() { playerTransform = transform; }

    private void Awake()
    {
        if (playerTransform == null) playerTransform = transform;
        playerRb2D = playerTransform.GetComponent<Rigidbody2D>();

        EnsureOwnerFlagsSized();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Starest 씬 진입 시 초기 상태: 마을=true, 나머지=false
        if (!starestOnly || string.Equals(scene.name, starestSceneName, StringComparison.Ordinal))
        {
            SetVillageOnlyState();
            if (verboseLog) Debug.Log($"[Teleporter] SceneLoaded => VillageOnly (scene='{scene.name}')");
        }
        else
        {
            // 다른 씬에서는 플래그 안 씀(원한다면 초기화)
            ClearAllFlags();
        }
    }

    private void Update()
    {
        // 집과 접촉 중일 때 F키로 입장
        if (currentHouseIndex != -1 && IsIndexValid(currentHouseIndex))
        {
            if (houseActivationKey == KeyCode.None || Input.GetKeyDown(houseActivationKey))
            {
                Sequence_HouseToDoor(currentHouseIndex);
                currentHouseIndex = -1; // 재실행 방지
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        int hIdx = FindIndexByParents(col.collider.transform, houses);
        if (hIdx >= 0)
        {
            currentHouseIndex = hIdx;
            if (verboseLog) Debug.Log($"[Teleporter] 집과 충돌 시작 idx={currentHouseIndex}");
        }
    }

    private void OnCollisionExit2D(Collision2D col)
    {
        int hIdx = FindIndexByParents(col.collider.transform, houses);
        if (hIdx >= 0 && hIdx == currentHouseIndex)
        {
            currentHouseIndex = -1;
            if (verboseLog) Debug.Log($"[Teleporter] 집과 충돌 끝 idx={hIdx}");
        }
    }

    // 문에서 집으로 돌아가는 로직 (S 키 유지)
    private void OnCollisionStay2D(Collision2D col)
    {
        int dIdx = FindIndexByParents(col.collider.transform, GetDoorGameObjects());
        if (dIdx >= 0 && IsIndexValid(dIdx))
        {
            if (Input.GetKey(doorReturnKey))
            {
                Teleport_DoorToHouse(dIdx);
            }
        }
    }

    // ───────────────── 시퀀스: House → Door (입장) ─────────────────
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

        // ★ Starest 불린 상태 갱신: 집 "입장"
        if (ShouldUseStarestFlags())
        {
            SetState_OnEnterHouse(index);
        }
    }

    // ───────────────── 텔레포트: House → Door ─────────────────
    private void TeleportToDoorIndex(int index)
    {
        Transform door = doors[index];
        if (!door) { if (verboseLog) Debug.LogWarning($"[Teleporter] Doors[{index}] 없음"); return; }

        Vector2 offset = (index >= 0 && index < doorOffsets.Length) ? doorOffsets[index] : Vector2.zero;
        Vector3 p = door.position;
        Vector3 target = new Vector3(p.x + offset.x, p.y + offset.y, playerTransform.position.z);
        SnapPlayer(target);

        if (verboseLog) Debug.Log($"[Teleporter] House→Door 완료 idx={index}, owner='{CurrentOwnerName}'");
    }

    // ───────────────── 텔레포트: Door → House (마을로 나감) ─────────────────
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

        if (verboseLog) Debug.Log($"[Teleporter] Door→House(=마을) 완료 idx={index}");

        // ★ Starest 불린 상태 갱신: 집 "퇴장 → 마을"
        if (ShouldUseStarestFlags())
        {
            SetState_OnExitToVillage(index);
        }
    }

    // ───────────────── 내부 유틸 ─────────────────
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

    private void SnapPlayer(Vector3 target)
    {
        if (useRigidbodySnap && playerRb2D)
        {
#if UNITY_2022_2_OR_NEWER
            playerRb2D.linearVelocity = Vector2.zero;
#else
            playerRb2D.velocity = Vector2.zero;
#endif
            playerRb2D.angularVelocity = 0f;
            playerRb2D.position = new Vector2(target.x, target.y);
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

    // ───────────────── Starest 상태 로직 ─────────────────
    private bool ShouldUseStarestFlags()
    {
        if (!starestOnly) return true;
        var scn = SceneManager.GetActiveScene().name;
        return string.Equals(scn, starestSceneName, StringComparison.Ordinal);
    }

    private void EnsureOwnerFlagsSized()
    {
        if (ownerFlags == null) ownerFlags = Array.Empty<OwnerFlags>();

        if (ownerFlags.Length != ownerNames.Length)
        {
            var newArr = new OwnerFlags[ownerNames.Length];
            for (int i = 0; i < newArr.Length; i++)
            {
                newArr[i] = (i < ownerFlags.Length && ownerFlags[i] != null) ? ownerFlags[i] : new OwnerFlags();
                newArr[i].ownerName = (i < ownerNames.Length) ? ownerNames[i] : "";
            }
            ownerFlags = newArr;
        }
        else
        {
            for (int i = 0; i < ownerFlags.Length; i++)
                if (ownerFlags[i] != null) ownerFlags[i].ownerName = (i < ownerNames.Length) ? ownerNames[i] : ownerFlags[i].ownerName;
        }
    }

    [ContextMenu("Starest/ClearAllFlags")]
    private void ClearAllFlags()
    {
        EnsureOwnerFlagsSized();
        IsVillage = false;
        for (int i = 0; i < ownerFlags.Length; i++)
        {
            ownerFlags[i].InHouse = false;
            ownerFlags[i].ExitedToVillage = false;
        }
    }

    [ContextMenu("Starest/SetVillageOnlyState")]
    private void SetVillageOnlyState()
    {
        EnsureOwnerFlagsSized();
        IsVillage = true;
        for (int i = 0; i < ownerFlags.Length; i++)
        {
            ownerFlags[i].InHouse = false;
            ownerFlags[i].ExitedToVillage = false;
        }
    }

    // 집 "입장" 시 상태
    private void SetState_OnEnterHouse(int index)
    {
        EnsureOwnerFlagsSized();

        IsVillage = false;                         // 마을 false
        for (int i = 0; i < ownerFlags.Length; i++)
        {
            ownerFlags[i].InHouse = (i == index);  // 해당 집만 true
            ownerFlags[i].ExitedToVillage = false; // 입장 순간엔 전부 false
        }

        if (verboseLog)
        {
            Debug.Log($"[Teleporter] EnterHouse → Village=false, InHouse[{index}]=true, ExitedToVillage[*]=false");
        }
    }

    // 집에서 "마을로 퇴장" 시 상태
    private void SetState_OnExitToVillage(int index)
    {
        EnsureOwnerFlagsSized();

        IsVillage = true;                           // 마을 true
        for (int i = 0; i < ownerFlags.Length; i++)
        {
            ownerFlags[i].InHouse = false;          // 이제 집 내부 아님
            ownerFlags[i].ExitedToVillage = (i == index); // 방금 나온 집만 true
        }

        if (verboseLog)
        {
            Debug.Log($"[Teleporter] ExitToVillage ← from index {index} → Village=true, ExitedToVillage[{index}]=true");
        }
    }

    // ───────────────── 외부 조회 API ─────────────────
    // MessageSystem 등에서 Bool 조건 평가에 사용
    // key:
    //   "IsVillage"
    //   "<Owner>_InHouse"            (예: "Sol_InHouse")
    //   "<Owner>_ExitedToVillage"    (예: "Sol_ExitedToVillage")
    public bool TryGetFlag(string key, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(key)) return false;

        string k = key.Trim();

        // IsVillage
        if (string.Equals(k, "IsVillage", StringComparison.OrdinalIgnoreCase))
        {
            value = IsVillage;
            return true;
        }

        // Owner-based
        int under = k.IndexOf('_');
        if (under > 0 && under < k.Length - 1)
        {
            string owner = k.Substring(0, under);
            string suffix = k.Substring(under + 1);

            int idx = IndexOfOwner(owner);
            if (idx < 0) return false;

            var of = ownerFlags[idx];
            if (of == null) return false;

            if (string.Equals(suffix, "InHouse", StringComparison.OrdinalIgnoreCase))
            {
                value = of.InHouse;
                return true;
            }
            if (string.Equals(suffix, "ExitedToVillage", StringComparison.OrdinalIgnoreCase))
            {
                value = of.ExitedToVillage;
                return true;
            }
        }
        return false;
    }

    // 편의용(못 찾으면 false 반환)
    public bool GetFlag(string key) => TryGetFlag(key, out var v) && v;

    private int IndexOfOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || ownerNames == null) return -1;
        for (int i = 0; i < ownerNames.Length; i++)
            if (string.Equals(ownerNames[i], owner, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
