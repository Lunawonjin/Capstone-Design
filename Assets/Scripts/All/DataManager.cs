// DataManager.cs
using UnityEngine;
using System.IO;

// 직렬화 대상 데이터 컨테이너
[System.Serializable]
public class PlayerData
{
    public string Name;
    public int Level;
    public int Coin;
    public int Item;
}

// 세이브/로드/삭제를 담당하는 싱글톤
public class DataManager : MonoBehaviour
{
    public static DataManager instance;

    public PlayerData nowPlayer = new PlayerData();
    public string path;    // 예: ".../save"
    public int nowSlot;    // 현재 선택 슬롯

    private void Awake()
    {
        // 싱글톤 보장
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // "save0", "save1", "save2" 형식과 호환
        path = Application.persistentDataPath + "/save";
        Debug.Log("[DataManager] 저장 경로: " + path);
    }

    // 저장
    public void SaveData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] SaveData: nowSlot이 유효하지 않음: " + nowSlot);
            return;
        }

        string data = JsonUtility.ToJson(nowPlayer);
        string file = path + nowSlot.ToString();
        File.WriteAllText(file, data);
    }

    // 로드
    public void LoadData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] LoadData: nowSlot이 유효하지 않음: " + nowSlot);
            return;
        }

        string file = path + nowSlot.ToString();
        if (!File.Exists(file))
        {
            Debug.LogError("[DataManager] LoadData: 파일이 없음: " + file);
            return;
        }

        string data = File.ReadAllText(file);
        var loaded = JsonUtility.FromJson<PlayerData>(data);
        nowPlayer = loaded ?? new PlayerData();
    }

    // 상태 초기화
    public void DataClear()
    {
        nowSlot = -1;
        nowPlayer = new PlayerData();
    }

    // ▼▼▼ 여기부터: 실제 파일 삭제 로직 ▼▼▼

    // 지정 슬롯의 세이브 파일을 삭제한다. 삭제 성공 시 true, 삭제할 파일이 없으면 false.
    public bool DeleteData(int slot)
    {
        if (slot < 0)
        {
            Debug.LogError("[DataManager] DeleteData: 잘못된 슬롯 인덱스: " + slot);
            return false;
        }

        string file = path + slot.ToString();

        // 파일이 존재하면 삭제
        if (File.Exists(file))
        {
            try
            {
                File.Delete(file);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[DataManager] DeleteData 실패 (" + file + "): " + e.Message);
                return false;
            }
        }

        // 파일이 원래 없었던 경우
        return false;
    }
}
