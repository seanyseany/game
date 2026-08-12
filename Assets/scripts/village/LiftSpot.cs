using System.Collections.Generic;
using UnityEngine;

public class LiftSpot : MonoBehaviour
{
    private static readonly List<LiftSpot> RegisteredLiftSpots = new List<LiftSpot>();

    [SerializeField] private List<Lift> registeredLiftPrefabs = new List<Lift>();

    public IReadOnlyList<Lift> RegisteredLifts => registeredLiftPrefabs;
    public int TotalLiftCount => registeredLiftPrefabs != null ? registeredLiftPrefabs.Count : 0;

    private void OnEnable()
    {
        if (!RegisteredLiftSpots.Contains(this))
            RegisteredLiftSpots.Add(this);

        BindLifts();
    }

    private void OnDisable()
    {
        RegisteredLiftSpots.Remove(this);
    }

    public int GetActiveLiftCount()
    {
        int count = 0;
        for (int i = 0; i < registeredLiftPrefabs.Count; i++)
        {
            Lift lift = registeredLiftPrefabs[i];
            if (lift != null && lift.gameObject.activeSelf)
                count++;
        }

        return count;
    }

    public int GetLiftPrice(int entryIndex)
    {
        if (entryIndex >= 0 && entryIndex < registeredLiftPrefabs.Count && registeredLiftPrefabs[entryIndex] != null)
            return registeredLiftPrefabs[entryIndex].Price;

        for (int i = 0; i < registeredLiftPrefabs.Count; i++)
        {
            if (registeredLiftPrefabs[i] != null)
                return registeredLiftPrefabs[i].Price;
        }

        return 0;
    }

    public bool TryActivateRandomInactiveLift()
    {
        List<Lift> inactiveLifts = new List<Lift>();
        for (int i = 0; i < registeredLiftPrefabs.Count; i++)
        {
            Lift lift = registeredLiftPrefabs[i];
            if (lift != null && !lift.gameObject.activeSelf)
                inactiveLifts.Add(lift);
        }

        if (inactiveLifts.Count == 0)
            return false;

        Lift selected = inactiveLifts[Random.Range(0, inactiveLifts.Count)];
        selected.AssignLiftSpot(this);
        selected.ApplyRuntimeActive(true);
        PushAllLiftStates();
        return true;
    }

    public bool TryFindNearestInactiveLift(Vector3 worldPoint, Lift excludingLift, out Lift targetLift)
    {
        targetLift = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < registeredLiftPrefabs.Count; i++)
        {
            Lift candidate = registeredLiftPrefabs[i];
            if (candidate == null || candidate == excludingLift || candidate.gameObject.activeSelf)
                continue;

            float distance = (candidate.transform.position - worldPoint).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            targetLift = candidate;
        }

        return targetLift != null;
    }

    public void RequestRelocation(Lift sourceLift, Lift targetLift)
    {
        if (sourceLift == null || targetLift == null || sourceLift == targetLift)
            return;

        sourceLift.RequestRelocation(targetLift);
        PushAllLiftStates();
    }

    public void CompleteRelocation(Lift sourceLift, Lift targetLift)
    {
        if (sourceLift == null || targetLift == null)
            return;

        targetLift.AssignLiftSpot(this);
        targetLift.ApplyRuntimeActive(true);
        sourceLift.ApplyRuntimeActive(false);
        PushAllLiftStates();
    }

    public void PrepareRuntimeRestore()
    {
        BindLifts();
        SetAllRegisteredLiftsActive(false);
    }

    public bool TryRestoreLiftState(VillageManagement.LiftState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.liftId))
            return false;

        BindLifts();
        for (int i = 0; i < registeredLiftPrefabs.Count; i++)
        {
            Lift lift = registeredLiftPrefabs[i];
            if (lift == null || !string.Equals(lift.LiftId, state.liftId, System.StringComparison.Ordinal))
                continue;

            lift.ApplyRuntimeActive(state.isActive);
            return true;
        }

        return false;
    }

    private void BindLifts()
    {
        for (int i = 0; i < registeredLiftPrefabs.Count; i++)
        {
            if (registeredLiftPrefabs[i] != null)
                registeredLiftPrefabs[i].AssignLiftSpot(this);
        }
    }

    private void PushAllLiftStates()
    {
        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        for (int i = 0; i < registeredLiftPrefabs.Count; i++)
        {
            Lift lift = registeredLiftPrefabs[i];
            if (lift == null)
                continue;

            villageManagement.UpsertLiftState(new VillageManagement.LiftState
            {
                liftId = lift.LiftId,
                isActive = lift.gameObject.activeSelf
            });
        }
    }

    private void SetAllRegisteredLiftsActive(bool active)
    {
        for (int i = 0; i < registeredLiftPrefabs.Count; i++)
        {
            Lift lift = registeredLiftPrefabs[i];
            if (lift == null)
                continue;

            lift.AssignLiftSpot(this);
            lift.ApplyRuntimeActive(active);
        }
    }
}
