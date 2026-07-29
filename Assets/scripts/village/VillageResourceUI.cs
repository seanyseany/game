using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class VillageResourceUI : MonoBehaviour
{
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private Slider resourceSlider;
    [SerializeField] private string prefix;

    private VillageManagement boundManager;

    protected abstract VillageManagement.ResourceType ResourceType { get; }

    protected virtual void Awake()
    {
        ResolveReferences();
    }

    protected virtual void OnEnable()
    {
        ResolveReferences();
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
        if (snapshot.type != ResourceType)
            return;

        if (valueText != null)
        {
            valueText.text = string.IsNullOrEmpty(prefix)
                ? $"{snapshot.current}/{snapshot.capacity}"
                : $"{prefix}{snapshot.current}/{snapshot.capacity}";
        }

        if (resourceSlider != null)
        {
            int capacity = Mathf.Max(0, snapshot.capacity);
            resourceSlider.minValue = 0f;
            resourceSlider.maxValue = capacity > 0 ? capacity : 1f;
            resourceSlider.wholeNumbers = true;
            resourceSlider.interactable = false;
            resourceSlider.transition = Selectable.Transition.None;
            resourceSlider.value = Mathf.Clamp(snapshot.current, 0, capacity > 0 ? capacity : 1);
        }
    }

    private void ResolveReferences()
    {
        if (resourceSlider == null)
            resourceSlider = GetComponent<Slider>();

        if (valueText == null)
            valueText = GetComponentInChildren<TMP_Text>(true);
    }
}
