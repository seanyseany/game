using UnityEngine;

public class LiftPlatform : MonoBehaviour
{
    [SerializeField] private Transform riderPoint;

    public Vector3 GetRiderLocalPosition()
    {
        if (riderPoint == null)
            return Vector3.zero;

        return transform.InverseTransformPoint(riderPoint.position);
    }
}
