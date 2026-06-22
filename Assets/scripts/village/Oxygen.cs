using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class Oxygen : MonoBehaviour
{
    [System.Serializable]
    public class LevelData
    {
        public Sprite sprite;
        public int oxygenPrice = 10;
        public int energyUsage = 1;
        public int oxygenProduction = 10;
        public Oxygen upgradePrefab;
    }

    [SerializeField] private string oxygenId;
    [SerializeField] private string slotId;
    [SerializeField] private int level = 1;
    [SerializeField] private LevelData level1 = new LevelData();
    [SerializeField] private LevelData level2 = new LevelData();
    [SerializeField] private LevelData level3 = new LevelData();
    [SerializeField] private GameObject exclamationPrefab;
    [SerializeField] private Animator animator;
    [SerializeField] private float productionInterval = 10f;
    [SerializeField] private Vector2 bottomLocalPosition;

    private int storedOxygen;
    private Coroutine productionRoutine;
    private GameObject exclamationInstance;

    public string OxygenId => oxygenId;
    public string SlotId => slotId;
    public int Level => level;
    public Vector2 BottomLocalPosition => bottomLocalPosition;
    public Sprite CurrentSprite => GetLevelData(level).sprite;
    public int CurrentOxygenPrice => GetLevelData(level).oxygenPrice;
    public int CurrentEnergyUsage => GetLevelData(level).energyUsage;
    public Oxygen UpgradePrefab => GetLevelData(level).upgradePrefab;

    private void Start()
    {
        RestartProduction();
        PushState();
    }

    private void OnDisable()
    {
        if (productionRoutine != null)
        {
            StopCoroutine(productionRoutine);
            productionRoutine = null;
        }
    }

    private void OnMouseUpAsButton()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (storedOxygen > 0)
            CollectStoredOxygen();
    }

    public void AssignSlot(string nextSlotId)
    {
        slotId = nextSlotId;
    }

    public void SetLevel(int nextLevel)
    {
        level = Mathf.Clamp(nextLevel, 1, 3);
        RestartProduction();
        PushState();
    }

    public void CollectStoredOxygen()
    {
        if (storedOxygen <= 0)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
            villageManagement.AddOxygen(storedOxygen);

        storedOxygen = 0;
        UpdateExclamation();
        PushState();
    }

    public void PushState()
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null || string.IsNullOrWhiteSpace(slotId))
            return;

        villageManagement.UpsertOxygenGeneratorState(new VillageManagement.OxygenGeneratorState
        {
            slotId = slotId,
            oxygenId = oxygenId,
            level = level,
            isPlaced = true,
            isProducing = VillageManagement.Instance != null && VillageManagement.Instance.CurrentEnergy >= GetLevelData(level).energyUsage,
            storedOxygen = storedOxygen
        });
    }

    private void RestartProduction()
    {
        if (productionRoutine != null)
            StopCoroutine(productionRoutine);

        productionRoutine = StartCoroutine(ProductionRoutine());
    }

    private IEnumerator ProductionRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(productionInterval);

            VillageManagement villageManagement = VillageManagement.EnsureInstance();
            if (villageManagement == null)
                continue;

            LevelData data = GetLevelData(level);
            if (villageManagement.CurrentEnergy < data.energyUsage)
            {
                if (animator != null)
                    animator.enabled = false;
                continue;
            }

            if (animator != null)
                animator.enabled = true;

            villageManagement.TrySpendEnergy(data.energyUsage);
            storedOxygen += data.oxygenProduction;
            UpdateExclamation();
            PushState();
        }
    }

    private void UpdateExclamation()
    {
        bool shouldShow = storedOxygen > 0 && exclamationPrefab != null;
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

    private LevelData GetLevelData(int targetLevel)
    {
        if (targetLevel >= 3)
            return level3.upgradePrefab == null && level3.oxygenPrice == 0 && level3.energyUsage == 0 && level3.oxygenProduction == 0 && level3.sprite == null
                ? level2
                : level3;
        if (targetLevel == 2)
            return level2.upgradePrefab == null && level2.oxygenPrice == 0 && level2.energyUsage == 0 && level2.oxygenProduction == 0 && level2.sprite == null
                ? level1
                : level2;
        return level1;
    }
}
