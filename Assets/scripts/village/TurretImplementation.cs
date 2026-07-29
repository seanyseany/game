using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

[RequireComponent(typeof(Collider2D))]
public class TurretImplementation : MonoBehaviour, IColliderPointerTarget
{
    private const float DragScaleMultiplier = 1.2f;
    private const float HoldDurationSeconds = 0.7f;
    private const int DragSortingOrderBoost = 1000;
    private const float DropSnapPadding = 1.5f;

    private static readonly System.Collections.Generic.List<TurretImplementation> AllSlots = new System.Collections.Generic.List<TurretImplementation>();

    [System.Serializable]
    public class BoundsRect
    {
        public Vector2 minLocal = new Vector2(-1f, -1f);
        public Vector2 maxLocal = new Vector2(1f, 1f);

        public bool Contains(Vector2 localPoint)
        {
            return localPoint.x >= Mathf.Min(minLocal.x, maxLocal.x) &&
                   localPoint.x <= Mathf.Max(minLocal.x, maxLocal.x) &&
                   localPoint.y >= Mathf.Min(minLocal.y, maxLocal.y) &&
                   localPoint.y <= Mathf.Max(minLocal.y, maxLocal.y);
        }
    }

    [SerializeField] private string slotId;
    [SerializeField] private BoundsRect placementArea = new BoundsRect();
    [SerializeField] private Vector2 placeLocalPosition;
    [SerializeField] private BaseTurret turretLevel1Prefab;
    [SerializeField] private BaseTurret turretLevel2Prefab;
    [SerializeField] private BaseTurret turretLevel3Prefab;
    [SerializeField] private TurretListUI turretListUI;
    [SerializeField] private TurretUI turretUI;

    private BaseTurret currentTurret;
    private BaseTurret draggedTurret;
    private Vector3 initialScale = Vector3.one;
    private Vector3 dragPointerOffset;
    private TurretImplementation dragOriginSlot;
    private Vector3 dragOriginWorldPosition;
    private float pointerDownStartedAt = -1f;
    private bool tapPending;
    private bool pointerHeld;
    private bool isDragging;
    private float lastDragFinishedAt = -1f;
    private SortingGroup draggedTurretSortingGroup;
    private string draggedTurretOriginalSortingLayerName;
    private int draggedTurretOriginalSortingOrder;
    private Vector3 draggedTurretOriginalScale = Vector3.one;

    public string SlotId => GetResolvedSlotId();
    public BaseTurret CurrentTurret
    {
        get
        {
            EnsureCurrentTurretReference();
            return currentTurret;
        }
    }

    public int CurrentTurretLevel
    {
        get
        {
            EnsureCurrentTurretReference();
            return currentTurret != null ? Mathf.Max(0, currentTurret.Level) : 0;
        }
    }
    public void ConfigureRuntimeSlot(string nextSlotId, Vector2 nextPlaceLocalPosition)
    {
        slotId = nextSlotId;
        placeLocalPosition = nextPlaceLocalPosition;
    }

    private void Awake()
    {
        EnsureSlotId();
        SyncCurrentTurretFromChildren();
        EnsureInteractionCollider();
        EnsurePointerForwarders();
        if (currentTurret != null)
            initialScale = currentTurret.transform.localScale;
    }

    private void OnTransformChildrenChanged()
    {
        SyncCurrentTurretFromChildren();
        EnsurePointerForwarders();
    }

    private void OnEnable()
    {
        if (!AllSlots.Contains(this))
            AllSlots.Add(this);
    }

    private void OnDisable()
    {
        AllSlots.Remove(this);
        VillagePointerCapture.Release(this);
    }

    private void Update()
    {
        if ((pointerHeld || isDragging) && !IsPointerStillPressed())
        {
            if (tapPending && !isDragging)
                TryOpenUiFromTap();

            ReleasePointerHold();
        }

        if (pointerHeld && !isDragging && Time.unscaledTime - pointerDownStartedAt >= HoldDurationSeconds)
            BeginDrag();

        if (isDragging)
            UpdateDragPosition();
    }

    private void OnMouseDown()
    {
        HandleColliderPointerDown();
    }

    private void OnMouseUp()
    {
        TryOpenUiFromTap();
        HandleColliderPointerUp();
    }

    private void OnMouseUpAsButton()
    {
        TryOpenUiFromTap();
    }

    public void HandleColliderPointerDown()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        VillagePointerCapture.Acquire(this);
        pointerHeld = true;
        tapPending = true;
        pointerDownStartedAt = Time.unscaledTime;
        dragOriginSlot = this;
        dragOriginWorldPosition = currentTurret != null ? currentTurret.transform.position : transform.position;
    }

    public void HandleColliderPointerUp()
    {
        VillagePointerCapture.Release(this);
        ReleasePointerHold();
    }

    public void HandleColliderPointerUpAsButton()
    {
        if (isDragging || Time.unscaledTime - lastDragFinishedAt < 0.1f)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (currentTurret == null)
            OpenList();
        else
            OpenTurretUI();
    }

    private void TryOpenUiFromTap()
    {
        if (!tapPending)
            return;

        if (isDragging || Time.unscaledTime - lastDragFinishedAt < 0.1f)
            return;

        if (Time.unscaledTime - pointerDownStartedAt >= HoldDurationSeconds)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (currentTurret == null)
            OpenList();
        else
            OpenTurretUI();
    }


    public void TryInstall(BaseTurret turretPrefab, bool ownedAlready)
    {
        EnsureCurrentTurretReference();
        if (currentTurret != null || turretPrefab == null)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        if (!ownedAlready)
        {
            if (!villageManagement.TrySpendOxygen(turretPrefab.CurrentOxygenPrice))
                return;

            villageManagement.AddOwnedTurret(turretPrefab.CatalogId);
        }

        PlaceTurret(GetPrefabForLevel(turretPrefab.Level, turretPrefab), turretPrefab.Level, false);
        turretListUI?.Close();
    }

    public bool TryInstallFromShop(BaseTurret turretPrefab)
    {
        EnsureCurrentTurretReference();
        if (currentTurret != null || turretPrefab == null)
            return false;

        PlaceTurret(GetPrefabForLevel(turretPrefab.Level, turretPrefab), turretPrefab.Level, false);
        EnsureCurrentTurretReference();
        turretListUI?.Close();
        turretUI?.Close();
        return currentTurret != null;
    }

    public void TryUpgrade()
    {
        if (currentTurret == null || !currentTurret.CanUpgrade())
            return;

        BaseTurret upgradePrefab = currentTurret.GetUpgradePrefab();
        if (upgradePrefab == null || VillageManagement.Instance == null)
            return;

        if (!VillageManagement.Instance.TrySpendOxygen(upgradePrefab.CurrentOxygenPrice))
            return;

        ReplaceTurret(upgradePrefab, upgradePrefab.Level, false);
        turretUI?.Open(this, currentTurret);
    }

    public bool TryUpgradeFromShop(BaseTurret upgradePrefab)
    {
        if (currentTurret == null || upgradePrefab == null)
            return false;

        int targetLevel = Mathf.Max(1, upgradePrefab.Level);
        if (targetLevel != currentTurret.Level + 1)
            return false;

        ReplaceTurret(upgradePrefab, targetLevel, false);
        turretListUI?.Close();
        turretUI?.Close();
        return true;
    }

    public void RemoveTurret()
    {
        if (currentTurret == null)
            return;

        Destroy(currentTurret.gameObject);
        currentTurret = null;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
            villageManagement.RemoveTurretState(SlotId);

        turretUI?.Close();
    }

    public void PrepareForRestore()
    {
        if (currentTurret != null)
            Destroy(currentTurret.gameObject);

        currentTurret = null;
        draggedTurret = null;
        pointerHeld = false;
        isDragging = false;
        tapPending = false;
        pointerDownStartedAt = -1f;
    }

    public bool RestoreFromState(VillageManagement.TurretState state, BaseTurret fallbackPrefab)
    {
        if (state == null || fallbackPrefab == null)
            return false;

        EnsureSlotId();

        if (!string.Equals(state.slotId, SlotId, System.StringComparison.Ordinal))
            return false;

        ReplaceTurret(fallbackPrefab, Mathf.Max(1, state.level), false, false);
        EnsureCurrentTurretReference();
        if (currentTurret == null)
            return false;

        currentTurret.ApplySavedState(state.level, state.currentAmmo, state.maxAmmo);
        initialScale = currentTurret.transform.localScale;
        return true;
    }

    public bool TryReapplySavedState(VillageManagement.TurretState state)
    {
        if (state == null)
            return false;

        EnsureSlotId();
        EnsureCurrentTurretReference();
        if (currentTurret == null)
            return false;

        if (!string.Equals(state.slotId, SlotId, System.StringComparison.Ordinal))
            return false;

        currentTurret.ApplySavedState(state.level, state.currentAmmo, state.maxAmmo);
        initialScale = currentTurret.transform.localScale;
        return true;
    }

    private void OpenList()
    {
        if (turretListUI == null)
            turretListUI = TurretListUI.Instance != null ? TurretListUI.Instance : FindFirstObjectByType<TurretListUI>();
        if (turretListUI != null)
            turretListUI.Open(this);
    }

    private void OpenTurretUI()
    {
        if (turretUI == null)
            turretUI = TurretUI.EnsureInstance();
        if (turretUI != null)
            turretUI.Open(this, currentTurret);
    }

    private void PlaceTurret(BaseTurret prefab, int level, bool keepAmmoRatio, bool initializeAmmoState = true)
    {
        currentTurret = Instantiate(prefab, transform);
        currentTurret.AssignSlot(SlotId);
        currentTurret.transform.localPosition = new Vector3(
            placeLocalPosition.x - currentTurret.BottomLocalPosition.x,
            placeLocalPosition.y - currentTurret.BottomLocalPosition.y,
            0f);
        ConfigureTurretRange(currentTurret);
        if (initializeAmmoState)
            currentTurret.ApplyLevel(level, keepAmmoRatio);
        currentTurret.SetPlacementMirrored(ShouldMirrorPlacedTurret());
        if (initializeAmmoState)
            currentTurret.PushState();
        initialScale = currentTurret.transform.localScale;
        SyncCurrentTurretFromChildren();
        EnsurePointerForwarders();
    }

    private void ReplaceTurret(BaseTurret prefab, int level, bool keepAmmoRatio, bool initializeAmmoState = true)
    {
        if (currentTurret != null)
            Destroy(currentTurret.gameObject);

        currentTurret = null;
        PlaceTurret(GetPrefabForLevel(level, prefab), level, keepAmmoRatio, initializeAmmoState);
    }

    private BaseTurret GetPrefabForLevel(int level, BaseTurret fallback)
    {
        if (level >= 3 && turretLevel3Prefab != null)
            return turretLevel3Prefab;
        if (level == 2 && turretLevel2Prefab != null)
            return turretLevel2Prefab;
        if (level <= 1 && turretLevel1Prefab != null)
            return turretLevel1Prefab;

        return fallback;
    }

    private void BeginDrag()
    {
        if (isDragging || currentTurret == null)
            return;

        tapPending = false;
        if (turretListUI != null)
            turretListUI.Close();
        if (turretUI != null)
            turretUI.Close();

        isDragging = true;
        dragOriginSlot = this;
        dragOriginWorldPosition = currentTurret.transform.position;
        draggedTurret = currentTurret;
        Vector3 pointerWorld = GetPointerWorldPosition();
        dragPointerOffset = draggedTurret.transform.position - pointerWorld;
        draggedTurretOriginalScale = draggedTurret.transform.localScale;
        draggedTurret.transform.localScale = draggedTurretOriginalScale * DragScaleMultiplier;
        RaiseDraggedTurretSorting();
        ReleaseCurrentTurretReference(false);
        currentTurret = null;
        draggedTurret.transform.SetParent(null, true);
    }

    private void UpdateDragPosition()
    {
        if (draggedTurret == null)
            return;

        Vector3 pointerWorld = GetPointerWorldPosition();
        pointerWorld.z = draggedTurret.transform.position.z;
        draggedTurret.transform.position = pointerWorld + dragPointerOffset;
    }

    private void FinishDrag()
    {
        if (draggedTurret == null)
        {
            ResetPointerStateAfterDrop();
            return;
        }

        RestoreDraggedTurretSorting();
        draggedTurret.transform.localScale = draggedTurretOriginalScale;
        lastDragFinishedAt = Time.unscaledTime;

        Vector3 dropPoint = draggedTurret.transform.position + new Vector3(draggedTurret.BottomLocalPosition.x, draggedTurret.BottomLocalPosition.y, 0f);
        TurretImplementation occupiedTarget = FindDirectOccupiedDropTarget(dropPoint, dragOriginSlot);
        TurretImplementation emptyTarget = FindBestEmptyDropTarget(dropPoint, dragOriginSlot);

        float occupiedDistance = occupiedTarget != null ? Vector2.Distance(dropPoint, occupiedTarget.transform.position) : float.MaxValue;
        float emptyDistance = emptyTarget != null ? Vector2.Distance(dropPoint, emptyTarget.transform.position) : float.MaxValue;

        if (occupiedTarget != null && occupiedDistance <= emptyDistance)
        {
            if (dragOriginSlot != null && dragOriginSlot != occupiedTarget && dragOriginSlot.IsEmptySlot)
            {
                Vector3 displacedTurretOriginalScale = occupiedTarget.currentTurret != null
                    ? occupiedTarget.currentTurret.transform.localScale
                    : Vector3.one;
                BaseTurret displacedTurret = occupiedTarget.DetachCurrentTurret();
                occupiedTarget.AcceptMovedTurret(draggedTurret, draggedTurretOriginalScale);
                draggedTurret = null;

                if (displacedTurret != null)
                    dragOriginSlot.AcceptMovedTurret(displacedTurret, displacedTurretOriginalScale);

                ResetPointerStateAfterDrop();
                return;
            }

            TurretImplementation relocationTarget = FindRelocationTarget(occupiedTarget.transform.position, occupiedTarget, dragOriginSlot);
            if (relocationTarget != null)
            {
                Vector3 displacedTurretOriginalScale = occupiedTarget.currentTurret != null
                    ? occupiedTarget.currentTurret.transform.localScale
                    : Vector3.one;
                BaseTurret displacedTurret = occupiedTarget.DetachCurrentTurret();
                occupiedTarget.AcceptMovedTurret(draggedTurret, draggedTurretOriginalScale);
                draggedTurret = null;

                if (displacedTurret != null)
                    relocationTarget.AcceptMovedTurret(displacedTurret, displacedTurretOriginalScale);

                ResetPointerStateAfterDrop();
                return;
            }
        }

        if (emptyTarget != null)
        {
            emptyTarget.AcceptMovedTurret(draggedTurret, draggedTurretOriginalScale);
            draggedTurret = null;
            ResetPointerStateAfterDrop();
            return;
        }

        if (dragOriginSlot != null)
        {
            dragOriginSlot.AcceptMovedTurret(draggedTurret, draggedTurretOriginalScale);
            draggedTurret = null;
            ResetPointerStateAfterDrop();
            return;
        }

        draggedTurret.transform.position = dragOriginWorldPosition;
        ResetPointerStateAfterDrop();
    }

    private void ReleasePointerHold()
    {
        if (!pointerHeld && !isDragging)
            return;

        bool wasDragging = isDragging;
        pointerHeld = false;
        pointerDownStartedAt = -1f;
        tapPending = false;
        if (wasDragging)
            FinishDrag();
    }

    private void ResetPointerStateAfterDrop()
    {
        VillagePointerCapture.Release(this);
        pointerHeld = false;
        isDragging = false;
        pointerDownStartedAt = -1f;
        tapPending = false;
        dragOriginSlot = null;
        draggedTurret = null;
    }

    private void SyncCurrentTurretFromChildren()
    {
        BaseTurret[] children = GetComponentsInChildren<BaseTurret>(true);
        currentTurret = children.Length > 0 ? children[0] : null;
    }

    private void ReleaseCurrentTurretReference(bool removeState)
    {
        if (currentTurret == null)
            return;

        if (removeState)
        {
            VillageManagement villageManagement = VillageManagement.EnsureInstance();
            if (villageManagement != null && !string.IsNullOrWhiteSpace(SlotId))
                villageManagement.RemoveTurretState(SlotId);
        }
    }

    public BaseTurret DetachCurrentTurret()
    {
        if (currentTurret == null)
            return null;

        BaseTurret detachedTurret = currentTurret;
        currentTurret = null;
        detachedTurret.transform.SetParent(null, true);

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null && !string.IsNullOrWhiteSpace(SlotId))
            villageManagement.RemoveTurretState(SlotId);

        return detachedTurret;
    }

    public void AcceptMovedTurret(BaseTurret turret, Vector3 restoredScale)
    {
        if (turret == null)
            return;

        currentTurret = turret;
        currentTurret.transform.SetParent(transform, false);
        currentTurret.AssignSlot(SlotId);
        currentTurret.transform.localPosition = new Vector3(
            placeLocalPosition.x - currentTurret.BottomLocalPosition.x,
            placeLocalPosition.y - currentTurret.BottomLocalPosition.y,
            0f);
        ConfigureTurretRange(currentTurret);
        currentTurret.transform.localScale = restoredScale == Vector3.zero ? Vector3.one : restoredScale;
        currentTurret.SetPlacementMirrored(ShouldMirrorPlacedTurret());
        currentTurret.PushState();
        initialScale = currentTurret.transform.localScale;
        SyncCurrentTurretFromChildren();
        EnsurePointerForwarders();
    }

    private void EnsureCurrentTurretReference()
    {
        if (currentTurret == null)
            SyncCurrentTurretFromChildren();
    }

    private void ConfigureTurretRange(BaseTurret turret)
    {
        if (turret is not Turret configuredTurret)
            return;

        Path path = GetComponent<Path>();
        if (path == null)
            configuredTurret.ConfigureTargetRange(transform, new Vector2(-3f, -2f), new Vector2(3f, 2f));
        else
            configuredTurret.ConfigureTargetRange(
                path.transform,
                path.TurretTargetRangeMinLocal,
                path.TurretTargetRangeMaxLocal);
    }

    private bool ShouldMirrorPlacedTurret()
    {
        Path path = GetComponent<Path>();
        return path != null && path.RotatePlacedPrefab180;
    }

    private void EnsureSlotId()
    {
        if (!string.IsNullOrWhiteSpace(slotId))
            return;

        slotId = BuildStableSlotId();
    }

    private string GetResolvedSlotId()
    {
        EnsureSlotId();
        return slotId;
    }

    private string BuildStableSlotId()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder("turret_slot:");
        Transform current = transform;
        while (current != null)
        {
            builder.Append(current.name);
            builder.Append('[');
            builder.Append(current.GetSiblingIndex());
            builder.Append(']');

            current = current.parent;
            if (current != null)
                builder.Append('/');
        }

        return builder.ToString();
    }

    public bool IsEmptySlot => currentTurret == null;

    public static TurretImplementation FindBestEmptyDropTarget(Vector3 worldPoint, TurretImplementation originalSlot)
    {
        TurretImplementation bestSlot = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < AllSlots.Count; i++)
        {
            TurretImplementation candidate = AllSlots[i];
            if (candidate == null || candidate == originalSlot || !candidate.IsEmptySlot)
                continue;

            if (!candidate.IsWithinDropRange(worldPoint, out float distance))
                continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSlot = candidate;
            }
        }

        return bestSlot;
    }

    public static TurretImplementation FindDirectOccupiedDropTarget(Vector3 worldPoint, TurretImplementation originalSlot)
    {
        TurretImplementation bestSlot = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < AllSlots.Count; i++)
        {
            TurretImplementation candidate = AllSlots[i];
            if (candidate == null || candidate == originalSlot || candidate.currentTurret == null)
                continue;

            if (!candidate.IsDirectDropPoint(worldPoint))
                continue;

            float distance = Vector2.Distance(worldPoint, candidate.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSlot = candidate;
            }
        }

        return bestSlot;
    }

    public static TurretImplementation FindRelocationTarget(Vector3 referenceWorldPosition, TurretImplementation excludedSlot, TurretImplementation preferredSlot)
    {
        TurretImplementation bestSlot = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < AllSlots.Count; i++)
        {
            TurretImplementation candidate = AllSlots[i];
            if (candidate == null || candidate == excludedSlot || !candidate.IsEmptySlot)
                continue;

            float distance = Vector2.Distance(referenceWorldPosition, candidate.transform.position);
            if (preferredSlot != null && candidate == preferredSlot)
                distance -= 1000f;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSlot = candidate;
            }
        }

        return bestSlot;
    }

    private bool IsDirectDropPoint(Vector3 worldPoint)
    {
        Collider2D collider2D = GetComponent<Collider2D>();
        if (collider2D == null)
            return Vector2.Distance(transform.position, worldPoint) <= DropSnapPadding * 0.5f;

        Bounds bounds = collider2D.bounds;
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

    private bool IsWithinDropRange(Vector3 worldPoint, out float distance)
    {
        Collider2D collider2D = GetComponent<Collider2D>();
        if (collider2D == null)
        {
            distance = Vector2.Distance(transform.position, worldPoint);
            return distance <= DropSnapPadding;
        }

        Vector2 closestPoint = collider2D.ClosestPoint(worldPoint);
        distance = Vector2.Distance(worldPoint, closestPoint);
        if (collider2D.OverlapPoint(worldPoint))
            distance = 0f;

        Bounds bounds = collider2D.bounds;
        float reach = Mathf.Max(bounds.extents.x, bounds.extents.y) + DropSnapPadding;
        return distance <= reach;
    }

    private Vector3 GetPointerWorldPosition()
    {
        Camera cameraRef = Camera.main;
        Vector3 screen = GetCurrentPointerScreenPosition();
        if (cameraRef == null)
            return screen;

        screen.z = Mathf.Abs(cameraRef.transform.position.z - transform.position.z);
        return cameraRef.ScreenToWorldPoint(screen);
    }

    private bool IsPointerWithinInteractionArea(Vector3 worldPoint)
    {
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider2D = colliders[i];
            if (collider2D != null && collider2D.OverlapPoint(worldPoint))
                return true;
        }

        Vector2 local = transform.InverseTransformPoint(worldPoint);
        return placementArea.Contains(local);
    }

    private static Vector3 GetCurrentPointerScreenPosition()
    {
        if (Input.touchCount > 0)
            return Input.GetTouch(0).position;

        return Input.mousePosition;
    }

    private void EnsurePointerForwarders()
    {
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider2D = colliders[i];
            if (collider2D == null || collider2D.gameObject == gameObject)
                continue;

            if (collider2D.GetComponent<ColliderPointerForwarder2D>() == null)
                collider2D.gameObject.AddComponent<ColliderPointerForwarder2D>();
        }
    }

    private void EnsureInteractionCollider()
    {
        if (GetComponent<Collider2D>() != null)
            return;

        BoxCollider2D boxCollider = gameObject.AddComponent<BoxCollider2D>();
        boxCollider.isTrigger = true;
        boxCollider.size = new Vector2(2f, 2f);
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

    private void RaiseDraggedTurretSorting()
    {
        if (draggedTurret == null)
            return;

        draggedTurretSortingGroup = draggedTurret.GetComponent<SortingGroup>();
        if (draggedTurretSortingGroup == null)
            draggedTurretSortingGroup = draggedTurret.gameObject.AddComponent<SortingGroup>();

        draggedTurretOriginalSortingLayerName = draggedTurretSortingGroup.sortingLayerName;
        draggedTurretOriginalSortingOrder = draggedTurretSortingGroup.sortingOrder;
        draggedTurretSortingGroup.enabled = true;
        draggedTurretSortingGroup.sortingOrder = draggedTurretOriginalSortingOrder + DragSortingOrderBoost + 100;
    }

    private void RestoreDraggedTurretSorting()
    {
        if (draggedTurretSortingGroup == null)
            return;

        draggedTurretSortingGroup.sortingLayerName = draggedTurretOriginalSortingLayerName;
        draggedTurretSortingGroup.sortingOrder = draggedTurretOriginalSortingOrder;
        draggedTurretSortingGroup.enabled = false;
        draggedTurretSortingGroup = null;
    }
}
