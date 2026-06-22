using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class CustomerBlood : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private Vector2 heldItemLocalPosition = new Vector2(0.2f, 0.2f);

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string liftTrigger = "Lift";
    [SerializeField] private string idleTrigger = "Idle";
    [SerializeField] private string walkTrigger = "Walk";

    private Rigidbody2D body;
    private Coroutine lifeRoutine;
    private Coroutine moveRoutine;
    private EntranceManagement ownerEntranceManagement;
    private Entrance sourceEntrance;
    private Path currentPath;
    private Building targetBuilding;
    private Building.QueueSlot currentQueueSlot = Building.QueueSlot.None;
    private GameObject heldItemInstance;
    private bool facingRight = true;
    private bool waitingAtCounter;
    private bool purchaseFinished;
    private string spawnEntryId;

    public string SpawnEntryId => spawnEntryId;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
    }

    public void InitializeSpawn(
        string entryId,
        EntranceManagement entranceManagement,
        Entrance entrance,
        Path path)
    {
        ResetState();

        spawnEntryId = entryId;
        ownerEntranceManagement = entranceManagement;
        sourceEntrance = entrance;
        currentPath = path;
        targetBuilding = path != null ? path.Building : null;

        transform.position = entrance != null ? entrance.SpawnWorldPosition : transform.position;

        if (lifeRoutine != null)
            StopCoroutine(lifeRoutine);

        lifeRoutine = StartCoroutine(LifeCycleRoutine());
    }

    public bool IsWaitingAtCounter(Building building)
    {
        return building != null && targetBuilding == building && waitingAtCounter && currentQueueSlot == Building.QueueSlot.Counter;
    }

    public void MoveToQueueSlot(Building building, Building.QueueSlot slot, Vector3 worldTarget)
    {
        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        targetBuilding = building;
        currentQueueSlot = slot;
        moveRoutine = StartCoroutine(MoveToRoutine(worldTarget, slot == Building.QueueSlot.Counter, null));
    }

    public void ReceivePurchasedItem(GameObject itemPrefab)
    {
        if (itemPrefab != null)
        {
            heldItemInstance = Instantiate(itemPrefab, transform);
            heldItemInstance.transform.localPosition = heldItemLocalPosition;
            heldItemInstance.transform.localRotation = Quaternion.identity;
        }

        purchaseFinished = true;
        waitingAtCounter = false;
        PlayTrigger(liftTrigger);
    }

    private IEnumerator LifeCycleRoutine()
    {
        float lifetime = Random.Range(15f, 20f);
        float endTime = Time.time + lifetime;

        bool willAttemptPurchase = targetBuilding != null &&
                                   targetBuilding.IsWorking &&
                                   targetBuilding.HasPurchasableCustomerPoint() &&
                                   Random.value <= targetBuilding.GetPurchaseChance();

        if (willAttemptPurchase && targetBuilding.TryEnterQueue(this, out Building.QueueSlot slot, out Transform target))
        {
            currentQueueSlot = slot;
            yield return MoveToRoutine(target.position, slot == Building.QueueSlot.Counter, targetBuilding);

            while (!purchaseFinished && Time.time < endTime)
                yield return null;
        }
        else
        {
            PlayTrigger(walkTrigger);
            yield return RoamUntil(endTime);
        }

        while (Time.time < endTime)
            yield return RoamUntil(endTime);

        yield return ReturnToEntranceAndDespawn();
    }

    private IEnumerator RoamUntil(float endTime)
    {
        if (currentPath == null)
        {
            yield return null;
            yield break;
        }

        Vector3 waypointPosition = currentPath.GetRandomWorldPointOnPath();
        yield return MoveToRoutine(waypointPosition, false, null);

        if (Time.time < endTime)
            yield return new WaitForSeconds(Random.Range(0.2f, 0.8f));
    }

    private IEnumerator ReturnToEntranceAndDespawn()
    {
        if (targetBuilding != null)
            targetBuilding.NotifyCustomerLeaving(this);

        Vector3 targetPosition = sourceEntrance != null ? sourceEntrance.DespawnWorldPosition : transform.position;
        yield return MoveToRoutine(targetPosition, false, null);

        ownerEntranceManagement.NotifyCustomerDespawned(this);
        gameObject.SetActive(false);
    }

    private IEnumerator MoveToRoutine(Vector3 targetPosition, bool idleAtEnd, Building notifyBuilding)
    {
        waitingAtCounter = false;

        while (Vector3.Distance(transform.position, targetPosition) > 0.03f)
        {
            Vector3 next = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            UpdateFacing(next.x - transform.position.x);
            body.MovePosition(next);
            PlayTrigger(walkTrigger);
            yield return null;
        }

        body.MovePosition(targetPosition);

        if (idleAtEnd)
        {
            waitingAtCounter = true;
            PlayTrigger(idleTrigger);
        }

        if (notifyBuilding != null)
            notifyBuilding.NotifyCustomerReachedSlot(this);
    }

    private void ResetState()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        if (lifeRoutine != null)
        {
            StopCoroutine(lifeRoutine);
            lifeRoutine = null;
        }

        if (heldItemInstance != null)
            Destroy(heldItemInstance);

        if (targetBuilding != null)
            targetBuilding.NotifyCustomerLeaving(this);

        targetBuilding = null;
        currentPath = null;
        sourceEntrance = null;
        ownerEntranceManagement = null;
        currentQueueSlot = Building.QueueSlot.None;
        waitingAtCounter = false;
        purchaseFinished = false;
        spawnEntryId = string.Empty;
        body.linearVelocity = Vector2.zero;
        PlayTrigger(walkTrigger);
    }

    private void UpdateFacing(float deltaX)
    {
        if (Mathf.Abs(deltaX) < 0.001f)
            return;

        bool shouldFaceRight = deltaX > 0f;
        if (facingRight == shouldFaceRight)
            return;

        facingRight = shouldFaceRight;
        Vector3 angles = transform.localEulerAngles;
        angles.y = facingRight ? 0f : 180f;
        transform.localEulerAngles = angles;
    }

    private void PlayTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(triggerName))
            return;

        animator.SetTrigger(triggerName);
    }
}
