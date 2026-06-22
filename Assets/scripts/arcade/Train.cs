using System.Collections;
using UnityEngine;

public class Train : MonoBehaviour, IReinitializable
{
    [Header("MachineGun Path")]
    public Vector2 startLocalPos;
    public Vector2 endLocalPos;

    [Header("MachineGun")]
    public MachineGun machineGunPrefab;
    public GameObject machineGunExtraBodyPrefab;
    public float endDuration = 5f;
    public float bodyShiftX = 1.5f;
    public float bodyShiftDuration = 1f;

    private const float moveDuration = 1f;

    private MachineGun machineGunInstance;
    private GameObject machineGunExtraBodyInstance;
    private Coroutine routine;
    private Vector3 initialTrainLocalPosition;
    private bool initialTrainLocalPositionCaptured;
    private MechaLeg[] mechaLegs = System.Array.Empty<MechaLeg>();
    private bool machineGunSequenceNotified;

    private void Awake()
    {
        CaptureInitialTrainLocalPosition();
        CacheMechaLegsIfNeeded();
        EnsureMachineGunInstance();
        Reinit();
    }

    private void OnEnable()
    {
        GameData.OnMachineGunTrigger += HandleMachineGunTrigger;
        Reinit();
    }

    private void OnDisable()
    {
        GameData.OnMachineGunTrigger -= HandleMachineGunTrigger;

        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        EndMachineGunSequenceIfNeeded();

        SetMechaLegSpeedMultiplier(1f);
        transform.localPosition = initialTrainLocalPosition;

        if (machineGunInstance != null)
        {
            machineGunInstance.transform.localPosition = ToChildLocalVector3(startLocalPos);
            machineGunInstance.ReinitMountedState();
        }

        if (machineGunExtraBodyInstance != null)
            machineGunExtraBodyInstance.transform.localPosition = ToExtraBodyLocalVector3(startLocalPos);
    }

    public void Reinit()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        EndMachineGunSequenceIfNeeded();

        CaptureInitialTrainLocalPosition();
        CacheMechaLegsIfNeeded();
        transform.localPosition = initialTrainLocalPosition;
        SetMechaLegSpeedMultiplier(1f);

        EnsureMachineGunInstance();
        if (machineGunInstance != null)
        {
            machineGunInstance.transform.localPosition = ToChildLocalVector3(startLocalPos);
            machineGunInstance.ReinitMountedState();
        }

        if (machineGunExtraBodyInstance != null)
            machineGunExtraBodyInstance.transform.localPosition = ToExtraBodyLocalVector3(startLocalPos);
    }

    private void HandleMachineGunTrigger()
    {
        if (!isActiveAndEnabled)
            return;

        if (routine != null)
            return;

        routine = StartCoroutine(CoRunMachineGunSequence());
    }

    private IEnumerator CoRunMachineGunSequence()
    {
        EnsureMachineGunInstance();
        EnsureMachineGunExtraBodyInstance();
        CacheMechaLegsIfNeeded();
        BeginMachineGunSequence();

        yield return MoveTrainBodyX(0f, bodyShiftX, bodyShiftDuration, 1.5f);
        yield return MoveLocal(startLocalPos, endLocalPos, moveDuration);

        if (machineGunInstance != null)
        {
            machineGunInstance.BeginActivation();
            machineGunInstance.BeginPlayerControlImmediate();
        }

        float firingDuration = Mathf.Max(0f, endDuration);
        float stopLeadTime = StageManager.Instance != null
            ? Mathf.Max(0f, StageManager.Instance.machineGunSpawnEndLeadTime)
            : 3.8f;
        float obstacleStopDelay = Mathf.Max(0f, firingDuration - stopLeadTime);

        if (obstacleStopDelay > 0f)
        {
            yield return new WaitForSeconds(obstacleStopDelay);
            GameData.Instance?.NotifyMachineGunObstacleSpawnStop();
            yield return new WaitForSeconds(firingDuration - obstacleStopDelay);
        }
        else
        {
            GameData.Instance?.NotifyMachineGunObstacleSpawnStop();
            yield return new WaitForSeconds(firingDuration);
        }

        if (machineGunInstance != null)
            machineGunInstance.BeginDeactivation();

        yield return MoveTrainBodyX(bodyShiftX, 0f, bodyShiftDuration, 1f);
        yield return MoveLocal(endLocalPos, startLocalPos, moveDuration);
        EndMachineGunSequenceIfNeeded();
        routine = null;
    }

    private void BeginMachineGunSequence()
    {
        if (machineGunSequenceNotified)
            return;

        machineGunSequenceNotified = true;
        GameData.Instance?.NotifyMachineGunSequenceStarted();
    }

    private void EndMachineGunSequenceIfNeeded()
    {
        if (!machineGunSequenceNotified)
            return;

        machineGunSequenceNotified = false;
        GameData.Instance?.NotifyMachineGunSequenceEnded();
    }

    private IEnumerator MoveLocal(Vector2 from, Vector2 to, float duration)
    {
        if (machineGunInstance == null && machineGunExtraBodyInstance == null)
            yield break;

        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.0001f, duration);

        while (elapsed < safeDuration)
        {
            Vector2 pos = Vector2.Lerp(from, to, elapsed / safeDuration);
            if (machineGunInstance != null)
                machineGunInstance.transform.localPosition = ToChildLocalVector3(pos);
            if (machineGunExtraBodyInstance != null)
                machineGunExtraBodyInstance.transform.localPosition = ToExtraBodyLocalVector3(pos);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (machineGunInstance != null)
            machineGunInstance.transform.localPosition = ToChildLocalVector3(to);
        if (machineGunExtraBodyInstance != null)
            machineGunExtraBodyInstance.transform.localPosition = ToExtraBodyLocalVector3(to);
    }

    private void EnsureMachineGunInstance()
    {
        if (machineGunInstance != null)
            return;

        if (machineGunPrefab == null)
            return;

        machineGunInstance = Instantiate(machineGunPrefab, transform);
        machineGunInstance.name = machineGunPrefab.name;
    }

    private void EnsureMachineGunExtraBodyInstance()
    {
        if (machineGunExtraBodyInstance != null)
            return;

        if (machineGunExtraBodyPrefab == null)
            return;

        machineGunExtraBodyInstance = Instantiate(machineGunExtraBodyPrefab, transform);
        machineGunExtraBodyInstance.name = machineGunExtraBodyPrefab.name;
    }

    private IEnumerator MoveTrainBodyX(float fromOffset, float toOffset, float duration, float legSpeedMultiplier)
    {
        SetMechaLegSpeedMultiplier(legSpeedMultiplier);

        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.0001f, duration);

        while (elapsed < safeDuration)
        {
            float x = Mathf.Lerp(fromOffset, toOffset, elapsed / safeDuration);
            SetTrainBodyLocalX(x);
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetTrainBodyLocalX(toOffset);
        SetMechaLegSpeedMultiplier(1f);
    }

    private void SetTrainBodyLocalX(float xOffset)
    {
        transform.localPosition = new Vector3(
            initialTrainLocalPosition.x + xOffset,
            initialTrainLocalPosition.y,
            initialTrainLocalPosition.z
        );
    }

    private void CaptureInitialTrainLocalPosition()
    {
        if (initialTrainLocalPositionCaptured)
            return;

        initialTrainLocalPosition = transform.localPosition;
        initialTrainLocalPositionCaptured = true;
    }

    private void CacheMechaLegsIfNeeded()
    {
        mechaLegs = GetComponentsInChildren<MechaLeg>(true);
    }

    private void SetMechaLegSpeedMultiplier(float multiplier)
    {
        if (mechaLegs == null)
            return;

        for (int i = 0; i < mechaLegs.Length; i++)
        {
            if (mechaLegs[i] == null)
                continue;

            mechaLegs[i].SetExternalSpeedMultiplier(multiplier);
        }
    }

    private Vector3 ToChildLocalVector3(Vector2 localPos)
    {
        float z = machineGunInstance != null
            ? machineGunInstance.transform.localPosition.z
            : 0f;

        return new Vector3(localPos.x, localPos.y, z);
    }

    private Vector3 ToExtraBodyLocalVector3(Vector2 localPos)
    {
        float z = machineGunExtraBodyInstance != null
            ? machineGunExtraBodyInstance.transform.localPosition.z
            : 0f;

        return new Vector3(localPos.x, localPos.y, z);
    }
}
