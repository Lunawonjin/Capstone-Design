using System.Collections;
using UnityEngine;

public class BlockSpawnManager : MonoBehaviour
{
    // 스폰 X 전략
    public enum SpawnXMode
    {
        OffscreenBiased,   // 화면 바깥 + 중앙 편향
        ManualAbsolute,    // 절대 X
        ManualRange        // 범위 랜덤 X
    }

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
    [Tooltip("OffscreenBiased 모드일 때 화면 바깥 여유")]
    public float offscreenMargin = 1.0f;

    // ── 스폰 X 설정 ─────────────────────────────────────────────
    [Header("스폰 X 설정")]
    [Tooltip("스폰 X 계산 방식을 선택합니다.")]
    public SpawnXMode spawnXMode = SpawnXMode.OffscreenBiased;

    [Tooltip("ManualAbsolute 모드에서 사용할 절대 X 좌표(월드 좌표)")]
    public float manualSpawnX = -5f;

    [Tooltip("ManualRange 모드에서 사용할 [최소, 최대] X 범위(월드 좌표)")]
    public Vector2 manualSpawnXRange = new Vector2(-6f, -2f);

    [Header("OffscreenBiased 모드 옵션")]
    [Tooltip("0=화면 엣지 바깥, 1=목표 X=0 바로 위. 클수록 중앙 쪽에서 시작")]
    [Range(0f, 1f)] public float spawnCenterBias = 0.35f;

    [Tooltip("true면 편향을 주더라도 최소한 화면 밖에서 시작하도록 강제")]
    public bool keepOffscreenAtStart = true;

    [Tooltip("화면 밖 유지 시 필요한 최소 여유(월드 단위)")]
    public float minOffscreenMargin = 0.2f;

    // ── 카메라(옵션) ────────────────────────────────────────────
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
    int cameraStepCount = 0;
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

            // 스폰 X 결정 (좌/우 랜덤은 OffscreenBiased에서만 의미, 그러나 실제 좌우는 spawnX 부호로 확정)
            float spawnX = ComputeSpawnX(rng.NextDouble() < 0.5);
            bool fromLeft = spawnX < 0f;
            float activationX = fromLeft ? -0.5f : 0.5f; // ← 태그 전환 지점

            Vector3 from = new Vector3(spawnX, y, 0f);
            Vector3 to = new Vector3(0f, y, 0f);

            var go = Instantiate(blockPrefab, from, Quaternion.identity);
            ApplyRandomSpriteOrGray(go);
            go.tag = "NoBlock"; // 이동 중

            float speed = PickMoveSpeed();

            spawned++; remaining--;

            // 임계 X 통과 시점에 태그 전환
            yield return StartCoroutine(MoveBlock(go.transform, to, speed, go, activationX, fromLeft));

            // 안전 보증(이미 Block일 것이지만 한 번 더 보정 가능)
            go.transform.position = to;
            if (go.tag != "Block") go.tag = "Block";

            if (postArrivalDelay > 0f) yield return new WaitForSeconds(postArrivalDelay);
        }
    }

    // ── 스폰 X 계산 ─────────────────────────────────────────────
    float ComputeSpawnX(bool leftSideHint)
    {
        switch (spawnXMode)
        {
            case SpawnXMode.ManualAbsolute:
                return manualSpawnX;

            case SpawnXMode.ManualRange:
                {
                    float min = Mathf.Min(manualSpawnXRange.x, manualSpawnXRange.y);
                    float max = Mathf.Max(manualSpawnXRange.x, manualSpawnXRange.y);
                    return Random.Range(min, max);
                }

            case SpawnXMode.OffscreenBiased:
            default:
                return GetOffscreenBiasedX(leftSideHint);
        }
    }

    // 기존 + 중앙 편향 + 오프스크린 강제
    float GetOffscreenBiasedX(bool leftSide)
    {
        var cam = Camera.main;
        float half = cam.orthographicSize * cam.aspect; // 화면 반폭(월드)
        float camX = cam.transform.position.x;

        // 1) 엣지 바깥 기준점
        float edge = camX + (leftSide ? -half : +half);
        float spawnEdgeX = edge + (leftSide ? -offscreenMargin : +offscreenMargin);

        // 2) 목표점은 X=0
        float targetX = 0f;

        // 3) 편향 적용: 엣지 바깥에서 목표쪽으로 보간
        float biasedX = Mathf.Lerp(spawnEdgeX, targetX, Mathf.Clamp01(spawnCenterBias));

        // 4) 필요 시 "그래도 화면 밖" 유지
        if (keepOffscreenAtStart)
        {
            float minOutside = edge + (leftSide ? -minOffscreenMargin : +minOffscreenMargin);
            if (leftSide) biasedX = Mathf.Min(biasedX, minOutside);
            else biasedX = Mathf.Max(biasedX, minOutside);
        }

        return biasedX;
    }

    // ── 이동: 임계 X 통과 시 태그 전환 ─────────────────────────
    IEnumerator MoveBlock(Transform tr, Vector3 target, float speed, GameObject go, float activationX, bool fromLeft)
    {
        Vector3 p = tr.position; p.z = 0f; tr.position = p; target.z = 0f;
        float thrSqr = arriveThreshold * arriveThreshold;

        // 스폰 순간부터 이미 임계 X 안쪽일 수 있어 선제 체크
        if (go.tag != "Block")
        {
            float x0 = tr.position.x;
            if ((fromLeft && x0 >= activationX) ||
                (!fromLeft && x0 <= activationX))
            {
                go.tag = "Block";
            }
        }

        while ((tr.position - target).sqrMagnitude > thrSqr)
        {
            tr.position = Vector3.MoveTowards(tr.position, target, speed * Time.deltaTime);

            // 임계 X 통과 체크(통과 순간 1회만 전환)
            if (go.tag != "Block")
            {
                float x = tr.position.x;
                if ((fromLeft && x >= activationX) ||
                    (!fromLeft && x <= activationX))
                {
                    go.tag = "Block";
                }
            }

            // Z 클램프
            if (Mathf.Abs(tr.position.z) > 0.0001f)
            { var fix = tr.position; fix.z = 0f; tr.position = fix; }

            yield return null;
        }
    }

    // ── 속도/연출/유틸 ──────────────────────────────────────────
    float PickMoveSpeed()
    {
        if (moveSpeeds == null || moveSpeeds.Length == 0)
            return Mathf.Max(0.01f, defaultMoveSpeed);

        int tries = 0;
        while (tries < 8)
        {
            float pick = moveSpeeds[Random.Range(0, moveSpeeds.Length)];
            if (pick > 0f) return pick;
            tries++;
        }
        return Mathf.Max(0.01f, defaultMoveSpeed);
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

    // ───── 외부(플레이어 착지)에서 호출할 카메라 스텝 ─────
    public void TriggerCameraStep()
    {
        if (!cameraTarget || !cameraPunchyEnabled) return;

        float step = (cameraStepCount == 0) ? cameraFirstStep : cameraNextStep;
        cameraStepCount++;

        StepCameraPunchy(step);
    }

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
                moveSpeeds[i] = Mathf.Max(0f, moveSpeeds[i]);
        }
        arriveThreshold = Mathf.Max(0.0001f, arriveThreshold);
        offscreenMargin = Mathf.Max(0f, offscreenMargin);

        spawnCenterBias = Mathf.Clamp01(spawnCenterBias);
        minOffscreenMargin = Mathf.Max(0f, minOffscreenMargin);

        if (spawnXMode == SpawnXMode.ManualRange && manualSpawnXRange.x > manualSpawnXRange.y)
            manualSpawnXRange = new Vector2(manualSpawnXRange.y, manualSpawnXRange.x);
    }
#endif
}
