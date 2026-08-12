using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class Lift : MonoBehaviour, IColliderPointerTarget
{
    [System.Serializable]
    private sealed class PlatformRoute
    {
        public List<Transform> movementPoints = new List<Transform>();
        public Transform[] dropPoints = new Transform[2];
    }

    private sealed class PlatformState
    {
        public int platformIndex;
        public Transform root;
        public int currentSide;
        public bool moving;
        public bool reserved;
        public CustomerBlood passenger;
        public float lastBecameIdleAt;
    }

    private static readonly List<Lift> ActiveLifts = new List<Lift>();

    private const float HoldDurationSeconds = 0.7f;
    private const float LiftDetectionDistance = 0.6f;

    [Header("Identity")]
    [SerializeField] private string liftId;

    [Header("Lift")]
    [SerializeField] private PlatformRoute[] platformRoutes = new PlatformRoute[2];
    [SerializeField] private Way[] connectedWays = new Way[2];
    [SerializeField] private GameObject platformPrefab;
    [SerializeField] private int price = 10;
    [SerializeField] private float platformMoveSpeed = 2f;
    [SerializeField] private float customerRideChance = 0.7f;

    [Header("Interaction")]
    [SerializeField] private bool allowPointerRelocation = false;

    private readonly List<PlatformState> platforms = new List<PlatformState>(2);
    private LiftSpot ownerLiftSpot;
    private bool registered;
    private bool pointerHeld;
    private bool isDragging;
    private float pointerDownStartedAt = -1f;
    private Vector3 dragOriginPosition;
    private Lift pendingRelocationTarget;
    private Coroutine pointerHoldRoutine;

    public static IReadOnlyList<Lift> RegisteredLifts => ActiveLifts;

    public string LiftId => string.IsNullOrWhiteSpace(liftId) ? name : liftId.Trim();
    public int Price => Mathf.Max(0, price);
    public bool IsOperational => isActiveAndEnabled && gameObject.activeInHierarchy;
    public bool HasPendingRelocation => pendingRelocationTarget != null;

    private void Awake()
    {
        EnsurePlatforms();
    }

    private void OnEnable()
    {
        RegisterActiveLift();
        ResetPlatformsToDropPoints();
    }

    private void OnDisable()
    {
        UnregisterActiveLift();
        StopPointerHoldRoutine();
        pointerHeld = false;
        isDragging = false;
        pendingRelocationTarget = null;
        VillagePointerCapture.Release(this);
    }

    private void OnMouseDown()
    {
        HandleColliderPointerDown();
    }

    private void OnMouseUp()
    {
        HandleColliderPointerUp();
    }

    private void OnMouseUpAsButton()
    {
        HandleColliderPointerUpAsButton();
    }

    public void AssignLiftSpot(LiftSpot liftSpot)
    {
        ownerLiftSpot = liftSpot;
    }

    public void ApplyRuntimeActive(bool active)
    {
        if (gameObject.activeSelf == active)
            return;

        gameObject.SetActive(active);
    }

    public Vector3 GetDropPointWorld(int side)
    {
        return GetDropPointWorld(-1, side);
    }

    public Vector3 GetDropPointWorld(int platformIndex, int side)
    {
        Transform dropPoint = GetDropPoint(platformIndex, side);
        return dropPoint != null ? dropPoint.position : transform.position;
    }

    public bool TryGetConnectedWay(int side, out Way way)
    {
        if (side >= 0 && side < connectedWays.Length)
        {
            way = connectedWays[side];
            return way != null;
        }

        way = null;
        return false;
    }

    public bool CanAcceptCustomersFromSide(int side, Way way)
    {
        if (!IsOperational || HasPendingRelocation || way == null)
            return false;

        if (side < 0 || side >= connectedWays.Length)
            return false;

        return connectedWays[side] == way && HasAnyDropPointForSide(side);
    }

    public void RequestRelocation(Lift targetLift)
    {
        if (targetLift == null || targetLift == this)
            return;

        pendingRelocationTarget = targetLift;
        TryCompletePendingRelocation();
    }

    public IEnumerator TransportCustomer(CustomerBlood customer, int fromSide, Vector3 pickupWorldPosition, System.Action<bool, int> onCompleted)
    {
        if (customer == null || !CanAcceptCustomersFromSide(fromSide, connectedWays[fromSide]))
        {
            onCompleted?.Invoke(false, fromSide);
            yield break;
        }

        if (AreAllPlatformsOccupiedByPassengers())
        {
            onCompleted?.Invoke(false, fromSide);
            yield break;
        }

        PlatformState platform = null;
        while (platform == null)
        {
            if (!CanAcceptCustomersFromSide(fromSide, connectedWays[fromSide]))
            {
                onCompleted?.Invoke(false, fromSide);
                yield break;
            }

            if (AreAllPlatformsOccupiedByPassengers())
            {
                onCompleted?.Invoke(false, fromSide);
                yield break;
            }

            platform = TryReservePlatform(fromSide, pickupWorldPosition);
            if (platform == null)
                yield return null;
        }

        if (platform.currentSide != fromSide)
            yield return MovePlatformToSide(platform, fromSide);

        if (!CanAcceptCustomersFromSide(fromSide, connectedWays[fromSide]))
        {
            platform.reserved = false;
            onCompleted?.Invoke(false, fromSide);
            yield break;
        }

        Vector3 boardingPoint = GetDropPointWorld(platform.platformIndex, fromSide);
        if ((boardingPoint - pickupWorldPosition).sqrMagnitude > 4f)
        {
            platform.reserved = false;
            onCompleted?.Invoke(false, fromSide);
            yield break;
        }

        yield return customer.MoveDirectToPointRoutine(boardingPoint);
        customer.AttachToCarrier(platform.root, Vector3.zero);
        platform.passenger = customer;

        int destinationSide = 1 - fromSide;
        yield return MovePlatformToSide(platform, destinationSide);

        Vector3 disembarkPoint = GetDropPointWorld(platform.platformIndex, destinationSide);
        customer.DetachFromCarrier(disembarkPoint);
        platform.passenger = null;
        platform.reserved = false;
        platform.lastBecameIdleAt = Time.time;

        onCompleted?.Invoke(true, destinationSide);
        TryCompletePendingRelocation();
    }

    public static bool TryChooseLiftAlongSegment(Way currentWay, Vector3 start, Vector3 end, Lift excludedLift, out Lift lift, out int side, out Vector3 branchPoint)
    {
        lift = null;
        side = -1;
        branchPoint = end;

        if (currentWay == null)
            return false;

        float detectionSqrDistance = LiftDetectionDistance * LiftDetectionDistance;
        float bestT = float.MaxValue;

        for (int i = 0; i < ActiveLifts.Count; i++)
        {
            Lift candidate = ActiveLifts[i];
            if (candidate == null || !candidate.IsOperational)
                continue;

            if (candidate == excludedLift)
                continue;

            for (int candidateSide = 0; candidateSide < 2; candidateSide++)
            {
                if (!candidate.CanAcceptCustomersFromSide(candidateSide, currentWay))
                    continue;

                if (Random.value > Mathf.Clamp01(candidate.customerRideChance))
                    continue;

                for (int platformIndex = 0; platformIndex < candidate.platformRoutes.Length; platformIndex++)
                {
                    Transform dropPointTransform = candidate.GetDropPoint(platformIndex, candidateSide);
                    if (dropPointTransform == null)
                        continue;

                    Vector3 dropPoint = dropPointTransform.position;
                    Vector3 projectedPoint = GetClosestPointOnSegment(start, end, dropPoint, out float t);
                    float distance = (dropPoint - projectedPoint).sqrMagnitude;
                    if (distance > detectionSqrDistance || t >= bestT)
                        continue;

                    bestT = t;
                    lift = candidate;
                    side = candidateSide;
                    branchPoint = projectedPoint;
                }
            }
        }

        return lift != null && side >= 0;
    }

    public void HandleColliderPointerDown()
    {
        if (!CanStartPointerInteraction())
            return;

        VillagePointerCapture.Acquire(this);
        pointerHeld = true;
        pointerDownStartedAt = Time.unscaledTime;
        dragOriginPosition = transform.position;
        StopPointerHoldRoutine();
        pointerHoldRoutine = StartCoroutine(PointerHoldRoutine());
    }

    public void HandleColliderPointerUp()
    {
        VillagePointerCapture.Release(this);
        FinishDrag();
        ReleasePointerState();
    }

    public void HandleColliderPointerUpAsButton()
    {
        VillagePointerCapture.Release(this);
    }

    private void RegisterActiveLift()
    {
        if (registered)
            return;

        registered = true;
        if (!ActiveLifts.Contains(this))
            ActiveLifts.Add(this);
    }

    private void UnregisterActiveLift()
    {
        registered = false;
        ActiveLifts.Remove(this);
    }

    private void EnsurePlatforms()
    {
        if (platformPrefab == null)
            return;

        int routeCount = Mathf.Max(2, platformRoutes != null ? platformRoutes.Length : 0);
        while (platforms.Count < routeCount)
        {
            GameObject instance = Instantiate(platformPrefab, transform);
            instance.name = $"{platformPrefab.name}_Platform_{platforms.Count + 1}";
            PlatformState state = new PlatformState
            {
                platformIndex = platforms.Count,
                root = instance.transform,
                currentSide = Mathf.Clamp(platforms.Count, 0, 1)
            };

            platforms.Add(state);
        }
    }

    private void ResetPlatformsToDropPoints()
    {
        for (int i = 0; i < platforms.Count; i++)
        {
            PlatformState state = platforms[i];
            if (state == null || state.root == null)
                continue;

            state.currentSide = Mathf.Clamp(i, 0, 1);
            state.moving = false;
            state.reserved = false;
            state.passenger = null;
            state.lastBecameIdleAt = Time.time;
            state.root.position = GetDropPointWorld(state.platformIndex, state.currentSide);
        }
    }

    private PlatformState TryReservePlatform(int pickupSide, Vector3 pickupWorldPosition)
    {
        List<PlatformState> availablePlatforms = new List<PlatformState>();
        List<PlatformState> availableAtPickupSide = new List<PlatformState>();

        for (int i = 0; i < platforms.Count; i++)
        {
            PlatformState platform = platforms[i];
            if (platform == null || platform.root == null || platform.moving || platform.reserved || platform.passenger != null)
                continue;

            Transform pickupDropPoint = GetDropPoint(platform.platformIndex, pickupSide);
            if (pickupDropPoint == null)
                continue;

            availablePlatforms.Add(platform);
            if (platform.currentSide == pickupSide)
                availableAtPickupSide.Add(platform);
        }

        if (availablePlatforms.Count == 0)
            return null;

        PlatformState selected;
        if (availableAtPickupSide.Count == 1)
        {
            selected = availableAtPickupSide[0];
        }
        else if (availableAtPickupSide.Count >= 2)
        {
            selected = availableAtPickupSide[Random.Range(0, availableAtPickupSide.Count)];
        }
        else if (availablePlatforms.Count == 1)
        {
            selected = availablePlatforms[0];
        }
        else
        {
            selected = availablePlatforms[Random.Range(0, availablePlatforms.Count)];
        }

        selected.reserved = true;
        return selected;
    }

    private IEnumerator MovePlatformToSide(PlatformState platform, int destinationSide)
    {
        if (platform == null || platform.root == null)
            yield break;

        if (platform.moving)
        {
            while (platform != null && platform.moving)
                yield return null;

            yield break;
        }

        if (platform.currentSide == destinationSide)
            yield break;

        platform.moving = true;
        List<Vector3> route = BuildRoute(platform.platformIndex, platform.currentSide, destinationSide);
        for (int i = 0; i < route.Count; i++)
        {
            Vector3 target = route[i];
            while ((platform.root.position - target).sqrMagnitude > 0.000001f)
            {
                platform.root.position = Vector3.MoveTowards(platform.root.position, target, platformMoveSpeed * Time.deltaTime);
                yield return null;
            }
        }

        platform.currentSide = destinationSide;
        platform.moving = false;
    }

    private List<Vector3> BuildRoute(int platformIndex, int fromSide, int toSide)
    {
        List<Vector3> route = new List<Vector3>();
        PlatformRoute routeConfig = GetPlatformRoute(platformIndex);
        if (routeConfig == null)
            return route;

        if (fromSide <= toSide)
        {
            for (int i = 0; i < routeConfig.movementPoints.Count; i++)
            {
                if (routeConfig.movementPoints[i] != null)
                    route.Add(routeConfig.movementPoints[i].position);
            }
        }
        else
        {
            for (int i = routeConfig.movementPoints.Count - 1; i >= 0; i--)
            {
                if (routeConfig.movementPoints[i] != null)
                    route.Add(routeConfig.movementPoints[i].position);
            }
        }

        route.Add(GetDropPointWorld(platformIndex, toSide));
        return route;
    }

    private Transform GetDropPoint(int platformIndex, int side)
    {
        PlatformRoute routeConfig = GetPlatformRoute(platformIndex);
        if (routeConfig == null || routeConfig.dropPoints == null || side < 0 || side >= routeConfig.dropPoints.Length)
            return null;

        return routeConfig.dropPoints[side];
    }

    private PlatformRoute GetPlatformRoute(int platformIndex)
    {
        if (platformRoutes == null || platformIndex < 0 || platformIndex >= platformRoutes.Length)
            return null;

        return platformRoutes[platformIndex];
    }

    private bool HasAnyDropPointForSide(int side)
    {
        for (int i = 0; i < platforms.Count; i++)
        {
            if (GetDropPoint(i, side) != null)
                return true;
        }

        return false;
    }

    private bool AreAllPlatformsOccupiedByPassengers()
    {
        if (platforms.Count == 0)
            return false;

        for (int i = 0; i < platforms.Count; i++)
        {
            PlatformState platform = platforms[i];
            if (platform == null || platform.root == null || platform.passenger == null)
                return false;
        }

        return true;
    }

    private void TryCompletePendingRelocation()
    {
        if (pendingRelocationTarget == null || ownerLiftSpot == null)
            return;

        for (int i = 0; i < platforms.Count; i++)
        {
            PlatformState platform = platforms[i];
            if (platform == null)
                continue;

            if (platform.moving || platform.passenger != null || platform.reserved)
                return;
        }

        Lift target = pendingRelocationTarget;
        pendingRelocationTarget = null;
        ownerLiftSpot.CompleteRelocation(this, target);
    }

    private bool CanStartPointerInteraction()
    {
        if (!allowPointerRelocation)
            return false;

        if (!IsOperational || ownerLiftSpot == null)
            return false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return false;

        return true;
    }

    private void BeginDrag()
    {
        isDragging = true;
    }

    private void UpdateDragPreview()
    {
        Vector3 pointerWorld = GetPointerWorldPosition();
        pointerWorld.z = transform.position.z;
        transform.position = pointerWorld;
    }

    private void FinishDrag()
    {
        if (!isDragging)
            return;

        isDragging = false;
        transform.position = dragOriginPosition;

        if (ownerLiftSpot == null)
            return;

        if (ownerLiftSpot.TryFindNearestInactiveLift(GetPointerWorldPosition(), this, out Lift targetLift))
            ownerLiftSpot.RequestRelocation(this, targetLift);
    }

    private void ReleasePointerState()
    {
        StopPointerHoldRoutine();
        pointerHeld = false;
        isDragging = false;
        pointerDownStartedAt = -1f;
    }

    private bool IsPointerStillPressed()
    {
        return Input.GetMouseButton(0);
    }

    private IEnumerator PointerHoldRoutine()
    {
        while (pointerHeld || isDragging)
        {
            if (!IsPointerStillPressed())
            {
                ReleasePointerState();
                yield break;
            }

            if (pointerHeld && !isDragging && Time.unscaledTime - pointerDownStartedAt >= HoldDurationSeconds)
                BeginDrag();

            if (isDragging)
                UpdateDragPreview();

            yield return null;
        }

        pointerHoldRoutine = null;
    }

    private void StopPointerHoldRoutine()
    {
        if (pointerHoldRoutine != null)
        {
            StopCoroutine(pointerHoldRoutine);
            pointerHoldRoutine = null;
        }
    }

    private Vector3 GetPointerWorldPosition()
    {
        Camera cameraRef = Camera.main;
        Vector3 screenPosition = Input.mousePosition;
        if (cameraRef == null)
            return new Vector3(screenPosition.x, screenPosition.y, transform.position.z);

        Vector3 world = cameraRef.ScreenToWorldPoint(screenPosition);
        world.z = transform.position.z;
        return world;
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
