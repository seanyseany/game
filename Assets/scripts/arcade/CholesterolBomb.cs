using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class CholesterolBomb : MonoBehaviour, IReinitializable
{
    private const string ExplosionTriggerName = "bomb";
    private const float CleanupDelay = 0.5f;

    [Header("Cholesterol Bomb")]
    [Min(1)] [SerializeField] private int hitCount = 3;
    [SerializeField] private GameObject bombSmokePrefab;

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

    [Header("Lifetime")]
    [Min(0f)] [SerializeField] private float lifetime = 30f;

    [Header("Pooled Rage Obstacle Return")]
    public float pooledDespawnX = -25f;
    [SerializeField] private float collisionSkin = 0.02f;

    [Header("Collision Ignore")]
    [SerializeField] private string gateTag = "Gate";

    private Rigidbody2D cachedRigidbody;
    private Collider2D cachedCollider;
    private Collider2D[] cachedColliders;
    private Animator cachedAnimator;
    private Vector2 moveDir;
    private bool ignoringPlayer;
    private float spawnTime;
    private Vector3 initialLocalScale;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private SpriteRenderer[] cachedSpriteRenderers;
    private Animator[] cachedAnimators;
    private Sprite[] initialSprites;
    private Color[] initialSpriteColors;
    private Coroutine stretchRoutine;
    private Coroutine cleanupRoutine;
    private bool deathSequenceActive;
    private bool exploded;
    private int currentHitCount;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];

    private void Awake()
    {
        CacheComponents();

        if (cachedRigidbody != null)
        {
            cachedRigidbody.gravityScale = 0f;
            cachedRigidbody.bodyType = RigidbodyType2D.Kinematic;
            cachedRigidbody.freezeRotation = true;
        }

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
        CacheComponents();

        currentHitCount = 0;
        exploded = false;
        deathSequenceActive = false;
        ignoringPlayer = false;
        spawnTime = Time.time;
        ResetMoveDirection();

        if (cleanupRoutine != null)
        {
            StopCoroutine(cleanupRoutine);
            cleanupRoutine = null;
        }

        transform.localRotation = initialLocalRotation;

        if (IsPhaseOwnedObstacle())
            transform.localPosition = initialLocalPosition;

        transform.localScale = initialLocalScale;
        RestoreSpriteRenderers();
        StopStretchRoutine(resetScale: false);

        if (cachedAnimator != null)
        {
            cachedAnimator.ResetTrigger(ExplosionTriggerName);
            cachedAnimator.Rebind();
            cachedAnimator.Update(0f);
        }

        if (cachedRigidbody != null)
        {
            cachedRigidbody.linearVelocity = Vector2.zero;
            cachedRigidbody.angularVelocity = 0f;
        }

        if (cachedColliders != null)
        {
            for (int i = 0; i < cachedColliders.Length; i++)
            {
                if (cachedColliders[i] != null)
                    cachedColliders[i].enabled = true;
            }
        }

        IgnoreGateCollisions();
    }

    private void OnDisable()
    {
        if (cleanupRoutine != null)
        {
            StopCoroutine(cleanupRoutine);
            cleanupRoutine = null;
        }

        StopStretchRoutine(resetScale: true);
    }

    private void Update()
    {
        if (deathSequenceActive)
            return;

        if (HasLifetimeExpired())
        {
            DespawnWithoutExplosion();
            return;
        }

        MoveWithCollision(moveDir * Time.deltaTime);
        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        if (ShouldReturnToPool())
            TryReturnToObjectPool();
    }

    public void RegisterBulletHit()
    {
        if (exploded)
            return;

        currentHitCount++;
        if (currentHitCount < Mathf.Max(1, hitCount))
            return;

        Explode();
    }

    public void TriggerExplosionFromExternalHit()
    {
        Explode();
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

    public bool TryReturnToObjectPool()
    {
        if (ObjectPool.Instance == null)
            return false;

        if (IsPhaseOwnedObstacle())
            return false;

        return ObjectPool.Instance.TryReturnActive(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (exploded || other == null)
            return;

        if (IsGateCollider(other))
        {
            IgnoreCollisionWith(other);
            return;
        }

        BombHitBox bombHitBox = other.GetComponent<BombHitBox>() ?? other.GetComponentInParent<BombHitBox>();
        if (bombHitBox != null)
        {
            if (bombHitBox.affectsCholesterolBomb)
                Explode();
            return;
        }

        HandleBounceCollision(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null || exploded)
            return;

        if (IsGateCollider(collision.collider))
        {
            IgnoreCollisionWith(collision.collider);
            return;
        }

        HandleBounceCollision(collision.collider);
    }

    private void Explode()
    {
        if (exploded)
            return;

        exploded = true;

        MachineGunLastSpawnNotifier machineGunNotifier = GetComponent<MachineGunLastSpawnNotifier>() ?? GetComponentInParent<MachineGunLastSpawnNotifier>();
        if (machineGunNotifier != null)
            machineGunNotifier.NotifyDestroyTriggered();

        NotifyDeathStarted();

        if (cachedRigidbody != null)
        {
            cachedRigidbody.linearVelocity = Vector2.zero;
            cachedRigidbody.angularVelocity = 0f;
        }

        if (cachedColliders != null)
        {
            for (int i = 0; i < cachedColliders.Length; i++)
            {
                if (cachedColliders[i] != null)
                    cachedColliders[i].enabled = false;
            }
        }

        if (cachedAnimator != null)
            cachedAnimator.SetTrigger(ExplosionTriggerName);

        Quaternion spawnRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        SpawnBombSmoke(spawnRotation);

        cleanupRoutine = StartCoroutine(CoCleanupAfterExplosion());
    }

    private IEnumerator CoCleanupAfterExplosion()
    {
        if (CleanupDelay > 0f)
            yield return new WaitForSeconds(CleanupDelay);

        cleanupRoutine = null;

        if (TryReturnToObjectPool())
            yield break;

        gameObject.SetActive(false);
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

    private void DespawnWithoutExplosion()
    {
        if (exploded)
            return;

        exploded = true;
        NotifyDeathStarted();

        if (cachedRigidbody != null)
        {
            cachedRigidbody.linearVelocity = Vector2.zero;
            cachedRigidbody.angularVelocity = 0f;
        }

        if (TryReturnToObjectPool())
            return;

        gameObject.SetActive(false);
    }

    private IEnumerator TemporarilyIgnorePlayer(Collider2D playerCol)
    {
        ignoringPlayer = true;

        if (cachedCollider != null && playerCol != null)
            Physics2D.IgnoreCollision(cachedCollider, playerCol, true);

        yield return new WaitForSeconds(0.5f);

        if (cachedCollider != null && playerCol != null)
            Physics2D.IgnoreCollision(cachedCollider, playerCol, false);

        ignoringPlayer = false;
    }

    private void ResetMoveDirection()
    {
        moveDir = new Vector2(horizontalSpeed, verticalSpeed);
    }

    private bool ShouldReturnToPool()
    {
        if (ObjectPool.Instance == null)
            return false;

        if (IsPhaseOwnedObstacle())
            return false;

        if (transform.position.x <= pooledDespawnX)
            return true;

        return false;
    }

    private bool HasLifetimeExpired()
    {
        return lifetime > 0f && Time.time - spawnTime >= lifetime;
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
        int hitCountValue = cachedCollider.Cast(delta.normalized, filter, castHits, distance + Mathf.Max(0f, collisionSkin));

        RaycastHit2D nearestHit = new RaycastHit2D();
        bool foundBlockingHit = false;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hitCountValue; i++)
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

        float newYDir = myY > otherY ? 1f : -1f;
        moveDir.y = newYDir * Mathf.Max(0.01f, Mathf.Abs(moveDir.y)) * bounceFactor;
        moveDir.x = horizontalSpeed;

        if (other.CompareTag("player") && !ignoringPlayer)
            StartCoroutine(TemporarilyIgnorePlayer(other));
    }

    private static bool ShouldBounceFrom(Collider2D other)
    {
        if (other == null)
            return false;

        string tag = other.tag;
        return tag == "floor" || tag == "ceiling" || tag == "platform" || tag == "player";
    }

    private void IgnoreGateCollisions()
    {
        if (cachedColliders == null || cachedColliders.Length == 0)
            return;

        GateHealth[] gates = FindObjectsByType<GateHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (gates == null || gates.Length == 0)
            return;

        for (int gateIndex = 0; gateIndex < gates.Length; gateIndex++)
        {
            Collider2D[] gateColliders = gates[gateIndex].GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < gateColliders.Length; i++)
            {
                Collider2D gateCollider = gateColliders[i];
                if (!IsGateCollider(gateCollider))
                    continue;

                IgnoreCollisionWith(gateCollider);
            }
        }
    }

    private void IgnoreCollisionWith(Collider2D other)
    {
        if (other == null || cachedColliders == null)
            return;

        for (int i = 0; i < cachedColliders.Length; i++)
        {
            Collider2D ownCollider = cachedColliders[i];
            if (ownCollider != null)
                Physics2D.IgnoreCollision(ownCollider, other, true);
        }
    }

    private bool IsGateCollider(Collider2D other)
    {
        if (other == null)
            return false;

        return other.CompareTag(gateTag) || other.GetComponent<GateHealth>() != null || other.GetComponentInParent<GateHealth>() != null;
    }

    private void SpawnBombSmoke(Quaternion spawnRotation)
    {
        GameObject spawned = SpawnConfiguredPrefab(bombSmokePrefab, transform.position, spawnRotation, out string poolTag);
        if (spawned == null)
            return;

        CameraShakeManager.ShakeDefault();

        SmokeMover smokeMover = spawned.GetComponent<SmokeMover>();
        if (smokeMover != null)
            smokeMover.ConfigurePooling(!string.IsNullOrEmpty(poolTag), poolTag);

        BombHitBox bombHitBox = spawned.GetComponent<BombHitBox>();
        if (bombHitBox != null)
        {
            bombHitBox.affectsCholesterolBomb = false;

            if (!string.IsNullOrEmpty(poolTag))
                bombHitBox.poolTag = poolTag;

            bombHitBox.ActivateAfterDelay(0.2f);
        }

        AnimatorAutoDespawn autoDespawn = spawned.GetComponent<AnimatorAutoDespawn>();
        if (autoDespawn == null)
            autoDespawn = spawned.AddComponent<AnimatorAutoDespawn>();
        autoDespawn.ConfigurePooling(!string.IsNullOrEmpty(poolTag), poolTag);

        EnsureTriggerRigidbody(spawned);
    }

    private GameObject SpawnConfiguredPrefab(GameObject prefab, Vector3 position, Quaternion rotation, out string poolTag)
    {
        poolTag = ResolvePoolTag(prefab);
        if (!string.IsNullOrEmpty(poolTag) && ObjectPool.Instance != null && ObjectPool.Instance.HasPool(poolTag))
            return ObjectPool.Instance.SpawnFromPool(poolTag, position, rotation);

        return prefab != null ? Instantiate(prefab, position, rotation) : null;
    }

    private static string ResolvePoolTag(GameObject prefab)
    {
        if (prefab == null || ObjectPool.Instance == null || ObjectPool.Instance.pools == null)
            return string.Empty;

        for (int i = 0; i < ObjectPool.Instance.pools.Count; i++)
        {
            Pool pool = ObjectPool.Instance.pools[i];
            if (pool != null && pool.prefab == prefab)
                return pool.tag;
        }

        return string.Empty;
    }

    private static void EnsureTriggerRigidbody(GameObject target)
    {
        if (target == null)
            return;

        Rigidbody2D body = target.GetComponent<Rigidbody2D>();
        if (body == null)
            body = target.AddComponent<Rigidbody2D>();

        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.simulated = true;
    }

    private void CacheComponents()
    {
        if (cachedAnimator == null)
            cachedAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (cachedRigidbody == null)
            cachedRigidbody = GetComponent<Rigidbody2D>();

        if (cachedCollider == null)
            cachedCollider = GetComponent<Collider2D>();

        cachedColliders = GetComponentsInChildren<Collider2D>(true);
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
            SpriteRenderer spriteRenderer = cachedSpriteRenderers[i];
            if (spriteRenderer == null)
                continue;

            initialSprites[i] = spriteRenderer.sprite;
            initialSpriteColors[i] = spriteRenderer.color;
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
            SpriteRenderer spriteRenderer = cachedSpriteRenderers[i];
            if (spriteRenderer == null)
                continue;

            spriteRenderer.enabled = true;

            if (initialSprites != null && i < initialSprites.Length && initialSprites[i] != null)
                spriteRenderer.sprite = initialSprites[i];

            Color color = i < initialSpriteColors.Length ? initialSpriteColors[i] : spriteRenderer.color;
            color.a = 1f;
            spriteRenderer.color = color;
        }
    }
}
