using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BuildingListUI : ShopSectionUI
{
    private enum BuildingPurchaseState
    {
        Level1Available,
        Level1Constructing,
        Level1Placed,
        Level2Constructing,
        Complete
    }

    [Serializable]
    private sealed class BuildingCatalogEntry
    {
        public string displayName;
        public Building prefab;
        public int level1Price;
        public int level2Price;
    }

    [Header("Building Shop")]
    [SerializeField] private List<Building> registeredBuildingPrefabs = new List<Building>();

    private readonly List<BuildingCatalogEntry> entries = new List<BuildingCatalogEntry>();
    private readonly List<Button> buildingButtons = new List<Button>();

    private GameObject rootObject;
    private RectTransform listRoot;
    private Text statusText;
    private GameObject confirmationRoot;
    private Text confirmationText;
    private Button confirmYesButton;
    private Button confirmNoButton;
    private int pendingPurchaseIndex = -1;

    public override string SectionTitle => "Building";

    public override void ShowSection(RectTransform contentRoot)
    {
        if (contentRoot == null)
            return;

        RefreshCatalog();

        if (rootObject == null)
            rootObject = BuildRoot(contentRoot);
        else
            rootObject.transform.SetParent(contentRoot, false);

        rootObject.SetActive(true);
        RefreshButtons();
    }

    public override void HideSection()
    {
        if (rootObject != null)
            rootObject.SetActive(false);
    }

    private void RefreshCatalog()
    {
        entries.Clear();
        Dictionary<string, BuildingCatalogEntry> uniqueEntries = new Dictionary<string, BuildingCatalogEntry>(StringComparer.Ordinal);

        for (int i = 0; i < registeredBuildingPrefabs.Count; i++)
        {
            Building prefab = registeredBuildingPrefabs[i];
            if (prefab == null)
                continue;

            string id = prefab.BuildingId;
            if (string.IsNullOrWhiteSpace(id) || uniqueEntries.ContainsKey(id))
                continue;

            uniqueEntries.Add(id, new BuildingCatalogEntry
            {
                displayName = prefab.DisplayName,
                prefab = prefab,
                level1Price = prefab.GetPurchasePriceForLevel(1),
                level2Price = prefab.GetPurchasePriceForLevel(2)
            });
        }

        foreach (BuildingCatalogEntry entry in uniqueEntries.Values)
            entries.Add(entry);

        entries.Sort((left, right) => string.Compare(left.displayName, right.displayName, StringComparison.Ordinal));
    }

    private GameObject BuildRoot(RectTransform parent)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject root = new GameObject("BuildingSectionRoot", typeof(RectTransform));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = new Vector2(0f, 0f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        GameObject headerObject = new GameObject("Header", typeof(RectTransform));
        RectTransform headerRect = headerObject.GetComponent<RectTransform>();
        headerRect.SetParent(root.transform, false);
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.offsetMin = new Vector2(20f, -90f);
        headerRect.offsetMax = new Vector2(-20f, -20f);

        Text headerText = headerObject.AddComponent<Text>();
        headerText.font = font;
        headerText.fontSize = 34;
        headerText.alignment = TextAnchor.MiddleLeft;
        headerText.color = Color.white;
        headerText.text = "Building";

        GameObject statusObject = new GameObject("Status", typeof(RectTransform));
        RectTransform statusRect = statusObject.GetComponent<RectTransform>();
        statusRect.SetParent(root.transform, false);
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(1f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.offsetMin = new Vector2(20f, 20f);
        statusRect.offsetMax = new Vector2(-20f, 90f);

        statusText = statusObject.AddComponent<Text>();
        statusText.font = font;
        statusText.fontSize = 24;
        statusText.alignment = TextAnchor.MiddleLeft;
        statusText.color = Color.white;

        GameObject listObject = new GameObject("List", typeof(RectTransform));
        listRoot = listObject.GetComponent<RectTransform>();
        listRoot.SetParent(root.transform, false);
        listRoot.anchorMin = new Vector2(0f, 0f);
        listRoot.anchorMax = new Vector2(1f, 1f);
        listRoot.offsetMin = new Vector2(20f, 110f);
        listRoot.offsetMax = new Vector2(-20f, -110f);

        VerticalLayoutGroup layout = listObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        BuildConfirmation(root.transform, font);
        RebuildButtons();
        return root;
    }

    private void RebuildButtons()
    {
        if (listRoot == null)
            return;

        for (int i = 0; i < buildingButtons.Count; i++)
        {
            if (buildingButtons[i] != null)
                Destroy(buildingButtons[i].gameObject);
        }

        buildingButtons.Clear();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            Button button = CreateButton(listRoot, font);
            button.onClick.AddListener(() => HandlePurchase(index));
            buildingButtons.Add(button);
        }
    }

    private void RefreshButtons()
    {
        if (rootObject == null)
            return;

        if (buildingButtons.Count != entries.Count)
            RebuildButtons();

        VillageManagement villageManagement = VillageManagement.Instance;
        for (int i = 0; i < buildingButtons.Count && i < entries.Count; i++)
        {
            BuildingCatalogEntry entry = entries[i];
            BuildingPurchaseState purchaseState = GetPurchaseState(entry.prefab.BuildingId);
            int nextLevel = purchaseState == BuildingPurchaseState.Level1Placed ? 2 : 1;
            int price = nextLevel == 2 ? entry.level2Price : entry.level1Price;
            bool canPurchase = purchaseState == BuildingPurchaseState.Level1Available || purchaseState == BuildingPurchaseState.Level1Placed;
            bool canPlace = purchaseState != BuildingPurchaseState.Level1Available || Path.FindFirstEmpty() != null;
            bool canAfford = villageManagement != null && villageManagement.CurrentOxygen >= price;

            buildingButtons[i].interactable = canPurchase && canPlace && canAfford;
            Text label = buildingButtons[i].GetComponentInChildren<Text>();
            if (label != null)
                label.text = BuildButtonLabel(entry, purchaseState, price, canPlace, canAfford);
        }

        bool confirmationActive = confirmationRoot != null && confirmationRoot.activeSelf;
        for (int i = 0; i < buildingButtons.Count; i++)
        {
            if (buildingButtons[i] != null)
                buildingButtons[i].interactable &= !confirmationActive;
        }

        if (entries.Count == 0)
            SetStatus("등록된 건물 프리팹이 없습니다.");
        else
            SetStatus("건물은 1렙 구매 후 2렙 구매로 전환됩니다.");
    }

    private void HandlePurchase(int index)
    {
        if (index < 0 || index >= entries.Count)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        BuildingCatalogEntry entry = entries[index];
        BuildingPurchaseState purchaseState = GetPurchaseState(entry.prefab.BuildingId);
        if (purchaseState == BuildingPurchaseState.Level1Constructing || purchaseState == BuildingPurchaseState.Level2Constructing)
        {
            SetStatus($"{entry.displayName}은 현재 건설 중입니다.");
            return;
        }

        if (purchaseState == BuildingPurchaseState.Complete)
        {
            SetStatus($"{entry.displayName}은 이미 2레벨 구매 완료입니다.");
            return;
        }

        int nextLevel = purchaseState == BuildingPurchaseState.Level1Placed ? 2 : 1;
        int price = nextLevel <= 1 ? entry.level1Price : entry.level2Price;
        ShowConfirmation(index, entry.displayName, nextLevel, price);
    }

    private void ConfirmPurchase()
    {
        int index = pendingPurchaseIndex;

        if (index < 0 || index >= entries.Count)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
        {
            SetStatus("VillageManagement를 찾지 못했습니다.");
            return;
        }

        BuildingCatalogEntry entry = entries[index];
        BuildingPurchaseState purchaseState = GetPurchaseState(entry.prefab.BuildingId);
        if (purchaseState == BuildingPurchaseState.Level1Constructing || purchaseState == BuildingPurchaseState.Level2Constructing)
        {
            SetStatus($"{entry.displayName}은 현재 건설 중입니다.");
            return;
        }

        if (purchaseState == BuildingPurchaseState.Complete)
        {
            SetStatus($"{entry.displayName}은 이미 2레벨 구매 완료입니다.");
            return;
        }

        int nextLevel = purchaseState == BuildingPurchaseState.Level1Placed ? 2 : 1;
        int price = nextLevel <= 1 ? entry.level1Price : entry.level2Price;
        if (price > 0 && villageManagement.CurrentOxygen < price)
        {
            SetStatus($"산소가 부족합니다. 필요 O2 {price}");
            return;
        }

        if (nextLevel == 1)
        {
            Path targetPath = Path.FindRandomEmpty();
            if (targetPath == null)
            {
                SetStatus("빈 Path 슬롯이 없습니다.");
                return;
            }

            HideConfirmation();
            targetPath.TryBuildSelected(entry.prefab);
            SetStatus($"{entry.displayName} 1레벨 구매 후 설치를 시작했습니다.");
        }
        else
        {
            Path targetPath = FindPlacedPathForBuilding(entry.prefab.BuildingId);
            if (targetPath == null || targetPath.Building == null)
            {
                SetStatus($"{entry.displayName} 1레벨 건물을 찾지 못했습니다.");
                return;
            }

            HideConfirmation();
            targetPath.TryUpgradeCurrentBuilding();
            Shop.CloseAllShops();
            SetStatus($"{entry.displayName} 2레벨 구매를 시작했습니다.");
        }

        RefreshButtons();
    }

    private void CancelPurchase()
    {
        HideConfirmation();
        RefreshButtons();
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message ?? string.Empty;
    }

    private static Button CreateButton(Transform parent, Font font)
    {
        GameObject buttonObject = new GameObject("BuildingButton", typeof(RectTransform));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(0f, 84f);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.18f, 0.24f, 0.32f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.24f, 0.32f, 0.42f, 1f);
        colors.pressedColor = new Color(0.14f, 0.18f, 0.24f, 1f);
        colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.7f);
        button.colors = colors;

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 84f;

        GameObject textObject = new GameObject("Label", typeof(RectTransform));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(buttonObject.transform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20f, 10f);
        textRect.offsetMax = new Vector2(-20f, -10f);

        Text text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 28;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;

        return button;
    }

    private void BuildConfirmation(Transform parent, Font font)
    {
        confirmationRoot = new GameObject("Confirmation", typeof(RectTransform));
        RectTransform confirmRect = confirmationRoot.GetComponent<RectTransform>();
        confirmRect.SetParent(parent, false);
        confirmRect.anchorMin = new Vector2(0.18f, 0.2f);
        confirmRect.anchorMax = new Vector2(0.82f, 0.8f);
        confirmRect.offsetMin = Vector2.zero;
        confirmRect.offsetMax = Vector2.zero;

        Image background = confirmationRoot.AddComponent<Image>();
        background.color = new Color(0.07f, 0.09f, 0.12f, 0.98f);

        confirmationText = CreateText("ConfirmationText", confirmationRoot.transform, font, 32, TextAnchor.MiddleCenter);
        RectTransform textRect = confirmationText.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.35f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(24f, 24f);
        textRect.offsetMax = new Vector2(-24f, -24f);

        confirmYesButton = CreateDialogButton(confirmationRoot.transform, font, "YesButton", "예", new Vector2(0.25f, 0.18f));
        confirmYesButton.onClick.AddListener(ConfirmPurchase);

        confirmNoButton = CreateDialogButton(confirmationRoot.transform, font, "NoButton", "아니요", new Vector2(0.75f, 0.18f));
        confirmNoButton.onClick.AddListener(CancelPurchase);

        confirmationRoot.SetActive(false);
    }

    private void ShowConfirmation(int index, string displayName, int level, int price)
    {
        pendingPurchaseIndex = index;
        if (confirmationRoot != null)
            confirmationRoot.SetActive(true);
        if (confirmationText != null)
            confirmationText.text = $"{displayName} {level}레벨을 정말 구매하시겠습니까?\nO2 {price}";

        RefreshButtons();
    }

    private void HideConfirmation()
    {
        pendingPurchaseIndex = -1;
        if (confirmationRoot != null)
            confirmationRoot.SetActive(false);
    }

    private static BuildingPurchaseState GetPurchaseState(string buildingId)
    {
        if (string.IsNullOrWhiteSpace(buildingId))
            return BuildingPurchaseState.Level1Available;

        bool hasLevel1Construction = false;
        bool hasLevel1Placed = false;
        bool hasLevel2Construction = false;
        bool hasLevel2Placed = false;

        Path[] allPaths = FindObjectsByType<Path>(FindObjectsSortMode.None);
        for (int i = 0; i < allPaths.Length; i++)
        {
            Path path = allPaths[i];
            if (path == null)
                continue;

            if (path.Building != null && path.Building.BuildingId == buildingId)
            {
                if (path.Building.Level >= 2)
                    hasLevel2Placed = true;
                else
                    hasLevel1Placed = true;
            }

            if (!path.HasActiveConstruction || path.ActiveConstructionBuildingId != buildingId)
                continue;

            if (path.ActiveConstructionTargetLevel >= 2)
                hasLevel2Construction = true;
            else
                hasLevel1Construction = true;
        }

        if (hasLevel2Placed)
            return BuildingPurchaseState.Complete;
        if (hasLevel2Construction)
            return BuildingPurchaseState.Level2Constructing;
        if (hasLevel1Placed)
            return BuildingPurchaseState.Level1Placed;
        if (hasLevel1Construction)
            return BuildingPurchaseState.Level1Constructing;
        return BuildingPurchaseState.Level1Available;
    }

    private static Path FindPlacedPathForBuilding(string buildingId)
    {
        Path[] allPaths = FindObjectsByType<Path>(FindObjectsSortMode.None);
        for (int i = 0; i < allPaths.Length; i++)
        {
            Path path = allPaths[i];
            if (path == null || path.Building == null)
                continue;

            if (path.Building.BuildingId == buildingId)
                return path;
        }

        return null;
    }

    private static string BuildButtonLabel(BuildingCatalogEntry entry, BuildingPurchaseState purchaseState, int price, bool canPlace, bool canAfford)
    {
        if (purchaseState == BuildingPurchaseState.Level1Available)
        {
            string priceText = $"{entry.displayName}  1렙 O2 {entry.level1Price}";
            if (!canPlace)
                return $"{priceText}\n빈 슬롯 없음";
            if (!canAfford)
                return $"{priceText}\n1렙 구매 O2 {price} 부족";
            return $"{priceText}\n1렙 구매 O2 {price}";
        }

        if (purchaseState == BuildingPurchaseState.Level1Constructing)
            return $"{entry.displayName}\n1렙 건설 중";

        if (purchaseState == BuildingPurchaseState.Level1Placed)
        {
            string priceText = $"{entry.displayName}  2렙 O2 {entry.level2Price}";
            if (!canAfford)
                return $"{priceText}\n2렙 구매 O2 {price} 부족";
            return $"{priceText}\n2렙 구매 O2 {price}";
        }

        if (purchaseState == BuildingPurchaseState.Level2Constructing)
            return $"{entry.displayName}\n2렙 건설 중";

        return $"{entry.displayName}\n구매 완료";
    }

    private static Text CreateText(string name, Transform parent, Font font, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        return text;
    }

    private static Button CreateDialogButton(Transform parent, Font font, string name, string label, Vector2 anchor)
    {
        Button button = CreateButton(parent, font);
        button.name = name;
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(200f, 84f);
        rect.anchoredPosition = Vector2.zero;

        Text text = button.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
        }

        return button;
    }
}
