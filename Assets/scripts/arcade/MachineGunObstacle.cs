using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class MachineGunObstacle : MonoBehaviour, IReinitializable
{
    [Header("MachineGun Obstacle Settings")]
    [Tooltip("machineGun 장애물이 생성될 월드 좌표 목록")]
    public Vector3[] machineGunSpawnWorldPositions;
    public float machineGunSpawnStartDelay = 2f;
    public float machineGunSpawnInterval = 0.77f;
    public float machineGunSpawnEndLeadTime = 1.5f;
    public int machineGunObstaclesPerPoint = 1;
    [Tooltip("66% 확률 그룹에서 랜덤 선택할 장애물 풀 태그 목록")]
    public string[] machineGunObstaclePoolTags66;
    [Tooltip("33% 확률 그룹에서 랜덤 선택할 장애물 풀 태그 목록")]
    public string[] machineGunObstaclePoolTags33;

    private static readonly HashSet<string> knownPoolTags = new HashSet<string>();
    private static MachineGunObstacle currentSettingsSource;
    private static MachineGunObstacleRuntime activeRuntime;

    private void OnEnable()
    {
        RegisterKnownPoolTags();
    }

    private void OnDisable()
    {
        if (currentSettingsSource == this)
            currentSettingsSource = null;
    }

    public void Reinit()
    {
        RegisterKnownPoolTags();
    }

    public static void SetCurrentSource(MachineGunObstacle source)
    {
        currentSettingsSource = source;
    }

    public static MachineGunObstacle CurrentSource => currentSettingsSource;

    public static float GetCurrentSpawnEndLeadTime(float fallback = 3.8f)
    {
        return currentSettingsSource != null
            ? Mathf.Max(0f, currentSettingsSource.machineGunSpawnEndLeadTime)
            : Mathf.Max(0f, fallback);
    }

    public static void ClearAllSpawnedObstacles()
    {
        StopActiveMachineGunSpawn();

        if (ObjectPool.Instance == null || knownPoolTags.Count == 0)
            return;

        string[] tags = new string[knownPoolTags.Count];
        knownPoolTags.CopyTo(tags);
        ObjectPool.Instance.ReturnAllActive(tags);
    }

    public void BeginMachineGunSpawn()
    {
        StartSpawnRuntime(CreateRuntimeConfig());
    }

    public void StopMachineGunSpawn()
    {
        StopActiveMachineGunSpawn();
    }

    public static void StopActiveMachineGunSpawn()
    {
        if (activeRuntime == null)
            return;

        activeRuntime.StopSession();
        activeRuntime = null;
    }

    private void StartSpawnRuntime(MachineGunObstacleRuntime.Config config)
    {
        StopActiveMachineGunSpawn();
        activeRuntime = MachineGunObstacleRuntime.Create(config);
    }

    private MachineGunObstacleRuntime.Config CreateRuntimeConfig()
    {
        Vector3[] spawnPositions = machineGunSpawnWorldPositions != null
            ? (Vector3[])machineGunSpawnWorldPositions.Clone()
            : System.Array.Empty<Vector3>();
        string[] poolTags66 = machineGunObstaclePoolTags66 != null
            ? (string[])machineGunObstaclePoolTags66.Clone()
            : System.Array.Empty<string>();
        string[] poolTags33 = machineGunObstaclePoolTags33 != null
            ? (string[])machineGunObstaclePoolTags33.Clone()
            : System.Array.Empty<string>();

        return new MachineGunObstacleRuntime.Config
        {
            ownerName = name,
            machineGunSpawnStartDelay = machineGunSpawnStartDelay,
            machineGunSpawnInterval = machineGunSpawnInterval,
            machineGunObstaclesPerPoint = machineGunObstaclesPerPoint,
            spawnPositions = spawnPositions,
            poolTags66 = poolTags66,
            poolTags33 = poolTags33
        };
    }

    private void RegisterKnownPoolTags()
    {
        AddPoolTags(machineGunObstaclePoolTags66);
        AddPoolTags(machineGunObstaclePoolTags33);
    }

    private static void AddPoolTags(string[] source)
    {
        if (source == null)
            return;

        for (int i = 0; i < source.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(source[i]))
                knownPoolTags.Add(source[i]);
        }
    }
}

internal sealed class MachineGunObstacleRuntime : MonoBehaviour
{
    internal struct Config
    {
        public string ownerName;
        public float machineGunSpawnStartDelay;
        public float machineGunSpawnInterval;
        public int machineGunObstaclesPerPoint;
        public Vector3[] spawnPositions;
        public string[] poolTags66;
        public string[] poolTags33;
    }

    private Coroutine spawnRoutine;
    private Config config;

    internal static MachineGunObstacleRuntime Create(Config config)
    {
        GameObject host = new GameObject("MachineGunObstacleRuntime");
        MachineGunObstacleRuntime runtime = host.AddComponent<MachineGunObstacleRuntime>();
        runtime.Begin(config);
        return runtime;
    }

    internal void Begin(Config runtimeConfig)
    {
        config = runtimeConfig;
        spawnRoutine = StartCoroutine(CoSpawnObstacles());
    }

    internal void StopSession()
    {
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }

        Destroy(gameObject);
    }

    private IEnumerator CoSpawnObstacles()
    {
        if (config.spawnPositions == null || config.spawnPositions.Length == 0)
        {
            Debug.LogWarning($"[MachineGunObstacle] {config.ownerName} has no spawn positions.");
            spawnRoutine = null;
            Destroy(gameObject);
            yield break;
        }

        float startDelay = Mathf.Max(0f, config.machineGunSpawnStartDelay);
        if (startDelay > 0f)
            yield return WaitForSecondsRespectingGameplayPause(startDelay);

        while (true)
        {
            if (IsGameplayTransformPaused())
            {
                yield return null;
                continue;
            }

            for (int p = 0; p < config.spawnPositions.Length; p++)
            {
                Vector3 spawnPos = config.spawnPositions[p];
                for (int i = 0; i < config.machineGunObstaclesPerPoint; i++)
                    SpawnObstacle(spawnPos);
            }

            float interval = Mathf.Max(0f, config.machineGunSpawnInterval);
            if (interval > 0f)
                yield return WaitForSecondsRespectingGameplayPause(interval);
            else
                yield return null;
        }
    }

    private void SpawnObstacle(Vector3 spawnPos)
    {
        string poolTag = GetRandomMachineGunObstaclePoolTag();
        if (string.IsNullOrWhiteSpace(poolTag))
        {
            Debug.LogWarning($"[MachineGunObstacle] {config.ownerName} has no obstacle pool tags.");
            return;
        }

        GameObject spawned = ObjectPool.Instance != null
            ? ObjectPool.Instance.SpawnFromPool(poolTag, spawnPos, Quaternion.identity)
            : null;

        if (spawned == null)
        {
            Debug.LogWarning($"[MachineGunObstacle] Pool '{poolTag}' is empty or missing.");
            return;
        }

        Rigidbody2D rb = spawned.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.WakeUp();
        }

        Mover mover = spawned.GetComponent<Mover>();
        if (mover != null && StageManager.Instance != null)
            mover.baseSpeed = StageManager.Instance.phaseBaseSpeed;
    }

    private string GetRandomMachineGunObstaclePoolTag()
    {
        bool usePrimaryList = Random.value < 0.66f;
        string tag = GetRandomPoolTagFromList(usePrimaryList ? config.poolTags66 : config.poolTags33);

        if (!string.IsNullOrWhiteSpace(tag))
            return tag;

        return GetRandomPoolTagFromList(usePrimaryList ? config.poolTags33 : config.poolTags66);
    }

    private static string GetRandomPoolTagFromList(string[] poolTags)
    {
        if (poolTags == null || poolTags.Length == 0)
            return null;

        return poolTags[Random.Range(0, poolTags.Length)];
    }

    private static bool IsGameplayTransformPaused()
    {
        return StageManager.Instance != null && StageManager.Instance.IsGameplayTransformPaused;
    }

    private static IEnumerator WaitForSecondsRespectingGameplayPause(float seconds)
    {
        float remaining = Mathf.Max(0f, seconds);
        while (remaining > 0f)
        {
            if (IsGameplayTransformPaused())
            {
                yield return null;
                continue;
            }

            remaining -= Time.deltaTime;
            yield return null;
        }
    }
}
