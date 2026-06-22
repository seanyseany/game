using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ArcadeUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [System.Serializable]
    public class WhiteBloodCellEntry
    {
        public string id;
        public Sprite sprite;
        public int oxygenPrice;
    }

    [SerializeField] private List<WhiteBloodCellEntry> entries = new List<WhiteBloodCellEntry>();
    [SerializeField] private GameObject panel;
    [SerializeField] private RectTransform itemContainer;
    [SerializeField] private Image itemTemplate;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private float spacing = 220f;
    [SerializeField] private float dragThreshold = 40f;
    [SerializeField] private float smoothSpeed = 12f;
    [SerializeField] private float centerOvershootScale = 1.1f;
    [SerializeField] private float centerOvershootDuration = 0.3f;
    [SerializeField] private float centerSettleDuration = 0.2f;

    private readonly List<Image> spawnedItems = new List<Image>();
    private int selectedIndex;
    private float dragStartX;
    private float dragDelta;
    private float visualSelection;
    private float centerPulseStartTime = -10f;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);
        if (purchaseButton != null)
            purchaseButton.onClick.AddListener(HandlePurchase);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        RebuildVisuals();
    }

    private void Update()
    {
        if (panel == null || !panel.activeSelf)
            return;

        visualSelection = Mathf.Lerp(visualSelection, selectedIndex - dragDelta, Time.deltaTime * smoothSpeed);
        for (int i = 0; i < spawnedItems.Count; i++)
        {
            Image image = spawnedItems[i];
            if (image == null)
                continue;

            float offset = i - visualSelection;
            image.rectTransform.anchoredPosition = new Vector2(offset * spacing, 0f);

            float scale = Mathf.Abs(offset) < 0.5f ? 1f : 0.7f;
            if (i == selectedIndex && Mathf.Abs(offset) < 0.15f)
                scale *= GetCenterPulseScale();
            image.rectTransform.localScale = Vector3.one * scale;

            Color color = image.color;
            color.a = Mathf.Abs(offset) < 0.5f ? 1f : 0.7f;
            bool owned = VillageManagement.Instance != null && VillageManagement.Instance.HasOwnedWhiteBloodCell(entries[i].id);
            image.color = owned ? color : new Color(0.45f, 0.45f, 0.45f, color.a);
        }

        Refresh();
    }

    public void Open()
    {
        visualSelection = selectedIndex;
        centerPulseStartTime = Time.unscaledTime;
        if (panel != null)
            panel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
        dragDelta = 0f;
    }

    public void OnBeginDrag(PointerEventData eventData) => dragStartX = eventData.position.x;

    public void OnDrag(PointerEventData eventData)
    {
        dragDelta = Mathf.Clamp((eventData.position.x - dragStartX) / spacing, -1f, 1f);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        float delta = eventData.position.x - dragStartX;
        if (delta > dragThreshold)
            Shift(-1);
        else if (delta < -dragThreshold)
            Shift(1);

        dragDelta = 0f;
    }

    private void HandlePurchase()
    {
        if (entries.Count == 0 || VillageManagement.Instance == null)
            return;

        WhiteBloodCellEntry selected = entries[selectedIndex];
        if (VillageManagement.Instance.HasOwnedWhiteBloodCell(selected.id))
        {
            VillageManagement.Instance.SetSelectedWhiteBloodCell(selected.id);
            return;
        }

        if (!VillageManagement.Instance.TrySpendOxygen(selected.oxygenPrice))
            return;

        VillageManagement.Instance.AddOwnedWhiteBloodCell(selected.id);
        VillageManagement.Instance.SetSelectedWhiteBloodCell(selected.id);
        Refresh();
    }

    private void Shift(int delta)
    {
        int nextIndex = Mathf.Clamp(selectedIndex + delta, 0, Mathf.Max(0, entries.Count - 1));
        if (nextIndex != selectedIndex)
        {
            selectedIndex = nextIndex;
            centerPulseStartTime = Time.unscaledTime;
        }
    }

    private void Refresh()
    {
        if (entries.Count == 0)
            return;

        WhiteBloodCellEntry selected = entries[selectedIndex];
        bool owned = VillageManagement.Instance != null && VillageManagement.Instance.HasOwnedWhiteBloodCell(selected.id);
        bool affordable = VillageManagement.Instance != null && VillageManagement.Instance.CurrentOxygen >= selected.oxygenPrice;

        if (priceText != null)
            priceText.text = owned ? string.Empty : $"O2 {selected.oxygenPrice}";
        if (purchaseButton != null)
        {
            purchaseButton.gameObject.SetActive(!owned);
            purchaseButton.interactable = !owned && affordable;
        }
    }

    private void RebuildVisuals()
    {
        if (itemTemplate == null || itemContainer == null)
            return;

        itemTemplate.gameObject.SetActive(false);
        for (int i = 0; i < entries.Count; i++)
        {
            Image clone = Instantiate(itemTemplate, itemContainer);
            clone.gameObject.SetActive(true);
            clone.sprite = entries[i].sprite;
            spawnedItems.Add(clone);
        }
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
