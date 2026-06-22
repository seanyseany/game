using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class ObstacleMover : MonoBehaviour, IReinitializable
{
    [Header("Movement Settings")]
    public float speedX = -3.5f; // ✅ 오른쪽→왼쪽 속도
    public float angleRange = 35f;
    public float bounceFactor = 1f;
    public float maxVerticalSpeed = 3f;
    public float rotationSpeed = 90f;

    [Header("Bullet Reaction")]
    [SerializeField] private float stretchAmountX = 0.5f;
    [SerializeField] private float stretchAmountY = 1f;
    [SerializeField] private float stretchSpeed = 4f;
    [SerializeField] private float stretchDuration = 1f;

    [Header("Pooled Rage Obstacle Return")]
    public float pooledLifetime = 5f;
    public float pooledDespawnX = -25f;
    [SerializeField] private float collisionSkin = 0.02f;

    private Rigidbody2D rb;
    private Collider2D cachedCollider;
    private Vector2 moveDir;
    private bool ignoringPlayer = false;
    private float spawnTime;
    private Vector3 initialLocalScale;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private SpriteRenderer[] cachedSpriteRenderers;
    private Color[] initialSpriteColors;
    private Coroutine stretchRoutine;
    private bool deathSequenceActive;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        cachedCollider = GetComponent<Collider2D>();
        rb.gravityScale = 0;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.freezeRotation = true;
        initialLocalScale = transform.localScale;
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        CacheSpriteRenderers();
        ResetMoveDirection();
    }

    private void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        spawnTime = Time.time;
        deathSequenceActive = false;
        ResetMoveDirection();

        if (IsPhaseOwnedObstacle())
        {
            transform.localPosition = initialLocalPosition;
            transform.localRotation = initialLocalRotation;
        }

        transform.localScale = initialLocalScale;
        RestoreSpriteRenderers();

        StopStretchRoutine(resetScale: false);
    }

    private void OnDisable()
    {
        StopStretchRoutine(resetScale: true);
    }

    private void ResetMoveDirection()
    {
        // speedX 크기를 기준으로 이동 각도를 랜덤화한다.
        float clampedAngleRange = Mathf.Clamp(angleRange, 0f, 89f);
        float angleDeg = Random.Range(-clampedAngleRange, clampedAngleRange);
        float angleRad = angleDeg * Mathf.Deg2Rad;

        float totalSpeed = Mathf.Abs(speedX);
        moveDir = new Vector2(-totalSpeed * Mathf.Cos(angleRad),
                            totalSpeed * Mathf.Sin(angleRad));

        // 세로 속도 상한 적용
        moveDir.y = Mathf.Clamp(moveDir.y, -maxVerticalSpeed, maxVerticalSpeed);
    }


    void Update()
    {
        if (deathSequenceActive)
            return;

        MoveWithCollision(moveDir * Time.deltaTime);
        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        // Y속도 제한
        moveDir.y = Mathf.Clamp(moveDir.y, -maxVerticalSpeed, maxVerticalSpeed);

        if (ShouldReturnToPool())
            TryReturnToObjectPool();
    }

    private bool ShouldReturnToPool()
    {
        if (ObjectPool.Instance == null)
            return false;

        if (IsPhaseOwnedObstacle())
            return false;

        if (transform.position.x <= pooledDespawnX)
            return true;

        return pooledLifetime > 0f && Time.time - spawnTime >= pooledLifetime;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleBounceCollision(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null)
            return;

        HandleBounceCollision(collision.collider);
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

    public bool ReactToMachineGunBullet()
    {
        if (deathSequenceActive)
            return false;

        if (stretchRoutine != null)
            StopCoroutine(stretchRoutine);

        stretchRoutine = StartCoroutine(StretchRoutine());
        return true;
    }

    public void NotifyDeathStarted()
    {
        deathSequenceActive = true;
        moveDir = Vector2.zero;
        StopStretchRoutine(resetScale: true);
    }

    private IEnumerator StretchRoutine()
    {
        float elapsed = 0f;

        while (elapsed < stretchDuration)
        {
            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, stretchDuration));
            float damping = 1f - normalized;
            float wave = Mathf.Sin(elapsed * stretchSpeed * Mathf.PI * 2f) * damping;

            float scaleX = initialLocalScale.x + (wave * stretchAmountX);
            float scaleY = initialLocalScale.y - (wave * stretchAmountY);
            transform.localScale = new Vector3(scaleX, scaleY, initialLocalScale.z);

            yield return null;
        }

        transform.localScale = initialLocalScale;
        stretchRoutine = null;
    }

    public bool TryReturnToObjectPool()
    {
        if (ObjectPool.Instance == null)
            return false;

        if (IsPhaseOwnedObstacle())
            return false;

        return ObjectPool.Instance.TryReturnActive(gameObject);
    }

    private void StopStretchRoutine(bool resetScale)
    {
        if (stretchRoutine != null)
        {
            StopCoroutine(stretchRoutine);
            stretchRoutine = null;
        }

        if (resetScale)
            transform.localScale = initialLocalScale;
    }

    private bool IsPhaseOwnedObstacle()
    {
        return GetComponentInParent<PhaseLayoutSnapshot>(true) != null;
    }

    private void MoveWithCollision(Vector2 delta)
    {
        if (cachedCollider == null)
        {
            transform.Translate(delta);
            return;
        }

        float distance = delta.magnitude;
        if (distance <= Mathf.Epsilon)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = false;
        filter.useDepth = false;
        int hitCount = cachedCollider.Cast(delta.normalized, filter, castHits, distance + Mathf.Max(0f, collisionSkin));

        RaycastHit2D nearestHit = new RaycastHit2D();
        bool foundBlockingHit = false;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = castHits[i];
            Collider2D hitCollider = hit.collider;
            if (hitCollider == null || !ShouldBounceFrom(hitCollider))
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearestHit = hit;
                foundBlockingHit = true;
            }
        }

        if (!foundBlockingHit)
        {
            transform.Translate(delta);
            return;
        }

        float moveDistance = Mathf.Max(0f, nearestHit.distance - Mathf.Max(0f, collisionSkin));
        transform.Translate(delta.normalized * moveDistance);
        HandleBounceCollision(nearestHit.collider);
    }

    private void HandleBounceCollision(Collider2D other)
    {
        if (!ShouldBounceFrom(other))
            return;

        float otherY = other.bounds.center.y;
        float myY = transform.position.y;

        float newYDir = (myY > otherY) ? 1f : -1f;
        moveDir.y = newYDir * Mathf.Max(0.01f, Mathf.Abs(moveDir.y)) * bounceFactor;
        moveDir.x = speedX;

        if (other.CompareTag("player") && !ignoringPlayer)
            StartCoroutine(TemporarilyIgnorePlayer(other));
    }

    private static bool ShouldBounceFrom(Collider2D other)
    {
        if (other == null)
            return false;

        string tag = other.tag;
        return tag == "floor" || tag == "ceiling" || tag == "platform" || tag == "Obstacle" || tag == "player";
    }

    private void CacheSpriteRenderers()
    {
        cachedSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        int count = cachedSpriteRenderers != null ? cachedSpriteRenderers.Length : 0;
        initialSpriteColors = new Color[count];

        for (int i = 0; i < count; i++)
        {
            SpriteRenderer sr = cachedSpriteRenderers[i];
            if (sr == null)
                continue;

            initialSpriteColors[i] = sr.color;
        }
    }

    private void RestoreSpriteRenderers()
    {
        if (cachedSpriteRenderers == null || initialSpriteColors == null)
            CacheSpriteRenderers();

        int count = cachedSpriteRenderers != null ? cachedSpriteRenderers.Length : 0;
        for (int i = 0; i < count; i++)
        {
            SpriteRenderer sr = cachedSpriteRenderers[i];
            if (sr == null)
                continue;

            sr.enabled = true;

            Color color = i < initialSpriteColors.Length ? initialSpriteColors[i] : sr.color;
            color.a = 1f;
            sr.color = color;
        }
    }
}
