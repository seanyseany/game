using UnityEngine;
using System.Collections;

public class BloodSpawner : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject bloodPrefab;
    public Vector2 spawnPos = new Vector2(10f, -2f);  // 스폰 위치
    public float minSpawnInterval = 3f;
    public float maxSpawnInterval = 7f;
    private ObjectPool preparedPool;
    private GameObject preparedPrefab;
    private string poolTag = "Blood";

    private void Start()
    {
        PreparePool();
        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        while (true)
        {
            float delay = Random.Range(minSpawnInterval, maxSpawnInterval);
            yield return new WaitForSeconds(delay);

            if (GameData.Instance != null && GameData.Instance.gameOver)
                continue;

            SpawnOne();
        }
    }

    private void PreparePool()
    {
        ObjectPool pool = ObjectPool.Instance;
        if (pool == null || bloodPrefab == null || (pool == preparedPool && bloodPrefab == preparedPrefab)) return;
        GameObject registered = pool.GetRegisteredPrefab("Blood");
        poolTag = registered == null || registered == bloodPrefab ? "Blood" : "Blood_" + bloodPrefab.GetInstanceID();
        pool.EnsurePoolSize(poolTag, bloodPrefab, 4);
        preparedPool = pool;
        preparedPrefab = bloodPrefab;
    }

    private void SpawnOne()
    {
        if (bloodPrefab == null) return;
        PreparePool();
        GameObject spawned = preparedPool != null ? preparedPool.SpawnFromPool(poolTag, spawnPos, Quaternion.identity) : null;
        if (spawned == null) spawned = Instantiate(bloodPrefab, spawnPos, Quaternion.identity);
        if (spawned.TryGetComponent<Blood>(out var blood)) blood.ConfigurePooling(poolTag);
    }
}
