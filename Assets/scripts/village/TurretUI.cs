using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class TurretUI : MonoBehaviour
{
    private const float PanelWidth = 300f;
    private const float PanelHeight = 217f;
    private const float ScreenPadding = 12f;

    public static TurretUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private Image ammoFill;
    [SerializeField] private Button ammo30Button;
    [SerializeField] private Button ammo100Button;
    [SerializeField] private Button removeButton;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text ammo30Text;
    [SerializeField] private TMP_Text ammo100Text;
    [SerializeField] private TMP_Text upgradeText;

    private TurretImplementation boundImplementation;
    private BaseTurret boundTurret;
    private UnityAction ammo30Action;
    private UnityAction ammo100Action;
    private UnityAction upgradeAction;
    private int openedFrame = -1;

    public static TurretUI EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        TurretUI existing = FindFirstObjectByType<TurretUI>();
        if (existing != null)
            return existing;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        GameObject uiObject = new GameObject("TurretUI", typeof(RectTransform), typeof(TurretUI));
        Transform parent = canvas != null ? canvas.transform : null;
        uiObject.transform.SetParent(parent, false);
        return uiObject.GetComponent<TurretUI>();
    }

    private void Awake()
    {
        Instance = this;

        ammo30Action = () => TryBuyAmmo(30);
        ammo100Action = () => TryBuyAmmo(100);
        upgradeAction = () => boundImplementation?.TryUpgrade();

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
        if (panel != null && panel.activeSelf && boundTurret != null)
        {
            Refresh();
            HandleOutsidePress();
        }
    }

    public void Open(TurretImplementation implementation, BaseTurret turret)
    {
        boundImplementation = implementation;
        boundTurret = turret;
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
        boundImplementation = null;
        boundTurret = null;
    }

    public void Refresh()
    {
        if (boundTurret == null)
            return;

        if (ammoFill != null)
            ammoFill.fillAmount = boundTurret.AmmoCapacity > 0 ? (float)boundTurret.AmmoCurrent / boundTurret.AmmoCapacity : 0f;

        RefreshAmmoButton(ammo30Button, ammo30Text, 30);
        RefreshAmmoButton(ammo100Button, ammo100Text, 100);

        bool canUpgrade = boundTurret.CanUpgrade();
        if (upgradeButton != null)
            upgradeButton.gameObject.SetActive(canUpgrade);

        if (upgradeText != null)
        {
            BaseTurret upgradePrefab = boundTurret.GetUpgradePrefab();
            upgradeText.text = canUpgrade && upgradePrefab != null ? $"Upgrade O2 {upgradePrefab.CurrentOxygenPrice}" : string.Empty;
        }
        if (upgradeButton != null && VillageManagement.Instance != null)
        {
            BaseTurret upgradePrefab = boundTurret.GetUpgradePrefab();
            upgradeButton.interactable = canUpgrade && upgradePrefab != null &&
                                         VillageManagement.Instance.CurrentOxygen >= upgradePrefab.CurrentOxygenPrice;
        }

        UpdatePanelPosition();
    }

    private void TryBuyAmmo(int percent)
    {
        if (boundTurret == null)
            return;

        boundTurret.TryBuyAmmoPercent(percent);
        Refresh();
    }

    private void RefreshAmmoButton(Button button, TMP_Text label, int percent)
    {
        if (boundTurret == null)
            return;

        int price = boundTurret.GetBulletPriceForPercent(percent);
        if (label != null)
            label.text = $"O2 {price}";

        TMP_Text buttonText = button != null ? button.GetComponentInChildren<TMP_Text>() : null;
        if (buttonText != null)
            buttonText.text = $"{GetAmmoButtonLabel(percent)}  O2 {price}";

        if (button != null && VillageManagement.Instance != null)
            button.interactable = boundTurret.CanRefillPercent(percent) && VillageManagement.Instance.CurrentOxygen >= price;
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
               ammo30Button != null &&
               ammo100Button != null &&
               upgradeButton != null &&
               ammo30Text != null &&
               ammo100Text != null &&
               upgradeText != null;
    }

    private void CreateRuntimeUi()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform;

        panel = new GameObject("TurretManagementPanel", typeof(RectTransform), typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.SetParent(parent, false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.16f, 0.94f);

        CreateChargeRow("Ammo30", panel.transform, 0, out ammo30Text, out ammo30Button);
        CreateChargeRow("Ammo100", panel.transform, 1, out ammo100Text, out ammo100Button);

        upgradeButton = CreateButton("UpgradeButton", panel.transform, new Vector2(12f, 12f), new Vector2(-12f, 42f), () => boundImplementation?.TryUpgrade());
        upgradeText = upgradeButton.GetComponentInChildren<TMP_Text>();

        ammoFill = null;
        BindButtonCallbacks();
    }

    private void CreateChargeRow(string name, Transform parent, int index, out TMP_Text priceText, out Button button)
    {
        GameObject row = new GameObject(name, typeof(RectTransform));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.SetParent(parent, false);
        float top = -12f - (index * 52f);
        float widthFactor = index == 0 ? 1f / 3f : 1f;
        SetRect(rowRect, new Vector2(0f, 1f), new Vector2(widthFactor, 1f), new Vector2(12f, top - 42f), new Vector2(-12f, top));

        priceText = CreateText("Price", row.transform, 14, TextAlignmentOptions.Center);
        priceText.gameObject.SetActive(false);

        button = CreateButton("FillButton", row.transform, Vector2.zero, Vector2.zero, null);
        RectTransform buttonRect = button.GetComponent<RectTransform>();
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
        button.onClick.RemoveAllListeners();

        switch (index)
        {
            case 0:
                button.onClick.AddListener(ammo30Action);
                break;
            default:
                button.onClick.AddListener(ammo100Action);
                break;
        }
    }

    private static string GetAmmoButtonLabel(int percent)
    {
        return percent >= 100 ? "풀" : "1/3";
    }

    private void UpdatePanelPosition()
    {
        if (panel == null || boundImplementation == null)
            return;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        Canvas canvas = panelRect.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
        Camera cameraRef = Camera.main;
        Transform anchor = boundTurret is Turret configuredTurret ? configuredTurret.UiAnchor : null;
        if (anchor == null)
            anchor = boundImplementation.transform;

        Vector3 worldPoint = anchor != null ? anchor.position : boundImplementation.transform.position;
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

        if (IsPointerOverTurretSlot(screenPoint))
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

    private bool IsPointerOverTurretSlot(Vector2 screenPoint)
    {
        if (boundImplementation == null)
            return false;

        Camera cameraRef = Camera.main;
        if (cameraRef == null)
            return false;

        Vector3 worldPoint = cameraRef.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, Mathf.Abs(cameraRef.transform.position.z)));
        Collider2D[] colliders = boundImplementation.GetComponentsInChildren<Collider2D>(true);
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
        BindButton(ammo30Button, ammo30Action);
        BindButton(ammo100Button, ammo100Action);
        BindButton(upgradeButton, upgradeAction);
    }

    private static void BindButton(Button button, UnityAction action)
    {
        if (button == null || action == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static TMP_Text CreateText(string name, Transform parent, int size, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        return text;
    }

    private static Button CreateButton(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax, UnityEngine.Events.UnityAction onClick)
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

        TMP_Text label = CreateText("Label", obj.transform, 24, TextAlignmentOptions.Center);
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
