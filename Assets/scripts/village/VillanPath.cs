using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class VillanPath : MonoBehaviour
{
    [SerializeField] private Vector2 entry1LocalPosition;
    [SerializeField] private float exit1LocalX = -6f;
    [SerializeField] private Vector2 entry2LocalPosition;

    public Vector3 Entry1World => transform.TransformPoint(entry1LocalPosition);
    public Vector3 Entry2World => transform.TransformPoint(entry2LocalPosition);
    public float Exit1WorldX => transform.TransformPoint(new Vector3(exit1LocalX, 0f, 0f)).x;
    public float Exit2WorldX => transform.TransformPoint(new Vector3(-exit1LocalX, 0f, 0f)).x;
}
