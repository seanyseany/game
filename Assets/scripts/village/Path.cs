using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D))]
public class Path : MonoBehaviour
{
    private static readonly System.Collections.Generic.List<Path> AllPaths = new System.Collections.Generic.List<Path>();
    private const float DropSnapPadding = 1.5f;

    [Header("Placement")]
    [SerializeField] private bool allowsBuildingPlacement = true;
    [SerializeField] private bool rotatePlacedPrefab180;

    [Header("Turret Range")]
    [SerializeField] private Vector2 turretTargetRangeMinLocal = new Vector2(-3f, -2f);
    [SerializeField] private Vector2 turretTargetRangeMaxLocal = new Vector2(3f, 2f);

    private Collider2D cachedCollider;
    private Building buildingInstance;
    private string pathId;
    private Coroutine constructionRoutine;
    private string activeConstructionBuildingId;
    private int activeConstructionTargetLevel;
    private float activeConstructionRemainingSeconds;
    private bool activeConstructionUpgrading;

    public string PathId => pathId;
    public Building Building => buildingInstance;
    public bool IsEmpty => buildingInstance == null && constructionRoutine == null;
    public bool AllowsBuildingPlacement => allowsBuildingPlacement;
    public bool RotatePlacedPrefab180 => rotatePlacedPrefab180;
    public Vector2 TurretTargetRangeMinLocal => turretTargetRangeMinLocal;
    public Vector2 TurretTargetRangeMaxLocal => turretTargetRangeMaxLocal;
    public bool IsAvailableForBuildingPlacement => allowsBuildingPlacement && IsEmpty;
    public bool HasActiveConstruction => constructionRoutine != null;
    public string ActiveConstructionBuildingId => activeConstructionBuildingId;
    public int ActiveConstructionTargetLevel => constructionRoutine != null ? activeConstructionTargetLevel : 0;

    private void Awake()
    {
        cachedCollider = GetComponent<Collider2D>();
        pathId = BuildPathId();
        SyncBuildingReferenceFromChildren();
        RefreshInteractionCollider();
    }

    private void OnTransformChildrenChanged()
    {
        SyncBuildingReferenceFromChildren();
        RefreshInteractionCollider();
    }

    private void OnEnable()
    {
        if (!AllPaths.Contains(this))
            AllPaths.Add(this);
    }

    private void OnDisable()
    {
        AllPaths.Remove(this);
    }

    public float GetActivationScore()
    {
        if (buildingInstance == null || !buildingInstance.IsPlaced)
            return 0f;

        return buildingInstance.IsWorking ? 2f : 0.5f;
    }

    public Vector3 GetRandomWorldPointOnPath()
    {
        Bounds bounds = cachedCollider != null ? cachedCollider.bounds : new Bounds(transform.position, Vector3.one);
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y),
            0f);
    }

    private void OnMouseUpAsButton()
    {
        if (constructionRoutine != null)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (buildingInstance != null)
            OpenBuildingUI();
    }

    public static Path FindFirstEmpty()
    {
        for (int i = 0; i < AllPaths.Count; i++)
        {
            Path path = AllPaths[i];
            if (path != null && path.IsAvailableForBuildingPlacement)
                return path;
        }

        return null;
    }

    public static Path FindRandomEmpty()
    {
        System.Collections.Generic.List<Path> emptyPaths = new System.Collections.Generic.List<Path>();
        for (int i = 0; i < AllPaths.Count; i++)
        {
            Path path = AllPaths[i];
            if (path != null && path.IsAvailableForBuildingPlacement)
                emptyPaths.Add(path);
        }

        if (emptyPaths.Count == 0)
            return null;

        return emptyPaths[Random.Range(0, emptyPaths.Count)];
    }

    public static Path FindBestEmptyDropTarget(Vector3 bottomAnchorWorldPosition, Path originalPath)
    {
        Path bestPath = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < AllPaths.Count; i++)
        {
            Path candidate = AllPaths[i];
            if (candidate == null || candidate == originalPath || !candidate.IsAvailableForBuildingPlacement)
                continue;

            if (!candidate.IsWithinDropRange(bottomAnchorWorldPosition, out float distance))
                continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPath = candidate;
            }
        }

        return bestPath;
    }

    public static Path FindDirectOccupiedDropTarget(Vector3 worldPoint, Path originalPath)
    {
        Path bestPath = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < AllPaths.Count; i++)
        {
            Path candidate = AllPaths[i];
            if (candidate == null || candidate == originalPath || candidate.buildingInstance == null)
                continue;

            if (!candidate.IsDirectDropPoint(worldPoint))
                continue;

            float distance = Vector2.Distance(worldPoint, candidate.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPath = candidate;
            }
        }

        return bestPath;
    }

    public static Path FindRelocationTarget(Vector3 referenceWorldPosition, Path excludedPath)
    {
        System.Collections.Generic.List<Path> emptyPaths = new System.Collections.Generic.List<Path>();

        for (int i = 0; i < AllPaths.Count; i++)
        {
            Path candidate = AllPaths[i];
            if (candidate == null || candidate == excludedPath || !candidate.IsAvailableForBuildingPlacement)
                continue;

            emptyPaths.Add(candidate);
        }

        if (emptyPaths.Count == 0)
            return null;

        return emptyPaths[Random.Range(0, emptyPaths.Count)];
    }

    public bool IsDirectDropPoint(Vector3 worldPoint)
    {
        if (cachedCollider == null)
            return Vector2.Distance(transform.position, worldPoint) <= DropSnapPadding * 0.5f;

        Bounds bounds = cachedCollider.bounds;
        float expandAmount = DropSnapPadding * 0.7f;
        float minX = bounds.min.x - expandAmount;
        float maxX = bounds.max.x + expandAmount;
        float minY = bounds.min.y - expandAmount;
        float maxY = bounds.max.y + expandAmount;

        return worldPoint.x >= minX &&
               worldPoint.x <= maxX &&
               worldPoint.y >= minY &&
               worldPoint.y <= maxY;
    }

    public void TryBuildSelected(Building selectedPrefab)
    {
        if (!allowsBuildingPlacement || selectedPrefab == null || constructionRoutine != null || buildingInstance != null)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        int targetLevel = 1;
        int price = selectedPrefab.GetPurchasePriceForLevel(targetLevel);
        float constructionTime = selectedPrefab.GetConstructionTimeForLevel(targetLevel);

        if (price > 0 && !villageManagement.TrySpendOxygen(price))
            return;

        if (constructionTime > 0f)
            BeginConstruction(selectedPrefab, targetLevel, constructionTime, false);
        else
            PlaceBuildingImmediately(selectedPrefab, targetLevel, false);

        Shop.CloseAllShops();
    }

    public void TryUpgradeCurrentBuilding()
    {
        if (buildingInstance == null || buildingInstance.Level >= 2 || constructionRoutine != null)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        int targetLevel = 2;
        int price = buildingInstance.GetPurchasePriceForLevel(targetLevel);
        if (!villageManagement.TrySpendOxygen(price))
            return;

        float constructionTime = buildingInstance.GetConstructionTimeForLevel(targetLevel);
        if (constructionTime > 0f)
            BeginConstruction(buildingInstance, targetLevel, constructionTime, true);
        else
            buildingInstance.SetLevel(targetLevel);

        FindBuildingUi()?.Refresh();
    }

    public void RemoveCurrentBuilding()
    {
        if (buildingInstance == null)
            return;

        Destroy(buildingInstance.gameObject);
        buildingInstance = null;
        RefreshInteractionCollider();

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
        {
            villageManagement.RemoveBuildingState(pathId);
            villageManagement.SetEnergyCapacity(Mathf.Max(0, Mathf.RoundToInt(villageManagement.EnergyCapacity / 1.2f)));
        }

        BuildingUI buildingUI = FindBuildingUi();
        if (buildingUI != null)
            buildingUI.Close();
    }

    public void ClearPlacementState()
    {
        if (buildingInstance != null)
        {
            RemoveCurrentBuilding();
            return;
        }

        if (constructionRoutine != null)
        {
            StopCoroutine(constructionRoutine);
            constructionRoutine = null;
        }

        activeConstructionBuildingId = string.Empty;
        activeConstructionTargetLevel = 0;
        activeConstructionRemainingSeconds = 0f;
        activeConstructionUpgrading = false;
        RefreshInteractionCollider();

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
            villageManagement.RemoveBuildingState(pathId);
    }

    public void PrepareForRestore()
    {
        if (constructionRoutine != null)
        {
            StopCoroutine(constructionRoutine);
            constructionRoutine = null;
        }

        activeConstructionBuildingId = string.Empty;
        activeConstructionTargetLevel = 0;
        activeConstructionRemainingSeconds = 0f;
        activeConstructionUpgrading = false;

        if (buildingInstance != null)
            Destroy(buildingInstance.gameObject);

        buildingInstance = null;
        RefreshInteractionCollider();
    }

    public bool RestoreFromState(VillageManagement.BuildingState state, Building buildingPrefab)
    {
        if (state == null || buildingPrefab == null)
            return false;

        if (!string.Equals(state.slotId, pathId, System.StringComparison.Ordinal))
            return false;

        if (constructionRoutine != null)
            return false;

        if (buildingInstance != null)
        {
            Destroy(buildingInstance.gameObject);
            buildingInstance = null;
            RefreshInteractionCollider();
        }

        if (state.underConstruction)
        {
            BeginConstruction(
                buildingPrefab,
                Mathf.Max(1, state.level),
                Mathf.Max(0.01f, state.constructionRemainingSeconds),
                false);
            return true;
        }

        PlaceBuildingImmediately(buildingPrefab, Mathf.Max(1, state.level), true);
        if (buildingInstance == null)
            return false;

        buildingInstance.SetSalary(state.currentSalary, state.maxSalary);
        buildingInstance.SetWorking(state.isWorking);
        buildingInstance.MarkPlaced(state.isPlaced);
        return true;
    }

    public void SetBuildingPlacementAllowed(bool allowed)
    {
        allowsBuildingPlacement = allowed;
    }

    private void OpenBuildingUI()
    {
        BuildingUI buildingUI = FindBuildingUi();
        if (buildingUI != null)
            buildingUI.Open(this, buildingInstance);
    }

    private void BeginConstruction(Building buildingPrefab, int targetLevel, float duration, bool upgrading)
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        activeConstructionBuildingId = buildingPrefab != null ? buildingPrefab.BuildingId : string.Empty;
        activeConstructionTargetLevel = targetLevel;
        activeConstructionRemainingSeconds = duration;
        activeConstructionUpgrading = upgrading;
        RefreshInteractionCollider();
        if (!upgrading && villageManagement != null && buildingPrefab != null)
        {
            villageManagement.UpsertBuildingState(new VillageManagement.BuildingState
            {
                slotId = pathId,
                buildingId = buildingPrefab.BuildingId,
                level = targetLevel,
                currentSalary = 0,
                maxSalary = 0,
                isPlaced = false,
                isWorking = false,
                underConstruction = true,
                constructionRemainingSeconds = activeConstructionRemainingSeconds
            });
        }

        if (constructionRoutine != null)
            StopCoroutine(constructionRoutine);

        constructionRoutine = StartCoroutine(RunConstruction(duration, targetLevel, buildingPrefab, upgrading, () =>
        {
            constructionRoutine = null;
            if (upgrading)
            {
                Building upgradeTarget = buildingInstance != null ? buildingInstance : buildingPrefab;
                if (upgradeTarget != null)
                    upgradeTarget.SetLevel(targetLevel);
            }
            else
            {
                PlaceBuildingImmediately(buildingPrefab, targetLevel, false);
            }

            activeConstructionBuildingId = string.Empty;
            activeConstructionTargetLevel = 0;
            activeConstructionRemainingSeconds = 0f;
            activeConstructionUpgrading = false;
        }));
    }

    private void PlaceBuildingImmediately(Building buildingPrefab, int level, bool skipCapacityBonus)
    {
        if (buildingPrefab == null)
            return;

        if (buildingInstance != null)
            Destroy(buildingInstance.gameObject);

        buildingInstance = Instantiate(buildingPrefab, transform, false);
        buildingInstance.AssignSlot(pathId);
        buildingInstance.SetLevel(level);
        if (!skipCapacityBonus)
            buildingInstance.SetSalary(buildingInstance.MaxSalary, buildingInstance.MaxSalary);
        buildingInstance.MarkPlaced(true);
        SnapBuildingToPath(buildingInstance);
        buildingInstance.RestartOwnerPatrolFromAnchor();
        RefreshInteractionCollider();
        StartCoroutine(FinalizePlacedBuildingNextFrame(buildingInstance));

        if (!skipCapacityBonus)
        {
            VillageManagement villageManagement = VillageManagement.EnsureInstance();
            if (villageManagement != null)
                villageManagement.SetEnergyCapacity(Mathf.RoundToInt(villageManagement.EnergyCapacity * 1.2f));
        }

        buildingInstance.PushStateToVillageManagement();
    }

    public void ReleaseBuildingReference(Building building, bool removeSavedState = true)
    {
        if (buildingInstance == building)
        {
            buildingInstance = null;
            RefreshInteractionCollider();

            VillageManagement villageManagement = VillageManagement.EnsureInstance();
            if (removeSavedState && villageManagement != null)
                villageManagement.RemoveBuildingState(pathId);
        }
    }

    public void AcceptMovedBuilding(
        Building building,
        string previousSlotId = null,
        bool saveImmediately = true,
        bool removePreviousState = true)
    {
        if (building == null)
            return;

        buildingInstance = building;
        building.transform.SetParent(transform, false);
        building.AssignSlot(pathId);
        building.MarkPlaced(true);
        SnapBuildingToPath(building);
        building.RestartOwnerPatrolFromAnchor();
        RefreshInteractionCollider();
        StartCoroutine(FinalizePlacedBuildingNextFrame(building));

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null &&
            removePreviousState &&
            !string.IsNullOrWhiteSpace(previousSlotId) &&
            !string.Equals(previousSlotId, pathId, System.StringComparison.Ordinal))
        {
            villageManagement.RemoveBuildingState(previousSlotId, building.BuildingId, false);
        }

        if (saveImmediately)
            building.PushStateToVillageManagement(true);
    }

    public Building DetachCurrentBuilding(bool removeSavedState = true)
    {
        if (buildingInstance == null)
            return null;

        Building detachedBuilding = buildingInstance;
        buildingInstance = null;
        RefreshInteractionCollider();

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (removeSavedState && villageManagement != null)
            villageManagement.RemoveBuildingState(pathId);

        return detachedBuilding;
    }

    public void TransferActiveUpgradeTo(Path targetPath, Building movingBuilding)
    {
        if (targetPath == null ||
            movingBuilding == null ||
            targetPath == this ||
            constructionRoutine == null ||
            !activeConstructionUpgrading ||
            activeConstructionTargetLevel <= movingBuilding.Level)
            return;

        float remaining = Mathf.Max(0.01f, activeConstructionRemainingSeconds);
        int targetLevel = activeConstructionTargetLevel;
        string buildingId = activeConstructionBuildingId;

        StopCoroutine(constructionRoutine);
        constructionRoutine = null;
        activeConstructionBuildingId = string.Empty;
        activeConstructionTargetLevel = 0;
        activeConstructionRemainingSeconds = 0f;
        activeConstructionUpgrading = false;
        RefreshInteractionCollider();

        targetPath.ReceiveTransferredUpgrade(movingBuilding, targetLevel, remaining, buildingId);
    }

    private IEnumerator RunConstruction(float duration, int targetLevel, Building buildingPrefab, bool upgrading, System.Action onCompleted)
    {
        float remaining = Mathf.Max(0.01f, duration);
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            activeConstructionRemainingSeconds = Mathf.Max(0f, remaining);
            yield return null;
        }

        onCompleted?.Invoke();
    }

    private IEnumerator FinalizePlacedBuildingNextFrame(Building building)
    {
        yield return null;

        if (building == null || building.transform.parent != transform)
            yield break;

        SnapBuildingToPath(building);
        building.RestartOwnerPatrolFromAnchor();
    }

    private void SyncBuildingReferenceFromChildren()
    {
        Building[] children = GetComponentsInChildren<Building>(true);
        buildingInstance = children.Length > 0 ? children[0] : null;
    }

    private void RefreshInteractionCollider()
    {
        if (cachedCollider == null)
            cachedCollider = GetComponent<Collider2D>();

        if (cachedCollider == null)
            return;

        cachedCollider.enabled = buildingInstance == null && constructionRoutine == null;
    }

    private BuildingUI FindBuildingUi()
    {
        return BuildingUI.EnsureInstance();
    }

    private string BuildPathId()
    {
        Stack<string> segments = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            bool isPathsRoot = current.name.StartsWith("paths", System.StringComparison.OrdinalIgnoreCase);
            segments.Push(isPathsRoot
                ? "paths"
                : $"{current.name}[{current.GetSiblingIndex()}]");

            if (isPathsRoot)
                break;

            current = current.parent;
        }

        if (segments.Count == 0)
            return $"{transform.name}[{transform.GetSiblingIndex()}]";

        return string.Join("/", segments.ToArray());
    }

    private void SnapBuildingToPath(Building building)
    {
        if (building == null)
            return;

        building.SnapBottomAnchorToWorld(transform.position);
    }

    private bool IsWithinDropRange(Vector3 worldPoint, out float distance)
    {
        if (cachedCollider == null)
        {
            distance = Vector2.Distance(transform.position, worldPoint);
            return distance <= DropSnapPadding;
        }

        Vector2 closestPoint = cachedCollider.ClosestPoint(worldPoint);
        distance = Vector2.Distance(worldPoint, closestPoint);
        if (cachedCollider.OverlapPoint(worldPoint))
            distance = 0f;

        Bounds bounds = cachedCollider.bounds;
        float pathReach = Mathf.Max(bounds.extents.x, bounds.extents.y) + DropSnapPadding;
        return distance <= pathReach;
    }
    private void ReceiveTransferredUpgrade(Building movingBuilding, int targetLevel, float remainingDuration, string buildingId)
    {
        if (movingBuilding == null)
            return;

        buildingInstance = movingBuilding;
        activeConstructionBuildingId = string.IsNullOrWhiteSpace(buildingId) ? movingBuilding.BuildingId : buildingId;
        activeConstructionTargetLevel = targetLevel;
        activeConstructionRemainingSeconds = Mathf.Max(0.01f, remainingDuration);
        activeConstructionUpgrading = true;
        RefreshInteractionCollider();

        if (constructionRoutine != null)
            StopCoroutine(constructionRoutine);

        constructionRoutine = StartCoroutine(RunConstruction(activeConstructionRemainingSeconds, targetLevel, movingBuilding, true, () =>
        {
            constructionRoutine = null;
            if (buildingInstance != null)
                buildingInstance.SetLevel(targetLevel);

            activeConstructionBuildingId = string.Empty;
            activeConstructionTargetLevel = 0;
            activeConstructionRemainingSeconds = 0f;
            activeConstructionUpgrading = false;
        }));
    }
}
