using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class CustomerBlood : MonoBehaviour
{
    private static readonly System.Collections.Generic.HashSet<CustomerBlood> ActiveCustomers =
        new System.Collections.Generic.HashSet<CustomerBlood>();

    private enum VisualState
    {
        Walking,
        ReceivingItem,
        CarryingItem
    }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;
    private float walkStretchAmount = 0.03f;
    [SerializeField] private float walkStretchFrequency = 6f;
    [SerializeField] private Vector2 heldItemLocalPosition = new Vector2(0.2f, 0.2f);
    [SerializeField] private Vector2 receiveItemLocalPosition = new Vector2(0.1f, 0.2f);
    [SerializeField] private float roamPauseMin = 0f;
    [SerializeField] private float roamPauseMax = 0f;

    [Header("Sprites")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite receiveItemSprite;
    [SerializeField] private Sprite carryingWalkSprite;
    [SerializeField] private Sprite walkingSprite;
    [SerializeField] private float receiveItemSpawnDelay = 0.2f;
    [SerializeField] private float receiveToCarryDelay = 0.3f;
    [SerializeField] private float purchasePassDistance = 0.35f;
    [SerializeField] private int sortingBaseOrder = 145;
    [SerializeField] private float sortingOrderScale = 10f;
    [SerializeField] private int minimumBodySortingOrder = 100;
    [SerializeField] private int maximumBodySortingOrder = 190;

    private Rigidbody2D body;
    private SortingGroup sortingGroup;
    private Coroutine lifeRoutine;
    private Coroutine moveRoutine;
    private EntranceManagement ownerEntranceManagement;
    private Entrance sourceEntrance;
    private Way currentWay;
    private Path currentPath;
    private Building targetBuilding;
    private Building pendingPurchaseBuilding;
    private CustomerBlood sourcePrefab;
    private Building.QueueSlot currentQueueSlot = Building.QueueSlot.None;
    private GameObject heldItemInstance;
    private SpriteRenderer heldItemSpriteRenderer;
    private SpriteRenderer[] heldItemRenderers;
    private bool facingLeft = true;
    private bool waitingAtCounter;
    private bool purchaseFinished;
    private bool transitioningToCarry;
    private bool purchaseSequenceRunning;
    private int purchaseReceiveRoutineVersion;
    private string spawnEntryId;
    private float fixedZ;
    private float lifeEndTime;
    private int routeSequenceIndex = int.MinValue;
    private int currentRouteNodeIndex = -1;
    private int routeTravelDirection = 1;
    private Vector3 visualBaseScale = Vector3.one;
    private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
    private Transform activeMoveTargetTransform;
    private Vector3 activeMoveTargetPosition;
    public string SpawnEntryId => spawnEntryId;
    public CustomerBlood SourcePrefab => sourcePrefab;
    public float ReceiveItemSpawnDelay => receiveItemSpawnDelay;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        EnsureSortingGroup();

        body = GetComponent<Rigidbody2D>();
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        fixedZ = transform.position.z;

        if (spriteRenderer != null)
            visualBaseScale = spriteRenderer.transform.localScale;

        UpdateSortingOrders();
    }

    private void OnEnable()
    {
        ActiveCustomers.Add(this);
    }

    private void OnDisable()
    {
        ActiveCustomers.Remove(this);
    }

    public static void CancelBuildingInteractions(Building building)
    {
        if (building == null || ActiveCustomers.Count == 0)
            return;

        CustomerBlood[] customers = new CustomerBlood[ActiveCustomers.Count];
        ActiveCustomers.CopyTo(customers);
        for (int i = 0; i < customers.Length; i++)
        {
            if (customers[i] != null)
                customers[i].CancelBuildingWaitAndResumeWay(building);
        }
    }

    public void InitializeSpawn(
        string entryId,
        EntranceManagement entranceManagement,
        Entrance entrance,
        Way way,
        Path path,
        CustomerBlood prefabSource,
        int selectedRouteSequenceIndex)
    {
        ResetState();

        spawnEntryId = entryId;
        ownerEntranceManagement = entranceManagement;
        sourceEntrance = entrance;
        currentWay = way;
        currentPath = path;
        sourcePrefab = prefabSource;
        routeSequenceIndex = selectedRouteSequenceIndex;
        targetBuilding = path != null ? path.Building : null;

        Vector3 spawnPosition = entrance != null ? entrance.SpawnWorldPosition : transform.position;
        transform.SetParent(null, true);

        transform.position = WithFixedZ(spawnPosition);
        if (body != null)
            body.position = transform.position;

        if (lifeRoutine != null)
            StopCoroutine(lifeRoutine);

        lifeRoutine = StartCoroutine(LifeCycleRoutine());
    }

    public bool IsWaitingAtCounter(Building building)
    {
        return building != null && targetBuilding == building && waitingAtCounter && currentQueueSlot == Building.QueueSlot.Counter;
    }

    public bool IsReadyToReceiveAtCounter(Building building)
    {
        if (!IsWaitingAtCounter(building) || building == null)
            return false;

        Vector3 targetPosition = building.CustomerPoint.position;
        targetPosition.z = transform.position.z;
        if (Vector3.Distance(transform.position, targetPosition) <= 0.2f)
            return true;

        return activeMoveTargetTransform == null;
    }

    public void CancelBuildingWaitAndResumeWay(Building building)
    {
        if (building == null || !IsInteractingWithBuilding(building))
            return;

        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        if (lifeRoutine != null)
        {
            StopCoroutine(lifeRoutine);
            lifeRoutine = null;
        }

        waitingAtCounter = false;
        currentQueueSlot = Building.QueueSlot.None;
        targetBuilding = null;
        pendingPurchaseBuilding = null;
        purchaseSequenceRunning = false;
        transitioningToCarry = false;
        purchaseReceiveRoutineVersion++;
        if (!purchaseFinished)
            ClearHeldItem();
        ClearActiveMoveTarget();

        if (!purchaseFinished)
            ApplyVisualState(VisualState.Walking);

        if (!isActiveAndEnabled)
            return;

        lifeRoutine = StartCoroutine(ResumeAfterBuildingCancelRoutine());
    }

    public void MoveToQueueSlot(Building building, Building.QueueSlot slot, Vector3 worldTarget)
    {
        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        targetBuilding = building;
        currentQueueSlot = slot;
        Transform queueTarget = GetQueueSlotTransform(building, slot);
        moveRoutine = queueTarget != null
            ? StartCoroutine(MoveToTransformRoutine(
                queueTarget,
                worldTarget,
                slot == Building.QueueSlot.Counter,
                building,
                building,
                slot))
            : StartCoroutine(MoveToRoutine(
                worldTarget,
                slot == Building.QueueSlot.Counter,
                building,
                building,
                slot));
    }

    public IEnumerator ReceivePurchasedItemRoutine(GameObject itemPrefab, System.Action onItemSpawned = null)
    {
        int routineVersion = ++purchaseReceiveRoutineVersion;
        waitingAtCounter = false;
        transitioningToCarry = true;
        ApplyVisualState(VisualState.ReceivingItem);

        yield return new WaitForSeconds(receiveItemSpawnDelay);

        if (routineVersion != purchaseReceiveRoutineVersion)
            yield break;

        if (itemPrefab != null && heldItemInstance == null)
        {
            heldItemInstance = VillageItemPool.Spawn(itemPrefab, transform);
            heldItemInstance.transform.localPosition = receiveItemLocalPosition;
            heldItemInstance.transform.localRotation = Quaternion.identity;
            heldItemSpriteRenderer = heldItemInstance.GetComponentInChildren<SpriteRenderer>(true);
            heldItemRenderers = heldItemInstance.GetComponentsInChildren<SpriteRenderer>(true);
        }

        UpdateHeldItemPosition(receiveItemLocalPosition);
        UpdateSortingOrders();
        onItemSpawned?.Invoke();

        yield return new WaitForSeconds(receiveToCarryDelay);

        if (routineVersion != purchaseReceiveRoutineVersion)
        {
            if (!purchaseFinished)
                ClearHeldItem();
            yield break;
        }

        purchaseFinished = true;
        transitioningToCarry = false;
        ApplyVisualState(VisualState.CarryingItem);
        UpdateHeldItemPosition(heldItemLocalPosition);
    }

    private IEnumerator LifeCycleRoutine()
    {
        float lifetime = Random.Range(15f, 20f);
        lifeEndTime = Time.time + lifetime;

        ApplyVisualState(VisualState.Walking);

        while (Time.time < lifeEndTime)
            yield return RoamUntil(lifeEndTime);

        yield return ReturnToEntranceAndDespawn();
    }

    private IEnumerator ResumeAfterBuildingCancelRoutine()
    {
        yield return ReturnToWayAndResumeRoutine();

        while (Time.time < lifeEndTime)
            yield return RoamUntil(lifeEndTime);

        yield return ReturnToEntranceAndDespawn();
    }

    private IEnumerator RoamUntil(float endTime)
    {
        if (currentPath == null)
        {
            if (currentWay == null)
            {
                yield return null;
                yield break;
            }
        }

        if (currentWay != null)
        {
            int nextRouteNodeIndex = GetNextWayNodeIndex();
            if (nextRouteNodeIndex >= 0)
                yield return MoveToRouteNodeRoutine(nextRouteNodeIndex);
            else
                yield return TravelWaySegmentRoutine(currentWay.GetRandomRoamWorldPoint());
        }
        else
        {
            yield return MoveToRoutine(currentPath.GetRandomWorldPointOnPath(), false, null);
        }

        if (Time.time < endTime)
        {
            float pauseDuration = GetRoamPauseDuration();
            if (pauseDuration > 0f)
                yield return new WaitForSeconds(pauseDuration);
        }
    }

    private IEnumerator ReturnToEntranceAndDespawn()
    {
        if (targetBuilding != null)
            targetBuilding.NotifyCustomerLeaving(this);

        yield return ReturnAlongRouteSequence();

        if (sourceEntrance != null)
            yield return MoveToRoutine(sourceEntrance.DespawnWorldPosition, false, null);
        else
            yield return MoveToRoutine(transform.position, false, null);

        ownerEntranceManagement?.RecycleCustomer(this);
    }

    private IEnumerator ReturnToWayAndResumeRoutine()
    {
        if (currentWay != null && routeSequenceIndex != int.MinValue)
        {
            int closestNodeIndex = currentWay.GetClosestRouteNodeIndex(routeSequenceIndex, transform.position);
            if (closestNodeIndex >= 0 && currentWay.TryGetRouteNode(routeSequenceIndex, closestNodeIndex, out Vector3 worldPoint))
            {
                yield return MoveToRouteNodeRoutine(closestNodeIndex);
                currentRouteNodeIndex = closestNodeIndex;
            }
        }
        else if (currentPath != null)
        {
            yield return MoveToRoutine(currentPath.GetRandomWorldPointOnPath(), false, null);
        }

        moveRoutine = null;
    }

    private IEnumerator MoveToRoutine(
        Vector3 targetPosition,
        bool idleAtEnd,
        Building notifyBuilding,
        Building expectedBuilding = null,
        Building.QueueSlot expectedSlot = Building.QueueSlot.None)
    {
        waitingAtCounter = false;
        activeMoveTargetTransform = null;
        activeMoveTargetPosition = WithFixedZ(targetPosition);

        while (Vector3.Distance(transform.position, activeMoveTargetPosition) > 0.03f)
        {
            if (expectedBuilding != null &&
                (targetBuilding != expectedBuilding || currentQueueSlot != expectedSlot))
            {
                ClearActiveMoveTarget();
                ResetWalkStretch();
                UpdateSortingOrders();
                yield break;
            }

            Vector3 next = Vector3.MoveTowards(transform.position, activeMoveTargetPosition, moveSpeed * Time.fixedDeltaTime);
            next.z = fixedZ;
            UpdateFacing(next.x - transform.position.x);
            transform.position = next;
            if (body != null)
                body.position = next;
            ApplyWalkStretch();
            UpdateSortingOrders();
            if (!purchaseFinished)
                ApplyVisualState(VisualState.Walking);
            else if (transitioningToCarry)
                ApplyVisualState(VisualState.ReceivingItem);
            else
                ApplyVisualState(VisualState.CarryingItem);
            yield return waitForFixedUpdate;
        }

        Vector3 finalPosition = WithFixedZ(activeMoveTargetPosition);
        transform.position = finalPosition;
        if (body != null)
            body.position = finalPosition;
        ClearActiveMoveTarget();
        ResetWalkStretch();
        UpdateSortingOrders();

        if (expectedBuilding != null &&
            (targetBuilding != expectedBuilding || currentQueueSlot != expectedSlot))
            yield break;

        if (idleAtEnd)
        {
            waitingAtCounter = true;
            ApplyBuildingFacingPreference();
            if (!purchaseFinished)
                ApplyVisualState(VisualState.Walking);
        }

        if (notifyBuilding != null)
            notifyBuilding.NotifyCustomerReachedSlot(this);
    }

    private IEnumerator MoveToTransformRoutine(
        Transform targetTransform,
        Vector3 fallbackWorldPosition,
        bool idleAtEnd,
        Building notifyBuilding,
        Building expectedBuilding = null,
        Building.QueueSlot expectedSlot = Building.QueueSlot.None)
    {
        waitingAtCounter = false;
        activeMoveTargetTransform = targetTransform;
        activeMoveTargetPosition = WithFixedZ(targetTransform != null ? targetTransform.position : fallbackWorldPosition);

        while (Vector3.Distance(transform.position, GetCurrentMoveTargetPosition(fallbackWorldPosition)) > 0.03f)
        {
            if (expectedBuilding != null &&
                (targetBuilding != expectedBuilding || currentQueueSlot != expectedSlot))
            {
                ClearActiveMoveTarget();
                ResetWalkStretch();
                UpdateSortingOrders();
                yield break;
            }

            Vector3 moveTarget = GetCurrentMoveTargetPosition(fallbackWorldPosition);
            Vector3 next = Vector3.MoveTowards(transform.position, moveTarget, moveSpeed * Time.fixedDeltaTime);
            next.z = fixedZ;
            UpdateFacing(next.x - transform.position.x);
            transform.position = next;
            if (body != null)
                body.position = next;
            ApplyWalkStretch();
            UpdateSortingOrders();
            if (!purchaseFinished)
                ApplyVisualState(VisualState.Walking);
            else if (transitioningToCarry)
                ApplyVisualState(VisualState.ReceivingItem);
            else
                ApplyVisualState(VisualState.CarryingItem);
            yield return waitForFixedUpdate;
        }

        Vector3 finalPosition = GetCurrentMoveTargetPosition(fallbackWorldPosition);
        transform.position = finalPosition;
        if (body != null)
            body.position = finalPosition;
        bool keepTrackingQueueTarget = expectedBuilding != null && expectedSlot != Building.QueueSlot.None;
        if (!keepTrackingQueueTarget)
            ClearActiveMoveTarget();
        ResetWalkStretch();
        UpdateSortingOrders();

        if (expectedBuilding != null &&
            (targetBuilding != expectedBuilding || currentQueueSlot != expectedSlot))
            yield break;

        if (idleAtEnd)
        {
            waitingAtCounter = true;
            ApplyBuildingFacingPreference();
            if (!purchaseFinished)
                ApplyVisualState(VisualState.Walking);
        }

        if (notifyBuilding != null)
            notifyBuilding.NotifyCustomerReachedSlot(this);
    }

    private void ResetState()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        if (lifeRoutine != null)
        {
            StopCoroutine(lifeRoutine);
            lifeRoutine = null;
        }

        if (heldItemInstance != null)
            ClearHeldItem();

        if (targetBuilding != null)
            targetBuilding.NotifyCustomerLeaving(this);

        targetBuilding = null;
        pendingPurchaseBuilding = null;
        currentWay = null;
        currentPath = null;
        sourceEntrance = null;
        ownerEntranceManagement = null;
        currentQueueSlot = Building.QueueSlot.None;
        waitingAtCounter = false;
        purchaseFinished = false;
        transitioningToCarry = false;
        purchaseSequenceRunning = false;
        purchaseReceiveRoutineVersion++;
        spawnEntryId = string.Empty;
        routeSequenceIndex = int.MinValue;
        currentRouteNodeIndex = -1;
        routeTravelDirection = 1;
        lifeEndTime = 0f;
        ClearActiveMoveTarget();
        body.linearVelocity = Vector2.zero;
        facingLeft = true;
        Vector3 angles = transform.localEulerAngles;
        angles.y = 0f;
        transform.localEulerAngles = angles;
        ResetWalkStretch();
        UpdateSortingOrders();
        ApplyVisualState(VisualState.Walking);
    }

    private Vector3 WithFixedZ(Vector3 position)
    {
        position.z = fixedZ;
        return position;
    }

    private int GetNextWayNodeIndex()
    {
        if (currentWay == null)
            return -1;

        if (currentRouteNodeIndex < 0)
            return currentWay.GetFirstRouteNodeIndex(routeSequenceIndex);

        if (routeTravelDirection >= 0)
        {
            int nextIndex = currentWay.GetNextRouteNodeIndexNoLoop(routeSequenceIndex, currentRouteNodeIndex);
            if (nextIndex >= 0)
                return nextIndex;

            routeTravelDirection = -1;
            int previousIndex = currentWay.GetPreviousRouteNodeIndexNoLoop(routeSequenceIndex, currentRouteNodeIndex);
            return previousIndex >= 0 ? previousIndex : currentRouteNodeIndex;
        }

        int reverseIndex = currentWay.GetPreviousRouteNodeIndexNoLoop(routeSequenceIndex, currentRouteNodeIndex);
        if (reverseIndex >= 0)
            return reverseIndex;

        routeTravelDirection = 1;
        int forwardIndex = currentWay.GetNextRouteNodeIndexNoLoop(routeSequenceIndex, currentRouteNodeIndex);
        return forwardIndex >= 0 ? forwardIndex : currentRouteNodeIndex;
    }

    private IEnumerator ReturnAlongRouteSequence()
    {
        if (currentWay == null || routeSequenceIndex == int.MinValue)
            yield break;

        int returnNodeIndex;
        if (currentRouteNodeIndex < 0)
        {
            returnNodeIndex = currentWay.GetClosestRouteNodeIndex(routeSequenceIndex, transform.position);
        }
        else
        {
            int nextNodeIndex = currentWay.GetNextRouteNodeIndexNoLoop(routeSequenceIndex, currentRouteNodeIndex);
            int lastNodeIndex = currentWay.GetLastRouteNodeIndex(routeSequenceIndex);
            if (nextNodeIndex >= 0)
                returnNodeIndex = nextNodeIndex;
            else
                returnNodeIndex = lastNodeIndex >= 0 ? lastNodeIndex : currentRouteNodeIndex;
        }

        while (returnNodeIndex >= 0)
        {
            yield return MoveToRouteNodeRoutine(returnNodeIndex);
            returnNodeIndex = currentWay.GetPreviousRouteNodeIndexNoLoop(routeSequenceIndex, returnNodeIndex);
        }
    }

    private IEnumerator MoveToRouteNodeRoutine(int routeNodeIndex)
    {
        if (currentWay == null || routeNodeIndex < 0)
            yield break;

        Vector3 worldPoint = transform.position;
        Transform routeNode = currentWay.GetRouteNodeTransform(routeSequenceIndex, routeNodeIndex);
        if (routeNode == null && !currentWay.TryGetRouteNode(routeSequenceIndex, routeNodeIndex, out worldPoint))
            yield break;

        Vector3 targetPoint = routeNode != null ? routeNode.position : worldPoint;
        yield return TravelWaySegmentRoutine(targetPoint);

        currentRouteNodeIndex = routeNodeIndex;
    }

    private void UpdateFacing(float deltaX)
    {
        if (Mathf.Abs(deltaX) < 0.001f)
            return;

        bool shouldFaceLeft = deltaX < 0f;
        if (facingLeft == shouldFaceLeft)
            return;

        facingLeft = shouldFaceLeft;
        Vector3 angles = transform.localEulerAngles;
        angles.y = facingLeft ? 0f : 180f;
        transform.localEulerAngles = angles;
    }

    private void ApplyBuildingFacingPreference()
    {
        if (targetBuilding == null || currentQueueSlot != Building.QueueSlot.Counter)
            return;

        facingLeft = !targetBuilding.CustomerDefaultFacesRight;
        Vector3 angles = transform.localEulerAngles;
        angles.y = facingLeft ? 0f : 180f;
        transform.localEulerAngles = angles;
    }

    private void UpdateHeldItemPosition(Vector2 localPosition)
    {
        if (heldItemInstance == null)
            return;

        heldItemInstance.transform.localPosition = localPosition;
        UpdateSortingOrders();
    }

    private void ClearHeldItem()
    {
        if (heldItemInstance != null)
        {
            VillageItemPool.Release(heldItemInstance);
            heldItemInstance = null;
        }

        heldItemSpriteRenderer = null;
        heldItemRenderers = null;
    }

    private void ApplyVisualState(VisualState state)
    {
        if (spriteRenderer == null)
            return;

        switch (state)
        {
            case VisualState.ReceivingItem:
                if (receiveItemSprite != null)
                    spriteRenderer.sprite = receiveItemSprite;
                break;
            case VisualState.CarryingItem:
                if (carryingWalkSprite != null)
                    spriteRenderer.sprite = carryingWalkSprite;
                break;
            default:
                if (walkingSprite != null)
                    spriteRenderer.sprite = walkingSprite;
                break;
        }
    }

    private void ApplyWalkStretch()
    {
        if (spriteRenderer == null)
            return;

        float stretch = Mathf.Sin(Time.time * walkStretchFrequency) * walkStretchAmount;
        spriteRenderer.transform.localScale = new Vector3(
            visualBaseScale.x * (1f - stretch),
            visualBaseScale.y * (1f + stretch),
            visualBaseScale.z);
    }

    private void ResetWalkStretch()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.transform.localScale = visualBaseScale;
    }

    private void UpdateSortingOrders()
    {
        int bodyOrder = sortingBaseOrder - Mathf.RoundToInt(transform.position.y * sortingOrderScale);
        bodyOrder = Mathf.Clamp(bodyOrder, minimumBodySortingOrder, maximumBodySortingOrder);

        if (sortingGroup != null)
        {
            if (spriteRenderer != null)
            {
                sortingGroup.sortingLayerID = spriteRenderer.sortingLayerID;
                sortingGroup.sortingLayerName = spriteRenderer.sortingLayerName;
            }

            sortingGroup.sortingOrder = bodyOrder;
        }

        if (spriteRenderer != null)
            spriteRenderer.sortingOrder = 0;

        if (heldItemRenderers != null)
        {
            for (int i = 0; i < heldItemRenderers.Length; i++)
            {
                if (heldItemRenderers[i] != null)
                    heldItemRenderers[i].sortingOrder = 1;
            }
        }
        else if (heldItemSpriteRenderer != null)
        {
            heldItemSpriteRenderer.sortingOrder = 1;
        }
    }

    private void EnsureSortingGroup()
    {
        if (sortingGroup == null)
            sortingGroup = GetComponent<SortingGroup>();

        if (sortingGroup == null)
            sortingGroup = gameObject.AddComponent<SortingGroup>();

        if (spriteRenderer != null)
            sortingGroup.sortingLayerID = spriteRenderer.sortingLayerID;
    }

    private float GetRoamPauseDuration()
    {
        float minPause = Mathf.Max(0f, roamPauseMin);
        float maxPause = Mathf.Max(minPause, roamPauseMax);
        return maxPause <= 0f ? 0f : Random.Range(minPause, maxPause);
    }

    private IEnumerator TravelWaySegmentRoutine(Vector3 targetPoint)
    {
        Vector3 segmentStart = transform.position;
        targetPoint = WithFixedZ(targetPoint);

        if (!purchaseFinished &&
            !transitioningToCarry &&
            !purchaseSequenceRunning &&
            TryChoosePurchaseTargetAlongSegment(segmentStart, targetPoint, out Building building, out Path path, out Vector3 branchPoint))
        {
            pendingPurchaseBuilding = building;
            yield return MoveToRoutine(branchPoint, false, null);

            if (pendingPurchaseBuilding != building)
            {
                yield return MoveToRoutine(targetPoint, false, null);
                yield break;
            }

            if (building != null && building.TryEnterQueue(this, out Building.QueueSlot slot, out Transform queueTarget))
            {
                purchaseSequenceRunning = true;
                targetBuilding = building;
                pendingPurchaseBuilding = null;
                currentPath = path;
                currentQueueSlot = slot;

                while (targetBuilding == building && !purchaseFinished)
                {
                    Vector3 queueTargetPosition = GetQueueSlotWorldPosition(building, currentQueueSlot);
                    Transform queueTargetTransform = GetQueueSlotTransform(building, currentQueueSlot);
                    bool isCounterSlot = currentQueueSlot == Building.QueueSlot.Counter;

                    if (queueTargetTransform != null)
                    {
                        yield return MoveToTransformRoutine(
                            queueTargetTransform,
                            queueTargetPosition,
                            isCounterSlot,
                            isCounterSlot ? building : null,
                            building,
                            currentQueueSlot);
                    }
                    else
                    {
                        yield return MoveToRoutine(
                            queueTargetPosition,
                            isCounterSlot,
                            isCounterSlot ? building : null,
                            building,
                            currentQueueSlot);
                    }

                    if (targetBuilding != building || purchaseFinished)
                        break;

                    if (currentQueueSlot != Building.QueueSlot.Counter)
                    {
                        while (targetBuilding == building &&
                               currentQueueSlot != Building.QueueSlot.Counter &&
                               !purchaseFinished)
                            yield return null;

                        continue;
                    }

                    while (targetBuilding == building &&
                           currentQueueSlot == Building.QueueSlot.Counter &&
                           !purchaseFinished)
                        yield return null;
                }

                while (transitioningToCarry)
                    yield return null;

                if (targetBuilding == building && currentQueueSlot == Building.QueueSlot.Counter)
                    building.CompleteService(this);

                currentQueueSlot = Building.QueueSlot.None;

                yield return MoveToRoutine(branchPoint, false, null);

                if (targetBuilding == building)
                    targetBuilding = null;
                if (pendingPurchaseBuilding == building)
                    pendingPurchaseBuilding = null;
                purchaseSequenceRunning = false;
            }
            else if (pendingPurchaseBuilding == building)
            {
                pendingPurchaseBuilding = null;
            }
        }

        yield return MoveToRoutine(targetPoint, false, null);
    }

    private bool IsInteractingWithBuilding(Building building)
    {
        return targetBuilding == building || pendingPurchaseBuilding == building;
    }

    private Vector3 GetQueueSlotWorldPosition(Building building, Building.QueueSlot slot)
    {
        if (building == null)
            return transform.position;

        switch (slot)
        {
            case Building.QueueSlot.Counter:
                return building.CustomerPoint.position;
            case Building.QueueSlot.Line1:
                return building.Line1Point.position;
            case Building.QueueSlot.Line2:
                return building.Line2Point.position;
            default:
                return transform.position;
        }
    }

    private Transform GetQueueSlotTransform(Building building, Building.QueueSlot slot)
    {
        if (building == null)
            return null;

        switch (slot)
        {
            case Building.QueueSlot.Counter:
                return building.CustomerPoint;
            case Building.QueueSlot.Line1:
                return building.Line1Point;
            case Building.QueueSlot.Line2:
                return building.Line2Point;
            default:
                return null;
        }
    }

    private Vector3 GetCurrentMoveTargetPosition(Vector3 fallbackWorldPosition)
    {
        if (activeMoveTargetTransform != null)
            activeMoveTargetPosition = WithFixedZ(activeMoveTargetTransform.position);
        else
            activeMoveTargetPosition = WithFixedZ(activeMoveTargetPosition == Vector3.zero ? fallbackWorldPosition : activeMoveTargetPosition);

        return activeMoveTargetPosition;
    }

    private void ClearActiveMoveTarget()
    {
        activeMoveTargetTransform = null;
    }

    private bool TryChoosePurchaseTargetAlongSegment(Vector3 start, Vector3 end, out Building building, out Path path, out Vector3 branchPoint)
    {
        building = null;
        path = null;
        branchPoint = end;

        if (currentWay == null)
            return false;

        float detectionDistance = Mathf.Max(0.6f, purchasePassDistance);
        float detectionSqrDistance = detectionDistance * detectionDistance;
        float bestT = float.MaxValue;
        var connectedPaths = currentWay.ConnectedPaths;
        for (int i = 0; i < connectedPaths.Count; i++)
        {
            Path candidatePath = connectedPaths[i];
            if (!IsValidPurchasePath(candidatePath))
                continue;

            Building candidateBuilding = candidatePath.Building;
            if (candidateBuilding == null || Random.value > candidateBuilding.GetPurchaseChance())
                continue;

            Vector3 pathPoint = WithFixedZ(candidatePath.transform.position);
            Vector3 customerPoint = WithFixedZ(candidateBuilding.CustomerPoint.position);
            Vector3 pathProjectedPoint = GetClosestPointOnSegment(start, end, pathPoint, out float pathT);
            Vector3 customerProjectedPoint = GetClosestPointOnSegment(start, end, customerPoint, out float customerT);

            float pathDistance = (pathPoint - pathProjectedPoint).sqrMagnitude;
            float customerDistance = (customerPoint - customerProjectedPoint).sqrMagnitude;
            float candidateDistance = Mathf.Min(pathDistance, customerDistance);
            if (candidateDistance > detectionSqrDistance)
                continue;

            float t = pathDistance <= customerDistance ? pathT : customerT;
            if (t >= bestT)
                continue;

            bestT = t;
            building = candidateBuilding;
            path = candidatePath;
            branchPoint = pathDistance <= customerDistance ? pathProjectedPoint : customerProjectedPoint;
        }

        return building != null && path != null;
    }

    private static bool IsValidPurchasePath(Path path)
    {
        if (path == null || path.Building == null)
            return false;

        Building building = path.Building;
        return building.IsAvailableForCustomerPurchases();
    }

    private static Vector3 GetClosestPointOnSegment(Vector3 start, Vector3 end, Vector3 point, out float t)
    {
        Vector3 segment = end - start;
        float lengthSqr = segment.sqrMagnitude;
        if (lengthSqr <= Mathf.Epsilon)
        {
            t = 0f;
            return start;
        }

        t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSqr);
        return start + segment * t;
    }
}

public static class VillageItemPool
{
    private sealed class PoolMarker : MonoBehaviour
    {
        public GameObject sourcePrefab;
    }

    private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Queue<GameObject>> Pools =
        new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Queue<GameObject>>();

    public static GameObject Spawn(GameObject prefab, Transform parent)
    {
        if (prefab == null)
            return null;

        int key = prefab.GetInstanceID();
        if (!Pools.TryGetValue(key, out System.Collections.Generic.Queue<GameObject> pool))
        {
            pool = new System.Collections.Generic.Queue<GameObject>();
            Pools.Add(key, pool);
        }

        GameObject instance = null;
        while (pool.Count > 0 && instance == null)
            instance = pool.Dequeue();

        if (instance == null)
        {
            instance = Object.Instantiate(prefab);
            PoolMarker marker = instance.GetComponent<PoolMarker>();
            if (marker == null)
                marker = instance.AddComponent<PoolMarker>();

            marker.sourcePrefab = prefab;
        }

        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.SetActive(true);
        return instance;
    }

    public static void Release(GameObject instance)
    {
        if (instance == null)
            return;

        PoolMarker marker = instance.GetComponent<PoolMarker>();
        if (marker == null || marker.sourcePrefab == null)
        {
            Object.Destroy(instance);
            return;
        }

        int key = marker.sourcePrefab.GetInstanceID();
        if (!Pools.TryGetValue(key, out System.Collections.Generic.Queue<GameObject> pool))
        {
            pool = new System.Collections.Generic.Queue<GameObject>();
            Pools.Add(key, pool);
        }

        instance.SetActive(false);
        instance.transform.SetParent(null, false);
        pool.Enqueue(instance);
    }
}
