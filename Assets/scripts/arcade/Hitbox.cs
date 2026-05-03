using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Hitbox : MonoBehaviour
{
    private struct BossTargetRefs
    {
        public Boss boss;
        public BossSlime bossSlime;
    }

    public int damage = 1;
    public float lifeTime = 0.2f;
    public Player Owner;
    [SerializeField] private bool usePool = true;
    [SerializeField] private string poolTag = "";
    [SerializeField] private bool autoDespawnOnEnable = true;
    private readonly HashSet<GameObject> hitTargets = new HashSet<GameObject>();
    private static readonly Dictionary<int, BossTargetRefs> bossTargetCache = new Dictionary<int, BossTargetRefs>(64);
    private Coroutine lifeCo;

    void OnEnable()
    {
        hitTargets.Clear();
        if (lifeCo != null) StopCoroutine(lifeCo);
        lifeCo = autoDespawnOnEnable ? StartCoroutine(CoLife()) : null;
    }

    void OnDisable()
    {
        if (lifeCo != null)
        {
            StopCoroutine(lifeCo);
            lifeCo = null;
        }
    }

    private IEnumerator CoLife()
    {
        yield return new WaitForSeconds(lifeTime);
        lifeCo = null;
        Despawn();
    }

    public void ConfigurePooling(bool enablePool, string tag)
    {
        usePool = enablePool;
        poolTag = tag;
    }

    public void SetAutoDespawnOnEnable(bool enabled)
    {
        autoDespawnOnEnable = enabled;
        if (!enabled && lifeCo != null)
        {
            StopCoroutine(lifeCo);
            lifeCo = null;
        }
    }

    private void Despawn()
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }

    public void DespawnNow()
    {
        Despawn();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ResolveBossTargets(other, out var boss, out var bossSlime);

        bool isEnemy = IsEnemyCollider(other);
        bool isBoss = (boss != null) || (bossSlime != null) || other.CompareTag("Boss");
        if (!isEnemy && !isBoss) return;

        GameObject hitRoot = (bossSlime != null || boss != null)
            ? (bossSlime != null ? bossSlime.gameObject : boss.gameObject)
            : ResolveHitRoot(other);

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

        ApplyEnemyDamageFallback(other, damage);
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

        // 충돌 콜라이더 계층이 보스 스크립트와 분리된 경우(루트/자식 분리) 대비
        if (root != null)
        {
            if (boss == null) boss = root.GetComponent<Boss>() ?? root.GetComponentInChildren<Boss>(true);
            if (bossSlime == null) bossSlime = root.GetComponent<BossSlime>() ?? root.GetComponentInChildren<BossSlime>(true);
            bossTargetCache[root.GetInstanceID()] = new BossTargetRefs { boss = boss, bossSlime = bossSlime };
        }
    }


    private static void ApplyEnemyDamageFallback(Collider2D other, int finalDamageInt)
    {
        if (other == null) return;

        var monster = other.GetComponent<Monster>() ?? other.GetComponentInParent<Monster>();
        if (monster != null)
        {
            monster.Hit(finalDamageInt);
            return;
        }

        other.gameObject.SendMessage("Hit", finalDamageInt, SendMessageOptions.DontRequireReceiver);

        Transform parent = other.transform.parent;
        if (parent != null)
            parent.gameObject.SendMessage("Hit", finalDamageInt, SendMessageOptions.DontRequireReceiver);

        Transform root = other.transform.root;
        if (root != null && root != other.transform && (parent == null || root != parent))
            root.gameObject.SendMessage("Hit", finalDamageInt, SendMessageOptions.DontRequireReceiver);
    }

    private static bool IsEnemyCollider(Collider2D other)
    {
        if (other == null) return false;
        return HasEnemyTag(other.transform);
    }

    private static GameObject ResolveHitRoot(Collider2D other)
    {
        if (other == null) return null;
        Transform t = other.transform;
        while (t != null)
        {
            if (HasEnemyTag(t)) return t.gameObject;
            t = t.parent;
        }

        Transform root = other.transform.root;
        return root != null ? root.gameObject : other.gameObject;
    }

    private static bool HasEnemyTag(Transform t)
    {
        if (t == null) return false;
        string tag = t.tag;
        return tag == "Enemy" || tag == "Enemy-Slime" || tag == "Enemy-Vacteria";
    }

    public void SetOwner(Player p)
    {
        Owner = p;
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
}
