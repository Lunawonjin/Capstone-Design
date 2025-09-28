// EscOrButtonToCloseSelf.cs
// Unity 6 (LTS)
// 목적: ESC 또는 지정한 Button 클릭으로 "이 스크립트가 붙은 GameObject 자신"만 SetActive(false)
// 해결: 겹친 오브젝트가 모두 꺼지는 문제를 "최근에 활성화된 패널만 닫기" 스택으로 방지

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if TMP_PRESENT
using TMPro;
#endif

[DisallowMultipleComponent]
public class EscOrButtonToCloseSelf : MonoBehaviour
{
    // ====== 전역 스택: 최근 활성화된 패널이 맨 위(Top) ======
    private static readonly List<EscOrButtonToCloseSelf> s_stack = new List<EscOrButtonToCloseSelf>();

    [Header("ESC 입력 옵션")]
    [Tooltip("EventSystem에서 InputField/TMP_InputField에 포커스가 있으면 ESC를 무시")]
    [SerializeField] private bool ignoreWhenTyping = true;

    [Tooltip("Input.GetButtonDown(\"Cancel\")도 함께 인식 (ESC 기본 매핑)")]
    [SerializeField] private bool useInputCancelAxis = true;

    [Tooltip("activeInHierarchy 대신 activeSelf 기준으로만 끌지 여부")]
    [SerializeField] private bool requireActiveSelfOnly = false;

    [Header("겹침(중첩) 제어")]
    [Tooltip("겹쳐진 여러 패널이 있을 때, '스택의 최상단(가장 최근에 켜짐)'일 때만 ESC/버튼 입력 처리")]
    [SerializeField] private bool closeOnlyWhenTop = true;

    [Tooltip("스택에 참여하지 않음(= 항상 자기 자신만 ESC/버튼 처리). 특별한 경우가 아니면 권장하지 않음")]
    [SerializeField] private bool doNotParticipateInStack = false;

    [Header("버튼 옵션")]
    [Tooltip("이 버튼을 누르면 자신만 비활성화됩니다(선택). 비어 있으면 버튼 동작은 없음.")]
    [SerializeField] private Button closeButton;

    [Tooltip("closeButton이 비어 있을 때, 자식에서 첫 번째 Button을 자동 탐색해 사용")]
    [SerializeField] private bool autoFindFirstChildButton = false;

    [Header("디버그 로그")]
    [SerializeField] private bool logOnClose = false;

    // 구독 상태 관리
    private bool _buttonHooked = false;

    // ====== Unity 콜백 ======
    private void OnEnable()
    {
        // 스택 참여
        if (!doNotParticipateInStack)
            PushSelfToTop();

        HookButtonIfNeeded();
    }

    private void OnDisable()
    {
        UnhookButtonIfNeeded();

        // 스택 제거
        if (!doNotParticipateInStack)
            RemoveFromStack();
    }

    private void OnDestroy()
    {
        UnhookButtonIfNeeded();

        if (!doNotParticipateInStack)
            RemoveFromStack();
    }

    private void Update()
    {
        // 1) 활성 체크
        if (requireActiveSelfOnly)
        {
            if (!gameObject.activeSelf) return;
        }
        else
        {
            if (!gameObject.activeInHierarchy) return;
        }

        // 2) 스택 최상단 제약 (겹침 방지의 핵심)
        if (!doNotParticipateInStack && closeOnlyWhenTop && !IsTopOfStack())
            return;

        // 3) 입력란 포커스 시 ESC 무시(선택)
        if (ignoreWhenTyping && IsAnyInputFieldFocused())
            return;

        // 4) ESC / Cancel 감지
        bool escPressed = Input.GetKeyDown(KeyCode.Escape);
        if (!escPressed && useInputCancelAxis)
            escPressed = Input.GetButtonDown("Cancel");

        if (escPressed)
            CloseSelf();
    }

    // ====== 버튼 연결/해제 ======
    private void HookButtonIfNeeded()
    {
        if (_buttonHooked) return;

        if (closeButton == null && autoFindFirstChildButton)
            closeButton = GetComponentInChildren<Button>(includeInactive: true);

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseButtonClicked);
            _buttonHooked = true;
        }
    }

    private void UnhookButtonIfNeeded()
    {
        if (!_buttonHooked) return;
        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
        _buttonHooked = false;
    }

    private void OnCloseButtonClicked()
    {
        // 버튼으로 닫을 때도 스택 최상단 규칙 적용
        if (!doNotParticipateInStack && closeOnlyWhenTop && !IsTopOfStack())
            return;

        CloseSelf();
    }

    // ====== 스택 관리 ======
    private void PushSelfToTop()
    {
        // 먼저 기존에 있으면 제거
        int idx = s_stack.IndexOf(this);
        if (idx >= 0) s_stack.RemoveAt(idx);

        // 맨 끝(Top)으로 추가
        s_stack.Add(this);
    }

    private void RemoveFromStack()
    {
        int idx = s_stack.IndexOf(this);
        if (idx >= 0)
            s_stack.RemoveAt(idx);
    }

    private bool IsTopOfStack()
    {
        if (s_stack.Count == 0) return true; // 방어적 처리
        return s_stack[s_stack.Count - 1] == this;
    }

    // ====== 공통 유틸 ======
    private void CloseSelf()
    {
        if (logOnClose) Debug.Log($"[EscOrButtonToCloseSelf] Disable: {name}");
        gameObject.SetActive(false); // 자신만 비활성화
    }

    // 현재 UI 입력 필드가 선택되어 있는지 검사
    private static bool IsAnyInputFieldFocused()
    {
        GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel == null) return false;

        // UGUI InputField
        if (sel.GetComponent<InputField>() != null) return true;

        // TextMeshPro InputField (패키지 있을 때만)
#if TMP_PRESENT
        if (sel.GetComponent<TMP_InputField>() != null) return true;
#endif

        return false;
    }
}
