using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WayOil : MonoBehaviour
{
    private const float OilDropSnapPadding = 4.5f;
    private static readonly List<WayOil> AllWayOils = new List<WayOil>();
    private const int IgnoreRaycastLayer = 2;

    [System.Serializable]
    public class RouteSequence
    {
        public List<Transform> nodes = new List<Transform>();
    }

    [Header("Route")]
    [SerializeField] private List<RouteSequence> routeSequences = new List<RouteSequence>();
    [SerializeField] private bool loopRoute = true;

    [Header("Oil Connections")]
    [SerializeField] private List<Transform> connectedOilPaths = new List<Transform>();
    [SerializeField] private List<Entrance> connectedOilEntrances = new List<Entrance>();
    [SerializeField] private List<Oxygen> oilPrefabs = new List<Oxygen>();

    [Header("Oil Employees")]
    [SerializeField] private List<GameObject> employeePrefabs = new List<GameObject>();
    [SerializeField] private int baseEmployees = 3;
    [SerializeField] private int employeesPerInstalledOil = 2;
    [SerializeField] private float lifetimeMin = 15f;
    [SerializeField] private float lifetimeMax = 20f;
    [SerializeField] private float refreshInterval = 0.5f;
    [SerializeField] private float spawnInterval = 0.4f;
    [SerializeField] private float respawnCooldownMin = 3f;
    [SerializeField] private float respawnCooldownMax = 5f;
    [SerializeField] private float employeeReductionDelay = 1f;
    [SerializeField] private float installedOilStopChance = 0.7f;
    [SerializeField] private float installedOilStopDelayMin = 0f;
    [SerializeField] private float installedOilStopDelayMax = 1f;
    [SerializeField] private float installedOilStopDurationMin = 3f;
    [SerializeField] private float installedOilStopDurationMax = 5f;
    [SerializeField] private float installedOilStopDetectionRadius = 1.5f;

    private sealed class EmployeeState
    {
        public GameObject instance;
        public OilStaff oilStaff;
        public Entrance sourceEntrance;
        public int routeSequenceIndex = int.MinValue;
        public int currentRouteNodeIndex = -1;
        public int routeTravelDirection = 1;
        public Coroutine roamRoutine;
        public float lifeEndTime;
        public bool retiring;
    }

    private readonly List<EmployeeState> spawnedEmployees = new List<EmployeeState>();
    private readonly List<Oxygen> installedOils = new List<Oxygen>();
    private Coroutine refreshRoutine;
    private int cooldownEmployees;
    private float overCapacitySince = -1f;
    public static IReadOnlyList<WayOil> RegisteredWayOils => AllWayOils;

    public IReadOnlyList<Transform> ConnectedOilPaths => connectedOilPaths;
    public IReadOnlyList<Entrance> ConnectedOilEntrances => connectedOilEntrances;
    public IReadOnlyList<Oxygen> OilPrefabs => oilPrefabs;
    public IReadOnlyList<GameObject> EmployeePrefabs => employeePrefabs;
    public IReadOnlyList<RouteSequence> RouteSequences => routeSequences;
    public bool LoopRoute => loopRoute;

    private void OnEnable()
    {
        if (!AllWayOils.Contains(this))
            AllWayOils.Add(this);

        SyncConnectedPathPlacementRestrictions(false);
        EnsureInstalledOilCache();
        RefreshEmployees();
        if (refreshRoutine == null)
            refreshRoutine = StartCoroutine(RefreshLoop());
    }

    private void OnDisable()
    {
        AllWayOils.Remove(this);
        SyncConnectedPathPlacementRestrictions(true);
        if (refreshRoutine != null)
        {
            StopCoroutine(refreshRoutine);
            refreshRoutine = null;
        }

        CleanupDestroyedEmployees(true);
    }

    private void OnValidate()
    {
        SyncConnectedPathPlacementRestrictions(false);
    }

    public int GetInstalledOilCount()
    {
        EnsureInstalledOilCache();
        int installed = 0;
        for (int i = 0; i < installedOils.Count; i++)
        {
            Transform path = i < connectedOilPaths.Count ? connectedOilPaths[i] : null;
            if (path != null && !HasBuildingOnSlot(path) && installedOils[i] != null)
                installed++;
        }

        return installed;
    }

    public int GetTargetEmployeeCount()
    {
        int installedOilCount = GetInstalledOilCount();
        return Mathf.Max(0, baseEmployees + (installedOilCount * employeesPerInstalledOil));
    }

    public bool HasEmptyOilPath()
    {
        EnsureInstalledOilCache();
        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            Transform path = connectedOilPaths[i];
            if (path == null)
                continue;

            if (HasBuildingOnSlot(path))
                continue;

            if (installedOils[i] == null)
                return true;
        }

        return false;
    }

    public int ConnectedOilPathCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < connectedOilPaths.Count; i++)
            {
                if (connectedOilPaths[i] != null)
                    count++;
            }

            return count;
        }
    }

    public bool IsOilPathUsable(int pathIndex)
    {
        EnsureInstalledOilCache();
        if (pathIndex < 0 || pathIndex >= connectedOilPaths.Count)
            return false;

        Transform path = connectedOilPaths[pathIndex];
        return path != null && !HasBuildingOnSlot(path);
    }

    public int GetInstalledOilLevelAt(int pathIndex)
    {
        EnsureInstalledOilCache();
        if (pathIndex < 0 || pathIndex >= installedOils.Count)
            return 0;

        Oxygen installedOil = installedOils[pathIndex];
        return installedOil != null ? Mathf.Max(0, installedOil.Level) : 0;
    }

    public string GetSlotIdAt(int pathIndex)
    {
        if (pathIndex < 0 || pathIndex >= connectedOilPaths.Count)
            return string.Empty;

        return BuildOilSlotId(pathIndex);
    }

    public bool TryGetInstalledOilAt(int pathIndex, out Oxygen installedOil)
    {
        EnsureInstalledOilCache();
        if (pathIndex < 0 || pathIndex >= installedOils.Count)
        {
            installedOil = null;
            return false;
        }

        installedOil = installedOils[pathIndex];
        return installedOil != null;
    }

    public Oxygen GetOilPrefab(int oilIndex)
    {
        if (oilIndex < 0 || oilIndex >= oilPrefabs.Count)
            return null;

        return oilPrefabs[oilIndex];
    }

    public bool TryInstallPurchasedOil(Oxygen oilPrefab, bool ownedAlready)
    {
        return TryInstallPurchasedOil(oilPrefab, string.Empty, ownedAlready);
    }

    public bool TryInstallPurchasedOilAt(int pathIndex, Oxygen oilPrefab, string assignedSlotId, bool ownedAlready, string purchaseEntryId = "")
    {
        if (oilPrefab == null)
            return false;

        EnsureInstalledOilCache();

        if (pathIndex < 0 || pathIndex >= connectedOilPaths.Count)
            return false;

        Transform path = connectedOilPaths[pathIndex];
        if (path == null || installedOils[pathIndex] != null || HasBuildingOnSlot(path))
            return false;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return false;

        if (!ownedAlready)
        {
            if (!villageManagement.TrySpendOxygen(oilPrefab.CurrentOxygenPrice))
                return false;

            villageManagement.AddOwnedOxygen(oilPrefab.ShopFamilyId);
        }

        Oxygen installedOil = Instantiate(oilPrefab, path);
        installedOils[pathIndex] = installedOil;
        installedOil.AssignSlot(string.IsNullOrWhiteSpace(assignedSlotId) ? BuildOilSlotId(pathIndex) : assignedSlotId);
        installedOil.AssignPurchaseEntryId(purchaseEntryId);
        installedOil.ApplySavedState(oilPrefab.Level, 0);
        installedOil.SnapBottomToWorld(path.position);
        installedOil.SetPlacementMirrored(ShouldMirrorInstalledOil(path));
        installedOil.BindWayOilSlot(this, pathIndex);
        SetSlotRaycastEnabled(pathIndex, false);
        installedOil.PushState();
        RefreshEmployees();
        return true;
    }

    public bool TryInstallPurchasedOil(Oxygen oilPrefab, string assignedSlotId, bool ownedAlready, string purchaseEntryId = "")
    {
        if (oilPrefab == null)
            return false;

        EnsureInstalledOilCache();

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return false;

        if (!ownedAlready)
        {
            if (!villageManagement.TrySpendOxygen(oilPrefab.CurrentOxygenPrice))
                return false;

            villageManagement.AddOwnedOxygen(oilPrefab.ShopFamilyId);
        }

        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            Transform path = connectedOilPaths[i];
            if (path == null || installedOils[i] != null)
                continue;

            if (HasBuildingOnSlot(path))
            {
                Debug.LogWarning($"WayOil '{name}' ignored slot '{path.name}' because a Building already exists there.", path);
                continue;
            }

            return TryInstallPurchasedOilAt(i, oilPrefab, assignedSlotId, ownedAlready, purchaseEntryId);
        }

        return false;
    }

    public bool TryUpgradeInstalledOil(string oxygenId, Oxygen upgradePrefab)
    {
        if (string.IsNullOrWhiteSpace(oxygenId) || upgradePrefab == null)
            return false;

        EnsureInstalledOilCache();
        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            Oxygen installedOil = i < installedOils.Count ? installedOils[i] : null;
            Transform path = connectedOilPaths[i];
            if (path == null || installedOil == null || installedOil.ShopFamilyId != oxygenId)
                continue;

            string slotId = installedOil.SlotId;
            string purchaseEntryId = installedOil.PurchaseEntryId;
            int storedOxygen = installedOil.StoredOxygen;
            Destroy(installedOil.gameObject);

            Oxygen replacement = Instantiate(upgradePrefab, path);
            replacement.AssignSlot(slotId);
            replacement.AssignPurchaseEntryId(purchaseEntryId);
            replacement.ApplySavedState(upgradePrefab.Level, storedOxygen);
            replacement.SnapBottomToWorld(path.position);
            replacement.SetPlacementMirrored(ShouldMirrorInstalledOil(path));
            replacement.BindWayOilSlot(this, i);
            SetSlotRaycastEnabled(i, false);
            replacement.PushState();
            installedOils[i] = replacement;
            return true;
        }

        return false;
    }

    public bool TryUpgradeInstalledOilBySlotId(string slotId, Oxygen upgradePrefab)
    {
        if (string.IsNullOrWhiteSpace(slotId) || upgradePrefab == null)
            return false;

        EnsureInstalledOilCache();
        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            Oxygen installedOil = i < installedOils.Count ? installedOils[i] : null;
            Transform path = connectedOilPaths[i];
            if (path == null || installedOil == null || installedOil.SlotId != slotId)
                continue;

            int storedOxygen = installedOil.StoredOxygen;
            Destroy(installedOil.gameObject);

            Oxygen replacement = Instantiate(upgradePrefab, path);
            replacement.AssignSlot(slotId);
            replacement.AssignPurchaseEntryId(installedOil.PurchaseEntryId);
            replacement.ApplySavedState(upgradePrefab.Level, storedOxygen);
            replacement.SnapBottomToWorld(path.position);
            replacement.SetPlacementMirrored(ShouldMirrorInstalledOil(path));
            replacement.BindWayOilSlot(this, i);
            SetSlotRaycastEnabled(i, false);
            replacement.PushState();
            installedOils[i] = replacement;
            return true;
        }

        return false;
    }

    public bool TryUpgradeInstalledOilByPurchaseEntryId(string purchaseEntryId, Oxygen upgradePrefab)
    {
        if (string.IsNullOrWhiteSpace(purchaseEntryId) || upgradePrefab == null)
            return false;

        EnsureInstalledOilCache();
        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            Oxygen installedOil = i < installedOils.Count ? installedOils[i] : null;
            Transform path = connectedOilPaths[i];
            if (path == null || installedOil == null || installedOil.PurchaseEntryId != purchaseEntryId)
                continue;

            string slotId = installedOil.SlotId;
            int storedOxygen = installedOil.StoredOxygen;
            Destroy(installedOil.gameObject);

            Oxygen replacement = Instantiate(upgradePrefab, path);
            replacement.AssignSlot(slotId);
            replacement.AssignPurchaseEntryId(purchaseEntryId);
            replacement.ApplySavedState(upgradePrefab.Level, storedOxygen);
            replacement.SnapBottomToWorld(path.position);
            replacement.SetPlacementMirrored(ShouldMirrorInstalledOil(path));
            replacement.BindWayOilSlot(this, i);
            SetSlotRaycastEnabled(i, false);
            replacement.PushState();
            installedOils[i] = replacement;
            return true;
        }

        return false;
    }

    public bool TryGetSpawnEntrance(out Entrance entrance)
    {
        List<Entrance> validEntrances = new List<Entrance>();
        for (int i = 0; i < connectedOilEntrances.Count; i++)
        {
            if (connectedOilEntrances[i] != null)
                validEntrances.Add(connectedOilEntrances[i]);
        }

        if (validEntrances.Count == 0)
        {
            entrance = null;
            return false;
        }

        entrance = validEntrances[Random.Range(0, validEntrances.Count)];
        return true;
    }

    public bool TryGetInstalledOilPath(out Transform oilPath)
    {
        EnsureInstalledOilCache();
        List<Transform> installedPaths = new List<Transform>();
        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            Transform path = connectedOilPaths[i];
            if (path != null && !HasBuildingOnSlot(path) && installedOils[i] != null)
                installedPaths.Add(path);
        }

        if (installedPaths.Count == 0)
        {
            oilPath = null;
            return false;
        }

        oilPath = installedPaths[Random.Range(0, installedPaths.Count)];
        return true;
    }

    public bool TryGetInstalledOilBySlotId(string slotId, out Oxygen installedOil)
    {
        installedOil = null;
        if (string.IsNullOrWhiteSpace(slotId))
            return false;

        EnsureInstalledOilCache();
        for (int i = 0; i < installedOils.Count; i++)
        {
            Oxygen candidate = installedOils[i];
            if (candidate != null && string.Equals(candidate.SlotId, slotId, System.StringComparison.Ordinal))
            {
                installedOil = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryGetInstalledOilStopSlotIndex(Vector3 worldPoint, out int slotIndex)
    {
        EnsureInstalledOilCache();

        float detectionRadius = Mathf.Max(0.05f, installedOilStopDetectionRadius);
        float detectionSqrDistance = detectionRadius * detectionRadius;
        float bestSqrDistance = float.MaxValue;
        slotIndex = -1;

        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            Transform path = connectedOilPaths[i];
            if (path == null || HasBuildingOnSlot(path) || i >= installedOils.Count || installedOils[i] == null)
                continue;

            Vector3 delta = path.position - worldPoint;
            delta.z = 0f;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance > detectionSqrDistance || sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            slotIndex = i;
        }

        return slotIndex >= 0;
    }

    private void TryScheduleInstalledOilPause(EmployeeState employee, Vector3 worldPoint)
    {
        if (employee == null || employee.instance == null || employee.oilStaff == null || employee.retiring)
            return;

        if (!TryGetInstalledOilStopSlotIndex(worldPoint, out _))
            return;

        float stopChance = Mathf.Clamp01(installedOilStopChance);
        if (stopChance <= 0f || Random.value > stopChance)
            return;

        float delay = Random.Range(
            Mathf.Max(0f, installedOilStopDelayMin),
            Mathf.Max(Mathf.Max(0f, installedOilStopDelayMin), installedOilStopDelayMax));
        float duration = Random.Range(
            Mathf.Max(0f, installedOilStopDurationMin),
            Mathf.Max(Mathf.Max(0f, installedOilStopDurationMin), installedOilStopDurationMax));

        employee.oilStaff.SchedulePause(delay, duration);
    }

    public void RemoveAllInstalledOils()
    {
        EnsureInstalledOilCache();
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        for (int i = 0; i < installedOils.Count; i++)
        {
            Oxygen installedOil = installedOils[i];
            if (installedOil == null)
                continue;

            if (villageManagement != null && !string.IsNullOrWhiteSpace(installedOil.SlotId))
                villageManagement.RemoveOxygenGeneratorState(installedOil.SlotId);

            Destroy(installedOil.gameObject);
            installedOils[i] = null;
            SetSlotRaycastEnabled(i, true);
        }

        RefreshEmployees();
    }

    public void RefreshEmployees()
    {
        EnsureInstalledOilCache();
        CleanupDestroyedEmployees(false);

        int targetCount = GetTargetEmployeeCount();
        if (spawnedEmployees.Count <= targetCount)
        {
            overCapacitySince = -1f;
            return;
        }

        if (overCapacitySince < 0f)
        {
            overCapacitySince = Time.time;
            return;
        }

        if (Time.time - overCapacitySince < Mathf.Max(0f, employeeReductionDelay))
            return;

        int retiringCount = 0;
        for (int i = 0; i < spawnedEmployees.Count; i++)
        {
            if (spawnedEmployees[i] != null && spawnedEmployees[i].retiring)
                retiringCount++;
        }

        int activeCount = spawnedEmployees.Count - retiringCount;
        while (activeCount > targetCount)
        {
            EmployeeState employee = FindRetirementCandidate();
            if (employee == null)
                break;

            BeginEmployeeRetirement(employee);
            activeCount--;
        }
    }

    public Vector3 GetRandomRoamWorldPoint()
    {
        Collider2D roamCollider = GetComponent<Collider2D>();
        if (roamCollider != null)
        {
            Bounds bounds = roamCollider.bounds;
            for (int i = 0; i < 12; i++)
            {
                Vector3 candidate = new Vector3(
                    Random.Range(bounds.min.x, bounds.max.x),
                    Random.Range(bounds.min.y, bounds.max.y),
                    0f);

                if (roamCollider.OverlapPoint(candidate))
                    return candidate;
            }
        }

        int sequenceIndex = GetRandomRouteSequenceIndex();
        int firstIndex = GetFirstRouteNodeIndex(sequenceIndex);
        if (sequenceIndex != int.MinValue &&
            firstIndex >= 0 &&
            TryGetRouteNode(sequenceIndex, firstIndex, out Vector3 routePoint))
            return routePoint;

        return transform.position;
    }

    public int GetRandomRouteSequenceIndex()
    {
        List<int> validIndices = new List<int>();
        for (int i = 0; i < routeSequences.Count; i++)
        {
            if (GetFirstRouteNodeIndex(i) >= 0)
                validIndices.Add(i);
        }

        return validIndices.Count > 0 ? validIndices[Random.Range(0, validIndices.Count)] : int.MinValue;
    }

    public bool TryGetRouteNode(int sequenceIndex, int nodeIndex, out Vector3 worldPoint)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        if (nodeIndex >= 0 && nodeIndex < nodes.Count && nodes[nodeIndex] != null)
        {
            worldPoint = nodes[nodeIndex].position;
            return true;
        }

        worldPoint = transform.position;
        return false;
    }

    public int GetFirstRouteNodeIndex(int sequenceIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null)
                return i;
        }

        return -1;
    }

    public int GetNextRouteNodeIndex(int sequenceIndex, int currentIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int nextIndex = currentIndex + 1;
        while (nextIndex < nodes.Count)
        {
            if (nodes[nextIndex] != null)
                return nextIndex;

            nextIndex++;
        }

        if (!loopRoute)
            return -1;

        return GetFirstRouteNodeIndex(sequenceIndex);
    }

    public int GetNextRouteNodeIndexNoLoop(int sequenceIndex, int currentIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int nextIndex = currentIndex + 1;
        while (nextIndex < nodes.Count)
        {
            if (nodes[nextIndex] != null)
                return nextIndex;

            nextIndex++;
        }

        return -1;
    }

    public int GetPreviousRouteNodeIndex(int sequenceIndex, int currentIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int previousIndex = currentIndex - 1;
        while (previousIndex >= 0)
        {
            if (nodes[previousIndex] != null)
                return previousIndex;

            previousIndex--;
        }

        return -1;
    }

    public int GetPreviousRouteNodeIndexNoLoop(int sequenceIndex, int currentIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int previousIndex = currentIndex - 1;
        while (previousIndex >= 0)
        {
            if (nodes[previousIndex] != null)
                return previousIndex;

            previousIndex--;
        }

        return -1;
    }

    private IEnumerator RefreshLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.1f, refreshInterval));
        while (true)
        {
            CleanupDestroyedEmployees(false);
            RefreshEmployees();
            TrySpawnNextEmployee();
            yield return wait;
        }
    }

    private void TrySpawnNextEmployee()
    {
        int targetCount = GetTargetEmployeeCount();
        if (spawnedEmployees.Count + cooldownEmployees >= targetCount)
            return;

        EmployeeState employee = SpawnEmployee(spawnedEmployees.Count);
        if (employee == null)
            return;

        spawnedEmployees.Add(employee);
        employee.roamRoutine = StartCoroutine(EmployeeRoamRoutine(employee));
    }

    private EmployeeState SpawnEmployee(int employeeIndex)
    {
        if (employeePrefabs.Count == 0)
            return null;

        if (!TryGetSpawnEntrance(out Entrance entrance))
            return null;

        GameObject prefab = employeePrefabs[employeeIndex % employeePrefabs.Count];
        if (prefab == null)
            return null;

        Vector3 spawnPosition = entrance.SpawnWorldPosition;
        GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity, transform);
        instance.name = $"{prefab.name}_OilEmployee";
        OilStaff oilStaff = instance.GetComponent<OilStaff>();
        if (oilStaff == null)
            oilStaff = instance.AddComponent<OilStaff>();

        EmployeeState state = new EmployeeState
        {
            instance = instance,
            oilStaff = oilStaff,
            sourceEntrance = entrance,
            routeSequenceIndex = GetRandomRouteSequenceIndex(),
            lifeEndTime = Time.time + Random.Range(lifetimeMin, lifetimeMax)
        };

        int firstNodeIndex = GetFirstRouteNodeIndex(state.routeSequenceIndex);
        if (firstNodeIndex >= 0)
            state.currentRouteNodeIndex = firstNodeIndex;

        return state;
    }

    private IEnumerator EmployeeRoamRoutine(EmployeeState employee)
    {
        if (employee == null || employee.instance == null || employee.oilStaff == null)
            yield break;

        yield return new WaitForSeconds(Mathf.Max(0f, spawnInterval));

        while (employee != null &&
               employee.instance != null &&
               employee.oilStaff != null &&
               !employee.retiring &&
               Time.time < employee.lifeEndTime)
        {
            int nextNodeIndex = GetNextEmployeeWayNodeIndex(employee);
            Transform routeNode = GetRouteNodeTransform(employee.routeSequenceIndex, nextNodeIndex);
            if (nextNodeIndex >= 0 && routeNode != null)
            {
                yield return employee.oilStaff.MoveToTransformRoutine(routeNode, routeNode.position);
                employee.currentRouteNodeIndex = nextNodeIndex;
                TryScheduleInstalledOilPause(employee, routeNode.position);
            }
            else if (nextNodeIndex >= 0 && TryGetRouteNode(employee.routeSequenceIndex, nextNodeIndex, out Vector3 routePoint))
            {
                yield return employee.oilStaff.MoveToRoutine(routePoint);
                employee.currentRouteNodeIndex = nextNodeIndex;
                TryScheduleInstalledOilPause(employee, routePoint);
            }
            else
            {
                yield return employee.oilStaff.MoveToRoutine(GetRandomRoamWorldPoint());
            }

            yield return employee.oilStaff.PauseRoutine();
        }

        if (employee == null || employee.instance == null || employee.oilStaff == null)
            yield break;

        yield return ReturnEmployeeToEntranceAndDespawn(employee);
    }

    private void CleanupDestroyedEmployees(bool destroyAll)
    {
        for (int i = spawnedEmployees.Count - 1; i >= 0; i--)
        {
            EmployeeState employee = spawnedEmployees[i];
            if (destroyAll)
            {
                DestroyEmployee(employee);
                spawnedEmployees.RemoveAt(i);
                continue;
            }

            if (employee == null || employee.instance == null)
                spawnedEmployees.RemoveAt(i);
        }
    }

    private void DestroyEmployee(EmployeeState employee)
    {
        if (employee == null)
            return;

        if (employee.roamRoutine != null)
            StopCoroutine(employee.roamRoutine);

        if (employee.instance != null)
            Destroy(employee.instance);
    }

    private void BeginEmployeeRetirement(EmployeeState employee)
    {
        if (employee == null || employee.retiring)
            return;

        employee.retiring = true;
        if (employee.roamRoutine != null)
        {
            StopCoroutine(employee.roamRoutine);
            employee.roamRoutine = null;
        }

        employee.roamRoutine = StartCoroutine(ReturnEmployeeToEntranceAndDespawn(employee));
    }

    private IEnumerator ReturnEmployeeToEntranceAndDespawn(EmployeeState employee)
    {
        yield return ReturnEmployeeAlongRouteSequence(employee);

        if (employee == null || employee.instance == null || employee.oilStaff == null)
            yield break;

        Vector3 targetPosition = employee.sourceEntrance != null
            ? employee.sourceEntrance.DespawnWorldPosition
            : employee.instance.transform.position;
        yield return employee.oilStaff.MoveToRoutine(targetPosition);

        RemoveEmployee(employee);
    }

    private IEnumerator ReturnEmployeeAlongRouteSequence(EmployeeState employee)
    {
        if (employee == null || employee.routeSequenceIndex == int.MinValue)
            yield break;

        int returnNodeIndex;
        if (employee.currentRouteNodeIndex < 0)
        {
            returnNodeIndex = GetClosestRouteNodeIndex(employee.routeSequenceIndex, employee.instance.transform.position);
        }
        else
        {
            int nextNodeIndex = GetNextRouteNodeIndexNoLoop(employee.routeSequenceIndex, employee.currentRouteNodeIndex);
            int lastNodeIndex = GetLastRouteNodeIndex(employee.routeSequenceIndex);
            if (nextNodeIndex >= 0)
                returnNodeIndex = nextNodeIndex;
            else
                returnNodeIndex = lastNodeIndex >= 0 ? lastNodeIndex : employee.currentRouteNodeIndex;
        }

        while (returnNodeIndex >= 0)
        {
            if (employee.instance == null || employee.oilStaff == null)
                yield break;

            Transform routeNode = GetRouteNodeTransform(employee.routeSequenceIndex, returnNodeIndex);
            if (routeNode != null)
            {
                yield return employee.oilStaff.MoveToTransformRoutine(routeNode, routeNode.position);
                employee.currentRouteNodeIndex = returnNodeIndex;
            }
            else if (TryGetRouteNode(employee.routeSequenceIndex, returnNodeIndex, out Vector3 worldPoint))
            {
                yield return employee.oilStaff.MoveToRoutine(worldPoint);
                employee.currentRouteNodeIndex = returnNodeIndex;
            }

            returnNodeIndex = GetPreviousRouteNodeIndexNoLoop(employee.routeSequenceIndex, returnNodeIndex);
        }
    }

    private void RemoveEmployee(EmployeeState employee)
    {
        if (employee == null)
            return;

        if (employee.roamRoutine != null)
            StopCoroutine(employee.roamRoutine);

        spawnedEmployees.Remove(employee);
        if (employee.instance != null)
            Destroy(employee.instance);

        cooldownEmployees++;
        StartCoroutine(ReturnSpawnSlotAfterCooldown());
    }

    private EmployeeState FindRetirementCandidate()
    {
        for (int i = spawnedEmployees.Count - 1; i >= 0; i--)
        {
            EmployeeState employee = spawnedEmployees[i];
            if (employee == null || employee.retiring)
                continue;

            return employee;
        }

        return null;
    }

    private IEnumerator ReturnSpawnSlotAfterCooldown()
    {
        float cooldown = Random.Range(
            Mathf.Max(0f, respawnCooldownMin),
            Mathf.Max(Mathf.Max(0f, respawnCooldownMin), respawnCooldownMax));
        if (cooldown > 0f)
            yield return new WaitForSeconds(cooldown);

        cooldownEmployees = Mathf.Max(0, cooldownEmployees - 1);
    }

    private string BuildOilSlotId(int index)
    {
        return $"{name}_oil_{index}";
    }

    private void EnsureInstalledOilCache()
    {
        while (installedOils.Count < connectedOilPaths.Count)
            installedOils.Add(null);

        while (installedOils.Count > connectedOilPaths.Count)
            installedOils.RemoveAt(installedOils.Count - 1);

        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            if (installedOils[i] != null)
                continue;

            Transform path = connectedOilPaths[i];
            if (path == null)
                continue;

            if (HasBuildingOnSlot(path))
            {
                installedOils[i] = null;
                continue;
            }

            installedOils[i] = FindInstalledOilOnSlot(path);
            if (installedOils[i] != null)
            {
                installedOils[i].BindWayOilSlot(this, i);
                SetSlotRaycastEnabled(i, false);
            }
            else
            {
                SetSlotRaycastEnabled(i, true);
            }
        }
    }


    public bool TryGetSlotIndex(Transform slot, out int slotIndex)
    {
        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            if (connectedOilPaths[i] == slot)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    public void ReleaseOilReference(Oxygen oxygen)
    {
        if (oxygen == null)
            return;

        EnsureInstalledOilCache();
        for (int i = 0; i < installedOils.Count; i++)
        {
            if (installedOils[i] != oxygen)
                continue;

            installedOils[i] = null;
            SetSlotRaycastEnabled(i, true);
            VillageManagement villageManagement = VillageManagement.EnsureInstance();
            if (villageManagement != null)
                villageManagement.RemoveOxygenGeneratorState(oxygen.SlotId);
            return;
        }
    }

    public void AcceptMovedOil(Oxygen oxygen, int slotIndex)
    {
        if (oxygen == null || slotIndex < 0 || slotIndex >= connectedOilPaths.Count)
            return;

        EnsureInstalledOilCache();
        Transform slot = connectedOilPaths[slotIndex];
        if (slot == null)
            return;

        installedOils[slotIndex] = oxygen;
        oxygen.transform.SetParent(slot, false);
        oxygen.AssignSlot(GetSlotIdAt(slotIndex));
        oxygen.BindWayOilSlot(this, slotIndex);
        oxygen.SnapBottomToWorld(slot.position);
        oxygen.SetPlacementMirrored(ShouldMirrorInstalledOil(slot));
        SetSlotRaycastEnabled(slotIndex, false);
        oxygen.PushState();
        RefreshEmployees();
    }

    private static bool ShouldMirrorInstalledOil(Transform slot)
    {
        if (slot == null)
            return false;

        Path path = slot.GetComponent<Path>();
        return path != null && path.RotatePlacedPrefab180;
    }

    public Oxygen DetachInstalledOil(int slotIndex)
    {
        EnsureInstalledOilCache();
        if (slotIndex < 0 || slotIndex >= installedOils.Count)
            return null;

        Oxygen detached = installedOils[slotIndex];
        installedOils[slotIndex] = null;
        SetSlotRaycastEnabled(slotIndex, true);
        if (detached != null)
        {
            VillageManagement villageManagement = VillageManagement.EnsureInstance();
            if (villageManagement != null)
                villageManagement.RemoveOxygenGeneratorState(detached.SlotId);
        }

        RefreshEmployees();
        return detached;
    }

    public bool HasInstalledOilAt(int slotIndex)
    {
        EnsureInstalledOilCache();
        return slotIndex >= 0 && slotIndex < installedOils.Count && installedOils[slotIndex] != null;
    }

    public static bool TryFindBestEmptyDropTarget(Vector3 worldPoint, WayOil originalWayOil, int originalSlotIndex, out WayOil targetWayOil, out int targetSlotIndex)
    {
        targetWayOil = null;
        targetSlotIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < AllWayOils.Count; i++)
        {
            WayOil wayOil = AllWayOils[i];
            if (wayOil == null)
                continue;

            wayOil.EnsureInstalledOilCache();
            for (int slotIndex = 0; slotIndex < wayOil.connectedOilPaths.Count; slotIndex++)
            {
                if (wayOil == originalWayOil && slotIndex == originalSlotIndex)
                    continue;

                Transform slot = wayOil.connectedOilPaths[slotIndex];
                if (slot == null || wayOil.installedOils[slotIndex] != null || HasBuildingOnSlot(slot))
                    continue;

                if (!IsWithinOilSlotRange(slot, worldPoint, out float distance) || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                targetWayOil = wayOil;
                targetSlotIndex = slotIndex;
            }
        }

        return targetWayOil != null;
    }

    public static bool TryFindDirectOccupiedDropTarget(Vector3 worldPoint, WayOil originalWayOil, int originalSlotIndex, out WayOil targetWayOil, out int targetSlotIndex)
    {
        targetWayOil = null;
        targetSlotIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < AllWayOils.Count; i++)
        {
            WayOil wayOil = AllWayOils[i];
            if (wayOil == null)
                continue;

            wayOil.EnsureInstalledOilCache();
            for (int slotIndex = 0; slotIndex < wayOil.connectedOilPaths.Count; slotIndex++)
            {
                if (wayOil == originalWayOil && slotIndex == originalSlotIndex)
                    continue;

                Transform slot = wayOil.connectedOilPaths[slotIndex];
                if (slot == null || wayOil.installedOils[slotIndex] == null)
                    continue;

                if (!IsWithinOilSlotRange(slot, worldPoint, out float distance) || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                targetWayOil = wayOil;
                targetSlotIndex = slotIndex;
            }
        }

        return targetWayOil != null;
    }

    public static bool TryFindRelocationTarget(WayOil excludedWayOil, int excludedSlotIndex, out WayOil targetWayOil, out int targetSlotIndex)
    {
        targetWayOil = null;
        targetSlotIndex = -1;

        for (int i = 0; i < AllWayOils.Count; i++)
        {
            WayOil wayOil = AllWayOils[i];
            if (wayOil == null)
                continue;

            wayOil.EnsureInstalledOilCache();
            for (int slotIndex = 0; slotIndex < wayOil.connectedOilPaths.Count; slotIndex++)
            {
                if (wayOil == excludedWayOil && slotIndex == excludedSlotIndex)
                    continue;

                Transform slot = wayOil.connectedOilPaths[slotIndex];
                if (slot == null || wayOil.installedOils[slotIndex] != null || HasBuildingOnSlot(slot))
                    continue;

                targetWayOil = wayOil;
                targetSlotIndex = slotIndex;
                return true;
            }
        }

        return false;
    }

    private int GetNextEmployeeWayNodeIndex(EmployeeState employee)
    {
        if (employee == null)
            return -1;

        if (employee.routeSequenceIndex == int.MinValue)
            employee.routeSequenceIndex = GetRandomRouteSequenceIndex();

        if (employee.currentRouteNodeIndex < 0)
            return GetFirstRouteNodeIndex(employee.routeSequenceIndex);

        if (employee.routeTravelDirection >= 0)
        {
            int nextIndex = GetNextRouteNodeIndexNoLoop(employee.routeSequenceIndex, employee.currentRouteNodeIndex);
            if (nextIndex >= 0)
                return nextIndex;

            employee.routeTravelDirection = -1;
            int previousIndex = GetPreviousRouteNodeIndexNoLoop(employee.routeSequenceIndex, employee.currentRouteNodeIndex);
            return previousIndex >= 0 ? previousIndex : employee.currentRouteNodeIndex;
        }

        int reverseIndex = GetPreviousRouteNodeIndexNoLoop(employee.routeSequenceIndex, employee.currentRouteNodeIndex);
        if (reverseIndex >= 0)
            return reverseIndex;

        employee.routeTravelDirection = 1;
        int forwardIndex = GetNextRouteNodeIndexNoLoop(employee.routeSequenceIndex, employee.currentRouteNodeIndex);
        return forwardIndex >= 0 ? forwardIndex : employee.currentRouteNodeIndex;
    }

    private List<Transform> GetSequenceNodes(int sequenceIndex)
    {
        if (sequenceIndex < 0 || sequenceIndex >= routeSequences.Count || routeSequences[sequenceIndex] == null)
            return s_EmptyNodes;

        return routeSequences[sequenceIndex].nodes ?? s_EmptyNodes;
    }

    public int GetLastRouteNodeIndex(int sequenceIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i] != null)
                return i;
        }

        return -1;
    }

    public int GetClosestRouteNodeIndex(int sequenceIndex, Vector3 worldPosition)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        int bestIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < nodes.Count; i++)
        {
            Transform node = nodes[i];
            if (node == null)
                continue;

            float distance = (node.position - worldPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    public Transform GetRouteNodeTransform(int sequenceIndex, int nodeIndex)
    {
        List<Transform> nodes = GetSequenceNodes(sequenceIndex);
        if (nodeIndex >= 0 && nodeIndex < nodes.Count)
            return nodes[nodeIndex];

        return null;
    }

    private static bool HasBuildingOnSlot(Transform slot)
    {
        return slot != null && slot.GetComponentInChildren<Building>(true) != null;
    }

    private static Oxygen FindInstalledOilOnSlot(Transform slot)
    {
        if (slot == null)
            return null;

        Oxygen[] oils = slot.GetComponentsInChildren<Oxygen>(true);
        for (int i = 0; i < oils.Length; i++)
        {
            if (oils[i] != null)
                return oils[i];
        }

        return null;
    }

    private static bool IsWithinOilSlotRange(Transform slot, Vector3 worldPoint, out float distance)
    {
        if (slot == null)
        {
            distance = float.MaxValue;
            return false;
        }

        Collider2D collider2D = slot.GetComponent<Collider2D>();
        if (collider2D == null)
        {
            distance = Vector2.Distance(worldPoint, slot.position);
            return distance <= OilDropSnapPadding;
        }

        Vector2 closestPoint = collider2D.ClosestPoint(worldPoint);
        distance = Vector2.Distance(worldPoint, closestPoint);
        if (collider2D.OverlapPoint(worldPoint))
            distance = 0f;

        Bounds bounds = collider2D.bounds;
        float slotReach = ((Mathf.Max(bounds.extents.x, bounds.extents.y) * 2f) + OilDropSnapPadding) * 1.5f;
        return distance <= slotReach;
    }

    private void SyncConnectedPathPlacementRestrictions(bool allowBuildings)
    {
        for (int i = 0; i < connectedOilPaths.Count; i++)
        {
            Transform slot = connectedOilPaths[i];
            if (slot == null)
                continue;

            Path path = slot.GetComponent<Path>();
            if (path != null)
                path.SetBuildingPlacementAllowed(allowBuildings);
        }
    }

    private void SetSlotRaycastEnabled(int slotIndex, bool enabled)
    {
        if (slotIndex < 0 || slotIndex >= connectedOilPaths.Count)
            return;

        Transform slot = connectedOilPaths[slotIndex];
        if (slot == null)
            return;

        slot.gameObject.layer = enabled ? 0 : IgnoreRaycastLayer;
    }

    private static readonly List<Transform> s_EmptyNodes = new List<Transform>();
}
