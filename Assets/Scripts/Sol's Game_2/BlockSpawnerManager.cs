using System.Collections;
using UnityEngine;

public class BlockSpawnManager : MonoBehaviour
{
    // Spawn X strategy
    public enum SpawnXMode
    {
        OffscreenBiased,
        ManualAbsolute,
        ManualRange
    }

    // Which side spawns from
    public enum SpawnSideMode
    {
        RandomEach,
        ForceLeft,
        ForceRight
    }

    [Header("Prefab & Sprites")]
    public GameObject blockPrefab;
    public Sprite[] blockSprites;

    [Header("Spawn Count/Timing")]
    public int totalBlocks = 15;
    public float startDelay = 0f;
    public float postArrivalDelay = 0.12f;

    [Header("Y Rules (stair)")]
    public float firstBlockY = -2.5f;
    public float startYAfterFirst = -0.5f;
    public float stepY = 1.5f;

    [Header("Move (random speed)")]
    [Tooltip("Candidate speeds; one is picked randomly per block.")]
    public float[] moveSpeeds;
    [Tooltip("Fallback speed if moveSpeeds is empty.")]
    public float defaultMoveSpeed = 3f;
    [Tooltip("Arrival threshold (sqr distance).")]
    public float arriveThreshold = 0.02f;
    [Tooltip("Offscreen margin for OffscreenBiased mode.")]
    public float offscreenMargin = 1.0f;

    [Header("Spawn X")]
    public SpawnXMode spawnXMode = SpawnXMode.OffscreenBiased;
    public float manualSpawnX = -5f;
    public Vector2 manualSpawnXRange = new Vector2(-6f, -2f);

    [Header("OffscreenBiased Options")]
    [Range(0f, 1f)] public float spawnCenterBias = 0.35f;
    public bool keepOffscreenAtStart = true;
    public float minOffscreenMargin = 0.2f;

    [Header("Side Selection")]
    public SpawnSideMode spawnSideMode = SpawnSideMode.RandomEach;

    [Header("Game Start Gate")]
    [Tooltip("When true, spawning/movement proceed. Set by countdown.")]
    public bool gameCanRun = false;

    [Header("First Two Slow")]
    [Tooltip("If true, first two blocks use slowFirstTwoSpeed.")]
    public bool useSlowForFirstTwo = true;
    [Tooltip("Speed for first two blocks (overrides random picks).")]
    public float slowFirstTwoSpeed = 1.2f;

    [Header("Camera (optional)")]
    public Transform cameraTarget;
    public bool cameraPunchyEnabled = true;
    public float cameraFirstStep = 0.5f, cameraNextStep = 1.5f;
    public float punchOvershoot = 0.35f, punchDownKick = 0.18f;
    public int punchBounces = 2; [Range(0.1f, 0.95f)] public float punchDamping = 0.55f;
    public float punchUpDuration = 0.10f, punchBounceDuration = 0.10f;
    public float shakeAmplitude = 0.12f, shakeDuration = 0.10f; [Range(0.1f, 0.99f)] public float shakeDamping = 0.75f;

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
        // Wait for external game start flag
        yield return new WaitUntil(() => gameCanRun);

        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        while (remaining > 0)
        {
            // In case gameCanRun toggles off during runtime (optional safety)
            if (!gameCanRun)
                yield return new WaitUntil(() => gameCanRun);

            float y = (spawned == 0) ? firstBlockY : startYAfterFirst + (spawned - 1) * stepY;

            bool fromLeft = PickSpawnSide();
            float spawnX = ComputeSpawnX(fromLeft);
            float activationX = fromLeft ? -0.5f : 0.5f;

            Vector3 from = new Vector3(spawnX, y, 0f);
            Vector3 to = new Vector3(0f, y, 0f);

            var go = Instantiate(blockPrefab, from, Quaternion.identity);
            ApplyRandomSpriteOrGray(go);
            go.tag = "NoBlock";

            float speed = (useSlowForFirstTwo && spawned < 2)
                ? Mathf.Max(0.01f, slowFirstTwoSpeed)
                : PickMoveSpeed();

            spawned++; remaining--;

            yield return StartCoroutine(MoveBlock(go.transform, to, speed, go, activationX, fromLeft));

            go.transform.position = to;
            if (go.tag != "Block") go.tag = "Block";

            if (postArrivalDelay > 0f) yield return new WaitForSeconds(postArrivalDelay);
        }
    }

    bool PickSpawnSide()
    {
        switch (spawnSideMode)
        {
            case SpawnSideMode.ForceLeft: return true;
            case SpawnSideMode.ForceRight: return false;
            case SpawnSideMode.RandomEach:
            default: return rng.NextDouble() < 0.5;
        }
    }

    float ComputeSpawnX(bool fromLeft)
    {
        switch (spawnXMode)
        {
            case SpawnXMode.ManualAbsolute:
                return fromLeft ? -Mathf.Abs(manualSpawnX) : Mathf.Abs(manualSpawnX);

            case SpawnXMode.ManualRange:
                return PickFromRangeWithSide(manualSpawnXRange, fromLeft);

            case SpawnXMode.OffscreenBiased:
            default:
                return GetOffscreenBiasedX(fromLeft);
        }
    }

    float PickFromRangeWithSide(Vector2 range, bool left)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);

        float negMin = min;
        float negMax = Mathf.Min(max, 0f);
        bool hasNeg = negMin < negMax;

        float posMin = Mathf.Max(min, 0f);
        float posMax = max;
        bool hasPos = posMin < posMax;

        if (left)
        {
            if (hasNeg) return Random.Range(negMin, negMax);
            float pick = Random.Range(min, max);
            return -Mathf.Abs(pick);
        }
        else
        {
            if (hasPos) return Random.Range(posMin, posMax);
            float pick = Random.Range(min, max);
            return Mathf.Abs(pick);
        }
    }

    float GetOffscreenBiasedX(bool leftSide)
    {
        var cam = Camera.main;
        float half = cam.orthographicSize * cam.aspect;
        float camX = cam.transform.position.x;

        float edge = camX + (leftSide ? -half : +half);
        float spawnEdgeX = edge + (leftSide ? -offscreenMargin : +offscreenMargin);

        float targetX = 0f;
        float biasedX = Mathf.Lerp(spawnEdgeX, targetX, Mathf.Clamp01(spawnCenterBias));

        if (keepOffscreenAtStart)
        {
            float minOutside = edge + (leftSide ? -minOffscreenMargin : +minOffscreenMargin);
            if (leftSide) biasedX = Mathf.Min(biasedX, minOutside);
            else biasedX = Mathf.Max(biasedX, minOutside);
        }

        return biasedX;
    }

    IEnumerator MoveBlock(Transform tr, Vector3 target, float speed, GameObject go, float activationX, bool fromLeft)
    {
        Vector3 p = tr.position; p.z = 0f; tr.position = p; target.z = 0f;
        float thrSqr = arriveThreshold * arriveThreshold;

        if (go.tag != "Block")
        {
            float x0 = tr.position.x;
            if ((fromLeft && x0 >= activationX) || (!fromLeft && x0 <= activationX))
                go.tag = "Block";
        }

        while ((tr.position - target).sqrMagnitude > thrSqr)
        {
            // If game was paused by clearing gameCanRun, wait
            if (!gameCanRun) yield return new WaitUntil(() => gameCanRun);

            tr.position = Vector3.MoveTowards(tr.position, target, speed * Time.deltaTime);

            if (go.tag != "Block")
            {
                float x = tr.position.x;
                if ((fromLeft && x >= activationX) || (!fromLeft && x <= activationX))
                    go.tag = "Block";
            }

            if (Mathf.Abs(tr.position.z) > 0.0001f)
            {
                var fix = tr.position; fix.z = 0f; tr.position = fix;
            }

            yield return null;
        }
    }

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

        slowFirstTwoSpeed = Mathf.Max(0.01f, slowFirstTwoSpeed);
    }
#endif
}
