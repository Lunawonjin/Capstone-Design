using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// 월드의 캐릭터 Transform(머리 위치 등)을 따라 UI 말풍선(RectTransform)을 화면에 배치
/// ─ 핵심 추가(요청 반영)
/// 1) 줄마다 타겟이 확실히 바뀌도록 강제 리타겟 API/브로드캐스트 지원
///    - SetSpeakerId("Player") : 내부 매핑으로 Target 교체+스냅
///    - SetTarget(Transform)   : 직접 Target 교체+스냅
///    - WorldBubbleAnchor.BroadcastRetarget("default", playerHead)              // 타겟으로 리타겟
///    - WorldBubbleAnchor.BroadcastRetargetSpeaker("default", "President")      // 화자ID로 리타겟
/// 2) 매핑 리스트(speakers) 보유: 이 스크립트 하나만 수정해서 화자→머리 Transform 전환 가능
/// 3) Screen Space Overlay/Camera 모두 지원, 화면 가장자리 클램프 지원
/// </summary>
[DisallowMultipleComponent]
public class WorldBubbleAnchor : MonoBehaviour
{
    // =========================
    // 전역 브로드캐스트(줄마다 강제 리타겟)
    // =========================
    public struct RetargetMsg
    {
        public string channel;     // 채널명(여러 말풍선 구분용)
        public Transform target;   // 직접 타겟 지정
        public string speakerId;   // 화자ID로 매핑 지정
        public Canvas canvas;      // 선택: 캔버스 교체
        public RetargetMsg(string ch, Transform t, string spk, Canvas c = null)
        { channel = ch; target = t; speakerId = spk; canvas = c; }
    }

    /// <summary>타겟으로 리타겟 브로드캐스트</summary>
    public static void BroadcastRetarget(string channel, Transform newTarget, Canvas newCanvas = null)
        => OnRetarget?.Invoke(new RetargetMsg(channel, newTarget, null, newCanvas));

    /// <summary>화자ID로 리타겟 브로드캐스트(내부 매핑 사용)</summary>
    public static void BroadcastRetargetSpeaker(string channel, string speakerId, Canvas newCanvas = null)
        => OnRetarget?.Invoke(new RetargetMsg(channel, null, speakerId, newCanvas));

    private static event Action<RetargetMsg> OnRetarget;

    // =========================
    // 화자 매핑(이 스크립트 내부 보유)
    // =========================
    [Serializable]
    public class SpeakerEntry
    {
        [Tooltip("키 접미사와 동일한 화자 ID (예: Player, President)")]
        public string speakerId;
        [Tooltip("이 화자의 머리 Transform(말풍선이 붙을 위치)")]
        public Transform head;
    }

    [Header("브로드캐스트 자동 수신")]
    [SerializeField] private bool autoRetargetFromBroadcast = true;
    [SerializeField] private string broadcastChannel = "default";

    [Header("화자 → 머리 Transform 매핑(선택)")]
    [SerializeField] private SpeakerEntry[] speakers;

    // =========================
    // 기존 필드
    // =========================
    [Header("필수 참조")]
    [SerializeField] private Transform target;          // 따라갈 월드 대상(캐릭터 머리 등)
    [SerializeField] private RectTransform bubbleRect;  // 말풍선 루트 UI(RectTransform)
    [SerializeField] private Canvas rootCanvas;         // 화면 UI 캔버스(Screen Space)

    [Header("위치 옵션")]
    [Tooltip("월드 기준 머리 위 오프셋")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.8f, 0f);
    [Tooltip("스크린 좌표에서의 픽셀 보정")]
    [SerializeField] private Vector2 screenOffset = Vector2.zero;
    [Tooltip("화면 밖으로 나가지 않도록 가장자리에서 막기")]
    [SerializeField] private bool clampToScreen = true;
    [Tooltip("가장자리 여백(픽셀)")]
    [SerializeField] private float clampPadding = 8f;

    [Header("디버그/진단")]
    [SerializeField] private bool debugLog = false;      // 계산 결과 로그
    [SerializeField] private Color gizmoColor = new Color(1f, 0.6f, 0.1f, 0.85f);

    private Camera cam; // 월드 → 스크린 변환용 카메라

    // ===== 프로퍼티 =====
    public Transform Target
    {
        get => target;
        set { target = value; SnapNow(); }
    }
    public RectTransform BubbleRect => bubbleRect;
    public Canvas RootCanvas => rootCanvas;

    // ===== 라이프사이클 =====
    void Awake()
    {
        if (rootCanvas == null) rootCanvas = GetComponentInParent<Canvas>();
        if (bubbleRect == null) bubbleRect = GetComponent<RectTransform>();
        RefreshCamera();
    }

    void OnEnable()
    {
        if (autoRetargetFromBroadcast) OnRetarget += HandleGlobalRetarget;
    }

    void OnDisable()
    {
        if (autoRetargetFromBroadcast) OnRetarget -= HandleGlobalRetarget;
    }

    void OnValidate()
    {
        if (bubbleRect == null) bubbleRect = GetComponent<RectTransform>();
        if (rootCanvas == null) rootCanvas = GetComponentInParent<Canvas>();
    }

    void LateUpdate()
    {
        UpdatePosition();
    }

    // ===== 공개 API =====

    /// <summary>화자 ID로 Target 교체(내부 매핑 사용) + 즉시 스냅</summary>
    public void SetSpeakerId(string speakerId, bool snapNow = true)
    {
        var head = ResolveHead(speakerId);
        if (head == null)
        {
            if (debugLog) Debug.LogWarning($"[WorldBubbleAnchor] 화자 '{speakerId}' 매핑 없음");
            return;
        }
        SetTarget(head, snapNow);
    }

    /// <summary>타겟 교체 + 즉시 스냅</summary>
    public void SetTarget(Transform t, bool snapNow = true)
    {
        target = t;
        if (snapNow) SnapNow();
    }

    /// <summary>캔버스 교체(+카메라 재설정)</summary>
    public void SetRootCanvas(Canvas c, bool snap = true)
    {
        rootCanvas = c;
        RefreshCamera();
        if (snap) SnapNow();
    }

    /// <summary>월드→스크린 변환에 사용할 카메라 지정</summary>
    public void SetWorldCamera(Camera c, bool snap = true)
    {
        cam = c;
        if (snap) SnapNow();
    }

    /// <summary>당장 한 프레임 내에 위치 즉시 갱신</summary>
    public void SnapNow() => UpdatePosition();

    /// <summary>월드 오프셋 조정</summary>
    public void SetWorldOffset(Vector3 offset, bool snap = true)
    {
        worldOffset = offset;
        if (snap) SnapNow();
    }

    /// <summary>스크린 오프셋 조정</summary>
    public void SetScreenOffset(Vector2 offset, bool snap = true)
    {
        screenOffset = offset;
        if (snap) SnapNow();
    }

    /// <summary>화면 가장자리 클램프 설정</summary>
    public void SetClamp(bool on, float padding = -1f)
    {
        clampToScreen = on;
        if (padding >= 0f) clampPadding = padding;
        SnapNow();
    }

    // ===== 내부 구현 =====

    /// <summary>전역 리타겟 이벤트 수신(채널 일치 시 적용)</summary>
    private void HandleGlobalRetarget(RetargetMsg msg)
    {
        if (!autoRetargetFromBroadcast) return;
        if (!string.Equals((broadcastChannel ?? "").Trim(), (msg.channel ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        if (msg.canvas != null) SetRootCanvas(msg.canvas, snap: false);

        // target 우선, 없으면 speakerId 매핑 사용
        if (msg.target != null) SetTarget(msg.target, snapNow: true);
        else if (!string.IsNullOrEmpty(msg.speakerId)) SetSpeakerId(msg.speakerId, snapNow: true);
    }

    /// <summary>월드 좌표 → 스크린 → 캔버스 로컬로 변환하여 anchoredPosition 갱신</summary>
    private void UpdatePosition()
    {
        if (bubbleRect == null)
        {
            if (debugLog) Debug.LogWarning($"[WorldBubbleAnchor] bubbleRect 누락 ({name})");
            return;
        }
        if (rootCanvas == null)
        {
            if (debugLog) Debug.LogWarning($"[WorldBubbleAnchor] rootCanvas 누락 ({name})");
            return;
        }
        if (target == null)
        {
            if (debugLog) Debug.LogWarning($"[WorldBubbleAnchor] target 누락 ({name})");
            return;
        }

        // 캔버스/카메라 보정
        if (rootCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            if (rootCanvas.worldCamera == null && cam == null)
                rootCanvas.worldCamera = Camera.main;
            if (cam != rootCanvas.worldCamera)
                cam = rootCanvas.worldCamera;
        }
        else
        {
            if (cam == null) cam = Camera.main; // Overlay에서도 WorldToScreenPoint용 카메라 필요
        }

        // 월드→스크린
        Vector3 worldPos = target.position + worldOffset;
        Vector3 screenPos = RectTransformUtility.WorldToScreenPoint(cam, worldPos);

        // 스크린→캔버스 로컬
        RectTransform canvasRect = rootCanvas.transform as RectTransform;
        if (canvasRect == null)
        {
            if (debugLog) Debug.LogWarning($"[WorldBubbleAnchor] rootCanvas가 RectTransform 아님 ({name})");
            return;
        }

        Vector2 anchoredPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPos,
            (rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : cam,
            out anchoredPos
        );

        anchoredPos += screenOffset;

        // 가장자리 클램프
        if (clampToScreen)
        {
            Vector2 half = canvasRect.rect.size * 0.5f;
            float minX = -half.x + clampPadding;
            float maxX = half.x - clampPadding;
            float minY = -half.y + clampPadding;
            float maxY = half.y - clampPadding;

            anchoredPos.x = Mathf.Clamp(anchoredPos.x, minX, maxX);
            anchoredPos.y = Mathf.Clamp(anchoredPos.y, minY, maxY);
        }

        bubbleRect.anchoredPosition = anchoredPos;

        if (debugLog)
        {
            Debug.Log($"[WorldBubbleAnchor] '{name}' " +
                      $"Canvas='{rootCanvas.name}' Mode={rootCanvas.renderMode} Cam='{cam?.name ?? "null"}' | " +
                      $"Target='{target.name}' World={worldPos} Screen={screenPos} Anchored={anchoredPos}");
        }
    }

    /// <summary>캔버스 모드에 따라 카메라 재설정</summary>
    private void RefreshCamera()
    {
        if (rootCanvas == null) { cam = Camera.main; return; }

        if (rootCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            if (rootCanvas.worldCamera == null)
                rootCanvas.worldCamera = Camera.main;
            cam = rootCanvas.worldCamera;
        }
        else
        {
            cam = Camera.main; // Overlay
        }
    }

    /// <summary>화자ID로 매핑된 머리 Transform 찾기</summary>
    private Transform ResolveHead(string idRaw)
    {
        if (speakers == null || speakers.Length == 0) return null;
        string id = (idRaw ?? "").Trim();
        for (int i = 0; i < speakers.Length; i++)
        {
            var s = speakers[i];
            if (s == null) continue;
            if (string.Equals((s.speakerId ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase))
                return s.head;
        }
        return null;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = gizmoColor;
        Gizmos.DrawLine(target.position, target.position + worldOffset);
        Gizmos.DrawSphere(target.position + worldOffset, 0.05f);
    }
#endif
}
