using UnityEngine;

public class VillageManagementDebugProxy : MonoBehaviour
{
    [SerializeField] private VillageManagement target;

    public VillageManagement Target => target != null ? target : VillageManagement.Instance;

    public void Bind(VillageManagement villageManagement)
    {
        target = villageManagement;
    }

    public void SelectRuntimeManager()
    {
        VillageManagement villageManagement = Target;
        if (villageManagement != null)
            target = villageManagement;
    }

    public void ClearPlacedVillageObjects()
    {
        Target?.ClearPlacedVillageObjects();
    }

    public void ResetAllVillageProgress()
    {
        Target?.ResetAllVillageProgress();
    }
}
