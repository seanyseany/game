using UnityEngine;

[System.Serializable]
public class FixedSpawnEntry
{
    public GameObject prefab;              // 스폰할 프리팹
    [Tooltip("이 프리팹이 스폰될 수 있는 스폰 포인트 인덱스 (예: 0=1층, 4=5층)")]
    public int[] allowedSpawnIndices;      // 스폰 허용 인덱스들
}
