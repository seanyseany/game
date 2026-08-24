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

    [Header("Player Gate Transfer")]
    [Tooltip("머신건 탑승 시 플레이어가 빨려 들어갈 게이트 위치. 비어 있으면 GateHealth 게이트 위치를 사용합니다.")]
    public Transform playerBoardingPoint;
    [Min(0.01f)] public float playerBoardingDuration = 0.35f;
    [Min(0f)] public float playerExitDelay = 0.1f;
    [Min(0f)] public float playerGateOpenLeadTime = 0.2f;
    [Min(0f)] public float playerExitGateOpenDelay = 1.5f;

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

        GateHealth.Instance?.SetMachineGunReturnGateLocked(false);
        GateHealth.Instance?.CloseGate();
        ResolvePlayer()?.CancelMachineGunTransfer();

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

        GateHealth.Instance?.SetMachineGunReturnGateLocked(false);
        GateHealth.Instance?.CloseGate();
        ResolvePlayer()?.CancelMachineGunTransfer();

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
        MachineGunObstacle activeMachineGunObstacle = MachineGunObstacle.CurrentSource;
        BeginMachineGunSequence();

        yield return MoveTrainBodyX(0f, bodyShiftX, bodyShiftDuration, 1.5f);
        yield return MoveLocal(startLocalPos, endLocalPos, moveDuration);

        Player player = ResolvePlayer();
        GateHealth.Instance?.BeginOpenHold();
        if (playerGateOpenLeadTime > 0f)
            yield return new WaitForSeconds(playerGateOpenLeadTime);
        if (player != null)
            yield return player.CoBoardMachineGun(ResolveBoardingPoint(), playerBoardingDuration);
        GateHealth.Instance?.EndOpenHold();

        activeMachineGunObstacle?.BeginMachineGunSpawn();

        if (machineGunInstance != null)
        {
            machineGunInstance.BeginActivation();
            machineGunInstance.BeginPlayerControlImmediate();
        }

        if (activeMachineGunObstacle != null)
            yield return new WaitUntil(activeMachineGunObstacle.IsSpawnSequenceResolved);

        if (machineGunInstance != null)
            machineGunInstance.BeginDeactivation();

        GateHealth.Instance?.SetMachineGunReturnGateLocked(true);
        yield return MoveTrainBodyX(bodyShiftX, 0f, bodyShiftDuration, 1f);
        yield return MoveLocal(endLocalPos, startLocalPos, moveDuration);
        if (playerExitGateOpenDelay > 0f)
            yield return new WaitForSeconds(playerExitGateOpenDelay);
        GateHealth.Instance?.SetMachineGunReturnGateLocked(false);
        GateHealth.Instance?.BeginOpenHold();
        if (playerGateOpenLeadTime > 0f)
            yield return new WaitForSeconds(playerGateOpenLeadTime);
        if (player != null)
            yield return player.CoExitMachineGun(playerExitDelay);
        GateHealth.Instance?.EndOpenHold();
        MachineGunObstacle.SetCurrentSource(null);
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

        MachineGunObstacle.StopActiveMachineGunSpawn();
        MachineGunObstacle.SetCurrentSource(null);
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

    private Transform ResolveBoardingPoint()
    {
        return playerBoardingPoint != null
            ? playerBoardingPoint
            : GateHealth.Instance != null ? GateHealth.Instance.transform
            : machineGunInstance != null ? machineGunInstance.transform : null;
    }

    private static Player ResolvePlayer()
    {
        return Player.Instance != null ? Player.Instance : Object.FindFirstObjectByType<Player>();
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
