using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-500)]
public class VillageManagement : MonoBehaviour
{
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
        public int level;
        public bool isPlaced;
        public bool isProducing;
        public int storedOxygen;
    }

    [Serializable]
    public class VillageSaveData
    {
        public int bankLevel = 1;
        public string selectedWhiteBloodCellId = string.Empty;
        public int currentOxygen = 0;
        public int oxygenCapacity = 100;
        public int currentEnergy = 0;
        public int energyCapacity = 100;
        public int emergencyDifficulty = 1;
        public List<BuildingState> buildings = new List<BuildingState>();
        public List<TurretState> turrets = new List<TurretState>();
        public List<OxygenGeneratorState> oxygenGenerators = new List<OxygenGeneratorState>();
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

    public static VillageManagement Instance { get; private set; }
    public static event Action<VillageManagement> InstanceReady;

    public event Action<ResourceSnapshot> ResourceChanged;
    public event Action<VillageSaveData> SaveDataChanged;
    public event Action<int> EmergencyDifficultyChanged;

    [Header("Persistence")]
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool autoSaveOnChange = true;
    [SerializeField] private string saveFileName = "village_save.json";

    [Header("Arcade Reward Mapping")]
    [SerializeField] private bool awardArcadeOxygenToCurrentOxygen = true;
    [SerializeField] private bool awardArcadeEnergyToCurrentEnergy = true;

    [Header("Debug / Test")]
    [SerializeField] private VillageManagementTestControls debugControls = new VillageManagementTestControls();

    private VillageSaveData saveData = new VillageSaveData();
    private bool loaded;

    public VillageSaveData SaveData => saveData;
    public VillageManagementTestControls DebugControls => debugControls;

    public int CurrentOxygen => saveData.currentOxygen;
    public int OxygenCapacity => saveData.oxygenCapacity;
    public int CurrentEnergy => saveData.currentEnergy;
    public int EnergyCapacity => saveData.energyCapacity;
    public int EmergencyDifficulty => saveData.emergencyDifficulty;
    public int BankLevel => saveData.bankLevel;
    public string SelectedWhiteBloodCellId => saveData.selectedWhiteBloodCellId;

    public IReadOnlyList<BuildingState> Buildings => saveData.buildings;
    public IReadOnlyList<TurretState> Turrets => saveData.turrets;
    public IReadOnlyList<OxygenGeneratorState> OxygenGenerators => saveData.oxygenGenerators;
    public IReadOnlyList<string> OwnedBuildingIds => saveData.ownedBuildingIds;
    public IReadOnlyList<string> OwnedTurretIds => saveData.ownedTurretIds;
    public IReadOnlyList<string> OwnedOxygenIds => saveData.ownedOxygenIds;
    public IReadOnlyList<string> OwnedCustomerBloodIds => saveData.ownedCustomerBloodIds;
    public IReadOnlyList<string> OwnedWhiteBloodCellIds => saveData.ownedWhiteBloodCellIds;

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

        Load();
        InstanceReady?.Invoke(this);
    }

    private void Update()
    {
        ProcessDebugControls();
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

    public void Save()
    {
        try
        {
            SanitizeSaveData();
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
        Save();
        NotifyAllResourceSnapshots();
        SaveDataChanged?.Invoke(saveData);
        EmergencyDifficultyChanged?.Invoke(saveData.emergencyDifficulty);
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

        SaveAndBroadcast();
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
        SaveAndBroadcast();
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
        SaveAndBroadcast();
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
        SaveAndBroadcast();
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
        SaveAndBroadcast();
    }

    public void SetEmergencyDifficulty(int difficulty)
    {
        int clamped = Mathf.Clamp(difficulty, 1, 26);
        if (saveData.emergencyDifficulty == clamped)
            return;

        saveData.emergencyDifficulty = clamped;
        EmergencyDifficultyChanged?.Invoke(saveData.emergencyDifficulty);
        SaveAndBroadcast();
    }

    public void SetBankLevel(int level)
    {
        int clamped = Mathf.Clamp(level, 1, 3);
        if (saveData.bankLevel == clamped)
            return;

        saveData.bankLevel = clamped;
        SaveAndBroadcast();
    }

    public void SetSelectedWhiteBloodCell(string id)
    {
        string next = string.IsNullOrWhiteSpace(id) ? string.Empty : id;
        if (saveData.selectedWhiteBloodCellId == next)
            return;

        saveData.selectedWhiteBloodCellId = next;
        SaveAndBroadcast();
    }

    public void UpsertBuildingState(BuildingState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.slotId))
            return;

        BuildingState existing = FindBuildingState(state.slotId);
        if (existing == null)
        {
            saveData.buildings.Add(CloneBuildingState(state));
        }
        else
        {
            CopyBuildingState(state, existing);
        }

        SaveAndBroadcast();
    }

    public void RemoveBuildingState(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || saveData.buildings == null)
            return;

        for (int i = saveData.buildings.Count - 1; i >= 0; i--)
        {
            BuildingState state = saveData.buildings[i];
            if (state != null && state.slotId == slotId)
            {
                saveData.buildings.RemoveAt(i);
                SaveAndBroadcast();
                return;
            }
        }
    }

    public void UpsertTurretState(TurretState state)
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

        SaveAndBroadcast();
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
                SaveAndBroadcast();
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

        SaveAndBroadcast();
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
                SaveAndBroadcast();
                return;
            }
        }
    }

    public void SetOwnedCustomerBloodIds(IEnumerable<string> ids)
    {
        ReplaceStringList(saveData.ownedCustomerBloodIds, ids);
        SaveAndBroadcast();
    }

    public void SetOwnedWhiteBloodCellIds(IEnumerable<string> ids)
    {
        ReplaceStringList(saveData.ownedWhiteBloodCellIds, ids);
        SaveAndBroadcast();
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

    private void SaveAndBroadcast()
    {
        if (autoSaveOnChange)
            Save();

        SaveDataChanged?.Invoke(saveData);
    }

    private void SanitizeSaveData()
    {
        if (saveData == null)
            saveData = new VillageSaveData();

        saveData.oxygenCapacity = Mathf.Max(0, saveData.oxygenCapacity);
        saveData.energyCapacity = Mathf.Max(0, saveData.energyCapacity);
        saveData.bankLevel = Mathf.Clamp(saveData.bankLevel, 1, 3);
        saveData.currentOxygen = Mathf.Clamp(saveData.currentOxygen, 0, saveData.oxygenCapacity);
        saveData.currentEnergy = Mathf.Clamp(saveData.currentEnergy, 0, saveData.energyCapacity);
        saveData.emergencyDifficulty = Mathf.Clamp(saveData.emergencyDifficulty, 1, 26);

        if (saveData.buildings == null)
            saveData.buildings = new List<BuildingState>();
        if (saveData.turrets == null)
            saveData.turrets = new List<TurretState>();
        if (saveData.oxygenGenerators == null)
            saveData.oxygenGenerators = new List<OxygenGeneratorState>();
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

        for (int i = 0; i < saveData.buildings.Count; i++)
        {
            BuildingState state = saveData.buildings[i];
            if (state == null)
                continue;

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

        RemoveInvalidStrings(saveData.ownedBuildingIds);
        RemoveInvalidStrings(saveData.ownedTurretIds);
        RemoveInvalidStrings(saveData.ownedOxygenIds);
        RemoveInvalidStrings(saveData.ownedCustomerBloodIds);
        RemoveInvalidStrings(saveData.ownedWhiteBloodCellIds);
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
            SaveAndBroadcast();
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
            SaveAndBroadcast();
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
            SaveAndBroadcast();
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
            SaveAndBroadcast();
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

    private BuildingState FindBuildingState(string slotId)
    {
        for (int i = 0; i < saveData.buildings.Count; i++)
        {
            BuildingState item = saveData.buildings[i];
            if (item != null && item.slotId == slotId)
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

    private static BuildingState CloneBuildingState(BuildingState source)
    {
        return new BuildingState
        {
            slotId = source.slotId,
            buildingId = source.buildingId,
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
        target.slotId = source.slotId;
        target.buildingId = source.buildingId;
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
            level = source.level,
            isPlaced = source.isPlaced,
            isProducing = source.isProducing,
            storedOxygen = source.storedOxygen
        };
    }

    private static void CopyOxygenGeneratorState(OxygenGeneratorState source, OxygenGeneratorState target)
    {
        target.slotId = source.slotId;
        target.oxygenId = source.oxygenId;
        target.level = source.level;
        target.isPlaced = source.isPlaced;
        target.isProducing = source.isProducing;
        target.storedOxygen = source.storedOxygen;
    }

    private void AddUniqueString(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || list.Contains(value))
            return;

        list.Add(value);
        SaveAndBroadcast();
    }

    private void RemoveString(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (list.Remove(value))
            SaveAndBroadcast();
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

    private static void RemoveInvalidStrings(List<string> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(list[i]))
                list.RemoveAt(i);
        }
    }
}
