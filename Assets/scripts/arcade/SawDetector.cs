using UnityEngine;
using System.Collections.Generic;

public class SawDetector : MonoBehaviour
{
    public BombLauncher launcher; // 인스펙터에 런쳐 연결
    [Min(0f)] public float detectCooldown = 0.5f;

    private static readonly Collider2D[] overlapResults = new Collider2D[16];

    private float nextDetectTime;
    private float nextRetryTime;
    private Collider2D detectorCollider;
    private readonly HashSet<Collider2D> sawsInRange = new HashSet<Collider2D>();
    private readonly List<Collider2D> staleContacts = new List<Collider2D>(16);
    private bool columnFired;
    private bool needsInitialOverlap;

    private void Awake()
    {
        detectorCollider = GetComponent<Collider2D>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isActiveAndEnabled && IsSaw(other)) sawsInRange.Add(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        sawsInRange.Remove(other);
        if (sawsInRange.Count == 0) columnFired = false;
    }

    private void OnEnable()
    {
        sawsInRange.Clear();
        columnFired = false;
        needsInitialOverlap = true;
        nextRetryTime = nextDetectTime = Time.time;
    }

    private void OnDisable()
    {
        sawsInRange.Clear();
        staleContacts.Clear();
        columnFired = false;
    }

    private void FixedUpdate()
    {
        if (RageTransformFreezeController.IsGameplayPauseActive || Time.time < nextRetryTime) return;
        // Retry once per detector, rather than an overlap query for every staying saw.
        nextRetryTime = Time.time + 0.05f;
        foreach (Collider2D saw in sawsInRange)
            if (saw == null || !saw.enabled || !saw.gameObject.activeInHierarchy) staleContacts.Add(saw);
        foreach (Collider2D saw in staleContacts) sawsInRange.Remove(saw);
        staleContacts.Clear();
        if (sawsInRange.Count == 0) columnFired = false;

        if (columnFired || (!needsInitialOverlap && sawsInRange.Count == 0)) return;
        if (Time.time < nextDetectTime || launcher == null || !launcher.CanFire()) return;

        Transform target = PickRandomSawTarget();
        needsInitialOverlap = false;
        if (target != null && launcher.TryFireAt(target))
        {
            // Keep the whole detected column consumed even after the selected
            // target is hit or its in-flight reservation is released.
            columnFired = true;
            nextDetectTime = Time.time + Mathf.Max(0f, detectCooldown);
        }
    }

    private Transform PickRandomSawTarget()
    {
        if (detectorCollider == null || !detectorCollider.enabled) return null;

        var filter = new ContactFilter2D();
        filter.useTriggers = true;

        int count = detectorCollider.Overlap(filter, overlapResults);
        int sawCount = 0;
        Transform selected = null;
        sawsInRange.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider2D candidate = overlapResults[i];
            overlapResults[i] = null;

            if (!IsSaw(candidate)) continue;
            sawsInRange.Add(candidate);
            if (Bomb.IsTargetInFlight(candidate)) continue;

            sawCount++;
            if (Random.Range(0, sawCount) == 0)
                selected = candidate.transform;
        }

        return selected;
    }

    private static bool IsSaw(Collider2D other)
    {
        if (other == null || !other.enabled || !other.gameObject.activeInHierarchy)
            return false;

        var info = other.GetComponent<ObstacleInfo>();
        return info != null && info.type == ObstacleType.Saw;
    }

}
