using System.Collections;
using UnityEngine;

public class OwnerBlood : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;

    [Header("Serving")]
    [SerializeField] private GameObject itemPrefab;
    [SerializeField] private EnergyUI energyUI;
    [SerializeField] private Transform handPoint;
    [SerializeField] private float itemPickupPause = 0.2f;
    [SerializeField] private float itemGivePause = 0.25f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string walkTrigger = "Walk";
    [SerializeField] private string liftTrigger = "Lift";

    private Building building;
    private Coroutine behaviourRoutine;
    private bool facingRight = true;

    public void BindBuilding(Building targetBuilding)
    {
        building = targetBuilding;
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
        if (customer == null || building == null)
            return;

        if (behaviourRoutine != null)
            StopCoroutine(behaviourRoutine);

        behaviourRoutine = StartCoroutine(ServeRoutine(customer));
    }

    private IEnumerator PatrolLoop()
    {
        while (true)
        {
            if (building == null)
            {
                yield return null;
                continue;
            }

            float targetX = Random.Range(building.OwnerPatrolMinLocalX, building.OwnerPatrolMaxLocalX);
            Vector3 targetPosition = building.transform.TransformPoint(new Vector3(targetX, transform.localPosition.y, 0f));
            yield return MoveTo(targetPosition);
            yield return new WaitForSeconds(Random.Range(0.5f, 1.5f));
        }
    }

    private IEnumerator ServeRoutine(CustomerBlood customer)
    {
        GameObject spawnedItem = null;

        yield return MoveTo(building.ItemPoint.position);
        PlayTrigger(liftTrigger);
        yield return new WaitForSeconds(itemPickupPause);

        if (itemPrefab != null)
        {
            Transform origin = handPoint != null ? handPoint : transform;
            spawnedItem = Instantiate(itemPrefab, origin.position, origin.rotation, origin);
        }

        yield return MoveTo(building.OwnerPoint.position);
        PlayTrigger(liftTrigger);
        yield return new WaitForSeconds(itemGivePause);

        if (spawnedItem != null)
            Destroy(spawnedItem);

        customer.ReceivePurchasedItem(itemPrefab);

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement != null)
            villageManagement.AddEnergy(building.EnergyValue);

        building.CompleteService(customer);
        behaviourRoutine = StartCoroutine(PatrolLoop());
    }

    private IEnumerator MoveTo(Vector3 targetPosition)
    {
        while (Vector3.Distance(transform.position, targetPosition) > 0.025f)
        {
            Vector3 next = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            UpdateFacing(next.x - transform.position.x);
            PlayTrigger(walkTrigger);
            transform.position = next;
            yield return null;
        }
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
