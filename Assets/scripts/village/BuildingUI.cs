using UnityEngine;

// Kept so existing scenes/prefabs and Path.Close() references remain valid.
// Buildings now receive stock through box deliveries; the oxygen refill panel is retired.
[AddComponentMenu("")]
public class BuildingUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    public static BuildingUI Instance { get; private set; }

    public static BuildingUI EnsureInstance()
    {
        if (Instance != null) return Instance;
        BuildingUI existing = FindFirstObjectByType<BuildingUI>(FindObjectsInactive.Include);
        return existing != null ? existing : new GameObject("BuildingUI").AddComponent<BuildingUI>();
    }

    private void Awake()
    {
        Instance = this;
        Close();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Open(Path path, Building building) { Close(); }
    public void Refresh() { }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);
    }
}
