using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

public class SpecialBuilding : MonoBehaviour
{
    private const float DragScaleMultiplier = 1.2f;
    private const float HoldDurationSeconds = 0.7f;
    private const int DragSortingOrderBoost = 1000;

    [Header("Identity")]
    [SerializeField] private string slotId;
    [SerializeField] private string specialBuildingId;

    [Header("Special")]
    [SerializeField] private Transform customerEntrance;
    [SerializeField] private int specialBuildingValue;
    [SerializeField] [Range(1f, 5f)] private float starRating = 3f;
    [SerializeField] private GameObject interiorPrefab;

    [Header("Anchors")]
    [SerializeField] private Transform bottomAnchor;

    private GameObject runtimeInteriorInstance;
    private SortingGroup sortingGroup;
    private Path currentPath;
    private Path dragOriginPath;
    private Vector3 dragOriginWorldPosition;
    private Vector3 initialScale;
    private Vector3 dragPointerOffset;
    private float pointerDownStartedAt = -1f;
    private float lastDragFinishedAt = -10f;
    private bool tapPending;
    private bool pointerHeld;
    private bool isDragging;
    private bool isPlaced = true;
    private string originalSortingLayerName;
    private int originalSortingOrder;

    public string SlotId => slotId;
    public string SpecialBuildingId => string.IsNullOrWhiteSpace(specialBuildingId) ? SanitizeId(name) : specialBuildingId;
    public int SpecialBuildingValue => specialBuildingValue;
    public float StarRating => starRating;
    public float VisitChance => Mathf.Clamp01(starRating / 5f);
    public Transform CustomerEntrance => customerEntrance != null ? customerEntrance : transform;
    public Transform BottomAnchor => bottomAnchor != null ? bottomAnchor : transform;
    public bool IsPlaced => isPlaced;
    public bool IsDragging => isDragging;

    private void Awake()
    {
        ResolveBottomAnchor();
        ResolveCustomerEntrance();
        EnsureInteriorInstance();
        EnsureInteractionCollider();
        EnsureSortingGroup();
        initialScale = transform.localScale;
        currentPath = GetComponentInParent<Path>();
    }

    private void OnDisable()
    {
        VillagePointerCapture.Release(this);
    }

    private void OnValidate()
    {
        starRating = Mathf.Clamp(Mathf.Round(starRating * 10f) / 10f, 1f, 5f);
        ResolveBottomAnchor();
        ResolveCustomerEntrance();
        EnsureInteractionCollider();
        EnsureSortingGroup();
    }

    private void Update()
    {
        if ((pointerHeld || isDragging) && !IsPointerStillPressed())
            ReleasePointerHold();

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
        ReleasePointerHold();
    }

    public void AssignSlot(string nextSlotId)
    {
        slotId = nextSlotId;
        currentPath = GetComponentInParent<Path>();
    }

    public void MarkPlaced(bool placed)
    {
        isPlaced = placed;
    }

    public bool CanAcceptCustomers()
    {
        return isPlaced && !isDragging && CustomerEntrance != null;
    }

    public void PrepareForRelocation()
    {
    }

    public void SnapBottomAnchorToWorld(Vector3 worldPosition)
    {
        Transform anchor = BottomAnchor;
        if (anchor == null)
            return;

        Vector3 delta = worldPosition - anchor.position;
        transform.position += delta;
    }

    private bool CanStartPointerInteraction()
    {
        if (!isPlaced)
            return false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return false;

        return true;
    }

    private void BeginDrag()
    {
        if (isDragging || currentPath == null)
            return;

        isDragging = true;
        tapPending = false;
        transform.localScale = initialScale * DragScaleMultiplier;
        RaiseSortingForDrag();
        Vector3 pointerWorld = GetPointerWorldPosition();
        dragPointerOffset = transform.position - pointerWorld;
        currentPath.ReleaseSpecialBuildingReference(this, false);
    }

    private void UpdateDragPosition()
    {
        Vector3 pointerWorld = GetPointerWorldPosition();
        pointerWorld.z = transform.position.z;
        transform.position = pointerWorld + dragPointerOffset;
    }

    private void FinishDrag()
    {
        VillagePointerCapture.Release(this);
        isDragging = false;
        pointerHeld = false;
        tapPending = false;
        pointerDownStartedAt = -1f;
        lastDragFinishedAt = Time.unscaledTime;
        transform.localScale = initialScale;
        RestoreSortingAfterDrag();

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
            Building occupiedBuilding = occupiedTargetPath.Building;
            if (dragOriginPath != null &&
                dragOriginPath != occupiedTargetPath &&
                dragOriginPath.IsAvailableForBuildingPlacement)
            {
                SpecialBuilding displaced = occupiedTargetPath.DetachCurrentSpecialBuilding();
                if (displaced != null)
                {
                    currentPath = occupiedTargetPath;
                    occupiedTargetPath.AcceptMovedSpecialBuilding(this, null, false, false);
                    dragOriginPath.AcceptMovedSpecialBuilding(displaced, null, false, false);
                    occupiedTargetPath.SaveCurrentSpecialBuildingState(true);
                    dragOriginPath.SaveCurrentSpecialBuildingState(true);
                    return;
                }

                if (occupiedBuilding != null)
                {
                    Building displacedBuilding = occupiedTargetPath.DetachCurrentBuilding(false);
                    if (displacedBuilding != null)
                    {
                        currentPath = occupiedTargetPath;
                        occupiedTargetPath.AcceptMovedSpecialBuilding(this, null, false, false);

                        displacedBuilding.PrepareForRelocation();
                        dragOriginPath.AcceptMovedBuilding(displacedBuilding, null, false, false);
                        occupiedTargetPath.TransferActiveUpgradeTo(dragOriginPath, displacedBuilding);
                        occupiedTargetPath.SaveCurrentSpecialBuildingState(true);
                        displacedBuilding.PushStateToVillageManagement(true);
                        return;
                    }
                }
            }

            Path relocationPath = Path.FindRelocationTarget(occupiedTargetPath.transform.position, occupiedTargetPath);
            if (relocationPath != null)
            {
                SpecialBuilding displaced = occupiedTargetPath.DetachCurrentSpecialBuilding();
                if (displaced != null)
                {
                    displaced.PrepareForRelocation();
                    relocationPath.AcceptMovedSpecialBuilding(displaced, null, false, false);
                    currentPath = occupiedTargetPath;
                    occupiedTargetPath.AcceptMovedSpecialBuilding(this, null, false, false);
                    relocationPath.SaveCurrentSpecialBuildingState(true);
                    occupiedTargetPath.SaveCurrentSpecialBuildingState(true);
                    if (dragOriginPath != null)
                    {
                        VillageManagement villageManagement = VillageManagement.EnsureInstance();
                        if (villageManagement != null)
                            villageManagement.RemoveBuildingState(dragOriginPath.PathId, null, "special", false);
                    }
                    return;
                }

                if (occupiedBuilding != null)
                {
                    Building displacedBuilding = occupiedTargetPath.DetachCurrentBuilding(false);
                    if (displacedBuilding != null)
                    {
                        displacedBuilding.PrepareForRelocation();
                        relocationPath.AcceptMovedBuilding(displacedBuilding, null, false, false);
                        occupiedTargetPath.TransferActiveUpgradeTo(relocationPath, displacedBuilding);

                        currentPath = occupiedTargetPath;
                        occupiedTargetPath.AcceptMovedSpecialBuilding(this, null, false, false);
                        occupiedTargetPath.SaveCurrentSpecialBuildingState(true);
                        return;
                    }
                }
            }
        }

        if (emptyTargetPath != null)
        {
            currentPath = emptyTargetPath;
            emptyTargetPath.AcceptMovedSpecialBuilding(this, null, false, false);
            emptyTargetPath.SaveCurrentSpecialBuildingState(true);
            if (dragOriginPath != null)
            {
                VillageManagement villageManagement = VillageManagement.EnsureInstance();
                if (villageManagement != null)
                    villageManagement.RemoveBuildingState(dragOriginPath.PathId, null, "special", false);
            }
            return;
        }

        if (dragOriginPath != null)
        {
            currentPath = dragOriginPath;
            dragOriginPath.AcceptMovedSpecialBuilding(this, null, false, false);
            dragOriginPath.SaveCurrentSpecialBuildingState(true);
        }
        else
        {
            transform.position = dragOriginWorldPosition;
        }
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

    private Vector3 GetPointerWorldPosition()
    {
        Camera cameraRef = Camera.main;
        Vector3 screenPosition = GetCurrentPointerScreenPosition();
        if (cameraRef == null)
            return screenPosition;

        screenPosition.z = Mathf.Abs(cameraRef.transform.position.z - transform.position.z);
        return cameraRef.ScreenToWorldPoint(screenPosition);
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

    private void EnsureInteriorInstance()
    {
        if (Application.isPlaying)
        {
            if (interiorPrefab == null || runtimeInteriorInstance != null)
                return;

            runtimeInteriorInstance = Instantiate(interiorPrefab, transform, false);
            runtimeInteriorInstance.name = interiorPrefab.name;
            return;
        }

        if (interiorPrefab == null)
            return;

        Transform existing = transform.Find(interiorPrefab.name);
        runtimeInteriorInstance = existing != null ? existing.gameObject : null;
    }

    private void ResolveBottomAnchor()
    {
        if (bottomAnchor == null)
            bottomAnchor = FindChildRecursive(transform, "BottomAnchor");
    }

    private void ResolveCustomerEntrance()
    {
        if (customerEntrance == null)
            customerEntrance = FindChildRecursive(transform, "CustomerEntrance");
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

    private static string SanitizeDisplayName(string source)
    {
        string cleaned = (source ?? string.Empty).Replace("(Clone)", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "SpecialBuilding" : cleaned;
    }

    private static string SanitizeId(string source)
    {
        return SanitizeDisplayName(source).Replace(" ", string.Empty).ToLowerInvariant();
    }
}
