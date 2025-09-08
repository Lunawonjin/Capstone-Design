using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 통합 스포너/풀 매니저
/// - 통나무(Log): 스포너 0,1만. 같은 자리 2연속이면 다음은 강제 반대.
/// - 진흙(Mud): 스포너 0,1,2. 1~2개, 아주 낮은 확률로 3개 동시 스폰.
/// - 바위(Rock): 게임 시작 8초 뒤 첫 낙하, 이후 4초마다.
///     · 경고 동안 빨간 오브젝트가 "플레이어 라인"을 따라 2회 깜빡.
///     · 낙하 시작 순간 라인을 고정하고, 그 라인으로 고속 낙하.
///     · 경고 동안: 통나무는 "타깃 라인에서만" 허용, 나머지 두 라인 중 1개는 전면 차단.
/// - 겹침 방지: 스프라이트 실제 높이 기반 동적 간격 + 레인별 시간 게이트.
/// - 주기/값 변경에도 겹치지 않도록 안전장치 다중 적용.
/// </summary>
public class ObstacleSpawnerManager : MonoBehaviour
{
    [Header("공유 스포너 (길이 3: 0=1번, 1=2번, 2=3번)")]
    public Transform[] spawners;

    [Header("플레이어")]
    public Transform player;                   // ★ 플레이어 Transform (필수)

    [Header("프리팹 및 부모")]
    public GameObject logPrefab;               // 통나무
    public GameObject mudPrefab;               // 진흙
    public GameObject rockPrefab;              // 바위
    public GameObject warningPrefab;           // 경고 오브젝트(빨간 스프라이트)
    public Transform obstacleParent;           // 생성물 부모(선택)

    [Header("풀 설정")]
    public int logPoolSize = 5;
    public int mudPoolSize = 7;               // 동시 2~3개 대비
    public int rockPoolSize = 4;
    public int warnPoolSize = 3;

    [Header("이동/겹침 기본 설정")]
    public float scrollSpeed = 2f;             // 아래로 흐르는 속도(월드/초)
    [Tooltip("같은 레인(X 근접)에서 이 값보다 가까운 Y면 스폰 금지(월드 유닛).")]
    public float minVerticalGap = 1.6f;        // 수동 기준 최소 세로 간격
    [Tooltip("같은 스포너에서 연속 스폰을 막는 잠금 시간(초).")]
    public float spawnerLockDuration = 0.25f;
    [Tooltip("같은 레인(X)으로 판정할 최대 허용 가로 오차 (월드 유닛).")]
    public float spawnClearDistanceX = 0.6f;   // 레인 간격의 절반 이하 권장

    [Header("스폰 주기 (Log/Mud)")]
    public float logSpawnInterval = 1f;
    [Tooltip("흙탕물 주기를 통나무 주기의 비율로 사용할지")]
    public bool mudIntervalIsRelative = true;
    [Range(0.1f, 5f)]
    public float mudIntervalFactor = 0.5f;     // mud = log * factor
    public float mudSpawnIntervalOverride = 0.5f;

    [Header("진흙 동시 스폰 확률")]
    [Range(0f, 1f)] public float mudChance2 = 0.35f; // 2개
    [Range(0f, 1f)] public float mudChance3 = 0.02f; // 3개

    [Header("통나무 이후 해당 스포너 진흙 금지")]
    [Tooltip("통나무가 이 '거리'만큼 내려갈 때까지 동일 스포너 진흙 금지(월드 유닛).")]
    public float mudBanDistance = 3.0f;
    public float mudBanPadding = 0.05f;

    [Header("페이즈(겹침 완화)")]
    public bool useMudPhaseOffset = true;
    [Range(0f, 1f)]
    public float mudPhaseOffsetRatio = 0.5f;

    [Header("자동 간격 보정")]
    [Tooltip("프리팹 SpriteRenderer 높이를 읽어 최소 세로 간격을 자동 상향")]
    public bool autoGapFromPrefabs = true;
    [Tooltip("프리팹 높이에 더해줄 여유(월드 유닛)")]
    public float verticalGapPadding = 0.2f;

    [Header("바위(Rock) 설정")]
    public float rockFirstDelay = 8f;          // 첫 바위 8초 뒤
    public float rockInterval = 4f;          // 이후 4초마다
    public float rockFallSpeed = 10f;         // 빠른 낙하 속도
    public int rockWarnBlinks = 2;           // 경고 깜빡 횟수
    public float rockWarnOn = 0.25f;       // 경고 on 시간
    public float rockWarnOff = 0.20f;       // 경고 off 시간
    public float rockStartYOffset = 0.6f;      // 카메라 상단에서 여유
    public float warnYFromTop = 0.4f;      // 카메라 상단에서 경고 y 오프셋

    [Header("바위 예고 중 제약")]
    public bool restrictLogToRockLane = true;  // 예고 동안 통나무는 타깃 라인에서만
    public bool blockOneOtherLane = true;  // 예고 동안 타깃 외 2개 라인 중 1개 전면 차단

    // --- 내부 상태 ---
    private float mudSpawnInterval;                          // 실제 진흙 주기
    private readonly Queue<ObstacleMover> logPool = new();
    private readonly Queue<ObstacleMover> mudPool = new();
    private readonly Queue<ObstacleMover> rockPool = new();
    private readonly Queue<GameObject> warnPool = new();
    private readonly List<ObstacleMover> active = new();

    private float logTimer = 0f;
    private float mudTimer = 0f;

    // 스포너별 시간 게이트/잠금/금지
    private readonly float[] spawnerNextFreeTime = new float[3]; // 잠금
    private readonly float[] mudBanUntil = new float[3]; // 통나무→진흙 금지
    private readonly float[] laneNextSpawnTime = new float[3]; // 세로 간격 시간 게이트
    private readonly float[] globalBanUntil = new float[3]; // 바위 예고 중 전면 차단

    // 통나무: 같은 자리 2연속이면 다음은 반대
    private int lastLogIndex = -1;
    private int logSameCount = 0;

    // 자동 보정
    private float laneStep;                 // 레인 간격
    private float hLog, hMud, hRock;        // 프리팹 실제 높이(월드 유닛)

    // 바위 스케줄
    private float nextRockTime;
    private bool rockRoutineRunning = false;
    private int rockTargetLane = -1;      // 경고 중 추적하는 현재 타깃 라인(플레이어 라인)
    private int rockLockedLane = -1;     // 낙하 시작 순간 고정된 라인
    private int rockBlockedLane = -1;     // 예고 중 전면 차단 라인
    private Camera cam;

    void Start()
    {
        if (spawners == null || spawners.Length != 3)
        {
            Debug.LogError("spawners는 길이 3이어야 합니다.");
            enabled = false; return;
        }
        if (!player)
        {
            Debug.LogError("player Transform을 할당하세요.");
            enabled = false; return;
        }
        if (!logPrefab || !mudPrefab || !rockPrefab || !warningPrefab)
        {
            Debug.LogError("프리팹(log/mud/rock/warning)을 모두 할당하세요.");
            enabled = false; return;
        }

        cam = Camera.main;

        // 풀 생성
        CreatePool(logPrefab, logPool, logPoolSize, ObstacleMover.ObstacleType.Log);
        CreatePool(mudPrefab, mudPool, mudPoolSize, ObstacleMover.ObstacleType.Mud);
        CreatePool(rockPrefab, rockPool, rockPoolSize, ObstacleMover.ObstacleType.Rock);
        CreateWarnPool();

        // 레인 간격/X허용 클램프
        float x0 = spawners[0].position.x;
        float x1 = spawners[1].position.x;
        float x2 = spawners[2].position.x;
        laneStep = Mathf.Min(Mathf.Abs(x1 - x0), Mathf.Abs(x2 - x1));
        if (laneStep > 0f)
        {
            float maxX = laneStep * 0.45f;
            if (spawnClearDistanceX > maxX) spawnClearDistanceX = maxX;
        }

        // 프리팹 높이 캐시
        hLog = GetPrefabWorldHeight(logPrefab);
        hMud = GetPrefabWorldHeight(mudPrefab);
        hRock = GetPrefabWorldHeight(rockPrefab);

        // 주기/페이즈
        RecomputeIntervals(applyPhase: true);

        // 바위 스케줄 시작
        nextRockTime = Time.time + Mathf.Max(0f, rockFirstDelay);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        logTimer += dt;
        mudTimer += dt;

        // 통나무(0,1)
        if (logTimer >= logSpawnInterval)
        {
            logTimer -= logSpawnInterval;
            TrySpawnLogWithConstraints();
        }

        // 진흙(0,1,2)
        if (mudTimer >= mudSpawnInterval)
        {
            mudTimer -= mudSpawnInterval;
            TrySpawnMudBurst();
        }

        // 바위 스케줄
        if (!rockRoutineRunning && Time.time >= nextRockTime)
        {
            StartCoroutine(RockRoutineFollowPlayerAndDrop());
            nextRockTime += Mathf.Max(0.1f, rockInterval);
        }
    }

    void OnValidate()
    {
        minVerticalGap = Mathf.Max(0.1f, minVerticalGap);
        spawnerLockDuration = Mathf.Max(0.01f, spawnerLockDuration);
        mudBanDistance = Mathf.Max(0f, mudBanDistance);
        rockWarnBlinks = Mathf.Max(1, rockWarnBlinks);
        rockFallSpeed = Mathf.Max(scrollSpeed + 0.1f, rockFallSpeed);

        RecomputeIntervals(applyPhase: false);

        // 프리팹 높이 갱신(에디터에서 프리팹 교체 시 반영)
        hLog = GetPrefabWorldHeight(logPrefab);
        hMud = GetPrefabWorldHeight(mudPrefab);
        hRock = GetPrefabWorldHeight(rockPrefab);
    }

    // ─────────────────────────────────────────────────────────
    //   스폰 루프 (Log / Mud)
    // ─────────────────────────────────────────────────────────
    private void TrySpawnLogWithConstraints()
    {
        int first;
        if (lastLogIndex == -1) first = Random.Range(0, 2);
        else if (logSameCount >= 2) first = 1 - lastLogIndex; // 강제 반대
        else first = Random.Range(0, 2);

        int second = 1 - first;

        int chosen = -1;
        if (TrySpawnAtIndex(first, ObstacleMover.ObstacleType.Log)) chosen = first;
        else if (TrySpawnAtIndex(second, ObstacleMover.ObstacleType.Log)) chosen = second;

        if (chosen != -1)
        {
            if (lastLogIndex == chosen) logSameCount++;
            else { lastLogIndex = chosen; logSameCount = 1; }

            // 같은 스포너 진흙 금지 (거리/속도 기반)
            float banTime = (scrollSpeed > 0f) ? (mudBanDistance / scrollSpeed) : 0.5f;
            mudBanUntil[chosen] = Time.time + banTime + mudBanPadding;
        }
    }

    private void TrySpawnMudBurst()
    {
        int want = 1;
        if (Random.value < mudChance3) want = 3;
        else if (Random.value < mudChance2) want = 2;

        List<int> candidates = new() { 0, 1, 2 };
        Shuffle(candidates);

        int spawned = 0;
        for (int i = 0; i < candidates.Count && spawned < want; i++)
        {
            int idx = candidates[i];
            if (Time.time < mudBanUntil[idx]) continue;  // 통나무 이후 금지
            if (Time.time < globalBanUntil[idx]) continue; // 바위 예고 중 전면 차단

            if (TrySpawnAtIndex(idx, ObstacleMover.ObstacleType.Mud))
                spawned++;
        }
    }

    // ─────────────────────────────────────────────────────────
    //   바위 시퀀스: 경고(플레이어 라인 추적) → 낙하(라인 고정)
    // ─────────────────────────────────────────────────────────
    private IEnumerator RockRoutineFollowPlayerAndDrop()
    {
        rockRoutineRunning = true;

        float warnTotal = rockWarnBlinks * (rockWarnOn + rockWarnOff);
        float warnEnd = Time.time + warnTotal;

        // 경고 오브젝트 준비
        GameObject warn = GetWarning();
        if (!warn)
        {
            // 경고 풀 부족해도 타이밍만 유지
            yield return new WaitForSeconds(warnTotal);
        }
        else
        {
            // 경고: 깜빡이면서 플레이어 라인을 "따라감"
            float nextToggle = 0f;
            bool visible = false;

            // 예고 중 제약: 동적으로 타깃 라인/차단 라인 갱신
            rockTargetLane = GetLaneIndexByPlayerX();
            UpdateRockBlockLane(rockTargetLane, warnEnd);

            while (Time.time < warnEnd)
            {
                // 플레이어 라인 추적 갱신
                int currentLane = GetLaneIndexByPlayerX();
                if (currentLane != rockTargetLane)
                {
                    rockTargetLane = currentLane;
                    UpdateRockBlockLane(rockTargetLane, warnEnd); // 차단 라인 교체
                }

                // 경고 위치를 현재 타깃 라인에 맞춰 이동 (카메라 상단 기준)
                float camTop = cam ? cam.transform.position.y + cam.orthographicSize : spawners[rockTargetLane].position.y + 4f;
                Vector3 wpos = new Vector3(spawners[rockTargetLane].position.x, camTop - warnYFromTop, 0f);
                warn.transform.position = wpos;

                // 깜빡 (on/off)
                if (Time.time >= nextToggle)
                {
                    visible = !visible;
                    warn.SetActive(visible);
                    nextToggle = Time.time + (visible ? rockWarnOn : rockWarnOff);
                }

                yield return null; // 매 프레임 추적
            }

            warn.SetActive(false);
            ReturnWarning(warn);
        }

        // 낙하 시작: 현재 라인을 고정하고 그 위치로 바위 스폰
        rockLockedLane = rockTargetLane;   // ★ 이 순간 이후로 더 이상 추적하지 않음
        rockTargetLane = -1;               // 추적 종료
        ClearRockBlockLane();               // 전면 차단 해제

        // 카메라 상단 바깥에서 시작
        float startY = (cam ? cam.transform.position.y + cam.orthographicSize : spawners[rockLockedLane].position.y + 5f) + rockStartYOffset;
        Vector3 spawnPos = new Vector3(spawners[rockLockedLane].position.x, startY, spawners[rockLockedLane].position.z);

        var rock = SpawnFromPool(rockPool, ObstacleMover.ObstacleType.Rock, spawnPos);
        if (rock != null) rock.moveSpeed = rockFallSpeed;

        rockLockedLane = -1;
        rockRoutineRunning = false;
        yield break;
    }

    private int GetLaneIndexByPlayerX()
    {
        float px = player.position.x;
        int best = 0;
        float bestDist = Mathf.Abs(px - spawners[0].position.x);
        for (int i = 1; i < 3; i++)
        {
            float d = Mathf.Abs(px - spawners[i].position.x);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private void UpdateRockBlockLane(int targetLane, float untilTime)
    {
        // 전면 차단 라인 교체: 이전 차단 해제
        if (rockBlockedLane != -1)
            globalBanUntil[rockBlockedLane] = 0f;

        if (!blockOneOtherLane)
        {
            rockBlockedLane = -1;
            return;
        }

        // 타깃 외의 두 라인 중 하나를 무작위 전면 차단
        List<int> others = new() { 0, 1, 2 };
        others.Remove(targetLane);
        rockBlockedLane = others[Random.Range(0, others.Count)];
        globalBanUntil[rockBlockedLane] = untilTime;
    }

    private void ClearRockBlockLane()
    {
        if (rockBlockedLane != -1)
            globalBanUntil[rockBlockedLane] = 0f;
        rockBlockedLane = -1;
    }

    // ─────────────────────────────────────────────────────────
    //   공통 스폰/체크
    // ─────────────────────────────────────────────────────────
    private bool TrySpawnAtIndex(int spawnerIndex, ObstacleMover.ObstacleType newType)
    {
        // 바위 예고 중: 통나무는 타깃 라인에서만 허용
        if (restrictLogToRockLane &&
            rockTargetLane != -1 &&
            newType == ObstacleMover.ObstacleType.Log &&
            spawnerIndex != rockTargetLane)
            return false;

        // 바위 예고 중 전면 차단 라인
        if (Time.time < globalBanUntil[spawnerIndex])
            return false;

        // 레인 최소 간격 시간 게이트
        float gapTimeForType = GetGapTimeForType(newType);
        if (Time.time < laneNextSpawnTime[spawnerIndex])
            return false;

        // 스포너 잠금
        if (Time.time < spawnerNextFreeTime[spawnerIndex])
            return false;

        Vector3 pos = spawners[spawnerIndex].position;

        // 거리/크기 기반 겹침 검사(동적)
        if (!IsSpawnerClearForType(pos, newType))
            return false;

        // 통나무→진흙 금지
        if (newType == ObstacleMover.ObstacleType.Mud && Time.time < mudBanUntil[spawnerIndex])
            return false;

        // 풀에서 꺼내기
        ObstacleMover mover = null;
        switch (newType)
        {
            case ObstacleMover.ObstacleType.Log:
                if (logPool.Count <= 0) return false;
                mover = logPool.Dequeue();
                break;
            case ObstacleMover.ObstacleType.Mud:
                if (mudPool.Count <= 0) return false;
                mover = mudPool.Dequeue();
                break;
            case ObstacleMover.ObstacleType.Rock:
                if (rockPool.Count <= 0) return false;
                mover = rockPool.Dequeue();
                break;
        }

        // 세팅/활성화
        mover.type = newType;
        mover.moveSpeed = (newType == ObstacleMover.ObstacleType.Rock) ? rockFallSpeed : scrollSpeed;
        mover.transform.position = pos;
        mover.owner = this;
        mover.gameObject.SetActive(true);
        active.Add(mover);

        // 잠금 + 시간 게이트 갱신 (타입별 간격 반영)
        spawnerNextFreeTime[spawnerIndex] = Time.time + spawnerLockDuration;
        laneNextSpawnTime[spawnerIndex] = Time.time + gapTimeForType;

        return true;
    }

    private bool IsSpawnerClearForType(Vector3 spawnPos, ObstacleMover.ObstacleType newType)
    {
        // 새 오브젝트 높이(월드)
        float hNew = GetTypeWorldHeight(newType);
        float halfNew = hNew * 0.5f;

        for (int i = 0; i < active.Count; i++)
        {
            var a = active[i];
            Vector3 ap = a.transform.position;

            // 같은 레인인지(가로 허용치 내)
            if (Mathf.Abs(ap.x - spawnPos.x) > spawnClearDistanceX)
                continue;

            // 활성 오브젝트 실제 높이
            float hA = GetActiveWorldHeight(a);
            float halfA = hA * 0.5f;

            // 필요 최소 세로 간격 = 두 박스 반높이 합 + 패딩
            float required = halfA + halfNew + verticalGapPadding;

            if (Mathf.Abs(ap.y - spawnPos.y) <= required)
                return false;
        }
        return true;
    }

    private float GetGapTimeForType(ObstacleMover.ObstacleType t)
    {
        // 타입별 "한 칸 내려갈 시간" = (자기 높이 + 패딩) / 속도
        float h = GetTypeWorldHeight(t) + verticalGapPadding;
        float v = (t == ObstacleMover.ObstacleType.Rock) ? rockFallSpeed : scrollSpeed;
        if (v <= 0f) v = 0.5f; // 안전
        return h / v;
    }

    // ─────────────────────────────────────────────────────────
    //   풀/유틸
    // ─────────────────────────────────────────────────────────
    private void CreatePool(GameObject prefab, Queue<ObstacleMover> pool, int size, ObstacleMover.ObstacleType type)
    {
        for (int i = 0; i < size; i++)
        {
            var go = Instantiate(prefab, Vector3.down * 9999f, Quaternion.identity, obstacleParent);
            go.SetActive(false);

            var mover = go.GetComponent<ObstacleMover>();
            if (!mover) mover = go.AddComponent<ObstacleMover>();
            mover.owner = this;
            mover.moveSpeed = scrollSpeed;
            mover.type = type;

            pool.Enqueue(mover);
        }
    }

    private void CreateWarnPool()
    {
        for (int i = 0; i < warnPoolSize; i++)
        {
            var go = Instantiate(warningPrefab, Vector3.down * 9999f, Quaternion.identity, obstacleParent);
            go.SetActive(false);
            warnPool.Enqueue(go);
        }
    }

    private GameObject GetWarning()
    {
        if (warnPool.Count > 0) return warnPool.Dequeue();
        return null;
    }

    private void ReturnWarning(GameObject go)
    {
        if (!go) return;
        go.SetActive(false);
        warnPool.Enqueue(go);
    }

    private ObstacleMover SpawnFromPool(Queue<ObstacleMover> pool, ObstacleMover.ObstacleType type, Vector3 pos)
    {
        if (pool.Count <= 0) return null;
        var mover = pool.Dequeue();
        mover.type = type;
        mover.owner = this;
        mover.moveSpeed = (type == ObstacleMover.ObstacleType.Rock) ? rockFallSpeed : scrollSpeed;
        mover.transform.position = pos;
        mover.gameObject.SetActive(true);
        active.Add(mover);
        return mover;
    }
    /// <summary>
    /// 모든 장애물 이동/애니메이션 정지 + 스폰 중단
    /// (PlayerCollisionHandler에서 호출)
    /// </summary>
    public void PauseAllAndStopSpawning()
    {
        // 스폰 중단(이 스크립트 Update 멈춤)
        this.enabled = false;

        // 활성 장애물 이동/애니메이션 정지
        // (active 리스트/각 mover는 네 기존 매니저에 이미 존재)
        var toStop = new List<ObstacleMover>();
        // active 컬렉션이 private이면, 클래스 내부에서 접근 가능
        // 외부 호출 시점엔 이미 클래스 내부라 접근 가능

        // active가 클래스에 private List<ObstacleMover> active 라면:
        // 그냥 아래처럼 사용 가능
        foreach (var mover in new List<ObstacleMover>(/* active 컬렉션 참조 */ active))
        {
            if (mover == null) continue;
            mover.moveSpeed = 0f;
            var anim = mover.GetComponentInChildren<Animator>();
            if (anim) anim.speed = 0f;
        }
    }
    /// <summary>
    /// 스폰 중단 + 모든 활성 장애물 즉시 비활성화(풀 반환)
    /// </summary>
    public void StopAndClearAllObstacles()
    {
        // 스폰/업데이트 중단
        this.enabled = false;

        // 활성 리스트 스냅샷 후 전부 반환
        var snapshot = new List<ObstacleMover>(active);
        foreach (var mover in snapshot)
        {
            if (mover == null) continue;
            Despawn(mover); // 비활성화 + 풀 복귀
        }
    }
    public void Despawn(ObstacleMover mover)
    {
        if (!mover) return;

        mover.gameObject.SetActive(false);
        active.Remove(mover);

        switch (mover.type)
        {
            case ObstacleMover.ObstacleType.Log: logPool.Enqueue(mover); break;
            case ObstacleMover.ObstacleType.Mud: mudPool.Enqueue(mover); break;
            case ObstacleMover.ObstacleType.Rock: rockPool.Enqueue(mover); break;
            default: logPool.Enqueue(mover); break;
        }
    }

    public void SetScrollSpeed(float newSpeed)
    {
        scrollSpeed = newSpeed;
        for (int i = 0; i < active.Count; i++)
            active[i].moveSpeed = (active[i].type == ObstacleMover.ObstacleType.Rock) ? rockFallSpeed : newSpeed;
    }

    public void ApplyIntervalsFromInspector(bool applyPhase = true)
    {
        RecomputeIntervals(applyPhase);
        // 높이는 OnValidate에서 갱신됨
    }

    // ─────────────────────────────────────────────────────────
    //   간격/주기 계산
    // ─────────────────────────────────────────────────────────
    private void RecomputeIntervals(bool applyPhase)
    {
        mudSpawnInterval = mudIntervalIsRelative
            ? Mathf.Max(0.05f, logSpawnInterval * mudIntervalFactor)
            : Mathf.Max(0.05f, mudSpawnIntervalOverride);

        if (applyPhase && useMudPhaseOffset)
        {
            mudTimer = -mudSpawnInterval * mudPhaseOffsetRatio;
        }
    }

    // ─────────────────────────────────────────────────────────
    //   높이 유틸
    // ─────────────────────────────────────────────────────────
    private float GetPrefabWorldHeight(GameObject prefab)
    {
        if (!prefab) return 0f;
        var sr = prefab.GetComponentInChildren<SpriteRenderer>();
        if (!sr || !sr.sprite) return 0f;
        return sr.bounds.size.y; // 월드 유닛 기준 높이
    }

    private float GetTypeWorldHeight(ObstacleMover.ObstacleType t)
    {
        switch (t)
        {
            case ObstacleMover.ObstacleType.Log: return Mathf.Max(minVerticalGap, hLog);
            case ObstacleMover.ObstacleType.Mud: return Mathf.Max(minVerticalGap * 0.7f, hMud); // 진흙은 얕으면 약간 낮춰도 됨
            case ObstacleMover.ObstacleType.Rock: return Mathf.Max(minVerticalGap, hRock);
        }
        return minVerticalGap;
    }

    private float GetActiveWorldHeight(ObstacleMover mover)
    {
        var sr = mover.GetComponentInChildren<SpriteRenderer>();
        if (!sr || !sr.sprite)
        {
            // 프리팹 캐시로 대체
            return GetTypeWorldHeight(mover.type);
        }
        return sr.bounds.size.y;
    }

    // ─────────────────────────────────────────────────────────
    //   기타 유틸
    // ─────────────────────────────────────────────────────────
    private void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
