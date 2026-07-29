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
        public string familyId;
        public string displayName;
        public Oxygen level1Prefab;
        public Oxygen level2Prefab;
    }

    [Serializable]
    public sealed class OilPrefabGroup
    {
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
    [SerializeField] private List<OilPrefabGroup> registeredOilPrefabs = new List<OilPrefabGroup>();

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

    public void PrepareRuntimeRestore()
    {
        RefreshCatalogs();
    }

    public bool TryRestoreOxygenGeneratorState(VillageManagement.OxygenGeneratorState state)
    {
        if (state == null || !IsOilSection())
            return false;

        RefreshCatalogs();
        OilCatalogEntry entry = FindOilEntryForRestore(state);
        Oxygen prefab = GetOilPrefabForLevel(entry, Mathf.Max(1, state.level));
        if (entry == null || prefab == null)
            return false;

        if (!TryFindOilSlotTargetBySlotId(state.slotId, out OilSlotTarget target) || target.wayOil == null)
            return false;

        if (!target.wayOil.TryInstallPurchasedOilAt(target.pathIndex, prefab, state.slotId, true, entry.id))
            return false;

        if (!target.wayOil.TryGetInstalledOilBySlotId(state.slotId, out Oxygen installedOil) || installedOil == null)
            return false;

        installedOil.AssignPurchaseEntryId(entry.id);
        installedOil.ApplySavedState(state.level, state.storedOxygen);
        installedOil.PushState();
        return true;
    }

    public bool TryRestoreTurretState(VillageManagement.TurretState state)
    {
        if (state == null || !IsTurretSection())
            return false;

        RefreshCatalogs();

        if (TryRestoreTurretStateBySlotId(state))
            return true;

        BaseTurret fallbackPrefab = ResolveTurretPrefabByCatalogId(state.turretId, state.level);
        if (fallbackPrefab == null)
            return false;

        for (int i = 0; i < turretEntries.Count; i++)
        {
            if (!TryGetOrCreateTurretSlot(i, out TurretImplementation slot) || slot == null)
                continue;

            if (slot.RestoreFromState(state, fallbackPrefab))
                return true;
        }

        return false;
    }

    private bool TryRestoreTurretStateBySlotId(VillageManagement.TurretState state)
    {
        for (int i = 0; i < turretEntries.Count; i++)
        {
            TurretCatalogEntry entry = turretEntries[i];
            if (entry == null || !string.Equals(entry.id, state.slotId, StringComparison.Ordinal))
                continue;

            if (!TryGetOrCreateTurretSlot(i, out TurretImplementation slot) || slot == null)
                return false;

            BaseTurret prefab = GetTurretPrefabForLevel(entry, Mathf.Max(1, state.level));
            return slot.RestoreFromState(state, prefab);
        }

        return false;
    }

    private BaseTurret ResolveTurretPrefabByCatalogId(string turretId, int level)
    {
        if (string.IsNullOrWhiteSpace(turretId))
            return null;

        for (int i = 0; i < turretEntries.Count; i++)
        {
            TurretCatalogEntry entry = turretEntries[i];
            if (entry == null)
                continue;

            BaseTurret[] candidates =
            {
                entry.level1Prefab,
                entry.level2Prefab,
                entry.level3Prefab
            };

            for (int j = 0; j < candidates.Length; j++)
            {
                BaseTurret candidate = candidates[j];
                if (candidate != null && string.Equals(candidate.CatalogId, turretId, StringComparison.Ordinal))
                    return GetTurretPrefabForLevel(entry, Mathf.Max(1, level));
            }
        }

        return null;
    }

    private void RefreshOilCatalog()
    {
        oilEntries.Clear();
        if (registeredOilPrefabs == null || registeredOilPrefabs.Count == 0)
            return;

        for (int i = 0; i < registeredOilPrefabs.Count; i++)
        {
            OilPrefabGroup group = registeredOilPrefabs[i];
            if (group == null)
                continue;

            Oxygen level1Prefab = group.level1Prefab;
            Oxygen level2Prefab = group.level2Prefab;
            Oxygen identitySource = level1Prefab != null ? level1Prefab : level2Prefab;
            if (identitySource == null)
                continue;

            string familyId = identitySource.ShopFamilyId;
            if (string.IsNullOrWhiteSpace(familyId))
                continue;

            string displayName = level1Prefab != null
                ? level1Prefab.name
                : level2Prefab.name;

            oilEntries.Add(new OilCatalogEntry
            {
                id = BuildOilPurchaseEntryId(i),
                familyId = familyId,
                displayName = displayName,
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
            int purchasedLevel = GetOilTypeLevel(entry);
            int nextLevel = purchasedLevel + 1;
            Oxygen nextPrefab = GetOilPrefabForLevel(entry, nextLevel);
            bool completed = purchasedLevel >= GetMaxOilLevel(entry);
            bool canAfford = nextPrefab != null && villageManagement != null && villageManagement.CurrentOxygen >= nextPrefab.CurrentOxygenPrice;
            bool canPlace = CanUseOilEntry(entry, purchasedLevel);
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
        int purchasedLevel = GetOilTypeLevel(entry);
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
            ? TryInstallOilLevel1(entry, nextPrefab)
            : TryUpgradeOil(entry, nextPrefab);

        if (!success)
        {
            SetStatus(nextLevel == 1
                ? "설치 가능한 Oil 슬롯이 없습니다."
                : $"{entry.displayName} {nextLevel - 1}레벨 설치 위치를 찾지 못했습니다.");
            return;
        }

        villageManagement.TrySpendOxygen(nextPrefab.CurrentOxygenPrice);
        villageManagement.SetPurchasedOxygenLevel(entry.id, nextLevel);
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

    private bool TryInstallOilLevel1(OilCatalogEntry entry, Oxygen prefab)
    {
        if (entry == null || prefab == null)
            return false;

        List<WayOil> orderedWayOils = new List<WayOil>(WayOil.RegisteredWayOils);
        orderedWayOils.Sort(CompareWayOilOrder);

        for (int i = 0; i < orderedWayOils.Count; i++)
        {
            WayOil wayOil = orderedWayOils[i];
            if (wayOil != null && wayOil.TryInstallPurchasedOil(prefab, string.Empty, true, entry.id))
                return true;
        }

        return false;
    }

    private bool TryUpgradeOil(OilCatalogEntry entry, Oxygen upgradePrefab)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.id) || upgradePrefab == null)
            return false;

        List<WayOil> orderedWayOils = new List<WayOil>(WayOil.RegisteredWayOils);
        orderedWayOils.Sort(CompareWayOilOrder);

        for (int i = 0; i < orderedWayOils.Count; i++)
        {
            WayOil wayOil = orderedWayOils[i];
            if (wayOil != null && wayOil.TryUpgradeInstalledOilByPurchaseEntryId(entry.id, upgradePrefab))
                return true;
        }

        return false;
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

    private bool CanUseOilEntry(OilCatalogEntry entry, int purchasedLevel)
    {
        if (entry == null)
            return false;

        if (purchasedLevel <= 0)
            return HasAnyUsableOilSlot();

        return GetOilTypeLevel(entry) == purchasedLevel;
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
        if (purchasedLevel >= GetMaxOilLevel(entry))
            return $"{entry.displayName}\n완료";
        if (nextPrefab == null)
            return $"{entry.displayName}\n등록 필요";
        if (!canPlace && purchasedLevel == 0)
            return $"{entry.displayName}\n빈 Oil 슬롯 없음";
        if (!canPlace)
            return $"{entry.displayName}\n현재 설치 레벨 불일치";
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

    private static string BuildTurretShopSlotId(int index)
    {
        return $"turret_shop_slot_{index + 1}";
    }

    private int GetOilTypeLevel(OilCatalogEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.id))
            return 0;

        VillageManagement villageManagement = VillageManagement.Instance;
        if (villageManagement == null)
            return 0;

        return Mathf.Max(0, villageManagement.GetPurchasedOxygenLevel(entry.id));
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

    private bool HasAnyUsableOilSlot()
    {
        List<WayOil> orderedWayOils = new List<WayOil>(WayOil.RegisteredWayOils);
        orderedWayOils.Sort(CompareWayOilOrder);

        for (int i = 0; i < orderedWayOils.Count; i++)
        {
            WayOil wayOil = orderedWayOils[i];
            if (wayOil == null)
                continue;

            for (int pathIndex = 0; pathIndex < wayOil.ConnectedOilPaths.Count; pathIndex++)
            {
                if (wayOil.IsOilPathUsable(pathIndex) && wayOil.GetInstalledOilLevelAt(pathIndex) == 0)
                    return true;
            }
        }

        return false;
    }

    private bool TryFindOilSlotTargetBySlotId(string slotId, out OilSlotTarget target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(slotId))
            return false;

        for (int i = 0; i < cachedOilSlotTargets.Count; i++)
        {
            OilSlotTarget candidate = cachedOilSlotTargets[i];
            if (candidate.wayOil == null)
                continue;

            if (string.Equals(candidate.wayOil.GetSlotIdAt(candidate.pathIndex), slotId, StringComparison.Ordinal))
            {
                target = candidate;
                return true;
            }
        }

        return false;
    }

    private OilCatalogEntry FindOilEntryByCatalogId(string catalogId)
    {
        string normalizedId = Oxygen.BuildShopFamilyId(catalogId, catalogId);
        for (int i = 0; i < oilEntries.Count; i++)
        {
            OilCatalogEntry entry = oilEntries[i];
            if (entry != null &&
                (string.Equals(entry.familyId, catalogId, StringComparison.Ordinal) ||
                 string.Equals(entry.familyId, normalizedId, StringComparison.Ordinal)))
                return entry;
        }

        return null;
    }

    private OilCatalogEntry FindOilEntryForRestore(VillageManagement.OxygenGeneratorState state)
    {
        if (state == null)
            return null;

        if (!string.IsNullOrWhiteSpace(state.purchaseEntryId))
        {
            for (int i = 0; i < oilEntries.Count; i++)
            {
                OilCatalogEntry entry = oilEntries[i];
                if (entry != null && string.Equals(entry.id, state.purchaseEntryId, StringComparison.Ordinal))
                    return entry;
            }
        }

        return FindOilEntryByCatalogId(state.oxygenId);
    }

    private static string BuildOilPurchaseEntryId(int index)
    {
        return $"oil_purchase_entry_{index + 1}";
    }

    private static int GetMaxOilLevel(OilCatalogEntry entry)
    {
        if (entry == null)
            return 0;

        if (entry.level2Prefab != null)
            return 2;
        if (entry.level1Prefab != null)
            return 1;
        return 0;
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
