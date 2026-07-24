using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Turret : BaseTurret
{
    private const float IdleSwingAmplitude = 0.3f;
    private const float IdleSwingBaseOffset = 0.5f;
    private const float IdleSwingSpeed = 1.5f;
    private const float LineRangeThickness = 0.75f;
    private const float RotationDegreesPerSecond = 360f;

    [Header("Rig")]
    [SerializeField] private Transform centerPoint;
    [SerializeField] private Transform endPoint;
    [SerializeField] private List<Transform> bulletSpawnPoints = new List<Transform>();
    [SerializeField] private List<float> bulletSpawnPointExtraIntervals = new List<float>();

    [Header("Bullet")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float bulletSpawnInterval = 0.5f;
    [SerializeField] private int ammoCapacitySetting = 10;

    [Header("Reload")]
    [SerializeField] private List<GameObject> reloadPrefabs = new List<GameObject>();

    private readonly List<Animator> reloadAnimators = new List<Animator>();
    private readonly List<GameObject> reloadInstances = new List<GameObject>();
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Transform rangeReference;
    private Vector2 targetRangeMinLocal = new Vector2(-3f, -2f);
    private Vector2 targetRangeMaxLocal = new Vector2(3f, 2f);
    private bool poseCached;
    private int nextSpawnPointIndex;
    private float currentRotationAngle;

    protected override void Start()
    {
        ApplyInspectorAmmoCapacity();
        base.Start();
        CacheOriginalPose();
        CacheReloadAnimators();
        StopReloadAnimation();
    }

    private void OnValidate()
    {
        ApplyInspectorAmmoCapacity();
    }

    private void Update()
    {
        if (transform.parent == null)
            return;

        UpdateTarget();
        UpdateRotation();
        UpdateFiringState();
    }

    private void OnDisable()
    {
        if (firingRoutine != null)
        {
            StopCoroutine(firingRoutine);
            firingRoutine = null;
        }

        StopReloadAnimation();
    }

    private void ApplyInspectorAmmoCapacity()
    {
        int clampedCapacity = Mathf.Max(0, ammoCapacitySetting);
        GetDataForLevel(level).ammoCapacity = clampedCapacity;
    }

    protected override TurretBullet GetBulletPrefab()
    {
        if (bulletPrefab == null)
            return null;

        TurretBullet bullet = bulletPrefab.GetComponent<TurretBullet>();
        return bullet != null ? bullet : bulletPrefab.GetComponentInChildren<TurretBullet>(true);
    }

    protected override Quaternion GetBulletSpawnRotation(Transform spawnPoint)
    {
        if (centerPoint == null || endPoint == null)
            return base.GetBulletSpawnRotation(spawnPoint);

        Vector2 barrelDirection = endPoint.position - centerPoint.position;
        if (barrelDirection.sqrMagnitude <= 0.0001f)
            return base.GetBulletSpawnRotation(spawnPoint);

        float angle = Mathf.Atan2(barrelDirection.y, barrelDirection.x) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, 0f, angle);
    }

    public void ConfigureTargetRange(Transform referenceTransform, Vector2 minLocal, Vector2 maxLocal)
    {
        rangeReference = referenceTransform;
        targetRangeMinLocal = minLocal;
        targetRangeMaxLocal = maxLocal;
    }

    protected override IEnumerator FireRoutine()
    {
        Dictionary<int, float> nextFireTimes = new Dictionary<int, float>();

        while (currentTarget != null && HasAmmo())
        {
            TurretBullet currentBulletPrefab = GetBulletPrefab();
            if (currentBulletPrefab == null)
                break;

            List<int> activeSpawnPointIndices = GetActiveSpawnPointIndices();
            if (activeSpawnPointIndices.Count == 0)
                break;

            bool firedThisFrame = false;
            float now = Time.time;

            for (int i = 0; i < activeSpawnPointIndices.Count; i++)
            {
                if (!HasAmmo())
                    break;

                int spawnPointIndex = activeSpawnPointIndices[i];
                Transform spawnPoint = bulletSpawnPoints[spawnPointIndex];
                if (spawnPoint == null)
                    continue;

                if (!nextFireTimes.TryGetValue(spawnPointIndex, out float nextFireTime))
                    nextFireTime = now;

                if (now < nextFireTime)
                    continue;

                SpawnBullet(currentBulletPrefab, spawnPoint);
                nextFireTimes[spawnPointIndex] = now + GetSpawnInterval() + GetSpawnPointExtraInterval(spawnPointIndex);
                firedThisFrame = true;
            }

            if (firedThisFrame)
                PlayReloadAnimation();

            yield return null;
        }

        StopReloadAnimation();
        firingRoutine = null;
    }

    private void UpdateTarget()
    {
        if (IsTargetValid(currentTarget))
            return;

        currentTarget = FindClosestTargetInRange();
    }

    private bool IsTargetValid(Villan target)
    {
        return target != null && target.isActiveAndEnabled && IsInTargetRange(target.AimTarget.position);
    }

    private Villan FindClosestTargetInRange()
    {
        Villan[] candidates = FindObjectsByType<Villan>(FindObjectsSortMode.None);
        if (candidates == null || candidates.Length == 0)
            return null;

        Transform reference = GetRangeReference();
        Vector3 minAnchor = reference.TransformPoint(new Vector3(targetRangeMinLocal.x, targetRangeMinLocal.y, 0f));
        Villan nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < candidates.Length; i++)
        {
            Villan candidate = candidates[i];
            if (candidate == null || !candidate.isActiveAndEnabled || !IsInTargetRange(candidate.AimTarget.position))
                continue;

            float distance = (candidate.AimTarget.position - minAnchor).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    private bool IsInTargetRange(Vector3 worldPosition)
    {
        float minX = Mathf.Min(targetRangeMinLocal.x, targetRangeMaxLocal.x);
        float maxX = Mathf.Max(targetRangeMinLocal.x, targetRangeMaxLocal.x);
        float minY = Mathf.Min(targetRangeMinLocal.y, targetRangeMaxLocal.y);
        float maxY = Mathf.Max(targetRangeMinLocal.y, targetRangeMaxLocal.y);

        bool hasWidth = !Mathf.Approximately(minX, maxX);
        bool hasHeight = !Mathf.Approximately(minY, maxY);

        if (hasWidth && hasHeight)
        {
            Vector3 local = GetRangeReference().InverseTransformPoint(worldPosition);
            return local.x >= minX &&
                   local.x <= maxX &&
                   local.y >= minY &&
                   local.y <= maxY;
        }

        Vector3 worldMin = GetRangeReference().TransformPoint(new Vector3(targetRangeMinLocal.x, targetRangeMinLocal.y, 0f));
        Vector3 worldMax = GetRangeReference().TransformPoint(new Vector3(targetRangeMaxLocal.x, targetRangeMaxLocal.y, 0f));
        return DistanceToSegment(worldPosition, worldMin, worldMax) <= LineRangeThickness;
    }

    private float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector2 segment = end - start;
        float segmentLengthSq = segment.sqrMagnitude;
        if (segmentLengthSq <= 0.0001f)
            return Vector2.Distance(point, start);

        float projection = Vector2.Dot((Vector2)(point - start), segment) / segmentLengthSq;
        projection = Mathf.Clamp01(projection);
        Vector3 closest = start + (Vector3)(segment * projection);
        return Vector2.Distance(point, closest);
    }

    private void UpdateRotation()
    {
        if (!poseCached)
            CacheOriginalPose();

        Vector3 desiredWorldTarget;
        if (currentTarget == null)
        {
            desiredWorldTarget = GetIdleWorldTarget();
        }
        else
        {
            desiredWorldTarget = currentTarget.AimTarget.position;
        }

        ApplySmoothedRotation(desiredWorldTarget);
    }

    private void ApplySmoothedRotation(Vector3 worldTarget)
    {
        if (centerPoint == null || endPoint == null)
            return;

        RestoreOriginalPose();

        Vector2 currentDirection = endPoint.position - centerPoint.position;
        Vector2 desiredDirection = worldTarget - centerPoint.position;
        if (currentDirection.sqrMagnitude < 0.0001f || desiredDirection.sqrMagnitude < 0.0001f)
            return;

        float desiredAngle = Vector2.SignedAngle(currentDirection, desiredDirection);
        currentRotationAngle = Mathf.MoveTowardsAngle(currentRotationAngle, desiredAngle, RotationDegreesPerSecond * Time.deltaTime);
        transform.RotateAround(centerPoint.position, Vector3.forward, currentRotationAngle);
    }

    private void UpdateFiringState()
    {
        if (currentTarget != null && HasAmmo() && GetBulletPrefab() != null && HasSpawnPoint())
        {
            if (firingRoutine == null)
                firingRoutine = StartCoroutine(FireRoutine());
            return;
        }

        if (firingRoutine != null)
        {
            StopCoroutine(firingRoutine);
            firingRoutine = null;
        }

        StopReloadAnimation();
    }

    private bool HasSpawnPoint()
    {
        return GetActiveSpawnPointIndices().Count > 0;
    }

    private List<int> GetActiveSpawnPointIndices()
    {
        List<int> activeSpawnPointIndices = new List<int>();
        for (int i = 0; i < bulletSpawnPoints.Count; i++)
        {
            if (bulletSpawnPoints[i] != null)
                activeSpawnPointIndices.Add(i);
        }

        return activeSpawnPointIndices;
    }

    private float GetSpawnPointExtraInterval(int spawnPointIndex)
    {
        if (spawnPointIndex < 0 || spawnPointIndex >= bulletSpawnPointExtraIntervals.Count)
            return 0f;

        return Mathf.Max(0f, bulletSpawnPointExtraIntervals[spawnPointIndex]);
    }

    private float GetSpawnInterval()
    {
        if (bulletSpawnInterval > 0f)
            return bulletSpawnInterval;

        TurretBullet currentBulletPrefab = GetBulletPrefab();
        return currentBulletPrefab != null ? Mathf.Max(0.01f, currentBulletPrefab.SpawnSpeed) : 0.5f;
    }

    private void CacheOriginalPose()
    {
        originalLocalPosition = transform.localPosition;
        originalLocalRotation = transform.localRotation;
        poseCached = true;
    }

    private Vector3 GetIdleWorldTarget()
    {
        if (centerPoint == null || endPoint == null)
        {
            return transform.position;
        }

        Vector3 idleLocalTarget = new Vector3(
            (targetRangeMinLocal.x + targetRangeMaxLocal.x) * 0.5f,
            (targetRangeMinLocal.y + targetRangeMaxLocal.y) * 0.5f + IdleSwingBaseOffset + Mathf.Sin(Time.time * IdleSwingSpeed) * IdleSwingAmplitude,
            0f);

        return GetRangeReference().TransformPoint(idleLocalTarget);
    }

    private void RestoreOriginalPose()
    {
        transform.localPosition = originalLocalPosition;
        transform.localRotation = originalLocalRotation;
    }

    private Transform GetRangeReference()
    {
        return rangeReference != null ? rangeReference : transform.parent != null ? transform.parent : transform;
    }

    private void CacheReloadAnimators()
    {
        reloadAnimators.Clear();
        reloadInstances.Clear();

        for (int i = 0; i < reloadPrefabs.Count; i++)
        {
            GameObject reloadObject = reloadPrefabs[i];
            if (reloadObject == null)
                continue;

            reloadInstances.Add(reloadObject);

            Animator animator = reloadObject.GetComponent<Animator>();
            if (animator == null)
                animator = reloadObject.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                reloadAnimators.Add(animator);
                animator.speed = 0f;
            }
        }
    }

    private void PlayReloadAnimation()
    {
        for (int i = 0; i < reloadInstances.Count; i++)
        {
            if (reloadInstances[i] != null)
                reloadInstances[i].SetActive(true);
        }

        for (int i = 0; i < reloadAnimators.Count; i++)
        {
            Animator animator = reloadAnimators[i];
            if (animator == null)
                continue;

            animator.speed = 1f;
            if (!animator.isActiveAndEnabled)
                animator.enabled = true;
        }
    }

    private void StopReloadAnimation()
    {
        for (int i = 0; i < reloadAnimators.Count; i++)
        {
            Animator animator = reloadAnimators[i];
            if (animator == null)
                continue;

            animator.speed = 0f;
        }
    }
}
