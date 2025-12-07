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

    [Header("설정")]
    public VolumeType volumeType = VolumeType.BGM;

    private Slider _slider;

    private void Start() // Awake 대신 Start 권장 (SoundManager Instance 확실히 로드 후 실행)
    {
        _slider = GetComponent<Slider>();

        if (SoundManager.Instance == null) return;

        // 1. 현재 매니저의 실제 볼륨 값을 가져와서 슬라이더 위치를 갱신 (동기화)
        switch (volumeType)
        {
            case VolumeType.BGM:
                _slider.value = SoundManager.Instance.bgmVolume;
                break;
            case VolumeType.SFX:
                _slider.value = SoundManager.Instance.sfxVolume;
                break;
        }

        // 2. 값 변경 이벤트 연결 (초기화 이후에 연결해야 불필요한 호출 방지)
        _slider.onValueChanged.AddListener(OnSliderValueChanged);
    }

    private void OnSliderValueChanged(float v)
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