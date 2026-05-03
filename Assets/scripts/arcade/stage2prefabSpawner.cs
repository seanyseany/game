using System.Collections.Generic;
using UnityEngine;

public class stage2prefabSpawner : MonoBehaviour
{
    [Header("Prefabs (5)")]
    public GameObject[] prefabs = new GameObject[5];
    public string[] poolTags = new string[5];
    public int poolSizePerPrefab = 4;

    [Header("Spawn Timing")]
    public bool playOnEnable = false;
    public float minSpawnInterval = 2f;
    public float maxSpawnInterval = 3f;

    [Header("World Positions")]
    public Vector3 spawnWorldPos = new Vector3(20f, -3.8f, 4f);
    public Vector3 endWorldPos = new Vector3(-20f, -3.8f, 4f);

    [Header("Movement")]
    public float moveSpeed = 6f;

    private readonly List<int> spawnBag = new List<int>(8);
    private readonly List<SpawnedItem> activeItems = new List<SpawnedItem>(32);

    private bool isSpawning;
    private bool spawnRequested;
    private bool pausedForBossSlime;
    private float nextSpawnTime;
    private bool poolsReady;

    private struct SpawnedItem
    {
        public GameObject gameObject;
        public string poolTag;
    }

    private void Awake()
    {
        ValidatePoolTags();
    }

    private void Start()
    {
        EnsurePoolsReady();

        if (playOnEnable)
            BeginSpawn();
    }

    private void OnEnable()
    {
        GameData.OnGameOver += HandleGameOver;

        if (playOnEnable && Application.isPlaying)
            BeginSpawn();
    }

    private void OnDisable()
    {
        GameData.OnGameOver -= HandleGameOver;
        StopSpawn();
    }

    private void Update()
    {
        UpdateActiveMovement();

        if (!isSpawning)
            return;

        if (Time.time >= nextSpawnTime)
        {
            SpawnOne();
            ScheduleNextSpawn();
        }
    }

    public void BeginSpawn()
    {
        EnsurePoolsReady();
        spawnRequested = true;
        RefreshSpawnState(forceImmediateSpawn: false);
        if (nextSpawnTime <= Time.time)
            ScheduleNextSpawn();
    }

    public void StopSpawn()
    {
        spawnRequested = false;
        isSpawning = false;
    }

    public void SetBossSlimePaused(bool paused)
    {
        pausedForBossSlime = paused;
        RefreshSpawnState(forceImmediateSpawn: !paused);
    }

    public void ClearActive()
    {
        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            ReturnOrDisable(activeItems[i].gameObject, activeItems[i].poolTag);
        }

        activeItems.Clear();
    }

    public void ResetSpawner()
    {
        StopSpawn();
        pausedForBossSlime = false;
        nextSpawnTime = 0f;
        spawnBag.Clear();
        ClearActive();
    }

    private void SpawnOne()
    {
        int prefabIndex = GetNextPrefabIndex();
        if (prefabIndex < 0)
            return;

        GameObject prefab = prefabs[prefabIndex];
        string poolTag = poolTags[prefabIndex];

        GameObject spawned = null;
        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            spawned = ObjectPool.Instance.SpawnFromPool(poolTag, spawnWorldPos, Quaternion.identity);

        if (spawned == null && prefab != null)
        {
            spawned = Instantiate(prefab, spawnWorldPos, Quaternion.identity);
            if (!string.IsNullOrEmpty(poolTag))
                spawned.tag = poolTag;
        }

        if (spawned == null)
            return;

        activeItems.Add(new SpawnedItem
        {
            gameObject = spawned,
            poolTag = poolTag
        });
    }

    private void UpdateActiveMovement()
    {
        float mult = GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;
        float speed = Mathf.Max(0.01f, moveSpeed) * mult;
        Vector3 target = endWorldPos;

        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            GameObject go = activeItems[i].gameObject;
            if (go == null)
            {
                activeItems.RemoveAt(i);
                continue;
            }

            go.transform.position = Vector3.MoveTowards(go.transform.position, target, speed * Time.deltaTime);

            if (go.transform.position.x <= endWorldPos.x)
            {
                ReturnOrDisable(go, activeItems[i].poolTag);
                activeItems.RemoveAt(i);
            }
        }
    }

    private void ReturnOrDisable(GameObject go, string poolTag)
    {
        if (go == null)
            return;

        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
        {
            ObjectPool.Instance.ReturnToPool(poolTag, go);
            return;
        }

        go.SetActive(false);
    }

    private int GetNextPrefabIndex()
    {
        RefillSpawnBagIfNeeded();
        if (spawnBag.Count == 0)
            return -1;

        int lastIndex = spawnBag.Count - 1;
        int prefabIndex = spawnBag[lastIndex];
        spawnBag.RemoveAt(lastIndex);
        return prefabIndex;
    }

    private void RefillSpawnBagIfNeeded()
    {
        if (spawnBag.Count > 0)
            return;

        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] != null)
                spawnBag.Add(i);
        }

        for (int i = 0; i < spawnBag.Count; i++)
        {
            int swapIndex = Random.Range(i, spawnBag.Count);
            int temp = spawnBag[i];
            spawnBag[i] = spawnBag[swapIndex];
            spawnBag[swapIndex] = temp;
        }
    }

    private void ScheduleNextSpawn()
    {
        float min = Mathf.Min(minSpawnInterval, maxSpawnInterval);
        float max = Mathf.Max(minSpawnInterval, maxSpawnInterval);
        float delay = Random.value < 0.5f ? min : max;
        nextSpawnTime = Time.time + delay;
    }

    private void EnsurePoolsReady()
    {
        if (poolsReady)
            return;

        ValidatePoolTags();

        if (ObjectPool.Instance != null)
        {
            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject prefab = prefabs[i];
                string poolTag = poolTags[i];
                if (prefab == null || string.IsNullOrEmpty(poolTag))
                    continue;

                if (!ObjectPool.Instance.HasPool(poolTag))
                    ObjectPool.Instance.RegisterPool(poolTag, prefab, Mathf.Max(1, poolSizePerPrefab));
            }
        }

        poolsReady = true;
    }

    private void RefreshSpawnState(bool forceImmediateSpawn)
    {
        bool shouldSpawn = spawnRequested && !pausedForBossSlime;
        if (!shouldSpawn)
        {
            isSpawning = false;
            return;
        }

        isSpawning = true;
        if (forceImmediateSpawn)
            nextSpawnTime = Time.time;
    }

    private void HandleGameOver()
    {
        StopSpawn();
    }

    private void ValidatePoolTags()
    {
        if (prefabs == null)
            prefabs = new GameObject[0];

        if (poolTags == null || poolTags.Length != prefabs.Length)
            System.Array.Resize(ref poolTags, prefabs.Length);

        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] == null)
                continue;

            if (string.IsNullOrWhiteSpace(poolTags[i]))
                poolTags[i] = prefabs[i].name + "_Stage2Pool_" + i;
        }
    }
}
