using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Villan : MonoBehaviour
{
    private static readonly Dictionary<int, Queue<Villan>> PoolsByPrefab = new Dictionary<int, Queue<Villan>>();
    private const float ArriveDistance = 0.03f;
    private const int SortingOrderOffsetCycle = 20;
    private static int nextSortingOrderOffset;

    [FormerlySerializedAs("baseMoveSpeed")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private int hitCount = 1;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [HideInInspector] [SerializeField] private Transform aimTarget;
    [HideInInspector] [SerializeField] private Animator animator;

    private const string DestroyTrigger = "destroy";

    private VillanPath path;
    private Rigidbody2D body;
    private Villan sourcePrefab;
    private int currentHitCount;
    private float currentMoveSpeed;
    private int routeIndex;
    private bool dead;
    private bool facingLeft = true;
    private bool returningToPool;
    private Vector3 originalLocalScale = Vector3.one;
    private Vector3 originalLocalEulerAngles;
    private int prefabPoolKey;
    private int baseSortingOrder;

    public Transform AimTarget => aimTarget != null ? aimTarget : transform;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        GetComponent<Collider2D>().isTrigger = true;
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
            baseSortingOrder = spriteRenderer.sortingOrder;
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        originalLocalScale = transform.localScale;
        originalLocalEulerAngles = transform.localEulerAngles;
    }

    private void Update()
    {
        if (dead || returningToPool || path == null)
            return;

        MoveAlongRoute();
    }

    public static Villan Spawn(Villan prefab, VillanPath nextPath, int nextLevel)
    {
        if (prefab == null)
            return null;

        int poolKey = prefab.GetInstanceID();
        if (!PoolsByPrefab.TryGetValue(poolKey, out Queue<Villan> pool))
        {
            pool = new Queue<Villan>();
            PoolsByPrefab.Add(poolKey, pool);
        }

        Villan villan = null;
        while (pool.Count > 0 && villan == null)
            villan = pool.Dequeue();

        if (villan == null)
        {
            villan = Instantiate(prefab);
            villan.sourcePrefab = prefab;
            villan.prefabPoolKey = poolKey;
        }
        else
        {
            villan.gameObject.SetActive(true);
        }

        villan.Initialize(nextPath, nextLevel);
        return villan;
    }

    public void Initialize(VillanPath nextPath, int nextLevel)
    {
        path = nextPath;
        dead = false;
        returningToPool = false;
        routeIndex = 0;
        StopAllCoroutines();
        ResetOrientation();
        transform.position = path != null ? path.EntranceWorldPosition : transform.position;
        if (body != null)
            body.position = transform.position;
        body.linearVelocity = Vector2.zero;

        transform.localScale = originalLocalScale;
        currentHitCount = Mathf.Max(1, hitCount);
        currentMoveSpeed = Mathf.Max(0.2f, moveSpeed);
        ApplySpawnSortingOrderOffset();
    }

    public void TakeDamage(int amount)
    {
        if (dead)
            return;

        currentHitCount = Mathf.Max(0, currentHitCount - Mathf.Max(1, amount));
        if (currentHitCount <= 0)
            StartCoroutine(DieRoutine());
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (dead || returningToPool || other == null)
            return;

        Bank bank = other.GetComponent<Bank>() ?? other.GetComponentInParent<Bank>();
        if (bank != null)
            StartCoroutine(DieRoutine());
    }

    private IEnumerator DieRoutine()
    {
        if (dead)
            yield break;

        dead = true;
        returningToPool = true;
        body.linearVelocity = Vector2.zero;

        if (animator != null)
        {
            animator.SetTrigger(DestroyTrigger);
            yield return new WaitForSeconds(0.3f);
        }

        ReturnToPool();
    }

    private void UpdateFacing(bool shouldFaceLeft)
    {
        if (facingLeft == shouldFaceLeft)
            return;

        facingLeft = shouldFaceLeft;
        Vector3 angles = transform.localEulerAngles;
        angles.y = facingLeft ? 0f : 180f;
        transform.localEulerAngles = angles;
    }

    private void MoveAlongRoute()
    {
        if (path == null || routeIndex >= path.MovePoints.Count)
        {
            StartCoroutine(ReturnToPoolAtRouteEnd());
            return;
        }

        Vector3 target = path.GetMovePointPosition(routeIndex);
        Vector3 next = Vector3.MoveTowards(transform.position, target, currentMoveSpeed * Time.deltaTime);
        float deltaX = next.x - transform.position.x;
        if (Mathf.Abs(deltaX) > 0.0001f)
            UpdateFacing(deltaX < 0f);

        transform.position = next;
        if (body != null)
            body.position = next;

        if (Vector3.Distance(transform.position, target) > ArriveDistance)
            return;

        Transform reachedPoint = path.MovePoints[routeIndex];
        if (path.IsFlipPoint(reachedPoint))
            UpdateFacing(!facingLeft);

        routeIndex++;
    }

    private IEnumerator ReturnToPoolAtRouteEnd()
    {
        if (returningToPool)
            yield break;

        returningToPool = true;
        if (body != null)
            body.linearVelocity = Vector2.zero;

        yield return null;
        ReturnToPool();
    }

    private void ReturnToPool()
    {
        ResetOrientation();
        path = null;
        routeIndex = 0;
        dead = false;
        returningToPool = false;

        if (!PoolsByPrefab.TryGetValue(prefabPoolKey, out Queue<Villan> pool))
        {
            pool = new Queue<Villan>();
            PoolsByPrefab.Add(prefabPoolKey, pool);
        }

        gameObject.SetActive(false);
        pool.Enqueue(this);
    }

    private void ResetOrientation()
    {
        facingLeft = true;
        transform.localEulerAngles = originalLocalEulerAngles;
    }

    private void ApplySpawnSortingOrderOffset()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.sortingOrder = baseSortingOrder + nextSortingOrderOffset;
        nextSortingOrderOffset = (nextSortingOrderOffset + 1) % SortingOrderOffsetCycle;
    }
}
