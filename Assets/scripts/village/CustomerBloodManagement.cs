using System.Collections.Generic;
using UnityEngine;

public class CustomerBloodManagement : MonoBehaviour
{
    [System.Serializable]
    public class BloodEntry
    {
        public string id;
        public CustomerBlood prefab;
        [Min(0)] public int count = 1;
    }

    [System.Serializable]
    public class SpecialBloodGroup
    {
        public List<BloodEntry> entries = new List<BloodEntry>();
    }

    [SerializeField] private List<BloodEntry> baseBloodEntries = new List<BloodEntry>();
    [SerializeField] private List<SpecialBloodGroup> specialBloodGroups = new List<SpecialBloodGroup>();

    private readonly Dictionary<string, int> activeCounts = new Dictionary<string, int>();

    public int GetMaxActiveCustomerCount()
    {
        int total = 0;
        List<BloodEntry> entries = GetAvailableEntries();
        for (int i = 0; i < entries.Count; i++)
            total += Mathf.Max(0, entries[i].count);

        return total;
    }

    public List<BloodEntry> GetAvailableEntries()
    {
        List<BloodEntry> result = new List<BloodEntry>();
        result.AddRange(baseBloodEntries);

        int unlockedSpecialGroupCount = GetUnlockedSpecialGroupCount();
        for (int i = 0; i < Mathf.Min(unlockedSpecialGroupCount, specialBloodGroups.Count); i++)
        {
            if (specialBloodGroups[i] == null || specialBloodGroups[i].entries == null)
                continue;

            result.AddRange(specialBloodGroups[i].entries);
        }

        return result;
    }

    public bool TryGetSpawnPrefab(out BloodEntry selectedEntry)
    {
        List<BloodEntry> spawnable = new List<BloodEntry>();
        List<BloodEntry> available = GetAvailableEntries();

        for (int i = 0; i < available.Count; i++)
        {
            BloodEntry entry = available[i];
            if (entry == null || entry.prefab == null || string.IsNullOrWhiteSpace(entry.id))
                continue;

            int active = GetActiveCount(entry.id);
            if (active < Mathf.Max(0, entry.count))
                spawnable.Add(entry);
        }

        if (spawnable.Count == 0)
        {
            selectedEntry = null;
            return false;
        }

        selectedEntry = spawnable[Random.Range(0, spawnable.Count)];
        return true;
    }

    public void RegisterSpawn(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return;

        activeCounts[entryId] = GetActiveCount(entryId) + 1;
    }

    public void RegisterDespawn(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return;

        int next = Mathf.Max(0, GetActiveCount(entryId) - 1);
        activeCounts[entryId] = next;
    }

    private int GetUnlockedSpecialGroupCount()
    {
        VillageManagement villageManagement = VillageManagement.Instance;
        if (villageManagement == null)
            return 0;

        int purchasedBuildingCount = 0;
        IReadOnlyList<VillageManagement.BuildingState> buildings = villageManagement.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            VillageManagement.BuildingState state = buildings[i];
            if (state != null)
                purchasedBuildingCount++;
        }

        return purchasedBuildingCount;
    }

    private int GetActiveCount(string entryId)
    {
        return activeCounts.TryGetValue(entryId, out int count) ? count : 0;
    }
}
