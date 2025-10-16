using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────
// HouseDoorTeleporter (단일 파일 완결형, 레이캐스트 차단 제거 버전)
//  - 인덱스 매칭 하우스 <-> 도어 이동
//  - 태그=Door + 이름 매칭으로 Road/Starest/StarestCenter 전환
//  - 페이드 인 → 좌표/루트/카메라 토글 → 페이드 아웃
//  - Bus는 절대 비활성화하지 않음(요청 사항)
//  - 내장 페이더 사용(GraphicRaycaster 미사용, Image.raycastTarget=false)
// ─────────────────────────────────────────────────────────────
[DisallowMultipleComponent]
public class HouseDoorTeleporter : MonoBehaviour
{
    [Header("플레이어 참조(비우면 본 컴포넌트의 Transform)")]
    [SerializeField] private Transform playerTransform;
    private Rigidbody2D playerRb2D;

    // ==========================
    // 인덱스 매칭 기반(기존 기능)
    // ==========================
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

    [Header("카메라/연출(기존)")]
    [SerializeField] private GameObject cameraToDisable;
    [SerializeField] private bool deactivateOtherCharacterHousesFirst = false;

    [Header("입력/물리")]
    [Tooltip("House→Door 이동용 키. None이면 충돌 즉시 이동")]
    [SerializeField] private KeyCode houseActivationKey = KeyCode.F;

    [Tooltip("Door→House 복귀용 키(누른 '상태'여야 함). 기본 S")]
    [SerializeField] private KeyCode doorReturnKey = KeyCode.S;

    [SerializeField] private bool useRigidbodySnap = true;
    [SerializeField] private bool verboseLog = true;

    // ===========================================
    // Starest ↔ Road 전환(태그=Door, 이름 매칭)
    // ===========================================
    [Header("Starest ↔ Road 전환(태그=Door, 이름 매칭)")]
    [Tooltip("Road 루트 오브젝트")]
    [SerializeField] private GameObject roadRoot;
    [Tooltip("Starest 구간의 도로 루트 오브젝트 (StarestRoad)")]
    [SerializeField] private GameObject starestRoadRoot;
    [Tooltip("버스 오브젝트(필요 시 켜기만 함, 끄지 않음)")]
    [SerializeField] private GameObject busObject;

    [Tooltip("Road 카메라(선택)")]
    [SerializeField] private GameObject roadCamera;
    [Tooltip("Starest 카메라(선택)")]
    [SerializeField] private GameObject starestCamera;

    [Tooltip("Door 오브젝트 이름: Road → Starest 로 넘어가는 문 이름")]
    [SerializeField] private string doorName_RoadToStarest = "Road to Starest";

    [Tooltip("Door 오브젝트 이름: Starest → Road 로 돌아가는 문 이름")]
    [SerializeField] private string doorName_BackToRoad = "Back to Road";

    [Tooltip("Road → Starest 전환 시 플레이어 목표 좌표")]
    [SerializeField] private Vector2 starestSpawnPos = new Vector2(0f, -13f);

    [Tooltip("Starest → Road 전환 시 플레이어 목표 좌표")]
    [SerializeField] private Vector2 roadReturnPos = new Vector2(0f, -15.5f);

    // ==================================================
    // StarestCenter ↔ StarestRoad 전환
    // ==================================================
    [Header("Starest Center 전환(태그=Door, 이름 매칭)")]
    [Tooltip("Starest 중앙 루트 오브젝트")]
    [SerializeField] private GameObject starestCenterRoot;

    [Tooltip("플레이어 전용 카메라 루트 (PlayerCamera)")]
    [SerializeField] private GameObject playerCameraRoot;

    [Tooltip("Door 오브젝트 이름: StarestRoad에서 중앙으로 들어오는 문")]
    [SerializeField] private string doorName_ArriveToStarest = "Arrive to Starest";

    [Tooltip("Door 오브젝트 이름: 중앙에서 StarestRoad로 되돌아가는 문")]
    [SerializeField] private string doorName_BackToStarest = "Back to Starest";

    [Tooltip("Arrive to Starest 시 플레이어 좌표")]
    [SerializeField] private Vector2 starestCenterPos = new Vector2(0f, 0f);

    [Tooltip("Back to Starest 시 플레이어 좌표")]
    [SerializeField] private Vector2 starestRoadPosFromCenter = new Vector2(0f, -5.5f);

    // ==========================
    // 전환 페이드 공통 (내장 페이더)
    // ==========================
    [Header("전환 페이드(공통)")]
    [Tooltip("전환 시 페이드 사용")]
    [SerializeField] private bool useDoorFade = true;
    [Tooltip("페이드 인(0→1) 시간")]
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.25f;
    [Tooltip("페이드 아웃(1→0) 시간(즉시 원하면 0 또는 매우 작게)")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.001f;
    [Tooltip("페이드 색상")]
    [SerializeField] private Color fadeColor = Color.black;
    [Tooltip("페이드용 Image(비워두면 런타임에 자동 생성)")]
    [SerializeField] private Image fadeOverlay;

    // 전환 중복 방지
    private bool _isSwitchingByDoor = false;

    // ==========================
    // Starest 상태 플래그
    // ==========================
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
        [Tooltip("이 집에서 막 마을로 나온 직후 상태인가?")]
        public bool ExitedToVillage;
    }

    [Tooltip("ownerNames와 같은 길이로 자동 정렬됩니다.")]
    [SerializeField] private List<OwnerFlags> ownerFlagsList = new List<OwnerFlags>();
    private Dictionary<string, OwnerFlags> ownerFlagsMap;

    public string CurrentOwnerName { get; private set; } = "";
    public int CurrentOwnerIndex { get; private set; } = -1;

    private int currentHouseIndex = -1;

    // ───────────────────────── 초기화 ─────────────────────────
    private void Reset()
    {
        playerTransform = transform;

        doorName_RoadToStarest = "Road to Starest";
        doorName_BackToRoad = "Back to Road";
        starestSpawnPos = new Vector2(0f, -13f);
        roadReturnPos = new Vector2(0f, -15.5f);

        doorName_ArriveToStarest = "Arrive to Starest";
        doorName_BackToStarest = "Back to Starest";
        starestCenterPos = new Vector2(0f, 0f);
        starestRoadPosFromCenter = new Vector2(0f, -5.5f);

        useDoorFade = true;
        fadeInDuration = 0.25f;
        fadeOutDuration = 0.001f;
        fadeColor = Color.black;
    }

    private void Awake()
    {
        if (playerTransform == null) playerTransform = transform;
        playerRb2D = playerTransform.GetComponent<Rigidbody2D>();

        EnsureOwnerFlagsSized();
        RebuildOwnerMap();

        EnsureFadeOverlay(); // 내장 페이더 준비 (클릭 차단 제거)

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureOwnerFlagsSized();
        RebuildOwnerMap();

        if (!starestOnly || string.Equals(scene.name, starestSceneName, StringComparison.Ordinal))
        {
            SetVillageOnlyState();
            if (verboseLog) Debug.Log($"[Teleporter] SceneLoaded => VillageOnly (scene='{scene.name}')");
        }
        else
        {
            ClearAllFlags();
        }
    }

    private void Update()
    {
        // 집과 겹칠 때 F로 입장(기존)
        if (currentHouseIndex != -1 && IsIndexValid(currentHouseIndex))
        {
            if (houseActivationKey == KeyCode.None || Input.GetKeyDown(houseActivationKey))
            {
                Sequence_HouseToDoor(currentHouseIndex);
                currentHouseIndex = -1;
            }
        }
    }

    // ───────── 기존: 집 충돌 로직(비트리거) ─────────
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

    // 문에서 집으로 돌아가는 로직 (S 유지)
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

    // ───────── 태그=Door 트리거 처리 ─────────
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other || !other.CompareTag("Door")) return;
        if (_isSwitchingByDoor) return;

        string doorName = other.gameObject.name;

        // Road → Starest
        if (NameEquals(doorName, doorName_RoadToStarest))
        {
            if (verboseLog) Debug.Log("[DoorSwitch] Road to Starest 감지");
            StartCoroutine(Co_SwitchSet(
                targetPos: starestSpawnPos,
                setRoadRoot: false, setStarestRoadRoot: true,
                setStarestCenterRoot: null,
                setRoadCamera: null, setStarestCamera: null,
                setPlayerCamera: null,
                ensureBusOn: true       // 버스는 켜기만 함 (끄지 않음)
            ));
            return;
        }

        // Starest → Road
        if (NameEquals(doorName, doorName_BackToRoad))
        {
            if (verboseLog) Debug.Log("[DoorSwitch] Back to Road 감지");
            StartCoroutine(Co_SwitchSet(
                targetPos: roadReturnPos,
                setRoadRoot: true, setStarestRoadRoot: false,
                setStarestCenterRoot: null,
                setRoadCamera: null, setStarestCamera: null,
                setPlayerCamera: null,
                ensureBusOn: false      // 아무 작업 안 함(절대 끄지 않음)
            ));
            return;
        }

        // StarestRoad → StarestCenter
        if (NameEquals(doorName, doorName_ArriveToStarest))
        {
            if (verboseLog) Debug.Log("[DoorSwitch] Arrive to Starest 감지");
            StartCoroutine(Co_SwitchSet(
                targetPos: starestCenterPos,
                setRoadRoot: null, setStarestRoadRoot: false,
                setStarestCenterRoot: true,
                setRoadCamera: null, setStarestCamera: null,
                setPlayerCamera: true,
                ensureBusOn: false
            ));
            return;
        }

        // StarestCenter → StarestRoad
        if (NameEquals(doorName, doorName_BackToStarest))
        {
            if (verboseLog) Debug.Log("[DoorSwitch] Back to Starest 감지");
            StartCoroutine(Co_SwitchSet(
                targetPos: starestRoadPosFromCenter,
                setRoadRoot: null, setStarestRoadRoot: true,
                setStarestCenterRoot: false,
                setRoadCamera: null, setStarestCamera: null,
                setPlayerCamera: false,
                ensureBusOn: false
            ));
            return;
        }
    }

    private bool NameEquals(string a, string b)
    {
        return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // 공통 전환 코루틴:
    // null → 그 항목은 상태 변경 안 함, true/false → 강제 설정
    private System.Collections.IEnumerator Co_SwitchSet(
        Vector2 targetPos,
        bool? setRoadRoot,
        bool? setStarestRoadRoot,
        bool? setStarestCenterRoot,
        bool? setRoadCamera,
        bool? setStarestCamera,
        bool? setPlayerCamera,
        bool ensureBusOn)
    {
        _isSwitchingByDoor = true;

        // 0) 페이드 인(검은 화면)
        if (useDoorFade) yield return FadeTo(1f, Mathf.Max(0.01f, fadeInDuration));

        // 1) 목적지 관련 활성화(켜야 하는 것들 먼저 켬)
        if (setRoadRoot.HasValue && setRoadRoot.Value && roadRoot && !roadRoot.activeSelf) roadRoot.SetActive(true);
        if (setStarestRoadRoot.HasValue && setStarestRoadRoot.Value && starestRoadRoot && !starestRoadRoot.activeSelf) starestRoadRoot.SetActive(true);
        if (setStarestCenterRoot.HasValue && setStarestCenterRoot.Value && starestCenterRoot && !starestCenterRoot.activeSelf) starestCenterRoot.SetActive(true);

        if (setRoadCamera.HasValue && setRoadCamera.Value && roadCamera && !roadCamera.activeSelf) roadCamera.SetActive(true);
        if (setStarestCamera.HasValue && setStarestCamera.Value && starestCamera && !starestCamera.activeSelf) starestCamera.SetActive(true);
        if (setPlayerCamera.HasValue && setPlayerCamera.Value && playerCameraRoot && !playerCameraRoot.activeSelf) playerCameraRoot.SetActive(true);

        // ★ Bus는 절대 끄지 않음: on 지시일 때만 켠다
        if (ensureBusOn && busObject) { if (!busObject.activeSelf) busObject.SetActive(true); }

        // 2) 좌표 이동(물리 안전)
        SnapPlayer(new Vector3(targetPos.x, targetPos.y, playerTransform.position.z));

        // 3) 동기화 여유
        yield return new WaitForFixedUpdate();
        yield return null;

        // 4) 출발지 관련 비활성(꺼야 하는 것들 끔)
        if (setRoadRoot.HasValue && !setRoadRoot.Value && roadRoot) roadRoot.SetActive(false);
        if (setStarestRoadRoot.HasValue && !setStarestRoadRoot.Value && starestRoadRoot) starestRoadRoot.SetActive(false);
        if (setStarestCenterRoot.HasValue && !setStarestCenterRoot.Value && starestCenterRoot) starestCenterRoot.SetActive(false);

        if (setRoadCamera.HasValue && !setRoadCamera.Value && roadCamera) roadCamera.SetActive(false);
        if (setStarestCamera.HasValue && !setStarestCamera.Value && starestCamera) starestCamera.SetActive(false);
        if (setPlayerCamera.HasValue && !setPlayerCamera.Value && playerCameraRoot) playerCameraRoot.SetActive(false);

        // 5) 페이드 아웃(바로 복귀)
        if (useDoorFade) yield return FadeTo(0f, Mathf.Max(0f, fadeOutDuration));

        _isSwitchingByDoor = false;
    }

    // ───────── 기존 인덱스 기반 시퀀스 ─────────
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

        if (ShouldUseStarestFlags())
            SetState_OnEnterHouseByName(CurrentOwnerName);
    }

    private void TeleportToDoorIndex(int index)
    {
        Transform door = (index >= 0 && index < doors.Length) ? doors[index] : null;
        if (!door) { if (verboseLog) Debug.LogWarning($"[Teleporter] Doors[{index}] 없음"); return; }

        Vector2 offset = (index >= 0 && index < doorOffsets.Length) ? doorOffsets[index] : Vector2.zero;
        Vector3 p = door.position;
        Vector3 target = new Vector3(p.x + offset.x, p.y + offset.y, playerTransform.position.z);
        SnapPlayer(target);

        if (verboseLog) Debug.Log($"[Teleporter] House→Door 완료 idx={index}, owner='{CurrentOwnerName}'");
    }

    private void Teleport_DoorToHouse(int index)
    {
        GameObject house = (index >= 0 && index < houses.Length) ? houses[index] : null;
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

        if (ShouldUseStarestFlags())
        {
            string owner = (index >= 0 && index < ownerNames.Length) ? ownerNames[index] : "";
            SetState_OnExitToVillageByName(owner);
        }
    }

    // ───────── 유틸 ─────────
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
            playerRb2D.WakeUp();
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

    // ───────── Starest 상태 로직 ─────────
    private bool ShouldUseStarestFlags()
    {
        if (!starestOnly) return true;
        var scn = SceneManager.GetActiveScene().name;
        return string.Equals(scn, starestSceneName, StringComparison.Ordinal);
    }

    private void EnsureOwnerFlagsSized()
    {
        if (ownerFlagsList == null) ownerFlagsList = new List<OwnerFlags>(ownerNames?.Length ?? 0);

        int want = ownerNames?.Length ?? 0;

        while (ownerFlagsList.Count < want)
            ownerFlagsList.Add(new OwnerFlags());

        while (ownerFlagsList.Count > want)
            ownerFlagsList.RemoveAt(ownerFlagsList.Count - 1);

        for (int i = 0; i < want; i++)
        {
            if (ownerFlagsList[i] == null) ownerFlagsList[i] = new OwnerFlags();
            ownerFlagsList[i].ownerName = ownerNames[i] ?? "";
        }
    }

    private void RebuildOwnerMap()
    {
        if (ownerFlagsMap == null)
            ownerFlagsMap = new Dictionary<string, OwnerFlags>(StringComparer.OrdinalIgnoreCase);
        else
            ownerFlagsMap.Clear();

        for (int i = 0; i < ownerFlagsList.Count; i++)
        {
            var of = ownerFlagsList[i];
            if (of == null) continue;
            var key = of.ownerName ?? "";
            if (string.IsNullOrWhiteSpace(key)) continue;
            ownerFlagsMap[key.Trim()] = of;
        }
    }

    [ContextMenu("Starest/ClearAllFlags")]
    private void ClearAllFlags()
    {
        EnsureOwnerFlagsSized();
        RebuildOwnerMap();

        IsVillage = false;
        foreach (var of in ownerFlagsList)
        {
            if (of == null) continue;
            of.InHouse = false;
            of.ExitedToVillage = false;
        }
    }

    [ContextMenu("Starest/SetVillageOnlyState")]
    private void SetVillageOnlyState()
    {
        EnsureOwnerFlagsSized();
        RebuildOwnerMap();

        IsVillage = true;
        foreach (var of in ownerFlagsList)
        {
            if (of == null) continue;
            of.InHouse = false;
            of.ExitedToVillage = false;
        }
    }

    private void SetState_OnEnterHouseByName(string ownerName)
    {
        EnsureOwnerFlagsSized();
        RebuildOwnerMap();

        IsVillage = false;
        foreach (var of in ownerFlagsList)
        {
            if (of == null) continue;
            of.InHouse = string.Equals(of.ownerName, ownerName, StringComparison.OrdinalIgnoreCase);
            of.ExitedToVillage = false;
        }

        if (verboseLog)
            Debug.Log($"[Teleporter] EnterHouse → Village=false, InHouse='{ownerName}', ExitedToVillage[*]=false");
    }

    private void SetState_OnExitToVillageByName(string ownerName)
    {
        EnsureOwnerFlagsSized();
        RebuildOwnerMap();

        IsVillage = true;
        foreach (var of in ownerFlagsList)
        {
            if (of == null) continue;
            of.InHouse = false;
            of.ExitedToVillage = string.Equals(of.ownerName, ownerName, StringComparison.OrdinalIgnoreCase);
        }

        if (verboseLog)
            Debug.Log($"[Teleporter] ExitToVillage ← from '{ownerName}' → Village=true, ExitedToVillage['{ownerName}']=true");
    }

    public bool TryGetFlag(string key, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(key)) return false;

        EnsureOwnerFlagsSized();
        RebuildOwnerMap();

        string k = key.Trim();

        if (string.Equals(k, "IsVillage", StringComparison.OrdinalIgnoreCase))
        {
            value = IsVillage;
            return true;
        }

        int under = k.IndexOf('_');
        if (under <= 0 || under >= k.Length - 1) return false;

        string owner = k.Substring(0, under);
        string suffix = k.Substring(under + 1);

        if (!ownerFlagsMap.TryGetValue(owner, out var of) || of == null)
        {
            if (verboseLog) Debug.LogWarning($"[Teleporter] TryGetFlag: unknown owner '{owner}' (key='{key}')");
            return false;
        }

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

        if (verboseLog) Debug.LogWarning($"[Teleporter] TryGetFlag: unknown suffix '{suffix}' (key='{key}')");
        return false;
    }

    public bool GetFlag(string key) => TryGetFlag(key, out var v) && v;

    private int IndexOfOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || ownerNames == null) return -1;
        for (int i = 0; i < ownerNames.Length; i++)
            if (string.Equals(ownerNames[i], owner, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // ──────────────────────── 내장 페이더 ────────────────────────
    private void EnsureFadeOverlay()
    {
        if (!useDoorFade) return;

        // 기존 Overlay를 지정받았으면 알파/설정만 정리
        if (fadeOverlay && fadeOverlay.gameObject)
        {
            var c = fadeOverlay.color; c.a = 0f; fadeOverlay.color = c;
            fadeOverlay.raycastTarget = false; // ★ 클릭 막지 않도록
            if (!fadeOverlay.gameObject.activeSelf) fadeOverlay.gameObject.SetActive(true);
            EnsureOverlayCanvasOnTop(fadeOverlay);
            return;
        }

        // 자동 생성 (GraphicRaycaster 없이 생성)
        var canvasGo = new GameObject("[DoorFadeCanvas]", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue; // 최상단

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var imgGo = new GameObject("FadeOverlay", typeof(Image));
        imgGo.transform.SetParent(canvasGo.transform, false);

        var img = imgGo.GetComponent<Image>();
        img.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 0f);
        img.raycastTarget = false; // ★ 클릭 차단 금지

        var rt = img.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        fadeOverlay = img;
    }

    private void EnsureOverlayCanvasOnTop(Image img)
    {
        var canvas = img.GetComponentInParent<Canvas>();
        if (canvas)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
        }
    }

    private System.Collections.IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (!useDoorFade) yield break;

        if (!fadeOverlay) EnsureFadeOverlay();
        if (!fadeOverlay) yield break;

        Color start = fadeOverlay.color;
        Color target = new Color(fadeColor.r, fadeColor.g, fadeColor.b, Mathf.Clamp01(targetAlpha));

        if (duration <= 0f)
        {
            fadeOverlay.color = target;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            fadeOverlay.color = Color.Lerp(start, target, u);
            yield return null;
        }
        fadeOverlay.color = target;
    }
}
