using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))]
public class PhaseDetector : MonoBehaviour
{
    private static readonly List<PhaseDetector> activeDetectors = new List<PhaseDetector>(16);
    private Collider2D cachedCollider;

    public static IReadOnlyList<PhaseDetector> ActiveDetectors => activeDetectors;
    public Collider2D CachedCollider => cachedCollider;

    private void Awake()
    {
        cachedCollider = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        if (cachedCollider == null)
        {
            cachedCollider = GetComponent<Collider2D>();
        }

        if (!activeDetectors.Contains(this))
        {
            activeDetectors.Add(this);
        }
    }

    private void OnDisable()
    {
        activeDetectors.Remove(this);
    }

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.simulated = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ;
    }
}
