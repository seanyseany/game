using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class Oxygen : MonoBehaviour
{
    [SerializeField] private string oxygenId;
    [SerializeField] private string slotId;
    [SerializeField] private int oxygenPrice = 10;
    [SerializeField] private int energyUsage = 1;
    [SerializeField] private int oxygenProduction = 10;
    [SerializeField] private int level = 1;
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
    public int CurrentOxygenPrice => oxygenPrice;
    public int CurrentEnergyUsage => energyUsage;
    public int OxygenProduction => oxygenProduction;
    public Vector2 BottomLocalPosition => bottomLocalPosition;

    private void Awake()
    {
        level = Mathf.Clamp(level, 1, 3);
    }

    private void Start()
    {
        RestartProduction();
        UpdateProductionAnimation();
        PushState();
    }

    private void Update()
    {
        UpdateProductionAnimation();
    }

    private void OnDisable()
    {
        if (productionRoutine != null)
        {
            StopCoroutine(productionRoutine);
            productionRoutine = null;
        }

        if (animator != null)
            animator.enabled = false;
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
        UpdateProductionAnimation();
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
            isProducing = CanProduce(villageManagement),
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

            UpdateProductionAnimation();
            if (!CanProduce(villageManagement))
            {
                PushState();
                continue;
            }

            villageManagement.TrySpendEnergy(energyUsage);
            storedOxygen += oxygenProduction;
            UpdateExclamation();
            PushState();
        }
    }

    private bool CanProduce(VillageManagement villageManagement)
    {
        return villageManagement != null && villageManagement.CurrentEnergy >= energyUsage;
    }

    private void UpdateProductionAnimation()
    {
        if (animator == null)
            return;

        VillageManagement villageManagement = VillageManagement.Instance;
        animator.enabled = CanProduce(villageManagement);
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
}
