using System.Collections.Generic;
using UnityEngine;

public class VillanPath : MonoBehaviour
{
    [Header("Path")]
    [SerializeField] private Transform villanEntrance;
    [SerializeField] private List<Transform> movePoints = new List<Transform>();
    [SerializeField] private List<Transform> flipPoints = new List<Transform>();

    public Transform VillanEntrance => villanEntrance != null ? villanEntrance : transform;
    public IReadOnlyList<Transform> MovePoints => movePoints;

    public Vector3 EntranceWorldPosition => VillanEntrance.position;

    public bool IsFlipPoint(Transform point)
    {
        return point != null && flipPoints.Contains(point);
    }

    public Vector3 GetMovePointPosition(int index)
    {
        if (index < 0 || index >= movePoints.Count || movePoints[index] == null)
            return transform.position;

        return movePoints[index].position;
    }
}
