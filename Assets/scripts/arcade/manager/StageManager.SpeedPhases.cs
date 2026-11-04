using System.Collections.Generic;
using UnityEngine;

public partial class StageManager
{
    private speedPhases activeSpeedPhaseSettings;
    private PhaseInfo currentSpeedPhase;
    private int speedPhasesRemaining;
    private float speedPhaseMultiplier = 1f;
    private float speedPhaseEntryElapsed;
    private float speedPhaseEntryMultiplier = 1f;
    private float speedPhaseSteadyMultiplier = 1f;
    private float speedPhaseEntryHoldDuration;
    private float speedPhaseEntryReturnDuration;
    private bool waitingForSpeedPhaseStart;
    private bool waitingForSpeedPhaseEnd;
    private bool forceNormalPhaseAfterSpeedPhase;
    private speedPhases interruptedSpeedPhaseSettings;
    private int normalPhasesBeforeSpeedRestart;
    private PhaseInfo currentSpeedRecoveryNormalPhase;
    private int deferredSpeedPhaseBossStage;
    private readonly List<GameObject> speedPhaseCandidates = new List<GameObject>();
    private readonly List<GameObject> speedPhaseBag = new List<GameObject>();
    private Player speedPhaseEffectPlayer;
    private string speedPhaseEffectPoolTag;
    private float nextSpeedPhaseEffectSpawnTime = -1f;
    private float lastSpeedPhaseEffectSpawnTime = -1f;
    private float speedPhaseEffectWidth;
    private float speedPhaseEffectOpacity = 1f;
    private readonly List<speedEffect> activeSpeedPhaseEffects = new List<speedEffect>();

    public bool IsSpeedPhaseSequenceActive => activeSpeedPhaseSettings != null;
    public bool IsSpeedPhaseSpeedActive => IsSpeedPhaseSequenceActive && !waitingForSpeedPhaseStart && isActiveAndEnabled &&
        (GameData.Instance == null || (!GameData.Instance.gameOver && !GameData.Instance.IsResetting));
    public float SpecialPhaseSpeedMultiplier => IsSpeedPhaseSpeedActive ? speedPhaseMultiplier : 1f;
    public float SpecialPhaseSteadySpeedMultiplier => IsSpeedPhaseSpeedActive ? speedPhaseSteadyMultiplier : 1f;

    private bool TryStartSpeedPhaseSequence(speedPhases settings, bool countTowardPhaseProgress, bool runPhaseRolls)
    {
        speedPhaseCandidates.Clear();
        if (!IsUsableSpeedPhasePrefab(settings.startPhasePrefab))
        {
            Debug.LogWarning("[StageManager] speedPhases의 Start Phase Prefab에 PhaseEndTrigger가 있는 시작 페이즈를 등록해 주세요.", settings);
            return false;
        }
        if (!IsUsableSpeedPhasePrefab(settings.endPhasePrefab))
        {
            Debug.LogWarning("[StageManager] speedPhases의 End Phase Prefab에 PhaseEndTrigger가 있는 종료 페이즈를 등록해 주세요.", settings);
            return false;
        }
        if (settings.phasePrefabs != null)
        {
            foreach (GameObject prefab in settings.phasePrefabs)
            {
                if (prefab == null || speedPhaseCandidates.Contains(prefab))
                    continue;
                // A nested special sequence or a separate pause trigger would own the same spawn flow.
                if (!IsUsableSpeedPhasePrefab(prefab))
                {
                    Debug.LogWarning($"[StageManager] speedPhases의 {prefab.name}은 사용할 수 없습니다. PhaseEndTrigger가 있는 일반 페이즈를 등록해 주세요.", settings);
                    continue;
                }
                speedPhaseCandidates.Add(prefab);
            }
        }

        if (speedPhaseCandidates.Count == 0)
        {
            Debug.LogWarning("[StageManager] speedPhases에 유효한 페이즈가 없어 일반 페이즈로 복귀합니다.", settings);
            return false;
        }

        activeSpeedPhaseSettings = settings;
        speedPhasesRemaining = Mathf.Max(1, settings.phaseSpawnCount);
        speedPhaseMultiplier = 1f;
        waitingForSpeedPhaseStart = true;
        waitingForSpeedPhaseEnd = false;
        speedPhaseBag.Clear();
        forceNormalPhaseAfterSpeedPhase = false;
        currentSpeedPhase = SpawnSelectedPhase(settings.startPhasePrefab, GetCurrentPhaseStage(), false, false, false, true);

        // The whole special sequence occupies one ordinary stage slot.
        if (countTowardPhaseProgress)
        {
            phaseSpawnCount++;
            PrewarmUpcomingStage();
        }
        if (runPhaseRolls && GameData.Instance != null)
            GameData.Instance.CheckBossTriggerBeforeSpeedUp(this, phaseSpawnCount);
        return true;
    }

    private static bool IsUsableSpeedPhasePrefab(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<speedPhases>() == null &&
            !prefab.CompareTag(MachineGunStageTag) && !prefab.CompareTag(MiniBossStageTag) &&
            prefab.GetComponentInChildren<PhaseEndTrigger>(true) != null;
    }

    private void SpawnNextSpeedPhase()
    {
        if (speedPhaseBag.Count == 0)
        {
            speedPhaseBag.AddRange(speedPhaseCandidates);
            ShuffleList(speedPhaseBag);
        }
        int index = speedPhaseBag.Count - 1;
        GameObject prefab = speedPhaseBag[index];
        speedPhaseBag.RemoveAt(index);
        speedPhasesRemaining--;
        currentSpeedPhase = SpawnSelectedPhase(prefab, GetCurrentPhaseStage(), false, false, false, true);
    }

    private void AdvanceSpeedPhaseSequence()
    {
        if (!IsSpeedPhaseSequenceActive || currentSpeedPhase == null || !currentSpeedPhase.passedEndTrigger)
            return;

        bool starting = waitingForSpeedPhaseStart;
        if (waitingForSpeedPhaseStart)
        {
            waitingForSpeedPhaseStart = false;
            BeginSpeedPhaseEntrySpeed();
            StartSpeedPhaseEffects();
        }

        if (speedPhasesRemaining > 0)
        {
            SpawnNextSpeedPhase();
            if (starting)
            {
                ArcadeFeedback.ShowSpeedMode();
            }
            return;
        }

        if (!waitingForSpeedPhaseEnd)
        {
            waitingForSpeedPhaseEnd = true;
            currentSpeedPhase = SpawnSelectedPhase(activeSpeedPhaseSettings.endPhasePrefab,
                GetCurrentPhaseStage(), false, false, false, true);
            return;
        }

        StopSpeedPhaseEffects(despawnExisting: false);
        ResetSpeedPhaseSequenceState();
        Player player = GameData.Instance != null ? GameData.Instance.playerRef : null;
        if (player == null)
            player = Object.FindFirstObjectByType<Player>();
        if (player != null)
            player.NotifySpeedPhaseCompleted();
        if (TryStartDeferredSpeedPhaseBoss())
            return;
        forceNormalPhaseAfterSpeedPhase = true;
        SpawnPhase();
    }

    private void BeginSpeedPhaseEntrySpeed()
    {
        speedPhaseSteadyMultiplier = SanitizeSpeedPhaseMultiplier(activeSpeedPhaseSettings.speedMultiplier);
        speedPhaseEntryMultiplier = SanitizeSpeedPhaseMultiplier(activeSpeedPhaseSettings.entrySpeedMultiplier);
        speedPhaseEntryHoldDuration = SanitizeSpeedPhaseDuration(activeSpeedPhaseSettings.entryHoldDuration);
        speedPhaseEntryReturnDuration = SanitizeSpeedPhaseDuration(activeSpeedPhaseSettings.entryReturnDuration);
        speedPhaseEntryElapsed = 0f;
        speedPhaseMultiplier = speedPhaseEntryHoldDuration + speedPhaseEntryReturnDuration > 0f
            ? speedPhaseEntryMultiplier : speedPhaseSteadyMultiplier;
    }

    private void UpdateSpeedPhaseEntrySpeed(float deltaTime)
    {
        if (!IsSpeedPhaseSpeedActive || gameplayPauseByTransform || RageTransformFreezeController.ShouldSkipGameplayFrame())
            return;

        float totalDuration = speedPhaseEntryHoldDuration + speedPhaseEntryReturnDuration;
        if (speedPhaseEntryElapsed >= totalDuration)
            return;

        speedPhaseEntryElapsed = Mathf.Min(totalDuration, speedPhaseEntryElapsed + Mathf.Max(0f, deltaTime));
        if (speedPhaseEntryElapsed < speedPhaseEntryHoldDuration)
        {
            speedPhaseMultiplier = speedPhaseEntryMultiplier;
            return;
        }

        float progress = speedPhaseEntryReturnDuration > 0f
            ? Mathf.Clamp01((speedPhaseEntryElapsed - speedPhaseEntryHoldDuration) / speedPhaseEntryReturnDuration)
            : 1f;
        speedPhaseMultiplier = Mathf.Lerp(speedPhaseEntryMultiplier, speedPhaseSteadyMultiplier, progress);
    }

    private static float SanitizeSpeedPhaseMultiplier(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(0.01f, value);

    private static float SanitizeSpeedPhaseDuration(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);

    private void InterruptSpeedPhaseSequenceForRage()
    {
        if (IsSpeedPhaseSequenceActive)
            interruptedSpeedPhaseSettings = activeSpeedPhaseSettings;
        if (interruptedSpeedPhaseSettings != null)
            normalPhasesBeforeSpeedRestart = 2;
        currentSpeedRecoveryNormalPhase = null;
        StopSpeedPhaseEffects();
        ResetSpeedPhaseSequenceState();
    }

    private void SpawnSpeedPhaseRecovery()
    {
        // Only the current recovery phase's detector may advance this flow.
        if (currentSpeedRecoveryNormalPhase != null && !currentSpeedRecoveryNormalPhase.passedEndTrigger)
            return;

        currentSpeedRecoveryNormalPhase = null;
        if (normalPhasesBeforeSpeedRestart > 0)
        {
            currentSpeedRecoveryNormalPhase = SpawnNormalPhaseAfterSpeedPhase();
            if (currentSpeedRecoveryNormalPhase != null)
            {
                normalPhasesBeforeSpeedRestart--;
                return;
            }
        }

        speedPhases settings = interruptedSpeedPhaseSettings;
        interruptedSpeedPhaseSettings = null;
        normalPhasesBeforeSpeedRestart = 0;
        // The original selection already consumed its stage slot and special roll.
        if (settings != null && TryStartSpeedPhaseSequence(settings, false, false))
            return;
        if (!TryStartDeferredSpeedPhaseBoss())
            SpawnNormalPhaseAfterSpeedPhase();
    }

    private bool TryDeferBossForSpeedPhase(int stage)
    {
        if (!IsSpeedPhaseSequenceActive && interruptedSpeedPhaseSettings == null)
            return false;
        deferredSpeedPhaseBossStage = stage;
        pendingMachineGunBossStage = 0;
        return true;
    }

    private bool TryStartDeferredSpeedPhaseBoss()
    {
        if (deferredSpeedPhaseBossStage == 0)
            return false;
        int stage = deferredSpeedPhaseBossStage;
        deferredSpeedPhaseBossStage = 0;
        StartBossEncounterNow(stage);
        return true;
    }

    private void CancelSpeedPhaseSequence()
    {
        StopSpeedPhaseEffects();
        ResetSpeedPhaseSequenceState();
        interruptedSpeedPhaseSettings = null;
        normalPhasesBeforeSpeedRestart = 0;
        currentSpeedRecoveryNormalPhase = null;
        deferredSpeedPhaseBossStage = 0;
    }

    private void ResetSpeedPhaseSequenceState()
    {
        activeSpeedPhaseSettings = null;
        currentSpeedPhase = null;
        speedPhasesRemaining = 0;
        speedPhaseMultiplier = 1f;
        speedPhaseEntryElapsed = 0f;
        speedPhaseEntryMultiplier = speedPhaseSteadyMultiplier = 1f;
        speedPhaseEntryHoldDuration = speedPhaseEntryReturnDuration = 0f;
        waitingForSpeedPhaseStart = false;
        waitingForSpeedPhaseEnd = false;
        forceNormalPhaseAfterSpeedPhase = false;
        speedPhaseCandidates.Clear();
        speedPhaseBag.Clear();
    }

    private void StartSpeedPhaseEffects()
    {
        if (activeSpeedPhaseSettings.speedEffectPrefab == null)
            return;
        speedPhaseEffectPlayer = GameData.Instance != null ? GameData.Instance.playerRef : null;
        if (speedPhaseEffectPlayer == null)
            speedPhaseEffectPlayer = Object.FindFirstObjectByType<Player>();
        if (speedPhaseEffectPlayer == null)
            return;

        speedPhaseEffectPoolTag = "SpeedPhaseEffect_" + activeSpeedPhaseSettings.speedEffectPrefab.GetInstanceID();
        speedPhaseEffectPlayer.PrepareSpeedEffectPool(activeSpeedPhaseSettings.speedEffectPrefab,
            speedPhaseEffectPoolTag, activeSpeedPhaseSettings.speedEffectMoveSpeed);
        speedPhaseEffectOpacity = 1f;
        speedPhaseEffectWidth = 0f;
        lastSpeedPhaseEffectSpawnTime = -1f;
        nextSpeedPhaseEffectSpawnTime = Time.time;
        TrySpawnSpeedPhaseEffects();
    }

    private void TrySpawnSpeedPhaseEffects()
    {
        for (int i = activeSpeedPhaseEffects.Count - 1; i >= 0; i--)
            if (activeSpeedPhaseEffects[i] == null || !activeSpeedPhaseEffects[i].gameObject.activeInHierarchy)
                activeSpeedPhaseEffects.RemoveAt(i);

        if (!IsSpeedPhaseSpeedActive || gameplayPauseByTransform || RageTransformFreezeController.ShouldSkipGameplayFrame() ||
            speedPhaseEffectPlayer == null || activeSpeedPhaseSettings.speedEffectPrefab == null ||
            nextSpeedPhaseEffectSpawnTime < 0f)
            return;

        float targetOpacity = GameData.Instance == null || GameData.Instance.CanSpawnSpeedEffects() ? 1f : 0f;
        float fadeDuration = Mathf.Max(0f, activeSpeedPhaseSettings.speedEffectFadeDuration);
        speedPhaseEffectOpacity = fadeDuration > 0f
            ? Mathf.MoveTowards(speedPhaseEffectOpacity, targetOpacity, Time.deltaTime / fadeDuration)
            : targetOpacity;

        float travelMultiplier = GameData.Instance != null
            ? GameData.Instance.GetStageSpeedMultIgnoringObstacleSlowdown() : SpecialPhaseSpeedMultiplier;
        float travelSpeed = Mathf.Max(0f, activeSpeedPhaseSettings.speedEffectMoveSpeed) * travelMultiplier;
        float interval = GetSpeedPhaseEffectInterval(travelMultiplier, travelSpeed);
        if (lastSpeedPhaseEffectSpawnTime >= 0f)
            nextSpeedPhaseEffectSpawnTime = Mathf.Min(nextSpeedPhaseEffectSpawnTime, lastSpeedPhaseEffectSpawnTime + interval);

        if (Time.time >= nextSpeedPhaseEffectSpawnTime)
        {
            int previousCount = activeSpeedPhaseEffects.Count;
            speedPhaseEffectPlayer.SpawnSpeedEffectWave(activeSpeedPhaseSettings.speedEffectPrefab,
                speedPhaseEffectPoolTag, activeSpeedPhaseEffects, Mathf.Max(0f, activeSpeedPhaseSettings.speedEffectMoveSpeed));
            if (speedPhaseEffectWidth <= 0f && activeSpeedPhaseEffects.Count > previousCount)
            {
                speedPhaseEffectWidth = activeSpeedPhaseEffects[previousCount].HorizontalSpan;
                speedPhaseEffectPlayer.PrepareSpeedEffectPool(activeSpeedPhaseSettings.speedEffectPrefab,
                    speedPhaseEffectPoolTag, activeSpeedPhaseSettings.speedEffectMoveSpeed, GetSpeedPhaseEffectSpacing(travelMultiplier));
            }
            lastSpeedPhaseEffectSpawnTime = Time.time;
            nextSpeedPhaseEffectSpawnTime = Time.time + GetSpeedPhaseEffectInterval(travelMultiplier, travelSpeed);
        }

        // New waves inherit the current fade, so they cannot flash during slowdown.
        foreach (speedEffect effect in activeSpeedPhaseEffects)
        {
            effect.SetTravelSpeedMultiplier(travelMultiplier);
            effect.SetOpacity(speedPhaseEffectOpacity);
        }
    }

    private float GetSpeedPhaseEffectSpacing(float travelMultiplier)
    {
        float steadyMultiplier = travelMultiplier / SpecialPhaseSpeedMultiplier * SpecialPhaseSteadySpeedMultiplier;
        float spacing = activeSpeedPhaseSettings.speedEffectMoveSpeed * steadyMultiplier *
            Mathf.Max(0.01f, speedPhaseEffectPlayer.speedEffectSpawnInterval);
        // Slight overlap hides transparent sprite edges and keeps adjacent strips connected.
        if (speedPhaseEffectWidth > 0f)
            spacing = Mathf.Min(spacing, speedPhaseEffectWidth * 0.9f);
        return Mathf.Max(0.01f, spacing);
    }

    private float GetSpeedPhaseEffectInterval(float travelMultiplier, float travelSpeed) => travelSpeed > 0f
        ? Mathf.Max(0.01f, GetSpeedPhaseEffectSpacing(travelMultiplier) / travelSpeed)
        : Mathf.Max(0.01f, speedPhaseEffectPlayer.speedEffectSpawnInterval);

    private void StopSpeedPhaseEffects(bool despawnExisting = true)
    {
        nextSpeedPhaseEffectSpawnTime = -1f;
        lastSpeedPhaseEffectSpawnTime = -1f;
        if (despawnExisting)
        {
            foreach (speedEffect effect in activeSpeedPhaseEffects)
            {
                if (effect == null || !effect.gameObject.activeInHierarchy)
                    continue;
                effect.gameObject.SetActive(false);
                effect.DespawnNow();
            }
            activeSpeedPhaseEffects.Clear();
        }
        else
        {
            float fadeDuration = activeSpeedPhaseSettings != null ? activeSpeedPhaseSettings.speedEffectFadeDuration : 1f;
            foreach (speedEffect effect in activeSpeedPhaseEffects)
                if (effect != null && effect.gameObject.activeInHierarchy)
                    effect.FadeOut(fadeDuration);
        }
        speedPhaseEffectPlayer = null;
        speedPhaseEffectPoolTag = null;
    }

    private PhaseInfo SpawnNormalPhaseAfterSpeedPhase()
    {
        forceNormalPhaseAfterSpeedPhase = false;
        int stage = GetCurrentPhaseStage();
        if (stage != TestPhaseStage)
            TryRollStage1BackgroundChange();
        var normals = new List<GameObject>();
        AddAvailableNormalPrefabs(normals, GetPhasePrefabsForStage(stage), allowMachineGunStage: false);
        normals.RemoveAll(prefab => prefab.CompareTag(MiniBossStageTag) || prefab.GetComponent<speedPhases>() != null);
        SpecialPhaseEntry[] specials = GetSpecialPhaseEntriesForStage(stage);
        if (specials != null)
            foreach (SpecialPhaseEntry entry in specials)
                if (entry != null)
                    normals.RemoveAll(prefab => prefab == entry.prefab);

        if (normals.Count == 0)
        {
            Debug.LogWarning("[StageManager] speedPhases 종료 후 사용할 일반 페이즈를 해당 스테이지에 등록해 주세요.", this);
            return null;
        }

        GameObject selected = null;
        if (phaseShuffleByStage.TryGetValue(stage, out var cycle))
        {
            for (int i = 0; i < cycle.Count; i++)
            {
                if (!normals.Contains(cycle[i]))
                    continue;
                selected = cycle[i];
                cycle.RemoveAt(i);
                break;
            }
        }
        if (selected == null)
            selected = normals[Random.Range(0, normals.Count)];
        bool advancesStage = stage != TestPhaseStage;
        return SpawnSelectedPhase(selected, stage, false, advancesStage, advancesStage);
    }
}
