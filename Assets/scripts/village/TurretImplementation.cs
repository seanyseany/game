using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider2D))]
public class TurretImplementation : MonoBehaviour
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
    [SerializeField] private BaseTurret turretLevel1Prefab;
    [SerializeField] private BaseTurret turretLevel2Prefab;
    [SerializeField] private BaseTurret turretLevel3Prefab;
    [SerializeField] private TurretListUI turretListUI;
    [SerializeField] private TurretUI turretUI;

    private BaseTurret currentTurret;

    public string SlotId => slotId;
    public BaseTurret CurrentTurret => currentTurret;

    private void OnMouseUpAsButton()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector3 world = GetPointerWorldPosition();
        Vector2 local = transform.InverseTransformPoint(world);
        if (!placementArea.Contains(local))
            return;

        if (currentTurret == null)
            OpenList();
        else
            OpenTurretUI();
    }

    public void TryInstall(BaseTurret turretPrefab, bool ownedAlready)
    {
        if (currentTurret != null || turretPrefab == null)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        if (!ownedAlready)
        {
            if (!villageManagement.TrySpendOxygen(turretPrefab.CurrentOxygenPrice))
                return;

            villageManagement.AddOwnedTurret(turretPrefab.TurretId);
        }

        PlaceTurret(GetPrefabForLevel(turretPrefab.Level, turretPrefab), turretPrefab.Level, false);
        turretListUI?.Close();
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

        Destroy(currentTurret.gameObject);
        currentTurret = Instantiate(GetPrefabForLevel(upgradePrefab.Level, upgradePrefab), transform);
        currentTurret.AssignSlot(slotId);
        currentTurret.transform.localPosition = new Vector3(
            placeLocalPosition.x - currentTurret.BottomLocalPosition.x,
            placeLocalPosition.y - currentTurret.BottomLocalPosition.y,
            0f);
        currentTurret.ApplyLevel(upgradePrefab.Level, true);
        currentTurret.PushState();
        turretUI?.Open(this, currentTurret);
    }

    public void RemoveTurret()
    {
        if (currentTurret == null)
            return;

        Destroy(currentTurret.gameObject);
        currentTurret = null;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
            villageManagement.RemoveTurretState(slotId);

        turretUI?.Close();
    }

    private void OpenList()
    {
        if (turretListUI == null)
            turretListUI = FindFirstObjectByType<TurretListUI>();
        if (turretListUI != null)
            turretListUI.Open(this);
    }

    private void OpenTurretUI()
    {
        if (turretUI == null)
            turretUI = FindFirstObjectByType<TurretUI>();
        if (turretUI != null)
            turretUI.Open(this, currentTurret);
    }

    private void PlaceTurret(BaseTurret prefab, int level, bool keepAmmoRatio)
    {
        currentTurret = Instantiate(prefab, transform);
        currentTurret.AssignSlot(slotId);
        currentTurret.transform.localPosition = new Vector3(
            placeLocalPosition.x - currentTurret.BottomLocalPosition.x,
            placeLocalPosition.y - currentTurret.BottomLocalPosition.y,
            0f);
        currentTurret.ApplyLevel(level, keepAmmoRatio);
        currentTurret.PushState();
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
