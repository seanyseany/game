using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Missile : MonoBehaviour
{
    [SerializeField] private float baseSpeed = 12.5f; 
    [SerializeField] private float life = 5f;

    [Header("Explosion Effects")]
    public GameObject[] explosionPrefabs;

    private Rigidbody2D rb;
    private float spawnTime;
    private bool returned = false;
    private bool forceDisabledByPool = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;

        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    void OnEnable()
    {
        returned = false;
        forceDisabledByPool = false;

        spawnTime = Time.time;
        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.left * baseSpeed;
        rb.angularVelocity = 0f;
    }

    public void ForceLaunch(float speedOverride)
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;

        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.left * Mathf.Abs(speedOverride);
        rb.angularVelocity = 0f;
    }


    void Update()
    {
        // 🔥 life가 끝나면 자동 회수
        if (!returned && !forceDisabledByPool && Time.time - spawnTime >= life)
        {
            ReturnToPool();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 플레이어 충돌
        var player = other.GetComponent<Player>();
        if (player != null)
        {
            if (GameData.Instance != null &&
                GameData.Instance.selectedPlayerType == 3)
            {
                Explode();
                ReturnToPool();
                return;
            }

            if (!player.IsRageModeActive())
            {
                player.TakeDamage(1);
            }

            Explode();
            ReturnToPool();
            return;
        }

        // 적 충돌은 무시
        if (other.CompareTag("Enemy"))
            return;
    }

    private void Explode()
    {
        // 풀에 Smoke가 있으면 우선 사용
        if (ObjectPool.Instance != null)
        {
            GameObject effect = ObjectPool.Instance.SpawnFromPool("Smoke", transform.position, Quaternion.identity);
            if (effect == null && explosionPrefabs.Length > 0)
            {
                int idx = Random.Range(0, explosionPrefabs.Length);
                Instantiate(explosionPrefabs[idx], transform.position, Quaternion.identity);
            }
            return;
        }

        // 풀 없을 때
        if (explosionPrefabs.Length > 0)
        {
            int idx = Random.Range(0, explosionPrefabs.Length);
            Instantiate(explosionPrefabs[idx], transform.position, Quaternion.identity);
        }
    }

    // 🔥 풀 반환을 하나의 메서드로 통일
    private void ReturnToPool()
    {
        // 두 번 반환 방지
        if (returned) return;
        returned = true;

        // Rigidbody 안정화
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.Sleep();

        // 풀로 반환
        if (ObjectPool.Instance != null)
            ObjectPool.Instance.ReturnToPool("missile", gameObject);
        else
            Destroy(gameObject);
    }
    public void NotifyReturnedByPool()
    {
        forceDisabledByPool = true;
    }
}
