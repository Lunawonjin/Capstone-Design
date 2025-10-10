using UnityEngine;
using TMPro;

/// <summary>
/// 간단한 데모용: 패널에 라벨을 찍어 변화가 보이도록 함
/// </summary>
public class UIPanelSampleView : MonoBehaviour
{
    [Tooltip("패널 안의 TextMeshProUGUI 레퍼런스")]
    public TextMeshProUGUI label;

    public void SetLabel(string text)
    {
        if (label != null) label.text = text;
    }
}
