using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-500)]
public class VillageManagement : MonoBehaviour
{
    private enum SaveMode
    {
        Immediate,
        Delayed
    }

    public enum ResourceType
    {
        Oxygen,
        Energy
    }

    [Serializable]
    public struct ResourceSnapshot
    {
        public ResourceType type;
        public int current;
        public int capacity;

        public ResourceSnapshot(ResourceType type, int current, int capacity)
        {
            this.type = type;
            this.current = current;
            this.capacity = capacity;
        }
    }

    [Serializable]
    public class BuildingState
    {
        public string slotId;
        public string buildingId;
        public string buildingType;
        public int level;
        public int currentSalary;
        public int maxSalary;
        public bool isPlaced;
        public bool isWorking;
        public bool underConstruction;
        public float constructionRemainingSeconds;
    }

    [Serializable]
    public class TurretState
    {
        public string slotId;
        public string turretId;
        public int level;
        public int currentAmmo;
        public int maxAmmo;
        public bool isPlaced;
    }

    [Serializable]
    public class OxygenGeneratorState
    {
        public string slotId;
        public string oxygenId;
        public string purchaseEntryId;
        public int level;
        public bool isPlaced;
        public bool isProducing;
        public int storedOxygen;
    }

    [Serializable]
    public class LiftState
    {
        public string liftId;
        public bool isActive;
    }

    [Serializable]
    public class PurchaseLevelState
    {
        public string id;
        public int level;
    }

    [Serializable]
    public class VillageSaveData
    {
        public int bankLevel = 1;
        public string selectedWhiteBloodCellId = string.Empty;
        public int selectedArcadeSceneIndex = 0;
        public int selectedArcadePlayerType = -1;
        public int currentOxygen = 0;
        public int oxygenCapacity = 100;
        public int currentEnergy = 0;
        public int energyCapacity = 100;
        public int emergencyDifficulty = 1;
        public List<BuildingState> buildings = new List<BuildingState>();
        public List<TurretState> turrets = new List<TurretState>();
        public List<OxygenGeneratorState> oxygenGenerators = new List<OxygenGeneratorState>();
        public List<LiftState> lifts = new List<LiftState>();
        public List<PurchaseLevelState> turretPurchaseLevels = new List<PurchaseLevelState>();
        public List<PurchaseLevelState> oxygenPurchaseLevels = new List<PurchaseLevelState>();
        public List<string> ownedBuildingIds = new List<string>();
        public List<string> ownedTurretIds = new List<string>();
        public List<string> ownedOxygenIds = new List<string>();
        public List<string> ownedCustomerBloodIds = new List<string>();
        public List<string> ownedWhiteBloodCellIds = new List<string>();
        public int lifetimeArcadeOxygenEarned = 0;
        public int lifetimeArcadeEnergyEarned = 0;
        public string lastSavedUtc = string.Empty;
    }

    [Serializable]
    public class VillageManagementTestControls
    {
        [Serializable]
        public class BloodDebugState
        {
            public string id;
            public bool active;
        }

        [Header("Direct Resource Values")]
        public int currentOxygen;
        public int oxygenCapacity;
        public int currentEnergy;
        public int energyCapacity;
        public bool applyResourceValues;

        [Header("Quick Fill / Empty")]
        public bool fillAllBuildingSalary;
        public bool emptyAllBuildingSalary;
        public bool fillAllTurretAmmo;
        public bool emptyAllTurretAmmo;
        public bool fillEnergyToMax;
        public bool emptyEnergy;

        [Header("Emergency")]
        public int emergencyDifficulty = 1;
        public bool triggerEmergency;

        [Header("Blood Preview")]
        public bool refreshBloodDebugStates;
        public List<BloodDebugState> activeCustomerBloods = new List<BloodDebugState>();
        public List<BloodDebugState> activeWhiteBloodCells = new List<BloodDebugState>();
    }

    [Serializable]
    public class ArcadePlayerEntry
    {
        [Range(1, 5)] public int playerType = 1;
        public bool available = true;
    }

    public static VillageManagement Instance { get; private set; }
    public static event Action<VillageManagement> InstanceReady;

    public event Action<ResourceSnapshot> ResourceChanged;
    public event Action<VillageSaveData> SaveDataChanged;
    public event Action<int> EmergencyDifficultyChanged;

    [Header("Persistence")]
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool autoSaveOnChange = true;
    [SerializeField] private string saveFileName = "village_save.json";
    [SerializeField] private float delayedSaveIntervalSeconds = 2f;

    [Header("Arcade Reward Mapping")]
    [SerializeField] private bool awardArcadeOxygenToCurrentOxygen = true;
    [SerializeField] private bool awardArcadeEnergyToCurrentEnergy = true;

    [Header("Arcade Players")]
    [SerializeField] private List<ArcadePlayerEntry> arcadePlayers = new List<ArcadePlayerEntry>
    {
        new ArcadePlayerEntry { playerType = 1, available = true },
        new ArcadePlayerEntry { playerType = 2, available = true },
        new ArcadePlayerEntry { playerType = 3, available = true },
        new ArcadePlayerEntry { playerType = 4, available = true },
        new ArcadePlayerEntry { playerType = 5, available = true }
    };

    [Header("Debug / Test")]
    [SerializeField] private VillageManagementTestControls debugControls = new VillageManagementTestControls();

    private VillageSaveData saveData = new VillageSaveData();
    private bool loaded;
    private bool hasPendingDelayedSave;
    private float nextDelayedSaveAt;
    private bool restoreInProgress;
    private Coroutine restoreSceneRoutine;
    private VillageManagementDebugProxy debugProxy;

    public VillageSaveData SaveData => saveData;
    public VillageManagementTestControls DebugControls => debugControls;

    public int CurrentOxygen => saveData.currentOxygen;
    public int OxygenCapacity => saveData.oxygenCapacity;
    public int CurrentEnergy => saveData.currentEnergy;
    public int EnergyCapacity => saveData.energyCapacity;
    public int EmergencyDifficulty => saveData.emergencyDifficulty;
    public int BankLevel => saveData.bankLevel;
    public string SelectedWhiteBloodCellId => saveData.selectedWhiteBloodCellId;
    public int SelectedArcadeSceneIndex => saveData.selectedArcadeSceneIndex;
    public int SelectedArcadePlayerType => saveData.selectedArcadePlayerType;

    public IReadOnlyList<BuildingState> Buildings => saveData.buildings;
    public IReadOnlyList<TurretState> Turrets => saveData.turrets;
    public IReadOnlyList<OxygenGeneratorState> OxygenGenerators => saveData.oxygenGenerators;
    public IReadOnlyList<LiftState> Lifts => saveData.lifts;
    public IReadOnlyList<string> OwnedBuildingIds => saveData.ownedBuildingIds;
    public IReadOnlyList<string> OwnedTurretIds => saveData.ownedTurretIds;
    public IReadOnlyList<string> OwnedOxygenIds => saveData.ownedOxygenIds;
    public IReadOnlyList<PurchaseLevelState> TurretPurchaseLevels => saveData.turretPurchaseLevels;
    public IReadOnlyList<PurchaseLevelState> OxygenPurchaseLevels => saveData.oxygenPurchaseLevels;
    public IReadOnlyList<string> OwnedCustomerBloodIds => saveData.ownedCustomerBloodIds;
    public IReadOnlyList<string> OwnedWhiteBloodCellIds => saveData.ownedWhiteBloodCellIds;
    public IReadOnlyList<ArcadePlayerEntry> ArcadePlayers => arcadePlayers;
    public bool IsRestoreInProgress => restoreInProgress;

    public static VillageManagement EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        VillageManagement found = FindFirstObjectByType<VillageManagement>();
        if (found != null)
            return found;

        GameObject root = new GameObject(nameof(VillageManagement));
        return root.AddComponent<VillageManagement>();
    }

    private string SavePath => System.IO.Path.Combine(Application.persistentDataPath, saveFileName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        Load();
        EnsureDebugProxy();
        InstanceReady?.Invoke(this);
    }

    private void Start()
    {
        ScheduleRestoreSceneState(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            FlushPendingSave();
        }
    }

    private void Update()
    {
        ProcessDebugControls();
        ProcessDelayedSave();
    }

    public void Load()
    {
        if (loaded)
            return;

        try
        {
            if (File.Exists(SavePath))
            {
                string json = File.ReadAllText(SavePath);
                VillageSaveData loadedData = JsonUtility.FromJson<VillageSaveData>(json);
                saveData = loadedData ?? new VillageSaveData();
            }
            else
            {
                saveData = new VillageSaveData();
                Save();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Village save load failed. A fresh save will be created.\n{exception}");
            saveData = new VillageSaveData();
            Save();
        }

        SanitizeSaveData();
        loaded = true;
        NotifyAllResourceSnapshots();
        SaveDataChanged?.Invoke(saveData);
        EmergencyDifficultyChanged?.Invoke(saveData.emergencyDifficulty);
    }

    public void FlushPendingSaveNow()
    {
        FlushPendingSave();
    }

    public void Save(bool updateLastSavedUtc = true)
    {
        try
        {
            SanitizeSaveData();
            if (updateLastSavedUtc)
                saveData.lastSavedUtc = DateTime.UtcNow.ToString("O");
            string json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(SavePath, json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Village save write failed.\n{exception}");
        }
    }

    public void ResetSaveData()
    {
        saveData = new VillageSaveData();
        hasPendingDelayedSave = false;
        Save();
        NotifyAllResourceSnapshots();
        SaveDataChanged?.Invoke(saveData);
        EmergencyDifficultyChanged?.Invoke(saveData.emergencyDifficulty);
    }

    public void ResetAllVillageProgress()
    {
        ClearPlacedVillageObjects();
        saveData.bankLevel = 1;
        saveData.selectedWhiteBloodCellId = string.Empty;
        saveData.selectedArcadeSceneIndex = 0;
        saveData.currentOxygen = 0;
        saveData.currentEnergy = 0;
        saveData.emergencyDifficulty = 1;
        saveData.lifetimeArcadeOxygenEarned = 0;
        saveData.lifetimeArcadeEnergyEarned = 0;
        saveData.lastSavedUtc = string.Empty;

        Save(false);
        NotifyAllResourceSnapshots();
        SaveDataChanged?.Invoke(saveData);
        EmergencyDifficultyChanged?.Invoke(saveData.emergencyDifficulty);
    }

    public void ClearPlacedVillageObjects()
    {
        Path[] paths = FindObjectsByType<Path>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < paths.Length; i++)
        {
            Path path = paths[i];
            if (path != null)
                path.ClearPlacementState();
        }

        TurretImplementation[] turretSlots = FindObjectsByType<TurretImplementation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < turretSlots.Length; i++)
        {
            TurretImplementation slot = turretSlots[i];
            if (slot != null && slot.CurrentTurret != null)
                slot.RemoveTurret();
        }

        WayOil[] wayOils = FindObjectsByType<WayOil>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < wayOils.Length; i++)
        {
            if (wayOils[i] != null)
                wayOils[i].RemoveAllInstalledOils();
        }

        LiftSpot[] liftSpots = FindObjectsByType<LiftSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < liftSpots.Length; i++)
        {
            LiftSpot liftSpot = liftSpots[i];
            if (liftSpot == null)
                continue;

            IReadOnlyList<Lift> lifts = liftSpot.RegisteredLifts;
            for (int liftIndex = 0; liftIndex < lifts.Count; liftIndex++)
            {
                if (lifts[liftIndex] != null)
                    lifts[liftIndex].ApplyRuntimeActive(false);
            }
        }

        saveData.buildings.Clear();
        saveData.turrets.Clear();
        saveData.oxygenGenerators.Clear();
        saveData.lifts.Clear();
        saveData.ownedBuildingIds.Clear();
        saveData.ownedTurretIds.Clear();
        saveData.ownedOxygenIds.Clear();
        saveData.turretPurchaseLevels.Clear();
        saveData.oxygenPurchaseLevels.Clear();

        SaveDataChanged?.Invoke(saveData);
        FlushPendingSave();
    }

    public void ApplyArcadeResults(int oxygenReward, int energyReward)
    {
        oxygenReward = Mathf.Max(0, oxygenReward);
        energyReward = Mathf.Max(0, energyReward);

        saveData.lifetimeArcadeOxygenEarned += oxygenReward;
        saveData.lifetimeArcadeEnergyEarned += energyReward;

        if (awardArcadeOxygenToCurrentOxygen)
            AddOxygen(oxygenReward);

        if (awardArcadeEnergyToCurrentEnergy)
            AddEnergy(energyReward);

        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void AddOxygen(int amount)
    {
        SetCurrentOxygen(saveData.currentOxygen + amount);
    }

    public bool TrySpendOxygen(int amount)
    {
        amount = Mathf.Max(0, amount);
        if (saveData.currentOxygen < amount)
            return false;

        SetCurrentOxygen(saveData.currentOxygen - amount);
        return true;
    }

    public void SetCurrentOxygen(int value)
    {
        int clamped = Mathf.Clamp(value, 0, saveData.oxygenCapacity);
        if (saveData.currentOxygen == clamped)
            return;

        saveData.currentOxygen = clamped;
        BroadcastResource(ResourceType.Oxygen);
        SaveAndBroadcast(SaveMode.Delayed);
    }

    public void SetOxygenCapacity(int value, bool keepFillRatio = false)
    {
        int previousCapacity = Mathf.Max(1, saveData.oxygenCapacity);
        int nextCapacity = Mathf.Max(0, value);
        if (saveData.oxygenCapacity == nextCapacity)
            return;

        float fillRatio = previousCapacity > 0 ? (float)saveData.currentOxygen / previousCapacity : 0f;
        saveData.oxygenCapacity = nextCapacity;
        saveData.currentOxygen = keepFillRatio
            ? Mathf.Clamp(Mathf.RoundToInt(saveData.oxygenCapacity * fillRatio), 0, saveData.oxygenCapacity)
            : Mathf.Clamp(saveData.currentOxygen, 0, saveData.oxygenCapacity);

        BroadcastResource(ResourceType.Oxygen);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void AddEnergy(int amount)
    {
        SetCurrentEnergy(saveData.currentEnergy + amount);
    }

    public bool TrySpendEnergy(int amount)
    {
        amount = Mathf.Max(0, amount);
        if (saveData.currentEnergy < amount)
            return false;

        SetCurrentEnergy(saveData.currentEnergy - amount);
        return true;
    }

    public void SetCurrentEnergy(int value)
    {
        int clamped = Mathf.Clamp(value, 0, saveData.energyCapacity);
        if (saveData.currentEnergy == clamped)
            return;

        saveData.currentEnergy = clamped;
        BroadcastResource(ResourceType.Energy);
        SaveAndBroadcast(SaveMode.Delayed);
    }

    public void SetEnergyCapacity(int value, bool keepFillRatio = false)
    {
        int previousCapacity = Mathf.Max(1, saveData.energyCapacity);
        int nextCapacity = Mathf.Max(0, value);
        if (saveData.energyCapacity == nextCapacity)
            return;

        float fillRatio = previousCapacity > 0 ? (float)saveData.currentEnergy / previousCapacity : 0f;
        saveData.energyCapacity = nextCapacity;
        saveData.currentEnergy = keepFillRatio
            ? Mathf.Clamp(Mathf.RoundToInt(saveData.energyCapacity * fillRatio), 0, saveData.energyCapacity)
            : Mathf.Clamp(saveData.currentEnergy, 0, saveData.energyCapacity);

        BroadcastResource(ResourceType.Energy);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void SetEmergencyDifficulty(int difficulty)
    {
        int clamped = Mathf.Clamp(difficulty, 1, 26);
        if (saveData.emergencyDifficulty == clamped)
            return;

        saveData.emergencyDifficulty = clamped;
        EmergencyDifficultyChanged?.Invoke(saveData.emergencyDifficulty);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void SetBankLevel(int level)
    {
        int clamped = Mathf.Clamp(level, 1, 3);
        if (saveData.bankLevel == clamped)
            return;

        saveData.bankLevel = clamped;
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void SetSelectedWhiteBloodCell(string id)
    {
        string next = string.IsNullOrWhiteSpace(id) ? string.Empty : id;
        if (saveData.selectedWhiteBloodCellId == next)
            return;

        saveData.selectedWhiteBloodCellId = next;
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void SetSelectedArcadeSceneIndex(int index)
    {
        int next = Mathf.Max(0, index);
        if (saveData.selectedArcadeSceneIndex == next)
            return;

        saveData.selectedArcadeSceneIndex = next;
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void SetSelectedArcadePlayerType(int playerType)
    {
        int next = playerType <= 0 ? -1 : Mathf.Clamp(playerType, 1, 5);
        if (saveData.selectedArcadePlayerType == next)
            return;

        saveData.selectedArcadePlayerType = next;
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void UpsertBuildingState(BuildingState state, bool immediate = false)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.slotId))
            return;

        BuildingState existing = FindBuildingState(state.slotId, state.buildingType);
        if (existing == null)
        {
            saveData.buildings.Add(CloneBuildingState(state));
        }
        else
        {
            CopyBuildingState(state, existing);
        }

        SaveAndBroadcast(immediate ? SaveMode.Immediate : GetBuildingSaveMode(existing, state));
    }

    public void RemoveBuildingState(string slotId, string buildingId = null, bool saveImmediately = true)
    {
        RemoveBuildingState(slotId, buildingId, null, saveImmediately);
    }

    public void RemoveBuildingState(string slotId, string buildingId, string buildingType, bool saveImmediately = true)
    {
        if (string.IsNullOrWhiteSpace(slotId) || saveData.buildings == null)
            return;

        for (int i = saveData.buildings.Count - 1; i >= 0; i--)
        {
            BuildingState state = saveData.buildings[i];
            if (state == null || state.slotId != slotId)
                continue;

            if (!string.IsNullOrWhiteSpace(buildingId) &&
                !string.Equals(state.buildingId, buildingId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(buildingType) &&
                !string.Equals(state.buildingType, buildingType, StringComparison.Ordinal))
            {
                continue;
            }

            if (state != null)
            {
                saveData.buildings.RemoveAt(i);
                if (saveImmediately)
                    SaveAndBroadcast(SaveMode.Immediate);
                return;
            }
        }
    }

    public void UpsertTurretState(TurretState state, bool immediate = false)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.slotId))
            return;

        TurretState existing = FindTurretState(state.slotId);
        if (existing == null)
        {
            saveData.turrets.Add(CloneTurretState(state));
        }
        else
        {
            CopyTurretState(state, existing);
        }

        SaveAndBroadcast(immediate ? SaveMode.Immediate : GetTurretSaveMode(existing, state));
    }

    public void RemoveTurretState(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || saveData.turrets == null)
            return;

        for (int i = saveData.turrets.Count - 1; i >= 0; i--)
        {
            TurretState state = saveData.turrets[i];
            if (state != null && state.slotId == slotId)
            {
                saveData.turrets.RemoveAt(i);
                SaveAndBroadcast(SaveMode.Immediate);
                return;
            }
        }
    }

    public void UpsertOxygenGeneratorState(OxygenGeneratorState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.slotId))
            return;

        OxygenGeneratorState existing = FindOxygenGeneratorState(state.slotId);
        if (existing == null)
        {
            saveData.oxygenGenerators.Add(CloneOxygenGeneratorState(state));
        }
        else
        {
            CopyOxygenGeneratorState(state, existing);
        }

        SaveAndBroadcast(GetOxygenGeneratorSaveMode(existing, state));
    }

    public void RemoveOxygenGeneratorState(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || saveData.oxygenGenerators == null)
            return;

        for (int i = saveData.oxygenGenerators.Count - 1; i >= 0; i--)
        {
            OxygenGeneratorState state = saveData.oxygenGenerators[i];
            if (state != null && state.slotId == slotId)
            {
                saveData.oxygenGenerators.RemoveAt(i);
                SaveAndBroadcast(SaveMode.Immediate);
                return;
            }
        }
    }

    public void UpsertLiftState(LiftState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.liftId))
            return;

        LiftState existing = FindLiftState(state.liftId);
        if (existing == null)
            saveData.lifts.Add(CloneLiftState(state));
        else
            CopyLiftState(state, existing);

        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void SetOwnedCustomerBloodIds(IEnumerable<string> ids)
    {
        ReplaceStringList(saveData.ownedCustomerBloodIds, ids);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void SetOwnedWhiteBloodCellIds(IEnumerable<string> ids)
    {
        ReplaceStringList(saveData.ownedWhiteBloodCellIds, ids);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public bool HasOwnedCustomerBlood(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && saveData.ownedCustomerBloodIds.Contains(id);
    }

    public bool HasOwnedBuilding(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && saveData.ownedBuildingIds.Contains(id);
    }

    public bool HasOwnedTurret(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && saveData.ownedTurretIds.Contains(id);
    }

    public bool HasOwnedOxygen(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && saveData.ownedOxygenIds.Contains(id);
    }

    public bool HasOwnedWhiteBloodCell(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && saveData.ownedWhiteBloodCellIds.Contains(id);
    }

    public int GetPurchasedTurretLevel(string id)
    {
        return GetPurchaseLevel(saveData.turretPurchaseLevels, id);
    }

    public int GetPurchasedOxygenLevel(string id)
    {
        return GetPurchaseLevel(saveData.oxygenPurchaseLevels, id);
    }

    public void SetPurchasedTurretLevel(string id, int level)
    {
        SetPurchaseLevel(saveData.turretPurchaseLevels, id, level);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public void SetPurchasedOxygenLevel(string id, int level)
    {
        SetPurchaseLevel(saveData.oxygenPurchaseLevels, id, level);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    public bool IsArcadePlayerAvailable(int playerType)
    {
        ArcadePlayerEntry entry = FindArcadePlayerEntry(playerType);
        return entry != null && entry.available;
    }

    public bool TryGetArcadePlayer(int playerType, out ArcadePlayerEntry entry)
    {
        entry = FindArcadePlayerEntry(playerType);
        return entry != null;
    }

    public List<ArcadePlayerEntry> GetAvailableArcadePlayers()
    {
        List<ArcadePlayerEntry> results = new List<ArcadePlayerEntry>();
        for (int i = 0; i < arcadePlayers.Count; i++)
        {
            ArcadePlayerEntry entry = arcadePlayers[i];
            if (entry == null || !entry.available)
                continue;

            results.Add(entry);
        }

        return results;
    }

    public void AddOwnedCustomerBlood(string id)
    {
        AddUniqueString(saveData.ownedCustomerBloodIds, id);
    }

    public void AddOwnedBuilding(string id)
    {
        AddUniqueString(saveData.ownedBuildingIds, id);
    }

    public void AddOwnedTurret(string id)
    {
        AddUniqueString(saveData.ownedTurretIds, id);
    }

    public void AddOwnedOxygen(string id)
    {
        AddUniqueString(saveData.ownedOxygenIds, id);
    }

    public void AddOwnedWhiteBloodCell(string id)
    {
        AddUniqueString(saveData.ownedWhiteBloodCellIds, id);
    }

    public void RemoveOwnedCustomerBlood(string id)
    {
        RemoveString(saveData.ownedCustomerBloodIds, id);
    }

    public void RemoveOwnedWhiteBloodCell(string id)
    {
        RemoveString(saveData.ownedWhiteBloodCellIds, id);
    }

    public ResourceSnapshot GetSnapshot(ResourceType type)
    {
        return type == ResourceType.Oxygen
            ? new ResourceSnapshot(type, saveData.currentOxygen, saveData.oxygenCapacity)
            : new ResourceSnapshot(type, saveData.currentEnergy, saveData.energyCapacity);
    }

    private void NotifyAllResourceSnapshots()
    {
        BroadcastResource(ResourceType.Oxygen);
        BroadcastResource(ResourceType.Energy);
    }

    private void BroadcastResource(ResourceType type)
    {
        ResourceChanged?.Invoke(GetSnapshot(type));
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FlushPendingSave();
        EnsureDebugProxy();
        ScheduleRestoreSceneState(scene);
    }

    private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        FlushPendingSave();
        EnsureDebugProxy();
    }

    private void ScheduleRestoreSceneState(Scene scene)
    {
        if (!loaded || !scene.IsValid() || !scene.isLoaded)
            return;

        if (restoreSceneRoutine != null)
            StopCoroutine(restoreSceneRoutine);

        restoreSceneRoutine = StartCoroutine(RestoreSceneStateDeferred(scene));
    }

    private System.Collections.IEnumerator RestoreSceneStateDeferred(Scene scene)
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        if (scene.IsValid() && scene.isLoaded)
            RestoreSceneState(scene);

        restoreSceneRoutine = null;
    }

    private void EnsureDebugProxy()
    {
        if (!Application.isPlaying)
            return;

        if (debugProxy == null)
            debugProxy = FindFirstObjectByType<VillageManagementDebugProxy>(FindObjectsInactive.Include);

        if (debugProxy == null)
        {
            GameObject proxyObject = new GameObject("VillageManagement Debug");
            debugProxy = proxyObject.AddComponent<VillageManagementDebugProxy>();
        }

        debugProxy.Bind(this);
    }

    private void RestoreSceneState(Scene scene)
    {
        if (!loaded || !scene.IsValid() || !scene.isLoaded)
            return;

        restoreInProgress = true;
        try
        {
            PrepareSceneForRestore();

            ShopPlaceholderUI[] shopSections = FindObjectsByType<ShopPlaceholderUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < shopSections.Length; i++)
            {
                if (shopSections[i] != null)
                    shopSections[i].PrepareRuntimeRestore();
            }

            LiftSpot[] liftSpots = FindObjectsByType<LiftSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < liftSpots.Length; i++)
            {
                if (liftSpots[i] != null)
                    liftSpots[i].PrepareRuntimeRestore();
            }

            RestoreBuildingStates();
            RestoreTurretStates(shopSections);
            RestoreOxygenGeneratorStates(shopSections);
            RestoreLiftStates(liftSpots);
            SaveDataChanged?.Invoke(saveData);
        }
        finally
        {
            restoreInProgress = false;
        }
    }

    private void PrepareSceneForRestore()
    {
        if (saveData.buildings != null && saveData.buildings.Count > 0)
        {
            Path[] paths = FindObjectsByType<Path>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] != null)
                    paths[i].PrepareForRestore();
            }
        }

        if (saveData.turrets != null && saveData.turrets.Count > 0)
        {
            TurretImplementation[] turretSlots = FindObjectsByType<TurretImplementation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < turretSlots.Length; i++)
            {
                if (turretSlots[i] != null)
                    turretSlots[i].PrepareForRestore();
            }
        }
    }

    private void RestoreBuildingStates()
    {
        BuildingListUI[] buildingCatalogs = FindObjectsByType<BuildingListUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (buildingCatalogs == null || buildingCatalogs.Length == 0)
            return;

        Dictionary<string, Building> prefabById = new Dictionary<string, Building>(StringComparer.Ordinal);
        Dictionary<string, SpecialBuilding> specialPrefabById = new Dictionary<string, SpecialBuilding>(StringComparer.Ordinal);
        for (int i = 0; i < buildingCatalogs.Length; i++)
        {
            BuildingListUI catalog = buildingCatalogs[i];
            if (catalog == null)
                continue;

            for (int stateIndex = 0; stateIndex < saveData.buildings.Count; stateIndex++)
            {
                BuildingState state = saveData.buildings[stateIndex];
                if (state == null || string.IsNullOrWhiteSpace(state.buildingId) || prefabById.ContainsKey(state.buildingId))
                    continue;

                Building prefab = catalog.ResolveBuildingPrefab(state.buildingId);
                if (prefab != null)
                    prefabById.Add(state.buildingId, prefab);
            }

            for (int stateIndex = 0; stateIndex < saveData.buildings.Count; stateIndex++)
            {
                BuildingState state = saveData.buildings[stateIndex];
                if (state == null ||
                    !string.Equals(state.buildingType, "special", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(state.buildingId) ||
                    specialPrefabById.ContainsKey(state.buildingId))
                {
                    continue;
                }

                SpecialBuilding prefab = catalog.ResolveSpecialBuildingPrefab(state.buildingId);
                if (prefab != null)
                    specialPrefabById.Add(state.buildingId, prefab);
            }
        }

        Path[] paths = FindObjectsByType<Path>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Dictionary<string, Path> pathById = new Dictionary<string, Path>(StringComparer.Ordinal);
        for (int i = 0; i < paths.Length; i++)
        {
            Path path = paths[i];
            if (path == null || string.IsNullOrWhiteSpace(path.PathId))
                continue;

            string normalizedPathId = NormalizeBuildingSlotId(path.PathId);
            if (!pathById.ContainsKey(normalizedPathId))
                pathById.Add(normalizedPathId, path);
        }

        for (int i = 0; i < saveData.buildings.Count; i++)
        {
            BuildingState state = saveData.buildings[i];
            TryRestoreBuildingState(state, pathById, prefabById, specialPrefabById);
        }

        // Reconcile once more in case a swap or restore-order edge case left a path empty.
        for (int i = 0; i < saveData.buildings.Count; i++)
        {
            BuildingState state = saveData.buildings[i];
            if (state == null ||
                string.IsNullOrWhiteSpace(state.slotId) ||
                !pathById.TryGetValue(NormalizeBuildingSlotId(state.slotId), out Path path) ||
                path == null)
            {
                continue;
            }

            Building placedBuilding = path.Building;
            if (placedBuilding != null &&
                string.Equals(placedBuilding.BuildingId, state.buildingId, StringComparison.Ordinal))
            {
                continue;
            }

            TryRestoreBuildingState(state, pathById, prefabById, specialPrefabById);
        }
    }

    private bool TryRestoreBuildingState(
        BuildingState state,
        Dictionary<string, Path> pathById,
        Dictionary<string, Building> prefabById,
        Dictionary<string, SpecialBuilding> specialPrefabById)
    {
        if (state == null ||
            string.IsNullOrWhiteSpace(state.slotId) ||
            string.IsNullOrWhiteSpace(state.buildingId) ||
            !pathById.TryGetValue(NormalizeBuildingSlotId(state.slotId), out Path path) ||
            path == null)
        {
            return false;
        }

        if (string.Equals(state.buildingType, "special", StringComparison.Ordinal))
        {
            if (!specialPrefabById.TryGetValue(state.buildingId, out SpecialBuilding specialPrefab) || specialPrefab == null)
                return false;

            return path.RestoreSpecialFromState(state, specialPrefab);
        }

        if (!prefabById.TryGetValue(state.buildingId, out Building prefab) || prefab == null)
            return false;

        return path.RestoreFromState(state, prefab);
    }

    private void RestoreTurretStates(ShopPlaceholderUI[] shopSections)
    {
        if (shopSections == null || shopSections.Length == 0)
            return;

        for (int i = 0; i < saveData.turrets.Count; i++)
        {
            TurretState state = saveData.turrets[i];
            if (state == null)
                continue;

            for (int shopIndex = 0; shopIndex < shopSections.Length; shopIndex++)
            {
                ShopPlaceholderUI shopSection = shopSections[shopIndex];
                if (shopSection != null && shopSection.TryRestoreTurretState(state))
                    break;
            }
        }
    }

    private void RestoreOxygenGeneratorStates(ShopPlaceholderUI[] shopSections)
    {
        if (shopSections == null || shopSections.Length == 0)
            return;

        for (int i = 0; i < saveData.oxygenGenerators.Count; i++)
        {
            OxygenGeneratorState state = saveData.oxygenGenerators[i];
            if (state == null)
                continue;

            for (int shopIndex = 0; shopIndex < shopSections.Length; shopIndex++)
            {
                ShopPlaceholderUI shopSection = shopSections[shopIndex];
                if (shopSection != null && shopSection.TryRestoreOxygenGeneratorState(state))
                    break;
            }
        }
    }

    private void RestoreLiftStates(LiftSpot[] liftSpots)
    {
        if (liftSpots == null || liftSpots.Length == 0 || saveData.lifts == null)
            return;

        for (int i = 0; i < saveData.lifts.Count; i++)
        {
            LiftState state = saveData.lifts[i];
            if (state == null)
                continue;

            for (int spotIndex = 0; spotIndex < liftSpots.Length; spotIndex++)
            {
                LiftSpot liftSpot = liftSpots[spotIndex];
                if (liftSpot != null && liftSpot.TryRestoreLiftState(state))
                    break;
            }
        }
    }

    private void SaveAndBroadcast(SaveMode mode)
    {
        if (mode == SaveMode.Immediate)
            FlushPendingSave();
        else
            QueueDelayedSave();

        SaveDataChanged?.Invoke(saveData);
    }

    private void QueueDelayedSave()
    {
        if (!autoSaveOnChange)
            return;

        hasPendingDelayedSave = true;
        nextDelayedSaveAt = Time.unscaledTime + Mathf.Max(0.25f, delayedSaveIntervalSeconds);
    }

    private void ProcessDelayedSave()
    {
        if (!hasPendingDelayedSave || !autoSaveOnChange)
            return;

        if (Time.unscaledTime < nextDelayedSaveAt)
            return;

        FlushPendingSave();
    }

    private void FlushPendingSave()
    {
        if (!autoSaveOnChange)
        {
            hasPendingDelayedSave = false;
            return;
        }

        if (hasPendingDelayedSave)
            hasPendingDelayedSave = false;

        Save();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            FlushPendingSave();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            FlushPendingSave();
    }

    private void OnApplicationQuit()
    {
        FlushPendingSave();
    }

    private static SaveMode GetBuildingSaveMode(BuildingState existing, BuildingState next)
    {
        if (existing == null)
            return SaveMode.Immediate;

        bool structuralChanged =
            !string.Equals(existing.buildingId, next.buildingId, StringComparison.Ordinal) ||
            existing.level != next.level ||
            existing.isPlaced != next.isPlaced ||
            existing.underConstruction != next.underConstruction;

        return structuralChanged ? SaveMode.Immediate : SaveMode.Delayed;
    }

    private static string NormalizeBuildingSlotId(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
            return string.Empty;

        string trimmed = slotId.Trim();
        int pathsIndex = trimmed.IndexOf("paths[", StringComparison.OrdinalIgnoreCase);
        if (pathsIndex >= 0)
        {
            string normalized = trimmed.Substring(pathsIndex);
            int segmentEnd = normalized.IndexOf('/');
            if (segmentEnd < 0)
                return "paths";

            return "paths" + normalized.Substring(segmentEnd);
        }

        int plainPathsIndex = trimmed.IndexOf("paths/", StringComparison.OrdinalIgnoreCase);
        if (plainPathsIndex >= 0)
            return trimmed.Substring(plainPathsIndex);

        return trimmed;
    }

    private static SaveMode GetTurretSaveMode(TurretState existing, TurretState next)
    {
        if (existing == null)
            return SaveMode.Immediate;

        bool structuralChanged =
            !string.Equals(existing.turretId, next.turretId, StringComparison.Ordinal) ||
            existing.level != next.level ||
            existing.isPlaced != next.isPlaced;

        return structuralChanged ? SaveMode.Immediate : SaveMode.Delayed;
    }

    private static SaveMode GetOxygenGeneratorSaveMode(OxygenGeneratorState existing, OxygenGeneratorState next)
    {
        if (existing == null)
            return SaveMode.Immediate;

        bool structuralChanged =
            !string.Equals(existing.oxygenId, next.oxygenId, StringComparison.Ordinal) ||
            existing.level != next.level ||
            existing.isPlaced != next.isPlaced;

        return structuralChanged ? SaveMode.Immediate : SaveMode.Delayed;
    }

    private void SanitizeSaveData()
    {
        if (saveData == null)
            saveData = new VillageSaveData();

        saveData.oxygenCapacity = Mathf.Max(0, saveData.oxygenCapacity);
        saveData.energyCapacity = Mathf.Max(0, saveData.energyCapacity);
        saveData.bankLevel = Mathf.Clamp(saveData.bankLevel, 1, 3);
        saveData.selectedArcadeSceneIndex = Mathf.Max(0, saveData.selectedArcadeSceneIndex);
        saveData.selectedArcadePlayerType = saveData.selectedArcadePlayerType <= 0
            ? -1
            : Mathf.Clamp(saveData.selectedArcadePlayerType, 1, 5);
        saveData.currentOxygen = Mathf.Clamp(saveData.currentOxygen, 0, saveData.oxygenCapacity);
        saveData.currentEnergy = Mathf.Clamp(saveData.currentEnergy, 0, saveData.energyCapacity);
        saveData.emergencyDifficulty = Mathf.Clamp(saveData.emergencyDifficulty, 1, 26);

        if (saveData.buildings == null)
            saveData.buildings = new List<BuildingState>();
        if (saveData.turrets == null)
            saveData.turrets = new List<TurretState>();
        if (saveData.oxygenGenerators == null)
            saveData.oxygenGenerators = new List<OxygenGeneratorState>();
        if (saveData.lifts == null)
            saveData.lifts = new List<LiftState>();
        if (saveData.turretPurchaseLevels == null)
            saveData.turretPurchaseLevels = new List<PurchaseLevelState>();
        if (saveData.oxygenPurchaseLevels == null)
            saveData.oxygenPurchaseLevels = new List<PurchaseLevelState>();
        if (saveData.ownedBuildingIds == null)
            saveData.ownedBuildingIds = new List<string>();
        if (saveData.ownedTurretIds == null)
            saveData.ownedTurretIds = new List<string>();
        if (saveData.ownedOxygenIds == null)
            saveData.ownedOxygenIds = new List<string>();
        if (saveData.ownedCustomerBloodIds == null)
            saveData.ownedCustomerBloodIds = new List<string>();
        if (saveData.ownedWhiteBloodCellIds == null)
            saveData.ownedWhiteBloodCellIds = new List<string>();
        if (arcadePlayers == null)
            arcadePlayers = new List<ArcadePlayerEntry>();

        DeduplicateBuildingStates();

        for (int i = 0; i < saveData.buildings.Count; i++)
        {
            BuildingState state = saveData.buildings[i];
            if (state == null)
                continue;

            if (string.IsNullOrWhiteSpace(state.buildingType))
                state.buildingType = "normal";
            state.maxSalary = Mathf.Max(0, state.maxSalary);
            state.currentSalary = Mathf.Clamp(state.currentSalary, 0, state.maxSalary);
            state.level = Mathf.Max(0, state.level);
            state.constructionRemainingSeconds = Mathf.Max(0f, state.constructionRemainingSeconds);
        }

        for (int i = 0; i < saveData.turrets.Count; i++)
        {
            TurretState state = saveData.turrets[i];
            if (state == null)
                continue;

            state.maxAmmo = Mathf.Max(0, state.maxAmmo);
            state.currentAmmo = Mathf.Clamp(state.currentAmmo, 0, state.maxAmmo);
            state.level = Mathf.Max(0, state.level);
        }

        for (int i = 0; i < saveData.oxygenGenerators.Count; i++)
        {
            OxygenGeneratorState state = saveData.oxygenGenerators[i];
            if (state == null)
                continue;

            state.level = Mathf.Max(0, state.level);
            state.storedOxygen = Mathf.Max(0, state.storedOxygen);
        }

        for (int i = 0; i < saveData.lifts.Count; i++)
        {
            LiftState state = saveData.lifts[i];
            if (state == null)
                continue;

            state.liftId = string.IsNullOrWhiteSpace(state.liftId) ? string.Empty : state.liftId.Trim();
        }

        RemoveInvalidStrings(saveData.ownedBuildingIds);
        RemoveInvalidStrings(saveData.ownedTurretIds);
        RemoveInvalidStrings(saveData.ownedOxygenIds);
        RemoveInvalidStrings(saveData.ownedCustomerBloodIds);
        RemoveInvalidStrings(saveData.ownedWhiteBloodCellIds);
        SanitizePurchaseLevels(saveData.turretPurchaseLevels);
        SanitizePurchaseLevels(saveData.oxygenPurchaseLevels);
        SanitizeArcadePlayers();
    }

    private void DeduplicateBuildingStates()
    {
        if (saveData.buildings == null || saveData.buildings.Count <= 1)
            return;

        Dictionary<string, BuildingState> latestBySlot = new Dictionary<string, BuildingState>(StringComparer.Ordinal);
        List<string> order = new List<string>();

        for (int i = 0; i < saveData.buildings.Count; i++)
        {
            BuildingState state = saveData.buildings[i];
            if (state == null)
                continue;

            string normalizedSlotId = NormalizeBuildingSlotId(state.slotId);
            if (string.IsNullOrWhiteSpace(normalizedSlotId))
                continue;

            state.slotId = normalizedSlotId;
            if (!latestBySlot.ContainsKey(normalizedSlotId))
                order.Add(normalizedSlotId);

            latestBySlot[normalizedSlotId] = state;
        }

        List<BuildingState> deduplicated = new List<BuildingState>(latestBySlot.Count);
        for (int i = 0; i < order.Count; i++)
        {
            string slotId = order[i];
            if (latestBySlot.TryGetValue(slotId, out BuildingState state) && state != null)
                deduplicated.Add(state);
        }

        saveData.buildings = deduplicated;
    }

    private void ProcessDebugControls()
    {
        if (!Application.isPlaying || debugControls == null)
            return;

        if (debugControls.applyResourceValues)
        {
            debugControls.applyResourceValues = false;
            SetOxygenCapacity(debugControls.oxygenCapacity);
            SetCurrentOxygen(debugControls.currentOxygen);
            SetEnergyCapacity(debugControls.energyCapacity);
            SetCurrentEnergy(debugControls.currentEnergy);
        }

        if (debugControls.fillAllBuildingSalary)
        {
            debugControls.fillAllBuildingSalary = false;
            for (int i = 0; i < saveData.buildings.Count; i++)
            {
                BuildingState state = saveData.buildings[i];
                if (state == null)
                    continue;

                state.currentSalary = state.maxSalary;
                state.isWorking = state.maxSalary > 0;
            }
            SaveAndBroadcast(SaveMode.Delayed);
        }

        if (debugControls.emptyAllBuildingSalary)
        {
            debugControls.emptyAllBuildingSalary = false;
            for (int i = 0; i < saveData.buildings.Count; i++)
            {
                BuildingState state = saveData.buildings[i];
                if (state == null)
                    continue;

                state.currentSalary = 0;
                state.isWorking = false;
            }
            SaveAndBroadcast(SaveMode.Delayed);
        }

        if (debugControls.fillAllTurretAmmo)
        {
            debugControls.fillAllTurretAmmo = false;
            for (int i = 0; i < saveData.turrets.Count; i++)
            {
                TurretState state = saveData.turrets[i];
                if (state == null)
                    continue;

                state.currentAmmo = state.maxAmmo;
            }
            SaveAndBroadcast(SaveMode.Delayed);
        }

        if (debugControls.emptyAllTurretAmmo)
        {
            debugControls.emptyAllTurretAmmo = false;
            for (int i = 0; i < saveData.turrets.Count; i++)
            {
                TurretState state = saveData.turrets[i];
                if (state == null)
                    continue;

                state.currentAmmo = 0;
            }
            SaveAndBroadcast(SaveMode.Delayed);
        }

        if (debugControls.fillEnergyToMax)
        {
            debugControls.fillEnergyToMax = false;
            SetCurrentEnergy(saveData.energyCapacity);
        }

        if (debugControls.emptyEnergy)
        {
            debugControls.emptyEnergy = false;
            SetCurrentEnergy(0);
        }

        if (debugControls.triggerEmergency)
        {
            debugControls.triggerEmergency = false;
            SetEmergencyDifficulty(debugControls.emergencyDifficulty);
        }

        if (debugControls.refreshBloodDebugStates)
        {
            debugControls.refreshBloodDebugStates = false;
            RebuildBloodDebugStates(debugControls.activeCustomerBloods, saveData.ownedCustomerBloodIds);
            RebuildBloodDebugStates(debugControls.activeWhiteBloodCells, saveData.ownedWhiteBloodCellIds);
        }
    }

    private static void RebuildBloodDebugStates(List<VillageManagementTestControls.BloodDebugState> target, List<string> source)
    {
        target.Clear();

        for (int i = 0; i < source.Count; i++)
        {
            string id = source[i];
            if (string.IsNullOrWhiteSpace(id))
                continue;

            target.Add(new VillageManagementTestControls.BloodDebugState
            {
                id = id,
                active = true
            });
        }
    }

    private BuildingState FindBuildingState(string slotId, string buildingType = null)
    {
        string normalizedSlotId = NormalizeBuildingSlotId(slotId);
        for (int i = 0; i < saveData.buildings.Count; i++)
        {
            BuildingState item = saveData.buildings[i];
            if (item == null || NormalizeBuildingSlotId(item.slotId) != normalizedSlotId)
                continue;

            if (!string.IsNullOrWhiteSpace(buildingType) &&
                !string.Equals(item.buildingType, buildingType, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(buildingType) ||
                string.Equals(item.buildingType, buildingType, StringComparison.Ordinal))
                return item;
        }

        return null;
    }

    private TurretState FindTurretState(string slotId)
    {
        for (int i = 0; i < saveData.turrets.Count; i++)
        {
            TurretState item = saveData.turrets[i];
            if (item != null && item.slotId == slotId)
                return item;
        }

        return null;
    }

    private OxygenGeneratorState FindOxygenGeneratorState(string slotId)
    {
        for (int i = 0; i < saveData.oxygenGenerators.Count; i++)
        {
            OxygenGeneratorState item = saveData.oxygenGenerators[i];
            if (item != null && item.slotId == slotId)
                return item;
        }

        return null;
    }

    private LiftState FindLiftState(string liftId)
    {
        for (int i = 0; i < saveData.lifts.Count; i++)
        {
            LiftState item = saveData.lifts[i];
            if (item != null && string.Equals(item.liftId, liftId, StringComparison.Ordinal))
                return item;
        }

        return null;
    }

    private ArcadePlayerEntry FindArcadePlayerEntry(int playerType)
    {
        playerType = Mathf.Clamp(playerType, 1, 5);
        for (int i = 0; i < arcadePlayers.Count; i++)
        {
            ArcadePlayerEntry entry = arcadePlayers[i];
            if (entry != null && entry.playerType == playerType)
                return entry;
        }

        return null;
    }

    private static BuildingState CloneBuildingState(BuildingState source)
    {
        return new BuildingState
        {
            slotId = NormalizeBuildingSlotId(source.slotId),
            buildingId = source.buildingId,
            buildingType = string.IsNullOrWhiteSpace(source.buildingType) ? "normal" : source.buildingType,
            level = source.level,
            currentSalary = source.currentSalary,
            maxSalary = source.maxSalary,
            isPlaced = source.isPlaced,
            isWorking = source.isWorking,
            underConstruction = source.underConstruction,
            constructionRemainingSeconds = source.constructionRemainingSeconds
        };
    }

    private static void CopyBuildingState(BuildingState source, BuildingState target)
    {
        target.slotId = NormalizeBuildingSlotId(source.slotId);
        target.buildingId = source.buildingId;
        target.buildingType = string.IsNullOrWhiteSpace(source.buildingType) ? "normal" : source.buildingType;
        target.level = source.level;
        target.currentSalary = source.currentSalary;
        target.maxSalary = source.maxSalary;
        target.isPlaced = source.isPlaced;
        target.isWorking = source.isWorking;
        target.underConstruction = source.underConstruction;
        target.constructionRemainingSeconds = source.constructionRemainingSeconds;
    }

    private static TurretState CloneTurretState(TurretState source)
    {
        return new TurretState
        {
            slotId = source.slotId,
            turretId = source.turretId,
            level = source.level,
            currentAmmo = source.currentAmmo,
            maxAmmo = source.maxAmmo,
            isPlaced = source.isPlaced
        };
    }

    private static void CopyTurretState(TurretState source, TurretState target)
    {
        target.slotId = source.slotId;
        target.turretId = source.turretId;
        target.level = source.level;
        target.currentAmmo = source.currentAmmo;
        target.maxAmmo = source.maxAmmo;
        target.isPlaced = source.isPlaced;
    }

    private static OxygenGeneratorState CloneOxygenGeneratorState(OxygenGeneratorState source)
    {
        return new OxygenGeneratorState
        {
            slotId = source.slotId,
            oxygenId = source.oxygenId,
            purchaseEntryId = source.purchaseEntryId,
            level = source.level,
            isPlaced = source.isPlaced,
            isProducing = source.isProducing,
            storedOxygen = source.storedOxygen
        };
    }

    private static LiftState CloneLiftState(LiftState source)
    {
        return new LiftState
        {
            liftId = source.liftId,
            isActive = source.isActive
        };
    }

    private static void CopyOxygenGeneratorState(OxygenGeneratorState source, OxygenGeneratorState target)
    {
        target.slotId = source.slotId;
        target.oxygenId = source.oxygenId;
        target.purchaseEntryId = source.purchaseEntryId;
        target.level = source.level;
        target.isPlaced = source.isPlaced;
        target.isProducing = source.isProducing;
        target.storedOxygen = source.storedOxygen;
    }

    private static void CopyLiftState(LiftState source, LiftState target)
    {
        target.liftId = source.liftId;
        target.isActive = source.isActive;
    }

    private void AddUniqueString(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || list.Contains(value))
            return;

        list.Add(value);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    private void RemoveString(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (list.Remove(value))
            SaveAndBroadcast(SaveMode.Immediate);
    }

    private static void ReplaceStringList(List<string> target, IEnumerable<string> source)
    {
        target.Clear();
        if (source == null)
            return;

        foreach (string value in source)
        {
            if (string.IsNullOrWhiteSpace(value) || target.Contains(value))
                continue;

            target.Add(value);
        }
    }

    private static int GetPurchaseLevel(List<PurchaseLevelState> states, string id)
    {
        if (states == null || string.IsNullOrWhiteSpace(id))
            return 0;

        for (int i = 0; i < states.Count; i++)
        {
            PurchaseLevelState state = states[i];
            if (state != null && state.id == id)
                return Mathf.Max(0, state.level);
        }

        return 0;
    }

    private static void SetPurchaseLevel(List<PurchaseLevelState> states, string id, int level)
    {
        if (states == null || string.IsNullOrWhiteSpace(id))
            return;

        level = Mathf.Max(0, level);
        for (int i = 0; i < states.Count; i++)
        {
            PurchaseLevelState state = states[i];
            if (state == null || state.id != id)
                continue;

            state.level = Mathf.Max(state.level, level);
            return;
        }

        states.Add(new PurchaseLevelState
        {
            id = id,
            level = level
        });
    }

    private static void SanitizePurchaseLevels(List<PurchaseLevelState> states)
    {
        if (states == null)
            return;

        for (int i = states.Count - 1; i >= 0; i--)
        {
            PurchaseLevelState state = states[i];
            if (state == null || string.IsNullOrWhiteSpace(state.id))
            {
                states.RemoveAt(i);
                continue;
            }

            state.level = Mathf.Max(0, state.level);
        }
    }

    private static void RemoveInvalidStrings(List<string> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(list[i]))
                list.RemoveAt(i);
        }
    }

    private void SanitizeArcadePlayers()
    {
        if (arcadePlayers == null)
            arcadePlayers = new List<ArcadePlayerEntry>();

        HashSet<int> usedPlayerTypes = new HashSet<int>();
        for (int i = arcadePlayers.Count - 1; i >= 0; i--)
        {
            ArcadePlayerEntry entry = arcadePlayers[i];
            if (entry == null)
            {
                arcadePlayers.RemoveAt(i);
                continue;
            }

            entry.playerType = Mathf.Clamp(entry.playerType, 1, 5);

            if (!usedPlayerTypes.Add(entry.playerType))
                arcadePlayers.RemoveAt(i);
        }

        for (int playerType = 1; playerType <= 5; playerType++)
        {
            if (usedPlayerTypes.Contains(playerType))
                continue;

            arcadePlayers.Add(new ArcadePlayerEntry
            {
                playerType = playerType,
                available = false
            });
        }

        arcadePlayers.Sort((a, b) => a.playerType.CompareTo(b.playerType));
    }
}
