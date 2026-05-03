using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class Monster : MonoBehaviour, IReinitializable
{
    [Header("Animation")]
    [SerializeField] private Animator anim;
    [SerializeField] private string walkTrigger = "walk";
    [SerializeField] private string dieTrigger = "die";
    [SerializeField] private float dieDespawnDelay = 0.6f;

    [Header("Gate")]
    [SerializeField] private string gateTag = "Gate";
    [SerializeField] private bool damageGateOnContact = true;

    [Header("Pooling")]
    [SerializeField] private bool usePool = false;
    [SerializeField] private string poolTag = "";

    private Collider2D[] cachedColliders;
    private bool dead;
    private bool rageCounted;
    private Coroutine dieRoutine;
    private RageUIController rageUi;

    void Awake()
    {
        if (anim == null)
            anim = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        cachedColliders = GetComponentsInChildren<Collider2D>(true);
    }

    void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        dead = false;
        rageCounted = false;

        if (dieRoutine != null)
        {
            StopCoroutine(dieRoutine);
            dieRoutine = null;
        }

        SetAllColliders(true);
        PlayWalk();
    }

    public void ConfigurePooling(bool shouldUsePool, string tag)
    {
        usePool = shouldUsePool;
        poolTag = tag;
    }

    // Hitbox / ProjectileBall / ZigzagLightning에서 SendMessage("Hit", damage)로 호출됨
    public void Hit(int damage)
    {
        if (dead) return;
        StartDie(byPlayerKill: true);
    }

    // 호환용
    public void TakeDamage(int damage)
    {
        Hit(damage);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (dead || other == null) return;

        if (ShouldDamageGate(other))
        {
            if (damageGateOnContact && GateHealth.Instance != null)
                GateHealth.Instance.TakeBossMissileHit();

            StartDie(byPlayerKill: false);
            return;
        }

        if (IsPlayerAttackCollider(other))
            StartDie(byPlayerKill: true);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (dead || collision == null) return;
        var other = collision.collider;
        if (other == null) return;

        if (ShouldDamageGate(other))
        {
            if (damageGateOnContact && GateHealth.Instance != null)
                GateHealth.Instance.TakeBossMissileHit();

            StartDie(byPlayerKill: false);
        }
    }

    private void StartDie(bool byPlayerKill)
    {
        if (dead) return;
        dead = true;

        SetAllColliders(false);
        PlayDie();

        if (byPlayerKill)
            AddRageOneKill();

        if (dieRoutine != null) StopCoroutine(dieRoutine);
        dieRoutine = StartCoroutine(CoDespawnAfterDie());
    }

    private IEnumerator CoDespawnAfterDie()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, dieDespawnDelay));
        dieRoutine = null;
        Despawn();
    }

    private void Despawn()
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }

    private void AddRageOneKill()
    {
        if (rageCounted) return;
        rageCounted = true;

        if (rageUi == null)
            rageUi = Object.FindFirstObjectByType<RageUIController>();

        if (rageUi != null)
            rageUi.AddKill();
    }

    private bool ShouldDamageGate(Collider2D other)
    {
        if (other.CompareTag(gateTag)) return true;
        if (other.GetComponent<GateHealth>() != null) return true;
        if (other.GetComponentInParent<GateHealth>() != null) return true;
        return false;
    }

    private static bool IsPlayerAttackCollider(Collider2D other)
    {
        if (other == null) return false;

        if (other.GetComponent<Hitbox>() != null) return true;
        if (other.GetComponent<ProjectileBall>() != null) return true;
        if (other.GetComponent<ZigzagLightning>() != null) return true;

        Transform p = other.transform.parent;
        if (p != null)
        {
            if (p.GetComponent<Hitbox>() != null) return true;
            if (p.GetComponent<ProjectileBall>() != null) return true;
            if (p.GetComponent<ZigzagLightning>() != null) return true;
        }

        Transform r = other.transform.root;
        if (r != null)
        {
            if (r.GetComponent<Hitbox>() != null) return true;
            if (r.GetComponent<ProjectileBall>() != null) return true;
            if (r.GetComponent<ZigzagLightning>() != null) return true;
        }

        return false;
    }

    private void PlayWalk()
    {
        if (anim == null) return;
        anim.ResetTrigger(dieTrigger);
        anim.SetTrigger(walkTrigger);
    }

    private void PlayDie()
    {
        if (anim == null) return;
        anim.ResetTrigger(walkTrigger);
        anim.SetTrigger(dieTrigger);
    }

    private void SetAllColliders(bool enabled)
    {
        if (cachedColliders == null) return;
        for (int i = 0; i < cachedColliders.Length; i++)
        {
            if (cachedColliders[i] != null)
                cachedColliders[i].enabled = enabled;
        }
    }
}
