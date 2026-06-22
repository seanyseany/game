using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider2D))]
public class Bank : MonoBehaviour
{
    [Header("Upgrade")]
    [SerializeField] private int level2OxygenPrice = 50;
    [SerializeField] private int level3OxygenPrice = 100;
    [SerializeField] private SpriteRenderer level1TankSprite;
    [SerializeField] private SpriteRenderer level2TankSprite;
    [SerializeField] private SpriteRenderer level3TankSprite;
    [SerializeField] private int level1Capacity = 100;
    [SerializeField] private int level2Capacity = 200;
    [SerializeField] private int level3Capacity = 300;
    [SerializeField] private Transform level1OxygenVisual;
    [SerializeField] private Transform level2OxygenVisual;
    [SerializeField] private Transform level3OxygenVisual;
    [SerializeField] private BankUI bankUI;

    [SerializeField] private Animator animator;
    [SerializeField] private string hitTrigger = "Hit";
    [SerializeField] private SpriteRenderer[] tintTargets;
    [SerializeField] private float flashDuration = 0.3f;

    private void OnEnable()
    {
        VillageManagement.InstanceReady += HandleVillageReady;
        if (VillageManagement.Instance != null)
            VillageManagement.Instance.SaveDataChanged += HandleSaveDataChanged;
    }

    private void OnDisable()
    {
        VillageManagement.InstanceReady -= HandleVillageReady;
        if (VillageManagement.Instance != null)
            VillageManagement.Instance.SaveDataChanged -= HandleSaveDataChanged;
    }

    private void Start()
    {
        HandleVillageReady(VillageManagement.Instance);
        RefreshVisuals();
    }

    private void OnMouseUpAsButton()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (bankUI == null)
            bankUI = FindFirstObjectByType<BankUI>();

        if (bankUI != null)
            bankUI.Open(this);
    }

    public void TakeDamage(int amount)
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
            villageManagement.SetCurrentOxygen(villageManagement.CurrentOxygen - Mathf.Max(0, amount));

        if (animator != null && !string.IsNullOrWhiteSpace(hitTrigger))
            animator.SetTrigger(hitTrigger);

        StartCoroutine(FlashRedRoutine());
    }

    public int GetUpgradePriceForLevel(int targetLevel)
    {
        switch (targetLevel)
        {
            case 2: return level2OxygenPrice;
            case 3: return level3OxygenPrice;
            default: return 0;
        }
    }

    public int GetCapacityForLevel(int targetLevel)
    {
        switch (targetLevel)
        {
            case 3: return level3Capacity;
            case 2: return level2Capacity;
            default: return level1Capacity;
        }
    }

    public bool TryUpgrade()
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return false;

        int currentLevel = villageManagement.BankLevel;
        if (currentLevel >= 3)
            return false;

        int nextLevel = currentLevel + 1;
        int price = GetUpgradePriceForLevel(nextLevel);
        if (!villageManagement.TrySpendOxygen(price))
            return false;

        villageManagement.SetBankLevel(nextLevel);
        villageManagement.SetOxygenCapacity(GetCapacityForLevel(nextLevel));
        RefreshVisuals();
        return true;
    }

    public void RefreshVisuals()
    {
        VillageManagement villageManagement = VillageManagement.Instance;
        int level = villageManagement != null ? villageManagement.BankLevel : 1;

        if (level1TankSprite != null)
            level1TankSprite.gameObject.SetActive(level >= 1);
        if (level2TankSprite != null)
            level2TankSprite.gameObject.SetActive(level >= 2);
        if (level3TankSprite != null)
            level3TankSprite.gameObject.SetActive(level >= 3);

        UpdateOxygenVisual(level1OxygenVisual, villageManagement, level1Capacity);
        UpdateOxygenVisual(level2OxygenVisual, villageManagement, level2Capacity);
        UpdateOxygenVisual(level3OxygenVisual, villageManagement, level3Capacity);
    }

    private void HandleVillageReady(VillageManagement villageManagement)
    {
        if (villageManagement == null)
            return;

        villageManagement.SaveDataChanged -= HandleSaveDataChanged;
        villageManagement.SaveDataChanged += HandleSaveDataChanged;
        villageManagement.SetOxygenCapacity(GetCapacityForLevel(villageManagement.BankLevel));
        RefreshVisuals();
    }

    private void HandleSaveDataChanged(VillageManagement.VillageSaveData _)
    {
        RefreshVisuals();
    }

    private void UpdateOxygenVisual(Transform visual, VillageManagement villageManagement, int maxVisualCapacity)
    {
        if (visual == null)
            return;

        bool active = villageManagement != null && villageManagement.BankLevel == GetLevelForCapacity(maxVisualCapacity);
        visual.gameObject.SetActive(active);
        if (!active || villageManagement == null)
            return;

        float ratio = villageManagement.OxygenCapacity > 0
            ? Mathf.Clamp01(villageManagement.CurrentOxygen / (float)villageManagement.OxygenCapacity)
            : 0f;
        Vector3 scale = visual.localScale;
        scale.y = ratio;
        visual.localScale = scale;
    }

    private int GetLevelForCapacity(int capacity)
    {
        if (capacity >= level3Capacity)
            return 3;
        if (capacity >= level2Capacity)
            return 2;
        return 1;
    }

    private IEnumerator FlashRedRoutine()
    {
        if (tintTargets == null || tintTargets.Length == 0)
            yield break;

        for (int i = 0; i < tintTargets.Length; i++)
        {
            if (tintTargets[i] != null)
                tintTargets[i].color = Color.red;
        }

        yield return new WaitForSeconds(flashDuration);

        for (int i = 0; i < tintTargets.Length; i++)
        {
            if (tintTargets[i] != null)
                tintTargets[i].color = Color.white;
        }
    }
}
