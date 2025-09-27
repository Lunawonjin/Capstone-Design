using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UIExclusiveManagerSingle
///
/// 목적
/// - 하나의 스크립트에서 여러 UI 오브젝트를 관리한다.
/// - 목록 중 하나라도 활성화되어 있으면(=현재 사용 중) 다른 오브젝트는 비활성화될 때까지 활성화되지 못하게 막는다.
/// - 외부에서 누가 SetActive(true) 를 해도 다음 프레임에 즉시 교정한다(워치독).
///
/// 사용법
/// 1) 빈 GameObject(예: UIRoot)에 본 스크립트를 붙인다.
/// 2) targets 에 상호 배타로 묶을 패널(또는 UI 루트)들을 순서대로 넣는다.
/// 3) 버튼에서는 본 스크립트의 TryActivateByIndex(index) 또는 TryActivate(GameObject) 를 호출한다.
///    - 현재 다른 패널이 열려 있으면 false 를 반환하고 아무 일도 하지 않는다.
/// 4) Esc 로 닫고 싶으면 escToClose 를 켜고, escBlocker 가 켜져 있을 때는 Esc 입력을 막고 싶다면 escBlocker 를 지정한다(선택).
///
/// 주의
/// - “현재 켜진 패널이 있을 때는 다른 패널을 여는 것 자체를 금지”하는 규칙이다(스위치 불가).
/// - 스위치(열려 있는 패널을 닫고 다른 패널을 곧바로 여는 행위)가 필요하다면 TrySwitchTo 계열을 별도로 구현하라.
/// </summary>
[DisallowMultipleComponent]
public class UIExclusiveManager : MonoBehaviour
{
    [Header("상호 배타 관리 대상들(동일 그룹)")]
    [Tooltip("여기에 등록된 오브젝트들만 상호 배타 규칙이 적용된다")]
    [SerializeField] private List<GameObject> targets = new List<GameObject>();

    [Header("시작 시 정리 옵션")]
    [Tooltip("중복/Null 제거 및 시작 시 다수가 켜져 있으면 하나만 남기고 끈다")]
    [SerializeField] private bool sanitizeOnStart = true;

    [Header("Esc 닫기(선택)")]
    [Tooltip("켜져 있는 대상이 있을 때 Esc 를 누르면 닫는다")]
    [SerializeField] private bool escToClose = false;

    [Tooltip("이 오브젝트가 활성화되어 있으면 Esc 입력을 막는다(경고창 등)")]
    [SerializeField] private GameObject escBlocker;

    [Header("외부 SetActive(true) 교정(워치독)")]
    [Tooltip("프레임마다 규칙 위반을 감지해 교정한다")]
    [SerializeField] private bool watchdogEnabled = true;

    [Header("Esc 동작 확장")]
    [Tooltip("아무 대상도 활성화되어 있지 않을 때 ESC를 누르면 첫 번째(targets[0])를 연다")]
    [SerializeField] private bool escOpensFirstWhenEmpty = true;

    /// <summary>현재 활성 인덱스(없으면 -1)</summary>
    public int CurrentActiveIndex { get; private set; } = -1;

    /// <summary>하나라도 켜져 있는가</summary>
    public bool IsAnyActive => CurrentActiveIndex >= 0;

    // 내부 임시 버퍼
    private static readonly List<int> _activeIndices = new List<int>(8);

    void Awake()
    {
        if (sanitizeOnStart)
        {
            SanitizeList();
        }
    }

    void Start()
    {
        if (sanitizeOnStart)
        {
            // 시작 시 다수가 켜져 있으면 하나만 유지
            ResolveMultipleActivesAtStart();
        }
        else
        {
            // 현 상태로 인덱스만 동기화
            SyncCurrentActiveIndex();
        }
    }

    void Update()
    {
        if (watchdogEnabled)
        {
            WatchdogEnforceExclusivity();
        }

        if (escToClose && IsAnyActive)
        {
            if (!IsEscBlocked() && Input.GetKeyDown(KeyCode.Escape))
            {
                ForceDeactivateAll();
            }
        }
    }

    // ===== 공개 API =====

    /// <summary>
    /// 대상 오브젝트를 활성화 시도.
    /// 이미 다른 대상이 활성 상태라면 실패(false).
    /// 성공 시 true.
    /// </summary>
    public bool TryActivate(GameObject target)
    {
        if (target == null) return false;
        int idx = targets.IndexOf(target);
        if (idx < 0)
        {
            Debug.LogWarning($"[UIExclusiveManagerSingle] 등록되지 않은 대상입니다: {target.name}");
            return false;
        }
        return TryActivateByIndex(idx);
    }

    /// <summary>
    /// 인덱스로 활성화 시도.
    /// 이미 다른 대상이 활성 상태라면 실패(false).
    /// 성공 시 true.
    /// </summary>
    public bool TryActivateByIndex(int index)
    {
        if (!IsValidIndex(index)) return false;

        if (IsAnyActive && CurrentActiveIndex != index)
        {
            // 규칙: 누가 켜져 있으면 다른 대상은 못 켠다
            return false;
        }

        var go = targets[index];
        if (go == null) return false;

        if (!go.activeSelf)
            go.SetActive(true);

        CurrentActiveIndex = index;
        return true;
    }

    /// <summary>
    /// 현재 활성 대상을 강제로 비활성화. 아무것도 없으면 무시.
    /// </summary>
    public void CloseCurrent()
    {
        if (!IsAnyActive) return;
        var go = targets[CurrentActiveIndex];
        if (go != null && go.activeSelf)
            go.SetActive(false);
        CurrentActiveIndex = -1;
    }

    /// <summary>
    /// 전체 비활성화(규칙 초기화).
    /// </summary>
    public void ForceDeactivateAll()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var go = targets[i];
            if (go != null && go.activeSelf)
                go.SetActive(false);
        }
        CurrentActiveIndex = -1;
    }

    /// <summary>
    /// 현재 활성 오브젝트를 반환(없으면 null).
    /// </summary>
    public GameObject GetCurrentActive()
    {
        if (!IsAnyActive) return null;
        return targets[CurrentActiveIndex];
    }

    /// <summary>
    /// 대상이 이 매니저의 목록에 포함되어 있는가
    /// </summary>
    public bool Contains(GameObject target)
    {
        return target != null && targets.IndexOf(target) >= 0;
    }

    // ===== 내부 로직 =====

    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < targets.Count;
    }

    private bool IsEscBlocked()
    {
        return escBlocker != null && escBlocker.activeInHierarchy;
    }

    private void SanitizeList()
    {
        // Null 제거 + 중복 제거
        var set = new HashSet<GameObject>();
        var clean = new List<GameObject>(targets.Count);
        foreach (var t in targets)
        {
            if (t == null) continue;
            if (set.Add(t))
                clean.Add(t);
        }
        targets = clean;
    }

    private void ResolveMultipleActivesAtStart()
    {
        _activeIndices.Clear();
        for (int i = 0; i < targets.Count; i++)
        {
            var go = targets[i];
            if (go != null && go.activeSelf)
                _activeIndices.Add(i);
        }

        if (_activeIndices.Count == 0)
        {
            CurrentActiveIndex = -1;
            return;
        }

        // 가장 앞의 하나만 남기고 나머지는 끈다
        int keep = _activeIndices[0];
        for (int k = 1; k < _activeIndices.Count; k++)
        {
            int idx = _activeIndices[k];
            var go = targets[idx];
            if (go != null && go.activeSelf)
                go.SetActive(false);
        }
        CurrentActiveIndex = keep;
        _activeIndices.Clear();
    }

    private void SyncCurrentActiveIndex()
    {
        // 첫 번째로 켜져 있는 인덱스를 취한다(여러 개면 실제 동작은 워치독이 교정)
        CurrentActiveIndex = -1;
        for (int i = 0; i < targets.Count; i++)
        {
            var go = targets[i];
            if (go != null && go.activeSelf)
            {
                CurrentActiveIndex = i;
                break;
            }
        }
    }

    private void WatchdogEnforceExclusivity()
    {
        // 현재 프레임에 누군가 외부에서 SetActive(true)를 호출해도
        // “하나만 허용” 규칙을 유지하도록 교정한다.
        _activeIndices.Clear();
        for (int i = 0; i < targets.Count; i++)
        {
            var go = targets[i];
            if (go != null && go.activeSelf)
                _activeIndices.Add(i);
        }

        if (_activeIndices.Count == 0)
        {
            // 아무도 안 켜짐
            CurrentActiveIndex = -1;
            return;
        }

        // 하나 이상 켜져 있으면 첫 번째만 유지
        int keep = _activeIndices[0];
        if (CurrentActiveIndex == -1)
        {
            // 이전에는 비어 있었는데 새로 누군가 켜짐 → 첫 번째를 현재로
            CurrentActiveIndex = keep;
        }
        else if (CurrentActiveIndex != keep)
        {
            // 현재로 기록된 대상과 첫 번째 활성 대상이 다르면
            // 규칙상 스위치를 허용하지 않으므로, 새로 켜진 것들을 끈다
            // 단, 현재 기록된 대상이 실제로 꺼졌다면(외부에서 끔)
            // 그때는 첫 번째를 현재로 갱신한다.
            var curGo = GetCurrentActive();
            if (curGo == null || !curGo.activeSelf)
            {
                // 현재가 사라졌으니 첫 번째로 교체
                CurrentActiveIndex = keep;
            }
        }

        // 첫 번째(keep)만 남기고 나머지 전부 끈다
        for (int n = 0; n < _activeIndices.Count; n++)
        {
            int idx = _activeIndices[n];
            if (idx == keep) continue;
            var go = targets[idx];
            if (go != null && go.activeSelf)
                go.SetActive(false);
        }

        _activeIndices.Clear();
    }
}
