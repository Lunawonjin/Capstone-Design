using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class ChattingChoiceView : MonoBehaviour
{
    [Header("선택지 버튼")]
    [SerializeField] private Button s1Button;
    [SerializeField] private Button s2Button;

    [Header("선택지 텍스트")]
    [SerializeField] private TextMeshProUGUI s1Text;
    [SerializeField] private TextMeshProUGUI s2Text;

    // ChattingManger에서 기다리는 값들
    public bool IsSelected { get; private set; }
    public int SelectedIndex { get; private set; } = -1; // 0 = S1, 1 = S2

    private void Awake()
    {
        if (s1Button != null)
        {
            s1Button.onClick.RemoveListener(OnClickS1);
            s1Button.onClick.AddListener(OnClickS1);
        }

        if (s2Button != null)
        {
            s2Button.onClick.RemoveListener(OnClickS2);
            s2Button.onClick.AddListener(OnClickS2);
        }
    }

    /// <summary>
    /// ChattingManger에서 S1/S2 문자열을 넘겨줄 때 호출
    /// </summary>
    public void Setup(string s1, string s2)
    {
        // S1
        if (string.IsNullOrEmpty(s1))
        {
            if (s1Button != null) s1Button.gameObject.SetActive(false);
        }
        else
        {
            if (s1Button != null) s1Button.gameObject.SetActive(true);
            if (s1Text != null) s1Text.text = s1;
        }

        // S2
        if (string.IsNullOrEmpty(s2))
        {
            if (s2Button != null) s2Button.gameObject.SetActive(false);
        }
        else
        {
            if (s2Button != null) s2Button.gameObject.SetActive(true);
            if (s2Text != null) s2Text.text = s2;
        }

        IsSelected = false;
        SelectedIndex = -1;

        Debug.Log($"[ChattingChoiceView] Setup 완료: S1={(string.IsNullOrEmpty(s1) ? "비활성" : s1)}, S2={(string.IsNullOrEmpty(s2) ? "비활성" : s2)}", this);
    }

    private void OnClickS1()
    {
        OnClickChoice(0);
    }

    private void OnClickS2()
    {
        OnClickChoice(1);
    }

    private void OnClickChoice(int index)
    {
        if (IsSelected)
            return;

        IsSelected = true;
        SelectedIndex = index;

        // 더 이상 여러 번 눌리지 않도록 막기
        if (s1Button != null) s1Button.interactable = false;
        if (s2Button != null) s2Button.interactable = false;

        Debug.Log($"[ChattingChoiceView] 선택 완료: index={index}", this);
    }
}
