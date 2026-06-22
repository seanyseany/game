using UnityEngine;
using System.Collections;

public class obstacleWiggling : MonoBehaviour
{
    [SerializeField] private float moveDistance = 7f;
    [SerializeField] private float moveDuration = 2.5f;

    private ObstacleRageMover rageMover;
    private Transform obstacleTransform;
    private Collider2D triggerCollider;
    private Coroutine moveCo;
    private bool triggered;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        triggered = false;
        CacheReferences();
    }

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void LateUpdate()
    {
        if (triggered || moveCo != null)
        {
            return;
        }

        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<Collider2D>();
        }

        if (triggerCollider == null)
        {
            return;
        }

        Bounds triggerBounds = triggerCollider.bounds;
        var detectors = PhaseDetector.ActiveDetectors;
        for (int i = 0; i < detectors.Count; i++)
        {
            PhaseDetector detector = detectors[i];
            if (detector == null)
            {
                continue;
            }

            Collider2D detectorCollider = detector.CachedCollider;
            if (detectorCollider != null && detectorCollider.enabled && triggerBounds.Intersects(detectorCollider.bounds))
            {
                TriggerWiggle();
                return;
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.TryGetComponent<PhaseDetector>(out _))
        {
            return;
        }

        TriggerWiggle();
    }

    private void TriggerWiggle()
    {
        if (triggered || moveCo != null)
        {
            return;
        }

        if (rageMover == null || obstacleTransform == null)
        {
            CacheReferences();
        }

        if (obstacleTransform == null)
        {
            return;
        }

        triggered = true;
        float direction = rageMover != null && rageMover.isTop ? -1f : 1f;
        float targetY = obstacleTransform.position.y + direction * moveDistance;
        StartMoveTo(targetY);
    }

    private void CacheReferences()
    {
        triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }

        rageMover = GetComponentInParent<ObstacleRageMover>();
        obstacleTransform = rageMover != null ? rageMover.transform : transform.parent;
    }

    private void StartMoveTo(float targetY)
    {
        if (moveCo != null)
        {
            StopCoroutine(moveCo);
        }

        moveCo = StartCoroutine(MoveYTo(targetY));
    }

    private IEnumerator MoveYTo(float targetY)
    {
        float startY = obstacleTransform.position.y;
        float elapsed = 0f;
        float duration = Mathf.Max(0.0001f, moveDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Vector3 pos = obstacleTransform.position;
            float y = Mathf.Lerp(startY, targetY, elapsed / duration);
            obstacleTransform.position = new Vector3(pos.x, y, pos.z);
            yield return null;
        }

        Vector3 finalPos = obstacleTransform.position;
        obstacleTransform.position = new Vector3(finalPos.x, targetY, finalPos.z);
        moveCo = null;
    }
}
