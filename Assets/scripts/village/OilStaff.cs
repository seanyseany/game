using System.Collections;
using UnityEngine;

public class OilStaff : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float walkStretchFrequency = 6f;
    [SerializeField] private float roamPauseMin = 0f;
    [SerializeField] private float roamPauseMax = 0f;
    [SerializeField] private float walkStretchAmount = 0.03f;
    [SerializeField] private float arriveDistance = 0.05f;

    [Header("Visual")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Vector3 visualBaseScale = Vector3.one;
    private bool facingLeft = true;
    private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
    private Transform activeMoveTargetTransform;
    private Vector3 activeMoveTargetPosition;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            visualBaseScale = spriteRenderer.transform.localScale;
    }

    public IEnumerator MoveToRoutine(Vector3 targetPosition)
    {
        activeMoveTargetTransform = null;
        activeMoveTargetPosition = WithFixedZ(targetPosition);
        while (Vector3.Distance(transform.position, activeMoveTargetPosition) > arriveDistance)
        {
            Vector3 current = transform.position;
            Vector3 next = Vector3.MoveTowards(current, activeMoveTargetPosition, moveSpeed * Time.fixedDeltaTime);
            UpdateFacing(next.x - current.x);
            transform.position = next;
            ApplyWalkStretch();
            yield return waitForFixedUpdate;
        }

        transform.position = activeMoveTargetPosition;
        activeMoveTargetTransform = null;
    }

    public IEnumerator MoveToTransformRoutine(Transform targetTransform, Vector3 fallbackWorldPosition)
    {
        activeMoveTargetTransform = targetTransform;
        activeMoveTargetPosition = WithFixedZ(targetTransform != null ? targetTransform.position : fallbackWorldPosition);

        while (Vector3.Distance(transform.position, GetCurrentMoveTargetPosition(fallbackWorldPosition)) > arriveDistance)
        {
            Vector3 current = transform.position;
            Vector3 moveTarget = GetCurrentMoveTargetPosition(fallbackWorldPosition);
            Vector3 next = Vector3.MoveTowards(current, moveTarget, moveSpeed * Time.fixedDeltaTime);
            UpdateFacing(next.x - current.x);
            transform.position = next;
            ApplyWalkStretch();
            yield return waitForFixedUpdate;
        }

        transform.position = GetCurrentMoveTargetPosition(fallbackWorldPosition);
        activeMoveTargetTransform = null;
        ResetWalkStretch();
    }

    private void UpdateFacing(float deltaX)
    {
        if (Mathf.Abs(deltaX) < 0.001f)
            return;

        bool shouldFaceLeft = deltaX < 0f;
        if (facingLeft == shouldFaceLeft)
            return;

        facingLeft = shouldFaceLeft;
        Vector3 angles = transform.localEulerAngles;
        angles.y = facingLeft ? 0f : 180f;
        transform.localEulerAngles = angles;
    }

    private void ApplyWalkStretch()
    {
        if (spriteRenderer == null)
            return;

        float stretch = Mathf.Sin(Time.time * walkStretchFrequency) * walkStretchAmount;
        spriteRenderer.transform.localScale = new Vector3(
            visualBaseScale.x * (1f - stretch),
            visualBaseScale.y * (1f + stretch),
            visualBaseScale.z);
    }

    private void ResetWalkStretch()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.transform.localScale = visualBaseScale;
    }

    private float GetRoamPauseDuration()
    {
        float minPause = Mathf.Max(0f, roamPauseMin);
        float maxPause = Mathf.Max(minPause, roamPauseMax);
        return maxPause <= 0f ? 0f : Random.Range(minPause, maxPause);
    }

    public IEnumerator PauseRoutine()
    {
        float duration = GetRoamPauseDuration();
        if (duration > 0f)
            yield return new WaitForSeconds(duration);
    }

    private Vector3 WithFixedZ(Vector3 position)
    {
        position.z = transform.position.z;
        return position;
    }

    private Vector3 GetCurrentMoveTargetPosition(Vector3 fallbackWorldPosition)
    {
        if (activeMoveTargetTransform != null)
            activeMoveTargetPosition = WithFixedZ(activeMoveTargetTransform.position);
        else
            activeMoveTargetPosition = WithFixedZ(activeMoveTargetPosition == Vector3.zero ? fallbackWorldPosition : activeMoveTargetPosition);

        return activeMoveTargetPosition;
    }

}
