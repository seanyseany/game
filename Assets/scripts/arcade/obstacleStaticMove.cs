using UnityEngine;

public class obstacleStaticMove : MonoBehaviour
{
    private const string ObstacleTag = "Obstacle";

    [Header("Move Settings")]
    [SerializeField] private float moveYOffset = 2f;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private bool triggerOnlyOnce = true;
    [SerializeField] private float animatorStopFadeDuration = 0.4f;

    [Header("Optional Target")]
    [SerializeField] private Transform moveTarget;

    private bool isMoving;
    private bool hasTriggered;
    private float targetYPosition;
    private Collider2D[] cachedColliders = System.Array.Empty<Collider2D>();
    private Animator[] cachedAnimators = System.Array.Empty<Animator>();
    private Coroutine animatorFadeCoroutine;

    private void Reset()
    {
        moveTarget = transform;
    }

    private void Awake()
    {
        if (moveTarget == null)
        {
            moveTarget = transform;
        }

        RefreshColliderCache();
        RefreshAnimatorCache();
    }

    private void OnEnable()
    {
        isMoving = false;
        hasTriggered = false;

        if (moveTarget == null)
        {
            moveTarget = transform;
        }

        RefreshColliderCache();
        RefreshAnimatorCache();
        RestoreAnimatorPlayback();
        ObstacleDetector.Register(this);
    }

    private void OnDisable()
    {
        if (animatorFadeCoroutine != null)
        {
            StopCoroutine(animatorFadeCoroutine);
            animatorFadeCoroutine = null;
        }

        ObstacleDetector.Unregister(this);
    }

    private void Update()
    {
        if (!isMoving || moveTarget == null)
        {
            return;
        }

        Vector3 currentPosition = moveTarget.position;
        float nextY = Mathf.MoveTowards(currentPosition.y, targetYPosition, Mathf.Max(0f, moveSpeed) * Time.deltaTime);
        moveTarget.position = new Vector3(currentPosition.x, nextY, currentPosition.z);

        if (Mathf.Approximately(nextY, targetYPosition))
        {
            moveTarget.position = new Vector3(currentPosition.x, targetYPosition, currentPosition.z);
            isMoving = false;
            FadeOutAnimatorPlayback();
        }
    }

    public void TriggerMove()
    {
        if (triggerOnlyOnce && hasTriggered)
        {
            return;
        }

        if (isMoving)
        {
            return;
        }

        if (moveTarget == null)
        {
            moveTarget = transform;
        }

        targetYPosition = moveTarget.position.y + moveYOffset;

        if (Mathf.Approximately(moveTarget.position.y, targetYPosition))
        {
            isMoving = false;
            FadeOutAnimatorPlayback();
            return;
        }

        hasTriggered = true;
        isMoving = true;
        RestoreAnimatorPlayback();
    }

    public void RefreshColliderCache()
    {
        if (moveTarget == null)
        {
            cachedColliders = GetComponentsInChildren<Collider2D>(true);
            return;
        }

        cachedColliders = moveTarget.GetComponentsInChildren<Collider2D>(true);
        if (cachedColliders == null || cachedColliders.Length == 0)
        {
            cachedColliders = GetComponentsInChildren<Collider2D>(true);
        }
    }

    public void RefreshAnimatorCache()
    {
        if (!ShouldControlAnimators())
        {
            cachedAnimators = System.Array.Empty<Animator>();
            return;
        }

        if (moveTarget == null)
        {
            cachedAnimators = GetComponentsInChildren<Animator>(true);
            return;
        }

        cachedAnimators = moveTarget.GetComponentsInChildren<Animator>(true);
        if (cachedAnimators == null || cachedAnimators.Length == 0)
        {
            cachedAnimators = GetComponentsInChildren<Animator>(true);
        }
    }

    private bool ShouldControlAnimators()
    {
        if (HasObstacleTag(transform))
        {
            return true;
        }

        if (moveTarget != null && HasObstacleTag(moveTarget))
        {
            return true;
        }

        return false;
    }

    private void RestoreAnimatorPlayback()
    {
        if (!ShouldControlAnimators())
        {
            return;
        }

        if (animatorFadeCoroutine != null)
        {
            StopCoroutine(animatorFadeCoroutine);
            animatorFadeCoroutine = null;
        }

        if (cachedAnimators == null || cachedAnimators.Length == 0)
        {
            RefreshAnimatorCache();
        }

        for (int i = 0; i < cachedAnimators.Length; i++)
        {
            Animator animator = cachedAnimators[i];
            if (animator == null)
            {
                continue;
            }

            animator.enabled = true;
            animator.speed = 1f;
        }
    }

    private void FadeOutAnimatorPlayback()
    {
        if (!ShouldControlAnimators())
        {
            return;
        }

        if (animatorFadeCoroutine != null)
        {
            StopCoroutine(animatorFadeCoroutine);
        }

        animatorFadeCoroutine = StartCoroutine(FadeOutAnimators());
    }

    private System.Collections.IEnumerator FadeOutAnimators()
    {
        if (cachedAnimators == null || cachedAnimators.Length == 0)
        {
            RefreshAnimatorCache();
        }

        float duration = Mathf.Max(0f, animatorStopFadeDuration);
        if (duration <= 0f)
        {
            SetAnimatorSpeed(0f);
            SetAnimatorEnabled(false);
            animatorFadeCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float nextSpeed = Mathf.Lerp(1f, 0f, elapsed / duration);
            SetAnimatorSpeed(nextSpeed);
            yield return null;
        }

        SetAnimatorSpeed(0f);
        SetAnimatorEnabled(false);
        animatorFadeCoroutine = null;
    }

    private void SetAnimatorSpeed(float speed)
    {
        for (int i = 0; i < cachedAnimators.Length; i++)
        {
            Animator animator = cachedAnimators[i];
            if (animator == null)
            {
                continue;
            }

            animator.speed = speed;
        }
    }

    private void SetAnimatorEnabled(bool isEnabled)
    {
        for (int i = 0; i < cachedAnimators.Length; i++)
        {
            Animator animator = cachedAnimators[i];
            if (animator == null)
            {
                continue;
            }

            animator.enabled = isEnabled;
        }
    }

    private static bool HasObstacleTag(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        Transform current = target;
        while (current != null)
        {
            string currentTag = current.tag;
            if (string.Equals(currentTag, ObstacleTag, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    public bool TryTriggerFromBounds(Bounds detectorBounds)
    {
        if ((triggerOnlyOnce && hasTriggered) || isMoving)
        {
            return false;
        }

        if (cachedColliders == null || cachedColliders.Length == 0)
        {
            RefreshColliderCache();
        }

        for (int i = 0; i < cachedColliders.Length; i++)
        {
            Collider2D col = cachedColliders[i];
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (IntersectsOnXY(detectorBounds, col.bounds))
            {
                TriggerMove();
                return true;
            }
        }

        return false;
    }

    private static bool IntersectsOnXY(Bounds a, Bounds b)
    {
        return a.min.x <= b.max.x &&
               a.max.x >= b.min.x &&
               a.min.y <= b.max.y &&
               a.max.y >= b.min.y;
    }
}
