using System;
using System.Collections;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 플레이어 이름 입력 UI.
/// - 인풋필드는 표시되자마자 자동 포커스(클릭 불필요)
/// - 엔터/확인 버튼으로 확정
/// - 확정 시 DataManager.nowPlayer.Name 갱신 + SubSaveCommit()으로 sub_save에 즉시 기록
/// - nowSlot 미설정 시 가장 최근 슬롯 또는 0번 슬롯을 사용하도록 보정
/// - 확정 시 지정한 오브젝트를 활성화(objectToActivateOnConfirm.SetActive(true))
/// - 확정 직후 대사에 {playerName} 즉시 반영(RefreshPlayerNameNow)
/// </summary>
public class PlayerNameInputUI : MonoBehaviour
{
    [Header("필수 참조")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button confirmButton;

    [Header("선택: 경고/안내 텍스트")]
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private string emptyWarningText = "이름을 입력해 주세요.";
    [SerializeField] private string savedMessageText = "이름이 저장되었습니다.";

    [Header("입력 설정")]
    [Tooltip("앞뒤 공백 자동 제거")]
    [SerializeField] private bool trimWhitespace = true;

    [Tooltip("최대 글자 수(0이면 제한 없음)")]
    [SerializeField] private int maxLength = 12;

    [Tooltip("허용 문자 정규식(비우면 비활성). 예: ^[가-힣a-zA-Z0-9_]+$")]
    [SerializeField] private string allowedPattern = "";

    [Tooltip("허용 패턴 불일치 시 보여줄 문구")]
    [SerializeField] private string patternWarningText = "허용되지 않는 문자가 포함되어 있습니다.";

    [Header("확정 후 처리")]
    [Tooltip("확정 후 이 UI를 비활성화")]
    [SerializeField] private bool disableSelfOnConfirm = true;

    [Tooltip("확정 후 이 오브젝트를 파괴")]
    [SerializeField] private bool destroySelfOnConfirm = false;

    [Tooltip("확정 후 추가 동작(씬 전환 등)")]
    public UnityEvent onConfirmed;

    [Header("확정 시 활성화할 오브젝트")]
    [Tooltip("확인 버튼을 누르면 이 오브젝트를 SetActive(true) 합니다.")]
    [SerializeField] private GameObject objectToActivateOnConfirm;

    private Regex _allowRegex;

    private void Reset()
    {
        inputField = GetComponentInChildren<TMP_InputField>(true);
        if (!confirmButton) confirmButton = GetComponentInChildren<Button>(true);
        if (!messageText) messageText = GetComponentInChildren<TMP_Text>(true);
    }

    private void Awake()
    {
        if (!string.IsNullOrEmpty(allowedPattern))
        {
            try { _allowRegex = new Regex(allowedPattern, RegexOptions.Compiled); }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerNameInputUI] 정규식 컴파일 실패: {allowedPattern}\n{e}");
                _allowRegex = null;
            }
        }

        if (confirmButton != null)
            confirmButton.onClick.AddListener(Confirm);

        // 기존 이름 프리필
        if (DataManager.instance != null && DataManager.instance.nowPlayer != null && inputField != null)
            inputField.text = DataManager.instance.nowPlayer.Name ?? "";

        if (messageText != null) messageText.text = "";
    }

    private void OnEnable()
    {
        EnsureEventSystemExists();
        StartCoroutine(CoAutoFocus());
    }

    private IEnumerator CoAutoFocus()
    {
        yield return null;
        if (!inputField) yield break;

        inputField.Select();
        inputField.ActivateInputField();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(inputField.gameObject);
    }

    private void Update()
    {
        if (inputField != null && inputField.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                Confirm();
        }
    }

    /// <summary>
    /// 이름 확정: 검증 → DataManager 이름 갱신 → (대사 즉시 리프레시) → SubSaveCommit → 지정 오브젝트 활성화
    /// </summary>
    public void Confirm()
    {
        if (inputField == null)
        {
            Debug.LogError("[PlayerNameInputUI] InputField 참조가 없습니다.");
            return;
        }

        string raw = inputField.text ?? "";
        string name = trimWhitespace ? raw.Trim() : raw;

        if (string.IsNullOrWhiteSpace(name))
        {
            SetMessage(emptyWarningText);
            ReFocusInput();
            return;
        }

        if (maxLength > 0 && name.Length > maxLength)
            name = name.Substring(0, maxLength);

        if (_allowRegex != null && !_allowRegex.IsMatch(name))
        {
            SetMessage(patternWarningText);
            ReFocusInput();
            return;
        }

        if (DataManager.instance == null || DataManager.instance.nowPlayer == null)
        {
            Debug.LogError("[PlayerNameInputUI] DataManager 또는 nowPlayer가 없습니다.");
            SetMessage("저장 시스템을 찾을 수 없습니다.");
            return;
        }

        // 1) 데이터매니저의 플레이어 이름 갱신 (HUD 반영 포함)
        DataManager.instance.SetPlayerName(name);

        // 1.5) 대사창이 켜져 있다면 현재 라인을 새 이름으로 즉시 재렌더
        var runner = FindFirstObjectByType<DialogueRunnerStringTables>(FindObjectsInactive.Include);
        if (runner != null) runner.RefreshPlayerNameNow();

        // 2) sub_save 커밋 (슬롯 미지정 시 안전 보정)
        EnsureSlotForSubSave();
        DataManager.instance.SubSaveCommit();

        // 3) 지정 오브젝트 활성화
        if (objectToActivateOnConfirm != null)
            objectToActivateOnConfirm.SetActive(true);

        SetMessage(savedMessageText);

        onConfirmed?.Invoke();

        if (disableSelfOnConfirm) gameObject.SetActive(false);
        if (destroySelfOnConfirm) Destroy(gameObject);
    }

    /// <summary>
    /// nowSlot이 미설정(-1)이면:
    /// - 가장 최근 저장 슬롯을 찾아 설정하거나
    /// - 아무 저장도 없으면 0번 슬롯을 사용하도록 초기화합니다.
    /// SubSaveCommit은 nowSlot 필요.
    /// </summary>
    private void EnsureSlotForSubSave()
    {
        var dm = DataManager.instance;
        if (dm == null) return;

        if (dm.nowSlot < 0)
        {
            int recent = dm.GetMostRecentSaveSlot(3);
            dm.nowSlot = (recent >= 0) ? recent : 0;
            Debug.Log($"[PlayerNameInputUI] nowSlot 미설정 → {dm.nowSlot}번 슬롯으로 보정하여 SubSave 사용");
        }
    }

    private void SetMessage(string msg)
    {
        if (messageText != null) messageText.text = msg ?? "";
    }

    private void ReFocusInput()
    {
        StartCoroutine(CoRefocusNextFrame());
    }

    private IEnumerator CoRefocusNextFrame()
    {
        yield return null;
        if (!inputField) yield break;

        inputField.Select();
        inputField.caretPosition = inputField.text.Length;
        inputField.ActivateInputField();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(inputField.gameObject);
    }

    private void EnsureEventSystemExists()
    {
        if (EventSystem.current != null) return;

        var es = new GameObject("EventSystem").AddComponent<EventSystem>();
        es.gameObject.AddComponent<StandaloneInputModule>();
        Debug.Log("[PlayerNameInputUI] EventSystem이 없어 자동 생성했습니다.");
    }

#if UNITY_EDITOR
    [ContextMenu("Test Confirm()")]
    private void _EditorTestConfirm() => Confirm();
#endif
}
