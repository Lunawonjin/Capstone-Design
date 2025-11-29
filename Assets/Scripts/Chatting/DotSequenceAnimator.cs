using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class DotSequenceAnimator : MonoBehaviour
{
    [Header("점 이미지 (순서대로 3개 넣기)")]
    [SerializeField] private Image[] dotImages = new Image[3];

    [Header("스케일 설정")]
    [SerializeField] private float initialScale = 0.8f;   // 시작 스케일
    [SerializeField] private float targetScale = 1.0f;    // 최종 스케일 (마지막에 유지)
    [SerializeField] private float overShootScale = 1.2f; // 중간에 잠깐 커지는 스케일

    [Header("색상 설정")]
    [SerializeField] private Color baseColor = new Color32(200, 200, 200, 255); // #C8C8C8
    [SerializeField] private Color activeColor = Color.white;                   // 흰색

    [Header("애니메이션 타이밍")]
    [SerializeField] private float scaleUpDuration = 0.15f;   // initial -> overShoot
    [SerializeField] private float scaleDownDuration = 0.15f; // overShoot -> target
    [SerializeField] private float delayBetweenDots = 0.05f;  // 점 사이 딜레이

    [Header("자동 재생 옵션")]
    [SerializeField] private bool playOnEnable = true;        // 켜질 때 자동 재생 여부

    private Coroutine playRoutine;

    private void OnEnable()
    {
        InitDots();

        if (playOnEnable)
        {
            Play();
        }
    }

    /// <summary>
    /// 외부에서 다시 실행하고 싶을 때 호출
    /// </summary>
    public void Play()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
        }

        playRoutine = StartCoroutine(PlaySequence());
    }

    /// <summary>
    /// 모든 점을 초기 상태로 맞추기
    /// </summary>
    private void InitDots()
    {
        if (dotImages == null) return;

        foreach (var img in dotImages)
        {
            if (img == null) continue;

            RectTransform rt = img.rectTransform;

            if (rt != null)
            {
                rt.localScale = Vector3.one * initialScale;
            }

            img.color = baseColor;
        }
    }

    /// <summary>
    /// 1번 점 → 2번 점 → 3번 점 순서로 색/스케일 애니메이션
    /// </summary>
    private IEnumerator PlaySequence()
    {
        if (dotImages == null || dotImages.Length == 0)
        {
            yield break;
        }

        for (int i = 0; i < dotImages.Length; i++)
        {
            Image img = dotImages[i];
            if (img == null) continue;

            RectTransform rt = img.rectTransform;
            if (rt == null) continue;

            // 시작 상태 강제 세팅
            rt.localScale = Vector3.one * initialScale;
            img.color = baseColor;

            float elapsed = 0f;
            float totalDuration = scaleUpDuration + scaleDownDuration;

            while (elapsed < totalDuration)
            {
                elapsed += Time.deltaTime;
                float clampedElapsed = Mathf.Clamp(elapsed, 0f, totalDuration);

                // 전체 진행도 (0~1) - 색상 보간용
                float tTotal = clampedElapsed / totalDuration;

                float currentScale;

                if (clampedElapsed <= scaleUpDuration)
                {
                    // 1단계: initialScale -> overShootScale
                    float tUp = Mathf.Clamp01(clampedElapsed / scaleUpDuration);
                    currentScale = Mathf.Lerp(initialScale, overShootScale, tUp);
                }
                else
                {
                    // 2단계: overShootScale -> targetScale
                    float downElapsed = clampedElapsed - scaleUpDuration;
                    float tDown = Mathf.Clamp01(downElapsed / scaleDownDuration);
                    currentScale = Mathf.Lerp(overShootScale, targetScale, tDown);
                }

                rt.localScale = Vector3.one * currentScale;
                img.color = Color.Lerp(baseColor, activeColor, tTotal);

                yield return null;
            }

            // 마지막 값 보정
            rt.localScale = Vector3.one * targetScale;
            img.color = activeColor;

            if (delayBetweenDots > 0f)
            {
                yield return new WaitForSeconds(delayBetweenDots);
            }
        }

        playRoutine = null;
    }
}
