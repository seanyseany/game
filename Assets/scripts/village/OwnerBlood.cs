using System.Collections;
using UnityEngine;

public class OwnerBlood : MonoBehaviour
{
    private enum VisualState
    {
        Walking,
        Package
    }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;

    [SerializeField] private float itemPickupPause = 0.2f;

    [Header("Sprites")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite walkingSprite;
    [SerializeField] private Sprite packageSprite;

    private Building building;
    private Coroutine behaviourRoutine;
    private CustomerBlood servingCustomer;
    private bool facingRight = true;
    private bool dragLocked;
    private bool servingActive;
    private bool atServicePoint;
    private VisualState visualState = VisualState.Walking;
    private Transform activeMoveTargetTransform;
    private Vector3 activeMoveTargetPosition;
    private Vector3 activeMoveTargetLocalPosition;
    private bool activeMoveUsesBuildingLocalSpace;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        ApplyVisualState(VisualState.Walking);
    }

    public void BindBuilding(Building targetBuilding)
    {
        building = targetBuilding;
        facingRight = building == null || building.OwnerDefaultFacesRight;
        ApplyFacingRotation();
        ApplyVisualState(VisualState.Walking);
        RestartPatrolFromAnchor();
    }

    private void OnEnable()
    {
        if (behaviourRoutine == null)
            behaviourRoutine = StartCoroutine(PatrolLoop());
    }

    private void OnDisable()
    {
        if (behaviourRoutine != null)
        {
            StopCoroutine(behaviourRoutine);
            behaviourRoutine = null;
        }
    }

    public void ServeCustomer(CustomerBlood customer)
    {
        if (customer == null || building == null || dragLocked)
            return;

        if (behaviourRoutine != null)
            StopCoroutine(behaviourRoutine);

        behaviourRoutine = StartCoroutine(ServeRoutine(customer));
    }

    public void CancelCurrentService(bool resumePatrol)
    {
        servingCustomer = null;
        servingActive = false;
        atServicePoint = false;

        if (behaviourRoutine != null)
        {
            StopCoroutine(behaviourRoutine);
            behaviourRoutine = null;
        }

        ApplyVisualState(VisualState.Walking);

        if (resumePatrol && isActiveAndEnabled && !dragLocked)
            behaviourRoutine = StartCoroutine(PatrolLoop());
    }

    private IEnumerator PatrolLoop()
    {
        while (true)
        {
            if (building == null || dragLocked)
            {
                yield return null;
                continue;
            }

            Vector2 patrolFrom = building.OwnerPatrolFromLocalPosition;
            Vector2 patrolTo = building.OwnerPatrolToLocalPosition;
            float targetLocalX = Random.Range(patrolFrom.x, patrolTo.x);
            float fixedLocalY = building.OwnerLocalPosition.y;
            Vector3 targetLocalPosition = new Vector3(targetLocalX, fixedLocalY, transform.localPosition.z);
            yield return MoveToBuildingLocal(targetLocalPosition);
            yield return new WaitForSeconds(Random.Range(0.5f, 1.5f));
        }
    }

    private IEnumerator ServeRoutine(CustomerBlood customer)
    {
        servingCustomer = customer;
        servingActive = true;
        atServicePoint = false;

        yield return MoveToTransform(building != null ? building.OwnerPoint : null, building != null ? building.OwnerPoint.position : transform.position, true);

        if (customer != servingCustomer || !servingActive)
            yield break;

        atServicePoint = true;
        ApplyServicePointFacing();
        ApplyVisualState(VisualState.Walking);

        float pickupPause = Mathf.Max(0f, itemPickupPause);

        while (customer != null &&
               building != null &&
               customer.IsWaitingAtCounter(building) &&
               !customer.IsReadyToReceiveAtCounter(building))
            yield return null;

        if (customer == null || building == null || !customer.IsReadyToReceiveAtCounter(building))
        {
            if (building != null)
                building.AbortService(customer);

            CancelCurrentService(true);
            yield break;
        }

        float packageStartDelay = pickupPause * (2f / 3f);
        float customerReceiveStartDelay = Mathf.Max(0f, pickupPause - customer.ReceiveItemSpawnDelay);

        if (customerReceiveStartDelay < packageStartDelay)
        {
            if (customerReceiveStartDelay > 0f)
                yield return new WaitForSeconds(customerReceiveStartDelay);

            Coroutine receiveRoutine = StartCoroutine(
                customer.ReceivePurchasedItemRoutine(building.ItemPrefab, () => ApplyVisualState(VisualState.Walking)));

            float remainingUntilPackage = packageStartDelay - customerReceiveStartDelay;
            if (remainingUntilPackage > 0f)
                yield return new WaitForSeconds(remainingUntilPackage);

            ApplyVisualState(VisualState.Package);
            yield return receiveRoutine;
        }
        else
        {
            if (packageStartDelay > 0f)
                yield return new WaitForSeconds(packageStartDelay);

            ApplyVisualState(VisualState.Package);

            float remainingUntilReceive = customerReceiveStartDelay - packageStartDelay;
            if (remainingUntilReceive > 0f)
                yield return new WaitForSeconds(remainingUntilReceive);

            yield return customer.ReceivePurchasedItemRoutine(
                building.ItemPrefab,
                () => ApplyVisualState(VisualState.Walking));
        }

        if (building != null)
            building.AwardTradeEnergy();

        CancelCurrentService(true);
    }

    private IEnumerator MoveTo(Vector3 targetPosition, bool snapToTarget = false)
    {
        activeMoveTargetTransform = null;
        activeMoveUsesBuildingLocalSpace = false;
        activeMoveTargetPosition = targetPosition;

        while (Vector3.Distance(transform.position, activeMoveTargetPosition) > 0.025f)
        {
            Vector3 next = Vector3.MoveTowards(transform.position, activeMoveTargetPosition, moveSpeed * Time.deltaTime);
            UpdateFacing(next.x - transform.position.x);
            transform.position = next;
            yield return null;
        }

        if (snapToTarget)
            transform.position = activeMoveTargetPosition;

        ClearActiveMoveTarget();
    }

    private IEnumerator MoveToTransform(Transform targetTransform, Vector3 fallbackWorldPosition, bool snapToTarget = false)
    {
        activeMoveTargetTransform = targetTransform;
        activeMoveUsesBuildingLocalSpace = false;
        activeMoveTargetPosition = targetTransform != null ? targetTransform.position : fallbackWorldPosition;

        while (Vector3.Distance(transform.position, GetCurrentMoveTargetPosition(fallbackWorldPosition)) > 0.025f)
        {
            Vector3 moveTarget = GetCurrentMoveTargetPosition(fallbackWorldPosition);
            Vector3 next = Vector3.MoveTowards(transform.position, moveTarget, moveSpeed * Time.deltaTime);
            UpdateFacing(next.x - transform.position.x);
            transform.position = next;
            yield return null;
        }

        if (snapToTarget)
            transform.position = GetCurrentMoveTargetPosition(fallbackWorldPosition);

        ClearActiveMoveTarget();
    }

    private IEnumerator MoveToBuildingLocal(Vector3 targetLocalPosition, bool snapToTarget = false)
    {
        if (building == null)
            yield break;

        activeMoveTargetTransform = null;
        activeMoveUsesBuildingLocalSpace = true;
        activeMoveTargetLocalPosition = targetLocalPosition;
        activeMoveTargetPosition = building.transform.TransformPoint(targetLocalPosition);

        while (Vector3.Distance(transform.position, GetCurrentMoveTargetPosition(transform.position)) > 0.025f)
        {
            Vector3 moveTarget = GetCurrentMoveTargetPosition(transform.position);
            Vector3 next = Vector3.MoveTowards(transform.position, moveTarget, moveSpeed * Time.deltaTime);
            UpdateFacing(next.x - transform.position.x);
            transform.position = next;
            yield return null;
        }

        if (snapToTarget)
            transform.position = GetCurrentMoveTargetPosition(transform.position);

        ClearActiveMoveTarget();
    }

    private void UpdateFacing(float deltaX)
    {
        if (Mathf.Abs(deltaX) < 0.001f)
            return;

        bool shouldFaceRight = deltaX > 0f;
        if (facingRight == shouldFaceRight)
            return;

        facingRight = shouldFaceRight;
        ApplyFacingRotation();
    }

    private void ApplyFacingRotation()
    {
        bool defaultFacesRight = building == null || building.OwnerDefaultFacesRight;
        Vector3 angles = transform.localEulerAngles;
        angles.y = facingRight == defaultFacesRight ? 0f : 180f;
        transform.localEulerAngles = angles;
    }

    private void ApplyServicePointFacing()
    {
        facingRight = building == null || building.OwnerDefaultFacesRight;
        ApplyFacingRotation();
    }

    private void ApplyVisualState(VisualState nextState)
    {
        visualState = nextState;

        if (spriteRenderer == null)
            return;

        switch (visualState)
        {
            case VisualState.Package:
                if (packageSprite != null)
                    spriteRenderer.sprite = packageSprite;
                break;
            default:
                if (walkingSprite != null)
                    spriteRenderer.sprite = walkingSprite;
                break;
        }
    }

    private void SnapToOwnerPosition()
    {
        if (building == null)
            return;

        Vector3 ownerWorldPosition = building.transform.TransformPoint(new Vector3(
            building.OwnerLocalPosition.x,
            building.OwnerLocalPosition.y,
            0f));

        transform.position = ownerWorldPosition;
    }

    public void LockToBuildingForDrag()
    {
        dragLocked = true;
        CancelCurrentService(false);
        SnapToOwnerPosition();
    }

    public void FollowBuildingWhileDragging()
    {
        if (!dragLocked)
            return;

        SnapToOwnerPosition();
    }

    public void UnlockAfterDrag()
    {
        dragLocked = false;
        ApplyVisualState(VisualState.Walking);
        RestartPatrolFromAnchor();
    }

    public void RestartPatrolFromAnchor()
    {
        SnapToOwnerPosition();
        ApplyVisualState(VisualState.Walking);
        servingCustomer = null;
        servingActive = false;
        atServicePoint = false;

        if (!isActiveAndEnabled)
            return;

        if (behaviourRoutine != null)
            StopCoroutine(behaviourRoutine);

        behaviourRoutine = StartCoroutine(PatrolLoop());
    }

    private Vector3 GetCurrentMoveTargetPosition(Vector3 fallbackWorldPosition)
    {
        if (activeMoveTargetTransform != null)
            activeMoveTargetPosition = activeMoveTargetTransform.position;
        else if (activeMoveUsesBuildingLocalSpace && building != null)
            activeMoveTargetPosition = building.transform.TransformPoint(activeMoveTargetLocalPosition);
        else if (activeMoveTargetPosition == Vector3.zero)
            activeMoveTargetPosition = fallbackWorldPosition;

        return activeMoveTargetPosition;
    }

    private void ClearActiveMoveTarget()
    {
        activeMoveTargetTransform = null;
        activeMoveUsesBuildingLocalSpace = false;
    }
}
