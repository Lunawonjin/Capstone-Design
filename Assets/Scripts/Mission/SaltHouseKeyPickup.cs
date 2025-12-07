using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class SaltKeyPickup : MonoBehaviour
{
    [Header("플레이어 인식")]
    [SerializeField] private string playerTag = "Player";

    [Header("조작 키")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Header("UI (선택 사항)")]
    [SerializeField] private GameObject pressKeyUI;  // "F 키" 안내용 UI가 있으면 연결

    private bool _playerInRange = false;

    private void Reset()
    {
        // 기본 설정: 콜라이더를 트리거로 사용
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        _playerInRange = true;

        if (pressKeyUI != null)
            pressKeyUI.SetActive(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        _playerInRange = false;

        if (pressKeyUI != null)
            pressKeyUI.SetActive(false);
    }

    private void Update()
    {
        if (!_playerInRange) return;

        if (!Input.GetKeyDown(interactKey)) return;

        // 플레이어 데이터에 키 보유 플래그 설정
        if (DataManager.instance != null && DataManager.instance.nowPlayer != null)
        {
            // PlayerData 안에 public bool Salt_House_Key; 가 있다고 가정
            DataManager.instance.nowPlayer.Salt_House_Key = true;
            Debug.Log("[SaltKeyPickup] Salt_House_Key = true 로 설정됨");
        }
        else
        {
            Debug.LogWarning("[SaltKeyPickup] DataManager 또는 nowPlayer를 찾지 못했습니다.");
        }

        // ★ [추가] 열쇠 습득 시 미션 텍스트 갱신
        if (MissionPanel.Instance != null)
        {
            MissionPanel.Instance.ShowText("소금이 집으로 가자");
        }

        // UI 끄고 키 오브젝트 비활성화
        if (pressKeyUI != null)
            pressKeyUI.SetActive(false);

        gameObject.SetActive(false);
    }
}