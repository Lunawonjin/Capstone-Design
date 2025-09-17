using UnityEngine;
using System.IO;
using System;

/// <summary>
/// 플레이어 데이터 컨테이너(직렬화 대상)
/// </summary>
[System.Serializable]
public class PlayerData
{
    public string Name;  // 플레이어 이름
    public int Level;    // 레벨
    public int Coin;     // 보유 재화
    public int Item;     // 아이템 코드(예: 시작 아이템)
}

/// <summary>
/// 세이브/로드/삭제 및 현재 슬롯/플레이어 상태를 관리하는 싱글톤
/// - 파일명은 path + 슬롯번호 (예: ".../save0")
/// </summary>
public class DataManager : MonoBehaviour
{
    public static DataManager instance;   // 싱글톤 인스턴스

    public PlayerData nowPlayer = new PlayerData(); // 현재 플레이어 데이터
    public string path;                   // 파일 경로 접두부 (예: ".../save")
    public int nowSlot;                   // 현재 슬롯 인덱스

    private void Awake()
    {
        // 표준 싱글톤 패턴
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

        // 기존 파일 형식과 호환: "save0", "save1", "save2"
        path = Application.persistentDataPath + "/save";
        Debug.Log("[DataManager] 저장 경로: " + path);
    }

    /// <summary>
    /// 현재 nowPlayer를 현재 nowSlot 파일로 저장한다.
    /// nowSlot이 유효하지 않으면 저장하지 않는다.
    /// </summary>
    public void SaveData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] SaveData 호출 시 nowSlot이 유효하지 않음: " + nowSlot);
            return;
        }

        string data = JsonUtility.ToJson(nowPlayer);
        string file = path + nowSlot.ToString();
        File.WriteAllText(file, data);
    }

    /// <summary>
    /// 현재 nowSlot 파일에서 nowPlayer를 로드한다.
    /// 파일이 없거나 nowSlot이 유효하지 않으면 로드하지 않는다.
    /// </summary>
    public void LoadData()
    {
        if (nowSlot < 0)
        {
            Debug.LogError("[DataManager] LoadData 호출 시 nowSlot이 유효하지 않음: " + nowSlot);
            return;
        }

        string file = path + nowSlot.ToString();
        if (!File.Exists(file))
        {
            Debug.LogError("[DataManager] 저장 파일이 없음: " + file);
            return;
        }

        string data = File.ReadAllText(file);
        var loaded = JsonUtility.FromJson<PlayerData>(data);
        nowPlayer = loaded ?? new PlayerData();
    }

    /// <summary>
    /// 현재 선택 슬롯과 플레이어 데이터를 초기화한다.
    /// - nowSlot = -1
    /// - nowPlayer = 새 인스턴스
    /// </summary>
    public void DataClear()
    {
        nowSlot = -1;
        nowPlayer = new PlayerData();
    }

    /// <summary>
    /// 지정 슬롯의 세이브 파일 존재 여부
    /// </summary>
    public bool ExistsSlot(int slot)
    {
        if (slot < 0) return false;
        string file = path + slot.ToString();
        return File.Exists(file);
    }

    /// <summary>
    /// 0..(slotCount-1) 중 하나라도 세이브 파일이 있으면 true
    /// </summary>
    public bool HasAnySave(int slotCount = 3)
    {
        for (int i = 0; i < slotCount; i++)
        {
            if (ExistsSlot(i)) return true;
        }
        return false;
    }

    /// <summary>
    /// 가장 최근에 저장된 슬롯 인덱스를 반환. 없으면 -1
    /// 파일의 LastWriteTime을 기준으로 판단.
    /// </summary>
    public int GetMostRecentSaveSlot(int slotCount = 3)
    {
        int bestSlot = -1;
        DateTime bestTime = DateTime.MinValue;

        for (int i = 0; i < slotCount; i++)
        {
            string file = path + i.ToString();
            if (!File.Exists(file)) continue;

            DateTime t = File.GetLastWriteTime(file);
            if (t > bestTime)
            {
                bestTime = t;
                bestSlot = i;
            }
        }

        return bestSlot;
    }

    /// <summary>
    /// 가장 최근 세이브를 찾아 nowSlot을 설정하고 로드까지 시도한다.
    /// 성공하면 true, 실패하면 false
    /// </summary>
    public bool TryLoadMostRecentSave(int slotCount = 3)
    {
        int slot = GetMostRecentSaveSlot(slotCount);
        if (slot < 0) return false;

        nowSlot = slot;
        try
        {
            LoadData();
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[DataManager] TryLoadMostRecentSave 실패: " + e.Message);
            DataClear();
            return false;
        }
    }

    /// <summary>
    /// 지정 슬롯의 세이브 파일을 삭제한다. 삭제 성공 시 true, 없으면 false.
    /// </summary>
    public bool DeleteData(int slot)
    {
        if (slot < 0)
        {
            Debug.LogError("[DataManager] DeleteData: 잘못된 슬롯 인덱스: " + slot);
            return false;
        }

        string file = path + slot.ToString();

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

        return false;
    }
}
