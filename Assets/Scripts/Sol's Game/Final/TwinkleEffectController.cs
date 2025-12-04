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

    [Tooltip("최소 밝기 (0~1)")]
    [Range(0f, 1f)]
    public float minBrightness = 0.2f;

    [Tooltip("최대 밝기 (0~1)")]
    [Range(0f, 1f)]
    public float maxBrightness = 1.0f;

    [Tooltip("무작위로 반짝일지 여부 (체크 해제 시 동시에 반짝임)")]
    public bool isRandom = true;

    // 내부 변수
    private List<Material> targetMaterials = new List<Material>();
    private float[] randomOffsets; // 각 오브젝트마다 다른 타이밍을 주기 위한 오프셋

    void Start()
    {
        InitializeMaterials();
    }

    void Update()
    {
        TwinkleObjects();
    }

    // 초기화: 각 오브젝트의 머테리얼을 가져오고 오프셋 설정
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
                    // 최적화를 위해 머테리얼 캐싱
                    targetMaterials.Add(rend.material);

                    // 무작위 타이밍을 위해 랜덤 오프셋 부여 (isRandom이 true일 때만 유효)
                    randomOffsets[i] = isRandom ? Random.Range(0f, 100f) : 0f;
                }
            }
        }
    }

    // 실제 반짝이는 로직
    void TwinkleObjects()
    {
        for (int i = 0; i < targetMaterials.Count; i++)
        {
            if (targetMaterials[i] != null)
            {
                // 시간과 오프셋을 이용해 사인파(Sine Wave) 생성 (-1 ~ 1)
                float timeVal = (Time.time + randomOffsets[i]) * twinkleSpeed;
                float sinVal = Mathf.Sin(timeVal); // -1 ~ 1 사이 값

                // 사인파를 0 ~ 1 사이 값으로 변환
                float normalizedVal = (sinVal + 1f) / 2f;

                // 최소~최대 밝기 사이로 값 보정 (Lerp)
                float brightness = Mathf.Lerp(minBrightness, maxBrightness, normalizedVal);

                // 현재 머테리얼의 색상을 가져와서 알파값(투명도) 또는 밝기 조절
                Color currentColor = targetMaterials[i].color;

                // 방법 1: 알파값(투명도) 조절 (Transparent 쉐이더 필요)
                // targetMaterials[i].color = new Color(currentColor.r, currentColor.g, currentColor.b, brightness);

                // 방법 2: 색상 자체의 밝기 조절 (일반 Opaque 쉐이더용 - 검은색으로 어두워짐)
                targetMaterials[i].color = new Color(brightness, brightness, brightness, 1f);

                // 방법 3: Emission(발광) 조절 (Emission이 켜져 있어야 함)
                // targetMaterials[i].SetColor("_EmissionColor", currentColor * brightness);
            }
        }
    }
}