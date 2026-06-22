using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Emergency : MonoBehaviour
{
    [System.Serializable]
    public class DifficultySpawn
    {
        [Range(1, 10)] public int villanLevel = 1;
        public int count = 1;
    }

    [System.Serializable]
    public class DifficultyEntry
    {
        [Range(1, 26)] public int difficulty = 1;
        public List<DifficultySpawn> spawns = new List<DifficultySpawn>();
    }

    [SerializeField] private int difficulty = 1;
    [SerializeField] private Villan villanPrefab;
    [SerializeField] private VillanPath villanPath;
    [SerializeField] private List<DifficultyEntry> difficultyEntries = new List<DifficultyEntry>();
    [SerializeField] private GameObject emergencyWarning;
    [SerializeField] private float[] timelineOptions = { 40f, 60f, 110f, 130f, 160f };

    private readonly List<int> remainingTimelineIndices = new List<int>();
    private float elapsedInVillage;
    private float nextEmergencyAt = -1f;

    private void OnEnable()
    {
        VillageManagement.InstanceReady += HandleVillageReady;
        if (VillageManagement.Instance != null)
            VillageManagement.Instance.SaveDataChanged += HandleSaveDataChanged;
    }

    private void OnDisable()
    {
        VillageManagement.InstanceReady -= HandleVillageReady;
        if (VillageManagement.Instance != null)
            VillageManagement.Instance.SaveDataChanged -= HandleSaveDataChanged;
    }

    private void Start()
    {
        ResetTimelinePoolIfNeeded();
        ChooseNextEmergencyTime();
        HandleVillageReady(VillageManagement.Instance);
    }

    private void Update()
    {
        if (!IsVillageSceneActive())
            return;

        elapsedInVillage += Time.deltaTime;
        if (nextEmergencyAt > 0f && elapsedInVillage >= nextEmergencyAt)
        {
            StartCoroutine(EmergencyRoutine());
            nextEmergencyAt = -1f;
        }
    }

    private void HandleVillageReady(VillageManagement villageManagement)
    {
        if (villageManagement == null)
            return;

        villageManagement.SaveDataChanged -= HandleSaveDataChanged;
        villageManagement.SaveDataChanged += HandleSaveDataChanged;

        difficulty = CalculateDynamicDifficulty(villageManagement);
        villageManagement.SetEmergencyDifficulty(difficulty);
    }

    private void HandleSaveDataChanged(VillageManagement.VillageSaveData _)
    {
        if (VillageManagement.Instance == null)
            return;

        difficulty = CalculateDynamicDifficulty(VillageManagement.Instance);
        VillageManagement.Instance.SetEmergencyDifficulty(difficulty);
    }

    private IEnumerator EmergencyRoutine()
    {
        if (emergencyWarning != null)
            emergencyWarning.SetActive(true);

        yield return new WaitForSeconds(3f);

        if (emergencyWarning != null)
            emergencyWarning.SetActive(false);

        DifficultyEntry entry = GetDifficultyEntry(difficulty);
        if (entry != null)
            yield return SpawnWave(entry);

        elapsedInVillage = 0f;
        ResetTimelinePoolIfNeeded();
        ChooseNextEmergencyTime();
    }

    private IEnumerator SpawnWave(DifficultyEntry entry)
    {
        List<DifficultySpawn> expanded = new List<DifficultySpawn>();
        for (int i = 0; i < entry.spawns.Count; i++)
        {
            DifficultySpawn spawn = entry.spawns[i];
            for (int c = 0; c < Mathf.Max(0, spawn.count); c++)
                expanded.Add(spawn);
        }

        int total = expanded.Count;
        if (total == 0 || villanPrefab == null || villanPath == null)
            yield break;

        int firstCount = Mathf.RoundToInt(total * 0.2f);
        int secondCount = Mathf.RoundToInt(total * 0.3f);
        int thirdCount = Mathf.Max(0, total - firstCount - secondCount);

        yield return SpawnChunk(expanded, 0, firstCount, 6f);
        yield return SpawnChunk(expanded, firstCount, secondCount, 7f);
        yield return SpawnChunk(expanded, firstCount + secondCount, thirdCount, 7f);
    }

    private IEnumerator SpawnChunk(List<DifficultySpawn> expanded, int startIndex, int count, float duration)
    {
        if (count <= 0)
            yield break;

        float interval = duration / count;
        for (int i = 0; i < count; i++)
        {
            DifficultySpawn spawn = expanded[startIndex + i];
            Villan villan = Instantiate(villanPrefab);
            villan.Initialize(villanPath, spawn.villanLevel);
            yield return new WaitForSeconds(interval);
        }
    }

    private DifficultyEntry GetDifficultyEntry(int targetDifficulty)
    {
        for (int i = 0; i < difficultyEntries.Count; i++)
        {
            if (difficultyEntries[i] != null && difficultyEntries[i].difficulty == targetDifficulty)
                return difficultyEntries[i];
        }

        return difficultyEntries.Count > 0 ? difficultyEntries[difficultyEntries.Count - 1] : null;
    }

    private int CalculateDynamicDifficulty(VillageManagement villageManagement)
    {
        int result = 1;

        for (int i = 0; i < villageManagement.Buildings.Count; i++)
        {
            VillageManagement.BuildingState state = villageManagement.Buildings[i];
            if (state != null && state.isPlaced)
                result += Mathf.Max(0, state.level);
        }

        for (int i = 0; i < villageManagement.OxygenGenerators.Count; i++)
        {
            VillageManagement.OxygenGeneratorState state = villageManagement.OxygenGenerators[i];
            if (state != null && state.isPlaced)
                result += Mathf.Max(0, state.level);
        }

        for (int i = 0; i < villageManagement.Turrets.Count; i++)
        {
            VillageManagement.TurretState state = villageManagement.Turrets[i];
            if (state != null && state.isPlaced)
                result += Mathf.Max(0, state.level);
        }

        return Mathf.Clamp(result, 1, 26);
    }

    private void ResetTimelinePoolIfNeeded()
    {
        if (remainingTimelineIndices.Count > 0)
            return;

        remainingTimelineIndices.Clear();
        for (int i = 0; i < timelineOptions.Length; i++)
            remainingTimelineIndices.Add(i);
    }

    private void ChooseNextEmergencyTime()
    {
        if (remainingTimelineIndices.Count == 0)
            return;

        int pick = Random.Range(0, remainingTimelineIndices.Count);
        int timelineIndex = remainingTimelineIndices[pick];
        remainingTimelineIndices.RemoveAt(pick);
        nextEmergencyAt = timelineOptions[timelineIndex];
    }

    private bool IsVillageSceneActive()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.IsValid() && scene.name == "Village";
    }
}
