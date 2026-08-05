using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class BuildingUI : MonoBehaviour
{
    private const float PanelWidth = 300f;
    private const float PanelHeight = 164f;
    private const float ScreenPadding = 12f;

    public static BuildingUI Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Image workerImage;
    [SerializeField] private Image salaryFill;
    [SerializeField] private Button pay30Button;
    [SerializeField] private Button pay100Button;
    [SerializeField] private Button removeButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text pay30Text;
    [SerializeField] private TMP_Text pay100Text;

    private Path boundPath;
    private Building boundBuilding;
    private UnityAction pay30Action;
    private UnityAction pay100Action;
    private int openedFrame = -1;

    public static BuildingUI EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        BuildingUI existing = FindFirstObjectByType<BuildingUI>();
        if (existing != null)
            return existing;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        GameObject uiObject = new GameObject("BuildingUI", typeof(RectTransform), typeof(BuildingUI));
        Transform parent = canvas != null ? canvas.transform : null;
        uiObject.transform.SetParent(parent, false);
        return uiObject.GetComponent<BuildingUI>();
    }

    private void Awake()
    {
        Instance = this;

        pay30Action = () => TryPay(30);
        pay100Action = () => TryPay(100);
        EnsureRuntimeUi();

        if (panel != null)
            panel.SetActive(false);

        BindButtonCallbacks();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (panel != null && panel.activeSelf && boundBuilding != null)
        {
            Refresh();
            HandleOutsidePress();
        }
    }

    public void Open(Path path, Building building)
    {
        boundPath = path;
        boundBuilding = building;
        EnsureRuntimeUi();
        if (panel != null)
        {
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
        }
        openedFrame = Time.frameCount;
        Refresh();
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
        boundPath = null;
        boundBuilding = null;
    }

    public void Refresh()
    {
        if (boundBuilding == null)
            return;

        if (workerImage != null)
            workerImage.enabled = false;
        if (salaryFill != null)
            salaryFill.fillAmount = boundBuilding.MaxSalary > 0 ? (float)boundBuilding.CurrentSalary / boundBuilding.MaxSalary : 0f;

        RefreshPayButton(pay30Button, pay30Text, 30);
        RefreshPayButton(pay100Button, pay100Text, 100);
        UpdatePanelPosition();
    }

    private void TryPay(int percent)
    {
        if (boundBuilding == null || VillageManagement.Instance == null)
            return;

        int price = boundBuilding.GetSalaryPriceForPercent(percent);
        if (!boundBuilding.CanReceiveSalaryPercent(percent))
            return;
        if (!VillageManagement.Instance.TrySpendOxygen(price))
            return;

        boundBuilding.TryAddSalaryPercent(percent);
        Refresh();
    }

    private void RefreshPayButton(Button button, TMP_Text label, int percent)
    {
        if (boundBuilding == null)
            return;

        int price = boundBuilding.GetSalaryPriceForPercent(percent);
        if (label != null)
            label.text = $"O2 {price}";

        TMP_Text buttonText = button != null ? button.GetComponentInChildren<TMP_Text>() : null;
        if (buttonText != null)
            buttonText.text = $"{GetPayButtonLabel(percent)}  O2 {price}";

        if (button != null && VillageManagement.Instance != null)
        {
            button.interactable = boundBuilding.CanReceiveSalaryPercent(percent) &&
                                 VillageManagement.Instance.CurrentOxygen >= price;
        }
    }

    private void EnsureRuntimeUi()
    {
        if (HasRequiredReferences())
            return;

        if (panel != null)
            Destroy(panel);

        CreateRuntimeUi();
    }

    private bool HasRequiredReferences()
    {
        return panel != null &&
               pay30Button != null &&
               pay100Button != null &&
               pay30Text != null &&
               pay100Text != null;
    }

    private void CreateRuntimeUi()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform;
        Font fallbackFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        panel = new GameObject("BuildingManagementPanel", typeof(RectTransform), typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.SetParent(parent, false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.16f, 0.94f);

        CreateChargeRow("Charge30", panel.transform, fallbackFont, 0, out pay30Text, out pay30Button);
        CreateChargeRow("Charge100", panel.transform, fallbackFont, 1, out pay100Text, out pay100Button);

        workerImage = null;
        salaryFill = null;
        BindButtonCallbacks();
    }

    private void CreateChargeRow(string name, Transform parent, Font fallbackFont, int index, out TMP_Text priceText, out Button button)
    {
        GameObject row = new GameObject(name, typeof(RectTransform));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.SetParent(parent, false);
        float top = -12f - (index * 52f);
        float widthFactor = index == 0 ? 1f / 3f : 1f;
        SetRect(rowRect, new Vector2(0f, 1f), new Vector2(widthFactor, 1f), new Vector2(12f, top - 42f), new Vector2(-12f, top));

        priceText = CreateText("Price", row.transform, fallbackFont, 14, TextAlignmentOptions.Center);
        priceText.gameObject.SetActive(false);

        button = CreateButton("FillButton", row.transform, fallbackFont, Vector2.zero, Vector2.zero, null);
        RectTransform buttonRect = button.GetComponent<RectTransform>();
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
        button.onClick.RemoveAllListeners();

        switch (index)
        {
            case 0:
                button.onClick.AddListener(pay30Action);
                break;
            default:
                button.onClick.AddListener(pay100Action);
                break;
        }
    }

    private static string GetPayButtonLabel(int percent)
    {
        return percent >= 100 ? "풀" : "1/3";
    }

    private void UpdatePanelPosition()
    {
        if (panel == null || boundBuilding == null)
            return;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        Canvas canvas = panelRect.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
        Camera cameraRef = Camera.main;
        Transform anchor = boundBuilding.UiAnchor;
        Vector3 worldPoint = anchor != null ? anchor.position : boundBuilding.transform.position;
        Vector3 screenPoint = cameraRef != null
            ? cameraRef.WorldToScreenPoint(worldPoint)
            : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

        if (canvasRect == null)
        {
            panelRect.anchoredPosition = screenPoint;
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPoint,
            canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null,
            out Vector2 localPoint);

        float minX = canvasRect.rect.xMin + ScreenPadding;
        float maxX = canvasRect.rect.xMax - ScreenPadding - panelRect.rect.width;
        float minY = canvasRect.rect.yMin + ScreenPadding + panelRect.rect.height * panelRect.pivot.y;
        float maxY = canvasRect.rect.yMax - ScreenPadding - panelRect.rect.height * (1f - panelRect.pivot.y);

        localPoint.x = Mathf.Clamp(localPoint.x, minX, maxX);
        localPoint.y = Mathf.Clamp(localPoint.y, minY, maxY);
        panelRect.anchoredPosition = localPoint;
    }

    private void HandleOutsidePress()
    {
        if (Time.frameCount == openedFrame || !TryGetPointerDownScreenPoint(out Vector2 screenPoint))
            return;

        RectTransform panelRect = panel != null ? panel.GetComponent<RectTransform>() : null;
        Canvas canvas = panelRect != null ? panelRect.GetComponentInParent<Canvas>() : null;
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        if (panelRect != null && RectTransformUtility.RectangleContainsScreenPoint(panelRect, screenPoint, eventCamera))
            return;

        if (IsPointerOverBuilding(screenPoint))
            return;

        Close();
    }

    private static bool TryGetPointerDownScreenPoint(out Vector2 screenPoint)
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                screenPoint = touch.position;
                return true;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            screenPoint = Input.mousePosition;
            return true;
        }

        screenPoint = default;
        return false;
    }

    private bool IsPointerOverBuilding(Vector2 screenPoint)
    {
        if (boundBuilding == null)
            return false;

        Camera cameraRef = Camera.main;
        if (cameraRef == null)
            return false;

        Vector3 worldPoint = cameraRef.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, Mathf.Abs(cameraRef.transform.position.z)));
        Collider2D[] colliders = boundBuilding.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider2D = colliders[i];
            if (collider2D != null && collider2D.OverlapPoint(worldPoint))
                return true;
        }

        return false;
    }

    private void BindButtonCallbacks()
    {
        BindButton(pay30Button, pay30Action);
        BindButton(pay100Button, pay100Action);
    }

    private static void BindButton(Button button, UnityAction action)
    {
        if (button == null || action == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static TMP_Text CreateText(string name, Transform parent, Font fallbackFont, int size, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        if (text.font == null && fallbackFont != null)
            text.fontSharedMaterial = null;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        return text;
    }

    private static Button CreateButton(string name, Transform parent, Font fallbackFont, Vector2 offsetMin, Vector2 offsetMax, UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = obj.GetComponent<Image>();
        image.color = new Color(0.92f, 0.58f, 0.18f, 1f);

        Button button = obj.GetComponent<Button>();
        if (onClick != null)
            button.onClick.AddListener(onClick);

        TMP_Text label = CreateText("Label", obj.transform, fallbackFont, 24, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
        return button;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
