using UnityEngine;

public class ProjectileBall : MonoBehaviour
{
    public float speed = 12f;
    public float life = 3f;
    public int damage = 1;
    public GameObject hitboxPrefab; 

    public Rigidbody2D rb;
    public SpriteRenderer sr;     
    public bool faceDirection = true; 
    private Player owner;
    // 🔹 추가: 스모크 프리팹 (Player에서 할당)
    [HideInInspector] public GameObject smokePrefab;
    [SerializeField] private bool usePool = false;
    [SerializeField] private string poolTag = "";
    [SerializeField] private string smokePoolTag = "";
    private bool isDespawning = false;
    private Vector2 lastPhysicsPos;
    private Collider2D selfCollider;
    private static readonly RaycastHit2D[] linecastBuffer = new RaycastHit2D[8];
    private static readonly Collider2D[] nearbyEnemyBuffer = new Collider2D[8];
    private static readonly Collider2D[] nearbyEnemyBuffer2 = new Collider2D[8];
    private static readonly RaycastHit2D[] castBuffer = new RaycastHit2D[8];
    private ContactFilter2D castFilter;
    private bool castFilterInitialized = false;
    private bool pendingSurfaceHit = false;
    private Vector2 pendingSurfacePos = Vector2.zero;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!sr) sr = GetComponent<SpriteRenderer>();
        if (!selfCollider) selfCollider = GetComponent<Collider2D>();

        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        castFilter.useLayerMask = false;
        castFilter.useTriggers = true;
        castFilterInitialized = true;
    }

    void OnEnable()
    {
        isDespawning = false;
        pendingSurfaceHit = false;
        pendingSurfacePos = Vector2.zero;
        if (!selfCollider) selfCollider = GetComponent<Collider2D>();
        if (selfCollider != null) selfCollider.enabled = true;
        lastPhysicsPos = rb != null ? rb.position : (Vector2)transform.position;
    }

    void OnDisable()
    {
        CancelInvoke();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        isDespawning = false;
        pendingSurfaceHit = false;
    }

    public void Fire(Vector2 origin, Vector2 dir, float? overrideSpeed = null)
    {
        isDespawning = false;
        pendingSurfaceHit = false;
        transform.position = new Vector3(origin.x, origin.y, -0.5f);
        transform.rotation = Quaternion.identity;
        rb.linearVelocity = (overrideSpeed ?? speed) * dir.normalized;
        lastPhysicsPos = rb.position;
        CancelInvoke(); Invoke(nameof(Despawn), life);
        // ✅ 공격용 히트박스 추가
        if (hitboxPrefab != null)
        {
            bool hbFromPool = false;
            var hb = SpawnUsingPool(hitboxPrefab, "ProjectileHitbox", transform.position, Quaternion.identity, out hbFromPool, 6);
            if (hb == null) return;
            hb.transform.parent = transform;

            var hbComp = hb.GetComponent<Hitbox>();
            if (hbComp)
            {
                hbComp.damage = damage;
                hbComp.SetOwner(owner);
                hbComp.ConfigurePooling(hbFromPool, "ProjectileHitbox");
                hbComp.lifeTime = life;
            }
        }
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

    void OnTriggerEnter2D(Collider2D other)
    {
        HandleTriggerContact(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        HandleTriggerContact(other);
    }

    void FixedUpdate()
    {
        if (isDespawning || rb == null) return;

        Vector2 now = rb.position;
        Vector2 delta = now - lastPhysicsPos;

        if (delta.sqrMagnitude > 0.0001f && castFilterInitialized && selfCollider != null)
        {
            int castCount = rb.Cast(delta.normalized, castFilter, castBuffer, delta.magnitude + 0.05f);
            for (int i = 0; i < castCount; i++)
            {
                var col = castBuffer[i].collider;
                if (col == null || col == selfCollider) continue;
                if (TryHandleEnemyOnly(col))
                {
                    lastPhysicsPos = now;
                    return;
                }
            }
        }

        if (delta.sqrMagnitude > 0.0001f)
        {
            int hitCount = Physics2D.LinecastNonAlloc(lastPhysicsPos, now, linecastBuffer);

            // 1) 같은 프레임 라인 충돌에서는 적을 최우선 처리
            for (int i = 0; i < hitCount; i++)
            {
                var col = linecastBuffer[i].collider;
                if (col == null || col == selfCollider) continue;
                if (TryHandleEnemyOnly(col))
                {
                    lastPhysicsPos = now;
                    return;
                }
            }

            // 2) 적이 없을 때만 바닥/플랫폼 등 일반 처리
            for (int i = 0; i < hitCount; i++)
            {
                var col = linecastBuffer[i].collider;
                if (col == null || col == selfCollider) continue;
                if (TryHandleCollision(col)) break;
            }
        }

        if (!isDespawning && pendingSurfaceHit)
        {
            pendingSurfaceHit = false;
            if (TryHitNearbyEnemy())
            {
                Despawn();
            }
            else
            {
                SpawnSmoke(pendingSurfacePos);
                Despawn();
            }
        }
        lastPhysicsPos = now;
    }

    private bool TryHandleCollision(Collider2D other)
    {
        if (isDespawning || other == null) return false;

        // 장애물은 절대 적으로 처리하지 않는다.
        if (IsObstacleCollider(other))
            return false;

        if (TryApplyEnemyDamage(other))
        {
            Despawn();
            return true;
        }

        // floor/platform은 적 판정 다음으로 처리
        if (IsSmokeSurface(other))
        {
            pendingSurfaceHit = true;
            pendingSurfacePos = other.ClosestPoint(transform.position);
            return true;
        }

        return false;
    }

    private bool TryHitNearbyEnemy()
    {
        const float radius = 0.38f;

        // 현재 위치 1차 검사
        int count = Physics2D.OverlapCircleNonAlloc(transform.position, radius, nearbyEnemyBuffer);
        for (int i = 0; i < count; i++)
        {
            var col = nearbyEnemyBuffer[i];
            if (col == null || col == selfCollider) continue;
            if (IsObstacleCollider(col)) continue;

            if (TryApplyEnemyDamage(col)) return true;
        }

        // 이전~현재 중간점 2차 검사 (바닥과 거의 동시에 스칠 때 보정)
        Vector2 mid = ((Vector2)transform.position + lastPhysicsPos) * 0.5f;
        int count2 = Physics2D.OverlapCircleNonAlloc(mid, radius, nearbyEnemyBuffer2);
        for (int i = 0; i < count2; i++)
        {
            var col = nearbyEnemyBuffer2[i];
            if (col == null || col == selfCollider) continue;
            if (IsObstacleCollider(col)) continue;

            if (TryApplyEnemyDamage(col)) return true;
        }
        return false;
    }

    private bool TryHandleEnemyOnly(Collider2D other)
    {
        if (isDespawning || other == null) return false;
        if (IsObstacleCollider(other)) return false;

        if (!TryApplyEnemyDamage(other)) return false;
        Despawn();
        return true;
    }

    private bool TryApplyEnemyDamage(Collider2D other)
    {
        if (other == null) return false;

        var enemyRoot = ResolveEnemyRoot(other.transform);
        if (enemyRoot == null) return false;

        var monster = enemyRoot.GetComponent<Monster>() ?? enemyRoot.GetComponentInChildren<Monster>(true);
        if (monster != null)
        {
            monster.Hit(damage);
            return true;
        }

        enemyRoot.gameObject.SendMessage("Hit", damage, SendMessageOptions.DontRequireReceiver);
        return true;
    }

    private static Transform ResolveEnemyRoot(Transform t)
    {
        while (t != null)
        {
            if (IsEnemyTag(t.tag))
                return t;
            t = t.parent;
        }
        return null;
    }

    private static bool IsEnemyTag(string tag)
    {
        return tag == "Enemy" || tag == "Enemy-Slime" || tag == "Enemy-Vacteria";
    }

    private void HandleTriggerContact(Collider2D other)
    {
        if (isDespawning || other == null) return;

        // Trigger 단계에서는 적 판정만 즉시 처리
        if (TryHandleEnemyOnly(other)) return;

        if (IsObstacleCollider(other)) return;

        // 바닥/플랫폼은 즉시 소멸하지 않고 물리 프레임 끝에서 재검사 후 처리
        if (IsSmokeSurface(other))
        {
            pendingSurfaceHit = true;
            pendingSurfacePos = other.ClosestPoint(transform.position);
        }
    }

    private static bool IsSmokeSurface(Collider2D other)
    {
        if (other == null) return false;
        if (HasTagInHierarchy(other, "floor")) return true;
        if (HasTagInHierarchy(other, "platform")) return true;
        if (IsObstacleCollider(other)) return false;
        return false;
    }

    private static bool IsObstacleCollider(Collider2D other)
    {
        if (other == null) return false;

        if (other.CompareTag("Obstacle")) return true;
        if (other.GetComponent<Obstacle>() != null || other.GetComponent<ObstacleInfo>() != null) return true;

        Transform parent = other.transform.parent;
        if (parent != null)
        {
            if (parent.CompareTag("Obstacle")) return true;
            if (parent.GetComponent<Obstacle>() != null || parent.GetComponent<ObstacleInfo>() != null) return true;
        }

        Transform root = other.transform.root;
        if (root != null)
        {
            if (root.CompareTag("Obstacle")) return true;
            if (root.GetComponent<Obstacle>() != null || root.GetComponent<ObstacleInfo>() != null) return true;
        }

        return false;
    }

    private static bool HasTagInHierarchy(Collider2D other, string tag)
    {
        if (other == null || string.IsNullOrEmpty(tag)) return false;
        if (other.CompareTag(tag)) return true;

        Transform parent = other.transform.parent;
        if (parent != null && parent.CompareTag(tag)) return true;

        Transform root = other.transform.root;
        if (root != null && root.CompareTag(tag)) return true;

        return false;
    }

    private void SpawnSmoke(Vector2 pos)
    {
        // 🔹 Player가 Rage 중이면 smokePrefab 생성 막기
        if (owner != null && owner.IsRageModeActive())
            return;

        if (smokePrefab != null)
        {
            Vector3 spawnPos = new Vector3(pos.x, pos.y, -0.5f); // 👈 z 고정
            bool fromPool;
            var smoke = SpawnUsingPool(smokePrefab, smokePoolTag, spawnPos, Quaternion.identity, out fromPool, 8);
            if (smoke != null)
            {
                var mover = smoke.GetComponent<SmokeMover>();
                if (mover != null)
                    mover.ConfigurePooling(fromPool, smokePoolTag);
            }
        }
    }
    public void SetOwner(Player p)
    {
        owner = p;
    }

    public void ConfigurePooling(bool enablePool, string tag)
    {
        usePool = enablePool;
        poolTag = tag;
    }

    public void ConfigureSmokePooling(string tag)
    {
        smokePoolTag = tag;
    }

    private GameObject SpawnUsingPool(GameObject prefab, string tag, Vector3 pos, Quaternion rot, out bool spawnedFromPool, int initialSize = 0)
    {
        spawnedFromPool = false;
        if (prefab == null) return null;

        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(tag))
        {
            if (!ObjectPool.Instance.HasPool(tag))
                ObjectPool.Instance.RegisterPool(tag, prefab, initialSize);

            if (ObjectPool.Instance.HasPool(tag))
            {
                var pooled = ObjectPool.Instance.SpawnFromPool(tag, pos, rot);
                if (pooled != null)
                {
                    spawnedFromPool = true;
                    return pooled;
                }
            }
        }

        return Instantiate(prefab, pos, rot);
    }

    void Despawn()
    {
        if (isDespawning) return;
        isDespawning = true;
        pendingSurfaceHit = false;

        CancelInvoke();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }
}
