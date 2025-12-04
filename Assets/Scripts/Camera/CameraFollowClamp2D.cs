using UnityEngine;

[DisallowMultipleComponent]
public class CameraFollowClamp2D : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Offset")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Smoothing")]
    [SerializeField] private bool useSmooth = true;
    [SerializeField] private float smoothTime = 0.15f;

    [Header("Clamp X")]
    [SerializeField] private bool clampX = true;
    [SerializeField] private float minX = -10f;
    [SerializeField] private float maxX = 10f;

    [Header("Clamp Y")]
    [SerializeField] private bool clampY = true;
    [SerializeField] private float minY = -5f;
    [SerializeField] private float maxY = 5f;

    private Vector3 velocity;

    private void Reset()
    {
        if (target == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) target = playerObj.transform;
        }

        offset = new Vector3(0f, 0f, -10f);
        smoothTime = 0.15f;
        useSmooth = true;
        clampX = true;
        clampY = true;
        minX = -10f;
        maxX = 10f;
        minY = -5f;
        maxY = 5f;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + offset;

        if (clampX)
            desired.x = Mathf.Clamp(desired.x, minX, maxX);

        if (clampY)
            desired.y = Mathf.Clamp(desired.y, minY, maxY);

        if (useSmooth)
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref velocity,
                smoothTime
            );
        }
        else
        {
            transform.position = desired;
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public void SetClampX(float newMinX, float newMaxX)
    {
        minX = newMinX;
        maxX = newMaxX;
    }

    public void SetClampY(float newMinY, float newMaxY)
    {
        minY = newMinY;
        maxY = newMaxY;
    }
}
