using System;
using UnityEngine;

public partial class VillageManagement
{
    public const int MaxVillageLevel = 9;

    [Serializable]
    public class VillageLevelRequirements
    {
        [Min(1)] public int level1To2 = 100;
        [Min(1)] public int level2To3 = 200;
        [Min(1)] public int level3To4 = 300;
        [Min(1)] public int level4To5 = 400;
        [Min(1)] public int level5To6 = 500;
        [Min(1)] public int level6To7 = 600;
        [Min(1)] public int level7To8 = 700;
        [Min(1)] public int level8To9 = 800;

        public int GetRequiredScore(int level)
        {
            switch (level)
            {
                case 1: return Mathf.Max(1, level1To2);
                case 2: return Mathf.Max(1, level2To3);
                case 3: return Mathf.Max(1, level3To4);
                case 4: return Mathf.Max(1, level4To5);
                case 5: return Mathf.Max(1, level5To6);
                case 6: return Mathf.Max(1, level6To7);
                case 7: return Mathf.Max(1, level7To8);
                case 8: return Mathf.Max(1, level8To9);
                default: return 0;
            }
        }
    }

    [Header("Village Score / Level (1 → 9)")]
    [SerializeField] private VillageLevelRequirements villageLevelRequirements = new VillageLevelRequirements();
    [SerializeField, Range(1, MaxVillageLevel), Tooltip("저장 파일이 없거나 마을 진행을 초기화했을 때 적용되는 시작 레벨")]
    private int initialVillageLevel = 1;
    [SerializeField, Tooltip("새 저장 또는 초기화 시 적용되는 시작 점수. 게이지 총량 이상이면 레벨업합니다.")]
    private long initialVillageScore;

    public int VillageLevel => saveData.villageLevel;
    public long VillageScore => saveData.villageScore;
    public bool IsMaxVillageLevel => VillageLevel >= MaxVillageLevel;
    public int ScoreToNextVillageLevel => villageLevelRequirements.GetRequiredScore(VillageLevel);

    public bool IsVillageLevelUnlocked(int requiredLevel) => VillageLevel >= Mathf.Clamp(requiredLevel, 1, MaxVillageLevel);

    public void SetVillageProgress(int level, long score)
    {
        int previousLevel = VillageLevel;
        saveData.villageLevel = level;
        saveData.villageScore = score;
        NormalizeVillageProgress(saveData);
        saveData.emergencyDifficulty = VillageLevel;
        if (VillageLevel != previousLevel)
            EmergencyDifficultyChanged?.Invoke(EmergencyDifficulty);
        SaveAndBroadcast(SaveMode.Immediate);
    }

    private VillageSaveData CreateInitialSaveData()
    {
        var data = new VillageSaveData { villageLevel = initialVillageLevel, villageScore = initialVillageScore };
        NormalizeVillageProgress(data);
        data.emergencyDifficulty = data.villageLevel;
        return data;
    }

    private void NormalizeVillageProgress(VillageSaveData data)
    {
        data.villageLevel = Mathf.Clamp(data.villageLevel, 1, MaxVillageLevel);
        data.villageScore = Math.Max(0L, data.villageScore);
        while (data.villageLevel < MaxVillageLevel)
        {
            int required = villageLevelRequirements.GetRequiredScore(data.villageLevel);
            if (data.villageScore < required) break;
            data.villageScore -= required;
            data.villageLevel++;
        }
    }

    public void AddVillageScore(int amount)
    {
        if (amount <= 0) return;
        saveData.villageScore = saveData.villageScore > long.MaxValue - amount
            ? long.MaxValue : saveData.villageScore + amount;
        int previousLevel = VillageLevel;
        NormalizeVillageProgress(saveData);

        if (VillageLevel != previousLevel)
        {
            saveData.emergencyDifficulty = VillageLevel;
            EmergencyDifficultyChanged?.Invoke(EmergencyDifficulty);
        }
        SaveAndBroadcast(VillageLevel != previousLevel ? SaveMode.Immediate : SaveMode.Delayed);
    }

    public void RemoveVillageScore(int amount)
    {
        if (amount <= 0 || VillageScore == 0) return;
        saveData.villageScore = Math.Max(0L, VillageScore - amount);
        SaveAndBroadcast(SaveMode.Delayed);
    }

    public bool HasPurchasedPlayer(int playerType) => saveData.purchasedPlayerTypes.Contains(playerType);

    public bool TryPurchasePlayer(int playerType)
    {
        ArcadePlayerEntry entry = FindArcadePlayerEntry(playerType);
        if (entry == null || !entry.available || !IsVillageLevelUnlocked(playerType) || HasPurchasedPlayer(playerType))
            return false;
        if (CurrentOxygen < Mathf.Max(0, entry.purchaseOxygenCost)) return false;
        // Commit ownership before broadcasting resource changes so the button immediately refreshes to owned.
        saveData.purchasedPlayerTypes.Add(playerType);
        TrySpendOxygen(Mathf.Max(0, entry.purchaseOxygenCost));
        SaveAndBroadcast(SaveMode.Immediate);
        return true;
    }

    public static int GetBuildingUnlockLevel(string buildingId)
    {
        string id = (buildingId ?? string.Empty).ToLowerInvariant();
        if (id.Contains("fruit")) return 1;
        if (id.Contains("fish") || id.Contains("sushi")) return 2;
        if (id.Contains("pharmacy") || id.Contains("medicine")) return 3;
        if (id.Contains("chicken")) return 4;
        if (id.Contains("icecream")) return 5;
        if (id.Contains("bread")) return 6;
        if (id.Contains("coffee")) return 7;
        return 1;
    }

    public static int GetSpecialBuildingUnlockLevel(string buildingId)
    {
        string id = (buildingId ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        if (id.Contains("hotspring")) return 3;
        if (id.Contains("hotel")) return 6;
        return 8;
    }

    public static int GetOilUnlockLevel(int catalogIndex)
    {
        switch (catalogIndex)
        {
            case 0: return 1;
            case 1: return 2;
            case 2: return 4;
            default: return 6;
        }
    }
}
