using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

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
    [SerializeField] private bool ownerDefaultFacesRight = true;
    [SerializeField] private bool customerDefaultFacesRight = true;
    [SerializeField] private GameObject itemPrefab;
    [SerializeField] private GameObject bossCustomerPointPrefab;
    [SerializeField] private GameObject customerPointPrefab;
    [Header("Trade Reward")]
    [Tooltip("Amount of total energy awarded whenever this building completes a customer transaction.")]
    [SerializeField] private int energyValue = 1;
    [SerializeField] private float level1WorkTickSeconds = 5f;
    [SerializeField] private float level2WorkTickSeconds = 5f;
    [SerializeField] private int customerSalaryCost = 1;
    [SerializeField] private float line1LocalX = -0.6f;
    [SerializeField] private float line2LocalX = -1.2f;
    [SerializeField] private GameObject line1PointPrefab;
    [SerializeField] private GameObject line2PointPrefab;
    [SerializeField] private GameObject ownerPatrolFromPointPrefab;
    [SerializeField] private GameObject ownerPatrolToPointPrefab;
    [SerializeField] private Transform bottomAnchor;
    [SerializeField] private Transform uiAnchor;
    [SerializeField] private GameObject exclamationPrefab;
    [SerializeField] private Transform exclamationAnchor;
    [SerializeField] private float exclamationDuration = 0.8f;
    [SerializeField] private float exclamationMoveSpeed = 1.2f;
    [SerializeField] private GameObject[] salaryControlledPrefabs;
    [SerializeField] private string customerPointRequiredTag = "CustomerPoint";
    [SerializeField] private Canvas salaryStatusBar;
    [SerializeField] private Transform salaryStatusBarLevel1Anchor;
    [SerializeField] private Transform salaryStatusBarLevel2Anchor;

    private const float QueueLocalY = 0f;
    private const string Line1AnchorName = "_Line1Point";
    private const string Line2AnchorName = "_Line2Point";
    private const float DragScaleMultiplier = 1.2f;
    private const float HoldDurationSeconds = 0.7f;
    private const int DragSortingOrderBoost = 1000;
    private const float CustomerPurchaseReentryBlockSeconds = 0.5f;

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
    private SortingGroup sortingGroup;
    private BuildingUI cachedBuildingUI;
    private Vector3 initialScale;
    private Vector3 dragPointerOffset;
    private Path currentPath;
    private Path dragOriginPath;
    private Vector3 dragOriginWorldPosition;
    private float pointerDownStartedAt = -1f;
    private bool tapPending;
    private bool pointerHeld;
    private bool isDragging;
    private string originalSortingLayerName;
    private int originalSortingOrder;
    private float lastDragFinishedAt = -10f;
    private float customerPurchaseBlockedUntil = -10f;
    private Slider salaryStatusSlider;
    private Image salaryStatusFillImage;

    private static readonly Color SalaryLowFillColor = new Color(0.9f, 0.18f, 0.16f, 1f);
    private const float SalaryLowFillThreshold = 1f / 3f;
    private Color salaryStatusNormalFillColor = Color.green;

    public string SlotId => slotId;
    public string BuildingId => SanitizeId(name);
    public string DisplayName => SanitizeDisplayName(name);
    public int Level => level;
    public bool IsPlaced => isPlaced;
    public bool IsWorking => isPlaced && isWorking;
    public int EnergyValue => energyValue;
    public int CurrentSalary => currentSalary;
    public int MaxSalary => GetDefinitionForLevel(level).totalSalaryCapacity;
    public GameObject ItemPrefab => itemPrefab;
    public Transform OwnerPoint => runtimeBossCustomerPointObject != null ? runtimeBossCustomerPointObject.transform : transform;
    public Transform CustomerPoint => runtimeCustomerPointObject != null ? runtimeCustomerPointObject.transform : transform;
    public Transform Line1Point => line1Point != null ? line1Point : transform;
    public Transform Line2Point => line2Point != null ? line2Point : transform;
    public Vector2 OwnerLocalPosition => ownerLocalPosition;
    public bool OwnerDefaultFacesRight => ownerDefaultFacesRight;
    public bool CustomerDefaultFacesRight => customerDefaultFacesRight;
    public Vector2 OwnerPatrolFromLocalPosition => GetOrderedPatrolPoint(true);
    public Vector2 OwnerPatrolToLocalPosition => GetOrderedPatrolPoint(false);
    public Transform BottomAnchor => bottomAnchor != null ? bottomAnchor : transform;
    public Transform UiAnchor => uiAnchor != null ? uiAnchor : transform;
    public Sprite Level1Sprite => GetBuildingPreviewSprite(level1);
    public Sprite Level2Sprite => GetBuildingPreviewSprite(level2) != null ? GetBuildingPreviewSprite(level2) : GetBuildingPreviewSprite(level1);
    public Sprite WorkingBloodSprite => GetWorkingBloodSprite();

    private void Awake()
    {
        ResolveBottomAnchor();
        ResolveUiAnchor();
        ResolveExclamationAnchor();
        ResolveStatusBar();
        EnsureInteractionCollider();
        EnsureSortingGroup();
        initialScale = transform.localScale;
        EnsureRuntimeAnchors();
        EnsurePointObjects();
        UpdateAnchorPositions();
        ApplyLevelPresentation();
        BindOwnerBlood();
        currentPath = GetComponentInParent<Path>();
    }

    private void Start()
    {
        UpdateWorkingStateFromSalary(false);
        RestartSalaryRoutine();
        RefreshStatusBar();
        PushStateToVillageManagement();
    }

    private void OnDisable()
    {
        VillagePointerCapture.Release(this);

        if (salaryRoutine != null)
        {
            StopCoroutine(salaryRoutine);
            salaryRoutine = null;
        }
    }

    private void OnValidate()
    {
        ResolveBottomAnchor();
        ResolveUiAnchor();
        ResolveExclamationAnchor();
        ResolveStatusBar();
        EnsureInteractionCollider();
        EnsureSortingGroup();
        level = Mathf.Clamp(level, 1, 2);
        currentSalary = Mathf.Max(0, currentSalary);
        level1.totalSalaryCapacity = Mathf.Max(0, level1.totalSalaryCapacity);
        level2.totalSalaryCapacity = Mathf.Max(0, level2.totalSalaryCapacity);
        energyValue = Mathf.Max(0, energyValue);
        level1WorkTickSeconds = Mathf.Max(0f, level1WorkTickSeconds);
        level2WorkTickSeconds = Mathf.Max(0f, level2WorkTickSeconds);
        customerSalaryCost = Mathf.Max(0, customerSalaryCost);
        exclamationDuration = Mathf.Max(0.05f, exclamationDuration);
        exclamationMoveSpeed = Mathf.Max(0f, exclamationMoveSpeed);

        ApplyLevelBuildingVisual();

        if (level1BossPrefab != null)
            level1BossPrefab.transform.localPosition = new Vector3(ownerLocalPosition.x, ownerLocalPosition.y, 0f);

        if (Application.isPlaying)
        {
            UpdateAnchorPositions();
            ApplyLevelPresentation();
            UpdateWorkingStateFromSalary(false);
            RefreshStatusBar();
        }
        else
        {
            RefreshStatusBarAnchor();
        }
    }

    private void Update()
    {
        RefreshStatusBarAnchor();

        if ((pointerHeld || isDragging) && !IsPointerStillPressed())
        {
            if (tapPending && !isDragging)
                TryOpenManagementUiFromTap();

            ReleasePointerHold();
        }

        if (pointerHeld && !isDragging && Time.unscaledTime - pointerDownStartedAt >= HoldDurationSeconds)
            BeginDrag();

        if (isDragging)
            UpdateDragPosition();
    }

    private void OnMouseDown()
    {
        if (!CanStartPointerInteraction())
            return;

        VillagePointerCapture.Acquire(this);
        pointerHeld = true;
        tapPending = true;
        pointerDownStartedAt = Time.unscaledTime;
        dragOriginPath = currentPath;
        dragOriginWorldPosition = transform.position;
    }

    private void OnMouseUp()
    {
        TryOpenManagementUiFromTap();
        ReleasePointerHold();
    }

    private void OnMouseUpAsButton()
    {
        TryOpenManagementUiFromTap();
    }

    public float GetPurchaseChance()
    {
        return level >= 2 ? 0.8f : 0.6f;
    }

    public bool HasPurchasableCustomerPoint()
    {
        return runtimeCustomerPointObject != null;
    }

    public bool HasQueueCapacity()
    {
        return counterCustomer == null || queueCustomer1 == null || queueCustomer2 == null;
    }

    public bool IsAvailableForCustomerPurchases()
    {
        if (!IsWorking || !HasPurchasableCustomerPoint())
            return false;

        if (isDragging)
            return false;

        return Time.time >= customerPurchaseBlockedUntil;
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
                return GetFullSalaryRefillPrice();
        }
    }

    public int GetFullSalaryRefillPrice()
    {
        LevelDefinition definition = GetDefinitionForLevel(level);
        int maxSalary = Mathf.Max(0, MaxSalary);
        if (maxSalary <= 0 || currentSalary >= maxSalary)
            return 0;

        int remainingAmount = Mathf.Max(0, maxSalary - currentSalary);
        float remainingRatio = remainingAmount / (float)maxSalary;
        return Mathf.CeilToInt(definition.salaryPrice100 * remainingRatio);
    }

    public int GetSalaryAmountForPercent(int percent)
    {
        return Mathf.CeilToInt(GetDefinitionForLevel(level).totalSalaryCapacity * GetFillFraction(percent));
    }

    public bool CanReceiveSalaryPercent(int percent)
    {
        int maxSalary = Mathf.Max(0, MaxSalary);
        if (maxSalary <= 0)
            return false;

        if (percent >= 100)
            return currentSalary < maxSalary;

        int amountToAdd = GetSalaryAmountForPercent(percent);
        return amountToAdd > 0 && currentSalary <= maxSalary - amountToAdd;
    }

    public bool TryAddSalaryPercent(int percent)
    {
        if (!CanReceiveSalaryPercent(percent))
            return false;

        if (percent >= 100)
            currentSalary = MaxSalary;
        else
            currentSalary = Mathf.Clamp(currentSalary + GetSalaryAmountForPercent(percent), 0, MaxSalary);

        UpdateWorkingStateFromSalary(true);
        RestartSalaryRoutine();
        RefreshStatusBar();
        PushStateToVillageManagement(true);
        return true;
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

    public bool TryEnterQueue(CustomerBlood customer, out QueueSlot slot, out Transform target)
    {
        slot = QueueSlot.None;
        target = null;

        if (customer == null || !IsPlaced || !HasPurchasableCustomerPoint() || isDragging)
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

        if (isDragging)
        {
            customer.CancelBuildingWaitAndResumeWay(this);
            return;
        }

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

        ConsumeSalaryForCustomer();
        serviceRunning = false;
        PromoteQueue();
    }

    public void AwardTradeEnergy()
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null && energyValue > 0)
            villageManagement.AddEnergy(energyValue);

        ShowTradeExclamation();
    }

    public void AbortService(CustomerBlood customer)
    {
        if (customer != null && counterCustomer != customer)
            return;

        serviceRunning = false;
        TryStartService();
    }

    public void SetWorking(bool working)
    {
        bool previousWorking = IsWorking;
        isWorking = working && currentSalary > 0;
        ToggleWorkObjects(IsWorking);
        UpdateExclamation();
        RefreshStatusBar();
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
        RefreshStatusBar();
        PushStateToVillageManagement();
    }

    public void SetLevel(int nextLevel)
    {
        CancelWaitingCustomersForRelocation();

        if (activeOwnerBlood != null)
            activeOwnerBlood.CancelCurrentService(false);

        level = Mathf.Clamp(nextLevel, 1, 2);
        currentSalary = Mathf.Clamp(currentSalary, 0, MaxSalary);
        ApplyLevelPresentation();
        RefreshStatusBarAnchor();
        UpdateWorkingStateFromSalary(true);
        RestartSalaryRoutine();
        RefreshStatusBar();
        PushStateToVillageManagement();
    }

    public void AssignSlot(string nextSlotId)
    {
        slotId = nextSlotId;
        currentPath = GetComponentInParent<Path>();
    }

    public void MarkPlaced(bool placed)
    {
        isPlaced = placed;
        UpdateWorkingStateFromSalary(true);
        RefreshStatusBar();
        PushStateToVillageManagement();
    }

    public void PushStateToVillageManagement(bool immediate = false)
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null || string.IsNullOrWhiteSpace(slotId))
            return;

        if (villageManagement.IsRestoreInProgress)
            return;

        if (currentPath != null && currentPath.Building != this)
            return;

        villageManagement.UpsertBuildingState(new VillageManagement.BuildingState
        {
            slotId = slotId,
            buildingId = BuildingId,
            level = level,
            currentSalary = currentSalary,
            maxSalary = MaxSalary,
            isPlaced = isPlaced,
            isWorking = IsWorking,
            underConstruction = false,
            constructionRemainingSeconds = 0f
        }, immediate);
    }

    public void RestartOwnerPatrolFromAnchor()
    {
        if (activeOwnerBlood != null)
            activeOwnerBlood.RestartPatrolFromAnchor();
    }

    public void CancelWaitingCustomersForRelocation()
    {
        if (activeOwnerBlood != null)
            activeOwnerBlood.CancelCurrentService(false);

        counterCustomer = null;
        queueCustomer1 = null;
        queueCustomer2 = null;
        serviceRunning = false;
        CustomerBlood.CancelBuildingInteractions(this);
    }

    public void PrepareForRelocation()
    {
        CancelWaitingCustomersForRelocation();
    }

    public void SnapBottomAnchorToWorld(Vector3 worldPosition)
    {
        ResolveBottomAnchor();

        Transform anchor = BottomAnchor;
        if (anchor == null)
            return;

        Vector3 delta = worldPosition - anchor.position;
        transform.position += delta;

        anchor = BottomAnchor;
        if (anchor != null)
        {
            Vector3 alignedPosition = transform.position;
            alignedPosition.x += worldPosition.x - anchor.position.x;
            alignedPosition.y += worldPosition.y - anchor.position.y;
            transform.position = alignedPosition;
        }
    }

    private void PromoteQueue()
    {
        if (queueCustomer1 == null && queueCustomer2 != null)
        {
            queueCustomer1 = queueCustomer2;
            queueCustomer2 = null;
            queueCustomer1.MoveToQueueSlot(this, QueueSlot.Line1, Line1Point.position);
        }

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

    private bool CanStartPointerInteraction()
    {
        if (!isPlaced)
            return false;

        if (EventSystem.current != null)
        {
            if (EventSystem.current.IsPointerOverGameObject())
            {
                return false;
            }
        }

        return true;
    }

    private void BeginDrag()
    {
        if (isDragging || currentPath == null)
            return;

        BuildingUI ui = GetBuildingUI();
        if (ui != null)
            ui.Close();

        isDragging = true;
        tapPending = false;
        customerPurchaseBlockedUntil = Mathf.Max(customerPurchaseBlockedUntil, Time.time + CustomerPurchaseReentryBlockSeconds);
        transform.localScale = initialScale * DragScaleMultiplier;
        RaiseSortingForDrag();
        Vector3 pointerWorld = GetPointerWorldPosition();
        dragPointerOffset = transform.position - pointerWorld;
        CancelWaitingCustomersForRelocation();
        if (activeOwnerBlood != null)
            activeOwnerBlood.LockToBuildingForDrag();
        currentPath.ReleaseBuildingReference(this, false);
    }

    private void UpdateDragPosition()
    {
        Vector3 pointerWorld = GetPointerWorldPosition();
        pointerWorld.z = transform.position.z;
        transform.position = pointerWorld + dragPointerOffset;
        if (activeOwnerBlood != null)
            activeOwnerBlood.FollowBuildingWhileDragging();
    }

    private void FinishDrag()
    {
        VillagePointerCapture.Release(this);
        isDragging = false;
        pointerHeld = false;
        pointerDownStartedAt = -1f;
        lastDragFinishedAt = Time.unscaledTime;
        customerPurchaseBlockedUntil = Mathf.Max(customerPurchaseBlockedUntil, Time.time + CustomerPurchaseReentryBlockSeconds);
        transform.localScale = initialScale;
        RestoreSortingAfterDrag();
        if (activeOwnerBlood != null)
            activeOwnerBlood.UnlockAfterDrag();

        Transform anchor = BottomAnchor;
        Vector3 dropPoint = anchor != null ? anchor.position : transform.position;
        Path occupiedTargetPath = Path.FindDirectOccupiedDropTarget(dropPoint, dragOriginPath);
        Path emptyTargetPath = Path.FindBestEmptyDropTarget(dropPoint, dragOriginPath);

        float occupiedDistance = occupiedTargetPath != null ? Vector2.Distance(dropPoint, occupiedTargetPath.transform.position) : float.MaxValue;
        float emptyDistance = emptyTargetPath != null ? Vector2.Distance(dropPoint, emptyTargetPath.transform.position) : float.MaxValue;

        if (occupiedTargetPath != null &&
            occupiedTargetPath.IsDirectDropPoint(dropPoint) &&
            occupiedDistance <= emptyDistance)
        {
            SpecialBuilding occupiedSpecialBuilding = occupiedTargetPath.SpecialBuilding;
            if (dragOriginPath != null &&
                dragOriginPath != occupiedTargetPath &&
                dragOriginPath.IsAvailableForBuildingPlacement)
            {
                Building displacedBuilding = occupiedTargetPath.DetachCurrentBuilding(false);
                if (displacedBuilding != null)
                {
                    currentPath = occupiedTargetPath;
                    occupiedTargetPath.AcceptMovedBuilding(this, null, false, false);
                    dragOriginPath.TransferActiveUpgradeTo(occupiedTargetPath, this);

                    displacedBuilding.PrepareForRelocation();
                    dragOriginPath.AcceptMovedBuilding(displacedBuilding, null, false, false);
                    occupiedTargetPath.TransferActiveUpgradeTo(dragOriginPath, displacedBuilding);

                    PushStateToVillageManagement(true);
                    displacedBuilding.PushStateToVillageManagement(true);
                    return;
                }

                if (occupiedSpecialBuilding != null)
                {
                    SpecialBuilding displacedSpecialBuilding = occupiedTargetPath.DetachCurrentSpecialBuilding();
                    if (displacedSpecialBuilding != null)
                    {
                        currentPath = occupiedTargetPath;
                        occupiedTargetPath.AcceptMovedBuilding(this, null, false, false);
                        dragOriginPath.TransferActiveUpgradeTo(occupiedTargetPath, this);
                        dragOriginPath.AcceptMovedSpecialBuilding(displacedSpecialBuilding);
                        PushStateToVillageManagement(true);
                        return;
                    }
                }
            }

            Path relocationPath = Path.FindRelocationTarget(occupiedTargetPath.transform.position, occupiedTargetPath);
            if (relocationPath != null)
            {
                Building displacedBuilding = occupiedTargetPath.DetachCurrentBuilding(false);
                if (displacedBuilding != null)
                {
                    displacedBuilding.PrepareForRelocation();
                    relocationPath.AcceptMovedBuilding(displacedBuilding, null, false, false);
                    occupiedTargetPath.TransferActiveUpgradeTo(relocationPath, displacedBuilding);

                    currentPath = occupiedTargetPath;
                    occupiedTargetPath.AcceptMovedBuilding(this, null, false, false);
                    if (dragOriginPath != null)
                    {
                        dragOriginPath.TransferActiveUpgradeTo(occupiedTargetPath, this);
                        VillageManagement villageManagement = VillageManagement.EnsureInstance();
                        if (villageManagement != null)
                            villageManagement.RemoveBuildingState(dragOriginPath.PathId, BuildingId, false);
                    }

                    PushStateToVillageManagement(true);
                    displacedBuilding.PushStateToVillageManagement(true);
                    return;
                }

                if (occupiedSpecialBuilding != null)
                {
                    SpecialBuilding displacedSpecialBuilding = occupiedTargetPath.DetachCurrentSpecialBuilding();
                    if (displacedSpecialBuilding != null)
                    {
                        displacedSpecialBuilding.PrepareForRelocation();
                        relocationPath.AcceptMovedSpecialBuilding(displacedSpecialBuilding);

                        currentPath = occupiedTargetPath;
                        occupiedTargetPath.AcceptMovedBuilding(this, null, false, false);
                        if (dragOriginPath != null)
                        {
                            dragOriginPath.TransferActiveUpgradeTo(occupiedTargetPath, this);
                            VillageManagement villageManagement = VillageManagement.EnsureInstance();
                            if (villageManagement != null)
                                villageManagement.RemoveBuildingState(dragOriginPath.PathId, BuildingId, false);
                        }

                        PushStateToVillageManagement(true);
                        return;
                    }
                }
            }
        }

        if (emptyTargetPath != null)
        {
            currentPath = emptyTargetPath;
            emptyTargetPath.AcceptMovedBuilding(this, null, false, false);
            if (dragOriginPath != null)
            {
                dragOriginPath.TransferActiveUpgradeTo(emptyTargetPath, this);
                VillageManagement villageManagement = VillageManagement.EnsureInstance();
                if (villageManagement != null)
                    villageManagement.RemoveBuildingState(dragOriginPath.PathId, BuildingId, false);
            }
            PushStateToVillageManagement(true);
            return;
        }

        if (dragOriginPath != null)
        {
            currentPath = dragOriginPath;
            dragOriginPath.AcceptMovedBuilding(this, null, false, false);
            PushStateToVillageManagement(true);
        }
        else
        {
            transform.position = dragOriginWorldPosition;
        }
    }

    private Bounds GetWorldBounds()
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        Collider2D collider2d = GetComponentInChildren<Collider2D>();
        if (collider2d != null)
            return collider2d.bounds;

        return new Bounds(transform.position, Vector3.one);
    }

    private Vector3 GetPointerWorldPosition()
    {
        Camera cameraRef = Camera.main;
        Vector3 screenPosition = GetCurrentPointerScreenPosition();
        if (cameraRef == null)
            return screenPosition;

        screenPosition.z = Mathf.Abs(cameraRef.transform.position.z - transform.position.z);
        return cameraRef.ScreenToWorldPoint(screenPosition);
    }

    private void ReleasePointerHold()
    {
        if (!pointerHeld && !isDragging)
            return;

        VillagePointerCapture.Release(this);
        bool wasDragging = isDragging;
        pointerHeld = false;
        pointerDownStartedAt = -1f;
        tapPending = false;

        if (wasDragging)
            FinishDrag();
    }

    private void TryOpenManagementUiFromTap()
    {
        if (!tapPending)
            return;

        if (isDragging || Time.unscaledTime - lastDragFinishedAt < 0.1f)
            return;

        if (Time.unscaledTime - pointerDownStartedAt >= HoldDurationSeconds)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (currentPath == null)
            currentPath = GetComponentInParent<Path>();

        if (currentPath == null)
            return;

        BuildingUI buildingUI = GetBuildingUI();
        if (buildingUI != null)
            buildingUI.Open(currentPath, this);
    }

    private static Vector3 GetCurrentPointerScreenPosition()
    {
        if (Input.touchCount > 0)
            return Input.GetTouch(0).position;

        return Input.mousePosition;
    }

    private static bool IsPointerStillPressed()
    {
        if (Input.touchCount > 0)
        {
            TouchPhase phase = Input.GetTouch(0).phase;
            return phase != TouchPhase.Ended && phase != TouchPhase.Canceled;
        }

        return Input.GetMouseButton(0);
    }

    private void EnsureInteractionCollider()
    {
        BoxCollider2D rootCollider = GetComponent<BoxCollider2D>();
        if (rootCollider == null)
            rootCollider = gameObject.AddComponent<BoxCollider2D>();

        rootCollider.isTrigger = true;
    }

    private void EnsureSortingGroup()
    {
        if (sortingGroup == null)
            sortingGroup = GetComponent<SortingGroup>();

        if (sortingGroup == null)
            sortingGroup = gameObject.AddComponent<SortingGroup>();

        sortingGroup.enabled = false;
    }

    private void RaiseSortingForDrag()
    {
        EnsureSortingGroup();
        if (sortingGroup == null)
            return;

        originalSortingLayerName = sortingGroup.sortingLayerName;
        originalSortingOrder = sortingGroup.sortingOrder;
        sortingGroup.enabled = true;
        sortingGroup.sortingOrder = originalSortingOrder + DragSortingOrderBoost;
    }

    private void RestoreSortingAfterDrag()
    {
        if (sortingGroup == null)
            return;

        sortingGroup.sortingLayerName = originalSortingLayerName;
        sortingGroup.sortingOrder = originalSortingOrder;
        sortingGroup.enabled = false;
    }

    private static string SanitizeDisplayName(string source)
    {
        string cleaned = (source ?? string.Empty).Replace("(Clone)", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Building" : cleaned;
    }

    private static string SanitizeId(string source)
    {
        return SanitizeDisplayName(source).Replace(" ", string.Empty).ToLowerInvariant();
    }

    private void ResolveBottomAnchor()
    {
        if (bottomAnchor != null)
            return;

        bottomAnchor = FindChildRecursive(transform, "BottomAnchor");

        if (bottomAnchor == null)
            Debug.LogWarning($"Building '{name}' is missing a BottomAnchor child. Placement will fall back to the root transform.", this);
    }

    private void ResolveUiAnchor()
    {
        if (uiAnchor != null)
            return;

        uiAnchor = FindChildRecursive(transform, "UiAnchor");
        if (uiAnchor == null)
            uiAnchor = FindChildRecursive(transform, "UIAnchor");
    }

    private void ResolveExclamationAnchor()
    {
        if (exclamationAnchor != null)
            return;

        exclamationAnchor = FindChildRecursive(transform, "ExclamationAnchor");
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

    private static bool IsTagDefined(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        try
        {
            GameObject probe = new GameObject("TagProbe");
            bool matches = probe.CompareTag(tagName);
            if (Application.isPlaying)
                Destroy(probe);
            else
                DestroyImmediate(probe);

            return matches;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    private void TryStartService()
    {
        if (serviceRunning || !IsWorking || activeOwnerBlood == null || counterCustomer == null || isDragging)
            return;

        if (!activeOwnerBlood.isActiveAndEnabled)
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

        if (previousWorking && !IsWorking)
            CancelWaitingCustomersForRelocation();

        ToggleWorkObjects(IsWorking);
        UpdateExclamation();
        RefreshStatusBar();
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
            GameObject target = salaryControlledPrefabs[i];
            if (target == null)
                continue;

            // Never hide the level visuals when salary reaches zero.
            if (target == level1.buildingPrefab || target == level2.buildingPrefab)
                continue;

            target.SetActive(working);
        }
    }

    private void UpdateExclamation()
    {
        if (exclamationInstance != null)
        {
            Destroy(exclamationInstance);
            exclamationInstance = null;
        }
    }

    private void ShowTradeExclamation()
    {
        if (exclamationPrefab == null)
            return;

        ResolveExclamationAnchor();
        Transform anchor = exclamationAnchor != null ? exclamationAnchor : transform;
        GameObject instance = Instantiate(exclamationPrefab, anchor.position, Quaternion.identity);
        EnergyIcon icon = instance.GetComponent<EnergyIcon>();
        if (icon == null)
            icon = instance.AddComponent<EnergyIcon>();

        icon.Play(exclamationDuration, exclamationMoveSpeed);
    }

    private void NotifyWorkStateChanged(bool previousWorking, bool notifyEntranceManagement = true)
    {
        if (previousWorking == IsWorking)
            return;

        if (notifyEntranceManagement)
        {
            EntranceManagement entranceManagement = EntranceManagement.Instance != null ? EntranceManagement.Instance : FindFirstObjectByType<EntranceManagement>();
            if (entranceManagement != null)
                entranceManagement.NotifyBuildingTrafficChanged();
        }
    }

    private BuildingUI GetBuildingUI()
    {
        if (cachedBuildingUI == null)
            cachedBuildingUI = BuildingUI.EnsureInstance();

        return cachedBuildingUI;
    }

    private void ResolveStatusBar()
    {
        if (salaryStatusBar == null)
            salaryStatusBar = GetComponentInChildren<Canvas>(true);

        salaryStatusSlider = salaryStatusBar != null ? salaryStatusBar.GetComponentInChildren<Slider>(true) : null;
        salaryStatusFillImage = ResolveSalaryStatusFillImage();
        if (salaryStatusFillImage != null)
            salaryStatusNormalFillColor = salaryStatusFillImage.color;

        RefreshStatusBarAnchor();
    }

    private void RefreshStatusBarAnchor()
    {
        if (salaryStatusBar == null)
            return;

        Transform anchor = level >= 2 && salaryStatusBarLevel2Anchor != null
            ? salaryStatusBarLevel2Anchor
            : salaryStatusBarLevel1Anchor;

        if (anchor != null)
        {
            salaryStatusBar.transform.position = anchor.position;
            salaryStatusBar.transform.rotation = Quaternion.identity;
        }
    }

    private void RefreshStatusBar()
    {
        if (salaryStatusBar == null)
            ResolveStatusBar();

        if (salaryStatusBar == null)
            return;

        float normalized = MaxSalary > 0 ? Mathf.Clamp01((float)currentSalary / MaxSalary) : 0f;
        if (salaryStatusSlider != null)
        {
            salaryStatusSlider.minValue = 0f;
            salaryStatusSlider.maxValue = 1f;
            salaryStatusSlider.wholeNumbers = false;
            salaryStatusSlider.direction = Slider.Direction.LeftToRight;
            salaryStatusSlider.interactable = false;
            salaryStatusSlider.transition = Selectable.Transition.None;
            salaryStatusSlider.value = normalized;
        }

        if (salaryStatusFillImage != null)
            salaryStatusFillImage.color = normalized > 0f && normalized <= SalaryLowFillThreshold
                ? SalaryLowFillColor
                : salaryStatusNormalFillColor;

        salaryStatusBar.gameObject.SetActive(normalized > 0f || !Application.isPlaying);
        RefreshStatusBarAnchor();
    }

    private Image ResolveSalaryStatusFillImage()
    {
        if (salaryStatusSlider != null && salaryStatusSlider.fillRect != null)
            return salaryStatusSlider.fillRect.GetComponent<Image>();

        if (salaryStatusBar == null)
            return null;

        Image[] images = salaryStatusBar.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].name.ToLowerInvariant().Contains("fill"))
                return images[i];
        }

        return null;
    }

    private void RestartSalaryRoutine()
    {
        if (salaryRoutine != null)
        {
            StopCoroutine(salaryRoutine);
            salaryRoutine = null;
        }

        if (GetWorkTickSecondsForCurrentLevel() > 0f)
            salaryRoutine = StartCoroutine(SalaryDrainRoutine());
    }

    private IEnumerator SalaryDrainRoutine()
    {
        while (true)
        {
            float tickSeconds = GetWorkTickSecondsForCurrentLevel();
            if (tickSeconds <= 0f)
            {
                yield return null;
                continue;
            }

            yield return new WaitForSeconds(tickSeconds);

            if (!isPlaced || currentSalary <= 0)
                continue;

            currentSalary = Mathf.Max(0, currentSalary - 1);
            UpdateWorkingStateFromSalary(true);
            RefreshStatusBar();
            PushStateToVillageManagement();
        }
    }

    private float GetWorkTickSecondsForCurrentLevel()
    {
        return level >= 2 ? level2WorkTickSeconds : level1WorkTickSeconds;
    }

    private void ConsumeSalaryForCustomer()
    {
        int amount = Mathf.Max(0, customerSalaryCost);
        if (amount <= 0 || currentSalary <= 0)
            return;

        currentSalary = Mathf.Max(0, currentSalary - amount);
        UpdateWorkingStateFromSalary(true);
        RestartSalaryRoutine();
        RefreshStatusBar();
        PushStateToVillageManagement();
    }

    private void EnsureRuntimeAnchors()
    {
        line1Point = ResolvePointTransform(line1PointPrefab, Line1AnchorName);
        line2Point = ResolvePointTransform(line2PointPrefab, Line2AnchorName);
    }

    private void EnsurePointObjects()
    {
        runtimeBossCustomerPointObject = ResolvePointObject(bossCustomerPointPrefab, "_BossCustomerPoint");
        runtimeCustomerPointObject = ResolvePointObject(customerPointPrefab, "_CustomerPoint");
        runtimeOwnerPatrolFromPointObject = ResolvePointObject(ownerPatrolFromPointPrefab, "_OwnerPatrolFromPoint");
        runtimeOwnerPatrolToPointObject = ResolvePointObject(ownerPatrolToPointPrefab, "_OwnerPatrolToPoint");
    }

    private GameObject ResolvePointObject(GameObject referenceObject, string fallbackName)
    {
        GameObject runtimeObject = ResolveRuntimeChildObject(referenceObject, fallbackName);
        if (runtimeObject != null)
            return runtimeObject;

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

    private Transform ResolvePointTransform(GameObject referenceObject, string fallbackName)
    {
        GameObject runtimeObject = ResolveRuntimeChildObject(referenceObject, fallbackName);
        if (runtimeObject != null)
            return runtimeObject.transform;

        if (referenceObject != null)
            return referenceObject.transform;

        Transform anchor = transform.Find(fallbackName);
        if (anchor != null)
            return anchor;

        GameObject root = new GameObject(fallbackName);
        root.transform.SetParent(transform, false);
        return root.transform;
    }

    private GameObject ResolveRuntimeChildObject(GameObject referenceObject, string fallbackName)
    {
        if (referenceObject != null)
        {
            if (referenceObject.transform.IsChildOf(transform))
                return referenceObject;

            Transform sameNamedChild = FindChildRecursive(transform, referenceObject.name);
            if (sameNamedChild != null)
                return sameNamedChild.gameObject;
        }

        Transform fallbackChild = FindChildRecursive(transform, fallbackName);
        return fallbackChild != null ? fallbackChild.gameObject : null;
    }

    private void UpdateAnchorPositions()
    {
        if (line1Point != null && line1PointPrefab == null)
            line1Point.localPosition = new Vector3(line1LocalX, QueueLocalY, 0f);

        if (line2Point != null && line2PointPrefab == null)
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
