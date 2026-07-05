using UnityEngine;

public class Entrance : MonoBehaviour
{
    [SerializeField] private Vector2 spawnLocalPosition;

    public Vector3 SpawnWorldPosition => transform.TransformPoint(spawnLocalPosition);
    public Vector3 DespawnWorldPosition => SpawnWorldPosition;
}
