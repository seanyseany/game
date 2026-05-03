using UnityEngine;

public class FakeBall : MonoBehaviour
{
    public float speed = 12f;
    public float life = 3f;
    public int damage = 1;

    public Rigidbody2D rb;
    public SpriteRenderer sr;     
    public bool faceDirection = true; // 이동 방향으로 회전할지

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!sr) sr = GetComponent<SpriteRenderer>();
    }

    public void Fire(Vector2 origin, Vector2 dir, float? overrideSpeed = null)
    {
        transform.position = origin;
        rb.linearVelocity = (overrideSpeed ?? speed) * dir.normalized;
        CancelInvoke(); Invoke(nameof(Despawn), life);
    }

    void Update()
    {
        if (faceDirection && rb.linearVelocity.sqrMagnitude > 0.0001f)
        {
            var v = rb.linearVelocity.normalized;
            float ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, ang);
        }
    }

    // ❌ Enemy에는 반응하지 않음
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("floor"))
        {
            Despawn();
        }
    }

    // ❌ Enemy에는 반응하지 않음
    private void OnCollisionEnter2D(Collision2D col)
    {
        if (col.collider.CompareTag("floor"))
        {
            Despawn();
        }
    }

    void Despawn() => Destroy(gameObject);
}
