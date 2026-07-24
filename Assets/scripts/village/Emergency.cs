using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Emergency : MonoBehaviour
{
    [System.Serializable]
    public class EmergencyVillanEntry
    {
        public Villan villanPrefab;
        [Min(1)] public int count = 1;
    }

    [System.Serializable]
    public class EmergencyLevel
    {
        public bool enabled = true;
        public List<EmergencyVillanEntry> villans = new List<EmergencyVillanEntry>();
        public List<float> spawnIntervals = new List<float>();
    }

    [Header("Emergency")]
    [SerializeField] private List<float> emergencyTriggerTimes = new List<float> { 40f, 60f, 110f, 130f, 160f };
    [SerializeField] private GameObject emergencyWarning;

    [Header("Paths")]
    [SerializeField] private List<VillanPath> villanPaths = new List<VillanPath>();

    [Header("Levels")]
    [SerializeField] private List<EmergencyLevel> levels = new List<EmergencyLevel>();

    private float elapsedInVillage;
    private float nextEmergencyAt = -1f;
    private bool emergencyRunning;

    private void Start()
    {
        ChooseNextEmergencyTime();
    }

    private void Update()
    {
        if (!IsVillageSceneActive() || emergencyRunning)
            return;

        elapsedInVillage += Time.deltaTime;
        if (nextEmergencyAt > 0f && elapsedInVillage >= nextEmergencyAt)
        {
            StartCoroutine(EmergencyRoutine());
            nextEmergencyAt = -1f;
        }
    }

    private IEnumerator EmergencyRoutine()
    {
        emergencyRunning = true;

        if (emergencyWarning != null)
            emergencyWarning.SetActive(true);

        yield return new WaitForSeconds(3f);

        if (emergencyWarning != null)
            emergencyWarning.SetActive(false);

        for (int i = 0; i < levels.Count; i++)
        {
            EmergencyLevel level = levels[i];
            if (level == null || !level.enabled)
                continue;

            yield return StartCoroutine(SpawnLevel(level));
        }

        ChooseNextEmergencyTime();
        emergencyRunning = false;
    }

    private IEnumerator SpawnLevel(EmergencyLevel level)
    {
        if (level == null || level.villans.Count == 0)
            yield break;

        VillanPath path = GetRandomPath();
        if (path == null)
            yield break;

        for (int i = 0; i < level.villans.Count; i++)
        {
            EmergencyVillanEntry entry = level.villans[i];
            if (entry == null || entry.villanPrefab == null || entry.count <= 0)
                continue;

            for (int spawnIndex = 0; spawnIndex < entry.count; spawnIndex++)
            {
                Villan.Spawn(entry.villanPrefab, path, 1);
                float interval = GetRandomSpawnInterval(level.spawnIntervals);
                yield return new WaitForSeconds(interval);
            }
        }
    }

    private VillanPath GetRandomPath()
    {
        List<VillanPath> availablePaths = new List<VillanPath>();
        for (int i = 0; i < villanPaths.Count; i++)
        {
            if (villanPaths[i] != null)
                availablePaths.Add(villanPaths[i]);
        }

        if (availablePaths.Count == 0)
            return null;

        return availablePaths[Random.Range(0, availablePaths.Count)];
    }

    private float GetRandomSpawnInterval(List<float> intervals)
    {
        List<float> validIntervals = new List<float>();
        for (int i = 0; i < intervals.Count; i++)
        {
            if (intervals[i] > 0f)
                validIntervals.Add(intervals[i]);
        }

        if (validIntervals.Count == 0)
            return 1f;

        return validIntervals[Random.Range(0, validIntervals.Count)];
    }

    private void ChooseNextEmergencyTime()
    {
        List<float> validTimes = new List<float>();
        for (int i = 0; i < emergencyTriggerTimes.Count; i++)
        {
            if (emergencyTriggerTimes[i] > 0f)
                validTimes.Add(emergencyTriggerTimes[i]);
        }

        if (validTimes.Count == 0)
            return;

        float delay = validTimes[Random.Range(0, validTimes.Count)];
        nextEmergencyAt = elapsedInVillage + delay;
    }

    private bool IsVillageSceneActive()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.IsValid() && scene.name == "Village";
    }
}
