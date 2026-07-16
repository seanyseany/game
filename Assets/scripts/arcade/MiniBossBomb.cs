using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class MiniBossBomb : MonoBehaviour
{
    private const string DestroyTriggerName = "destroy";

    [Header("Mini Boss Bomb")]
    [SerializeField] private float selfDestructionTime = 3f;
    [SerializeField] private float xMoveSpeed = 4f;
    [SerializeField] private float initialYMoveSpeed = 6f;
    [SerializeField] private float finalYMoveSpeed = 3f;
    [SerializeField] private float launchDuration = 0.45f;
    [SerializeField] private float followResponseTime = 0.18f;
    [SerializeField] private float yTurnDuration = 0.35f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float destroyCleanupDelay = 0.5f;
    [SerializeField] private string playerTag = "player";

    private Animator cachedAnimator;
    private Collider2D cachedCollider;
    private Transform targetPlayer;
    private Coroutine selfDestroyRoutine;
    private Coroutine cleanupRoutine;
    private bool destroyTriggered;
    private float spawnTime;
    private float currentYVelocity;
    private bool trackingStarted;
    private readonly HashSet<int> hitPlayerIds = new HashSet<int>();

    private void Awake()
    {
        cachedAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        cachedCollider = GetComponent<Collider2D>();

        if (cachedCollider != null)
            cachedCollider.isTrigger = true;
    }

    private void OnEnable()
    {
        ResetRuntime();
    }

    private void OnDisable()
    {
        if (selfDestroyRoutine != null)
        {
            StopCoroutine(selfDestroyRoutine);
            selfDestroyRoutine = null;
        }

        if (cleanupRoutine != null)
        {
            StopCoroutine(cleanupRoutine);
            cleanupRoutine = null;
        }

        hitPlayerIds.Clear();
    }

    public void SetTarget(Transform playerTarget)
    {
        targetPlayer = playerTarget;
    }

    private void ResetRuntime()
    {
        destroyTriggered = false;
        targetPlayer = Player.Instance != null ? Player.Instance.transform : FindFirstObjectByType<Player>()?.transform;
        spawnTime = Time.time;
        currentYVelocity = -Mathf.Max(0f, initialYMoveSpeed);
        trackingStarted = false;
        hitPlayerIds.Clear();

        if (cachedCollider != null)
            cachedCollider.enabled = true;

        if (cachedAnimator != null)
        {
            cachedAnimator.ResetTrigger(DestroyTriggerName);
            cachedAnimator.Rebind();
            cachedAnimator.Update(0f);
        }

        if (selfDestroyRoutine != null)
            StopCoroutine(selfDestroyRoutine);

        selfDestroyRoutine = StartCoroutine(CoSelfDestruct());
    }

    private void Update()
    {
        if (destroyTriggered)
            return;

        Vector3 position = transform.position;
        float horizontalSpeed = Mathf.Max(0f, xMoveSpeed);
        position.x += -horizontalSpeed * Time.deltaTime;

        if (!trackingStarted)
        {
            position.y += currentYVelocity * Time.deltaTime;

            float launchDeceleration = Mathf.Max(0.01f, Mathf.Abs(initialYMoveSpeed)) / Mathf.Max(0.01f, launchDuration);
            currentYVelocity = Mathf.MoveTowards(currentYVelocity, 0f, launchDeceleration * Time.deltaTime);

            if (Mathf.Abs(currentYVelocity) <= 0.01f || Time.time - spawnTime >= Mathf.Max(0.01f, launchDuration))
            {
                currentYVelocity = 0f;
                trackingStarted = true;
            }
        }
        else
        {
            float targetY = targetPlayer != null ? targetPlayer.position.y : position.y;
            float deltaY = targetY - position.y;
            float desiredVelocity = 0f;

            if (Mathf.Abs(deltaY) > 0.02f)
            {
                float responseTime = Mathf.Max(0.01f, followResponseTime);
                desiredVelocity = Mathf.Clamp(
                    deltaY / responseTime,
                    -Mathf.Max(0f, finalYMoveSpeed),
                    Mathf.Max(0f, finalYMoveSpeed));
            }

            currentYVelocity = Mathf.MoveTowards(
                currentYVelocity,
                desiredVelocity,
                (Mathf.Max(0.01f, finalYMoveSpeed) / Mathf.Max(0.01f, yTurnDuration)) * Time.deltaTime);

            position.y += currentYVelocity * Time.deltaTime;
        }

        transform.position = position;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (destroyTriggered || other == null)
            return;

        Player player = other.GetComponent<Player>() ?? other.GetComponentInParent<Player>();
        if (player == null && !other.CompareTag(playerTag))
            return;

        if (player != null)
        {
            int playerId = player.GetInstanceID();
            if (hitPlayerIds.Contains(playerId))
                return;

            bool applied = player.TakeExternalObstacleDamage(damage, ObstacleType.Saw, other);
            if (applied)
                hitPlayerIds.Add(playerId);
        }
    }

    private IEnumerator CoSelfDestruct()
    {
        if (selfDestructionTime > 0f)
            yield return new WaitForSeconds(selfDestructionTime);

        selfDestroyRoutine = null;
        TriggerDestroy();
    }

    private void TriggerDestroy()
    {
        if (destroyTriggered)
            return;

        destroyTriggered = true;

        if (selfDestroyRoutine != null)
        {
            StopCoroutine(selfDestroyRoutine);
            selfDestroyRoutine = null;
        }

        if (cachedCollider != null)
            cachedCollider.enabled = false;

        if (cachedAnimator != null)
            cachedAnimator.SetTrigger(DestroyTriggerName);

        if (cleanupRoutine != null)
            StopCoroutine(cleanupRoutine);

        cleanupRoutine = StartCoroutine(CoCleanup());
    }

    private IEnumerator CoCleanup()
    {
        if (destroyCleanupDelay > 0f)
            yield return new WaitForSeconds(destroyCleanupDelay);

        cleanupRoutine = null;
        Destroy(gameObject);
    }
}
