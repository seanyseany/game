using UnityEngine;
using System.Collections;

public class BloodSpawner : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject bloodPrefab;
    public Vector2 spawnPos = new Vector2(10f, -2f);  // 스폰 위치
    public float minSpawnInterval = 3f;
    public float maxSpawnInterval = 7f;

    private void Start()
    {
        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        while (true)
        {
            float delay = Random.Range(minSpawnInterval, maxSpawnInterval);
            yield return new WaitForSeconds(delay);
            Instantiate(bloodPrefab, spawnPos, Quaternion.identity);
        }
    }
}
