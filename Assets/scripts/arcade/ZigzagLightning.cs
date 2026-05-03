using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class ZigzagLightning : MonoBehaviour
{
    private struct BossTargetRefs
    {
        public Boss boss;
        public BossSlime bossSlime;
    }

    [Header("Settings")]
    public float life = 0.5f;
    [HideInInspector] public int damage;

    [Header("Animation")]
    public Sprite[] lightningFrames;
    public float frameRate = 0.15f;
    [SerializeField] private bool colliderEnabledOnSpawn = true;
    [SerializeField] private bool usePool = true;
    [SerializeField] private string poolTag = "";

    private SpriteRenderer sr;
    private BoxCollider2D hitCollider;
    private int currentFrame = 0;
    private float timer = 0f;
    private readonly HashSet<GameObject> hitTargets = new HashSet<GameObject>();
    private static readonly Dictionary<int, BossTargetRefs> bossTargetCache = new Dictionary<int, BossTargetRefs>(64);
    private Coroutine lifeCo;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.simulated = true;

        hitCollider = GetComponent<BoxCollider2D>();
        hitCollider.isTrigger = true;
    }

    private void OnEnable()
    {
        timer = 0f;
        currentFrame = 0;
        hitTargets.Clear();
        if (hitCollider != null)
            hitCollider.enabled = colliderEnabledOnSpawn;
        if (sr != null && lightningFrames != null && lightningFrames.Length > 0)
            sr.sprite = lightningFrames[0];

        if (lifeCo != null) StopCoroutine(lifeCo);
        lifeCo = StartCoroutine(CoLife());
    }

    private void OnDisable()
    {
        if (lifeCo != null)
        {
            StopCoroutine(lifeCo);
            lifeCo = null;
        }
    }

    private void Update()
    {
        if (lightningFrames != null && lightningFrames.Length > 0)
        {
            timer += Time.deltaTime;
            if (timer >= frameRate)
            {
                timer = 0f;
                currentFrame = (currentFrame + 1) % lightningFrames.Length;
                sr.sprite = lightningFrames[currentFrame];
            }
        }
    }

    public void SetOrientation(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, ang);
    }

    public void ConfigurePooling(bool enablePool, string tag)
    {
        usePool = enablePool;
        poolTag = tag;
    }

    public void ActivateDamageHitbox(int damageValue, float lifeSeconds, bool enablePool, string tag)
    {
        damage = damageValue;
        life = Mathf.Max(0.01f, lifeSeconds);
        usePool = enablePool;
        poolTag = tag;

        if (hitCollider != null)
            hitCollider.enabled = true;

        if (!isActiveAndEnabled)
            return;

        if (lifeCo != null) StopCoroutine(lifeCo);
        lifeCo = StartCoroutine(CoLife());
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ResolveBossTargets(other, out var boss, out var bossSlime);

        bool isBoss = (boss != null) || (bossSlime != null) || other.CompareTag("Boss");
        bool isEnemy = other.CompareTag("Enemy-Slime") || other.CompareTag("Enemy-Vacteria");
        if (!isEnemy && !isBoss) return;

        GameObject hitRoot = bossSlime != null
            ? bossSlime.gameObject
            : (boss != null ? boss.gameObject : other.gameObject);

        if (hitTargets.Contains(hitRoot)) return;
        hitTargets.Add(hitRoot);

        if (bossSlime != null)
        {
            // Boss/BossSlime은 공격 stat(damage)을 쓰지 않고
            // "기본 1타 * 플레이어 배율"만 적용한다.
            float finalDamage = GetBossDamageMultiplier();
            bossSlime.TakeDamage(finalDamage);
            return;
        }

        if (boss != null)
        {
            // Boss/BossSlime은 공격 stat(damage)을 쓰지 않고
            // "기본 1타 * 플레이어 배율"만 적용한다.
            float finalDamage = GetBossDamageMultiplier();
            boss.TakeDamage(finalDamage);
            return;
        }

        int finalDamageInt = damage;
        var monster = other.GetComponent<Monster>() ?? other.GetComponentInParent<Monster>();
        if (monster != null)
        {
            monster.Hit(finalDamageInt);
            return;
        }

        other.gameObject.SendMessage("Hit", finalDamageInt, SendMessageOptions.DontRequireReceiver);
    }

    private static void ResolveBossTargets(Collider2D other, out Boss boss, out BossSlime bossSlime)
    {
        boss = null;
        bossSlime = null;
        if (other == null) return;

        Transform root = other.transform.root;
        if (root != null)
        {
            int rootId = root.GetInstanceID();
            if (bossTargetCache.TryGetValue(rootId, out var cached))
            {
                boss = cached.boss;
                bossSlime = cached.bossSlime;
                return;
            }
        }

        boss = other.GetComponent<Boss>() ?? other.GetComponentInParent<Boss>();
        bossSlime = other.GetComponent<BossSlime>() ?? other.GetComponentInParent<BossSlime>();

        if (root != null)
        {
            if (boss == null) boss = root.GetComponent<Boss>() ?? root.GetComponentInChildren<Boss>(true);
            if (bossSlime == null) bossSlime = root.GetComponent<BossSlime>() ?? root.GetComponentInChildren<BossSlime>(true);
            bossTargetCache[root.GetInstanceID()] = new BossTargetRefs { boss = boss, bossSlime = bossSlime };
        }
    }

    private static float GetBossDamageMultiplier()
    {
        int t = (GameData.Instance != null) ? GameData.Instance.selectedPlayerType : 2;
        switch (t)
        {
            case 1: return 2.9f;
            case 2: return 1.46f;
            case 3: return 1.46f;
            case 4: return 1.5f;
            case 5: return 5.4f;
            default: return 1f;
        }
    }

    private System.Collections.IEnumerator CoLife()
    {
        yield return new WaitForSeconds(life);
        lifeCo = null;
        Despawn();
    }

    private void Despawn()
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }
}
