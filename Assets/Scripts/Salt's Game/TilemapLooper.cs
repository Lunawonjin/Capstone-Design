using UnityEngine;

public class TilemapLooper : MonoBehaviour
{
    public float speed = 1f;   // 타일맵 내려가는 속도
    public float resetY = -4f; // 리셋될 Y좌표
    public float startY = 4f;  // 다시 올려줄 Y좌표

    private Transform tr;

    void Start()
    {
        tr = transform;
    }

    void Update()
    {
        // 타일맵을 아래로 이동
        tr.Translate(Vector3.down * speed * Time.deltaTime, Space.World);

        // y좌표가 resetY보다 작거나 같아지면 다시 startY로 올림
        if (tr.position.y <= resetY)
        {
            tr.position = new Vector3(tr.position.x, startY, tr.position.z);
        }
    }
}
