using System.Collections.Generic;
using UnityEngine;

public partial class StageManager
{
    private speedPhases activeSpeedPhaseSettings;
    private PhaseInfo currentSpeedPhase;
    private int speedPhasesRemaining;
    private float speedPhaseMultiplier = 1f;
    private bool waitingForSpeedPhaseStart;
    private bool waitingForSpeedPhaseEnd;
    private bool forceNormalPhaseAfterSpeedPhase;
    private readonly List<GameObject> speedPhaseCandidates = new List<GameObject>();
    private readonly List<GameObject> speedPhaseBag = new List<GameObject>();
    private Player speedPhaseEffectPlayer;
    private string speedPhaseEffectPoolTag;
    private float nextSpeedPhaseEffectSpawnTime = -1f;
    private readonly List<speedEffect> activeSpeedPhaseEffects = new List<speedEffect>();

    public bool IsSpeedPhaseSequenceActive => activeSpeedPhaseSettings != null;
    public bool IsSpeedPhaseSpeedActive => IsSpeedPhaseSequenceActive && !waitingForSpeedPhaseStart && isActiveAndEnabled &&
        (GameData.Instance == null || (!GameData.Instance.gameOver && !GameData.Instance.IsResetting));
    public float SpecialPhaseSpeedMultiplier => IsSpeedPhaseSpeedActive ? speedPhaseMultiplier : 1f;

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

        if (waitingForSpeedPhaseStart)
        {
            waitingForSpeedPhaseStart = false;
            float multiplier = activeSpeedPhaseSettings.speedMultiplier;
            speedPhaseMultiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier)
                ? 1f : Mathf.Max(0.01f, multiplier);
            StartSpeedPhaseEffects();
        }

        if (speedPhasesRemaining > 0)
        {
            SpawnNextSpeedPhase();
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
        forceNormalPhaseAfterSpeedPhase = true;
        SpawnPhase();
    }

    private void CancelSpeedPhaseSequence()
    {
        StopSpeedPhaseEffects();
        ResetSpeedPhaseSequenceState();
    }

    private void ResetSpeedPhaseSequenceState()
    {
        activeSpeedPhaseSettings = null;
        currentSpeedPhase = null;
        speedPhasesRemaining = 0;
        speedPhaseMultiplier = 1f;
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
            nextSpeedPhaseEffectSpawnTime < 0f || Time.time < nextSpeedPhaseEffectSpawnTime)
            return;

        if (GameData.Instance != null && !GameData.Instance.CanSpawnSpeedEffects())
            return;

        nextSpeedPhaseEffectSpawnTime = Time.time + Mathf.Max(0.01f, speedPhaseEffectPlayer.speedEffectSpawnInterval);
        speedPhaseEffectPlayer.SpawnSpeedEffectWave(activeSpeedPhaseSettings.speedEffectPrefab,
            speedPhaseEffectPoolTag, activeSpeedPhaseEffects, Mathf.Max(0f, activeSpeedPhaseSettings.speedEffectMoveSpeed));
    }

    private void StopSpeedPhaseEffects(bool despawnExisting = true)
    {
        nextSpeedPhaseEffectSpawnTime = -1f;
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
        speedPhaseEffectPlayer = null;
        speedPhaseEffectPoolTag = null;
    }

    private void SpawnNormalPhaseAfterSpeedPhase()
    {
        forceNormalPhaseAfterSpeedPhase = false;
        int stage = GetCurrentPhaseStage();
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
            return;
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
        bool isTestPhase = stage == TestPhaseStage;
        SpawnSelectedPhase(selected, stage, false, !isTestPhase, !isTestPhase);
    }
}
