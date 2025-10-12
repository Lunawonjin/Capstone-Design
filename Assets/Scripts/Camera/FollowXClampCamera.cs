using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class FollowX_NoJitter : MonoBehaviour
{
    public enum UpdatePhase { LateUpdate, FixedUpdate }

    [Header("Target")]
    [SerializeField] private Transform target;         // Player transform
    [SerializeField] private Rigidbody2D targetRb;     // Optional: assign if player uses Rigidbody2D

    [Header("Follow")]
    [SerializeField] private float offsetX = 0f;       // Horizontal offset
    [SerializeField] private float smoothTime = 0.12f; // 0 = snap, >0 = smooth

    [Header("Update Phase")]
    [SerializeField] private UpdatePhase updatePhase = UpdatePhase.LateUpdate;

    [Header("Axis Lock")]
    [SerializeField] private bool lockYToStart = true; // If true, keep initial Y
    [SerializeField] private float fixedY = 0f;        // Used when lockYToStart == false

    [Header("Clamp X")]
    [SerializeField] private float minX = 0f;          // Lower bound (e.g., 0 to prevent negative)
    [SerializeField] private float maxX = 12f;         // Upper bound (now capped at 12)

    [Header("Pixel Snap (optional)")]
    [SerializeField] private bool pixelSnap = false;   // Snap to pixel grid to kill micro jitter
    [SerializeField] private float pixelsPerUnit = 32f;

    private Vector3 vel;   // SmoothDamp velocity cache
    private float yLock;
    private float zLock;

    private void Awake()
    {
        yLock = lockYToStart ? transform.position.y : fixedY;
        zLock = transform.position.z;

        if (!targetRb && target) targetRb = target.GetComponent<Rigidbody2D>();
        if (maxX < minX) maxX = minX; // safety
    }

    private void LateUpdate()
    {
        if (updatePhase == UpdatePhase.LateUpdate) Follow(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (updatePhase == UpdatePhase.FixedUpdate) Follow(Time.fixedDeltaTime);
    }

    private void Follow(float dt)
    {
        if (!target) return;

        // Read target X (use Rigidbody2D for interpolated position if available)
        float tx = targetRb ? targetRb.position.x : target.position.x;

        // Apply offset, then clamp to [minX, maxX]
        float desiredX = Mathf.Clamp(tx + offsetX, minX, maxX);

        Vector3 desired = new Vector3(desiredX, yLock, zLock);

        if (smoothTime <= 0f)
        {
            transform.position = PixelSnapIfNeeded(desired);
        }
        else
        {
            transform.position = PixelSnapIfNeeded(
                Vector3.SmoothDamp(transform.position, desired, ref vel, smoothTime, Mathf.Infinity, dt)
            );
        }
    }

    private Vector3 PixelSnapIfNeeded(Vector3 pos)
    {
        if (!pixelSnap || pixelsPerUnit <= 0f) return pos;
        pos.x = Mathf.Round(pos.x * pixelsPerUnit) / pixelsPerUnit;
        pos.y = Mathf.Round(pos.y * pixelsPerUnit) / pixelsPerUnit;
        return pos;
    }

    public void SetTarget(Transform t, Rigidbody2D rb = null)
    {
        target = t;
        targetRb = rb ? rb : (t ? t.GetComponent<Rigidbody2D>() : null);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (smoothTime < 0f) smoothTime = 0f;
        if (pixelsPerUnit < 0f) pixelsPerUnit = 0f;
        if (maxX < minX) maxX = minX;
    }
#endif
}
