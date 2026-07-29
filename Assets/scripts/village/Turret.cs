using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Turret : BaseTurret
{
    [System.Serializable]
    private class LauncherEntry
    {
        public Transform launcher;
        public Transform bulletSpawnPoint;
        public float extraInterval;
        public float recoilDistance = 0.1f;
        public float recoilSpeed = 6f;

        [HideInInspector] public Vector3 originalLocalPosition;
        [HideInInspector] public bool poseCached;
        [HideInInspector] public float currentRecoilOffset;
    }

    private const float IdleSwingAmplitude = 0.3f;
    private const float IdleSwingBaseOffset = 0.5f;
    private const float IdleSwingSpeed = 1.5f;
    private const float LineRangeThickness = 0.75f;
    private const float RotationDegreesPerSecond = 180f;

    [Header("Rig")]
    [SerializeField] private Transform centerPoint;
    [SerializeField] private Transform endPoint;
    [SerializeField] private Transform uiAnchor;
    [SerializeField] private List<LauncherEntry> launchers = new List<LauncherEntry>();

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
    private float currentRotationAngle;
    private Vector3 uiAnchorOriginalLocalPosition;
    private bool uiAnchorPoseCached;

    [HideInInspector] [SerializeField] private List<Transform> legacyBulletSpawnPoints = new List<Transform>();
    [HideInInspector] [SerializeField] private List<float> legacyBulletSpawnPointExtraIntervals = new List<float>();

    public Transform UiAnchor => uiAnchor != null ? uiAnchor : transform;

    protected override void Start()
    {
        ResolveUiAnchor();
        ApplyInspectorAmmoCapacity();
        base.Start();
        CacheOriginalPose();
        CacheLauncherPoses();
        CacheReloadAnimators();
        StopReloadAnimation();
    }

    private void OnValidate()
    {
        UpgradeLegacyLauncherData();
        ResolveUiAnchor();
        ApplyInspectorAmmoCapacity();
        CacheLauncherPoses();
        EditorValidateStatusBar();
    }

    private void Update()
    {
        if (!IsInstalled())
        {
            StopFiringImmediately();
            return;
        }

        UpdateTarget();
        UpdateRotation();
        UpdateLauncherRecoil();
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

    private void UpgradeLegacyLauncherData()
    {
        if (launchers.Count > 0 || legacyBulletSpawnPoints.Count == 0)
            return;

        for (int i = 0; i < legacyBulletSpawnPoints.Count; i++)
        {
            Transform spawnPoint = legacyBulletSpawnPoints[i];
            if (spawnPoint == null)
                continue;

            LauncherEntry entry = new LauncherEntry
            {
                bulletSpawnPoint = spawnPoint,
                launcher = spawnPoint.parent,
                extraInterval = i < legacyBulletSpawnPointExtraIntervals.Count ? legacyBulletSpawnPointExtraIntervals[i] : 0f
            };
            launchers.Add(entry);
        }
    }

    private void ResolveUiAnchor()
    {
        if (uiAnchor != null)
            return;

        uiAnchor = FindChildRecursive(transform, "UiAnchor");
        if (uiAnchor == null)
            uiAnchor = FindChildRecursive(transform, "UIAnchor");
    }

    private void CacheUiAnchorPose()
    {
        if (uiAnchor == null || uiAnchorPoseCached)
            return;

        uiAnchorOriginalLocalPosition = uiAnchor.localPosition;
        uiAnchorPoseCached = true;
    }

    protected override void HandlePlacementMirrorChanged(bool mirrored)
    {
        ResolveUiAnchor();
        CacheUiAnchorPose();
        if (uiAnchor == null)
            return;

        Vector3 localPosition = uiAnchorOriginalLocalPosition;
        localPosition.x = mirrored ? -Mathf.Abs(localPosition.x) : Mathf.Abs(localPosition.x);
        uiAnchor.localPosition = localPosition;
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
                return child;

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
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

        while (IsInstalled() && currentTarget != null && HasAmmo())
        {
            TurretBullet currentBulletPrefab = GetBulletPrefab();
            if (currentBulletPrefab == null)
                break;

            List<int> activeLauncherIndices = GetActiveLauncherIndices();
            if (activeLauncherIndices.Count == 0)
                break;

            bool firedThisFrame = false;
            float now = Time.time;

            for (int i = 0; i < activeLauncherIndices.Count; i++)
            {
                if (!HasAmmo())
                    break;

                int launcherIndex = activeLauncherIndices[i];
                LauncherEntry launcherEntry = launchers[launcherIndex];
                if (launcherEntry == null || launcherEntry.bulletSpawnPoint == null)
                    continue;

                if (!nextFireTimes.TryGetValue(launcherIndex, out float nextFireTime))
                    nextFireTime = now;

                if (now < nextFireTime)
                    continue;

                SpawnBullet(currentBulletPrefab, launcherEntry.bulletSpawnPoint);
                TriggerLauncherRecoil(launcherEntry);
                nextFireTimes[launcherIndex] = now + GetSpawnInterval() + Mathf.Max(0f, launcherEntry.extraInterval);
                firedThisFrame = true;
            }

            if (firedThisFrame)
                PlayReloadAnimation();

            yield return null;
        }

        StopReloadAnimation();
        firingRoutine = null;
    }

    private bool IsInstalled()
    {
        return transform.parent != null && transform.parent.GetComponent<TurretImplementation>() != null;
    }

    private void StopFiringImmediately()
    {
        currentTarget = null;
        if (firingRoutine != null)
        {
            StopCoroutine(firingRoutine);
            firingRoutine = null;
        }

        StopReloadAnimation();
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

        Vector3 desiredWorldTarget = currentTarget != null ? currentTarget.AimTarget.position : GetIdleWorldTarget();
        ApplySmoothedRotation(desiredWorldTarget);
    }

    private void ApplySmoothedRotation(Vector3 worldTarget)
    {
        if (centerPoint == null || endPoint == null)
            return;

        RestoreOriginalPose();

        Vector2 baseDirection = endPoint.position - centerPoint.position;
        Vector2 desiredDirection = worldTarget - centerPoint.position;
        if (baseDirection.sqrMagnitude < 0.0001f || desiredDirection.sqrMagnitude < 0.0001f)
            return;

        float desiredAngle = Vector2.SignedAngle(baseDirection, desiredDirection);
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
        return GetActiveLauncherIndices().Count > 0;
    }

    private List<int> GetActiveLauncherIndices()
    {
        List<int> activeLauncherIndices = new List<int>();
        for (int i = 0; i < launchers.Count; i++)
        {
            LauncherEntry entry = launchers[i];
            if (entry != null && entry.bulletSpawnPoint != null)
                activeLauncherIndices.Add(i);
        }

        return activeLauncherIndices;
    }

    private void CacheLauncherPoses()
    {
        for (int i = 0; i < launchers.Count; i++)
        {
            LauncherEntry entry = launchers[i];
            if (entry == null || entry.launcher == null)
                continue;

            entry.originalLocalPosition = entry.launcher.localPosition;
            entry.poseCached = true;
        }
    }

    private void TriggerLauncherRecoil(LauncherEntry entry)
    {
        if (entry == null || entry.launcher == null)
            return;

        if (!entry.poseCached)
        {
            entry.originalLocalPosition = entry.launcher.localPosition;
            entry.poseCached = true;
        }

        float distance = Mathf.Max(0f, entry.recoilDistance);
        entry.currentRecoilOffset = Mathf.Max(entry.currentRecoilOffset, distance);
    }

    private void UpdateLauncherRecoil()
    {
        for (int i = 0; i < launchers.Count; i++)
        {
            LauncherEntry entry = launchers[i];
            if (entry == null || entry.launcher == null)
                continue;

            if (!entry.poseCached)
            {
                entry.originalLocalPosition = entry.launcher.localPosition;
                entry.poseCached = true;
            }

            float speed = Mathf.Max(0.01f, entry.recoilSpeed);
            entry.currentRecoilOffset = Mathf.MoveTowards(entry.currentRecoilOffset, 0f, speed * Time.deltaTime);
            entry.launcher.localPosition = entry.originalLocalPosition + Vector3.right * entry.currentRecoilOffset;
        }
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
