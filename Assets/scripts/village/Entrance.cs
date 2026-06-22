using UnityEngine;

public class Entrance : MonoBehaviour
{
    [SerializeField] private Vector2 spawnLocalPosition;
    [SerializeField] private Vector2 despawnLocalPosition;

    public Vector3 SpawnWorldPosition => transform.TransformPoint(spawnLocalPosition);
    public Vector3 DespawnWorldPosition => transform.TransformPoint(despawnLocalPosition);
}
