using UnityEngine;

public class Bomb : MonoBehaviour
{
    public float speed = 12f;

    [Header("Rotate")]
    public float rotationOffset = 0f;
    public float rotateLerpSpeed = 20f;
    public float maxTiltAngle = 30f;

    [Header("Collider Timing")]
    public float colliderEnableDelay = 0.5f;
    public float colliderEnableDistance = 1.2f;

    [Header("Auto Destroy")]
    [Tooltip("발사 후 이 시간이 지나면 자동 폭발")]
    public float autoExplodeTime = 1.5f;

    [Header("Pooling")]
    public string poolTag = "Bomb";
    public string hitboxTag = "BombHitBox";
    private string smokeTag = "Smoke";
    public GameObject hitboxFallbackPrefab;
    public GameObject smokeFallbackPrefab;

    private Transform target;
    private bool exploded = false;

    private Collider2D col;
    private float spawnTime;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        exploded = false;
        target = null;
        spawnTime = Time.time;

        if (col != null) col.enabled = false;
    }

    public void SetTarget(Transform t)
    {
        target = t;
        exploded = false;

        spawnTime = Time.time;

        if (col != null) col.enabled = false;
    }

    public void SetSmokeTag(string tag) => smokeTag = tag;

    void Update()
    {
        if (exploded) return;

        if (Time.time - spawnTime >= autoExplodeTime)
        {
            Explode();
            return;
        }

        if (target == null)
        {
            Explode();
            return;
        }

        if (col != null && !col.enabled)
        {
            float dt = Time.time - spawnTime;
            float dist = Vector2.Distance(transform.position, target.position);

            if (dt >= colliderEnableDelay || dist <= colliderEnableDistance)
                col.enabled = true;
        }

        Vector3 dir = (target.position - transform.position).normalized;
        transform.position += dir * speed * Time.deltaTime;

        float rawAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + rotationOffset;
        rawAngle = Mathf.DeltaAngle(0f, rawAngle);

        float clamped = Mathf.Clamp(rawAngle, -maxTiltAngle, maxTiltAngle);
        Quaternion targetRot = Quaternion.Euler(0f, 0f, clamped);

        if (rotateLerpSpeed <= 0f)
            transform.rotation = targetRot;
        else
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, rotateLerpSpeed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (exploded) return;
        if (col != null && !col.enabled) return;

        var info = other.GetComponent<ObstacleInfo>();
        if (info != null && info.type == ObstacleType.Saw)
        {
            Explode();
        }
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
