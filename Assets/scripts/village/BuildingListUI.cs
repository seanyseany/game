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
        Complete,
        SpecialPlaced,
        LiftAvailable
    }

    private enum CatalogEntryType
    {
        Building,
        SpecialBuilding,
        LiftToken
    }

    [Serializable]
    private sealed class BuildingCatalogEntry
    {
        public CatalogEntryType entryType;
        public string displayName;
        public Building prefab;
        public SpecialBuilding specialPrefab;
        public int liftTokenIndex = -1;
        public int level1Price;
        public int level2Price;
    }

    [Header("Building Shop")]
    [SerializeField] private List<Building> registeredBuildingPrefabs = new List<Building>();
    [SerializeField] private List<SpecialBuilding> registeredSpecialBuildingPrefabs = new List<SpecialBuilding>();
    [SerializeField] private LiftSpot registeredLiftSpot;

    private readonly List<BuildingCatalogEntry> entries = new List<BuildingCatalogEntry>();
    private readonly List<Button> buildingButtons = new List<Button>();

    private GameObject rootObject;
    private ScrollRect listScrollRect;
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
        EnsureLiftSpotReference();
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
                entryType = CatalogEntryType.Building,
                displayName = prefab.DisplayName,
                prefab = prefab,
                level1Price = prefab.GetPurchasePriceForLevel(1),
                level2Price = prefab.GetPurchasePriceForLevel(2)
            });
        }

        for (int i = 0; i < registeredSpecialBuildingPrefabs.Count; i++)
        {
            SpecialBuilding prefab = registeredSpecialBuildingPrefabs[i];
            if (prefab == null)
                continue;

            string id = prefab.SpecialBuildingId;
            if (string.IsNullOrWhiteSpace(id) || uniqueEntries.ContainsKey(id))
                continue;

            uniqueEntries.Add(id, new BuildingCatalogEntry
            {
                entryType = CatalogEntryType.SpecialBuilding,
                displayName = prefab.name,
                specialPrefab = prefab,
                level1Price = Mathf.Max(0, prefab.SpecialBuildingValue),
                level2Price = 0
            });
        }

        foreach (BuildingCatalogEntry entry in uniqueEntries.Values)
            entries.Add(entry);

        if (registeredLiftSpot != null)
        {
            for (int i = 0; i < registeredLiftSpot.TotalLiftCount; i++)
            {
                entries.Add(new BuildingCatalogEntry
                {
                    entryType = CatalogEntryType.LiftToken,
                    displayName = $"Lift {i + 1}",
                    liftTokenIndex = i,
                    level1Price = registeredLiftSpot.GetLiftPrice(i)
                });
            }
        }

        entries.Sort((left, right) => string.Compare(left.displayName, right.displayName, StringComparison.Ordinal));
    }

    public Building ResolveBuildingPrefab(string buildingId)
    {
        if (string.IsNullOrWhiteSpace(buildingId))
            return null;

        RefreshCatalog();
        for (int i = 0; i < entries.Count; i++)
        {
            BuildingCatalogEntry entry = entries[i];
            if (entry?.prefab != null &&
                string.Equals(entry.prefab.BuildingId, buildingId, StringComparison.Ordinal))
                return entry.prefab;
        }

        return null;
    }

    public SpecialBuilding ResolveSpecialBuildingPrefab(string specialBuildingId)
    {
        if (string.IsNullOrWhiteSpace(specialBuildingId))
            return null;

        RefreshCatalog();
        for (int i = 0; i < entries.Count; i++)
        {
            BuildingCatalogEntry entry = entries[i];
            if (entry?.specialPrefab != null &&
                string.Equals(entry.specialPrefab.SpecialBuildingId, specialBuildingId, StringComparison.Ordinal))
            {
                return entry.specialPrefab;
            }
        }

        return null;
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
        statusText.fontSize = 20;
        statusText.alignment = TextAnchor.MiddleLeft;
        statusText.color = Color.white;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.verticalOverflow = VerticalWrapMode.Overflow;
        statusText.raycastTarget = false;

        GameObject scrollObject = new GameObject("ListScrollView", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
        RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
        scrollRectTransform.SetParent(root.transform, false);
        scrollRectTransform.anchorMin = new Vector2(0f, 0f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(20f, 110f);
        scrollRectTransform.offsetMax = new Vector2(-20f, -110f);

        Image scrollImage = scrollObject.GetComponent<Image>();
        scrollImage.color = new Color(0f, 0f, 0f, 0.08f);
        scrollImage.raycastTarget = true;

        Mask scrollMask = scrollObject.GetComponent<Mask>();
        scrollMask.showMaskGraphic = false;

        listScrollRect = scrollObject.GetComponent<ScrollRect>();
        listScrollRect.horizontal = false;
        listScrollRect.vertical = true;
        listScrollRect.scrollSensitivity = 30f;

        GameObject contentObject = new GameObject("List", typeof(RectTransform));
        listRoot = contentObject.GetComponent<RectTransform>();
        listRoot.SetParent(scrollObject.transform, false);
        listRoot.anchorMin = new Vector2(0f, 1f);
        listRoot.anchorMax = new Vector2(1f, 1f);
        listRoot.pivot = new Vector2(0.5f, 1f);
        listRoot.offsetMin = Vector2.zero;
        listRoot.offsetMax = Vector2.zero;

        listScrollRect.viewport = scrollRectTransform;
        listScrollRect.content = listRoot;

        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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

        EnsureLiftSpotReference();

        if (buildingButtons.Count != entries.Count)
            RebuildButtons();

        VillageManagement villageManagement = VillageManagement.Instance;
        for (int i = 0; i < buildingButtons.Count && i < entries.Count; i++)
        {
            BuildingCatalogEntry entry = entries[i];
            BuildingPurchaseState purchaseState = GetPurchaseState(entry);
            int nextLevel = purchaseState == BuildingPurchaseState.Level1Placed ? 2 : 1;
            int price = entry.entryType == CatalogEntryType.SpecialBuilding
                ? entry.level1Price
                : (nextLevel == 2 ? entry.level2Price : entry.level1Price);
            bool canPurchase = entry.entryType == CatalogEntryType.SpecialBuilding
                ? purchaseState == BuildingPurchaseState.Level1Available
                : entry.entryType == CatalogEntryType.LiftToken
                    ? purchaseState == BuildingPurchaseState.LiftAvailable
                    : purchaseState == BuildingPurchaseState.Level1Available || purchaseState == BuildingPurchaseState.Level1Placed;
            bool canPlace = entry.entryType == CatalogEntryType.LiftToken
                ? registeredLiftSpot != null && registeredLiftSpot.GetActiveLiftCount() < registeredLiftSpot.TotalLiftCount
                : purchaseState != BuildingPurchaseState.Level1Available || Path.FindFirstEmpty() != null;
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
            SetStatus("등록된 빌딩/특별빌딩 프리팹이 없습니다.");
        else
            SetStatus("일반 건물은 1렙 후 2렙 업그레이드, 특별빌딩은 1회 구매 설치, Lift는 비활성 슬롯을 랜덤 활성화합니다.");
    }

    private void HandlePurchase(int index)
    {
        EnsureLiftSpotReference();

        if (index < 0 || index >= entries.Count)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        BuildingCatalogEntry entry = entries[index];
        BuildingPurchaseState purchaseState = GetPurchaseState(entry);
        if (purchaseState == BuildingPurchaseState.Level1Constructing || purchaseState == BuildingPurchaseState.Level2Constructing)
        {
            SetStatus($"{entry.displayName}은 현재 건설 중입니다.");
            return;
        }

        if (entry.entryType == CatalogEntryType.LiftToken)
        {
            int liftPrice = entry.level1Price;
            ShowConfirmation(index, entry.displayName, 1, liftPrice);
            return;
        }

        if (purchaseState == BuildingPurchaseState.Complete || purchaseState == BuildingPurchaseState.SpecialPlaced)
        {
            SetStatus(entry.entryType == CatalogEntryType.SpecialBuilding
                ? $"{entry.displayName}은 이미 설치되어 있습니다."
                : $"{entry.displayName}은 이미 2레벨 구매 완료입니다.");
            return;
        }

        int nextLevel = purchaseState == BuildingPurchaseState.Level1Placed ? 2 : 1;
        int price = entry.entryType == CatalogEntryType.SpecialBuilding
            ? entry.level1Price
            : (nextLevel <= 1 ? entry.level1Price : entry.level2Price);
        ShowConfirmation(index, entry.displayName, nextLevel, price);
    }

    private void ConfirmPurchase()
    {
        EnsureLiftSpotReference();

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
        BuildingPurchaseState purchaseState = GetPurchaseState(entry);
        if (purchaseState == BuildingPurchaseState.Level1Constructing || purchaseState == BuildingPurchaseState.Level2Constructing)
        {
            SetStatus($"{entry.displayName}은 현재 건설 중입니다.");
            return;
        }

        if (entry.entryType == CatalogEntryType.LiftToken)
        {
            int liftPrice = entry.level1Price;
            if (liftPrice > 0 && villageManagement.CurrentOxygen < liftPrice)
            {
                SetStatus($"산소가 부족합니다. 필요 O2 {liftPrice}");
                return;
            }

            if (registeredLiftSpot == null || !registeredLiftSpot.TryActivateRandomInactiveLift())
            {
                SetStatus("활성화 가능한 Lift 슬롯이 없습니다.");
                return;
            }

            if (liftPrice > 0)
                villageManagement.TrySpendOxygen(liftPrice);

            HideConfirmation();
            Shop.CloseAllShops();
            SetStatus($"{entry.displayName} 구매 완료");
            RefreshButtons();
            return;
        }

        if (purchaseState == BuildingPurchaseState.Complete || purchaseState == BuildingPurchaseState.SpecialPlaced)
        {
            SetStatus(entry.entryType == CatalogEntryType.SpecialBuilding
                ? $"{entry.displayName}은 이미 설치되어 있습니다."
                : $"{entry.displayName}은 이미 2레벨 구매 완료입니다.");
            return;
        }

        int nextLevel = purchaseState == BuildingPurchaseState.Level1Placed ? 2 : 1;
        int price = entry.entryType == CatalogEntryType.SpecialBuilding
            ? entry.level1Price
            : (nextLevel <= 1 ? entry.level1Price : entry.level2Price);
        if (price > 0 && villageManagement.CurrentOxygen < price)
        {
            SetStatus($"산소가 부족합니다. 필요 O2 {price}");
            return;
        }

        if (entry.entryType == CatalogEntryType.SpecialBuilding)
        {
            Path targetPath = Path.FindRandomEmpty();
            if (targetPath == null)
            {
                SetStatus("빈 Path 슬롯이 없습니다.");
                return;
            }

            HideConfirmation();
            targetPath.TryBuildSelected(entry.specialPrefab);
            SetStatus($"{entry.displayName} 특별빌딩 설치를 시작했습니다.");
        }
        else if (nextLevel == 1)
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

    private BuildingPurchaseState GetPurchaseState(BuildingCatalogEntry entry)
    {
        if (entry == null)
            return BuildingPurchaseState.Level1Available;

        if (entry.entryType == CatalogEntryType.LiftToken)
            return GetLiftPurchaseState(entry);

        if (entry.entryType == CatalogEntryType.SpecialBuilding)
            return GetSpecialPurchaseState(entry.specialPrefab);

        return GetPurchaseState(entry.prefab != null ? entry.prefab.BuildingId : string.Empty);
    }

    private static BuildingPurchaseState GetSpecialPurchaseState(SpecialBuilding prefab)
    {
        if (prefab == null)
            return BuildingPurchaseState.Level1Available;

        Path[] paths = FindObjectsByType<Path>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < paths.Length; i++)
        {
            Path path = paths[i];
            if (path == null || path.SpecialBuilding == null)
                continue;

            if (string.Equals(path.SpecialBuilding.SpecialBuildingId, prefab.SpecialBuildingId, StringComparison.Ordinal))
                return BuildingPurchaseState.SpecialPlaced;
        }

        return BuildingPurchaseState.Level1Available;
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
        text.raycastTarget = false;

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
        {
            BuildingCatalogEntry entry = index >= 0 && index < entries.Count ? entries[index] : null;
            confirmationText.text = entry != null && entry.entryType == CatalogEntryType.SpecialBuilding
                ? $"{displayName} 특별빌딩을 정말 구매하시겠습니까?\nO2 {price}"
                : entry != null && entry.entryType == CatalogEntryType.LiftToken
                    ? $"{displayName} 토큰을 정말 구매하시겠습니까?\nO2 {price}"
                : $"{displayName} {level}레벨을 정말 구매하시겠습니까?\nO2 {price}";
        }

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

        VillageManagement villageManagement = VillageManagement.Instance;
        if (villageManagement != null)
        {
            IReadOnlyList<VillageManagement.BuildingState> savedBuildings = villageManagement.Buildings;
            for (int i = 0; i < savedBuildings.Count; i++)
            {
                VillageManagement.BuildingState state = savedBuildings[i];
                if (state == null || !string.Equals(state.buildingId, buildingId, StringComparison.Ordinal))
                    continue;

                if (state.underConstruction)
                {
                    if (state.level >= 2)
                        hasLevel2Construction = true;
                    else
                        hasLevel1Construction = true;
                    continue;
                }

                if (!state.isPlaced)
                    continue;

                if (state.level >= 2)
                    hasLevel2Placed = true;
                else
                    hasLevel1Placed = true;
            }
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

    private BuildingPurchaseState GetLiftPurchaseState(BuildingCatalogEntry entry)
    {
        EnsureLiftSpotReference();

        if (entry == null || registeredLiftSpot == null)
            return BuildingPurchaseState.Complete;

        int activeCount = registeredLiftSpot.GetActiveLiftCount();
        if (activeCount >= registeredLiftSpot.TotalLiftCount)
            return BuildingPurchaseState.Complete;

        return BuildingPurchaseState.LiftAvailable;
    }

    private void EnsureLiftSpotReference()
    {
        if (registeredLiftSpot != null)
            return;

        registeredLiftSpot = FindFirstObjectByType<LiftSpot>(FindObjectsInactive.Include);
    }

    private static string BuildButtonLabel(BuildingCatalogEntry entry, BuildingPurchaseState purchaseState, int price, bool canPlace, bool canAfford)
    {
        if (entry != null && entry.entryType == CatalogEntryType.SpecialBuilding)
        {
            string priceText = $"{entry.displayName}  특별빌딩 O2 {entry.level1Price}";
            if (purchaseState == BuildingPurchaseState.SpecialPlaced)
                return $"{entry.displayName}\n설치 완료";
            if (!canPlace)
                return $"{priceText}\n빈 슬롯 없음";
            if (!canAfford)
                return $"{priceText}\n구매 O2 {price} 부족";
            return $"{priceText}\n구매 O2 {price}";
        }

        if (entry != null && entry.entryType == CatalogEntryType.LiftToken)
        {
            string priceText = $"{entry.displayName}  O2 {entry.level1Price}";
            if (purchaseState == BuildingPurchaseState.Complete)
                return $"{entry.displayName}\n구매 완료";
            if (!canAfford)
                return $"{priceText}\n구매 O2 {price} 부족";
            return $"{priceText}\n구매 O2 {price}";
        }

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
