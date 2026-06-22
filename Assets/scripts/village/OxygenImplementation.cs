using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider2D))]
public class OxygenImplementation : MonoBehaviour
{
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
    [SerializeField] private Oxygen level1Prefab;
    [SerializeField] private Oxygen level2Prefab;
    [SerializeField] private Oxygen level3Prefab;
    [SerializeField] private OxygenListUI oxygenListUI;
    [SerializeField] private OxygenGeneratorUI oxygenGeneratorUI;

    private Oxygen currentOxygen;

    private void OnMouseUpAsButton()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector3 world = GetPointerWorldPosition();
        Vector2 local = transform.InverseTransformPoint(world);
        if (!placementArea.Contains(local))
            return;

        if (currentOxygen == null)
            OpenList();
        else
            OpenUi();
    }

    public void TryInstall(Oxygen oxygenPrefab, bool ownedAlready)
    {
        if (currentOxygen != null || oxygenPrefab == null)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        if (!ownedAlready)
        {
            if (!villageManagement.TrySpendOxygen(oxygenPrefab.CurrentOxygenPrice))
                return;

            villageManagement.AddOwnedOxygen(oxygenPrefab.OxygenId);
        }

        PlaceOxygen(GetPrefabForLevel(oxygenPrefab.Level, oxygenPrefab), oxygenPrefab.Level);
        oxygenListUI?.Close();
    }

    public void TryUpgrade()
    {
        if (currentOxygen == null || currentOxygen.Level >= 3 || currentOxygen.UpgradePrefab == null || VillageManagement.Instance == null)
            return;

        Oxygen upgradePrefab = currentOxygen.UpgradePrefab;
        if (!VillageManagement.Instance.TrySpendOxygen(upgradePrefab.CurrentOxygenPrice))
            return;

        Destroy(currentOxygen.gameObject);
        PlaceOxygen(GetPrefabForLevel(upgradePrefab.Level, upgradePrefab), upgradePrefab.Level);
        oxygenGeneratorUI?.Open(this, currentOxygen);
    }

    public void RemoveOxygen()
    {
        if (currentOxygen == null)
            return;

        Destroy(currentOxygen.gameObject);
        currentOxygen = null;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
            villageManagement.RemoveOxygenGeneratorState(slotId);
    }

    private void OpenList()
    {
        if (oxygenListUI == null)
            oxygenListUI = FindFirstObjectByType<OxygenListUI>();
        if (oxygenListUI != null)
            oxygenListUI.Open(this);
    }

    private void OpenUi()
    {
        if (oxygenGeneratorUI == null)
            oxygenGeneratorUI = FindFirstObjectByType<OxygenGeneratorUI>();
        if (oxygenGeneratorUI != null)
            oxygenGeneratorUI.Open(this, currentOxygen);
    }

    private void PlaceOxygen(Oxygen prefab, int level)
    {
        currentOxygen = Instantiate(prefab, transform);
        currentOxygen.AssignSlot(slotId);
        currentOxygen.transform.localPosition = new Vector3(
            placeLocalPosition.x - currentOxygen.BottomLocalPosition.x,
            placeLocalPosition.y - currentOxygen.BottomLocalPosition.y,
            0f);
        currentOxygen.SetLevel(level);
        currentOxygen.PushState();
    }

    private Oxygen GetPrefabForLevel(int level, Oxygen fallback)
    {
        if (level >= 3 && level3Prefab != null)
            return level3Prefab;
        if (level == 2 && level2Prefab != null)
            return level2Prefab;
        if (level <= 1 && level1Prefab != null)
            return level1Prefab;

        return fallback;
    }

    private Vector3 GetPointerWorldPosition()
    {
        Camera cameraRef = Camera.main;
        Vector3 screen = Input.mousePosition;
        if (cameraRef == null)
            return screen;

        screen.z = Mathf.Abs(cameraRef.transform.position.z - transform.position.z);
        return cameraRef.ScreenToWorldPoint(screen);
    }
}
