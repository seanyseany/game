using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class GateShovel : MonoBehaviour
{
    [Header("Gate Shovel")]
    [FormerlySerializedAs("requiredBloodCount")]
    [Min(1)] public int baseRequiredBloodCount = 4;
    public float closeDelay = 1f;
    public Transform suctionTargetPointA;
    public Transform suctionTargetPointB;

    private readonly HashSet<Blood> bloodsInRange = new HashSet<Blood>();
    private readonly HashSet<Blood> suckingBloods = new HashSet<Blood>();
    private readonly HashSet<Blood> countedBloods = new HashSet<Blood>();

    private Coroutine cycleRoutine;
    private bool cycleStarting;
    private bool cycleGateHoldActive;
    private int bloodCount = 0;
    private int suctionTargetIndex = 0;
    private Collider2D shovelCollider;

    private void Awake()
    {
        shovelCollider = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        GateHealth.OnOpenHoldStarted += HandleGateOpenHoldStarted;
    }

    private void OnDisable()
    {
        GateHealth.OnOpenHoldStarted -= HandleGateOpenHoldStarted;

        if (cycleGateHoldActive)
        {
            GateHealth.Instance?.EndOpenHold();
            cycleGateHoldActive = false;
        }

        cycleRoutine = null;
        cycleStarting = false;
    }

    private void Update()
    {
        CleanupBloodSets();

        if (cycleRoutine == null && !cycleStarting && bloodCount >= GetRequiredBloodCount())
            StartCycle();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        RegisterBlood(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        RegisterBlood(other);
    }

    private void RegisterBlood(Collider2D other)
    {
        Blood blood = other != null ? other.GetComponent<Blood>() : null;
        if (blood == null)
            return;

        RegisterBloodInstance(blood);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        UnregisterBlood(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        RegisterBlood(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        RegisterBlood(collision.collider);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        UnregisterBlood(collision.collider);
    }

    private IEnumerator RunCycle()
    {
        GateHealth.Instance?.BeginOpenHold();
        cycleGateHoldActive = true;

        while (true)
        {
            CleanupBloodSets();
            AssignWaitingBloods();

            if (bloodsInRange.Count == 0 && suckingBloods.Count == 0)
            {
                yield return new WaitForSeconds(closeDelay);
                CleanupBloodSets();
                AssignWaitingBloods();

                if (bloodsInRange.Count == 0 && suckingBloods.Count == 0)
                    break;
            }

            yield return null;
        }

        if (cycleGateHoldActive)
        {
            GateHealth.Instance?.EndOpenHold();
            cycleGateHoldActive = false;
        }
        ResetCycleState();
        cycleRoutine = null;
    }

    private void HandleGateOpenHoldStarted()
    {
        if (!isActiveAndEnabled || cycleRoutine != null || cycleStarting)
            return;

        // A player-driven gate opening should also collect every O2 currently at the shovel.
        CleanupBloodSets();
        if (bloodsInRange.Count == 0)
            return;

        StartCycle();
    }

    private void StartCycle()
    {
        cycleStarting = true;
        cycleRoutine = StartCoroutine(RunCycle());
        cycleStarting = false;
    }

    private void AssignWaitingBloods()
    {
        List<Blood> waitingBloods = new List<Blood>();

        foreach (Blood blood in bloodsInRange)
        {
            if (blood == null || suckingBloods.Contains(blood))
                continue;

            waitingBloods.Add(blood);
        }

        for (int i = 0; i < waitingBloods.Count; i++)
        {
            Blood blood = waitingBloods[i];
            suckingBloods.Add(blood);
            blood.BeginShovelSuction(GetNextSuctionTarget(), shovelCollider, HandleBloodSucked);
        }
    }

    private void HandleBloodSucked(Blood blood)
    {
        bloodsInRange.Remove(blood);
        suckingBloods.Remove(blood);

        if (GameData.Instance != null)
            GameData.Instance.AddO2(1);
    }

    private void ResetCycleState()
    {
        foreach (Blood blood in countedBloods)
        {
            if (blood != null)
                blood.ResetGateShovelTouch();
        }

        countedBloods.Clear();
        bloodCount = 0;
        suctionTargetIndex = 0;
        bloodsInRange.Clear();
        suckingBloods.Clear();
    }

    private Transform GetNextSuctionTarget()
    {
        Transform targetA = suctionTargetPointA != null ? suctionTargetPointA : transform;
        Transform targetB = suctionTargetPointB != null ? suctionTargetPointB : targetA;

        Transform selected = suctionTargetIndex % 2 == 0 ? targetA : targetB;
        suctionTargetIndex++;
        return selected;
    }

    public void RegisterBloodInstance(Blood blood)
    {
        if (blood == null)
            return;

        bloodsInRange.Add(blood);

        if (blood.TryMarkGateShovelTouch())
        {
            countedBloods.Add(blood);
            bloodCount++;
        }
    }

    public void UnregisterBloodInstance(Blood blood)
    {
        if (blood == null)
            return;

        bloodsInRange.Remove(blood);
    }

    private void UnregisterBlood(Collider2D other)
    {
        Blood blood = other != null ? other.GetComponent<Blood>() : null;
        UnregisterBloodInstance(blood);
    }

    private void CleanupBloodSets()
    {
        bloodsInRange.RemoveWhere(IsInvalidBloodReference);
        suckingBloods.RemoveWhere(IsInvalidBloodReference);
        countedBloods.RemoveWhere(IsInvalidBloodReference);
    }

    private static bool IsInvalidBloodReference(Blood blood)
    {
        return blood == null || !blood.gameObject.activeInHierarchy;
    }

    private int GetRequiredBloodCount()
    {
        int gateDamageStep = GateHealth.Instance != null ? GateHealth.Instance.CurrentHits * 2 : 0;
        return Mathf.Max(1, baseRequiredBloodCount + gateDamageStep);
    }
}
