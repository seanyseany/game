using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Train : MonoBehaviour, IReinitializeOnEnable
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

    [Header("Normal Movement")]
    public float normalRandomMinOffset = -0.5f;
    public float normalRandomMaxOffset = 0.5f;
    [Min(0.01f)] public float normalRandomMoveDuration = 1f;

    [Header("Rage / Speed Phase Movement")]
    [Tooltip("분노 시 후퇴 거리입니다. 정상 속도의 스피드 구간에서는 이 거리와 랜덤 이동 오프셋의 절반을 사용하며, 이동 시간은 동일합니다.")]
    [Min(0f)] public float ragePushBackDistance = 4f;
    [Min(0f)] public float ragePushBackDuration = 0.5f;
    [Tooltip("뒤로 밀린 위치를 기준으로 랜덤 이동할 X 오프셋 범위입니다.")]
    [Min(0f)] public float rageRandomMinOffset = 2.2f;
    [FormerlySerializedAs("rageRecoveryDistance")]
    [Min(0f)] public float rageRandomMaxOffset = 3.4f;
    [FormerlySerializedAs("rageRecoveryDuration")]
    [Min(0.01f)] public float rageRandomMoveDuration = 1f;
    [Min(0f)] public float rageReturnDuration = 1f;

    [Header("O2 Obstacle Hit Flash")]
    [Tooltip("산소를 깎는 장애물 충돌 시 빨간색 강도. 0 = 원래 색, 1 = 완전한 빨간색. 원래 투명도는 유지합니다.")]
    [Range(0f, 1f)] public float oxygenHitRedIntensity = 1f;
    [Tooltip("산소를 깎는 장애물 충돌 시 빨간색을 유지하는 시간(초)입니다.")]
    [Min(0f)] public float oxygenHitFlashDuration = 0.2f;

    [Header("Player Gate Transfer")]
    [Tooltip("머신건 탑승 시 플레이어가 빨려 들어갈 게이트 위치. 비어 있으면 GateHealth 게이트 위치를 사용합니다.")]
    public Transform playerBoardingPoint;
    [Min(0.01f)] public float playerBoardingDuration = 0.35f;
    [Min(0f)] public float playerExitDelay = 0.1f;
    [Min(0f)] public float playerGateOpenLeadTime = 0.2f;
    [Min(0f)] public float playerExitGateOpenDelay = 1.5f;
    [Min(0f)] public float playerExitGatePreOpenLeadTime = 1f;

    private const float moveDuration = 1f;

    private MachineGun machineGunInstance;
    private GameObject machineGunExtraBodyInstance;
    private Coroutine routine;
    private Coroutine rageMovementRoutine;
    private bool rageMovementRequested;
    private bool pushBackMovementActive;
    private float pushBackMovementScale;
    private float machineGunBodyOffsetX;
    private float rageBodyOffsetX;
    private float normalBodyOffsetX;
    private float normalMoveStartX;
    private float normalMoveTargetX;
    private float normalMoveElapsed;
    private bool normalMoveActive;
    private Vector3 initialTrainLocalPosition;
    private bool initialTrainLocalPositionCaptured;
    private MechaLeg[] mechaLegs = System.Array.Empty<MechaLeg>();
    private bool machineGunSequenceNotified;
    private readonly Dictionary<SpriteRenderer, Color> oxygenHitColors = new Dictionary<SpriteRenderer, Color>();
    private float oxygenHitFlashUntil = -1f;

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
        GameData.OnRageStart += HandleRageStart;
        GameData.OnRageEnd += HandleRageEnd;
        GateHealth.OnGateBroken += HandleGateBroken;
        Reinit();
    }

    private void OnDisable()
    {
        RestoreOxygenHitColors();
        GameData.OnMachineGunTrigger -= HandleMachineGunTrigger;
        GameData.OnRageStart -= HandleRageStart;
        GameData.OnRageEnd -= HandleRageEnd;
        GateHealth.OnGateBroken -= HandleGateBroken;
        ResetRageMovement();

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
        RestoreOxygenHitColors();
        ResetRageMovement();

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

    public void FlashOxygenLoss()
    {
        if (oxygenHitFlashDuration <= 0f || oxygenHitRedIntensity <= 0f)
        {
            RestoreOxygenHitColors();
            return;
        }
        foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sprite.GetComponentInParent<Player>() != null)
                continue;
            if (!oxygenHitColors.ContainsKey(sprite))
                oxygenHitColors.Add(sprite, sprite.color);
            Color original = oxygenHitColors[sprite];
            sprite.color = GetOxygenHitColor(original);
        }
        oxygenHitFlashUntil = Time.unscaledTime + oxygenHitFlashDuration;
    }

    private Color GetOxygenHitColor(Color original) => Color.Lerp(
        original, new Color(1f, 0f, 0f, original.a), Mathf.Clamp01(oxygenHitRedIntensity));

    private void LateUpdate()
    {
        if (oxygenHitFlashUntil < 0f)
            return;
        if (Time.unscaledTime >= oxygenHitFlashUntil)
        {
            RestoreOxygenHitColors();
            return;
        }
        foreach (var entry in oxygenHitColors)
            if (entry.Key != null)
                entry.Key.color = GetOxygenHitColor(entry.Value);
    }

    private void RestoreOxygenHitColors()
    {
        foreach (var entry in oxygenHitColors)
            if (entry.Key != null)
                entry.Key.color = entry.Value;
        oxygenHitColors.Clear();
        oxygenHitFlashUntil = -1f;
    }

    private void Update()
    {
        SyncPushBackMovement();

        GameData gameData = GameData.Instance;
        Player player = Player.Instance;
        if (gameData == null || gameData.IsResetting || gameData.gameOver || IsGateBroken() || gameData.rageMode ||
            gameData.IsMachineGunSequenceActive() || routine != null || machineGunSequenceNotified ||
            rageMovementRoutine != null || RageTransformFreezeController.ShouldSkipGameplayFrame() ||
            player == null || !player.isActiveAndEnabled || player.IsSpawnOrTransferActive || player.IsRageModeActive())
        {
            return;
        }

        if (!normalMoveActive)
        {
            float minOffset = Mathf.Min(normalRandomMinOffset, normalRandomMaxOffset);
            float maxOffset = Mathf.Max(normalRandomMinOffset, normalRandomMaxOffset);
            normalMoveStartX = normalBodyOffsetX;
            normalMoveTargetX = Random.Range(minOffset, maxOffset);
            normalMoveElapsed = 0f;
            normalMoveActive = true;
        }

        float duration = Mathf.Max(0.01f, normalRandomMoveDuration);
        normalMoveElapsed = Mathf.Min(normalMoveElapsed + Time.deltaTime, duration);
        normalBodyOffsetX = Mathf.Lerp(normalMoveStartX, normalMoveTargetX, normalMoveElapsed / duration);
        ApplyTrainBodyPosition();
        if (normalMoveElapsed >= duration)
            normalMoveActive = false;
    }

    private void HandleRageStart()
    {
        rageMovementRequested = true;
        SyncPushBackMovement();
    }

    private void HandleRageEnd()
    {
        rageMovementRequested = false;
        SyncPushBackMovement();
    }

    private void SyncPushBackMovement()
    {
        if (!isActiveAndEnabled || IsGateBroken())
            return;

        GameData gameData = GameData.Instance;
        bool speedPhaseMovementRequested = StageManager.Instance != null &&
            StageManager.Instance.IsSpeedPhaseSpeedActive && gameData != null && gameData.CanSpawnSpeedEffects();
        float requestedScale = rageMovementRequested ? 1f : speedPhaseMovementRequested ? 0.5f : 0f;
        bool shouldPushBack = requestedScale > 0f;
        if (pushBackMovementActive == shouldPushBack && Mathf.Approximately(pushBackMovementScale, requestedScale))
            return;

        pushBackMovementActive = shouldPushBack;
        pushBackMovementScale = requestedScale;
        StopRageMovement();
        if (shouldPushBack)
        {
            // Start at the visible position, including any unfinished normal movement.
            rageBodyOffsetX += normalBodyOffsetX;
            normalBodyOffsetX = 0f;
            normalMoveActive = false;
            rageMovementRoutine = StartCoroutine(CoRagePushBack());
        }
        else
        {
            rageMovementRoutine = StartCoroutine(CoRageReturn());
        }
    }

    private void HandleGateBroken()
    {
        normalMoveActive = false;
        pushBackMovementActive = false;
        pushBackMovementScale = 0f;
        StopRageMovement();
    }

    private static bool IsGateBroken()
    {
        return GateHealth.Instance != null && GateHealth.Instance.IsBroken;
    }

    private IEnumerator CoRageReturn()
    {
        yield return MoveRageBodyX(0f, rageReturnDuration);
        rageMovementRoutine = null;
    }

    private IEnumerator CoRagePushBack()
    {
        // Let all rage-start handlers finish starting the transform freeze first.
        yield return null;
        yield return MoveRageBodyX(-ragePushBackDistance * pushBackMovementScale, ragePushBackDuration);

        // Targets stay relative to the pushed-back position, so movement never accumulates.
        // Return from the current position once neither rage nor the speed phase needs it.
        while (true)
        {
            float minOffset = Mathf.Max(0f, Mathf.Min(rageRandomMinOffset, rageRandomMaxOffset));
            float maxOffset = Mathf.Max(minOffset, Mathf.Max(rageRandomMinOffset, rageRandomMaxOffset));
            float targetOffset = (-ragePushBackDistance + Random.Range(minOffset, maxOffset)) * pushBackMovementScale;
            yield return MoveRageBodyX(targetOffset, Mathf.Max(0.01f, rageRandomMoveDuration));
        }
    }

    private IEnumerator MoveRageBodyX(float targetOffset, float duration)
    {
        float startOffset = rageBodyOffsetX;
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.0001f, duration);

        while (elapsed < safeDuration)
        {
            if (RageTransformFreezeController.ShouldSkipGameplayFrame())
            {
                yield return null;
                continue;
            }

            elapsed = Mathf.Min(elapsed + Time.deltaTime, safeDuration);
            rageBodyOffsetX = Mathf.Lerp(startOffset, targetOffset, elapsed / safeDuration);
            ApplyTrainBodyPosition();
            yield return null;
        }
    }

    private void StopRageMovement()
    {
        if (rageMovementRoutine == null)
            return;

        StopCoroutine(rageMovementRoutine);
        rageMovementRoutine = null;
    }

    private void ResetRageMovement()
    {
        StopRageMovement();
        rageMovementRequested = false;
        pushBackMovementActive = false;
        pushBackMovementScale = 0f;
        rageBodyOffsetX = 0f;
        machineGunBodyOffsetX = 0f;
        normalBodyOffsetX = 0f;
        normalMoveActive = false;
    }

    private void HandleMachineGunTrigger()
    {
        if (!isActiveAndEnabled)
            return;

        if (routine != null)
            return;

        // Let the machine-gun movement take over without snapping back to the origin.
        machineGunBodyOffsetX += normalBodyOffsetX;
        normalBodyOffsetX = 0f;
        normalMoveActive = false;
        routine = StartCoroutine(CoRunMachineGunSequence());
    }

    private IEnumerator CoRunMachineGunSequence()
    {
        EnsureMachineGunInstance();
        EnsureMachineGunExtraBodyInstance();
        CacheMechaLegsIfNeeded();
        MachineGunObstacle activeMachineGunObstacle = MachineGunObstacle.CurrentSource;
        BeginMachineGunSequence();

        yield return MoveTrainBodyX(machineGunBodyOffsetX, bodyShiftX, bodyShiftDuration, 1.5f);
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

        yield return MoveTrainBodyX(bodyShiftX, 0f, bodyShiftDuration, 1f);
        yield return MoveLocal(endLocalPos, startLocalPos, moveDuration);

        float preOpenLeadTime = Mathf.Min(playerExitGatePreOpenLeadTime, playerExitGateOpenDelay);
        float waitBeforePreOpen = Mathf.Max(0f, playerExitGateOpenDelay - preOpenLeadTime);
        if (waitBeforePreOpen > 0f)
            yield return new WaitForSeconds(waitBeforePreOpen);

        // Keep the player gate open before the exit intro begins. An O2 suction hold can share it.
        GateHealth.Instance?.BeginOpenHold();
        if (preOpenLeadTime > 0f)
            yield return new WaitForSeconds(preOpenLeadTime);
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
            if (RageTransformFreezeController.ShouldSkipGameplayFrame())
            {
                yield return null;
                continue;
            }

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
        machineGunBodyOffsetX = xOffset;
        ApplyTrainBodyPosition();
    }

    private void ApplyTrainBodyPosition()
    {
        transform.localPosition = new Vector3(
            initialTrainLocalPosition.x + machineGunBodyOffsetX + rageBodyOffsetX + normalBodyOffsetX,
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
