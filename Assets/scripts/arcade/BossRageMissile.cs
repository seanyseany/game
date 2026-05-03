using UnityEngine;

public class BossRageMissile : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 12f;
    public Vector3 direction = Vector3.left;
    public float lifeTime = 8f;

    [Header("Hit")]
    public string playerTag = "player";
    public string gateTag = "Gate";
    public string destroyPoolTag = "";
    public GameObject destroyFxPrefab;

    [Header("Pooling")]
    public bool usePool = true;
    public string poolTag = "BossRageMissile";

    private float alive;
    private bool dead;

    private void OnEnable()
    {
        alive = 0f;
        dead = false;
    }

    private void Update()
    {
        if (dead) return;

        Vector3 dir = direction.sqrMagnitude <= 0.0001f ? Vector3.left : direction.normalized;
        transform.position += dir * speed * Time.deltaTime;

        alive += Time.deltaTime;
        if (alive >= lifeTime)
            ExplodeAndDie(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (dead) return;

        if (other.CompareTag(playerTag))
        {
            ExplodeAndDie(true);
            return;
        }

        if (other.CompareTag(gateTag) || other.GetComponent<GateHealth>() != null)
        {
            if (GateHealth.Instance != null)
                GateHealth.Instance.TakeBossMissileHit();

            ExplodeAndDie(true);
        }
    }

    private void ExplodeAndDie(bool spawnFx)
    {
        if (dead) return;
        dead = true;

        if (spawnFx)
        {
            if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(destroyPoolTag) && ObjectPool.Instance.HasPool(destroyPoolTag))
                ObjectPool.Instance.SpawnFromPool(destroyPoolTag, transform.position, Quaternion.identity);
            else if (destroyFxPrefab != null)
                Instantiate(destroyFxPrefab, transform.position, Quaternion.identity);
        }

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }
}
