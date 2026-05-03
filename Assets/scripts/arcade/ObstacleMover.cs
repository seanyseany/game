using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class ObstacleMover : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speedX = -3.5f; // ✅ 오른쪽→왼쪽 속도 더 빠르게 (기존 -2 → -3.5)
    public float speedY = 2f;
    public float bounceFactor = 1f;
    public float maxVerticalSpeed = 3f;
    [Header("Pooled Rage Obstacle Return")]
    public float pooledLifetime = 5f;
    public float pooledDespawnX = -25f;

    private Rigidbody2D rb;
    private Vector2 moveDir;
    private bool ignoringPlayer = false;
    private float spawnTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.freezeRotation = true;
        ResetMoveDirection();
    }

    private void OnEnable()
    {
        spawnTime = Time.time;
        ResetMoveDirection();
    }

    private void ResetMoveDirection()
    {
        // ✅ 속도 크기 유지하면서 이동 각도 랜덤화
        float angleDeg = Random.Range(-35f, 35f); // ← 각도 범위 (조절 가능)
        float angleRad = angleDeg * Mathf.Deg2Rad;

        // speedX는 왼쪽 이동이니까 음수 유지
        float totalSpeed = Mathf.Abs(speedX); // 속도 크기 (예: 3.5)
        moveDir = new Vector2(-totalSpeed * Mathf.Cos(angleRad),
                            totalSpeed * Mathf.Sin(angleRad));

        // Y속도 제한 적용
        moveDir.y = Mathf.Clamp(moveDir.y, -maxVerticalSpeed, maxVerticalSpeed);
    }


    void Update()
    {
        // 직접 이동 (Kinematic은 물리 이동 없음)
        transform.Translate(moveDir * Time.deltaTime);

        // Y속도 제한
        moveDir.y = Mathf.Clamp(moveDir.y, -maxVerticalSpeed, maxVerticalSpeed);

        if (ShouldReturnToPool())
            TryReturnToObjectPool();
    }

    private bool ShouldReturnToPool()
    {
        if (ObjectPool.Instance == null)
            return false;

        if (transform.position.x <= pooledDespawnX)
            return true;

        return pooledLifetime > 0f && Time.time - spawnTime >= pooledLifetime;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        string tag = other.tag;

        // ✅ floor / platform / Obstacle / player 동일한 튕김 로직
        if (tag == "floor" || tag == "platform" || tag == "Obstacle" || tag == "player")
        {
            float otherY = other.bounds.center.y;
            float myY = transform.position.y;

            // 위쪽 닿았으면 아래로, 아래쪽 닿았으면 위로
            float newYDir = (myY > otherY) ? 1f : -1f;
            moveDir.y = newYDir * Mathf.Abs(moveDir.y) * bounceFactor;

            // X속도 유지 (오른쪽→왼쪽 빠르게)
            moveDir.x = speedX;

            // ✅ 플레이어라면 0.5초 동안 충돌 무시
            if (tag == "player" && !ignoringPlayer)
            {
                StartCoroutine(TemporarilyIgnorePlayer(other));
            }
        }
    }

    private IEnumerator TemporarilyIgnorePlayer(Collider2D playerCol)
    {
        ignoringPlayer = true;
        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol && playerCol)
            Physics2D.IgnoreCollision(myCol, playerCol, true);

        yield return new WaitForSeconds(0.5f);

        if (myCol && playerCol)
            Physics2D.IgnoreCollision(myCol, playerCol, false);

        ignoringPlayer = false;
    }

    public bool TryReturnToObjectPool()
    {
        if (ObjectPool.Instance == null)
            return false;

        return ObjectPool.Instance.TryReturnActive(gameObject);
    }
}
