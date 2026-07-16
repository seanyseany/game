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
    [Tooltip("마지막 스폰에서만 강제로 사용할 장애물 풀 태그")]
    public string lastMachineGunObstaclePoolTag;

    [Header("Cholesterol Bomb Settings")]
    [Tooltip("머신건 트리거 후 지연 스폰할 Cholesterol Bomb 프리팹")]
    [SerializeField] private GameObject cholesterolBombPrefab;
    [Tooltip("머신건 트리거 후 각 시간마다 spawn position 중 한 곳에서 Cholesterol Bomb를 1개씩 스폰")]
    [SerializeField] private float[] cholesterolBombSpawnDelays;

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
        if (activeRuntime == null)
            return;

        activeRuntime.RequestStopSpawning();
    }

    public bool IsSpawnSequenceResolved()
    {
        return activeRuntime == null || activeRuntime.IsSpawnSequenceResolved;
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
            machineGunSpawnDuration = Mathf.Max(0f, machineGunSpawnEndLeadTime),
            machineGunObstaclesPerPoint = machineGunObstaclesPerPoint,
            spawnPositions = spawnPositions,
            cholesterolBombPrefab = cholesterolBombPrefab,
            cholesterolBombSpawnDelays = cholesterolBombSpawnDelays != null
                ? (float[])cholesterolBombSpawnDelays.Clone()
                : System.Array.Empty<float>(),
            poolTags66 = poolTags66,
            poolTags33 = poolTags33
        };
    }

    private void RegisterKnownPoolTags()
    {
        AddPoolTags(machineGunObstaclePoolTags66);
        AddPoolTags(machineGunObstaclePoolTags33);
        AddPoolTags(new[] { lastMachineGunObstaclePoolTag });
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
    private const float AllObstaclesClearedStopDelay = 1f;

    internal struct Config
    {
        public string ownerName;
        public float machineGunSpawnStartDelay;
        public float machineGunSpawnInterval;
        public float machineGunSpawnDuration;
        public int machineGunObstaclesPerPoint;
        public Vector3[] spawnPositions;
        public GameObject cholesterolBombPrefab;
        public float[] cholesterolBombSpawnDelays;
        public string[] poolTags66;
        public string[] poolTags33;
    }

    private Coroutine spawnRoutine;
    private Coroutine cholesterolBombRoutine;
    private Coroutine delayedStopRoutine;
    private Config config;
    private readonly HashSet<MachineGunLastSpawnNotifier> trackedObstacles = new HashSet<MachineGunLastSpawnNotifier>();
    private bool spawnStopRequested;
    private bool obstacleSpawnStopNotified;

    internal bool IsSpawnSequenceResolved => obstacleSpawnStopNotified;

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

        if (config.cholesterolBombPrefab != null &&
            config.cholesterolBombSpawnDelays != null &&
            config.cholesterolBombSpawnDelays.Length > 0)
        {
            cholesterolBombRoutine = StartCoroutine(CoSpawnCholesterolBombs());
        }
    }

    internal void StopSession()
    {
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }

        if (cholesterolBombRoutine != null)
        {
            StopCoroutine(cholesterolBombRoutine);
            cholesterolBombRoutine = null;
        }

        if (delayedStopRoutine != null)
        {
            StopCoroutine(delayedStopRoutine);
            delayedStopRoutine = null;
        }

        ClearTrackedObstacleBindings();

        Destroy(gameObject);
    }

    internal void RequestStopSpawning()
    {
        if (spawnStopRequested)
            return;

        spawnStopRequested = true;

        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }

        if (cholesterolBombRoutine != null)
        {
            StopCoroutine(cholesterolBombRoutine);
            cholesterolBombRoutine = null;
        }

        EvaluateStopCompletion();
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

        float elapsed = 0f;
        bool spawnedAtLeastOnce = false;

        while (!spawnStopRequested && (config.machineGunSpawnDuration <= 0f ? !spawnedAtLeastOnce : elapsed <= config.machineGunSpawnDuration))
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

            spawnedAtLeastOnce = true;

            float interval = Mathf.Max(0f, config.machineGunSpawnInterval);
            if (interval > 0f)
            {
                yield return WaitForSecondsRespectingGameplayPause(interval);
                elapsed += interval;
            }
            else
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        spawnRoutine = null;
        RequestStopSpawning();
    }

    private IEnumerator CoSpawnCholesterolBombs()
    {
        if (config.spawnPositions == null || config.spawnPositions.Length == 0)
        {
            cholesterolBombRoutine = null;
            yield break;
        }

        float previousDelay = 0f;
        for (int i = 0; i < config.cholesterolBombSpawnDelays.Length; i++)
        {
            float targetDelay = Mathf.Max(0f, config.cholesterolBombSpawnDelays[i]);
            float delay = Mathf.Max(0f, targetDelay - previousDelay);
            if (delay > 0f)
                yield return WaitForSecondsRespectingGameplayPause(delay);

            if (IsGameplayTransformPaused())
            {
                while (IsGameplayTransformPaused())
                    yield return null;
            }

            SpawnCholesterolBombAtRandomPosition();
            previousDelay = Mathf.Max(previousDelay, targetDelay);
        }

        cholesterolBombRoutine = null;
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

        TrackSpawnedObstacle(spawned);

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

    private void SpawnCholesterolBombAtRandomPosition()
    {
        if (config.cholesterolBombPrefab == null || config.spawnPositions == null || config.spawnPositions.Length == 0)
            return;

        Vector3 spawnPos = config.spawnPositions[Random.Range(0, config.spawnPositions.Length)];
        GameObject spawned = Instantiate(config.cholesterolBombPrefab, spawnPos, Quaternion.identity);
        TrackSpawnedObstacle(spawned);
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

    private void TrackSpawnedObstacle(GameObject spawned)
    {
        if (spawned == null)
            return;

        MachineGunLastSpawnNotifier notifier = spawned.GetComponent<MachineGunLastSpawnNotifier>();
        if (notifier == null)
            notifier = spawned.AddComponent<MachineGunLastSpawnNotifier>();

        notifier.Bind(HandleTrackedObstacleDestroyTriggered, HandleTrackedObstacleDisabled);
        trackedObstacles.Add(notifier);
    }

    private void ClearTrackedObstacleBindings()
    {
        foreach (MachineGunLastSpawnNotifier notifier in trackedObstacles)
        {
            if (notifier != null)
                notifier.ClearBinding();
        }

        trackedObstacles.Clear();
    }

    private void HandleTrackedObstacleDestroyTriggered(MachineGunLastSpawnNotifier notifier)
    {
        if (notifier != null)
            trackedObstacles.Remove(notifier);

        EvaluateStopCompletion();
    }

    private void HandleTrackedObstacleDisabled(MachineGunLastSpawnNotifier notifier)
    {
        if (notifier != null)
            trackedObstacles.Remove(notifier);

        if (!spawnStopRequested)
            return;

        EvaluateStopCompletion();
    }

    private void EvaluateStopCompletion()
    {
        PruneInactiveTrackedObstacles();

        if (trackedObstacles.Count > 0)
            return;

        if (delayedStopRoutine != null)
            return;

        delayedStopRoutine = StartCoroutine(CoNotifyObstacleSpawnStopDelayed());
    }

    private void PruneInactiveTrackedObstacles()
    {
        trackedObstacles.RemoveWhere(static notifier =>
            notifier == null ||
            !notifier.gameObject.activeInHierarchy);
    }

    private void NotifyObstacleSpawnStopIfNeeded()
    {
        if (obstacleSpawnStopNotified)
            return;

        obstacleSpawnStopNotified = true;
        delayedStopRoutine = null;
        GameData.Instance?.NotifyMachineGunObstacleSpawnStop();
        ClearTrackedObstacleBindings();
    }

    private IEnumerator CoNotifyObstacleSpawnStopDelayed()
    {
        if (AllObstaclesClearedStopDelay > 0f)
            yield return WaitForSecondsRespectingGameplayPause(AllObstaclesClearedStopDelay);

        delayedStopRoutine = null;
        PruneInactiveTrackedObstacles();

        if (!obstacleSpawnStopNotified && trackedObstacles.Count == 0)
            NotifyObstacleSpawnStopIfNeeded();
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
