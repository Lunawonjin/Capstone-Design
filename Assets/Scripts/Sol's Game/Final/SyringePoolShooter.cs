using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SyringePoolShooter : MonoBehaviour
{
    [Header("Prefab / Pool")]
    [Tooltip("주사기 발사체 프리팹")]
    [SerializeField] private SyringeProjectile projectilePrefab;

    [Tooltip("미리 생성해 둘 풀 크기")]
    [SerializeField] private int poolSize = 20;

    [Tooltip("풀 오브젝트들이 들어갈 부모(비우면 자동 생성)")]
    [SerializeField] private Transform poolParent;

    [Header("Shoot")]
    [Tooltip("좌클릭으로 발사")]
    [SerializeField] private bool shootOnLeftClick = true;

    [Tooltip("한 발 쏘고 나서 다음 발까지의 시간(초). 기본 1초")]
    [SerializeField] private float fireInterval = 1.0f;

    [Tooltip("발사 위치(비우면 이 오브젝트 위치)")]
    [SerializeField] private Transform muzzle;

    [Tooltip("마우스 방향으로 발사")]
    [SerializeField] private bool aimToMouse = true;

    [Tooltip("마우스 조준에 쓸 카메라(비우면 MainCamera)")]
    [SerializeField] private Camera aimCamera;

    private readonly Queue<SyringeProjectile> poolQueue = new Queue<SyringeProjectile>();
    private readonly List<SyringeProjectile> allProjectiles = new List<SyringeProjectile>();

    private float nextFireTime = 0f;

    void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;

        if (poolParent == null)
        {
            GameObject p = new GameObject("SyringePool");
            poolParent = p.transform;
        }

        PrewarmPool();
    }

    void Update()
    {
        if (!shootOnLeftClick) return;

        if (Input.GetMouseButtonDown(0))
        {
            TryShoot();
        }
    }

    private void TryShoot()
    {
        if (Time.time < nextFireTime) return;
        nextFireTime = Time.time + Mathf.Max(0.01f, fireInterval);

        SyringeProjectile proj = GetProjectile();
        if (proj == null) return;

        Vector3 spawnPos = (muzzle != null) ? muzzle.position : transform.position;
        proj.transform.position = spawnPos;
        proj.transform.rotation = Quaternion.identity;

        Vector2 direction = Vector2.right;

        if (aimToMouse && aimCamera != null)
        {
            Vector3 mouseWorld = aimCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = spawnPos.z;
            Vector3 d3 = mouseWorld - spawnPos;
            direction = new Vector2(d3.x, d3.y);
        }

        proj.gameObject.SetActive(true);
        proj.Launch(direction, this);
    }

    private void PrewarmPool()
    {
        poolQueue.Clear();
        allProjectiles.Clear();

        for (int i = 0; i < poolSize; i++)
        {
            SyringeProjectile proj = Instantiate(projectilePrefab, poolParent);
            proj.gameObject.SetActive(false);
            poolQueue.Enqueue(proj);
            allProjectiles.Add(proj);
        }
    }

    private SyringeProjectile GetProjectile()
    {
        if (poolQueue.Count > 0)
        {
            return poolQueue.Dequeue();
        }

        SyringeProjectile proj = Instantiate(projectilePrefab, poolParent);
        proj.gameObject.SetActive(false);
        allProjectiles.Add(proj);
        return proj;
    }

    public void ReturnProjectile(SyringeProjectile proj)
    {
        if (proj == null) return;

        proj.gameObject.SetActive(false);
        proj.transform.SetParent(poolParent, false);
        poolQueue.Enqueue(proj);
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild Pool")]
    private void CtxRebuildPool()
    {
        for (int i = 0; i < allProjectiles.Count; i++)
        {
            if (allProjectiles[i] != null)
            {
                if (Application.isPlaying) Destroy(allProjectiles[i].gameObject);
                else DestroyImmediate(allProjectiles[i].gameObject);
            }
        }
        allProjectiles.Clear();
        poolQueue.Clear();
        PrewarmPool();
    }
#endif
}
