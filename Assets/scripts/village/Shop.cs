using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Shop : MonoBehaviour
{
    private static readonly List<Shop> AllShops = new List<Shop>();

    [Header("Sections")]
    [SerializeField] private BuildingListUI buildingSectionUI;
    [SerializeField] private ShopSectionUI playerSectionUI;
    [SerializeField] private ShopSectionUI turretSectionUI;
    [SerializeField] private ShopSectionUI oilSectionUI;

    private readonly List<Button> tabButtons = new List<Button>();
    private readonly List<ShopSectionUI> sectionOrder = new List<ShopSectionUI>();

    [Header("UI")]
    [SerializeField] private Button openButton;

    private Canvas rootCanvas;
    private RectTransform panelRoot;
    private RectTransform leftMenuRoot;
    private RectTransform contentRoot;
    private Text titleText;
    private Button closeButton;
    private ShopSectionUI activeSection;
    private bool uiBuilt;
    private GameObject emptyContentRoot;
    private bool sectionReferencesResolved;

    private void Awake()
    {
        if (!AllShops.Contains(this))
            AllShops.Add(this);

        if (openButton == null)
            openButton = GetComponent<Button>();

        if (openButton != null)
            openButton.onClick.AddListener(OpenShop);

        ResolveSectionReferences();
        BuildUiIfNeeded();
        RebuildSectionOrder();
        ShowSection(GetDefaultSection());
        SetPanelVisible(false);
    }

    private void OnEnable()
    {
        if (!AllShops.Contains(this))
            AllShops.Add(this);

        VillageManagement.InstanceReady += HandleVillageReady;
        if (VillageManagement.Instance != null)
            VillageManagement.Instance.SaveDataChanged += HandleSaveDataChanged;
    }

    private void OnDisable()
    {
        AllShops.Remove(this);
        VillageManagement.InstanceReady -= HandleVillageReady;
        if (VillageManagement.Instance != null)
            VillageManagement.Instance.SaveDataChanged -= HandleSaveDataChanged;
    }

    public void OpenShop()
    {
        ResolveSectionReferences();
        BuildUiIfNeeded();
        RebuildSectionOrder();
        SetPanelVisible(true);
        panelRoot.SetAsLastSibling();
        ShowSection(GetDefaultSection());
    }

    public void CloseShop()
    {
        SetPanelVisible(false);
    }

    public static void CloseAllShops()
    {
        for (int i = 0; i < AllShops.Count; i++)
        {
            if (AllShops[i] != null)
                AllShops[i].CloseShop();
        }
    }

    private void HandleVillageReady(VillageManagement villageManagement)
    {
        if (villageManagement == null)
            return;

        villageManagement.SaveDataChanged -= HandleSaveDataChanged;
        villageManagement.SaveDataChanged += HandleSaveDataChanged;
        RefreshActiveSection();
    }

    private void HandleSaveDataChanged(VillageManagement.VillageSaveData _)
    {
        RefreshActiveSection();
    }

    private void BuildUiIfNeeded()
    {
        if (uiBuilt)
            return;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        rootCanvas = GetComponentInParent<Canvas>();
        Transform panelParent = rootCanvas != null ? rootCanvas.transform : transform;

        GameObject panelObject = CreateUiObject("ShopPanel", panelParent);
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.11f, 0.16f, 0.96f);
        panelRoot = panelObject.GetComponent<RectTransform>();
        panelRoot.anchorMin = new Vector2(0.08f, 0.1f);
        panelRoot.anchorMax = new Vector2(0.92f, 0.9f);
        panelRoot.offsetMin = Vector2.zero;
        panelRoot.offsetMax = Vector2.zero;

        titleText = CreateText("Title", panelObject.transform, font, 42, TextAnchor.MiddleLeft);
        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(32f, -92f);
        titleRect.offsetMax = new Vector2(-160f, -20f);
        titleText.text = "Shop";

        closeButton = CreateButton("CloseButton", panelObject.transform, new Vector2(120f, 56f), new Vector2(-90f, -60f), "Close", font);
        RectTransform closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 1f);
        closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeButton.onClick.AddListener(CloseShop);

        GameObject bodyObject = CreateUiObject("Body", panelObject.transform);
        RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.offsetMin = new Vector2(24f, 24f);
        bodyRect.offsetMax = new Vector2(-24f, -104f);

        leftMenuRoot = CreateUiObject("LeftMenu", panelObject.transform).GetComponent<RectTransform>();
        leftMenuRoot.SetParent(bodyObject.transform, false);
        leftMenuRoot.anchorMin = new Vector2(0f, 0f);
        leftMenuRoot.anchorMax = new Vector2(0.2f, 1f);
        leftMenuRoot.offsetMin = new Vector2(0f, 0f);
        leftMenuRoot.offsetMax = new Vector2(-12f, 0f);

        Image leftMenuImage = leftMenuRoot.gameObject.AddComponent<Image>();
        leftMenuImage.color = new Color(0.13f, 0.17f, 0.23f, 0.98f);

        VerticalLayoutGroup menuLayout = leftMenuRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        menuLayout.spacing = 12f;
        menuLayout.padding = new RectOffset(16, 16, 16, 16);
        menuLayout.childForceExpandWidth = true;
        menuLayout.childForceExpandHeight = false;
        menuLayout.childControlWidth = true;
        menuLayout.childControlHeight = true;

        contentRoot = CreateUiObject("Content", panelObject.transform).GetComponent<RectTransform>();
        contentRoot.SetParent(bodyObject.transform, false);
        contentRoot.anchorMin = new Vector2(0.2f, 0f);
        contentRoot.anchorMax = new Vector2(1f, 1f);
        contentRoot.offsetMin = new Vector2(12f, 0f);
        contentRoot.offsetMax = new Vector2(0f, 0f);

        Image contentImage = contentRoot.gameObject.AddComponent<Image>();
        contentImage.color = new Color(0.11f, 0.15f, 0.2f, 0.98f);

        RebuildTabButtons(font);
        EnsureEmptyContent(font);
        uiBuilt = true;
    }

    private void RebuildSectionOrder()
    {
        sectionOrder.Clear();
        sectionOrder.Add(buildingSectionUI);
        sectionOrder.Add(playerSectionUI);
        sectionOrder.Add(turretSectionUI);
        sectionOrder.Add(oilSectionUI);
    }

    private void ResolveSectionReferences()
    {
        if (sectionReferencesResolved &&
            IsSceneSection(buildingSectionUI) &&
            IsSceneSection(playerSectionUI) &&
            IsSceneSection(turretSectionUI) &&
            IsSceneSection(oilSectionUI))
            return;

        buildingSectionUI = ResolveSceneSection(buildingSectionUI);
        playerSectionUI = ResolveSceneSection(playerSectionUI);
        turretSectionUI = ResolveSceneSection(turretSectionUI);
        oilSectionUI = ResolveSceneSection(oilSectionUI);
        sectionReferencesResolved = true;
    }

    private static T ResolveSceneSection<T>(T configuredSection) where T : ShopSectionUI
    {
        if (configuredSection == null)
            return null;

        GameObject configuredObject = configuredSection.gameObject;
        if (configuredObject != null && configuredObject.scene.IsValid() && configuredObject.scene.isLoaded)
            return configuredSection;

        T[] sceneSections = FindObjectsByType<T>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneSections.Length; i++)
        {
            T candidate = sceneSections[i];
            if (candidate == null || candidate == configuredSection)
                continue;

            GameObject candidateObject = candidate.gameObject;
            if (candidateObject == null || !candidateObject.scene.IsValid() || !candidateObject.scene.isLoaded)
                continue;

            if (candidateObject.name == configuredObject.name)
                return candidate;
        }

        return configuredSection;
    }

    private static bool IsSceneSection(ShopSectionUI section)
    {
        if (section == null)
            return false;

        GameObject sectionObject = section.gameObject;
        return sectionObject != null && sectionObject.scene.IsValid() && sectionObject.scene.isLoaded;
    }

    private void RebuildTabButtons(Font font)
    {
        for (int i = 0; i < tabButtons.Count; i++)
        {
            if (tabButtons[i] != null)
                Destroy(tabButtons[i].gameObject);
        }

        tabButtons.Clear();
        CreateTabButton(font, "Building", buildingSectionUI);
        CreateTabButton(font, "Player", playerSectionUI);
        CreateTabButton(font, "Turret", turretSectionUI);
        CreateTabButton(font, "Oil", oilSectionUI);
    }

    private void CreateTabButton(Font font, string label, ShopSectionUI section)
    {
        Button button = CreateButton($"Tab_{label}", leftMenuRoot, Vector2.zero, Vector2.zero, label, font);
        LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 92f;
        button.onClick.AddListener(() => ShowSection(section));
        tabButtons.Add(button);
    }

    private void ShowSection(ShopSectionUI section)
    {
        ShopSectionUI nextSection = section != null ? section : GetDefaultSection();
        if (contentRoot == null)
            return;

        EnsureEmptyContent(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        if (activeSection != null && activeSection != nextSection)
            activeSection.HideSection();

        if (emptyContentRoot != null)
            emptyContentRoot.SetActive(nextSection == null);

        if (nextSection == null)
        {
            activeSection = null;
            if (titleText != null)
                titleText.text = "Shop / Building";
            RefreshTabColors();
            return;
        }

        activeSection = nextSection;
        activeSection.ShowSection(contentRoot);

        if (titleText != null)
            titleText.text = $"Shop / {activeSection.SectionTitle}";

        RefreshTabColors();
    }

    private void RefreshActiveSection()
    {
        if (panelRoot != null && panelRoot.gameObject.activeSelf && activeSection != null)
            activeSection.ShowSection(contentRoot);
    }

    private ShopSectionUI GetDefaultSection()
    {
        if (buildingSectionUI != null)
            return buildingSectionUI;
        if (playerSectionUI != null)
            return playerSectionUI;
        if (turretSectionUI != null)
            return turretSectionUI;
        return oilSectionUI;
    }

    private void SetPanelVisible(bool visible)
    {
        if (panelRoot != null)
            panelRoot.gameObject.SetActive(visible);
    }

    private void RefreshTabColors()
    {
        for (int i = 0; i < tabButtons.Count; i++)
        {
            Image image = tabButtons[i] != null ? tabButtons[i].GetComponent<Image>() : null;
            if (image == null)
                continue;

            ShopSectionUI section = i < sectionOrder.Count ? sectionOrder[i] : null;
            image.color = section != null && section == activeSection
                ? new Color(0.96f, 0.63f, 0.22f, 1f)
                : new Color(0.2f, 0.26f, 0.34f, 1f);
        }
    }

    private void EnsureEmptyContent(Font font)
    {
        if (contentRoot == null || emptyContentRoot != null)
            return;

        emptyContentRoot = CreateUiObject("EmptyContent", contentRoot);
        RectTransform emptyRect = emptyContentRoot.GetComponent<RectTransform>();
        emptyRect.anchorMin = Vector2.zero;
        emptyRect.anchorMax = Vector2.one;
        emptyRect.offsetMin = new Vector2(24f, 24f);
        emptyRect.offsetMax = new Vector2(-24f, -24f);

        Text emptyText = CreateText("EmptyLabel", emptyContentRoot.transform, font, 34, TextAnchor.MiddleCenter);
        emptyText.text = "Building UI area";
        RectTransform textRect = emptyText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    private static Button CreateButton(string name, Transform parent, Vector2 size, Vector2 anchoredPosition, string label, Font font)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.2f, 0.26f, 0.34f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.28f, 0.36f, 0.46f, 1f);
        colors.pressedColor = new Color(0.16f, 0.2f, 0.26f, 1f);
        colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.7f);
        button.colors = colors;

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        Text buttonText = CreateText("Label", buttonObject.transform, font, 28, TextAnchor.MiddleCenter);
        buttonText.text = label;
        RectTransform textRect = buttonText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private static Text CreateText(string name, Transform parent, Font font, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = CreateUiObject(name, parent);
        Text text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return gameObject;
    }
}
