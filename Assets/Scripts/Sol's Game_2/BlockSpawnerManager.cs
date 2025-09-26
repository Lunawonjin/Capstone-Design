using System.Collections;
using UnityEngine;

/// <summary>
/// 블럭 자동 순차 스폰
/// - 좌/우 화면 밖에서 생성 → X=0으로 이동
/// - 생성 시 태그 "NoBlock" → X==0 도착 시 "Block"으로 변경
/// - 스프라이트 무작위 적용(null 슬롯은 회색 랜덤 컬러)
/// - 카메라 펀치 모션: 플레이어가 착지할 때 PlayerJump가 TriggerCameraStep() 호출
/// - 이동 속도: 인스펙터의 moveSpeeds 배열에서 랜덤 선택(비어있으면 defaultMoveSpeed 사용)
/// </summary>
public class BlockSpawnManager : MonoBehaviour
{
    [Header("프리팹 & 스프라이트")]
    public GameObject blockPrefab;
    public Sprite[] blockSprites;

    [Header("스폰 개수/타이밍")]
    public int totalBlocks = 15;
    public float startDelay = 0f;
    public float postArrivalDelay = 0.12f;

    [Header("Y 규칙(계단)")]
    public float firstBlockY = -2.5f;
    public float startYAfterFirst = -0.5f;
    public float stepY = 1.5f;

    [Header("이동(속도 랜덤)")]
    [Tooltip("후보 속도들을 넣어두면, 블록 생성 때마다 하나가 랜덤 선택됩니다.")]
    public float[] moveSpeeds;
    [Tooltip("moveSpeeds가 비어있을 때 사용할 기본 속도")]
    public float defaultMoveSpeed = 3f;
    [Tooltip("목표점 근처에서 도착 판정(제곱거리)")]
    public float arriveThreshold = 0.02f;
    [Tooltip("화면 밖에서 얼마나 더 바깥에서 시작할지 여유")]
    public float offscreenMargin = 1.0f;

    [Header("카메라(옵션)")]
    public Transform cameraTarget;
    public bool cameraPunchyEnabled = true;
    public float cameraFirstStep = 0.5f, cameraNextStep = 1.5f;
    public float punchOvershoot = 0.35f, punchDownKick = 0.18f;
    public int punchBounces = 2; [Range(0.1f, 0.95f)] public float punchDamping = 0.55f;
    public float punchUpDuration = 0.10f, punchBounceDuration = 0.10f;
    public float shakeAmplitude = 0.12f, shakeDuration = 0.10f; [Range(0.1f, 0.99f)] public float shakeDamping = 0.75f;

    // 내부
    System.Random rng = new System.Random();
    int spawned = 0;
    int remaining = 0;
    int cameraStepCount = 0;   // 착지에 의해 실행된 스텝 횟수
    Coroutine camCo;

    void Start()
    {
        remaining = Mathf.Max(0, totalBlocks);
        StartCoroutine(CoSpawnSequential());
    }

    IEnumerator CoSpawnSequential()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        while (remaining > 0)
        {
            float y = (spawned == 0) ? firstBlockY : startYAfterFirst + (spawned - 1) * stepY;
            bool left = rng.NextDouble() < 0.5;
            float spawnX = GetOffscreenX(left);

            Vector3 from = new Vector3(spawnX, y, 0f);
            Vector3 to = new Vector3(0f, y, 0f);

            var go = Instantiate(blockPrefab, from, Quaternion.identity);
            ApplyRandomSpriteOrGray(go);
            go.tag = "NoBlock"; // 이동 중

            // ★ 블록별 속도 선택
            float speed = PickMoveSpeed();

            spawned++; remaining--;

            // 이동 및 도착 처리(블록별 속도를 인자로 전달)
            yield return StartCoroutine(MoveBlock(go.transform, to, speed));

            // 도착
            go.transform.position = to;
            go.tag = "Block";

            // 카메라 스텝은 여기서 호출하지 않습니다. (플레이어가 착지할 때 TriggerCameraStep 호출)

            if (postArrivalDelay > 0f) yield return new WaitForSeconds(postArrivalDelay);
        }
    }

    // 블록별 속도 선택 로직
    float PickMoveSpeed()
    {
        // moveSpeeds가 비었거나 모두 0 이하라면 기본 속도 사용
        if (moveSpeeds == null || moveSpeeds.Length == 0)
            return Mathf.Max(0.01f, defaultMoveSpeed);

        // 유효(>0)한 후보만 모으고 랜덤 선택
        int tries = 0;
        while (tries < 8)
        {
            float pick = moveSpeeds[Random.Range(0, moveSpeeds.Length)];
            if (pick > 0f) return pick;
            tries++;
        }
        // 전부 0 이거나 음수면 기본 속도
        return Mathf.Max(0.01f, defaultMoveSpeed);
    }

    IEnumerator MoveBlock(Transform tr, Vector3 target, float speed)
    {
        Vector3 p = tr.position; p.z = 0f; tr.position = p; target.z = 0f;
        float thrSqr = arriveThreshold * arriveThreshold;

        while ((tr.position - target).sqrMagnitude > thrSqr)
        {
            tr.position = Vector3.MoveTowards(tr.position, target, speed * Time.deltaTime);
            if (Mathf.Abs(tr.position.z) > 0.0001f)
            { var fix = tr.position; fix.z = 0f; tr.position = fix; }
            yield return null;
        }
    }

    void ApplyRandomSpriteOrGray(GameObject go)
    {
        var sr = go.GetComponentInChildren<SpriteRenderer>();
        if (!sr) return;

        Sprite pick = null;
        if (blockSprites != null && blockSprites.Length > 0)
            pick = blockSprites[rng.Next(0, blockSprites.Length)];

        if (pick != null) { sr.sprite = pick; sr.color = Color.white; }
        else
        {
            byte g = (byte)rng.Next(70, 200);
            sr.color = new Color32(g, g, g, 255);
        }
    }

    float GetOffscreenX(bool leftSide)
    {
        var cam = Camera.main;
        float half = cam.orthographicSize * cam.aspect;
        float edge = cam.transform.position.x + (leftSide ? -half : +half);
        return edge + (leftSide ? -offscreenMargin : +offscreenMargin);
    }

    // ───── 외부(플레이어 착지)에서 호출할 카메라 스텝 ─────
    public void TriggerCameraStep()
    {
        if (!cameraTarget) return;

        float step = (cameraStepCount == 0) ? cameraFirstStep : cameraNextStep;
        cameraStepCount++;

        StepCameraPunchy(step);
    }

    // ───── 카메라 펀치 ─────
    void StepCameraPunchy(float step)
    {
        if (!cameraTarget || Mathf.Approximately(step, 0f)) return;
        if (camCo != null) StopCoroutine(camCo);
        camCo = StartCoroutine(CoPunchy(step));
    }

    IEnumerator CoPunchy(float step)
    {
        Vector3 basePos = cameraTarget.position;
        float targetY = basePos.y + step;

        float overshootY = targetY + punchOvershoot;
        yield return TweenY(basePos.y, overshootY, punchUpDuration, EaseOutQuad);

        if (shakeAmplitude > 0f && shakeDuration > 0f)
            yield return Shake(shakeDuration, shakeAmplitude, shakeDamping);

        float downY = targetY - punchDownKick;
        yield return TweenY(cameraTarget.position.y, downY, punchBounceDuration, EaseInOutQuad);

        float currentY = downY;
        float amp = (overshootY - targetY) * punchDamping;
        for (int i = 0; i < Mathf.Max(0, punchBounces); i++)
        {
            float upY = targetY + amp;
            yield return TweenY(currentY, upY, punchBounceDuration, EaseOutQuad);
            currentY = upY;

            float lowY = targetY - amp * 0.6f;
            yield return TweenY(currentY, lowY, punchBounceDuration, EaseInOutQuad);
            currentY = lowY;

            amp *= punchDamping;
            if (amp < 0.01f) break;
        }

        yield return TweenY(cameraTarget.position.y, targetY, punchBounceDuration, EaseOutQuad);
        cameraTarget.position = new Vector3(cameraTarget.position.x, targetY, cameraTarget.position.z);
        camCo = null;
    }

    IEnumerator TweenY(float fromY, float toY, float dur, System.Func<float, float> ease)
    {
        if (dur <= 0f) { cameraTarget.position = new Vector3(cameraTarget.position.x, toY, cameraTarget.position.z); yield break; }
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float y = Mathf.LerpUnclamped(fromY, toY, ease(Mathf.Clamp01(t / dur)));
            cameraTarget.position = new Vector3(cameraTarget.position.x, y, cameraTarget.position.z);
            yield return null;
        }
    }

    IEnumerator Shake(float dur, float amp, float damp)
    {
        Vector3 pivot = cameraTarget.position;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float fall = Mathf.Pow(damp, (t / dur) * 10f);
            float dx = (Random.value * 2f - 1f) * amp * fall * 0.5f;
            float dy = (Random.value * 2f - 1f) * amp * fall;
            cameraTarget.position = new Vector3(pivot.x + dx, pivot.y + dy, pivot.z);
            yield return null;
        }
        cameraTarget.position = pivot;
    }

    float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
    float EaseInOutQuad(float x) => (x < 0.5f) ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;

#if UNITY_EDITOR
    void OnValidate()
    {
        defaultMoveSpeed = Mathf.Max(0.01f, defaultMoveSpeed);
        if (moveSpeeds != null)
        {
            for (int i = 0; i < moveSpeeds.Length; i++)
                moveSpeeds[i] = Mathf.Max(0f, moveSpeeds[i]); // 음수 방지
        }
        arriveThreshold = Mathf.Max(0.0001f, arriveThreshold);
        offscreenMargin = Mathf.Max(0f, offscreenMargin);
    }
#endif
}
