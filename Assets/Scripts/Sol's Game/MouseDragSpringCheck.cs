using UnityEngine;

/// <summary>
/// 마우스로 오브젝트를 드래그하여 이동시키고, 마우스를 놓으면 스프링처럼 원래 위치로 복귀.
/// 추가 기능:
/// - 드래그 중 다른 오브젝트와 겹치면(Trigger) 태그와 uniqueId가 모두 동일한지 검사.
///   일치하면:
///   1) 퍼즐 매니저에 매칭 성공 보고(ReportMatchOnce).
///   2) 자기 자신 비활성화(SetActive(false)).
///   3) 상대 오브젝트의 SpriteRenderer 알파를 1.0으로 설정(완전 불투명).
/// - 드래그 중에는 항상 레이어 최상위(sortingOrder=999).
/// - 복귀 중에는 일반 오브젝트보다 위, 드래그 중인 오브젝트보다는 아래(sortingOrder=998).
/// 사용 조건:
/// - 이 오브젝트: Collider2D(IsTrigger=ON), Rigidbody2D(중력 0, 회전 고정), SpriteRenderer.
/// - 상대 오브젝트: Collider2D(IsTrigger=ON), SpriteRenderer, 이 스크립트 포함(고유값 비교용).
/// - 최소 한쪽에 Rigidbody2D가 있어야 Trigger 이벤트가 호출됨(여기서는 이쪽이 보유).
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Rigidbody2D))]
public class MouseDragSpringCheck : MonoBehaviour
{
    [Header("드래그 기능 켜기/끄기")]
    [Tooltip("드래그 동작을 허용할지 여부. false면 입력 및 이동 무시.")]
    public bool dragEnabled = true;

    [Header("드래그 설정")]
    [Tooltip("드래그 중, 마우스 목표 위치에 수렴하는 속도. 클수록 빠르게 붙음.")]
    public float followLerp = 20f;

    [Tooltip("2D 씬에서 고정할 Z 좌표 값.")]
    public float fixedZ = 0f;

    [Header("스프링 설정")]
    [Tooltip("스프링 강성. 클수록 원위치로 더 빠르고 강하게 복귀.")]
    public float springStiffness = 25f;

    [Tooltip("감쇠 계수. 클수록 진동이 빨리 줄어듦. 너무 크면 진동 없이 달라붙음.")]
    public float springDamping = 6f;

    [Tooltip("복귀 중 정지 판정에 사용할 속도 임계값(제곱 길이 기준).")]
    public float settleVelocityEpsilon = 0.01f;

    [Tooltip("복귀 중 정지 판정에 사용할 거리 임계값(제곱 길이 기준).")]
    public float settleDistanceEpsilon = 0.0025f;

    [Header("오브젝트 고유값")]
    [Tooltip("같은 그룹으로 인정할 문자열 ID. 태그와 이 값이 모두 같아야 매칭 성공.")]
    public string uniqueId;

    [Header("연결")]
    [Tooltip("퍼즐 매칭을 집계할 PuzzleManager. 비었으면 자동 탐색.")]
    public PuzzleManager puzzleManager;

    // 내부 상태 및 캐시
    private Camera cam;                    // 마우스 스크린좌표 -> 월드좌표 변환에 사용
    private Vector3 restPosition;          // 복귀 대상 원점(시작 위치)
    private Vector3 velocity;              // 스프링 운동에 사용할 속도
    private bool isDragging = false;       // 현재 드래그 중인지 여부
    private Vector3 dragOffsetWorld;       // 마우스 시작점과 오브젝트 중심 간 오프셋

    private SpriteRenderer sr;             // 본인 색/레이어 제어용
    private int defaultSortingOrder;       // 시작 시 레이어 정렬값 저장

    // 매칭이 성사되어 소비된 조각인지(중복 보고 방지).
    private bool matchedAndConsumed = false;

    private void Awake()
    {
        cam = Camera.main;
        sr = GetComponent<SpriteRenderer>();

        // Rigidbody2D 기본 설정: 트리거 감지를 위해 필수.
        var rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;                            // 중력 영향 제거
        rb.constraints = RigidbodyConstraints2D.FreezeRotation; // 회전 고정(픽셀아트 등에서 흔들림 방지)

        // 퍼즐 매니저 자동 연결(인스펙터 미지정 시).
        if (puzzleManager == null)
        {
            puzzleManager = FindObjectOfType<PuzzleManager>();
            if (puzzleManager == null)
            {
                Debug.LogWarning("[드래그] PuzzleManager를 찾지 못했습니다. 매칭 보고가 이뤄지지 않습니다.");
            }
        }

        // 트리거 조건 안내(개발 편의상 경고만 출력).
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger)
        {
            Debug.LogWarning("[드래그] Collider2D의 IsTrigger를 켜세요. 겹침 이벤트가 발생하지 않습니다.");
        }
    }

    private void Start()
    {
        // 시작 위치를 복귀 원점으로 저장.
        restPosition = transform.position;

        // 스프링 속도 초기화.
        velocity = Vector3.zero;

        // 현재 레이어 정렬값 저장.
        defaultSortingOrder = sr.sortingOrder;
    }

    private void Update()
    {
        // 드래그 중일 때: 마우스 목표 위치로 지수 보간 수렴.
        if (isDragging && dragEnabled)
        {
            Vector3 mouseWorld = GetMouseWorld();
            Vector3 target = mouseWorld + dragOffsetWorld;  // 클릭 지점 유지
            target.z = fixedZ;

            // 지수 보간(프레임 독립): 1 - exp(-k * dt) 형태로 부드러운 수렴
            float t = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, t);

            // 드래그 중에는 스프링 속도를 사용하지 않으므로 0으로 유지.
            velocity = Vector3.zero;

            // 드래그 중에는 항상 최상위 레이어.
            sr.sortingOrder = 999;
        }
        else
        {
            // 드래그가 아니면 스프링 복귀(감쇠 진동).
            Vector3 x = transform.position;     // 현재 위치
            Vector3 toRest = x - restPosition;  // 원점까지의 벡터

            // 선형 스프링-댐퍼 모델: a = -k*x - c*v
            Vector3 accel = (-springStiffness * toRest) - (springDamping * velocity);

            // 속도/위치 적분(세미 암시적 오일러)
            velocity += accel * Time.deltaTime;
            x += velocity * Time.deltaTime;

            // 충분히 느리고, 충분히 가까우면 스냅 정지 처리.
            if (toRest.sqrMagnitude <= settleDistanceEpsilon && velocity.sqrMagnitude <= settleVelocityEpsilon)
            {
                x = restPosition;
                velocity = Vector3.zero;

                // 완전히 멈추면 원래 레이어 정렬값 복원.
                sr.sortingOrder = defaultSortingOrder;
            }
            else
            {
                // 복귀 중에는 일반 오브젝트보다 위, 드래그 중인 것보다는 아래.
                sr.sortingOrder = 998;
            }

            // Z를 고정하여 2D 계층을 유지.
            x.z = fixedZ;
            transform.position = x;
        }
    }

    private void OnMouseDown()
    {
        // 드래그가 비활성화되었거나, 이미 매칭되어 소비된 조각이면 입력 무시.
        if (!dragEnabled || matchedAndConsumed) return;

        // 마우스 좌표를 월드로 변환하고, 클릭 지점 기준 오프셋을 저장.
        Vector3 mouseWorld = GetMouseWorld();
        dragOffsetWorld = transform.position - mouseWorld;

        // 드래그 시작.
        isDragging = true;

        // 스프링 속도 초기화(이전 관성 제거).
        velocity = Vector3.zero;

        // 드래그 중 최상위로 올림.
        sr.sortingOrder = 999;
    }

    private void OnMouseUp()
    {
        if (!dragEnabled) return;

        // 드래그 종료. 이후 Update()에서 스프링 복귀가 작동.
        isDragging = false;
    }

    /// <summary>
    /// 스크린 좌표의 마우스를 월드 좌표로 변환.
    /// 2D 카메라에서 고정 Z면 카메라와 평면 간 거리로 z를 보정해야 정확히 찍힘.
    /// </summary>
    private Vector3 GetMouseWorld()
    {
        Vector3 m = Input.mousePosition;

        // 카메라 z와 대상 평면 z의 차이(양수 거리).
        float z = Mathf.Abs(cam.transform.position.z - fixedZ);
        m.z = z;

        return cam.ScreenToWorldPoint(m);
    }

    /// <summary>
    /// 겹침 시작 시점에 1회 호출된다.
    /// 조건:
    /// - 드래그 중이어야 함.
    /// - 상대 오브젝트에도 동일 스크립트가 붙어 있어야 uniqueId 비교 가능.
    /// - 태그와 uniqueId가 모두 동일해야 매칭 성립.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isDragging || !dragEnabled || matchedAndConsumed) return;

        // 상대 쪽 컴포넌트를 가져와 고유값 비교 가능하도록 한다.
        var otherComp = other.GetComponent<MouseDragSpringCheck>();
        if (otherComp == null) return;

        // 태그 일치 여부 및 uniqueId 일치 여부 확인.
        bool tagOK = other.CompareTag(gameObject.tag);
        bool idOK = (otherComp.uniqueId == this.uniqueId);

        if (tagOK && idOK)
        {
            Debug.Log("[드래그] 매칭 성공: " + name + " ↔ " + other.name);

            // 퍼즐 매니저에 1회 보고. 목표치 도달 시 타이머가 멈춘다.
            if (puzzleManager != null)
            {
                puzzleManager.ReportMatchOnce();
            }

            // 자기 자신은 더 이상 사용하지 않으므로 비활성화.
            matchedAndConsumed = true;
            gameObject.SetActive(false);

            // 상대 오브젝트의 불투명도를 100%로 설정(완전히 나타나도록).
            var otherSr = other.GetComponent<SpriteRenderer>();
            if (otherSr != null)
            {
                Color c = otherSr.color;
                c.a = 1f;
                otherSr.color = c;
            }
        }
    }

    /// <summary>
    /// 외부에서 드래그 가능 여부를 토글.
    /// 드래그 중일 때 비활성화되면 즉시 드래그를 해제하고 레이어도 복구.
    /// </summary>
    public void EnableDrag(bool enable)
    {
        dragEnabled = enable;

        if (!dragEnabled && isDragging)
        {
            isDragging = false;
            sr.sortingOrder = defaultSortingOrder;
        }
    }

    /// <summary>
    /// 런타임 중 새로운 복귀 원점을 지정하고 싶을 때 사용.
    /// </summary>
    public void ResetRestPosition(Vector3 newRest)
    {
        restPosition = new Vector3(newRest.x, newRest.y, fixedZ);
        velocity = Vector3.zero;
    }
}
