using System.Collections;
using UnityEngine;

public class BossSpawnManager : MonoBehaviour
{
    public static BossSpawnManager Instance;

    [Header("프리팹")]
    [SerializeField] private GameObject bossPrefab;
    [SerializeField] private GameObject shadowPrefab;

    [Header("스폰 설정")]
    [Tooltip("그림자가 생길 고정 Y 좌표")]
    [SerializeField] private float shadowFixedY = -2.18f;
    [Tooltip("보스가 생성될 높이 (플레이어 Y + ?)")]
    [SerializeField] private float spawnHeightOffset = 10f;
    [Tooltip("그림자 깜빡임 횟수")]
    [SerializeField] private int blinkCount = 3;
    [Tooltip("깜빡임 속도")]
    [SerializeField] private float blinkInterval = 0.2f;

    private Transform playerTransform;
    private bool isSpawning = false;

    void Awake()
    {
        Instance = this;
    }

    public void TriggerBossSpawn()
    {
        if (isSpawning)
        {
            Debug.LogWarning("⚠️ BossSpawnManager: 이미 스폰 시퀀스가 진행 중입니다.");
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            StartCoroutine(SpawnSequence());
        }
        else
        {
            Debug.LogError("❌ BossSpawnManager: Player를 찾을 수 없습니다.");
        }
    }

    private IEnumerator SpawnSequence()
    {
        isSpawning = true; // 스폰 시작! 중복 방지 ON

        // 1. 위치 계산
        float targetX = playerTransform.position.x;
        Vector3 shadowPos = new Vector3(targetX, shadowFixedY, 0);

        // 2. 그림자 생성
        GameObject shadow = Instantiate(shadowPrefab, shadowPos, Quaternion.identity);

        // 그림자 레이어 순서 확실하게 앞으로 (가려짐 방지)
        SpriteRenderer sr = shadow.GetComponent<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = 20;

        Color activeColor = Color.red;
        Color inactiveColor = new Color(1, 0, 0, 0);

        // 3. 깜빡임 연출
        for (int i = 0; i < blinkCount; i++)
        {
            if (sr != null) sr.color = activeColor;
            yield return new WaitForSeconds(blinkInterval);

            if (sr != null) sr.color = inactiveColor;
            yield return new WaitForSeconds(blinkInterval);
        }

        // 4. 그림자 삭제
        if (shadow != null) Destroy(shadow);

        // 5. 보스 생성 (연출 종료)
        Vector3 bossPos = new Vector3(targetX, playerTransform.position.y + spawnHeightOffset, 0);
        Instantiate(bossPrefab, bossPos, Quaternion.identity);

        Debug.Log("🚀 보스 낙하 시작!");

        // [핵심 수정] 스폰 시퀀스 종료! 다시 소환 가능하도록 상태 초기화
        isSpawning = false;
    }
}