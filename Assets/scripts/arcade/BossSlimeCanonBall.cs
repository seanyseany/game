using UnityEngine;

public class BossSlimeCanonBall : MonoBehaviour, IReinitializable
{
    [System.Serializable]
    public struct LaunchVersion
    {
        public float speed;
        public float upSpeed;
    }

    [Header("Movement")]
    public float rotateSpeed = 360f;
    public float lifeTime = 8f;

    [Header("Launch Versions (set any count in Inspector)")]
    public LaunchVersion[] launchVersions;

    [Header("Hit")]
    public string playerTag = "player";
    public string gateTag = "Gate";
    public bool damageGateOnHit = true;

    [Header("FX")]
    public string destroyPoolTag = "";
    public GameObject destroyFxPrefab;

    [Header("Pool")]
    public bool usePool = true;
    public string poolTag = "BossSlimeCanonBall";

    private Rigidbody2D rb;
    private float alive;
    private bool dead;
    private float runtimeLaunchSpeed = 9f;
    private float runtimeLaunchUpSpeed = 2f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        alive = 0f;
        dead = false;

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = true;
        }

        // 버전이 지정되지 않은 경우 기본값
        runtimeLaunchSpeed = 9f;
        runtimeLaunchUpSpeed = 2f;
    }

    void Update()
    {
        if (dead) return;

        transform.Rotate(0f, 0f, rotateSpeed * Time.deltaTime, Space.Self);

        alive += Time.deltaTime;
        if (alive >= lifeTime)
            ExplodeAndDie(false);
    }

    public void LaunchAtAngle(float angleDeg)
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (rb == null) return;

        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        if (dir.sqrMagnitude <= 0.0001f)
            dir = Vector2.left;

        dir.Normalize();
        rb.linearVelocity = new Vector2(dir.x * runtimeLaunchSpeed, dir.y * runtimeLaunchSpeed + runtimeLaunchUpSpeed);
    }

    public int GetVersionCount()
    {
        return (launchVersions != null) ? launchVersions.Length : 0;
    }

    public void ApplyVersion(int index)
    {
        if (launchVersions == null || launchVersions.Length == 0) return;
        int i = Mathf.Clamp(index, 0, launchVersions.Length - 1);
        runtimeLaunchSpeed = launchVersions[i].speed;
        runtimeLaunchUpSpeed = launchVersions[i].upSpeed;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (dead) return;
        HandleHit(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (dead) return;
        if (collision == null) return;
        HandleHit(collision.collider);
    }

    private void HandleHit(Collider2D other)
    {
        if (other == null) return;
        if (other.CompareTag(playerTag))
        {
            ExplodeAndDie(true);
            return;
        }

        if (other.CompareTag(gateTag) || other.GetComponent<GateHealth>() != null)
        {
            if (damageGateOnHit && GateHealth.Instance != null)
                GateHealth.Instance.TakeBossMissileHit();

            ExplodeAndDie(true);
        }
    }

    private void ExplodeAndDie(bool spawnFx)
    {
        if (dead) return;
        dead = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        if (spawnFx)
        {
            if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(destroyPoolTag) && ObjectPool.Instance.HasPool(destroyPoolTag))
                ObjectPool.Instance.SpawnFromPool(destroyPoolTag, transform.position, Quaternion.identity);
            else if (destroyFxPrefab != null)
            {
                GameObject fx = Instantiate(destroyFxPrefab, transform.position, Quaternion.identity);
                if (fx != null)
                    Destroy(fx, 1.5f);
            }
        }

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }
}
