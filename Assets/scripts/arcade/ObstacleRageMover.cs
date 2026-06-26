using UnityEngine;
using System.Collections;

public class ObstacleRageMover : MonoBehaviour, IReinitializable
{
    [Header("Move Settings")]
    public float moveDistance = 10f;
    public bool isTop = true;

    private float moveDuration = 2f;
    private float returnMoveDuration = 4f;

    private Vector3 initialLocalPosition;
    private Vector3 externalLocalOffset;
    private Vector3 appliedLocalOffset;
    private float rageOffsetY;
    private Coroutine moveCo;
    private bool initialLocalPositionSet = false;
    private bool movementFrozen = false;
    public bool IsMovementFrozen => movementFrozen;

    private float TargetRageOffsetY => isTop ? moveDistance : -moveDistance;

    private void Awake()
    {
        CacheInitialLocalPosition();
    }

    void OnEnable()
    {
        Reinit();
        GameData.OnRageStart += HandleExpandStart;
        GameData.OnRageEnd   += HandleExpandEnd;
        GameData.OnMachineGunSequenceStart += HandleExpandStart;
        GameData.OnMachineGunSequenceEnd += HandleExpandEnd;

        if (ShouldStayExpanded())
            StartMoveTo(TargetRageOffsetY, 0f);
    }

    void OnDisable()
    {
        GameData.OnRageStart -= HandleExpandStart;
        GameData.OnRageEnd   -= HandleExpandEnd;
        GameData.OnMachineGunSequenceStart -= HandleExpandStart;
        GameData.OnMachineGunSequenceEnd -= HandleExpandEnd;

        if (moveCo != null)
        {
            StopCoroutine(moveCo);
            moveCo = null;
        }

        if (!gameObject.activeSelf)
            RestoreInitialTransformState();
    }

    public void Reinit()
    {
        movementFrozen = false;

        if (moveCo != null)
        {
            StopCoroutine(moveCo);
            moveCo = null;
        }

        CacheInitialLocalPosition(forceRefresh: true);
        externalLocalOffset = Vector3.zero;
        appliedLocalOffset = Vector3.zero;
        rageOffsetY = 0f;
    }

    private void HandleExpandStart()
    {
        if (movementFrozen)
            return;

        StartMoveTo(TargetRageOffsetY, moveDuration);
    }

    private void HandleExpandEnd()
    {
        if (movementFrozen)
            return;

        if (ShouldStayExpanded())
            return;

        StartMoveTo(0f, returnMoveDuration);
    }

    private bool ShouldStayExpanded()
    {
        if (GameData.Instance == null)
            return false;

        return GameData.Instance.rageMode || GameData.Instance.IsMachineGunSequenceActive();
    }

    private void StartMoveTo(float targetRageOffsetY, float duration)
    {
        if (movementFrozen)
            return;

        if (moveCo != null) StopCoroutine(moveCo);
        moveCo = StartCoroutine(MoveYTo(targetRageOffsetY, duration));
    }

    public void FreezeAtCurrentPosition()
    {
        movementFrozen = true;

        if (moveCo != null)
        {
            StopCoroutine(moveCo);
            moveCo = null;
        }
    }

    public void ResumeMovement()
    {
        movementFrozen = false;
    }

    public void ResumeMovementForCurrentState()
    {
        movementFrozen = false;

        if (ShouldStayExpanded())
            StartMoveTo(TargetRageOffsetY, moveDuration);
        else
            StartMoveTo(0f, returnMoveDuration);
    }

    public void SetExternalLocalOffset(Vector3 offset)
    {
        externalLocalOffset = offset;
        ApplyCurrentLocalPosition();
    }

    private IEnumerator MoveYTo(float targetRageOffsetY, float duration)
    {
        float startOffsetY = rageOffsetY;
        float t = 0f;
        duration = Mathf.Max(0.0001f, duration);

        while (t < duration)
        {
            t += Time.deltaTime;
            rageOffsetY = Mathf.Lerp(startOffsetY, targetRageOffsetY, t / duration);
            ApplyCurrentLocalPosition();
            yield return null;
        }

        rageOffsetY = targetRageOffsetY;
        ApplyCurrentLocalPosition();
        moveCo = null;
    }

    private void CacheInitialLocalPosition(bool forceRefresh = false)
    {
        if (initialLocalPositionSet && !forceRefresh)
            return;

        initialLocalPosition = transform.localPosition;
        initialLocalPositionSet = true;
    }

    private void RestoreInitialLocalPosition()
    {
        if (!initialLocalPositionSet)
            return;

        transform.localPosition = initialLocalPosition;
    }

    private void ApplyCurrentLocalPosition()
    {
        Vector3 nextOffset = externalLocalOffset + new Vector3(0f, rageOffsetY, 0f);
        Vector3 basePosition = transform.localPosition - appliedLocalOffset;
        transform.localPosition = basePosition + nextOffset;
        appliedLocalOffset = nextOffset;
    }

    private void RestoreInitialTransformState()
    {
        if (appliedLocalOffset != Vector3.zero)
            transform.localPosition -= appliedLocalOffset;

        appliedLocalOffset = Vector3.zero;
        externalLocalOffset = Vector3.zero;
        rageOffsetY = 0f;

        RestoreInitialLocalPosition();
    }
}
