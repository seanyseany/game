using UnityEngine;
using System.Collections;
using UnityEngine.Serialization;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class ObstacleMover : MonoBehaviour, IReinitializable
{
    [Header("Movement Settings")]
    [FormerlySerializedAs("speedX")]
    public float horizontalSpeed = -3.5f;
    public float verticalSpeed = 0f;
    public float bounceFactor = 1f;
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

    [Header("Gate Collision")]
    [SerializeField] private string gateTag = "Gate";
    [SerializeField] private string floorTag = "floor";
    [SerializeField] private string platformTag = "platform";
    [SerializeField] private string ceilingTag = "ceiling";
    [Min(0)] [SerializeField] private int gateOxygenPenalty = 1;

    private Rigidbody2D rb;
    private Collider2D cachedCollider;
    private Obstacle obstacle;
    private BulletObstacle bulletObstacle;
    private Vector2 moveDir;
    private bool ignoringPlayer = false;
    private float spawnTime;
    private Vector3 initialLocalScale;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private SpriteRenderer[] cachedSpriteRenderers;
    private Animator[] cachedAnimators;
    private Sprite[] initialSprites;
    private Color[] initialSpriteColors;
    private Coroutine stretchRoutine;
    private bool deathSequenceActive;
    private bool destroySequenceRequested;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        cachedCollider = GetComponent<Collider2D>();
        obstacle = GetComponent<Obstacle>();
        bulletObstacle = GetComponent<BulletObstacle>();
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
        destroySequenceRequested = false;
        ResetMoveDirection();

        transform.localRotation = initialLocalRotation;

        if (IsPhaseOwnedObstacle())
        {
            transform.localPosition = initialLocalPosition;
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
        moveDir = new Vector2(horizontalSpeed, verticalSpeed);
    }

    void Update()
    {
        if (deathSequenceActive)
            return;

        MoveWithCollision(moveDir * Time.deltaTime);
        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        if (ShouldReturnToPool())
            TriggerDestroyOrDespawn();
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
        if (TryHandleGateCollision(other))
            return;

        if (!ShouldBounceFrom(other))
        {
            IgnoreCollisionWith(other);
            return;
        }

        HandleBounceCollision(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null)
            return;

        if (TryHandleGateCollision(collision.collider))
            return;

        if (!ShouldBounceFrom(collision.collider))
        {
            IgnoreCollisionWith(collision.collider);
            return;
        }

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

    private void TriggerDestroyOrDespawn()
    {
        if (destroySequenceRequested)
            return;

        destroySequenceRequested = true;

        if (obstacle == null)
            obstacle = GetComponent<Obstacle>();

        if (obstacle != null)
        {
            obstacle.TriggerDestroySequence();
            return;
        }

        if (bulletObstacle == null)
            bulletObstacle = GetComponent<BulletObstacle>();

        if (bulletObstacle != null)
        {
            bulletObstacle.TriggerDestroySequence();
            return;
        }

        if (TryReturnToObjectPool())
            return;

        gameObject.SetActive(false);
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
            transform.Translate(delta, Space.World);
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
            transform.Translate(delta, Space.World);
            return;
        }

        float moveDistance = Mathf.Max(0f, nearestHit.distance - Mathf.Max(0f, collisionSkin));
        transform.Translate(delta.normalized * moveDistance, Space.World);
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
        moveDir.x = horizontalSpeed;

        if (other.CompareTag("player") && !ignoringPlayer)
            StartCoroutine(TemporarilyIgnorePlayer(other));
    }

    private bool ShouldBounceFrom(Collider2D other)
    {
        if (other == null)
            return false;

        string tag = other.tag;
        return tag == floorTag || tag == platformTag || tag == ceilingTag;
    }

    private bool TryHandleGateCollision(Collider2D other)
    {
        if (!IsGate(other))
            return false;

        if (destroySequenceRequested)
            return true;

        if (GameData.Instance != null && gateOxygenPenalty > 0)
            GameData.Instance.SpendO2(gateOxygenPenalty);

        TriggerDestroyOrDespawn();
        return true;
    }

    private bool IsGate(Collider2D other)
    {
        if (other == null)
            return false;

        return other.CompareTag(gateTag) || other.GetComponent<GateHealth>() != null || other.GetComponentInParent<GateHealth>() != null;
    }

    private void IgnoreCollisionWith(Collider2D other)
    {
        if (other == null || cachedCollider == null)
            return;

        Physics2D.IgnoreCollision(cachedCollider, other, true);
    }

    private void CacheSpriteRenderers()
    {
        cachedSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        cachedAnimators = GetComponentsInChildren<Animator>(true);
        int count = cachedSpriteRenderers != null ? cachedSpriteRenderers.Length : 0;
        initialSprites = new Sprite[count];
        initialSpriteColors = new Color[count];

        for (int i = 0; i < count; i++)
        {
            SpriteRenderer sr = cachedSpriteRenderers[i];
            if (sr == null)
                continue;

            initialSprites[i] = sr.sprite;
            initialSpriteColors[i] = sr.color;
        }
    }

    private void RestoreSpriteRenderers()
    {
        if (cachedSpriteRenderers == null || initialSpriteColors == null)
            CacheSpriteRenderers();

        if (cachedAnimators != null)
        {
            for (int i = 0; i < cachedAnimators.Length; i++)
            {
                Animator animator = cachedAnimators[i];
                if (animator == null)
                    continue;

                animator.Rebind();
                animator.Update(0f);
            }
        }

        int count = cachedSpriteRenderers != null ? cachedSpriteRenderers.Length : 0;
        for (int i = 0; i < count; i++)
        {
            SpriteRenderer sr = cachedSpriteRenderers[i];
            if (sr == null)
                continue;

            sr.enabled = true;

            if (initialSprites != null && i < initialSprites.Length && initialSprites[i] != null)
                sr.sprite = initialSprites[i];

            Color color = i < initialSpriteColors.Length ? initialSpriteColors[i] : sr.color;
            color.a = 1f;
            sr.color = color;
        }
    }
}
