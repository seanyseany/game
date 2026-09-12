using UnityEngine;
using System.Collections.Generic;

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

    private Collider2D col;
    private readonly List<Collider2D> ignoredObstacleColliders = new List<Collider2D>(32);
    private readonly HashSet<Collider2D> targetColliders = new HashSet<Collider2D>();

    private void Awake()
    {
        col = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        exploded = false;
        target = null;
        RestoreIgnoredObstacleCollisions();
        targetColliders.Clear();

        if (col != null) col.enabled = true;
    }

    private void OnDisable()
    {
        RestoreIgnoredObstacleCollisions();
        targetColliders.Clear();
    }

    public void SetTarget(Transform t)
    {
        RestoreIgnoredObstacleCollisions();
        targetColliders.Clear();

        target = t;
        exploded = false;

        if (col != null)
            col.enabled = true;

        ConfigureTargetOnlyCollisions();
    }

    public void SetSmokeTag(string tag) => smokeTag = tag;

    void Update()
    {
        if (exploded) return;

        if (target == null)
        {
            Explode();
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        if (toTarget.sqrMagnitude <= targetArrivalDistance * targetArrivalDistance)
        {
            Explode();
            return;
        }

        Vector3 dir = toTarget.normalized;
        float moveSpeed = GetCurrentMoveSpeed();
        if (moveSpeed <= Mathf.Max(0f, stopSpeedThreshold))
        {
            Explode();
            return;
        }

        transform.position += dir * moveSpeed * Time.deltaTime;

        float rawAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + rotationOffset;
        rawAngle = Mathf.DeltaAngle(0f, rawAngle);

        float clamped = Mathf.Clamp(rawAngle, -maxTiltAngle, maxTiltAngle);
        Quaternion targetRot = Quaternion.Euler(0f, 0f, clamped);

        if (rotateLerpSpeed <= 0f)
            transform.rotation = targetRot;
        else
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, rotateLerpSpeed * Time.deltaTime);
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

        Explode();
    }

    private void ConfigureTargetOnlyCollisions()
    {
        if (col == null)
            return;

        CacheTargetColliders();
        if (targetColliders.Count == 0)
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

        Collider2D[] colliders = targetRoot.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D targetCollider = colliders[i];
            if (targetCollider != null)
                targetColliders.Add(targetCollider);
        }
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

        Collider2D[] parentColliders = other.GetComponentsInParent<Collider2D>(true);
        for (int i = 0; i < parentColliders.Length; i++)
        {
            if (targetColliders.Contains(parentColliders[i]))
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

    private void Explode()
    {
        if (exploded) return;
        exploded = true;

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
