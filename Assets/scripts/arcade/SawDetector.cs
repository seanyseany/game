using UnityEngine;

public class SawDetector : MonoBehaviour
{
    public BombLauncher launcher; // 인스펙터에 런쳐 연결
    [Min(0f)] public float detectCooldown = 0.5f;

    private static readonly Collider2D[] overlapResults = new Collider2D[16];

    private float nextDetectTime;
    private Collider2D detectorCollider;

    private void Awake()
    {
        detectorCollider = GetComponent<Collider2D>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Time.time < nextDetectTime)
            return;

        if (!IsSaw(other))
            return;

        if (launcher != null && launcher.CanFire())
        {
            Transform target = PickRandomSawTarget(other);
            if (target == null)
                return;

            nextDetectTime = Time.time + detectCooldown;
            launcher.FireAt(target);
        }
    }

    private Transform PickRandomSawTarget(Collider2D fallback)
    {
        if (detectorCollider == null)
            return fallback != null ? fallback.transform : null;

        var filter = new ContactFilter2D();
        filter.useTriggers = true;

        int count = detectorCollider.Overlap(filter, overlapResults);
        int sawCount = 0;
        Transform fallbackTarget = fallback != null ? fallback.transform : null;

        for (int i = 0; i < count; i++)
        {
            Collider2D candidate = overlapResults[i];
            overlapResults[i] = null;

            if (!IsSaw(candidate))
                continue;

            sawCount++;
            if (Random.Range(0, sawCount) == 0)
                fallbackTarget = candidate.transform;
        }

        return fallbackTarget;
    }

    private static bool IsSaw(Collider2D other)
    {
        if (other == null)
            return false;

        var info = other.GetComponent<ObstacleInfo>();
        return info != null && info.type == ObstacleType.Saw;
    }
}
