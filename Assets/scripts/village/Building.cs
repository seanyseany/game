using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Building : MonoBehaviour
{
    [System.Serializable]
    public class LevelDefinition
    {
        public GameObject buildingPrefab;
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
    [SerializeField] private int currentSalary = 0;

    [Header("Level 1")]
    [SerializeField] private LevelDefinition level1 = new LevelDefinition();
    [SerializeField] private GameObject level1BossPrefab;

    [Header("Level 2")]
    [SerializeField] private LevelDefinition level2 = new LevelDefinition();

    [Header("Common")]
    [SerializeField] private Vector2 ownerLocalPosition;
    [SerializeField] private GameObject bossCustomerPointPrefab;
    [SerializeField] private GameObject customerPointPrefab;
    [SerializeField] private int energyValue = 1;
    [SerializeField] private float workTickSeconds = 5f;
    [SerializeField] private float line1LocalX = -0.6f;
    [SerializeField] private float line2LocalX = -1.2f;
    [SerializeField] private GameObject ownerPatrolFromPointPrefab;
    [SerializeField] private GameObject ownerPatrolToPointPrefab;
    [SerializeField] private Vector2 bottomLocalPosition;
    [SerializeField] private GameObject exclamationPrefab;
    [SerializeField] private GameObject[] salaryControlledPrefabs;
    [SerializeField] private string customerPointRequiredTag = "CustomerPoint";

    private const float QueueLocalY = 0f;
    private const string Line1AnchorName = "_Line1Point";
    private const string Line2AnchorName = "_Line2Point";

    private CustomerBlood counterCustomer;
    private CustomerBlood queueCustomer1;
    private CustomerBlood queueCustomer2;
    private bool serviceRunning;
    private Coroutine salaryRoutine;
    private GameObject exclamationInstance;
    private GameObject runtimeBossCustomerPointObject;
    private GameObject runtimeCustomerPointObject;
    private GameObject runtimeOwnerPatrolFromPointObject;
    private GameObject runtimeOwnerPatrolToPointObject;
    private OwnerBlood activeOwnerBlood;
    private Transform line1Point;
    private Transform line2Point;

    public string SlotId => slotId;
    public string BuildingId => buildingId;
    public int Level => level;
    public bool IsPlaced => isPlaced;
    public bool IsWorking => isPlaced && isWorking;
    public int EnergyValue => energyValue;
    public int CurrentSalary => currentSalary;
    public int MaxSalary => GetDefinitionForLevel(level).totalSalaryCapacity;
    public Transform ItemPoint => OwnerPoint;
    public Transform OwnerPoint => runtimeBossCustomerPointObject != null ? runtimeBossCustomerPointObject.transform : transform;
    public Transform CustomerPoint => runtimeCustomerPointObject != null ? runtimeCustomerPointObject.transform : transform;
    public Transform Line1Point => line1Point != null ? line1Point : transform;
    public Transform Line2Point => line2Point != null ? line2Point : transform;
    public Vector2 OwnerLocalPosition => ownerLocalPosition;
    public Vector2 OwnerPatrolFromLocalPosition => GetOrderedPatrolPoint(true);
    public Vector2 OwnerPatrolToLocalPosition => GetOrderedPatrolPoint(false);
    public Vector2 BottomLocalPosition => bottomLocalPosition;
    public Sprite Level1Sprite => GetBuildingPreviewSprite(level1);
    public Sprite Level2Sprite => GetBuildingPreviewSprite(level2) != null ? GetBuildingPreviewSprite(level2) : GetBuildingPreviewSprite(level1);
    public Sprite WorkingBloodSprite => GetWorkingBloodSprite();

    private void Awake()
    {
        EnsureRuntimeAnchors();
        EnsurePointObjects();
        UpdateAnchorPositions();
        ApplyLevelPresentation();
        BindOwnerBlood();
    }

    private void Start()
    {
        UpdateWorkingStateFromSalary(false);
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

    private void OnValidate()
    {
        level = Mathf.Clamp(level, 1, 2);
        currentSalary = Mathf.Max(0, currentSalary);
        level1.totalSalaryCapacity = Mathf.Max(0, level1.totalSalaryCapacity);
        level2.totalSalaryCapacity = Mathf.Max(0, level2.totalSalaryCapacity);

        ApplyLevelBuildingVisual();

        if (level1BossPrefab != null)
            level1BossPrefab.transform.localPosition = new Vector3(ownerLocalPosition.x, ownerLocalPosition.y, 0f);

        if (Application.isPlaying)
        {
            UpdateAnchorPositions();
            ApplyLevelPresentation();
            UpdateWorkingStateFromSalary(false);
        }
    }

    public float GetPurchaseChance()
    {
        return level >= 2 ? 0.8f : 0.6f;
    }

    public bool HasPurchasableCustomerPoint()
    {
        if (runtimeCustomerPointObject == null)
            return false;

        if (string.IsNullOrWhiteSpace(customerPointRequiredTag))
            return true;

        return runtimeCustomerPointObject.CompareTag(customerPointRequiredTag);
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
            case 30:
                return definition.salaryPrice30;
            case 60:
                return definition.salaryPrice60;
            default:
                return definition.salaryPrice100;
        }
    }

    public int GetSalaryAmountForPercent(int percent)
    {
        return Mathf.CeilToInt(GetDefinitionForLevel(level).totalSalaryCapacity * (percent / 100f));
    }

    public bool CanReceiveSalaryPercent(int percent)
    {
        int maxSalary = MaxSalary;
        return currentSalary < maxSalary && currentSalary + GetSalaryAmountForPercent(percent) <= maxSalary;
    }

    public bool TryAddSalaryPercent(int percent)
    {
        if (!CanReceiveSalaryPercent(percent))
            return false;

        currentSalary = Mathf.Clamp(currentSalary + GetSalaryAmountForPercent(percent), 0, MaxSalary);
        UpdateWorkingStateFromSalary(true);
        RestartSalaryRoutine();
        PushStateToVillageManagement();
        return true;
    }

    public bool TryEnterQueue(CustomerBlood customer, out QueueSlot slot, out Transform target)
    {
        slot = QueueSlot.None;
        target = null;

        if (customer == null || !IsPlaced || !HasPurchasableCustomerPoint())
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
        bool previousWorking = IsWorking;
        isWorking = working && currentSalary > 0;
        ToggleWorkObjects(IsWorking);
        UpdateExclamation();
        NotifyWorkStateChanged(previousWorking);
        PushStateToVillageManagement();

        if (!isWorking)
            serviceRunning = false;
        else
            TryStartService();
    }

    public void SetSalary(int current, int max)
    {
        LevelDefinition definition = GetDefinitionForLevel(level);
        definition.totalSalaryCapacity = Mathf.Max(0, max);
        currentSalary = Mathf.Clamp(current, 0, definition.totalSalaryCapacity);
        UpdateWorkingStateFromSalary(true);
        RestartSalaryRoutine();
        PushStateToVillageManagement();
    }

    public void SetLevel(int nextLevel)
    {
        level = Mathf.Clamp(nextLevel, 1, 2);
        currentSalary = Mathf.Clamp(currentSalary, 0, MaxSalary);
        ApplyLevelPresentation();
        UpdateWorkingStateFromSalary(true);
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
        UpdateWorkingStateFromSalary(true);
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
            maxSalary = MaxSalary,
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
        if (serviceRunning || !IsWorking || activeOwnerBlood == null || counterCustomer == null)
            return;

        if (!counterCustomer.IsWaitingAtCounter(this))
            return;

        serviceRunning = true;
        activeOwnerBlood.ServeCustomer(counterCustomer);
    }

    private LevelDefinition GetDefinitionForLevel(int targetLevel)
    {
        return targetLevel >= 2 ? level2 : level1;
    }

    private void ApplyLevelPresentation()
    {
        ApplyLevelBuildingVisual();
        ApplyOwnerActor();
        UpdateAnchorPositions();
    }

    private void ApplyLevelBuildingVisual()
    {
        if (level1.buildingPrefab != null)
            level1.buildingPrefab.SetActive(level >= 1);

        if (level2.buildingPrefab != null)
            level2.buildingPrefab.SetActive(level >= 2);
    }

    private void ApplyOwnerActor()
    {
        activeOwnerBlood = level1BossPrefab != null ? level1BossPrefab.GetComponentInChildren<OwnerBlood>(true) : null;

        if (level1BossPrefab != null && activeOwnerBlood == null)
            Debug.LogWarning($"Building '{name}' owner object is missing OwnerBlood component.", this);

        BindOwnerBlood();
    }

    private void BindOwnerBlood()
    {
        if (activeOwnerBlood != null)
            activeOwnerBlood.BindBuilding(this);
    }

    private void UpdateWorkingStateFromSalary(bool notifyEntranceManagement)
    {
        bool previousWorking = IsWorking;
        currentSalary = Mathf.Clamp(currentSalary, 0, MaxSalary);
        isWorking = currentSalary > 0 && isPlaced;
        ToggleWorkObjects(IsWorking);
        UpdateExclamation();
        NotifyWorkStateChanged(previousWorking, notifyEntranceManagement);

        if (!IsWorking)
            serviceRunning = false;
        else
            TryStartService();
    }

    private void ToggleWorkObjects(bool working)
    {
        if (salaryControlledPrefabs == null)
            return;

        for (int i = 0; i < salaryControlledPrefabs.Length; i++)
        {
            if (salaryControlledPrefabs[i] != null)
                salaryControlledPrefabs[i].SetActive(working);
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

    private void NotifyWorkStateChanged(bool previousWorking, bool notifyEntranceManagement = true)
    {
        if (previousWorking == IsWorking)
            return;

        if (notifyEntranceManagement)
        {
            EntranceManagement entranceManagement = FindFirstObjectByType<EntranceManagement>();
            if (entranceManagement != null)
                entranceManagement.NotifyBuildingTrafficChanged();
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
            UpdateWorkingStateFromSalary(true);
            PushStateToVillageManagement();
        }
    }

    private void EnsureRuntimeAnchors()
    {
        line1Point = FindOrCreateAnchor(Line1AnchorName);
        line2Point = FindOrCreateAnchor(Line2AnchorName);
    }

    private void EnsurePointObjects()
    {
        runtimeBossCustomerPointObject = ResolvePointObject(bossCustomerPointPrefab, "_BossCustomerPoint");
        runtimeCustomerPointObject = ResolveOptionalPointObject(customerPointPrefab, "_CustomerPoint");
        runtimeOwnerPatrolFromPointObject = ResolvePointObject(ownerPatrolFromPointPrefab, "_OwnerPatrolFromPoint");
        runtimeOwnerPatrolToPointObject = ResolvePointObject(ownerPatrolToPointPrefab, "_OwnerPatrolToPoint");
    }

    private GameObject ResolvePointObject(GameObject referenceObject, string fallbackName)
    {
        if (referenceObject != null)
            return referenceObject;

        Transform existing = transform.Find(fallbackName);
        if (existing != null)
            return existing.gameObject;

        GameObject point = new GameObject(fallbackName);
        point.transform.SetParent(transform, false);
        return point;
    }

    private GameObject ResolveOptionalPointObject(GameObject referenceObject, string fallbackName)
    {
        if (referenceObject != null)
            return referenceObject;

        Transform existing = transform.Find(fallbackName);
        return existing != null ? existing.gameObject : null;
    }

    private Transform FindOrCreateAnchor(string anchorName)
    {
        Transform anchor = transform.Find(anchorName);
        if (anchor != null)
            return anchor;

        GameObject root = new GameObject(anchorName);
        root.transform.SetParent(transform, false);
        return root.transform;
    }

    private void UpdateAnchorPositions()
    {
        if (line1Point != null)
            line1Point.localPosition = new Vector3(line1LocalX, QueueLocalY, 0f);

        if (line2Point != null)
            line2Point.localPosition = new Vector3(line2LocalX, QueueLocalY, 0f);

        if (level1BossPrefab != null)
            level1BossPrefab.transform.localPosition = new Vector3(ownerLocalPosition.x, ownerLocalPosition.y, 0f);
    }

    private Vector2 GetOrderedPatrolPoint(bool getMin)
    {
        Vector2 from = runtimeOwnerPatrolFromPointObject != null ? (Vector2)runtimeOwnerPatrolFromPointObject.transform.localPosition : Vector2.zero;
        Vector2 to = runtimeOwnerPatrolToPointObject != null ? (Vector2)runtimeOwnerPatrolToPointObject.transform.localPosition : Vector2.zero;

        return new Vector2(
            getMin ? Mathf.Min(from.x, to.x) : Mathf.Max(from.x, to.x),
            getMin ? Mathf.Min(from.y, to.y) : Mathf.Max(from.y, to.y));
    }

    private Sprite GetPrimarySprite(LevelDefinition definition)
    {
        if (definition == null || definition.buildingPrefab == null)
            return null;

        SpriteRenderer prefabSpriteRenderer = definition.buildingPrefab.GetComponentInChildren<SpriteRenderer>(true);
        return prefabSpriteRenderer != null ? prefabSpriteRenderer.sprite : null;
    }

    private Sprite GetWorkingBloodSprite()
    {
        if (level1BossPrefab != null)
        {
            SpriteRenderer prefabSpriteRenderer = level1BossPrefab.GetComponentInChildren<SpriteRenderer>(true);
            if (prefabSpriteRenderer != null)
                return prefabSpriteRenderer.sprite;
        }
        return null;
    }

    private Sprite GetBuildingPreviewSprite(LevelDefinition definition)
    {
        return GetPrimarySprite(definition);
    }
}
