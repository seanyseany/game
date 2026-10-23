using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class Building : MonoBehaviour
{
    [System.Serializable]
    public class LevelDefinition
    {
        public GameObject buildingPrefab;
        [Tooltip("건물 구매 또는 업그레이드에 사용하는 산소 비용입니다.")]
        [FormerlySerializedAs("oxygenPrice")]
        public int purchaseOxygenCost = 10;
        public float constructionTime = 3f;
        [Tooltip("상자 배달로 채우는 재고 게이지의 최대값입니다. 1레벨은 배달당 1/6, 2레벨은 1/3이 채워집니다.")]
        [FormerlySerializedAs("totalSalaryCapacity")]
        public int boxStockCapacity = 10;
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
    [FormerlySerializedAs("currentSalary")]
    [SerializeField] private float currentBoxStock = 0f;

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
    [Header("산소 생성")]
    [Tooltip("고객 거래를 한 번 완료할 때 마을에 지급하는 산소량입니다.")]
    [FormerlySerializedAs("energyValue")]
    [FormerlySerializedAs("oxygenValue")]
    [SerializeField] private int oxygenProducedPerTrade = 1;
    [Header("상자 재고 소모")]
    [SerializeField, Tooltip("배치된 건물의 재고를 1씩 소모하는 주기입니다. 0이면 시간에 따른 소모를 중지합니다.")]
    [FormerlySerializedAs("level1WorkTickSeconds")]
    private float level1StockConsumptionIntervalSeconds = 5f;
    [SerializeField, Tooltip("배치된 건물의 재고를 1씩 소모하는 주기입니다. 0이면 시간에 따른 소모를 중지합니다.")]
    [FormerlySerializedAs("level2WorkTickSeconds")]
    private float level2StockConsumptionIntervalSeconds = 5f;
    [SerializeField]
    [FormerlySerializedAs("customerSalaryCost")]
    private int stockConsumedPerCustomer = 1;
    [Header("Queue Points")]
    [SerializeField] private string customerPointRequiredTag = "CustomerPoint";
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
    [Header("상자 재고 게이지")]
    [FormerlySerializedAs("salaryControlledPrefabs")]
    [SerializeField] private GameObject[] stockControlledPrefabs;
    [FormerlySerializedAs("salaryStatusBar")]
    [SerializeField] private Canvas boxStockGauge;
    [FormerlySerializedAs("salaryStatusBarLevel1Anchor")]
    [SerializeField] private Transform boxStockGaugeLevel1Anchor;
    [FormerlySerializedAs("salaryStatusBarLevel2Anchor")]
    [SerializeField] private Transform boxStockGaugeLevel2Anchor;

    private const float QueueLocalY = 0f;
    private const string Line1AnchorName = "_Line1Point";
    private const string Line2AnchorName = "_Line2Point";
    private const float DragScaleMultiplier = 1.2f;
    private const float HoldDurationSeconds = 1.4f;
    private const float DragPreparationLeadTimeSeconds = 0.15f;
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
    private Vector3 initialScale;
    private Vector3 dragPointerOffset;
    private Path currentPath;
    private Path dragOriginPath;
    private Vector3 dragOriginWorldPosition;
    private float pointerDownStartedAt = -1f;
    private int activeTouchId = -1;
    private Vector2 lastPointerScreenPosition;
    private bool pointerHeld;
    private bool isDragging;
    private bool dragPreparationActive;
    private string originalSortingLayerName;
    private int originalSortingOrder;
    private float lastDragFinishedAt = -10f;
    private float customerPurchaseBlockedUntil = -10f;
    private Slider salaryStatusSlider;
    private Image salaryStatusFillImage;

    private static readonly Color SalaryLowFillColor = new Color(0.9f, 0.18f, 0.16f, 1f);
    private const float SalaryLowFillThreshold = 1f / 3f;
    private Color salaryStatusNormalFillColor = Color.green;

    internal static readonly System.Collections.Generic.List<Building> DeliveryBuildings = new System.Collections.Generic.List<Building>();

    public string SlotId => slotId;
    public string BuildingId => SanitizeId(name);
    public string DisplayName => SanitizeDisplayName(name);
    public int Level => level;
    public bool IsPlaced => isPlaced;
    public bool IsWorking => isPlaced && isWorking;
    public int OxygenValue => oxygenProducedPerTrade;
    public float CurrentSalary => currentBoxStock;
    public int MaxSalary => GetDefinitionForLevel(level).boxStockCapacity;
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

    private void OnEnable()
    {
        if (!DeliveryBuildings.Contains(this)) DeliveryBuildings.Add(this);
    }

    private void OnDisable()
    {
        DeliveryBuildings.Remove(this);
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
        currentBoxStock = Mathf.Max(0, currentBoxStock);
        level1.boxStockCapacity = Mathf.Max(0, level1.boxStockCapacity);
        level2.boxStockCapacity = Mathf.Max(0, level2.boxStockCapacity);
        oxygenProducedPerTrade = Mathf.Max(0, oxygenProducedPerTrade);
        level1StockConsumptionIntervalSeconds = Mathf.Max(0f, level1StockConsumptionIntervalSeconds);
        level2StockConsumptionIntervalSeconds = Mathf.Max(0f, level2StockConsumptionIntervalSeconds);
        stockConsumedPerCustomer = Mathf.Max(0, stockConsumedPerCustomer);
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
        TryBeginTouchHold();

        if ((pointerHeld || isDragging) && !IsPointerStillPressed())
        {
            ReleasePointerHold();
        }

        if (pointerHeld && !isDragging && !dragPreparationActive && Time.unscaledTime - pointerDownStartedAt >= DragPreparationLeadTimeSeconds)
            BeginDragPreparation();

        if (pointerHeld && !isDragging && Time.unscaledTime - pointerDownStartedAt >= HoldDurationSeconds)
            BeginDrag();

        if (isDragging)
            UpdateDragPosition();
    }

    private void OnMouseDown()
    {
        // Touch is handled directly, independent of mouse emulation settings.
        if (Input.touchCount > 0)
            return;

        TryBeginPointerHold(Input.mousePosition, -1);
    }

    private bool TryBeginPointerHold(Vector2 screenPosition, int touchId)
    {
        if (pointerHeld || isDragging || VillagePointerCapture.HasActiveCapture || !CanStartPointerInteraction(screenPosition))
            return false;

        currentPath = GetComponentInParent<Path>();
        VillagePointerCapture.Acquire(this);
        activeTouchId = touchId;
        lastPointerScreenPosition = screenPosition;
        pointerHeld = true;
        pointerDownStartedAt = Time.unscaledTime;
        dragOriginPath = currentPath;
        dragOriginWorldPosition = transform.position;
        return true;
    }

    private void TryBeginTouchHold()
    {
        if (pointerHeld || isDragging || Input.touchCount == 0)
            return;

        Camera cameraRef = Camera.main;
        if (cameraRef == null)
            return;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase != TouchPhase.Began)
                continue;

            if (FindBuildingAtScreenPoint(touch.position, cameraRef) == this && TryBeginPointerHold(touch.position, touch.fingerId))
                return;
        }
    }

    private static Building FindBuildingAtScreenPoint(Vector2 screenPosition, Camera cameraRef)
    {
        Ray ray = cameraRef.ScreenPointToRay(screenPosition);
        foreach (RaycastHit2D hit in Physics2D.GetRayIntersectionAll(ray, Mathf.Infinity, cameraRef.cullingMask))
        {
            Building building = hit.collider.GetComponentInParent<Building>();
            if (building != null && building.isActiveAndEnabled && building.IsPlaced)
                return building;
        }
        return null;
    }

    private void OnMouseUp()
    {
        if (activeTouchId < 0)
            ReleasePointerHold();
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
        return counterCustomer == null;
    }

    public bool IsAvailableForCustomerPurchases()
    {
        if (!IsWorking || !HasPurchasableCustomerPoint())
            return false;

        if (isDragging || pointerHeld || dragPreparationActive)
            return false;

        return Time.time >= customerPurchaseBlockedUntil;
    }

    public float GetConstructionTimeForLevel(int targetLevel)
    {
        return GetDefinitionForLevel(targetLevel).constructionTime;
    }

    public int GetPurchasePriceForLevel(int targetLevel)
    {
        return GetDefinitionForLevel(targetLevel).purchaseOxygenCost;
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

        if (counterCustomer == null)
        {
            counterCustomer = customer;
            slot = QueueSlot.Counter;
            target = CustomerPoint;
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

    public void AwardTradeOxygen()
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null && oxygenProducedPerTrade > 0)
            villageManagement.AddOxygen(oxygenProducedPerTrade);

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
        isWorking = working && currentBoxStock > 0;
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

    public void SetSalary(float current, int max)
    {
        LevelDefinition definition = GetDefinitionForLevel(level);
        definition.boxStockCapacity = Mathf.Max(0, max);
        currentBoxStock = Mathf.Clamp(current, 0, definition.boxStockCapacity);
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

        int clampedLevel = Mathf.Clamp(nextLevel, 1, 2);
        level = clampedLevel;
        currentBoxStock = Mathf.Clamp(currentBoxStock, 0, MaxSalary);
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

    public bool TryReceiveDelivery()
    {
        if (!CanRefillGauge())
            return false;

        AddGauge(MaxSalary / (level >= 2 ? 3f : 6f), true);
        return true;
    }

    private bool CanRefillGauge()
    {
        if (!isActiveAndEnabled || !isPlaced)
            return false;

        if (currentPath == null)
            currentPath = GetComponentInParent<Path>();

        return currentPath == null || (!currentPath.HasActiveConstruction && currentPath.Building == this);
    }

    private void AddGauge(float amount, bool immediate = false)
    {
        if (amount <= 0f || currentBoxStock >= MaxSalary)
            return;

        currentBoxStock = Mathf.Clamp(currentBoxStock + amount, 0f, MaxSalary);
        if (Mathf.Approximately(currentBoxStock, MaxSalary))
            currentBoxStock = MaxSalary;
        UpdateWorkingStateFromSalary(true);
        RefreshStatusBar();
        PushStateToVillageManagement(immediate);
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
            currentSalary = currentBoxStock,
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
        TryStartService();
    }

    private bool CanStartPointerInteraction(Vector2 screenPosition)
    {
        if (Tycoon.DeliveryRuntime.BlocksWorldInput)
            return false;

        if (!isPlaced)
            return false;

        if (EventSystem.current != null)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPosition }, hits);
            foreach (RaycastResult hit in hits)
            {
                // Physics raycasts and this building's gauge are not blocking menus.
                if (hit.module is GraphicRaycaster && !hit.gameObject.transform.IsChildOf(transform))
                    return false;
            }
        }

        return true;
    }

    private void BeginDrag()
    {
        if (isDragging || currentPath == null)
            return;

        isDragging = true;
        dragPreparationActive = false;
        customerPurchaseBlockedUntil = Mathf.Max(customerPurchaseBlockedUntil, Time.time + CustomerPurchaseReentryBlockSeconds);
        transform.localScale = initialScale * DragScaleMultiplier;
        RaiseSortingForDrag();
        Vector3 pointerWorld = GetPointerWorldPosition();
        dragPointerOffset = transform.position - pointerWorld;
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
        dragPreparationActive = false;
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
        if (wasDragging)
            UpdateDragPosition();
        pointerHeld = false;
        pointerDownStartedAt = -1f;
        activeTouchId = -1;

        if (!wasDragging)
            EndDragPreparation();

        if (wasDragging)
            FinishDrag();
    }

    private void BeginDragPreparation()
    {
        if (dragPreparationActive)
            return;

        dragPreparationActive = true;
        customerPurchaseBlockedUntil = Mathf.Max(
            customerPurchaseBlockedUntil,
            Time.time + HoldDurationSeconds + CustomerPurchaseReentryBlockSeconds);
        CancelWaitingCustomersForRelocation();
    }

    private void EndDragPreparation()
    {
        dragPreparationActive = false;
    }

    private Vector3 GetCurrentPointerScreenPosition()
    {
        if (activeTouchId >= 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.fingerId == activeTouchId)
                {
                    lastPointerScreenPosition = touch.position;
                    break;
                }
            }
            return lastPointerScreenPosition;
        }

        return Input.mousePosition;
    }

    private bool IsPointerStillPressed()
    {
        if (activeTouchId >= 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.fingerId == activeTouchId)
                    return touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
            }
            return false;
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
        currentBoxStock = Mathf.Clamp(currentBoxStock, 0, MaxSalary);
        isWorking = currentBoxStock > 0 && isPlaced;

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
        if (stockControlledPrefabs == null)
            return;

        for (int i = 0; i < stockControlledPrefabs.Length; i++)
        {
            GameObject target = stockControlledPrefabs[i];
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

    private void ResolveStatusBar()
    {
        if (boxStockGauge == null)
            boxStockGauge = GetComponentInChildren<Canvas>(true);

        salaryStatusSlider = boxStockGauge != null ? boxStockGauge.GetComponentInChildren<Slider>(true) : null;
        salaryStatusFillImage = ResolveSalaryStatusFillImage();
        if (salaryStatusFillImage != null)
            salaryStatusNormalFillColor = salaryStatusFillImage.color;

        RefreshStatusBarAnchor();
    }

    private void RefreshStatusBarAnchor()
    {
        if (boxStockGauge == null)
            return;

        Transform anchor = level >= 2 && boxStockGaugeLevel2Anchor != null
            ? boxStockGaugeLevel2Anchor
            : boxStockGaugeLevel1Anchor;

        if (anchor != null)
        {
            boxStockGauge.transform.position = anchor.position;
            boxStockGauge.transform.rotation = Quaternion.identity;
        }
    }

    private void RefreshStatusBar()
    {
        if (boxStockGauge == null)
            ResolveStatusBar();

        if (boxStockGauge == null)
            return;

        float normalized = MaxSalary > 0 ? Mathf.Clamp01((float)currentBoxStock / MaxSalary) : 0f;
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

        boxStockGauge.gameObject.SetActive(normalized > 0f || !Application.isPlaying);
        RefreshStatusBarAnchor();
    }

    private Image ResolveSalaryStatusFillImage()
    {
        if (salaryStatusSlider != null && salaryStatusSlider.fillRect != null)
            return salaryStatusSlider.fillRect.GetComponent<Image>();

        if (boxStockGauge == null)
            return null;

        Image[] images = boxStockGauge.GetComponentsInChildren<Image>(true);
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

            if (!isPlaced || currentBoxStock <= 0)
                continue;

            currentBoxStock = Mathf.Max(0, currentBoxStock - 1);
            UpdateWorkingStateFromSalary(true);
            RefreshStatusBar();
            PushStateToVillageManagement();
        }
    }

    private float GetWorkTickSecondsForCurrentLevel()
    {
        return level >= 2 ? level2StockConsumptionIntervalSeconds : level1StockConsumptionIntervalSeconds;
    }

    private void ConsumeSalaryForCustomer()
    {
        int amount = Mathf.Max(0, stockConsumedPerCustomer);
        if (amount <= 0 || currentBoxStock <= 0)
            return;

        currentBoxStock = Mathf.Max(0, currentBoxStock - amount);
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
