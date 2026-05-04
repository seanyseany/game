using UnityEngine;
using System.Collections.Generic;

public class BackgroundLaneSpawner : MonoBehaviour
{
    [Header("Prefabs (3개)")]
    public GameObject[] backgroundPrefabs; // 폭 28짜리 배경 3개

    [Header("Spawn/Despawn")]
    public float rightX = 28f;   // 스폰 위치 X
    public float leftX  = -28f;  // 파괴 기준 X
    public float pieceWidth = 28f;

    [Header("Motion")]
    public float scrollSpeed = 10f; // 왼쪽으로 이동 속도(px/sec)

    [Header("Layout")]
    public float yPosition = 0f;    // 배경 Y (네가 직접 조절)

    // 내부
    private float spawnInterval;       // 폭/속도 → 딱 맞게 이어짐
    private float nextSpawnTime;
    private List<int> bag = new List<int>();  // 셔플-백 (중복 방지)

    void Start()
    {
        if (backgroundPrefabs == null || backgroundPrefabs.Length == 0)
        {
            ;
            enabled = false;
            return;
        }

        spawnInterval = Mathf.Abs(pieceWidth / Mathf.Max(0.0001f, scrollSpeed));
        FillBagAndShuffle();

        nextSpawnTime = Time.time; // 시작 즉시 하나 스폰
    }

    void Update()
    {
        // 분노/속도 배수 반영 (원하면)
        float mult = (GameData.Instance != null) ? GameData.Instance.GetStageSpeedMult() : 1f;
        float intervalWithMult = spawnInterval / Mathf.Max(0.0001f, mult);

        if (Time.time >= nextSpawnTime)
        {
            SpawnOne();
            nextSpawnTime += intervalWithMult;
        }
    }

    private void SpawnOne()
    {
        // 셔플-백에서 하나 뽑기
        if (bag.Count == 0) FillBagAndShuffle();
        int prefabIndex = bag[bag.Count - 1];
        bag.RemoveAt(bag.Count - 1);

        var prefab = backgroundPrefabs[prefabIndex];
        var go = Instantiate(prefab);
        go.transform.position = new Vector3(rightX, yPosition, 0f);

        // 움직임 & 소멸 설정
        var piece = go.GetComponent<BackgroundPiece>();
        if (piece == null) piece = go.AddComponent<BackgroundPiece>();
        piece.Setup(scrollSpeed, leftX);
    }

    private void FillBagAndShuffle()
    {
        bag.Clear();
        for (int i = 0; i < backgroundPrefabs.Length; i++) bag.Add(i);

        // Fisher–Yates shuffle
        for (int i = 0; i < bag.Count; i++)
        {
            int j = Random.Range(i, bag.Count);
            int temp = bag[i];
            bag[i] = bag[j];
            bag[j] = temp;
        }
    }
}
