using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ShopPlaceholderUI : ShopSectionUI
{
    private struct OilSlotTarget
    {
        public WayOil wayOil;
        public int pathIndex;
    }

    [Serializable]
    private sealed class OilCatalogEntry
    {
        public string id;
        public string displayName;
        public Oxygen level1Prefab;
        public Oxygen level2Prefab;
    }

    [Serializable]
    private sealed class TurretCatalogEntry
    {
        public string id;
        public string displayName;
        public BaseTurret level1Prefab;
        public BaseTurret level2Prefab;
        public BaseTurret level3Prefab;
    }

    [SerializeField] private string sectionTitle = "Section";
    [SerializeField] [TextArea] private string message = "준비 중입니다.";

    [Header("Oil Shop")]
    [SerializeField] private List<Oxygen> registeredOilPrefabs = new List<Oxygen>();
    [SerializeField] private int oilShopSlotCount = 4;

    [Header("Turret Shop")]
    [SerializeField] private List<BaseTurret> registeredTurretPrefabs = new List<BaseTurret>();
    [SerializeField] private List<GameObject> registeredTurretPathPrefabs = new List<GameObject>();

    private readonly List<Button> buttons = new List<Button>();
    private readonly List<OilCatalogEntry> oilEntries = new List<OilCatalogEntry>();
    private readonly List<TurretCatalogEntry> turretEntries = new List<TurretCatalogEntry>();
    private readonly Dictionary<int, TurretImplementation> activeTurretSlots = new Dictionary<int, TurretImplementation>();
    private readonly List<OilSlotTarget> cachedOilSlotTargets = new List<OilSlotTarget>();

    private GameObject rootObject;
    private RectTransform listRoot;
    private Text statusText;
    private bool refreshQueued;
    private int lastKnownOxygen = int.MinValue;

    public override string SectionTitle => sectionTitle;

    private void OnEnable()
    {
        VillageManagement.InstanceReady += HandleVillageManagementReady;
        SubscribeVillageManagement(VillageManagement.Instance);
        QueueRefresh();
    }

    private void OnDisable()
    {
        VillageManagement.InstanceReady -= HandleVillageManagementReady;
        UnsubscribeVillageManagement(VillageManagement.Instance);
    }

    public override void ShowSection(RectTransform contentRoot)
    {
        if (contentRoot == null)
            return;

        RefreshCatalogs();

        if (rootObject == null)
            rootObject = BuildRoot(contentRoot);
        else
            rootObject.transform.SetParent(contentRoot, false);

        rootObject.SetActive(true);
        QueueRefresh(true);
    }

    public override void HideSection()
    {
        if (rootObject != null)
            rootObject.SetActive(false);
    }

    private GameObject BuildRoot(RectTransform parent)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject root = new GameObject($"{sectionTitle}SectionRoot", typeof(RectTransform));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image background = root.AddComponent<Image>();
        background.color = new Color(0.12f, 0.16f, 0.22f, 0.85f);

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
        headerText.text = sectionTitle;

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

        if (IsInteractiveSection())
        {
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

            RebuildButtons();
        }
        else
        {
            GameObject textObject = new GameObject("Message", typeof(RectTransform));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(root.transform, false);
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(40f, 40f);
            textRect.offsetMax = new Vector2(-40f, -120f);

            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 32;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = message;
        }

        return root;
    }

    private void RefreshCatalogs()
    {
        RefreshOilSlotTargetCache();

        if (IsOilSection())
            RefreshOilCatalog();
        else if (IsTurretSection())
            RefreshTurretCatalog();
    }

    private void RefreshOilCatalog()
    {
        oilEntries.Clear();
        Oxygen level1Prefab = null;
        Oxygen level2Prefab = null;

        for (int i = 0; i < registeredOilPrefabs.Count; i++)
        {
            Oxygen prefab = registeredOilPrefabs[i];
            if (prefab == null)
                continue;

            if (prefab.Level <= 1 && level1Prefab == null)
                level1Prefab = prefab;
            else if (prefab.Level == 2 && level2Prefab == null)
                level2Prefab = prefab;
        }

        int count = Mathf.Max(0, oilShopSlotCount);
        string baseName = level1Prefab != null ? level1Prefab.name : "Oil";
        for (int i = 0; i < count; i++)
        {
            oilEntries.Add(new OilCatalogEntry
            {
                id = BuildOilShopSlotId(i),
                displayName = $"{baseName} {i + 1}",
                level1Prefab = level1Prefab,
                level2Prefab = level2Prefab
            });
        }
    }

    private void RefreshTurretCatalog()
    {
        turretEntries.Clear();
        BaseTurret level1Prefab = null;
        BaseTurret level2Prefab = null;
        BaseTurret level3Prefab = null;

        EnsureAllTurretSceneSlots();

        for (int i = 0; i < registeredTurretPrefabs.Count; i++)
        {
            BaseTurret prefab = registeredTurretPrefabs[i];
            if (prefab == null)
                continue;

            if (prefab.Level <= 1 && level1Prefab == null)
                level1Prefab = prefab;
            else if (prefab.Level == 2 && level2Prefab == null)
                level2Prefab = prefab;
            else if (prefab.Level >= 3 && level3Prefab == null)
                level3Prefab = prefab;
        }

        int count = Mathf.Max(0, registeredTurretPathPrefabs.Count);
        string baseName = level1Prefab != null ? level1Prefab.name : "Turret";
        for (int i = 0; i < count; i++)
        {
            turretEntries.Add(new TurretCatalogEntry
            {
                id = BuildTurretShopSlotId(i),
                displayName = $"{baseName} {i + 1}",
                level1Prefab = level1Prefab,
                level2Prefab = level2Prefab,
                level3Prefab = level3Prefab
            });
        }
    }

    private void EnsureAllTurretSceneSlots()
    {
        for (int i = 0; i < registeredTurretPathPrefabs.Count; i++)
        {
            GameObject slotReference = registeredTurretPathPrefabs[i];
            if (slotReference == null)
                continue;

            GameObject sceneSlotRoot = slotReference.scene.IsValid()
                ? slotReference
                : FindSceneTurretSlotRoot(slotReference, i);

            if (sceneSlotRoot == null)
                continue;

            TurretImplementation slot = ResolveTurretSlot(sceneSlotRoot);
            if (slot == null)
                slot = sceneSlotRoot.AddComponent<TurretImplementation>();

            slot.ConfigureRuntimeSlot(BuildTurretShopSlotId(i), Vector2.zero);
            activeTurretSlots[i] = slot;
        }
    }

    private void RebuildButtons()
    {
        if (listRoot == null)
            return;

        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i] != null)
                Destroy(buttons[i].gameObject);
        }

        buttons.Clear();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        int count = GetEntryCount();
        for (int i = 0; i < count; i++)
        {
            int index = i;
            Button button = BuildingListUiFactory.CreateButton(listRoot, font);
            button.onClick.AddListener(() => HandlePurchase(index));
            buttons.Add(button);
        }
    }

    private void RefreshButtons()
    {
        if (!IsInteractiveSection())
            return;

        if (buttons.Count != GetEntryCount())
            RebuildButtons();

        if (IsOilSection())
            RefreshOilButtons();
        else if (IsTurretSection())
            RefreshTurretButtons();

        refreshQueued = false;
        VillageManagement villageManagement = VillageManagement.Instance;
        lastKnownOxygen = villageManagement != null ? villageManagement.CurrentOxygen : int.MinValue;
    }

    private void RefreshOilButtons()
    {
        VillageManagement villageManagement = VillageManagement.Instance;
        for (int i = 0; i < buttons.Count && i < oilEntries.Count; i++)
        {
            OilCatalogEntry entry = oilEntries[i];
            int purchasedLevel = GetOilSlotLevel(i);
            int nextLevel = purchasedLevel + 1;
            Oxygen nextPrefab = GetOilPrefabForLevel(entry, nextLevel);
            bool completed = purchasedLevel >= 2;
            bool canAfford = nextPrefab != null && villageManagement != null && villageManagement.CurrentOxygen >= nextPrefab.CurrentOxygenPrice;
            bool canPlace = CanUseOilSlot(i, purchasedLevel);
            buttons[i].interactable = !completed && nextPrefab != null && canAfford && canPlace;

            Text text = buttons[i].GetComponentInChildren<Text>();
            if (text != null)
                text.text = BuildOilLabel(entry, purchasedLevel, nextPrefab, canPlace);
        }

        SetStatus(oilEntries.Count == 0 ? "등록된 오일 프리팹이 없습니다." : "오일은 1렙 구매 후 2렙 구매로 완료됩니다.");
    }

    private void RefreshTurretButtons()
    {
        VillageManagement villageManagement = VillageManagement.Instance;
        for (int i = 0; i < buttons.Count && i < turretEntries.Count; i++)
        {
            TurretCatalogEntry entry = turretEntries[i];
            int purchasedLevel = GetTurretSlotLevel(i);
            int nextLevel = purchasedLevel + 1;
            BaseTurret nextPrefab = GetTurretPrefabForLevel(entry, nextLevel);
            bool completed = purchasedLevel >= 3;
            bool canAfford = nextPrefab != null && villageManagement != null && villageManagement.CurrentOxygen >= nextPrefab.CurrentOxygenPrice;
            bool canPlace = CanUseTurretSlot(i, purchasedLevel);
            buttons[i].interactable = !completed && nextPrefab != null && canAfford && canPlace;

            Text text = buttons[i].GetComponentInChildren<Text>();
            if (text != null)
                text.text = BuildTurretLabel(entry, purchasedLevel, nextPrefab, canPlace);
        }

        SetStatus(turretEntries.Count == 0 ? "등록된 터렛 프리팹이 없습니다." : "터렛은 등록된 터렛 슬롯에 1렙 설치 후 2렙, 3렙으로 업그레이드됩니다.");
    }

    private void HandlePurchase(int index)
    {
        if (IsOilSection())
            HandleOilPurchase(index);
        else if (IsTurretSection())
            HandleTurretPurchase(index);
    }

    private void HandleOilPurchase(int index)
    {
        if (index < 0 || index >= oilEntries.Count)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        OilCatalogEntry entry = oilEntries[index];
        int purchasedLevel = GetOilSlotLevel(index);
        int nextLevel = purchasedLevel + 1;
        Oxygen nextPrefab = GetOilPrefabForLevel(entry, nextLevel);
        if (nextPrefab == null)
        {
            SetStatus($"{entry.displayName}은 이미 완료되었습니다.");
            return;
        }

        if (villageManagement.CurrentOxygen < nextPrefab.CurrentOxygenPrice)
        {
            SetStatus($"산소가 부족합니다. 필요 O2 {nextPrefab.CurrentOxygenPrice}");
            return;
        }

        bool success = nextLevel == 1
            ? TryInstallOilLevel1(index, entry, nextPrefab)
            : TryUpgradeOil(index, entry.id, nextPrefab);

        if (!success)
        {
            SetStatus(nextLevel == 1
                ? "이 오일 칸에 설치 가능한 path가 없습니다."
                : $"{entry.displayName} 1레벨 설치 위치를 찾지 못했습니다.");
            return;
        }

        villageManagement.TrySpendOxygen(nextPrefab.CurrentOxygenPrice);
        Shop.CloseAllShops();
        SetStatus($"{entry.displayName} {nextLevel}레벨 구매 완료");
        QueueRefresh(true);
    }

    private void HandleTurretPurchase(int index)
    {
        if (index < 0 || index >= turretEntries.Count)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        TurretCatalogEntry entry = turretEntries[index];
        int purchasedLevel = GetTurretSlotLevel(index);
        int nextLevel = purchasedLevel + 1;
        BaseTurret nextPrefab = GetTurretPrefabForLevel(entry, nextLevel);
        if (nextPrefab == null)
        {
            SetStatus($"{entry.displayName}은 이미 완료되었습니다.");
            return;
        }

        if (villageManagement.CurrentOxygen < nextPrefab.CurrentOxygenPrice)
        {
            SetStatus($"산소가 부족합니다. 필요 O2 {nextPrefab.CurrentOxygenPrice}");
            return;
        }

        bool success = nextLevel == 1
            ? TryInstallTurretLevel1(index, nextPrefab)
            : TryUpgradeTurret(index, nextPrefab);

        if (!success)
        {
            SetStatus(nextLevel == 1
                ? "이 터렛 칸에 설치 가능한 슬롯이 없습니다."
                : $"{entry.displayName} {nextLevel - 1}레벨 설치 위치를 찾지 못했습니다.");
            return;
        }

        villageManagement.TrySpendOxygen(nextPrefab.CurrentOxygenPrice);
        Shop.CloseAllShops();
        SetStatus($"{entry.displayName} {nextLevel}레벨 구매 완료");
        QueueRefresh(true);
    }

    private void HandleVillageManagementReady(VillageManagement villageManagement)
    {
        SubscribeVillageManagement(villageManagement);
        QueueRefresh(true);
    }

    private void HandleResourceChanged(VillageManagement.ResourceSnapshot snapshot)
    {
        if (snapshot.type != VillageManagement.ResourceType.Oxygen)
            return;

        if (snapshot.current == lastKnownOxygen && !refreshQueued)
            return;

        QueueRefresh(true);
    }

    private void SubscribeVillageManagement(VillageManagement villageManagement)
    {
        if (villageManagement == null)
            return;

        villageManagement.ResourceChanged -= HandleResourceChanged;
        villageManagement.ResourceChanged += HandleResourceChanged;
        lastKnownOxygen = villageManagement.CurrentOxygen;
    }

    private void UnsubscribeVillageManagement(VillageManagement villageManagement)
    {
        if (villageManagement == null)
            return;

        villageManagement.ResourceChanged -= HandleResourceChanged;
    }

    private void QueueRefresh(bool immediate = false)
    {
        refreshQueued = true;

        if (immediate && rootObject != null && rootObject.activeSelf && IsInteractiveSection())
            RefreshButtons();
    }

    private bool TryInstallOilLevel1(int entryIndex, OilCatalogEntry entry, Oxygen prefab)
    {
        if (!TryGetOilSlotTarget(entryIndex, out OilSlotTarget target))
            return false;

        return target.wayOil != null &&
               target.wayOil.TryInstallPurchasedOilAt(target.pathIndex, prefab, entry.id, true);
    }

    private bool TryUpgradeOil(int entryIndex, string slotId, Oxygen upgradePrefab)
    {
        if (!TryGetOilSlotTarget(entryIndex, out OilSlotTarget target) || target.wayOil == null)
            return false;

        return target.wayOil.TryUpgradeInstalledOilBySlotId(slotId, upgradePrefab);
    }

    private bool TryInstallTurretLevel1(int entryIndex, BaseTurret prefab)
    {
        if (!TryGetOrCreateTurretSlot(entryIndex, out TurretImplementation slot) || slot == null)
            return false;

        return slot.TryInstallFromShop(prefab);
    }

    private bool TryUpgradeTurret(int entryIndex, BaseTurret upgradePrefab)
    {
        if (!TryGetTurretSlot(entryIndex, out TurretImplementation slot) || slot == null)
            return false;

        return slot.TryUpgradeFromShop(upgradePrefab);
    }

    private bool CanUseOilSlot(int entryIndex, int purchasedLevel)
    {
        if (!TryGetOilSlotTarget(entryIndex, out OilSlotTarget target) || target.wayOil == null)
            return false;

        if (!target.wayOil.IsOilPathUsable(target.pathIndex))
            return false;

        int level = target.wayOil.GetInstalledOilLevelAt(target.pathIndex);
        if (purchasedLevel <= 0)
            return level == 0;

        return level == purchasedLevel;
    }

    private bool CanUseTurretSlot(int entryIndex, int purchasedLevel)
    {
        if (purchasedLevel <= 0)
            return HasTurretPathPrefab(entryIndex);

        if (!TryGetTurretSlot(entryIndex, out TurretImplementation slot) || slot == null)
            return false;

        return slot.CurrentTurretLevel == purchasedLevel;
    }

    private int GetEntryCount()
    {
        if (IsOilSection())
            return oilEntries.Count;
        if (IsTurretSection())
            return turretEntries.Count;
        return 0;
    }

    private bool IsInteractiveSection()
    {
        return IsOilSection() || IsTurretSection();
    }

    private bool IsOilSection()
    {
        return string.Equals(sectionTitle, "Oil", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTurretSection()
    {
        return string.Equals(sectionTitle, "Turret", StringComparison.OrdinalIgnoreCase);
    }

    private static Oxygen GetOilPrefabForLevel(OilCatalogEntry entry, int level)
    {
        if (entry == null)
            return null;

        if (level == 2)
            return entry.level2Prefab;
        if (level == 1)
            return entry.level1Prefab;
        return null;
    }

    private static BaseTurret GetTurretPrefabForLevel(TurretCatalogEntry entry, int level)
    {
        if (entry == null)
            return null;

        if (level >= 3)
            return entry.level3Prefab;
        if (level == 2)
            return entry.level2Prefab;
        if (level == 1)
            return entry.level1Prefab;
        return null;
    }

    private static string BuildOilLabel(OilCatalogEntry entry, int purchasedLevel, Oxygen nextPrefab, bool canPlace)
    {
        if (purchasedLevel >= 2)
            return $"{entry.displayName}\n완료";
        if (nextPrefab == null)
            return $"{entry.displayName}\n등록 필요";
        if (!canPlace && purchasedLevel == 0)
            return $"{entry.displayName}\n빈 Oil 슬롯 없음";
        return $"{entry.displayName}\n{purchasedLevel + 1}레벨 구매 O2 {nextPrefab.CurrentOxygenPrice}";
    }

    private static string BuildTurretLabel(TurretCatalogEntry entry, int purchasedLevel, BaseTurret nextPrefab, bool canPlace)
    {
        if (purchasedLevel >= 3)
            return $"{entry.displayName}\n완료";
        if (nextPrefab == null)
            return $"{entry.displayName}\n등록 필요";
        if (!canPlace && purchasedLevel == 0)
            return $"{entry.displayName}\n빈 Turret 슬롯 없음";
        if (!canPlace)
            return $"{entry.displayName}\n현재 설치 레벨 불일치";
        return $"{entry.displayName}\n{purchasedLevel + 1}레벨 구매 O2 {nextPrefab.CurrentOxygenPrice}";
    }

    private void SetStatus(string nextMessage)
    {
        if (statusText != null)
            statusText.text = nextMessage ?? string.Empty;
    }

    private static string BuildOilShopSlotId(int index)
    {
        return $"oil_shop_slot_{index + 1}";
    }

    private static string BuildTurretShopSlotId(int index)
    {
        return $"turret_shop_slot_{index + 1}";
    }

    private int GetOilSlotLevel(int entryIndex)
    {
        if (!TryGetOilSlotTarget(entryIndex, out OilSlotTarget target) || target.wayOil == null)
            return 0;

        return target.wayOil.GetInstalledOilLevelAt(target.pathIndex);
    }

    private int GetTurretSlotLevel(int entryIndex)
    {
        if (!TryGetTurretSlot(entryIndex, out TurretImplementation slot) || slot == null)
            return 0;

        return slot.CurrentTurretLevel;
    }

    private bool TryGetOilSlotTarget(int entryIndex, out OilSlotTarget target)
    {
        target = default;
        if (entryIndex < 0)
            return false;

        if (entryIndex >= cachedOilSlotTargets.Count)
            return false;

        target = cachedOilSlotTargets[entryIndex];
        return true;
    }

    private bool TryGetTurretSlot(int entryIndex, out TurretImplementation slot)
    {
        slot = null;
        if (activeTurretSlots.TryGetValue(entryIndex, out TurretImplementation cachedSlot) && cachedSlot != null)
        {
            slot = cachedSlot;
            return true;
        }

        if (entryIndex < 0 || entryIndex >= registeredTurretPathPrefabs.Count)
            return false;

        GameObject slotRoot = registeredTurretPathPrefabs[entryIndex];
        if (slotRoot == null)
            return false;

        if (!slotRoot.scene.IsValid())
            slotRoot = FindSceneTurretSlotRoot(slotRoot, entryIndex);

        if (slotRoot == null || !slotRoot.scene.IsValid())
            return false;

        slot = ResolveTurretSlot(slotRoot);
        if (slot != null)
            activeTurretSlots[entryIndex] = slot;

        return slot != null;
    }

    private bool TryGetOrCreateTurretSlot(int entryIndex, out TurretImplementation slot)
    {
        slot = null;
        if (TryGetTurretSlot(entryIndex, out slot))
            return slot != null;

        if (!HasTurretPathPrefab(entryIndex))
            return false;

        GameObject slotPrefab = registeredTurretPathPrefabs[entryIndex];
        if (slotPrefab == null)
            return false;

        GameObject targetObject = slotPrefab.scene.IsValid()
            ? slotPrefab
            : FindSceneTurretSlotRoot(slotPrefab, entryIndex);

        if (targetObject == null)
            return false;

        slot = ResolveTurretSlot(targetObject);
        if (slot == null)
            slot = targetObject.AddComponent<TurretImplementation>();

        slot.ConfigureRuntimeSlot(BuildTurretShopSlotId(entryIndex), Vector2.zero);

        if (slot == null)
            return false;

        activeTurretSlots[entryIndex] = slot;
        return true;
    }

    private bool HasTurretPathPrefab(int entryIndex)
    {
        return entryIndex >= 0 &&
               entryIndex < registeredTurretPathPrefabs.Count &&
               registeredTurretPathPrefabs[entryIndex] != null;
    }

    private static TurretImplementation ResolveTurretSlot(GameObject slotRoot)
    {
        if (slotRoot == null)
            return null;

        TurretImplementation slot = slotRoot.GetComponent<TurretImplementation>();
        if (slot == null)
            slot = slotRoot.GetComponentInChildren<TurretImplementation>(true);

        return slot;
    }

    private GameObject FindSceneTurretSlotRoot(GameObject slotReference, int entryIndex)
    {
        if (slotReference == null)
            return null;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
            return null;

        string exactName = slotReference.name;
        Path[] allPaths = FindObjectsByType<Path>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        List<GameObject> exactMatches = new List<GameObject>();
        for (int i = 0; i < allPaths.Length; i++)
        {
            Path path = allPaths[i];
            if (path == null || path.gameObject.scene != activeScene)
                continue;

            if (string.Equals(path.gameObject.name, exactName, StringComparison.Ordinal))
                exactMatches.Add(path.gameObject);
        }

        if (exactMatches.Count == 1)
            return exactMatches[0];

        if (exactMatches.Count > 1)
        {
            exactMatches.Sort((left, right) =>
                string.Compare(GetHierarchyPath(left.transform), GetHierarchyPath(right.transform), StringComparison.Ordinal));
            int clampedIndex = Mathf.Clamp(entryIndex, 0, exactMatches.Count - 1);
            return exactMatches[clampedIndex];
        }

        TurretImplementation[] allSlots = FindObjectsByType<TurretImplementation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        List<GameObject> stableMatches = new List<GameObject>();
        string stableReferenceId = ShopIdentityUtility.GetStableId(string.Empty, slotReference);
        for (int i = 0; i < allSlots.Length; i++)
        {
            TurretImplementation sceneSlot = allSlots[i];
            if (sceneSlot == null || sceneSlot.gameObject.scene != activeScene)
                continue;

            string sceneStableId = ShopIdentityUtility.GetStableId(string.Empty, sceneSlot.gameObject);
            if (string.Equals(sceneStableId, stableReferenceId, StringComparison.Ordinal))
                stableMatches.Add(sceneSlot.gameObject);
        }

        if (stableMatches.Count == 0)
            return null;

        stableMatches.Sort((left, right) =>
            string.Compare(GetHierarchyPath(left.transform), GetHierarchyPath(right.transform), StringComparison.Ordinal));
        return stableMatches[Mathf.Clamp(entryIndex, 0, stableMatches.Count - 1)];
    }

    private void RefreshOilSlotTargetCache()
    {
        cachedOilSlotTargets.Clear();
        if (!IsOilSection())
            return;

        List<WayOil> orderedWayOils = new List<WayOil>(WayOil.RegisteredWayOils);
        orderedWayOils.Sort(CompareWayOilOrder);

        for (int i = 0; i < orderedWayOils.Count; i++)
        {
            WayOil wayOil = orderedWayOils[i];
            if (wayOil == null)
                continue;

            IReadOnlyList<Transform> connectedPaths = wayOil.ConnectedOilPaths;
            for (int pathIndex = 0; pathIndex < connectedPaths.Count; pathIndex++)
            {
                if (connectedPaths[pathIndex] == null)
                    continue;

                cachedOilSlotTargets.Add(new OilSlotTarget
                {
                    wayOil = wayOil,
                    pathIndex = pathIndex
                });
            }
        }
    }

    private static int CompareWayOilOrder(WayOil left, WayOil right)
    {
        if (left == right)
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        return string.Compare(GetHierarchyPath(left.transform), GetHierarchyPath(right.transform), StringComparison.Ordinal);
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        List<string> names = new List<string>();
        Transform current = target;
        while (current != null)
        {
            names.Add($"{current.name}[{current.GetSiblingIndex()}]");
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }
}
