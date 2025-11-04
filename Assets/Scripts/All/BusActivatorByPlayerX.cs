// BusActivatorByPlayerX.cs
// 목적: 비활성화된 버스(GameObject)를 "플레이어 X < 임계값"일 때 자동 활성화
// 사용법:
//   1) 씬에 이 스크립트를 빈 GameObject에 붙임.
//   2) Target Bus에 "활성화하고 싶은 버스 루트"를 드래그(초기에는 비활성화 상태 권장).
//   3) Player Transform에 플레이어 Transform 참조(없으면 Tag/이름으로 자동 탐색 시도).
//   4) ThresholdX(기본 0)를 상황에 맞게 조정. oneShot을 켜면 한 번만 활성화.
//   5) optionalDeactivateWhenRight을 켜면 X가 임계 이상으로 돌아오면 비활성화(토글형).

using UnityEngine;

[DisallowMultipleComponent]
public class BusActivatorByPlayerX : MonoBehaviour
{
    [Header("대상 버스(활성화 대상)")]
    [SerializeField] private GameObject targetBus;

    [Header("플레이어 참조(없으면 자동 탐색 시도)")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string fallbackPlayerName = "Player";

    [Header("활성화 조건: Player.X < ThresholdX")]
    [SerializeField] private float thresholdX = 0f;

    [Header("동작 방식")]
    [SerializeField] private bool oneShot = true;                 // 한 번 활성화 후 더 이상 체크하지 않음
    [SerializeField] private bool optionalDeactivateWhenRight = false; // X >= threshold일 때 다시 비활성화(토글형)

    private bool _hasActivated;

    void Awake()
    {
        // 초기에는 대상 버스가 비활성화되어 있어야 의도대로 보임/숨김이 동작함
        // 필요 시 여기서 강제로 비활성화하려면 주석 해제:
        // if (targetBus && targetBus.activeSelf) targetBus.SetActive(false);

        if (!playerTransform)
        {
            // Tag 우선 탐색
            if (!string.IsNullOrEmpty(playerTag))
            {
                try
                {
                    var tagged = GameObject.FindGameObjectWithTag(playerTag);
                    if (tagged) playerTransform = tagged.transform;
                }
                catch { /* 태그 미존재 등 예외 무시 */ }
            }

            // 이름 보조 탐색
            if (!playerTransform && !string.IsNullOrWhiteSpace(fallbackPlayerName))
            {
                var byName = GameObject.Find(fallbackPlayerName);
                if (byName) playerTransform = byName.transform;
            }
        }
    }

    void Update()
    {
        if (!targetBus || !playerTransform) return;

        float px = playerTransform.position.x;

        // 조건: 플레이어 X가 임계값보다 작으면 활성화
        if (px < thresholdX)
        {
            if (!targetBus.activeSelf)
                targetBus.SetActive(true);

            _hasActivated = true;

            if (oneShot)
            {
                // 한 번 활성화하고 끝
                enabled = false;
            }
        }
        else if (optionalDeactivateWhenRight)
        {
            // 임계 이상으로 돌아왔을 때 비활성화(토글형 모드 전용)
            if (targetBus.activeSelf && (!_hasActivated || !oneShot))
                targetBus.SetActive(false);
        }
    }

    // ===== 인스펙터 실시간 조정을 위한 보조 메서드 =====
    public void SetThresholdX(float value) => thresholdX = value;
    public void SetOneShot(bool on) => oneShot = on;
    public void SetDeactivateWhenRight(bool on) => optionalDeactivateWhenRight = on;
    public void SetTargetBus(GameObject go) => targetBus = go;
    public void SetPlayer(Transform tr) => playerTransform = tr;
}
