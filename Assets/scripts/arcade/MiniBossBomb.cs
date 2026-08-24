using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(ObstacleInfo))]
public class MiniBossBomb : MonoBehaviour
{
    private const string DestroyTriggerName = "destroy";
    private const string ObstacleTag = "Obstacle";
    private const float PreTrackingCenterY = 0f;

    [Header("Mini Boss Bomb")]
    [SerializeField] private float selfDestructionTime = 3f;
    [SerializeField] private float xMoveSpeed = 4f;
    [SerializeField] private float finalYMoveSpeed = 3f;
    [SerializeField] private float trackingStartWorldX = 7f;
    [SerializeField] private float preTrackingYOffset = 3f;
    [SerializeField] private float preTrackingOscillationSpeed = 2f;
    [SerializeField] private float followResponseTime = 0.18f;
    [SerializeField] private float yTurnDuration = 0.35f;
    [SerializeField] private float destroyCleanupDelay = 0.5f;

    [Header("Engines")]
    [Tooltip("Assign the root GameObject of the upper engine animation prefab child.")]
    [SerializeField] private GameObject upperEngine;
    [Tooltip("Assign the root GameObject of the lower engine animation prefab child.")]
    [SerializeField] private GameObject lowerEngine;
    [SerializeField, Min(0f)] private float engineActivationVelocity = 0.01f;

    private Animator cachedAnimator;
    private Collider2D cachedCollider;
    private ObstacleInfo cachedObstacleInfo;
    private Transform targetPlayer;
    private Coroutine selfDestroyRoutine;
    private Coroutine cleanupRoutine;
    private bool destroyTriggered;
    private float currentYVelocity;
    private float preTrackingOscillationTime;
    private GameObject activeEngine;

    private void Awake()
    {
        DisableEngines();
        cachedAnimator = GetComponent<Animator>() ?? FindBombAnimator();
        cachedCollider = GetComponent<Collider2D>();
        cachedObstacleInfo = GetComponent<ObstacleInfo>();

        if (cachedCollider != null)
            cachedCollider.isTrigger = true;

        if (cachedObstacleInfo != null)
            cachedObstacleInfo.type = ObstacleType.Saw;

        if (gameObject.tag != ObstacleTag)
            gameObject.tag = ObstacleTag;
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

    }

    public void SetTarget(Transform playerTarget)
    {
        targetPlayer = playerTarget;
    }

    private void ResetRuntime()
    {
        destroyTriggered = false;
        targetPlayer = Player.Instance != null ? Player.Instance.transform : FindFirstObjectByType<Player>()?.transform;
        currentYVelocity = 0f;
        preTrackingOscillationTime = 0f;
        DisableEngines();
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

        bool shouldTrackPlayerY = position.x <= trackingStartWorldX;
        float targetY = GetTargetY(position.y, shouldTrackPlayerY);
        UpdateVerticalVelocityToward(targetY, position.y);
        position.y += currentYVelocity * Time.deltaTime;

        transform.position = position;
        UpdateEngineState(currentYVelocity);
    }

    private float GetTargetY(float currentY, bool shouldTrackPlayerY)
    {
        if (shouldTrackPlayerY)
            return targetPlayer != null ? targetPlayer.position.y : currentY;

        preTrackingOscillationTime += Time.deltaTime * Mathf.Max(0f, preTrackingOscillationSpeed);
        return PreTrackingCenterY + Mathf.Sin(preTrackingOscillationTime) * Mathf.Abs(preTrackingYOffset);
    }

    private void UpdateVerticalVelocityToward(float targetY, float currentY)
    {
        float deltaY = targetY - currentY;
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
        DisableEngines();

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

    private Animator FindBombAnimator()
    {
        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator != null && !IsEngineTransform(animator.transform))
                return animator;
        }

        return null;
    }

    private bool IsEngineTransform(Transform target)
    {
        return (upperEngine != null && target.IsChildOf(upperEngine.transform))
            || (lowerEngine != null && target.IsChildOf(lowerEngine.transform));
    }

    private void UpdateEngineState(float verticalVelocity)
    {
        GameObject nextEngine = null;
        float activationVelocity = Mathf.Max(0f, engineActivationVelocity);

        if (verticalVelocity > activationVelocity)
            nextEngine = lowerEngine;
        else if (verticalVelocity < -activationVelocity)
            nextEngine = upperEngine;

        if (activeEngine == nextEngine)
            return;

        if (upperEngine != null)
            upperEngine.SetActive(nextEngine == upperEngine);

        if (lowerEngine != null)
            lowerEngine.SetActive(nextEngine == lowerEngine);

        activeEngine = nextEngine;
    }

    private void DisableEngines()
    {
        if (upperEngine != null)
            upperEngine.SetActive(false);

        if (lowerEngine != null)
            lowerEngine.SetActive(false);

        activeEngine = null;
    }

    private IEnumerator CoCleanup()
    {
        if (destroyCleanupDelay > 0f)
            yield return new WaitForSeconds(destroyCleanupDelay);

        cleanupRoutine = null;
        Destroy(gameObject);
    }
}
