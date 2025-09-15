// Select.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using TMPro;

public class Select : MonoBehaviour
{
    public GameObject creat;                  // 새 플레이어 이름 입력 패널
    public TMP_Text[] slotText;               // 각 슬롯 버튼 아래 텍스트
    public TMP_Text newPlayerName;            // 새 플레이어 이름 입력(TMP_Text)

    private bool[] savefile = new bool[3];    // 각 슬롯 세이브 존재 여부

    void Start()
    {
        RefreshSlotsUI();
    }

    // 시작/갱신 시 슬롯 UI를 채운다.
    private void RefreshSlotsUI()
    {
        for (int i = 0; i < savefile.Length; i++)
        {
            bool exists = File.Exists(DataManager.instance.path + i.ToString());
            savefile[i] = exists;

            if (exists)
            {
                // 이름 표시를 위해 임시 로드
                DataManager.instance.nowSlot = i;
                SafeLoad();

                if (slotText != null && i < slotText.Length && slotText[i] != null)
                {
                    string name = DataManager.instance.nowPlayer != null ? DataManager.instance.nowPlayer.Name : null;
                    slotText[i].text = string.IsNullOrEmpty(name) ? "Player" : name;
                }
            }
            else
            {
                if (slotText != null && i < slotText.Length && slotText[i] != null)
                    slotText[i].text = "비어있음";
            }
        }

        // 상태 초기화(표시 목적 로드였음)
        DataManager.instance.DataClear();
    }

    // 슬롯 버튼 클릭
    public void Slot(int number)
    {
        DataManager.instance.nowSlot = number;

        if (number < 0 || number >= savefile.Length)
        {
            Debug.LogError("[Select] 잘못된 슬롯 인덱스: " + number);
            return;
        }

        if (savefile[number])
        {
            SafeLoad();
            GoGame();
        }
        else
        {
            Creat();
        }
    }

    // 새 플레이어 이름 입력 패널 열기
    public void Creat()
    {
        if (creat != null) creat.SetActive(true);
    }

    // 게임 씬으로 이동 (필요 시 신규 저장 생성)
    public void GoGame()
    {
        int s = DataManager.instance.nowSlot;

        if (s < 0 || s >= savefile.Length)
        {
            Debug.LogWarning("[Select] 유효한 슬롯이 선택되지 않음. 먼저 슬롯을 선택하세요.");
            if (creat != null) creat.SetActive(true);
            return;
        }

        bool exists = File.Exists(DataManager.instance.path + s.ToString());

        if (!exists)
        {
            string name = (newPlayerName != null) ? newPlayerName.text.Trim() : "";
            if (string.IsNullOrEmpty(name)) name = "Player";

            DataManager.instance.nowPlayer = new PlayerData
            {
                Name = name,
                Level = 1,
                Coin = 0,
                Item = 0
            };

            DataManager.instance.SaveData();
            savefile[s] = true;
        }
        else
        {
            SafeLoad();
        }

        SceneManager.LoadScene("Player's Room");
    }

    // 예외 안전 로드
    private void SafeLoad()
    {
        try
        {
            DataManager.instance.LoadData();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Select] 로드 실패: " + e.Message);
            DataManager.instance.DataClear();
        }
    }

    // ▼▼▼ 여기부터: 삭제 버튼 핸들러 ▼▼▼

    // 슬롯 삭제 버튼에 연결: 해당 슬롯의 세이브를 삭제하고 UI를 갱신한다.
    public void DeleteSlot(int number)
    {
        // 인덱스 가드
        if (number < 0 || number >= savefile.Length)
        {
            Debug.LogError("[Select] DeleteSlot 잘못된 인덱스: " + number);
            return;
        }

        // 파일 삭제 시도
        bool deleted = DataManager.instance.DeleteData(number);

        // 삭제 성공 또는 원래 없었던 경우에도 UI를 "비어있음"으로 만들고 상태 갱신
        savefile[number] = false;

        if (slotText != null && number < slotText.Length && slotText[number] != null)
            slotText[number].text = "비어있음";

        // 현재 선택된 슬롯이 삭제된 슬롯과 같다면 상태 초기화
        if (DataManager.instance.nowSlot == number)
        {
            DataManager.instance.DataClear();
        }

        // 삭제 확인 로그
        if (deleted)
            Debug.Log("[Select] 슬롯 " + number + " 저장 삭제 완료");
        else
            Debug.Log("[Select] 슬롯 " + number + " 저장 파일이 없거나 삭제할 것이 없음");
    }
}
