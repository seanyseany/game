using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Ammo Purchase")]
    [SerializeField] protected int ammoPrice30 = 3;
    [SerializeField] protected int ammoPrice60 = 6;
    [SerializeField] protected int ammoPrice100 = 10;

    [Header("Status Bar")]
    [SerializeField] private Canvas ammoStatusBar;
    [SerializeField] private Transform ammoStatusBarLevel1Anchor;
    [SerializeField] private Transform ammoStatusBarLevel2Anchor;

    [HideInInspector] [SerializeField] protected TurretLevelData level1Data = new TurretLevelData();
    [HideInInspector] [SerializeField] protected TurretLevelData level2Data = new TurretLevelData();
    [HideInInspector] [SerializeField] protected TurretLevelData level3Data = new TurretLevelData();

    protected int ammoCurrent;
    protected int ammoCapacity;
    protected Villan currentTarget;
    protected Coroutine firingRoutine;
    protected GameObject reloadVisualInstance;

    protected string slotId;
    private bool stateInitialized;
    private bool isPlacementMirrored;
    private Slider ammoStatusSlider;
    private Image ammoStatusFillImage;
    private Color ammoStatusNormalFillColor = Color.green;
    private bool ammoStatusNormalFillColorCaptured;

    private static readonly Color AmmoStatusLowFillColor = new Color(0.9f, 0.18f, 0.16f, 1f);
    private const float AmmoStatusLowFillThreshold = 1f / 3f;

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
        ResolveStatusBar();

        RebuildReloadVisual();

        RefreshStatusBar();
    }

    protected virtual void LateUpdate()
    {
        RefreshStatusBarAnchor();
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
        stateInitialized = true;

        RefreshStatusBarAnchor();
        RebuildReloadVisual();
        RefreshStatusBar();
        PushState();
    }

    public void ApplySavedState(int savedLevel, int savedCurrentAmmo, int savedMaxAmmo)
    {
        level = Mathf.Clamp(savedLevel, 1, 3);
        ammoCapacity = Mathf.Max(0, GetDataForLevel(level).ammoCapacity);

        int capacityLimit = savedMaxAmmo > 0 ? Mathf.Min(ammoCapacity, savedMaxAmmo) : ammoCapacity;
        if (capacityLimit <= 0)
            capacityLimit = ammoCapacity;

        ammoCurrent = Mathf.Clamp(savedCurrentAmmo, 0, Mathf.Max(0, capacityLimit));
        stateInitialized = true;

        RefreshStatusBarAnchor();
        RebuildReloadVisual();
        RefreshStatusBar();
    }

    public void SetPlacementMirrored(bool mirrored)
    {
        isPlacementMirrored = mirrored;

        Vector3 scale = transform.localScale;
        float absX = Mathf.Abs(scale.x);
        scale.x = mirrored ? -absX : absX;
        transform.localScale = scale;
        HandlePlacementMirrorChanged(mirrored);
        RefreshStatusBarAnchor();
    }

    protected virtual void HandlePlacementMirrorChanged(bool mirrored)
    {
    }

    public bool CanRefillPercent(int percent)
    {
        int maxAmmo = Mathf.Max(0, ammoCapacity);
        if (maxAmmo <= 0)
            return false;

        if (percent >= 100)
            return ammoCurrent < maxAmmo;

        int amountToAdd = GetAmmoAmountForPercent(percent);
        return amountToAdd > 0 && ammoCurrent <= maxAmmo - amountToAdd;
    }

    public bool TryBuyAmmoPercent(int percent)
    {
        if (!CanRefillPercent(percent))
            return false;

        if (VillageManagement.Instance == null)
            return false;

        int price = GetBulletPriceForPercent(percent);
        if (!VillageManagement.Instance.TrySpendOxygen(price))
            return false;

        if (percent >= 100)
            ammoCurrent = ammoCapacity;
        else
            ammoCurrent = Mathf.Clamp(ammoCurrent + GetAmmoAmountForPercent(percent), 0, ammoCapacity);

        RebuildReloadVisual();
        RefreshStatusBar();
        PushState(true);
        return true;
    }

    public int GetBulletPriceForPercent(int percent, TurretBullet bulletPrefab = null)
    {
        switch (percent)
        {
            case 30:
                return ammoPrice30;
            case 60:
                return ammoPrice60;
            default:
                return GetFullAmmoRefillPrice();
        }
    }

    public int GetFullAmmoRefillPrice()
    {
        int maxAmmo = Mathf.Max(0, ammoCapacity);
        if (maxAmmo <= 0 || ammoCurrent >= maxAmmo)
            return 0;

        int remainingAmount = Mathf.Max(0, maxAmmo - ammoCurrent);
        float remainingRatio = remainingAmount / (float)maxAmmo;
        return Mathf.CeilToInt(ammoPrice100 * remainingRatio);
    }

    public int GetAmmoAmountForPercent(int percent)
    {
        return Mathf.CeilToInt(ammoCapacity * GetFillFraction(percent));
    }

    public bool CanUpgrade()
    {
        return level < 3 && GetDataForLevel(level).upgradePrefab != null;
    }

    public BaseTurret GetUpgradePrefab()
    {
        return GetDataForLevel(level).upgradePrefab;
    }

    private static float GetFillFraction(int percent)
    {
        switch (percent)
        {
            case 30:
                return 1f / 3f;
            case 60:
                return 2f / 3f;
            default:
                return 1f;
        }
    }

    public virtual void PushState(bool immediate = false)
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null || string.IsNullOrWhiteSpace(slotId))
            return;

        if (villageManagement.IsRestoreInProgress)
            return;

        villageManagement.UpsertTurretState(new VillageManagement.TurretState
        {
            slotId = slotId,
            turretId = CatalogId,
            level = level,
            currentAmmo = ammoCurrent,
            maxAmmo = ammoCapacity,
            isPlaced = true
        }, immediate);
    }

    protected void ConsumeAmmo(int amount)
    {
        ammoCurrent = Mathf.Max(0, ammoCurrent - amount);
        RebuildReloadVisual();
        RefreshStatusBar();
        PushState();
    }

    protected void ResolveStatusBar()
    {
        if (ammoStatusBar == null)
            ammoStatusBar = GetComponentInChildren<Canvas>(true);

        ammoStatusSlider = ammoStatusBar != null ? ammoStatusBar.GetComponentInChildren<Slider>(true) : null;
        ammoStatusFillImage = ResolveAmmoStatusFillImage();
        if (ammoStatusFillImage != null && !ammoStatusNormalFillColorCaptured)
        {
            ammoStatusNormalFillColor = ammoStatusFillImage.color;
            ammoStatusNormalFillColorCaptured = true;
        }

        RefreshStatusBarAnchor();
    }

    protected void EditorValidateStatusBar()
    {
        ResolveStatusBar();
        RefreshStatusBar();
    }

    private void RefreshStatusBarAnchor()
    {
        if (ammoStatusBar == null)
            return;

        Transform anchor = level >= 2 && ammoStatusBarLevel2Anchor != null
            ? ammoStatusBarLevel2Anchor
            : ammoStatusBarLevel1Anchor;

        if (anchor != null)
        {
            ammoStatusBar.transform.position = anchor.position;
            ammoStatusBar.transform.rotation = Quaternion.identity;

            Vector3 canvasScale = ammoStatusBar.transform.localScale;
            float absCanvasScaleX = Mathf.Abs(Mathf.Approximately(canvasScale.x, 0f) ? 1f : canvasScale.x);
            canvasScale.x = isPlacementMirrored ? -absCanvasScaleX : absCanvasScaleX;
            ammoStatusBar.transform.localScale = canvasScale;
        }
    }

    protected void RefreshStatusBar()
    {
        if (ammoStatusBar == null)
            ResolveStatusBar();

        if (ammoStatusBar == null)
            return;

        float normalized = ammoCapacity > 0 ? Mathf.Clamp01((float)ammoCurrent / ammoCapacity) : 0f;
        if (ammoStatusSlider != null)
        {
            ammoStatusSlider.minValue = 0f;
            ammoStatusSlider.maxValue = 1f;
            ammoStatusSlider.wholeNumbers = false;
            ammoStatusSlider.direction = Slider.Direction.LeftToRight;
            ammoStatusSlider.interactable = false;
            ammoStatusSlider.transition = Selectable.Transition.None;
            ammoStatusSlider.value = normalized;
        }

        if (ammoStatusFillImage != null)
        {
            ammoStatusFillImage.color = normalized > 0f && normalized <= AmmoStatusLowFillThreshold
                ? AmmoStatusLowFillColor
                : ammoStatusNormalFillColor;
        }

        ammoStatusBar.gameObject.SetActive(true);
        RefreshStatusBarAnchor();
    }

    private Image ResolveAmmoStatusFillImage()
    {
        if (ammoStatusSlider != null && ammoStatusSlider.fillRect != null)
            return ammoStatusSlider.fillRect.GetComponent<Image>();

        if (ammoStatusBar == null)
            return null;

        Image[] images = ammoStatusBar.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].name.ToLowerInvariant().Contains("fill"))
                return images[i];
        }

        return null;
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
