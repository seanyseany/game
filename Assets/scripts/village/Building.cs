using System.Collections;
using UnityEngine;

public class Building : MonoBehaviour
{
    [System.Serializable]
    public class LevelDefinition
    {
        public GameObject visualRoot;
        public int oxygenPrice = 10;
        public float constructionTime = 3f;
        public int totalSalaryCapacity = 10;
        public int salaryPrice30 = 3;
        public int salaryPrice60 = 6;
        public int salaryPrice100 = 10;
    }

    public enum QueueSlot
    {
        None,
        Counter,
        Line1,
        Line2
    }

    [Header("Identity")]
    [SerializeField] private string slotId;
    [SerializeField] private string buildingId;

    [Header("State")]
    [SerializeField] private int level = 1;
    [SerializeField] private bool isPlaced = true;
    [SerializeField] private bool isWorking = true;
    [SerializeField] private int energyValue = 1;
    [SerializeField] private int currentSalary = 0;
    [SerializeField] private int maxSalary = 10;

    [Header("Level Data")]
    [SerializeField] private LevelDefinition level1 = new LevelDefinition();
    [SerializeField] private LevelDefinition level2 = new LevelDefinition();
    [SerializeField] private Sprite level1Sprite;
    [SerializeField] private Sprite level2Sprite;
    [SerializeField] private Sprite workingBloodSprite;
    [SerializeField] private float workTickSeconds = 5f;
    [SerializeField] private GameObject[] disableWhenSalaryEmpty;
    [SerializeField] private GameObject exclamationPrefab;

    [Header("Points")]
    [SerializeField] private Transform itemPoint;
    [SerializeField] private Transform ownerPoint;
    [SerializeField] private Transform customerPoint;
    [SerializeField] private Transform line1Point;
    [SerializeField] private Transform line2Point;
    [SerializeField] private string customerPointRequiredTag = "CustomerPoint";
    [SerializeField] private Vector2 bottomLocalPosition;

    [Header("Owner Patrol Range")]
    [SerializeField] private float ownerPatrolMinLocalX = -1f;
    [SerializeField] private float ownerPatrolMaxLocalX = 1f;

    [Header("References")]
    [SerializeField] private OwnerBlood ownerBlood;

    private CustomerBlood counterCustomer;
    private CustomerBlood queueCustomer1;
    private CustomerBlood queueCustomer2;
    private bool serviceRunning;
    private Coroutine salaryRoutine;
    private GameObject exclamationInstance;

    public string SlotId => slotId;
    public string BuildingId => buildingId;
    public int Level => level;
    public bool IsPlaced => isPlaced;
    public bool IsWorking => isPlaced && isWorking;
    public int EnergyValue => energyValue;
    public int CurrentSalary => currentSalary;
    public int MaxSalary => maxSalary;
    public Transform ItemPoint => itemPoint != null ? itemPoint : transform;
    public Transform OwnerPoint => ownerPoint != null ? ownerPoint : transform;
    public Transform CustomerPoint => customerPoint != null ? customerPoint : transform;
    public Transform Line1Point => line1Point != null ? line1Point : transform;
    public Transform Line2Point => line2Point != null ? line2Point : transform;
    public float OwnerPatrolMinLocalX => Mathf.Min(ownerPatrolMinLocalX, ownerPatrolMaxLocalX);
    public float OwnerPatrolMaxLocalX => Mathf.Max(ownerPatrolMinLocalX, ownerPatrolMaxLocalX);
    public Vector2 BottomLocalPosition => bottomLocalPosition;
    public Sprite Level1Sprite => level1Sprite;
    public Sprite Level2Sprite => level2Sprite != null ? level2Sprite : level1Sprite;
    public Sprite WorkingBloodSprite => workingBloodSprite;

    private void Awake()
    {
        if (ownerBlood == null)
            ownerBlood = GetComponentInChildren<OwnerBlood>(true);

        if (ownerBlood != null)
            ownerBlood.BindBuilding(this);
    }

    private void Start()
    {
        ApplyLevelVisuals();
        UpdateWorkingStateFromSalary();
        RestartSalaryRoutine();
        PushStateToVillageManagement();
    }

    private void OnDisable()
    {
        if (salaryRoutine != null)
        {
            StopCoroutine(salaryRoutine);
            salaryRoutine = null;
        }
    }

    public float GetPurchaseChance()
    {
        if (level >= 2)
            return 0.8f;

        return 0.6f;
    }

    public bool HasPurchasableCustomerPoint()
    {
        if (CustomerPoint == null)
            return false;

        if (string.IsNullOrWhiteSpace(customerPointRequiredTag))
            return true;

        return CustomerPoint.CompareTag(customerPointRequiredTag);
    }

    public bool HasQueueCapacity()
    {
        return counterCustomer == null || queueCustomer1 == null || queueCustomer2 == null;
    }

    public float GetConstructionTimeForLevel(int targetLevel)
    {
        return GetDefinitionForLevel(targetLevel).constructionTime;
    }

    public int GetPurchasePriceForLevel(int targetLevel)
    {
        return GetDefinitionForLevel(targetLevel).oxygenPrice;
    }

    public int GetSalaryPriceForPercent(int percent)
    {
        LevelDefinition definition = GetDefinitionForLevel(level);
        switch (percent)
        {
            case 30: return definition.salaryPrice30;
            case 60: return definition.salaryPrice60;
            default: return definition.salaryPrice100;
        }
    }

    public int GetSalaryAmountForPercent(int percent)
    {
        return Mathf.CeilToInt(GetDefinitionForLevel(level).totalSalaryCapacity * (percent / 100f));
    }

    public bool CanReceiveSalaryPercent(int percent)
    {
        return currentSalary < maxSalary && currentSalary + GetSalaryAmountForPercent(percent) <= maxSalary;
    }

    public bool TryAddSalaryPercent(int percent)
    {
        if (!CanReceiveSalaryPercent(percent))
            return false;

        currentSalary = Mathf.Clamp(currentSalary + GetSalaryAmountForPercent(percent), 0, maxSalary);
        UpdateWorkingStateFromSalary();
        RestartSalaryRoutine();
        PushStateToVillageManagement();
        return true;
    }

    public bool TryEnterQueue(CustomerBlood customer, out QueueSlot slot, out Transform target)
    {
        slot = QueueSlot.None;
        target = null;

        if (customer == null || !IsPlaced)
            return false;

        if (counterCustomer == customer)
        {
            slot = QueueSlot.Counter;
            target = CustomerPoint;
            return true;
        }

        if (queueCustomer1 == customer)
        {
            slot = QueueSlot.Line1;
            target = Line1Point;
            return true;
        }

        if (queueCustomer2 == customer)
        {
            slot = QueueSlot.Line2;
            target = Line2Point;
            return true;
        }

        if (counterCustomer == null)
        {
            counterCustomer = customer;
            slot = QueueSlot.Counter;
            target = CustomerPoint;
            return true;
        }

        if (queueCustomer1 == null)
        {
            queueCustomer1 = customer;
            slot = QueueSlot.Line1;
            target = Line1Point;
            return true;
        }

        if (queueCustomer2 == null)
        {
            queueCustomer2 = customer;
            slot = QueueSlot.Line2;
            target = Line2Point;
            return true;
        }

        return false;
    }

    public void NotifyCustomerReachedSlot(CustomerBlood customer)
    {
        if (customer == null)
            return;

        if (customer == counterCustomer)
            TryStartService();
    }

    public void NotifyCustomerLeaving(CustomerBlood customer)
    {
        if (customer == null)
            return;

        bool changed = false;

        if (counterCustomer == customer)
        {
            counterCustomer = null;
            serviceRunning = false;
            changed = true;
        }

        if (queueCustomer1 == customer)
        {
            queueCustomer1 = null;
            changed = true;
        }

        if (queueCustomer2 == customer)
        {
            queueCustomer2 = null;
            changed = true;
        }

        if (changed)
            PromoteQueue();
    }

    public void CompleteService(CustomerBlood customer)
    {
        if (counterCustomer == customer)
            counterCustomer = null;

        serviceRunning = false;
        PromoteQueue();
    }

    public void SetWorking(bool working)
    {
        isWorking = working;
        ToggleInactiveVisuals(!IsWorking);
        PushStateToVillageManagement();

        if (!isWorking)
            serviceRunning = false;
        else
            TryStartService();
    }

    public void SetSalary(int current, int max)
    {
        maxSalary = Mathf.Max(0, max);
        currentSalary = Mathf.Clamp(current, 0, maxSalary);
        UpdateWorkingStateFromSalary();
        RestartSalaryRoutine();
        PushStateToVillageManagement();
    }

    public void SetLevel(int nextLevel)
    {
        level = Mathf.Max(1, nextLevel);
        maxSalary = GetDefinitionForLevel(level).totalSalaryCapacity;
        currentSalary = Mathf.Clamp(currentSalary, 0, maxSalary);
        ApplyLevelVisuals();
        UpdateWorkingStateFromSalary();
        RestartSalaryRoutine();
        PushStateToVillageManagement();
    }

    public void AssignSlot(string nextSlotId)
    {
        slotId = nextSlotId;
    }

    public void MarkPlaced(bool placed)
    {
        isPlaced = placed;
        UpdateWorkingStateFromSalary();
        PushStateToVillageManagement();
    }

    public void PushStateToVillageManagement()
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null || string.IsNullOrWhiteSpace(slotId))
            return;

        villageManagement.UpsertBuildingState(new VillageManagement.BuildingState
        {
            slotId = slotId,
            buildingId = buildingId,
            level = level,
            currentSalary = currentSalary,
            maxSalary = maxSalary,
            isPlaced = isPlaced,
            isWorking = IsWorking,
            underConstruction = false,
            constructionRemainingSeconds = 0f
        });
    }

    private void PromoteQueue()
    {
        if (counterCustomer == null && queueCustomer1 != null)
        {
            counterCustomer = queueCustomer1;
            queueCustomer1 = queueCustomer2;
            queueCustomer2 = null;

            counterCustomer.MoveToQueueSlot(this, QueueSlot.Counter, CustomerPoint.position);

            if (queueCustomer1 != null)
                queueCustomer1.MoveToQueueSlot(this, QueueSlot.Line1, Line1Point.position);
        }

        if (queueCustomer1 == null && queueCustomer2 != null)
        {
            queueCustomer1 = queueCustomer2;
            queueCustomer2 = null;
            queueCustomer1.MoveToQueueSlot(this, QueueSlot.Line1, Line1Point.position);
        }

        TryStartService();
    }

    private void TryStartService()
    {
        if (serviceRunning || !IsWorking || ownerBlood == null || counterCustomer == null)
            return;

        if (!counterCustomer.IsWaitingAtCounter(this))
            return;

        serviceRunning = true;
        ownerBlood.ServeCustomer(counterCustomer);
    }

    private LevelDefinition GetDefinitionForLevel(int targetLevel)
    {
        return targetLevel >= 2 ? level2 : level1;
    }

    private void ApplyLevelVisuals()
    {
        if (level1.visualRoot != null)
            level1.visualRoot.SetActive(level <= 1);

        if (level2.visualRoot != null)
            level2.visualRoot.SetActive(level >= 2);
    }

    private void UpdateWorkingStateFromSalary()
    {
        maxSalary = GetDefinitionForLevel(level).totalSalaryCapacity;
        currentSalary = Mathf.Clamp(currentSalary, 0, maxSalary);
        isWorking = currentSalary > 0 && isPlaced;
        ToggleInactiveVisuals(!isWorking);
        UpdateExclamation();
    }

    private void ToggleInactiveVisuals(bool inactive)
    {
        if (disableWhenSalaryEmpty == null)
            return;

        for (int i = 0; i < disableWhenSalaryEmpty.Length; i++)
        {
            if (disableWhenSalaryEmpty[i] != null)
                disableWhenSalaryEmpty[i].SetActive(!inactive);
        }
    }

    private void UpdateExclamation()
    {
        bool shouldShow = currentSalary <= 0 && exclamationPrefab != null;
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

    private void RestartSalaryRoutine()
    {
        if (salaryRoutine != null)
        {
            StopCoroutine(salaryRoutine);
            salaryRoutine = null;
        }

        if (workTickSeconds > 0f)
            salaryRoutine = StartCoroutine(SalaryDrainRoutine());
    }

    private IEnumerator SalaryDrainRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(workTickSeconds);

            if (!isPlaced || currentSalary <= 0)
                continue;

            currentSalary = Mathf.Max(0, currentSalary - 1);
            UpdateWorkingStateFromSalary();
            PushStateToVillageManagement();
        }
    }
}
