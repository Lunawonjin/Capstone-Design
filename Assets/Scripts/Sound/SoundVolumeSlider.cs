// SoundVolumeSlider.cs
// 슬라이더 하나당 BGM 또는 SFX 한 종류를 제어
// - UnityEngine.UI.Slider 필요
// - OnValueChanged 이벤트 없이 자동 적용( Awake에서 Slider 값 → SoundManager 적용, 슬라이더 조작 시마다 갱신 )

using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Slider))]
public class SoundVolumeSlider : MonoBehaviour
{
    public enum VolumeType
    {
        BGM,
        SFX
    }

    [Header("이 슬라이더가 제어할 볼륨 종류")]
    public VolumeType volumeType = VolumeType.BGM;

    [Header("0~1로 매핑(슬라이더 Min/Max도 0~1로 맞추는게 편함)")]
    public bool updateOnStart = true; // 씬 열릴 때 슬라이더 현재값을 적용할지

    private Slider _slider;

    private void Awake()
    {
        _slider = GetComponent<Slider>();
        _slider.onValueChanged.AddListener(OnSliderValueChanged);

        if (updateOnStart && SoundManager.Instance != null)
        {
            ApplyVolume(_slider.value);
        }
    }

    private void OnSliderValueChanged(float v)
    {
        ApplyVolume(v);
    }

    private void ApplyVolume(float v)
    {
        if (SoundManager.Instance == null) return;

        switch (volumeType)
        {
            case VolumeType.BGM:
                SoundManager.Instance.SetBGMVolume(v);
                break;
            case VolumeType.SFX:
                SoundManager.Instance.SetSFXVolume(v);
                break;
        }
    }
}
