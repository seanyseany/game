using TMPro;
using UnityEngine;

public abstract class VillageResourceUI : MonoBehaviour
{
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private string prefix;

    private VillageManagement boundManager;

    protected abstract VillageManagement.ResourceType ResourceType { get; }

    protected virtual void OnEnable()
    {
        TryBind(VillageManagement.Instance);
        VillageManagement.InstanceReady += TryBind;
    }

    protected virtual void OnDisable()
    {
        VillageManagement.InstanceReady -= TryBind;

        if (boundManager == null)
            return;

        boundManager.ResourceChanged -= HandleResourceChanged;
        boundManager = null;
    }

    private void TryBind(VillageManagement manager)
    {
        if (manager == null || boundManager == manager)
            return;

        if (boundManager != null)
            boundManager.ResourceChanged -= HandleResourceChanged;

        boundManager = manager;
        boundManager.ResourceChanged += HandleResourceChanged;
        HandleResourceChanged(boundManager.GetSnapshot(ResourceType));
    }

    private void HandleResourceChanged(VillageManagement.ResourceSnapshot snapshot)
    {
        if (snapshot.type != ResourceType || valueText == null)
            return;

        valueText.text = string.IsNullOrEmpty(prefix)
            ? $"{snapshot.current}/{snapshot.capacity}"
            : $"{prefix}{snapshot.current}/{snapshot.capacity}";
    }
}
