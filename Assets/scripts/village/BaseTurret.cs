using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BaseTurret : MonoBehaviour
{
    [System.Serializable]
    public class TurretLevelData
    {
        public Sprite sprite;
        public int oxygenPrice = 10;
        public int ammoCapacity = 10;
        public BaseTurret upgradePrefab;
        public GameObject reloadVisualPrefab;
    }

    [Header("Identity")]
    [SerializeField] protected string turretId;
    [SerializeField] protected string slotId;
    [SerializeField] protected int level = 1;

    [Header("Range")]
    [SerializeField] protected Vector2 rangeMinLocal = new Vector2(-3f, -2f);
    [SerializeField] protected Vector2 rangeMaxLocal = new Vector2(3f, 2f);

    [Header("Rig")]
    [SerializeField] protected Transform turretStart;
    [SerializeField] protected Transform turretEnd;
    [SerializeField] protected Vector2 bottomLocalPosition;
    [SerializeField] protected GameObject exclamationPrefab;

    [Header("Data")]
    [SerializeField] protected TurretLevelData level1Data = new TurretLevelData();
    [SerializeField] protected TurretLevelData level2Data = new TurretLevelData();
    [SerializeField] protected TurretLevelData level3Data = new TurretLevelData();

    protected int ammoCurrent;
    protected int ammoCapacity;
    protected Villan currentTarget;
    protected Coroutine firingRoutine;
    protected GameObject exclamationInstance;
    protected GameObject reloadVisualInstance;

    public string TurretId => turretId;
    public string CatalogId => ShopIdentityUtility.GetStableId(turretId, this);
    public string SlotId => slotId;
    public int Level => level;
    public int AmmoCurrent => ammoCurrent;
    public int AmmoCapacity => ammoCapacity;
    public Vector2 BottomLocalPosition => bottomLocalPosition;
    public Sprite CurrentSprite => GetDataForLevel(level).sprite;
    public int CurrentOxygenPrice => GetDataForLevel(level).oxygenPrice;

    protected virtual void Start()
    {
        ApplyLevel(level, false);
    }

    protected virtual void Update()
    {
        AcquireTarget();
        AimAtTargetOrCenter();
        UpdateFiringState();
    }

    public void AssignSlot(string nextSlotId)
    {
        slotId = nextSlotId;
    }

    public void ApplyLevel(int targetLevel, bool keepAmmoRatio)
    {
        int previousCapacity = Mathf.Max(1, ammoCapacity);
        float fillRatio = previousCapacity > 0 ? (float)ammoCurrent / previousCapacity : 0f;

        level = Mathf.Clamp(targetLevel, 1, 3);
        ammoCapacity = Mathf.Max(0, GetDataForLevel(level).ammoCapacity);
        ammoCurrent = keepAmmoRatio ? Mathf.Clamp(Mathf.RoundToInt(ammoCapacity * fillRatio), 0, ammoCapacity) : ammoCapacity;

        RebuildReloadVisual();
        UpdateEmptyIndicator();
        PushState();
    }

    public void SetPlacementMirrored(bool mirrored)
    {
        Vector3 scale = transform.localScale;
        float absX = Mathf.Abs(scale.x);
        scale.x = mirrored ? -absX : absX;
        transform.localScale = scale;
    }

    public bool CanRefillPercent(int percent)
    {
        return ammoCurrent < ammoCapacity && ammoCurrent + GetAmmoAmountForPercent(percent) <= ammoCapacity;
    }

    public bool TryBuyAmmoPercent(int percent)
    {
        if (!CanRefillPercent(percent))
            return false;

        Bullet bulletPrefab = GetBulletPrefab();
        if (bulletPrefab == null || VillageManagement.Instance == null)
            return false;

        int price = GetBulletPriceForPercent(percent, bulletPrefab);
        if (!VillageManagement.Instance.TrySpendOxygen(price))
            return false;

        ammoCurrent = Mathf.Clamp(ammoCurrent + GetAmmoAmountForPercent(percent), 0, ammoCapacity);
        RebuildReloadVisual();
        UpdateEmptyIndicator();
        PushState();
        return true;
    }

    public int GetBulletPriceForPercent(int percent, Bullet bulletPrefab = null)
    {
        Bullet source = bulletPrefab != null ? bulletPrefab : GetBulletPrefab();
        if (source == null)
            return 0;

        switch (percent)
        {
            case 30: return source.OxygenPrice30;
            case 60: return source.OxygenPrice60;
            default: return source.OxygenPrice100;
        }
    }

    public int GetAmmoAmountForPercent(int percent)
    {
        return Mathf.CeilToInt(ammoCapacity * (percent / 100f));
    }

    public bool CanUpgrade()
    {
        return level < 3 && GetDataForLevel(level).upgradePrefab != null;
    }

    public BaseTurret GetUpgradePrefab()
    {
        return GetDataForLevel(level).upgradePrefab;
    }

    public virtual void PushState()
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null || string.IsNullOrWhiteSpace(slotId))
            return;

        villageManagement.UpsertTurretState(new VillageManagement.TurretState
        {
            slotId = slotId,
            turretId = CatalogId,
            level = level,
            currentAmmo = ammoCurrent,
            maxAmmo = ammoCapacity,
            isPlaced = true
        });
    }

    protected void ConsumeAmmo(int amount)
    {
        ammoCurrent = Mathf.Max(0, ammoCurrent - amount);
        RebuildReloadVisual();
        UpdateEmptyIndicator();
        PushState();
    }

    protected abstract Bullet GetBulletPrefab();
    protected abstract IEnumerator FireRoutine();

    protected bool HasAmmo()
    {
        return ammoCurrent > 0;
    }

    protected Vector3 GetCurrentSpawnDirection(Transform spawnPoint)
    {
        if (currentTarget != null)
            return (currentTarget.AimTarget.position - spawnPoint.position).normalized;

        return transform.right;
    }

    protected void SpawnBullet(Bullet prefab, Transform spawnPoint)
    {
        if (prefab == null || spawnPoint == null || !HasAmmo())
            return;

        Bullet bullet = Instantiate(prefab, spawnPoint.position, Quaternion.identity);
        bullet.Launch(GetCurrentSpawnDirection(spawnPoint));
        ConsumeAmmo(1);
    }

    protected TurretLevelData GetDataForLevel(int targetLevel)
    {
        if (targetLevel >= 3)
            return level3Data.upgradePrefab == null && level3Data.ammoCapacity == 0 && level3Data.oxygenPrice == 0 && level3Data.sprite == null
                ? level2Data
                : level3Data;

        if (targetLevel == 2)
            return level2Data.sprite == null && level2Data.ammoCapacity == 0 && level2Data.oxygenPrice == 0 && level2Data.upgradePrefab == null
                ? level1Data
                : level2Data;

        return level1Data;
    }

    private void AcquireTarget()
    {
        if (currentTarget != null)
        {
            if (!currentTarget || !IsInRange(currentTarget.transform.position))
                currentTarget = null;
        }

        if (currentTarget != null)
            return;

        Villan[] all = FindObjectsByType<Villan>(FindObjectsSortMode.None);
        float bestDistance = float.MaxValue;
        for (int i = 0; i < all.Length; i++)
        {
            Villan candidate = all[i];
            if (candidate == null || !candidate.isActiveAndEnabled || !IsInRange(candidate.transform.position))
                continue;

            float distance = Vector2.Distance(transform.position, candidate.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                currentTarget = candidate;
            }
        }
    }

    private bool IsInRange(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return local.x >= Mathf.Min(rangeMinLocal.x, rangeMaxLocal.x) &&
               local.x <= Mathf.Max(rangeMinLocal.x, rangeMaxLocal.x) &&
               local.y >= Mathf.Min(rangeMinLocal.y, rangeMaxLocal.y) &&
               local.y <= Mathf.Max(rangeMinLocal.y, rangeMaxLocal.y);
    }

    private void AimAtTargetOrCenter()
    {
        Vector3 aimPoint = currentTarget != null
            ? currentTarget.AimTarget.position
            : transform.TransformPoint(new Vector3((rangeMinLocal.x + rangeMaxLocal.x) * 0.5f, (rangeMinLocal.y + rangeMaxLocal.y) * 0.5f, 0f));

        if (turretStart == null || turretEnd == null)
            return;

        Vector3 direction = aimPoint - turretStart.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Vector3 euler = transform.eulerAngles;
        euler.z = angle;
        transform.eulerAngles = euler;
    }

    private void UpdateFiringState()
    {
        if (currentTarget != null && HasAmmo())
        {
            if (firingRoutine == null)
                firingRoutine = StartCoroutine(FireRoutine());
        }
        else if (firingRoutine != null)
        {
            StopCoroutine(firingRoutine);
            firingRoutine = null;
        }
    }

    private void UpdateEmptyIndicator()
    {
        bool shouldShow = ammoCurrent <= 0 && exclamationPrefab != null;
        if (shouldShow && exclamationInstance == null)
        {
            exclamationInstance = Instantiate(exclamationPrefab, transform);
            exclamationInstance.transform.localPosition = Vector3.zero;
        }
        else if (!shouldShow && exclamationInstance != null)
        {
            Destroy(exclamationInstance);
            exclamationInstance = null;
        }
    }

    protected void RebuildReloadVisual()
    {
        GameObject prefab = GetDataForLevel(level).reloadVisualPrefab;
        if (reloadVisualInstance != null)
            Destroy(reloadVisualInstance);

        if (prefab != null && ammoCurrent > 0)
        {
            reloadVisualInstance = Instantiate(prefab, transform);
            reloadVisualInstance.transform.localPosition = Vector3.zero;
        }
    }
}
