using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class TurretBullet : MonoBehaviour
{
    private const string DestroyTrigger = "destroy";
    private const float ReturnToPoolDelaySeconds = 0.2f;
    private const float DestroyTransitionTimeoutSeconds = 0.1f;
    private const float DestroyAnimationTimeoutSeconds = 1f;
    private const float SpawnCollisionGraceSeconds = 0.08f;
    private const float FloorCollisionMinTravelDistance = 0.6f;

    private static readonly Dictionary<int, Queue<TurretBullet>> PoolsByPrefab = new Dictionary<int, Queue<TurretBullet>>();

    [Header("Purchase")]
    [SerializeField] private int oxygenPrice30 = 3;
    [SerializeField] private int oxygenPrice60 = 6;
    [SerializeField] private int oxygenPrice100 = 10;
    [SerializeField] [Min(1)] private int hitCount = 1;

    [Header("Motion")]
    [SerializeField] private float spawnSpeed = 0.5f;
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float spawnRotationOffsetZ = 180f;

    private Rigidbody2D body;
    private Collider2D hitCollider;
    private Animator animator;
    private Vector2 direction = Vector2.right;
    private bool returningToPool;
    private int prefabPoolKey;
    private float collisionEnabledAt;
    private Vector3 launchPosition;

    public int OxygenPrice30 => oxygenPrice30;
    public int OxygenPrice60 => oxygenPrice60;
    public int OxygenPrice100 => oxygenPrice100;
    public float SpawnSpeed => spawnSpeed;
    public float MoveSpeed => moveSpeed;
    public int HitCount => Mathf.Max(1, hitCount);

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        hitCollider = GetComponent<Collider2D>();
        hitCollider.isTrigger = true;
        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
    }

    private void OnDisable()
    {
        if (body != null)
            body.linearVelocity = Vector2.zero;
    }

    private void Update()
    {
        if (returningToPool || body == null)
            return;

        body.linearVelocity = direction * moveSpeed;
    }

    public static TurretBullet Spawn(TurretBullet prefab, Vector3 position, Vector2 nextDirection, Quaternion rotation)
    {
        if (prefab == null)
            return null;

        int poolKey = prefab.GetInstanceID();
        if (!PoolsByPrefab.TryGetValue(poolKey, out Queue<TurretBullet> pool))
        {
            pool = new Queue<TurretBullet>();
            PoolsByPrefab.Add(poolKey, pool);
        }

        TurretBullet bullet = null;
        while (pool.Count > 0 && bullet == null)
            bullet = pool.Dequeue();

        if (bullet == null)
        {
            bullet = Instantiate(prefab, position, rotation);
            bullet.prefabPoolKey = poolKey;
        }
        else
        {
            bullet.transform.position = position;
            bullet.transform.rotation = rotation;
            bullet.gameObject.SetActive(true);
        }

        bullet.Launch(nextDirection, rotation);
        return bullet;
    }

    public void Launch(Vector2 nextDirection, Quaternion rotation)
    {
        StopAllCoroutines();
        returningToPool = false;
        direction = nextDirection.sqrMagnitude > 0.0001f ? nextDirection.normalized : Vector2.right;
        collisionEnabledAt = Time.time + SpawnCollisionGraceSeconds;
        launchPosition = transform.position;
        Vector3 euler = rotation.eulerAngles;
        transform.eulerAngles = new Vector3(0f, 0f, euler.z + spawnRotationOffsetZ);

        if (body != null)
            body.linearVelocity = direction * moveSpeed;

        if (hitCollider != null)
            hitCollider.enabled = false;

        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }
    }

    private void LateUpdate()
    {
        if (hitCollider != null && !hitCollider.enabled && Time.time >= collisionEnabledAt)
            hitCollider.enabled = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (returningToPool || other == null || Time.time < collisionEnabledAt)
            return;

        Villan villan = other.GetComponent<Villan>() ?? other.GetComponentInParent<Villan>();
        if (villan != null)
        {
            villan.TakeDamage(HitCount);
            StartCoroutine(ReturnToPoolRoutine());
            return;
        }

        if (other.CompareTag("floor") || other.transform.CompareTag("floor"))
        {
            if (Vector2.Distance(transform.position, launchPosition) < FloorCollisionMinTravelDistance)
                return;

            StartCoroutine(ReturnToPoolRoutine());
        }
    }

    private IEnumerator ReturnToPoolRoutine()
    {
        if (returningToPool)
            yield break;

        returningToPool = true;
        if (body != null)
            body.linearVelocity = Vector2.zero;

        if (animator != null)
        {
            AnimatorStateInfo previousState = animator.GetCurrentAnimatorStateInfo(0);
            animator.SetTrigger(DestroyTrigger);
            yield return WaitForDestroyAnimation(previousState);
        }
        else
        {
            yield return new WaitForSeconds(ReturnToPoolDelaySeconds);
        }
        ReturnToPool();
    }

    private IEnumerator WaitForDestroyAnimation(AnimatorStateInfo previousState)
    {
        float elapsed = 0f;
        while (elapsed < DestroyTransitionTimeoutSeconds)
        {
            if (animator == null)
                yield break;

            if (animator.IsInTransition(0))
                break;

            AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
            if (currentState.fullPathHash != previousState.fullPathHash)
                break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < DestroyAnimationTimeoutSeconds)
        {
            if (animator == null)
                yield break;

            if (!animator.IsInTransition(0))
            {
                AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
                if (currentState.fullPathHash != previousState.fullPathHash && currentState.normalizedTime >= 1f)
                    yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(ReturnToPoolDelaySeconds);
    }

    private void ReturnToPool()
    {
        if (!PoolsByPrefab.TryGetValue(prefabPoolKey, out Queue<TurretBullet> pool))
        {
            pool = new Queue<TurretBullet>();
            PoolsByPrefab.Add(prefabPoolKey, pool);
        }

        if (hitCollider != null)
            hitCollider.enabled = true;

        gameObject.SetActive(false);
        pool.Enqueue(this);
    }
}
