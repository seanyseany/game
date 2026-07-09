using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class CustomerBlood : MonoBehaviour
{
    private enum VisualState
    {
        Walking,
        ReceivingItem,
        CarryingItem
    }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private Vector2 heldItemLocalPosition = new Vector2(0.2f, 0.2f);
    [SerializeField] private Vector2 receiveItemLocalPosition = new Vector2(0.1f, 0.2f);
    [SerializeField] private float roamPauseMin = 0f;
    [SerializeField] private float roamPauseMax = 0f;

    [Header("Sprites")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite receiveItemSprite;
    [SerializeField] private Sprite carryingWalkSprite;
    [SerializeField] private Sprite walkingSprite;
    [SerializeField] private float receivingToCarryingDelay = 0.5f;

    private Rigidbody2D body;
    private Coroutine lifeRoutine;
    private Coroutine moveRoutine;
    private Coroutine visualRoutine;
    private EntranceManagement ownerEntranceManagement;
    private Entrance sourceEntrance;
    private Way currentWay;
    private Path currentPath;
    private Building targetBuilding;
    private Building.QueueSlot currentQueueSlot = Building.QueueSlot.None;
    private GameObject heldItemInstance;
    private bool facingLeft = true;
    private bool waitingAtCounter;
    private bool purchaseFinished;
    private bool transitioningToCarry;
    private string spawnEntryId;
    private float fixedZ;
    private int routeSequenceIndex = int.MinValue;
    private int currentRouteNodeIndex = -1;
    private int routeTravelDirection = 1;
    private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

    public string SpawnEntryId => spawnEntryId;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        body = GetComponent<Rigidbody2D>();
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        fixedZ = transform.position.z;
    }

    public void InitializeSpawn(
        string entryId,
        EntranceManagement entranceManagement,
        Entrance entrance,
        Way way,
        Path path,
        int selectedRouteSequenceIndex)
    {
        ResetState();

        spawnEntryId = entryId;
        ownerEntranceManagement = entranceManagement;
        sourceEntrance = entrance;
        currentWay = way;
        currentPath = path;
        routeSequenceIndex = selectedRouteSequenceIndex;
        targetBuilding = path != null ? path.Building : null;

        Vector3 spawnPosition = entrance != null ? entrance.SpawnWorldPosition : transform.position;
        transform.position = WithFixedZ(spawnPosition);

        if (lifeRoutine != null)
            StopCoroutine(lifeRoutine);

        lifeRoutine = StartCoroutine(LifeCycleRoutine());
    }

    public bool IsWaitingAtCounter(Building building)
    {
        return building != null && targetBuilding == building && waitingAtCounter && currentQueueSlot == Building.QueueSlot.Counter;
    }

    public void MoveToQueueSlot(Building building, Building.QueueSlot slot, Vector3 worldTarget)
    {
        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        targetBuilding = building;
        currentQueueSlot = slot;
        moveRoutine = StartCoroutine(MoveToRoutine(worldTarget, slot == Building.QueueSlot.Counter, null));
    }

    public void ReceivePurchasedItem(GameObject itemPrefab)
    {
        if (itemPrefab != null)
        {
            heldItemInstance = Instantiate(itemPrefab, transform);
            heldItemInstance.transform.localPosition = receiveItemLocalPosition;
            heldItemInstance.transform.localRotation = Quaternion.identity;
        }

        purchaseFinished = true;
        waitingAtCounter = false;
        transitioningToCarry = true;

        if (visualRoutine != null)
            StopCoroutine(visualRoutine);

        visualRoutine = StartCoroutine(ReceiveThenCarryRoutine());
    }

    private IEnumerator LifeCycleRoutine()
    {
        float lifetime = Random.Range(15f, 20f);
        float endTime = Time.time + lifetime;

        bool willAttemptPurchase = targetBuilding != null &&
                                   targetBuilding.IsWorking &&
                                   targetBuilding.HasPurchasableCustomerPoint() &&
                                   Random.value <= targetBuilding.GetPurchaseChance();

        if (willAttemptPurchase && targetBuilding.TryEnterQueue(this, out Building.QueueSlot slot, out Transform target))
        {
            currentQueueSlot = slot;
            yield return MoveToRoutine(target.position, slot == Building.QueueSlot.Counter, targetBuilding);

            while (!purchaseFinished && Time.time < endTime)
                yield return null;

            while (transitioningToCarry && Time.time < endTime)
                yield return null;
        }
        else
        {
            ApplyVisualState(VisualState.Walking);
            yield return RoamUntil(endTime);
        }

        while (Time.time < endTime)
            yield return RoamUntil(endTime);

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
                yield return MoveToRoutine(currentWay.GetRandomRoamWorldPoint(), false, null);
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

        Vector3 targetPosition = sourceEntrance != null ? sourceEntrance.DespawnWorldPosition : transform.position;
        yield return MoveToRoutine(targetPosition, false, null);

        ownerEntranceManagement?.NotifyCustomerDespawned(this);
        gameObject.SetActive(false);
    }

    private IEnumerator MoveToRoutine(Vector3 targetPosition, bool idleAtEnd, Building notifyBuilding)
    {
        waitingAtCounter = false;
        targetPosition = WithFixedZ(targetPosition);

        while (Vector3.Distance(transform.position, targetPosition) > 0.03f)
        {
            Vector3 next = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.fixedDeltaTime);
            next.z = fixedZ;
            UpdateFacing(next.x - transform.position.x);
            body.MovePosition(next);
            if (!purchaseFinished)
                ApplyVisualState(VisualState.Walking);
            else if (transitioningToCarry)
                ApplyVisualState(VisualState.ReceivingItem);
            else
                ApplyVisualState(VisualState.CarryingItem);
            yield return waitForFixedUpdate;
        }

        body.MovePosition(WithFixedZ(targetPosition));

        if (idleAtEnd)
        {
            waitingAtCounter = true;
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

        if (visualRoutine != null)
        {
            StopCoroutine(visualRoutine);
            visualRoutine = null;
        }

        if (heldItemInstance != null)
            Destroy(heldItemInstance);

        if (targetBuilding != null)
            targetBuilding.NotifyCustomerLeaving(this);

        targetBuilding = null;
        currentWay = null;
        currentPath = null;
        sourceEntrance = null;
        ownerEntranceManagement = null;
        currentQueueSlot = Building.QueueSlot.None;
        waitingAtCounter = false;
        purchaseFinished = false;
        transitioningToCarry = false;
        spawnEntryId = string.Empty;
        routeSequenceIndex = int.MinValue;
        currentRouteNodeIndex = -1;
        routeTravelDirection = 1;
        body.linearVelocity = Vector2.zero;
        facingLeft = true;
        Vector3 angles = transform.localEulerAngles;
        angles.y = 0f;
        transform.localEulerAngles = angles;
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

        if (!currentWay.TryGetRouteNode(routeSequenceIndex, routeNodeIndex, out Vector3 worldPoint))
            yield break;

        yield return MoveToRoutine(worldPoint, false, null);
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

    private IEnumerator ReceiveThenCarryRoutine()
    {
        ApplyVisualState(VisualState.ReceivingItem);
        UpdateHeldItemPosition(receiveItemLocalPosition);

        yield return new WaitForSeconds(receivingToCarryingDelay);

        transitioningToCarry = false;
        ApplyVisualState(VisualState.CarryingItem);
        UpdateHeldItemPosition(heldItemLocalPosition);
        visualRoutine = null;
    }

    private void UpdateHeldItemPosition(Vector2 localPosition)
    {
        if (heldItemInstance == null)
            return;

        heldItemInstance.transform.localPosition = localPosition;
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

    private float GetRoamPauseDuration()
    {
        float minPause = Mathf.Max(0f, roamPauseMin);
        float maxPause = Mathf.Max(minPause, roamPauseMax);
        return maxPause <= 0f ? 0f : Random.Range(minPause, maxPause);
    }
}
