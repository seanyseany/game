using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))]
public class ObstacleDetector : MonoBehaviour
{
    private static readonly List<obstacleStaticMove> trackedObstacles = new List<obstacleStaticMove>(64);
    private Collider2D triggerCollider;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider2D>();
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

    private void LateUpdate()
    {
        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<Collider2D>();
            if (triggerCollider == null)
            {
                return;
            }
        }

        Bounds detectorBounds = triggerCollider.bounds;
        for (int i = trackedObstacles.Count - 1; i >= 0; i--)
        {
            obstacleStaticMove obstacle = trackedObstacles[i];
            if (obstacle == null)
            {
                trackedObstacles.RemoveAt(i);
                continue;
            }

            obstacle.TryTriggerFromBounds(detectorBounds);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTrigger(other);
    }

    private void TryTrigger(Collider2D other)
    {
        if (other == null)
        {
            return;
        }

        obstacleStaticMove targetMove = other.GetComponentInParent<obstacleStaticMove>();
        if (targetMove == null)
        {
            return;
        }

        targetMove.TriggerMove();
    }

    public static void Register(obstacleStaticMove obstacle)
    {
        if (obstacle == null || trackedObstacles.Contains(obstacle))
        {
            return;
        }

        trackedObstacles.Add(obstacle);
    }

    public static void Unregister(obstacleStaticMove obstacle)
    {
        if (obstacle == null)
        {
            return;
        }

        trackedObstacles.Remove(obstacle);
    }
}
