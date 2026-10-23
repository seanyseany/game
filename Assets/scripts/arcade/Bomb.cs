using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(200)]
public class Bomb : MonoBehaviour
{
    public float speed = 12f;

    [Header("Slowdown Compensation")]
    [SerializeField] private bool accelerateDuringObstacleSlowdown = true;
    [SerializeField] private float slowdownSpeedBoostCap = 3f;

    [Header("Rotate")]
    public float rotationOffset = 0f;
    public float rotateLerpSpeed = 20f;
    public float maxTiltAngle = 30f;

    [Header("Stop Explosion")]
    [Tooltip("계산된 이동 속도가 이 값 이하가 되면 폭발")]
    [SerializeField] private float stopSpeedThreshold = 0.01f;
    [Tooltip("목표까지 이 거리 이하로 접근하면 정지한 것으로 보고 폭발")]
    [SerializeField] private float targetArrivalDistance = 0.02f;

    [Header("Pooling")]
    public string poolTag = "Bomb";
    public string hitboxTag = "BombHitBox";
    private string smokeTag = "Smoke";
    public GameObject hitboxFallbackPrefab;
    public GameObject smokeFallbackPrefab;

    private Transform target;
    private bool exploded = false;
    private bool hasTargetReservation;
    private static readonly Dictionary<Collider2D, int> targetsInFlight = new Dictionary<Collider2D, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetTargetReservations() => targetsInFlight.Clear();

    internal static bool IsTargetInFlight(Collider2D source)
    {
        return source != null && targetsInFlight.ContainsKey(source);
    }

    private void ReleaseTargetReservation()
    {
        if (!hasTargetReservation) return;
        foreach (Collider2D collider in targetColliders)
        {
            // Destroyed Unity objects still need their dictionary keys removed.
            if (ReferenceEquals(collider, null)) continue;
            if (!targetsInFlight.TryGetValue(collider, out int count)) continue;
            if (count <= 1) targetsInFlight.Remove(collider);
            else targetsInFlight[collider] = count - 1;
        }
        hasTargetReservation = false;
    }

    private Collider2D col;
    private readonly List<Collider2D> ignoredObstacleColliders = new List<Collider2D>(32);
    private readonly HashSet<Collider2D> targetColliders = new HashSet<Collider2D>();
    private readonly List<Collider2D> targetColliderCandidates = new List<Collider2D>(4);
    private struct TargetCenters
    {
        public Vector2 visible;
        public Vector2 physical;
    }
    private readonly Dictionary<Collider2D, TargetCenters> previousTargetCenters = new Dictionary<Collider2D, TargetCenters>();
    private readonly List<RaycastHit2D> sweepHits = new List<RaycastHit2D>(16);
    private ContactFilter2D targetFilter;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        ReleaseTargetReservation();
        exploded = false;
        target = null;
        RestoreIgnoredObstacleCollisions();
        targetColliders.Clear();
        previousTargetCenters.Clear();
        sweepHits.Clear();

        if (col != null) col.enabled = true;
    }

    private void OnDisable()
    {
        ReleaseTargetReservation();
        RestoreIgnoredObstacleCollisions();
        targetColliders.Clear();
        targetColliderCandidates.Clear();
        previousTargetCenters.Clear();
    }

    private void OnDestroy() => ReleaseTargetReservation();

    public void SetTarget(Transform t)
    {
        ReleaseTargetReservation();
        RestoreIgnoredObstacleCollisions();
        targetColliders.Clear();
        previousTargetCenters.Clear();

        target = t;
        exploded = false;

        if (col != null)
            col.enabled = true;

        ConfigureTargetOnlyCollisions();
        if (targetColliders.Count > 0)
        {
            foreach (Collider2D collider in targetColliders)
            {
                targetsInFlight.TryGetValue(collider, out int count);
                targetsInFlight[collider] = count + 1;
            }
            hasTargetReservation = true;
        }
    }

    public void SetSmokeTag(string tag) => smokeTag = tag;

    void LateUpdate()
    {
        Advance(Time.deltaTime);
    }

    private void Advance(float deltaSeconds)
    {
        if (exploded) return;

        if (target == null || !target.gameObject.activeInHierarchy)
        {
            Explode();
            return;
        }

        if (!TryGetTargetPosition(out Vector3 aim))
        {
            Explode();
            return;
        }
        Vector3 toTarget = aim - transform.position;
        if (toTarget.sqrMagnitude <= targetArrivalDistance * targetArrivalDistance)
        {
            TryFindTargetImpact(transform.position, transform.position, out Collider2D overlap, out _);
            Explode(overlap);
            return;
        }

        Vector3 dir = toTarget.normalized;
        float moveSpeed = GetCurrentMoveSpeed();
        if (moveSpeed <= Mathf.Max(0f, stopSpeedThreshold))
        {
            Explode();
            return;
        }

        float rawAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + rotationOffset;
        rawAngle = Mathf.DeltaAngle(0f, rawAngle);

        float clamped = Mathf.Clamp(rawAngle, -maxTiltAngle, maxTiltAngle);
        Quaternion targetRot = Quaternion.Euler(0f, 0f, clamped);

        if (rotateLerpSpeed <= 0f)
            transform.rotation = targetRot;
        else
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, rotateLerpSpeed * Mathf.Max(0f, deltaSeconds));

        Vector3 from = transform.position;
        Vector3 next = Vector3.MoveTowards(from, aim, moveSpeed * Mathf.Max(0f, deltaSeconds));
        if (TryFindTargetImpact(from, next, out Collider2D hit, out float fraction))
        {
            transform.position = Vector3.Lerp(from, next, fraction);
            Explode(hit);
            return;
        }
        transform.position = next;
        if ((aim - next).sqrMagnitude <= targetArrivalDistance * targetArrivalDistance)
            Explode();
    }

    private bool TryGetTargetPosition(out Vector3 aim)
    {
        aim = target.position;
        // This is a 2D projectile: sorting depth must not consume flight speed.
        aim.z = transform.position.z;
        float nearest = float.PositiveInfinity;
        foreach (Collider2D collider in targetColliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            Vector2 center = GetTargetCenter(collider);
            float distance = (center - (Vector2)transform.position).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            // Circle targets use the same displayed pose for both homing and
            // direct impact geometry, independent of the physics world's sync.
            aim.x = center.x;
            aim.y = center.y;
        }
        return nearest < float.PositiveInfinity || targetColliders.Count == 0;
    }

    private static Vector2 GetTargetCenter(Collider2D collider)
    {
        return collider is CircleCollider2D
            ? (Vector2)collider.transform.TransformPoint(collider.offset)
            : (Vector2)collider.bounds.center;
    }

    private static Vector2 GetPhysicsCircleCenter(CircleCollider2D circle, Vector2 visibleCenter)
    {
        Rigidbody2D body = circle.attachedRigidbody;
        if (body == null) return visibleCenter;
        // A Rigidbody may advance before its interpolated Transform does, or a
        // parent may move the Transform before physics sync. Track both histories.
        Vector2 offset = visibleCenter - (Vector2)body.transform.position;
        float radians = (body.rotation - body.transform.eulerAngles.z) * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
        return body.position + new Vector2(offset.x * cos - offset.y * sin, offset.x * sin + offset.y * cos);
    }

    private bool TryFindTargetImpact(Vector3 from, Vector3 to, out Collider2D impact, out float impactFraction)
    {
        impact = null;
        impactFraction = 0f;
        if (col == null || !col.enabled || targetColliders.Count == 0) return false;
        float nearestFraction = float.PositiveInfinity;
        bool directCircleSweep = TryGetBombCapsule(out Vector2 offset, out Vector2 axis, out float halfLength, out float radius);
        foreach (Collider2D targetCollider in targetColliders)
        {
            if (targetCollider == null) continue;
            if (!targetCollider.enabled || !targetCollider.gameObject.activeInHierarchy)
            {
                previousTargetCenters.Remove(targetCollider);
                continue;
            }
            Vector2 center = GetTargetCenter(targetCollider);
            Vector2 physicsCenter = targetCollider is CircleCollider2D targetCircle
                ? GetPhysicsCircleCenter(targetCircle, center) : center;
            bool hasPrevious = previousTargetCenters.TryGetValue(targetCollider, out TargetCenters previous);
            Vector2 movement = hasPrevious ? center - previous.visible : Vector2.zero;
            Vector2 physicsMovement = hasPrevious ? physicsCenter - previous.physical : Vector2.zero;
            previousTargetCenters[targetCollider] = new TargetCenters { visible = center, physical = physicsCenter };
            // Express both movements relative to the target's current pose.
            // This also catches a saw sweeping across a nearly stationary bomb.
            Vector3 relativeFrom = from + (Vector3)movement;
            if (directCircleSweep && targetCollider is CircleCollider2D circle)
            {
                Vector3 scale = circle.transform.lossyScale;
                float combinedRadius = radius + circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                SweepCapsuleAgainstCircle((Vector2)relativeFrom + offset - center,
                    (Vector2)to + offset - center, axis, halfLength, combinedRadius, out float fraction);
                if (fraction > 0f && ((physicsCenter - center).sqrMagnitude > 0.00000001f ||
                    (physicsMovement - movement).sqrMagnitude > 0.00000001f))
                {
                    SweepCapsuleAgainstCircle((Vector2)from + physicsMovement + offset - physicsCenter,
                        (Vector2)to + offset - physicsCenter, axis, halfLength, combinedRadius, out float physicsFraction);
                    fraction = Mathf.Min(fraction, physicsFraction);
                }
                if (fraction <= 1f && fraction < nearestFraction)
                {
                    nearestFraction = fraction;
                    impactFraction = fraction;
                    impact = targetCollider;
                }
                continue;
            }
            float distance = Vector2.Distance(relativeFrom, to);
            int count = CastShape(relativeFrom, to);
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D candidate = sweepHits[i];
                if (candidate.collider != targetCollider) continue;
                float fraction = distance > 0f ? Mathf.Clamp01(candidate.distance / distance) : 0f;
                if (fraction >= nearestFraction) continue;
                nearestFraction = fraction;
                impactFraction = fraction;
                impact = targetCollider;
            }
        }
        return impact != null;
    }

    private bool TryGetBombCapsule(out Vector2 offset, out Vector2 axis, out float halfLength, out float radius)
    {
        offset = transform.TransformVector(col.offset);
        axis = transform.right;
        halfLength = 0f;
        radius = 0f;
        Vector3 scale = transform.lossyScale;
        Vector2 sizeScale = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        if (col is CircleCollider2D circle)
        {
            radius = circle.radius * Mathf.Max(sizeScale.x, sizeScale.y);
            return true;
        }
        if (!(col is CapsuleCollider2D capsule)) return false;
        Vector2 size = Vector2.Scale(capsule.size, sizeScale);
        bool horizontal = capsule.direction == CapsuleDirection2D.Horizontal;
        axis = horizontal ? transform.right : transform.up;
        radius = (horizontal ? size.y : size.x) * 0.5f;
        halfLength = Mathf.Max(0f, (horizontal ? size.x : size.y) * 0.5f - radius);
        return true;
    }

    // Relative motion turns a moving circle/capsule pair into a point entering
    // a stationary capsule. Test its rectangle and two round ends, with no scene query.
    private static bool SweepCapsuleAgainstCircle(Vector2 from, Vector2 to, Vector2 axis,
        float halfLength, float radius, out float fraction)
    {
        Vector2 normal = new Vector2(-axis.y, axis.x);
        Vector2 start = new Vector2(Vector2.Dot(from, axis), Vector2.Dot(from, normal));
        Vector2 delta = new Vector2(Vector2.Dot(to - from, axis), Vector2.Dot(to - from, normal));
        fraction = float.PositiveInfinity;
        float near = 0f, far = 1f;
        if (ClipSweepAxis(start.x, delta.x, halfLength, ref near, ref far) &&
            ClipSweepAxis(start.y, delta.y, radius, ref near, ref far))
            fraction = near;
        fraction = Mathf.Min(fraction, SweepCircleEnd(start - new Vector2(halfLength, 0f), delta, radius));
        fraction = Mathf.Min(fraction, SweepCircleEnd(start + new Vector2(halfLength, 0f), delta, radius));
        return fraction <= 1f;
    }

    private static bool ClipSweepAxis(float start, float delta, float extent, ref float near, ref float far)
    {
        if (Mathf.Abs(delta) < 0.0000001f) return Mathf.Abs(start) <= extent;
        float a = (-extent - start) / delta;
        float b = (extent - start) / delta;
        near = Mathf.Max(near, Mathf.Min(a, b));
        far = Mathf.Min(far, Mathf.Max(a, b));
        return near <= far;
    }

    private static float SweepCircleEnd(Vector2 start, Vector2 delta, float radius)
    {
        float c = start.sqrMagnitude - radius * radius;
        if (c <= 0f) return 0f;
        float a = delta.sqrMagnitude;
        if (a <= 0f) return float.PositiveInfinity;
        float b = Vector2.Dot(start, delta);
        if (b >= 0f) return float.PositiveInfinity;
        float discriminant = b * b - a * c;
        if (discriminant < 0f) return float.PositiveInfinity;
        return Mathf.Max(0f, (-b - Mathf.Sqrt(discriminant)) / a);
    }

    private int CastShape(Vector3 from, Vector3 to)
    {
        Vector2 displacement = (Vector2)(to - from);
        float distance = displacement.magnitude;
        Vector2 direction = distance > 0f ? displacement / distance : Vector2.right;
        // Bomb's supplied prefab has no Rigidbody2D. Cast its actual shape from
        // the current Transform pose instead of relying on an old physics pose.
        Vector2 center = (Vector2)from + (Vector2)transform.TransformVector(col.offset);
        Vector3 scale = transform.lossyScale;
        Vector2 shapeScale = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        float angle = transform.eulerAngles.z;
        int count;
        if (col is CapsuleCollider2D capsule)
            count = Physics2D.CapsuleCast(center, Vector2.Scale(capsule.size, shapeScale), capsule.direction,
                angle, direction, targetFilter, sweepHits, distance);
        else if (col is CircleCollider2D circle)
            count = Physics2D.CircleCast(center, circle.radius * Mathf.Max(shapeScale.x, shapeScale.y),
                direction, targetFilter, sweepHits, distance);
        else if (col is BoxCollider2D box)
            count = Physics2D.BoxCast(center, Vector2.Scale(box.size, shapeScale), angle,
                direction, targetFilter, sweepHits, distance);
        else if (col.attachedRigidbody != null)
            count = col.Cast(from, angle, direction, targetFilter, sweepHits, distance);
        else
            count = Physics2D.Raycast(center, direction, targetFilter, sweepHits, distance);
        return count;
    }

    private float GetCurrentMoveSpeed()
    {
        float moveSpeed = Mathf.Max(0f, speed);

        if (accelerateDuringObstacleSlowdown && GameData.Instance != null)
        {
            float currentMult = Mathf.Max(0.0001f, GameData.Instance.GetStageSpeedMult());
            float normalMult = Mathf.Max(currentMult, GameData.Instance.GetStageSpeedMultIgnoringObstacleSlowdown());

            if (currentMult < normalMult)
            {
                float slowdownBoost = normalMult / currentMult;
                moveSpeed *= Mathf.Clamp(slowdownBoost, 1f, Mathf.Max(1f, slowdownSpeedBoostCap));
            }
        }

        return moveSpeed;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (exploded) return;
        if (!IsTargetCollider(other)) return;

        Explode(other);
    }

    private void ConfigureTargetOnlyCollisions()
    {
        if (col == null)
            return;

        CacheTargetColliders();
        if (targetColliders.Count == 0)
            return;

        // A trigger never physically blocks another obstacle. Its callback and
        // sweep already accept only the assigned target, so no global scan is needed.
        if (col.isTrigger)
            return;

        ObstacleInfo[] obstacles = FindObjectsByType<ObstacleInfo>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < obstacles.Length; i++)
        {
            ObstacleInfo obstacle = obstacles[i];
            if (obstacle == null)
                continue;

            Collider2D[] obstacleColliders = obstacle.GetComponentsInChildren<Collider2D>(true);
            for (int j = 0; j < obstacleColliders.Length; j++)
            {
                Collider2D obstacleCollider = obstacleColliders[j];
                if (obstacleCollider == null || obstacleCollider == col)
                    continue;

                if (targetColliders.Contains(obstacleCollider))
                    continue;

                Physics2D.IgnoreCollision(col, obstacleCollider, true);
                ignoredObstacleColliders.Add(obstacleCollider);
            }
        }
    }

    private void CacheTargetColliders()
    {
        if (target == null)
            return;

        Transform targetRoot = ResolveTargetRoot(target);
        if (targetRoot == null)
            targetRoot = target;

        targetRoot.GetComponentsInChildren(true, targetColliderCandidates);
        int layers = 0;
        for (int i = 0; i < targetColliderCandidates.Count; i++)
        {
            Collider2D targetCollider = targetColliderCandidates[i];
            if (targetCollider != null)
            {
                targetColliders.Add(targetCollider);
                Vector2 center = GetTargetCenter(targetCollider);
                previousTargetCenters[targetCollider] = new TargetCenters
                {
                    visible = center,
                    physical = targetCollider is CircleCollider2D circle ? GetPhysicsCircleCenter(circle, center) : center
                };
                layers |= 1 << targetCollider.gameObject.layer;
            }
        }
        targetFilter = new ContactFilter2D { useTriggers = true };
        targetFilter.SetLayerMask(layers);
    }

    private static Transform ResolveTargetRoot(Transform source)
    {
        if (source == null)
            return null;

        Obstacle obstacle = source.GetComponentInParent<Obstacle>();
        if (obstacle != null)
            return obstacle.transform;

        ObstacleInfo info = source.GetComponentInParent<ObstacleInfo>();
        if (info != null)
            return info.transform;

        return source;
    }

    private bool IsTargetCollider(Collider2D other)
    {
        if (other == null)
            return false;

        if (targetColliders.Contains(other))
            return true;

        foreach (Collider2D targetCollider in targetColliders)
        {
            if (targetCollider != null && other.transform.IsChildOf(targetCollider.transform))
                return true;
        }

        return false;
    }

    private void RestoreIgnoredObstacleCollisions()
    {
        if (col == null || ignoredObstacleColliders.Count == 0)
            return;

        for (int i = 0; i < ignoredObstacleColliders.Count; i++)
        {
            Collider2D ignored = ignoredObstacleColliders[i];
            if (ignored != null)
                Physics2D.IgnoreCollision(col, ignored, false);
        }

        ignoredObstacleColliders.Clear();
    }

    private void Explode(Collider2D impact = null)
    {
        if (exploded) return;
        exploded = true;
        ReleaseTargetReservation();

        ArcadeFeedback.ShowScaledBoom(transform.position, 0.5f);

        GameObject hb = null;
        if (ObjectPool.Instance != null)
        {
            hb = ObjectPool.Instance.SpawnFromPool(hitboxTag, transform.position, Quaternion.identity);
        }
        if (hb == null && hitboxFallbackPrefab != null)
            hb = Instantiate(hitboxFallbackPrefab, transform.position, Quaternion.identity);

        if (hb != null)
        {
            BombHitBox hitbox = hb.GetComponent<BombHitBox>();
            if (hitbox != null)
            {
                // 스폰 태그와 복귀 태그를 강제로 일치시켜 풀 누수를 막는다.
                hitbox.poolTag = hitboxTag;
                hitbox.Activate(0.3f);
                // Apply the confirmed contact now: a swinging obstacle can leave
                // the explosion before the next physics trigger callback.
                if (impact != null) hitbox.ApplyImpact(impact);
            }
        }

        GameObject smoke = null;
        if (ObjectPool.Instance != null)
            smoke = ObjectPool.Instance.SpawnFromPool(smokeTag, transform.position, Quaternion.identity);
        if (smoke == null && smokeFallbackPrefab != null)
            Instantiate(smokeFallbackPrefab, transform.position, Quaternion.identity);

        ReturnSelf();
    }

    private void ReturnSelf()
    {
        if (ObjectPool.Instance != null)
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }
}
