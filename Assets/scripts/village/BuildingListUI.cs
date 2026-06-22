using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BuildingListUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [System.Serializable]
    public class BuildingEntry
    {
        public string id;
        public Sprite sprite;
        public Building buildingPrefab;
    }

    [Header("Catalog")]
    [SerializeField] private List<BuildingEntry> entries = new List<BuildingEntry>();

    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private RectTransform itemContainer;
    [SerializeField] private Image itemTemplate;
    [SerializeField] private Button buildButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button leftArrowButton;
    [SerializeField] private Button rightArrowButton;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private TMP_Text timeText;

    [Header("Layout")]
    [SerializeField] private float spacing = 260f;
    [SerializeField] private float smoothSpeed = 12f;
    [SerializeField] private float dragThreshold = 40f;
    [SerializeField] private float centerOvershootScale = 1.1f;
    [SerializeField] private float centerOvershootDuration = 0.3f;
    [SerializeField] private float centerSettleDuration = 0.2f;

    private readonly List<Image> spawnedItems = new List<Image>();
    private Path boundPath;
    private int selectedIndex;
    private float dragDelta;
    private float visualSelection;
    private float dragStartX;
    private int lastCenteredIndex = -1;
    private float centerPulseStartTime = -10f;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);

        if (buildButton != null)
            buildButton.onClick.AddListener(HandleBuildPressed);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
        if (leftArrowButton != null)
            leftArrowButton.onClick.AddListener(() => ShiftSelection(-1));
        if (rightArrowButton != null)
            rightArrowButton.onClick.AddListener(() => ShiftSelection(1));

        RebuildVisuals();
    }

    private void Update()
    {
        if (panel == null || !panel.activeSelf)
            return;

        visualSelection = Mathf.Lerp(visualSelection, selectedIndex - dragDelta, Time.deltaTime * smoothSpeed);
        RefreshVisualStates();
    }

    public void Open(Path path)
    {
        boundPath = path;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, entries.Count - 1));
        visualSelection = selectedIndex;
        lastCenteredIndex = selectedIndex;
        centerPulseStartTime = Time.unscaledTime;
        if (panel != null)
            panel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
        boundPath = null;
        dragDelta = 0f;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragStartX = eventData.position.x;
    }

    public void OnDrag(PointerEventData eventData)
    {
        dragDelta = Mathf.Clamp((eventData.position.x - dragStartX) / spacing, -1f, 1f);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        float deltaPixels = eventData.position.x - dragStartX;
        if (deltaPixels > dragThreshold)
            ShiftSelection(-1);
        else if (deltaPixels < -dragThreshold)
            ShiftSelection(1);

        dragDelta = 0f;
        Refresh();
    }

    public void Refresh()
    {
        RefreshVisualStates();
        RefreshButtonState();
    }

    private void HandleBuildPressed()
    {
        if (boundPath == null || entries.Count == 0)
            return;

        BuildingEntry entry = entries[Mathf.Clamp(selectedIndex, 0, entries.Count - 1)];
        if (entry == null || entry.buildingPrefab == null)
            return;

        boundPath.TryBuildSelected(entry.buildingPrefab);
        Refresh();
    }

    private void ShiftSelection(int delta)
    {
        if (entries.Count == 0)
            return;

        int nextIndex = Mathf.Clamp(selectedIndex + delta, 0, entries.Count - 1);
        if (nextIndex != selectedIndex)
        {
            selectedIndex = nextIndex;
            centerPulseStartTime = Time.unscaledTime;
        }
        Refresh();
    }

    private void RebuildVisuals()
    {
        if (itemTemplate == null || itemContainer == null)
            return;

        itemTemplate.gameObject.SetActive(false);
        for (int i = 0; i < spawnedItems.Count; i++)
        {
            if (spawnedItems[i] != null)
                Destroy(spawnedItems[i].gameObject);
        }
        spawnedItems.Clear();

        for (int i = 0; i < entries.Count; i++)
        {
            Image clone = Instantiate(itemTemplate, itemContainer);
            clone.gameObject.SetActive(true);
            clone.sprite = entries[i].sprite;
            spawnedItems.Add(clone);
        }
    }

    private void RefreshVisualStates()
    {
        VillageManagement villageManagement = VillageManagement.Instance;
        for (int i = 0; i < spawnedItems.Count; i++)
        {
            Image image = spawnedItems[i];
            if (image == null)
                continue;

            float offset = i - visualSelection;
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = new Vector2(offset * spacing, 0f);

            float distance = Mathf.Abs(offset);
            float baseScale = distance < 0.5f ? 1f : 0.7f;
            if (i == selectedIndex && distance < 0.15f)
                baseScale *= GetCenterPulseScale();
            rect.localScale = Vector3.one * baseScale;

            Color color = image.color;
            color.a = distance < 0.5f ? 1f : 0.7f;

            bool owned = villageManagement != null && villageManagement.HasOwnedBuilding(entries[i].id);
            color = owned ? new Color(1f, 1f, 1f, color.a) : new Color(0.45f, 0.45f, 0.45f, color.a);
            image.color = color;
        }

        lastCenteredIndex = selectedIndex;
    }

    private void RefreshButtonState()
    {
        if (entries.Count == 0)
            return;

        VillageManagement villageManagement = VillageManagement.Instance;
        BuildingEntry selected = entries[selectedIndex];
        bool owned = villageManagement != null && villageManagement.HasOwnedBuilding(selected.id);
        int price = selected.buildingPrefab != null && !owned ? selected.buildingPrefab.GetPurchasePriceForLevel(1) : 0;
        float time = selected.buildingPrefab != null && !owned ? selected.buildingPrefab.GetConstructionTimeForLevel(1) : 0f;

        if (priceText != null)
            priceText.text = owned ? string.Empty : $"O2 {price}";
        if (timeText != null)
            timeText.text = owned ? string.Empty : $"{time:0.#}s";

        if (buildButton != null)
            buildButton.interactable = owned || (villageManagement != null && villageManagement.CurrentOxygen >= price);
        if (leftArrowButton != null)
            leftArrowButton.gameObject.SetActive(selectedIndex > 0);
        if (rightArrowButton != null)
            rightArrowButton.gameObject.SetActive(selectedIndex < entries.Count - 1);
    }

    private float GetCenterPulseScale()
    {
        float elapsed = Time.unscaledTime - centerPulseStartTime;
        if (elapsed <= 0f)
            return 1f;

        if (elapsed <= centerOvershootDuration)
            return Mathf.Lerp(1f, centerOvershootScale, elapsed / centerOvershootDuration);

        float settleElapsed = elapsed - centerOvershootDuration;
        if (settleElapsed <= centerSettleDuration)
            return Mathf.Lerp(centerOvershootScale, 1f, settleElapsed / centerSettleDuration);

        return 1f;
    }
}
