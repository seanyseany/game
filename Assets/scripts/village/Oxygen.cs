using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

public class Oxygen : MonoBehaviour, IColliderPointerTarget
{
    private const float DragScaleMultiplier = 1.2f;
    private const float HoldDurationSeconds = 0.7f;
    private const int DragSortingOrderBoost = 1000;

    [SerializeField] private string oxygenId;
    [SerializeField] private string slotId;
    [SerializeField] private int oxygenPrice = 10;
    [SerializeField] private int energyUsage = 1;
    [SerializeField] private int oxygenProduction = 10;
    [SerializeField] private int level = 1;
    [SerializeField] private GameObject exclamationPrefab;
    [SerializeField] private Animator animator;
    [SerializeField] private float productionInterval = 10f;
    [SerializeField] private Vector2 bottomLocalPosition;

    private int storedOxygen;
    private Coroutine productionRoutine;
    private GameObject exclamationInstance;
    private SortingGroup sortingGroup;
    private Vector3 initialScale;
    private Vector3 dragPointerOffset;
    private WayOil currentWayOil;
    private WayOil dragOriginWayOil;
    private int currentSlotIndex = -1;
    private int dragOriginSlotIndex = -1;
    private Vector3 dragOriginWorldPosition;
    private float pointerDownStartedAt = -1f;
    private bool pointerHeld;
    private bool isDragging;
    private string originalSortingLayerName;
    private int originalSortingOrder;
    public string OxygenId => oxygenId;
    public string CatalogId => ShopIdentityUtility.GetStableId(oxygenId, this);
    public string SlotId => slotId;
    public int Level => level;
    public int CurrentOxygenPrice => oxygenPrice;
    public int CurrentEnergyUsage => energyUsage;
    public int OxygenProduction => oxygenProduction;
    public Vector2 BottomLocalPosition => bottomLocalPosition;

    private void Awake()
    {
        level = Mathf.Clamp(level, 1, 3);
        initialScale = transform.localScale;
        EnsureInteractionCollider();
        EnsurePointerForwarders();
        EnsureSortingGroup();
        ResolveWayOilBinding();
    }

    private void OnEnable()
    {
        VillageManagement.InstanceReady += HandleVillageManagementReady;
        SubscribeVillageManagement(VillageManagement.Instance);
    }

    private void Start()
    {
        RestartProduction();
        UpdateProductionAnimation();
        PushState();
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

    private void OnDisable()
    {
        VillagePointerCapture.Release(this);
        VillageManagement.InstanceReady -= HandleVillageManagementReady;
        UnsubscribeVillageManagement(VillageManagement.Instance);

        if (productionRoutine != null)
        {
            StopCoroutine(productionRoutine);
            productionRoutine = null;
        }

        if (animator != null)
            animator.enabled = false;
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

    public void HandleColliderPointerDown()
    {
        if (!CanStartPointerInteraction())
            return;

        VillagePointerCapture.Acquire(this);
        ResolveWayOilBinding();
        pointerHeld = true;
        pointerDownStartedAt = Time.unscaledTime;
        dragOriginWayOil = currentWayOil;
        dragOriginSlotIndex = currentSlotIndex;
        dragOriginWorldPosition = transform.position;
    }

    public void HandleColliderPointerUp()
    {
        VillagePointerCapture.Release(this);
        ReleasePointerHold();
    }

    public void HandleColliderPointerUpAsButton()
    {
        if (isDragging)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (storedOxygen > 0)
            CollectStoredOxygen();
    }

    public void AssignSlot(string nextSlotId)
    {
        slotId = nextSlotId;
    }

    public void BindWayOilSlot(WayOil wayOil, int slotIndex)
    {
        currentWayOil = wayOil;
        currentSlotIndex = slotIndex;
    }

    public void SnapBottomToWorld(Vector3 worldPosition)
    {
        transform.position = new Vector3(
            worldPosition.x - bottomLocalPosition.x,
            worldPosition.y - bottomLocalPosition.y,
            transform.position.z);
    }

    public void SetLevel(int nextLevel)
    {
        level = Mathf.Clamp(nextLevel, 1, 3);
        RestartProduction();
        UpdateProductionAnimation();
        PushState();
    }

    public void SetPlacementMirrored(bool mirrored)
    {
        Vector3 scale = transform.localScale;
        float absX = Mathf.Abs(scale.x);
        scale.x = mirrored ? -absX : absX;
        transform.localScale = scale;
        initialScale = scale;
    }

    public void ApplySavedState(int savedLevel, int savedStoredOxygen)
    {
        level = Mathf.Clamp(savedLevel, 1, 3);
        storedOxygen = Mathf.Max(0, savedStoredOxygen);
        RestartProduction();
        UpdateProductionAnimation();
        UpdateExclamation();
    }

    public void CollectStoredOxygen()
    {
        if (storedOxygen <= 0)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
            villageManagement.AddOxygen(storedOxygen);

        storedOxygen = 0;
        UpdateExclamation();
        PushState();
    }

    public void PushState()
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null || string.IsNullOrWhiteSpace(slotId))
            return;

        villageManagement.UpsertOxygenGeneratorState(new VillageManagement.OxygenGeneratorState
        {
            slotId = slotId,
            oxygenId = CatalogId,
            level = level,
            isPlaced = true,
            isProducing = CanProduce(villageManagement),
            storedOxygen = storedOxygen
        });
    }

    private bool CanStartPointerInteraction()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return false;

        return true;
    }

    private void BeginDrag()
    {
        if (isDragging || currentWayOil == null || currentSlotIndex < 0)
            return;

        isDragging = true;
        Vector3 pointerWorld = GetPointerWorldPosition();
        dragPointerOffset = transform.position - pointerWorld;
        transform.localScale = initialScale * DragScaleMultiplier;
        RaiseSortingForDrag();
        currentWayOil.ReleaseOilReference(this);
        transform.SetParent(null, true);
    }

    private void UpdateDragPosition()
    {
        Vector3 pointerWorld = GetPointerWorldPosition();
        pointerWorld.z = transform.position.z;
        transform.position = pointerWorld + dragPointerOffset;
    }

    private void FinishDrag()
    {
        isDragging = false;
        pointerHeld = false;
        pointerDownStartedAt = -1f;
        transform.localScale = initialScale;
        RestoreSortingAfterDrag();

        Vector3 dropPoint = new Vector3(
            transform.position.x + bottomLocalPosition.x,
            transform.position.y + bottomLocalPosition.y,
            transform.position.z);

        bool hasOccupiedTarget = WayOil.TryFindDirectOccupiedDropTarget(dropPoint, dragOriginWayOil, dragOriginSlotIndex, out WayOil occupiedWayOil, out int occupiedSlotIndex);
        bool hasEmptyTarget = WayOil.TryFindBestEmptyDropTarget(dropPoint, dragOriginWayOil, dragOriginSlotIndex, out WayOil emptyWayOil, out int emptySlotIndex);

        float occupiedDistance = hasOccupiedTarget && occupiedWayOil != null
            ? Vector2.Distance(dropPoint, occupiedWayOil.ConnectedOilPaths[occupiedSlotIndex].position)
            : float.MaxValue;
        float emptyDistance = hasEmptyTarget && emptyWayOil != null
            ? Vector2.Distance(dropPoint, emptyWayOil.ConnectedOilPaths[emptySlotIndex].position)
            : float.MaxValue;

        if (hasOccupiedTarget && occupiedDistance <= emptyDistance)
        {
            Oxygen displacedOil = occupiedWayOil.DetachInstalledOil(occupiedSlotIndex);
            occupiedWayOil.AcceptMovedOil(this, occupiedSlotIndex);

            if (displacedOil != null)
            {
                if (dragOriginWayOil != null && dragOriginSlotIndex >= 0)
                {
                    dragOriginWayOil.AcceptMovedOil(displacedOil, dragOriginSlotIndex);
                }
                else
                {
                    displacedOil.transform.position = dragOriginWorldPosition;
                }
            }

            ResetPointerStateAfterDrop();
            return;
        }

        if (hasEmptyTarget)
        {
            emptyWayOil.AcceptMovedOil(this, emptySlotIndex);
            ResetPointerStateAfterDrop();
            return;
        }

        if (dragOriginWayOil != null && dragOriginSlotIndex >= 0)
        {
            dragOriginWayOil.AcceptMovedOil(this, dragOriginSlotIndex);
            ResetPointerStateAfterDrop();
            return;
        }

        transform.position = dragOriginWorldPosition;
        ResetPointerStateAfterDrop();
    }

    private void ReleasePointerHold()
    {
        if (!pointerHeld && !isDragging)
            return;

        bool wasDragging = isDragging;
        pointerHeld = false;
        pointerDownStartedAt = -1f;
        if (wasDragging)
            FinishDrag();
    }

    private void ResetPointerStateAfterDrop()
    {
        VillagePointerCapture.Release(this);
        pointerHeld = false;
        isDragging = false;
        pointerDownStartedAt = -1f;
        dragOriginWayOil = null;
        dragOriginSlotIndex = -1;
    }

    private void ResolveWayOilBinding()
    {
        if (currentWayOil != null && currentSlotIndex >= 0)
            return;

        Transform parent = transform.parent;
        currentWayOil = GetComponentInParent<WayOil>();
        if (currentWayOil != null && parent != null && currentWayOil.TryGetSlotIndex(parent, out int slotIndex))
            currentSlotIndex = slotIndex;
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

    private void EnsureSortingGroup()
    {
        if (sortingGroup == null)
            sortingGroup = GetComponent<SortingGroup>();

        if (sortingGroup == null)
            sortingGroup = gameObject.AddComponent<SortingGroup>();

        sortingGroup.enabled = false;
    }

    private void EnsureInteractionCollider()
    {
        Collider2D collider2D = GetComponent<Collider2D>();
        if (collider2D != null)
            return;

        BoxCollider2D boxCollider = gameObject.AddComponent<BoxCollider2D>();
        boxCollider.isTrigger = true;
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

    private void RestartProduction()
    {
        if (productionRoutine != null)
            StopCoroutine(productionRoutine);

        productionRoutine = StartCoroutine(ProductionRoutine());
    }

    private IEnumerator ProductionRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(productionInterval);

            VillageManagement villageManagement = VillageManagement.EnsureInstance();
            if (villageManagement == null)
                continue;

            UpdateProductionAnimation();
            if (!CanProduce(villageManagement))
            {
                PushState();
                continue;
            }

            villageManagement.TrySpendEnergy(energyUsage);
            storedOxygen += oxygenProduction;
            UpdateExclamation();
            PushState();
        }
    }

    private bool CanProduce(VillageManagement villageManagement)
    {
        return villageManagement != null && villageManagement.CurrentEnergy >= energyUsage;
    }

    private void HandleVillageManagementReady(VillageManagement villageManagement)
    {
        SubscribeVillageManagement(villageManagement);
        UpdateProductionAnimation();
    }

    private void HandleResourceChanged(VillageManagement.ResourceSnapshot snapshot)
    {
        if (snapshot.type == VillageManagement.ResourceType.Energy)
            UpdateProductionAnimation();
    }

    private void SubscribeVillageManagement(VillageManagement villageManagement)
    {
        if (villageManagement == null)
            return;

        villageManagement.ResourceChanged -= HandleResourceChanged;
        villageManagement.ResourceChanged += HandleResourceChanged;
    }

    private void UnsubscribeVillageManagement(VillageManagement villageManagement)
    {
        if (villageManagement == null)
            return;

        villageManagement.ResourceChanged -= HandleResourceChanged;
    }

    private void UpdateProductionAnimation()
    {
        if (animator == null)
            return;

        VillageManagement villageManagement = VillageManagement.Instance;
        bool shouldEnable = CanProduce(villageManagement);
        if (animator.enabled != shouldEnable)
            animator.enabled = shouldEnable;
    }

    private void UpdateExclamation()
    {
        bool shouldShow = storedOxygen > 0 && exclamationPrefab != null;
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
}
