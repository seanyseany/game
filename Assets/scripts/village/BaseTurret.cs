using System.Collections;
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

    [SerializeField] protected int level = 1;

    [Header("Placement")]
    [SerializeField] protected Vector2 bottomLocalPosition;

    [HideInInspector] [SerializeField] protected TurretLevelData level1Data = new TurretLevelData();
    [HideInInspector] [SerializeField] protected TurretLevelData level2Data = new TurretLevelData();
    [HideInInspector] [SerializeField] protected TurretLevelData level3Data = new TurretLevelData();

    protected int ammoCurrent;
    protected int ammoCapacity;
    protected Villan currentTarget;
    protected Coroutine firingRoutine;
    protected GameObject reloadVisualInstance;

    protected string slotId;

    public string CatalogId => gameObject.name.Replace("(Clone)", string.Empty).Trim();
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

        TurretBullet bulletPrefab = GetBulletPrefab();
        if (bulletPrefab == null || VillageManagement.Instance == null)
            return false;

        int price = GetBulletPriceForPercent(percent, bulletPrefab);
        if (!VillageManagement.Instance.TrySpendOxygen(price))
            return false;

        ammoCurrent = Mathf.Clamp(ammoCurrent + GetAmmoAmountForPercent(percent), 0, ammoCapacity);
        RebuildReloadVisual();
        PushState();
        return true;
    }

    public int GetBulletPriceForPercent(int percent, TurretBullet bulletPrefab = null)
    {
        TurretBullet source = bulletPrefab != null ? bulletPrefab : GetBulletPrefab();
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
        PushState();
    }

    protected abstract TurretBullet GetBulletPrefab();
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

    protected virtual Quaternion GetBulletSpawnRotation(Transform spawnPoint)
    {
        Vector3 direction = GetCurrentSpawnDirection(spawnPoint);
        if (direction.sqrMagnitude <= 0.0001f)
            return Quaternion.identity;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, 0f, angle);
    }

    protected void SpawnBullet(TurretBullet prefab, Transform spawnPoint)
    {
        if (prefab == null || spawnPoint == null || !HasAmmo())
            return;

        TurretBullet.Spawn(prefab, spawnPoint.position, GetCurrentSpawnDirection(spawnPoint), GetBulletSpawnRotation(spawnPoint));
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
