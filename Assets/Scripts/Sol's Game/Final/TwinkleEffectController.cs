using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TwinkleEffectController : MonoBehaviour
{
    [Header("반짝일 오브젝트들")]
    [Tooltip("반짝이는 효과를 줄 오브젝트들을 이곳에 드래그앤드롭 하세요.")]
    public GameObject[] targetObjects;

    [Header("설정")]
    [Tooltip("반짝이는 속도 (낮을수록 느림)")]
    public float twinkleSpeed = 2.0f;

    [Tooltip("최소 밝기 비율 (0이면 완전한 회색, 1이면 흰색)")]
    [Range(0f, 1f)]
    public float minBrightness = 0.0f; // 0으로 설정 시 125,125,125부터 시작

    [Tooltip("최대 밝기 비율 (1이면 완전한 흰색 255,255,255)")]
    [Range(0f, 1f)]
    public float maxBrightness = 1.0f;

    [Tooltip("무작위로 반짝일지 여부")]
    public bool isRandom = true;

    [Header("상호작용 설정")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private bool stopOnInteract = true;

    // --- 색상 정의 (요구사항) ---
    // 평소/상호작용 후 색상: (125, 125, 125)
    private static readonly Color BaseColor = new Color(125f / 255f, 125f / 255f, 125f / 255f, 1f);
    // 반짝일 때 최대 색상: (255, 255, 255)
    private static readonly Color TargetColor = Color.white;

    // 내부 변수
    private List<Material> targetMaterials = new List<Material>();
    private float[] randomOffsets;
    private Dictionary<GameObject, bool> interactedObjects = new Dictionary<GameObject, bool>();
    private Dictionary<GameObject, bool> objectCollisionStates = new Dictionary<GameObject, bool>();

    void Start()
    {
        InitializeMaterials();
    }

    void Update()
    {
        TwinkleObjects();

        // F키 입력 시 처리
        if (stopOnInteract && Input.GetKeyDown(interactKey))
        {
            HandleInteraction();
        }
    }

    void InitializeMaterials()
    {
        if (targetObjects == null || targetObjects.Length == 0) return;

        randomOffsets = new float[targetObjects.Length];

        for (int i = 0; i < targetObjects.Length; i++)
        {
            if (targetObjects[i] != null)
            {
                Renderer rend = targetObjects[i].GetComponent<Renderer>();
                if (rend != null)
                {
                    targetMaterials.Add(rend.material);

                    // 초기 색상을 125,125,125(회색)으로 설정
                    targetMaterials[i].color = BaseColor;

                    randomOffsets[i] = isRandom ? Random.Range(0f, 100f) : 0f;
                }
                else
                {
                    targetMaterials.Add(null);
                    randomOffsets[i] = 0f;
                }

                interactedObjects[targetObjects[i]] = false;
                objectCollisionStates[targetObjects[i]] = false;

                // 충돌체 및 헬퍼 스크립트 자동 추가
                SetupCollider(targetObjects[i]);
            }
        }
    }

    void SetupCollider(GameObject obj)
    {
        if (!stopOnInteract) return;

        // Collider 없으면 추가
        if (obj.GetComponent<Collider2D>() == null)
        {
            BoxCollider2D col = obj.AddComponent<BoxCollider2D>();
            col.isTrigger = false; // 충돌 감지를 위해 Trigger 끄기 (필요시 true로 변경)
        }

        // 충돌 감지용 스크립트 추가
        TwinkleObjectCollider helper = obj.GetComponent<TwinkleObjectCollider>();
        if (helper == null)
        {
            helper = obj.AddComponent<TwinkleObjectCollider>();
            helper.Initialize(this, obj, playerTag);
        }
    }

    void TwinkleObjects()
    {
        for (int i = 0; i < targetObjects.Length; i++)
        {
            GameObject obj = targetObjects[i];
            if (obj == null) continue;

            // 이미 상호작용했다면 반짝임 로직 건너뜀 (이미 색은 회색으로 고정됨)
            if (interactedObjects.ContainsKey(obj) && interactedObjects[obj])
                continue;

            if (i < targetMaterials.Count && targetMaterials[i] != null)
            {
                // 시간 흐름에 따른 사인파 계산
                float timeVal = (Time.time + randomOffsets[i]) * twinkleSpeed;
                float sinVal = Mathf.Sin(timeVal); // -1 ~ 1
                float normalizedSin = (sinVal + 1f) * 0.5f; // 0 ~ 1 범위로 변환

                // 설정한 밝기 범위 적용
                float lerpFactor = Mathf.Lerp(minBrightness, maxBrightness, normalizedSin);

                // BaseColor(125,125,125) ~ TargetColor(255,255,255) 사이 보간
                Color finalColor = Color.Lerp(BaseColor, TargetColor, lerpFactor);

                targetMaterials[i].color = finalColor;
            }
        }
    }

    void HandleInteraction()
    {
        GameObject targetToStop = null;

        // 충돌 중인 오브젝트 중 아직 상호작용 안 한 것 찾기
        foreach (var kvp in objectCollisionStates)
        {
            GameObject obj = kvp.Key;
            bool isColliding = kvp.Value;

            if (isColliding && (!interactedObjects.ContainsKey(obj) || !interactedObjects[obj]))
            {
                targetToStop = obj;
                break; // 하나만 처리
            }
        }

        if (targetToStop != null)
        {
            StopTwinklingForObject(targetToStop);
        }
    }

    public void UpdateCollisionState(GameObject obj, bool isColliding)
    {
        if (objectCollisionStates.ContainsKey(obj))
        {
            objectCollisionStates[obj] = isColliding;
        }
    }

    /// <summary>
    /// 특정 오브젝트의 반짝임을 멈추고 색상을 125,125,125로 고정
    /// </summary>
    public void StopTwinklingForObject(GameObject obj)
    {
        if (obj == null) return;

        // 이미 처리됨 체크
        if (interactedObjects.ContainsKey(obj) && interactedObjects[obj]) return;

        int index = System.Array.IndexOf(targetObjects, obj);
        if (index >= 0 && index < targetMaterials.Count)
        {
            if (targetMaterials[index] != null)
            {
                // [중요] 상호작용 시 색상을 정확히 125,125,125로 되돌림
                targetMaterials[index].color = BaseColor;
            }

            interactedObjects[obj] = true;
            Debug.Log($"[Twinkle] {obj.name} 상호작용 완료 -> 색상 회색(125,125,125) 고정");
        }
    }
}

// 충돌 감지 헬퍼 클래스
public class TwinkleObjectCollider : MonoBehaviour
{
    private TwinkleEffectController controller;
    private GameObject targetObject;
    private string playerTag;

    public void Initialize(TwinkleEffectController ctrl, GameObject obj, string tag)
    {
        controller = ctrl;
        targetObject = obj;
        playerTag = tag;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            controller.UpdateCollisionState(targetObject, true);
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider.CompareTag(playerTag))
        {
            controller.UpdateCollisionState(targetObject, false);
        }
    }
}