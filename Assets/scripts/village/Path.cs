using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider2D))]
public class Path : MonoBehaviour
{
    [SerializeField] private string pathId;
    [SerializeField] private Building buildingInstance;
    [SerializeField] private Transform[] roamPoints;
    [SerializeField] private Construction constructionPrefab;
    [SerializeField] private BuildingListUI buildingListUI;
    [SerializeField] private BuildingUI buildingUI;

    private Collider2D cachedCollider;
    private Construction activeConstruction;

    public string PathId => pathId;
    public Building Building => buildingInstance;
    public Transform[] RoamPoints => roamPoints;

    private void Awake()
    {
        cachedCollider = GetComponent<Collider2D>();
    }

    public float GetActivationScore()
    {
        if (buildingInstance == null || !buildingInstance.IsPlaced)
            return 0f;

        return buildingInstance.IsWorking ? 2f : 0.5f;
    }

    public Vector3 GetRandomWorldPointOnPath()
    {
        if (roamPoints != null && roamPoints.Length > 0)
        {
            Transform point = roamPoints[Random.Range(0, roamPoints.Length)];
            if (point != null)
                return point.position;
        }

        Bounds bounds = cachedCollider != null ? cachedCollider.bounds : new Bounds(transform.position, Vector3.one);
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y),
            0f);
    }

    private void OnMouseUpAsButton()
    {
        if (activeConstruction != null)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (buildingInstance == null)
            OpenBuildingList();
        else
            OpenBuildingUI();
    }

    public void TryBuildSelected(Building selectedPrefab)
    {
        if (selectedPrefab == null || activeConstruction != null || buildingInstance != null)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        bool alreadyOwned = villageManagement.HasOwnedBuilding(selectedPrefab.BuildingId);
        int targetLevel = 1;
        int price = alreadyOwned ? 0 : selectedPrefab.GetPurchasePriceForLevel(targetLevel);
        float constructionTime = alreadyOwned ? 0f : selectedPrefab.GetConstructionTimeForLevel(targetLevel);

        if (price > 0 && !villageManagement.TrySpendOxygen(price))
            return;

        if (!alreadyOwned)
            villageManagement.AddOwnedBuilding(selectedPrefab.BuildingId);

        if (constructionTime > 0f && constructionPrefab != null)
            BeginConstruction(selectedPrefab, targetLevel, constructionTime, false);
        else
            PlaceBuildingImmediately(selectedPrefab, targetLevel, false);

        buildingListUI?.Close();
    }

    public void TryUpgradeCurrentBuilding()
    {
        if (buildingInstance == null || buildingInstance.Level >= 2 || activeConstruction != null)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        int targetLevel = 2;
        int price = buildingInstance.GetPurchasePriceForLevel(targetLevel);
        if (!villageManagement.TrySpendOxygen(price))
            return;

        float constructionTime = buildingInstance.GetConstructionTimeForLevel(targetLevel);
        if (constructionTime > 0f && constructionPrefab != null)
            BeginConstruction(buildingInstance, targetLevel, constructionTime, true);
        else
            buildingInstance.SetLevel(targetLevel);

        buildingUI?.Refresh();
    }

    public void RemoveCurrentBuilding()
    {
        if (buildingInstance == null)
            return;

        Destroy(buildingInstance.gameObject);
        buildingInstance = null;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
        {
            villageManagement.RemoveBuildingState(pathId);
            villageManagement.SetEnergyCapacity(Mathf.Max(0, Mathf.RoundToInt(villageManagement.EnergyCapacity / 1.2f)));
        }

        if (buildingUI != null)
            buildingUI.Close();
    }

    private void OpenBuildingList()
    {
        if (buildingListUI == null)
            buildingListUI = FindFirstObjectByType<BuildingListUI>();

        if (buildingListUI != null)
            buildingListUI.Open(this);
    }

    private void OpenBuildingUI()
    {
        if (buildingUI == null)
            buildingUI = FindFirstObjectByType<BuildingUI>();

        if (buildingUI != null)
            buildingUI.Open(this, buildingInstance);
    }

    private void BeginConstruction(Building buildingPrefab, int targetLevel, float duration, bool upgrading)
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
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
                constructionRemainingSeconds = duration
            });
        }

        if (activeConstruction != null)
            Destroy(activeConstruction.gameObject);

        activeConstruction = Instantiate(constructionPrefab, transform);
        activeConstruction.transform.localPosition = Vector3.zero;
        activeConstruction.Begin(duration, targetLevel, () =>
        {
            activeConstruction = null;
            if (upgrading && buildingInstance != null)
            {
                buildingInstance.SetLevel(targetLevel);
            }
            else
            {
                PlaceBuildingImmediately(buildingPrefab, targetLevel, false);
            }

            if (buildingUI != null && buildingInstance != null)
                buildingUI.Open(this, buildingInstance);
        });
    }

    private void PlaceBuildingImmediately(Building buildingPrefab, int level, bool skipCapacityBonus)
    {
        if (buildingPrefab == null)
            return;

        if (buildingInstance != null)
            Destroy(buildingInstance.gameObject);

        buildingInstance = Instantiate(buildingPrefab, transform);
        buildingInstance.AssignSlot(pathId);
        buildingInstance.transform.localPosition = new Vector3(
            -buildingInstance.BottomLocalPosition.x,
            -buildingInstance.BottomLocalPosition.y,
            0f);
        buildingInstance.SetLevel(level);
        buildingInstance.MarkPlaced(true);

        if (!skipCapacityBonus)
        {
            VillageManagement villageManagement = VillageManagement.EnsureInstance();
            if (villageManagement != null)
                villageManagement.SetEnergyCapacity(Mathf.RoundToInt(villageManagement.EnergyCapacity * 1.2f));
        }

        buildingInstance.PushStateToVillageManagement();
    }
}
