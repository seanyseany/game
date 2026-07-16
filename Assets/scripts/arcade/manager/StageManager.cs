using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Serialization;

public interface IReinitializable { void Reinit(); }

[System.Serializable]
public class SpecialPhaseEntry
{
    public GameObject prefab;
    [Range(0f, 1f)] public float spawnChance = 0.2f;
}

public class StageManager : MonoBehaviour
{
    public static StageManager Instance;

    private const int TestPhaseStage = 0;
    private const int RagePhaseStage = -1;
    private const int MinPhaseStage = 1;
    private const int MaxPhaseStage = 4;
    private const string MachineGunStageTag = "MachineGunStage";
    private const string MiniBossStageTag = "miniBoss";
    private const int BossStage3 = 3;
    private const int BossStage4 = 4;
    private const float PostMachineGunObstacleProtectionDuration = 0.5f;

    [Header("Test Phase Prefabs")]
    public GameObject[] testPhasePrefabs;

    [Header("Stage 1 Phase Prefabs")]
    public GameObject[] stage1PhasePrefabs;
    public SpecialPhaseEntry[] stage1SpecialPhasePrefabs;

    [Header("Stage 2 Phase Prefabs")]
    public GameObject[] stage2PhasePrefabs;
    public SpecialPhaseEntry[] stage2SpecialPhasePrefabs;

    [Header("Stage 3 Phase Prefabs")]
    public GameObject[] stage3PhasePrefabs;
    public SpecialPhaseEntry[] stage3SpecialPhasePrefabs;

    [Header("Stage 4 Phase Prefabs")]
    public GameObject[] stage4PhasePrefabs;
    public SpecialPhaseEntry[] stage4SpecialPhasePrefabs;

    [Header("Rage Stage Prefabs")]
    [Tooltip("분노로 일반 스폰이 멈춘 동안 대신 스폰할 전용 페이즈")]
    public GameObject[] rageStagePrefabs;

    [Header("Spawn Settings")]
    public float phaseBaseSpeed = 3f;
    public float spawnX = 50f;
    public float ragePhaseSpawnX = 50f;
    public float recoverX = -12f;
    public float despawnX = -20f;
    private float startSpawnDelay = 2f;

    [Header("Speed Up Thresholds")]
    public int speedUp1 = 3;
    public int speedUp2 = 6;
    public int speedUp3 = 9;
    public int speedUp4 = 12;

    [Header("Speed Up Multipliers")]
    public float speedMult1 = 1.1f;
    public float speedMult2 = 1.25f;
    public float speedMult3 = 1.4f;
    public float speedMult4 = 1.55f;

    [Header("Pooling")]
    public int poolSizePerPrefab = 10;
    [Min(0)] public int initialPoolSizePerPrefab = 1;

    private int phaseSpawnCount = 0;
    private bool spawnPaused = false;
    private float despawnCheckTimer;
    private bool isDelayActive = false;

    private class PhaseInfo
    {
        public GameObject obj;
        public PhaseCache cache;
        public float spawnTime;
        public bool isRageSpawn;
        public float freezeUntil;
        public bool suppressNextPhasePass;
        public bool recovered;
    }

    private readonly List<PhaseInfo> activePhases = new List<PhaseInfo>(256);
    private Dictionary<GameObject, Queue<GameObject>> poolDict;
    private readonly Dictionary<GameObject, GameObject> pooledInstanceToPrefab = new Dictionary<GameObject, GameObject>(256);
    private readonly HashSet<GameObject> activePhaseObjects = new HashSet<GameObject>();

    [Header("Rage Phase Settings")]
    public float ragePhaseDuration = 12f;
    public float ragePhaseResumeDelay = 3f;

    private Coroutine ragePhaseRoutine;
    private readonly Dictionary<int, List<GameObject>> phaseShuffleByStage = new Dictionary<int, List<GameObject>>(MaxPhaseStage);
    private bool testPhaseSequenceCompleted;
    private SpawnMode currentSpawnMode = SpawnMode.Normal;
    private bool phasePassedDuringRageCooldown;
    private bool pendingInitialRagePhaseSpawn;
    private bool machineGunPhasePauseActive;
    private bool machineGunStagePrePauseActive;
    private bool phasePassedDuringMachineGunPause;
    private bool miniBossPhasePauseActive;

    private const float rageSpawnPhaseMult = 1.8f;

    private enum SpawnMode
    {
        Normal,
        Rage,
        Cooldown
    }

    // ============================ BOSS ============================
    [Header("Boss Settings")]
    [FormerlySerializedAs("bossA")]
    public GameObject stage3BossPrefab;
    [FormerlySerializedAs("bossB")]
    public GameObject stage4BossPrefab;
    public string bossPhaseTriggerTag = "Boss";
    public string bossSlimePhaseTriggerTag = "BossSlime";

    [Header("Boss Spawn Fallback (Optional)")]
    public GameObject bossA;
    public GameObject bossB;
    public float bossResumeDelay = 3f;

    private bool bossTriggered = false;
    private bool bossAwaitingFinalPass = false;
    private bool bossRunning = false;
    private bool stageSpawnPausedByRage = false;
    private bool gameplayPauseByTransform = false;
    private int bossTriggerStage = 0;
    private int pendingMachineGunBossStage = 0;
    private bool pendingBossExtraNormalPhase = false;
    private GameObject activeBoss;
    private bool activeBossIsSceneObject = false;
    private Coroutine bossFlowRoutine;

    [Header("Stage 3 -> 4 Transition")]
    public GameObject cavePrefab;
    public GameObject background1Prefab;
    public GameObject background2Prefab;
    public stage2prefabSpawner stage4PhasePrefabSpawner;
    public Vector3 caveStartWorldPos = new Vector3(42f, 0.3f, 0f);
    public Vector3 caveEndWorldPos = new Vector3(-45f, 0.3f, 0f);
    public float caveBackgroundSwitchX = -1f;
    public float caveMoveSpeed = 18f;
    public float stage4StartDelayAfterBgSwitch = 2f;
    public float stage4SpawnerResumeDelay = 0.35f;
    public int cavePoolSize = 1;

    private readonly Queue<GameObject> cavePool = new Queue<GameObject>();
    private GameObject activeCaveObj;
    private Coroutine caveMoveRoutine;
    private Coroutine stageLoopRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        DeactivateBossTemplatesIfSceneObjects();
        InitPools();
        InitCavePool();
        StopStage4PhasePrefabSpawner();
        SetBackgroundVisible(background1Prefab, true);
        SetBackgroundVisible(background2Prefab, false);
        StartStageLoop();
    }

    void OnEnable()
    {
        GameData.OnRageStart += HandleRageStart;
        GameData.OnRageEnd += HandleRageEnd;
        GameData.OnMachineGunTrigger += HandleMachineGunTrigger;
        GameData.OnMachineGunSequenceStart += HandleMachineGunSequenceStart;
        GameData.OnMachineGunSequenceEnd += HandleMachineGunSequenceEnd;
    }

    void OnDisable()
    {
        GameData.OnRageStart -= HandleRageStart;
        GameData.OnRageEnd -= HandleRageEnd;
        GameData.OnMachineGunTrigger -= HandleMachineGunTrigger;
        GameData.OnMachineGunSequenceStart -= HandleMachineGunSequenceStart;
        GameData.OnMachineGunSequenceEnd -= HandleMachineGunSequenceEnd;
    }

    // ============================ Phase 풀 초기화 ============================
    private void InitPools()
    {
        List<GameObject> allPrefabs = CollectAllPhasePrefabs();
        poolDict = new Dictionary<GameObject, Queue<GameObject>>(allPrefabs.Count);

        foreach (var prefab in allPrefabs)
        {
            if (prefab == null || poolDict.ContainsKey(prefab))
                continue;

            var queue = new Queue<GameObject>(poolSizePerPrefab);
            int initialCount = Mathf.Clamp(initialPoolSizePerPrefab, 0, Mathf.Max(0, poolSizePerPrefab));
            for (int i = 0; i < initialCount; i++)
            {
                queue.Enqueue(CreatePhasePoolObject(prefab));
            }
            poolDict[prefab] = queue;
        }
    }

    private GameObject CreatePhasePoolObject(GameObject prefab)
    {
        var go = Instantiate(prefab);
        go.name = prefab.name + "_Pooled";
        pooledInstanceToPrefab[go] = prefab;

        var snap = go.GetComponent<PhaseLayoutSnapshot>() ?? go.AddComponent<PhaseLayoutSnapshot>();
        snap.Capture();

        var cache = go.GetComponent<PhaseCache>();
        if (cache == null)
            cache = go.AddComponent<PhaseCache>();
        go.SetActive(false);
        return go;
    }

    private GameObject GetFromPool(GameObject prefab, Vector3 spawnPos)
    {
        if (prefab == null)
            return null;

        if (!poolDict.TryGetValue(prefab, out var queue) || queue == null)
        {
            queue = new Queue<GameObject>(poolSizePerPrefab);
            poolDict[prefab] = queue;
        }

        GameObject go = null;
        while (queue.Count > 0 && go == null)
        {
            GameObject candidate = queue.Dequeue();
            if (candidate == null)
                continue;

            if (activePhaseObjects.Contains(candidate) || candidate.activeInHierarchy)
                continue;

            go = candidate;
        }

        if (go == null)
            go = CreatePhasePoolObject(prefab);

        if (go.name.IndexOf("_Pooled") < 0)
            go.name = prefab.name + "_Pooled";

        var snap = go.GetComponent<PhaseLayoutSnapshot>();
        snap?.Restore(false);
        go.transform.position = spawnPos;
        go.SetActive(true);
        ResetPhase(go);
        activePhaseObjects.Add(go);
        return go;
    }

    private Vector3 GetPhaseSpawnPosition(bool isRageSpawn)
    {
        if (isRageSpawn)
            return new Vector3(ragePhaseSpawnX, 0f, 0f);

        return new Vector3(spawnX, 0f, 0f);
    }

    private void ReturnToPool(GameObject prefab, GameObject go)
    {
        if (prefab == null || go == null)
            return;

        if (!activePhaseObjects.Contains(go) && !go.activeSelf)
            return;

        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            mb.StopAllCoroutines();
            mb.CancelInvoke();
        }

        var snap = go.GetComponent<PhaseLayoutSnapshot>();
        snap?.Restore();

        go.SetActive(false);
        go.transform.position = new Vector3(-999f, -999f, 0f);
        activePhaseObjects.Remove(go);

        if (!poolDict.TryGetValue(prefab, out var queue) || queue == null)
        {
            queue = new Queue<GameObject>(poolSizePerPrefab);
            poolDict[prefab] = queue;
        }

        queue.Enqueue(go);
    }

    void Update()
    {
        if (gameplayPauseByTransform)
            return;

        if (miniBossPhasePauseActive && !HasAliveMiniBoss() && !HasPendingMiniBossTrigger())
            ResolveMiniBossPhase();

        despawnCheckTimer += Time.deltaTime;
        if (despawnCheckTimer >= 0.05f)
        {
            for (int i = activePhases.Count - 1; i >= 0; i--)
            {
                var p = activePhases[i];
                if (!p.obj) { activePhases.RemoveAt(i); continue; }

                float despawnCheckX = p.obj.transform.position.x;
                if (p.cache != null && p.cache.phaseEndTrigger != null)
                    despawnCheckX = p.cache.phaseEndTrigger.transform.position.x;

                if (!p.recovered && despawnCheckX < recoverX)
                    RecoverPhase(p);

                if (despawnCheckX < despawnX)
                {
                    ReturnToPool(FindMatchingPrefab(p.obj), p.obj);
                    activePhases.RemoveAt(i);
                }
            }
            despawnCheckTimer = 0f;
        }

        EnsureRagePhasePresence();
    }

    void FixedUpdate()
    {
        float stageFactor = GetStageSpeedFactor();
        float globalMult = (GameData.Instance != null) ? GameData.Instance.GetStageSpeedMult() : 1f;

        for (int i = activePhases.Count - 1; i >= 0; i--)
        {
            var p = activePhases[i];
            if (p.obj == null) { activePhases.RemoveAt(i); continue; }

            bool frozen = Time.time < p.freezeUntil;

            float currentMult = globalMult;
            if (p.isRageSpawn && GameData.Instance && GameData.Instance.rageMode)
                currentMult = rageSpawnPhaseMult;

            float finalSpeed = (frozen || gameplayPauseByTransform) ? 0f : phaseBaseSpeed * stageFactor * currentMult;

            if (p.cache != null && p.cache.mover != null)
            {
                p.cache.mover.applyStageSpeedMultiplier = false;
                p.cache.mover.baseSpeed = finalSpeed;
            }
            else
                p.obj.transform.position += Vector3.left * finalSpeed * Time.fixedDeltaTime;
        }
    }

    private GameObject FindMatchingPrefab(GameObject go)
    {
        if (go != null && pooledInstanceToPrefab.TryGetValue(go, out var cachedPrefab) && cachedPrefab != null)
            return cachedPrefab;

        foreach (var kv in poolDict)
        {
            if (go.name.StartsWith(kv.Key.name))
            {
                pooledInstanceToPrefab[go] = kv.Key;
                return kv.Key;
            }
        }

        List<GameObject> allPrefabs = CollectAllPhasePrefabs();
        return allPrefabs.Count > 0 ? allPrefabs[0] : null;
    }

    // ============================ 트리거에서 호출 ============================
    public void OnPhasePassed()
    {
        OnPhasePassed(null);
    }

    public void OnPhasePassed(PhaseEndTrigger sourceTrigger)
    {
        if (gameplayPauseByTransform)
            return;

        PhaseInfo sourcePhase = FindPhaseInfoByTrigger(sourceTrigger);
        if (sourcePhase != null && sourcePhase.suppressNextPhasePass)
        {
            sourcePhase.suppressNextPhasePass = false;
            return;
        }

        // ✅ 보스 트리거 상태면 spawnPaused여도 "마지막 페이즈 감지"를 처리해야 함
        if (bossTriggered && bossAwaitingFinalPass)
        {
            if (ShouldInsertExtraNormalPhaseBeforeBoss(sourcePhase))
            {
                bossAwaitingFinalPass = false;
                pendingBossExtraNormalPhase = true;
                return;
            }

            bossAwaitingFinalPass = false;
            if (bossFlowRoutine != null) StopCoroutine(bossFlowRoutine);
            bossFlowRoutine = StartCoroutine(CoRunBossEncounter());
            return;
        }

        if (spawnPaused)
        {
            if (miniBossPhasePauseActive)
                return;

            if (machineGunPhasePauseActive)
            {
                phasePassedDuringMachineGunPause = true;
                return;
            }

            if (machineGunStagePrePauseActive)
            {
                machineGunStagePrePauseActive = false;
                spawnPaused = currentSpawnMode == SpawnMode.Cooldown;

                if (!spawnPaused)
                    SpawnPhase();
                return;
            }

            if (currentSpawnMode == SpawnMode.Cooldown && stageSpawnPausedByRage)
                phasePassedDuringRageCooldown = true;
            return;
        }

        SpawnPhase();
    }

    // ============================ 일반 Phase 스폰 ============================
    public void StartStageLoop()
    {
        ResetState();
        if (stageLoopRoutine != null)
            StopCoroutine(stageLoopRoutine);
        stageLoopRoutine = StartCoroutine(CoStartStageLoop());
    }

    public void StopStageLoop()
    {
        spawnPaused = true;
        if (stageLoopRoutine != null)
        {
            StopCoroutine(stageLoopRoutine);
            stageLoopRoutine = null;
        }
    }
    public bool IsStageLoopStopped() => spawnPaused;

    public void ClearAllPhases()
    {
        for (int i = activePhases.Count - 1; i >= 0; i--)
        {
            var p = activePhases[i];
            if (p.obj != null)
                ReturnToPool(FindMatchingPrefab(p.obj), p.obj);
        }

        activePhases.Clear();
        activePhaseObjects.Clear();
        phaseSpawnCount = 0;
        testPhaseSequenceCompleted = false;
    }

    public void ResetState()
    {
        Debug.Log($"[StageManager] ResetState mode={currentSpawnMode} ragePaused={stageSpawnPausedByRage}");
        ClearAllPhases();
        phaseSpawnCount = 0;
        spawnPaused = false;
        stageSpawnPausedByRage = false;
        currentSpawnMode = SpawnMode.Normal;
        pendingInitialRagePhaseSpawn = false;
        machineGunPhasePauseActive = false;
        machineGunStagePrePauseActive = false;
        phasePassedDuringMachineGunPause = false;
        miniBossPhasePauseActive = false;
        phaseShuffleByStage.Clear();
        testPhaseSequenceCompleted = false;
        isDelayActive = false;
        if (stageLoopRoutine != null)
        {
            StopCoroutine(stageLoopRoutine);
            stageLoopRoutine = null;
        }
        StopStage4PhasePrefabSpawner();
        StopRagePhaseSpawn();

        // ✅ 보스 상태 리셋
        ResetBossState();
        ClearActiveCaveInstance();
        SetBackgroundVisible(background1Prefab, true);
        SetBackgroundVisible(background2Prefab, false);
    }

    private void SpawnPhase()
    {
        if (currentSpawnMode == SpawnMode.Cooldown)
            return;

        if (currentSpawnMode == SpawnMode.Rage)
        {
            SpawnRagePhaseDirect();
            return;
        }

        int speedStage = GetCurrentPhaseStage();
        bool isTestPhase = speedStage == TestPhaseStage;
        SpawnPhaseInternal(
            stage: speedStage,
            isRageSpawn: false,
            countTowardPhaseProgress: !isTestPhase,
            runPhaseRolls: !isTestPhase,
            warnLabel: $"speed stage {speedStage}");
    }

    private IEnumerator CoStartStageLoop()
    {
        spawnPaused = true;

        float delay = Mathf.Max(0f, startSpawnDelay);
        if (delay > 0f)
            yield return WaitForSecondsRespectingGameplayPause(delay);

        if (bossTriggered || bossRunning)
        {
            stageLoopRoutine = null;
            yield break;
        }

        spawnPaused = false;
        SpawnPhase();
        stageLoopRoutine = null;
    }

    public void AddPhaseDelay(float delay)
    {
        if (isDelayActive) return;

        isDelayActive = true;
        StartCoroutine(DelayCooldownTimer());

        float adjustedDelay = delay * 1.1f;
        float now = Time.time;
        float freezeWindow = 1.5f;

        for (int i = 0; i < activePhases.Count; i++)
        {
            var p = activePhases[i];
            if (p.obj == null) continue;

            if ((now - p.spawnTime) <= freezeWindow && !p.isRageSpawn)
                p.freezeUntil = Mathf.Max(p.freezeUntil, now + adjustedDelay);
        }
    }

    private float GetStageSpeedFactor()
    {
        if (phaseSpawnCount < speedUp1) return speedMult1;
        if (phaseSpawnCount < speedUp2) return speedMult2;
        if (phaseSpawnCount < speedUp3) return speedMult3;
        return speedMult4;
    }

    private IEnumerator DelayCooldownTimer()
    {
        float stageSpeedFactor = GetStageSpeedFactor();
        float baseCooldown = 7f;
        float adjustedCooldown = baseCooldown / stageSpeedFactor;

        yield return WaitForSecondsRespectingGameplayPause(adjustedCooldown);
        isDelayActive = false;
    }

    private void ResetPhase(GameObject phase)
    {
        var cache = phase.GetComponent<PhaseCache>();
        if (cache != null)
        {
            cache.SetActiveChildren(true);
            cache.ResetCached();
        }

        // 첫 스폰도 복구 경로와 동일하게 비활성 자식까지 되살린 뒤 재초기화한다.
        foreach (var r in phase.GetComponentsInChildren<IReinitializable>(true))
            r.Reinit();
    }

    private void RecoverPhase(PhaseInfo phaseInfo)
    {
        if (phaseInfo == null || phaseInfo.obj == null || phaseInfo.recovered)
            return;

        GameObject phase = phaseInfo.obj;
        PhaseCache cache = phaseInfo.cache != null ? phaseInfo.cache : phase.GetComponent<PhaseCache>();

        foreach (var mb in phase.GetComponentsInChildren<MonoBehaviour>(true))
        {
            mb.StopAllCoroutines();
            mb.CancelInvoke();
        }

        Vector3 worldPosition = phase.transform.position;
        Quaternion worldRotation = phase.transform.rotation;

        var snap = phase.GetComponent<PhaseLayoutSnapshot>();
        snap?.Restore(false);

        phase.transform.SetPositionAndRotation(worldPosition, worldRotation);

        if (cache != null)
        {
            cache.SetActiveChildren(true);
            cache.ResetCached();
        }

        foreach (var r in phase.GetComponentsInChildren<IReinitializable>(true))
        {
            if (r != null)
                r.Reinit();
        }

        phaseInfo.recovered = true;
    }

    public void SetSpawnPaused(bool paused) => spawnPaused = paused;
    public void SetGameplayPause(bool paused)
    {
        gameplayPauseByTransform = paused;

        if (!paused && currentSpawnMode == SpawnMode.Rage && !bossRunning && !bossTriggered && !machineGunPhasePauseActive)
        {
            if (pendingInitialRagePhaseSpawn)
            {
                SpawnRagePhaseDirect();
                pendingInitialRagePhaseSpawn = false;
            }
            else if (GetActivePhaseCount() == 0)
            {
                SpawnRagePhaseDirect();
            }
        }
    }

    // ============================ BOSS API ============================
    public int GetPhaseSpawnCount() => phaseSpawnCount;

    public void TriggerBossEncounter(int stage)
    {
        if (bossRunning) return;
        if (bossTriggered) return;

        bossTriggered = true;
        bossTriggerStage = stage;
        bossAwaitingFinalPass = true;

        // ✅ 즉시 스폰 중단 (마지막 페이즈가 detector 찍을 때까지 기다림)
        spawnPaused = true;

        Debug.Log($"👑 Boss Triggered (stage {stage}) -> spawn paused, waiting final phase pass...");
    }

    public void QueueBossEncounterAfterMachineGun(int stage)
    {
        if (stage < BossStage3 || stage > BossStage4)
            return;

        pendingMachineGunBossStage = stage;
    }

    public bool TryResolveBossStageFromTaggedObject(GameObject source, out int stage)
    {
        stage = 0;
        if (source == null)
            return false;

        string tag = source.tag;
        if (!string.IsNullOrEmpty(bossPhaseTriggerTag) && tag == bossPhaseTriggerTag)
        {
            stage = BossStage3;
            return true;
        }

        if (!string.IsNullOrEmpty(bossSlimePhaseTriggerTag) && tag == bossSlimePhaseTriggerTag)
        {
            stage = BossStage4;
            return true;
        }

        return false;
    }

    private void StartBossEncounterNow(int stage)
    {
        if (bossRunning || bossTriggered)
            return;

        bossTriggered = true;
        bossRunning = false;
        bossTriggerStage = stage;
        bossAwaitingFinalPass = false;
        spawnPaused = true;
        pendingMachineGunBossStage = 0;

        if (bossFlowRoutine != null)
            StopCoroutine(bossFlowRoutine);

        bossFlowRoutine = StartCoroutine(CoRunBossEncounter());
        Debug.Log($"👑 Boss Triggered immediately from tagged trigger (stage {stage})");
    }

    private IEnumerator CoRunBossEncounter()
    {
        bossRunning = true;
        DeactivateBossTemplatesIfSceneObjects();
        StopStage4PhasePrefabSpawner();

        // 1) Stage별 보스 스폰
        ClearActiveBossInstance();

        GameObject source = PickBossPrefabForStage(bossTriggerStage);
        if (source == null)
        {
            Debug.LogWarning($"⚠️ Boss prefab not set for stage {bossTriggerStage}. Resume stage.");
            yield return WaitForSecondsRespectingGameplayPause(bossResumeDelay);
            ResumeAfterBoss(startStage4PrefabSpawner: bossTriggerStage >= 4);
            yield break;
        }

        // StageManager는 좌표를 정하지 않는다.
        // 씬에 배치된 보스 오브젝트면 그대로 활성화해서 사용,
        // 에셋 프리팹이면 원본 transform 기준으로 인스턴스화한다.
        activeBossIsSceneObject = source.scene.IsValid();
        if (!activeBossIsSceneObject)
        {
            Debug.LogWarning($"⚠️ Stage {bossTriggerStage} boss reference must be a SCENE object (not prefab asset).");
            yield return WaitForSecondsRespectingGameplayPause(bossResumeDelay);
            ResumeAfterBoss(startStage4PrefabSpawner: bossTriggerStage >= 4);
            yield break;
        }

        activeBoss = source;
        Debug.Log($"👑 Using scene boss object '{activeBoss.name}' at {activeBoss.transform.position} for stage {bossTriggerStage}");

        if (activeBoss != null && !activeBoss.activeSelf)
            activeBoss.SetActive(true);

        // 2) 보스 Begin()
        var boss = activeBoss.GetComponentInChildren<Boss>(true);
        var slimeBoss = activeBoss.GetComponentInChildren<BossSlime>(true);
        if (boss != null)
        {
            if (!boss.gameObject.activeSelf)
                boss.gameObject.SetActive(true);
            boss.enabled = true;
            boss.ResetBossRuntime();
            boss.state = Boss.State.Inactive;
            boss.Begin();
        }
        else if (slimeBoss != null)
        {
            if (!slimeBoss.gameObject.activeSelf)
                slimeBoss.gameObject.SetActive(true);

            slimeBoss.enabled = true;
            slimeBoss.ResetBossRuntime();
            slimeBoss.state = BossSlime.State.Inactive;
            slimeBoss.Begin();
        }
        else
        {
            Debug.LogWarning("⚠️ Boss prefab has no Boss.cs/BossSlime.cs. Resume stage.");
            yield return WaitForSecondsRespectingGameplayPause(bossResumeDelay);
            ResumeAfterBoss(startStage4PrefabSpawner: bossTriggerStage >= 4);
            yield break;
        }

        // 3) 보스 끝날 때까지 대기(비활성/파괴)
        while (activeBoss != null && activeBoss.activeInHierarchy)
            yield return null;

        int completedBossStage = bossTriggerStage;
        if (completedBossStage == 3)
        {
            yield return CoCaveBackgroundTransition(
                fromBackground: background1Prefab,
                toBackground: background2Prefab,
                waitForRageEnd: true);
            ResumeAfterBoss(startStage4PrefabSpawner: true);
        }
        else if (completedBossStage >= 4)
        {
            yield return CoCaveBackgroundTransition(
                fromBackground: background2Prefab,
                toBackground: background1Prefab,
                waitForRageEnd: true);
            ResumeAfterBoss(startStage4PrefabSpawner: false);
        }
        else
        {
            // 4) 3초 딜레이 후 스폰 재개
            yield return WaitForSecondsRespectingGameplayPause(bossResumeDelay);
            ResumeAfterBoss(startStage4PrefabSpawner: false);
        }
    }

    private void ResumeAfterBoss(bool startStage4PrefabSpawner = false)
    {
        bossRunning = false;
        bossTriggered = false;
        bossAwaitingFinalPass = false;
        bossTriggerStage = 0;
        pendingBossExtraNormalPhase = false;

        spawnPaused = false;

        // ✅ 바로 다음 페이즈 다시 스폰 재개
        SpawnPhase();

        if (startStage4PrefabSpawner)
            StartCoroutine(CoStartStage4SpawnerDeferred());

        Debug.Log("✅ Boss ended -> stage spawn resumed");
    }

    private IEnumerator CoStartStage4SpawnerDeferred()
    {
        float delay = Mathf.Max(0f, stage4SpawnerResumeDelay);
        if (delay > 0f)
            yield return WaitForSecondsRespectingGameplayPause(delay);

        StartStage4PhasePrefabSpawner();
    }

    private void ResetBossState()
    {
        bossTriggered = false;
        bossAwaitingFinalPass = false;
        bossRunning = false;
        bossTriggerStage = 0;
        pendingMachineGunBossStage = 0;
        pendingBossExtraNormalPhase = false;

        if (bossFlowRoutine != null)
        {
            StopCoroutine(bossFlowRoutine);
            bossFlowRoutine = null;
        }

        ClearActiveBossInstance();
        ClearActiveCaveInstance();

        DeactivateBossTemplatesIfSceneObjects();
    }

    // 게임오버 시 보스 전투 오브젝트 즉시 정리용
    public void ForceClearBossNow()
    {
        ResetBossState();
        StopStage4PhasePrefabSpawner();
    }

    private void InitCavePool()
    {
        cavePool.Clear();
        if (cavePrefab == null) return;

        int count = Mathf.Max(1, cavePoolSize);
        for (int i = 0; i < count; i++)
        {
            var go = Instantiate(cavePrefab);
            go.name = cavePrefab.name + "_Pooled";
            go.SetActive(false);
            cavePool.Enqueue(go);
        }
    }

    private GameObject GetCaveFromPool()
    {
        if (cavePrefab == null) return null;

        GameObject go = (cavePool.Count > 0) ? cavePool.Dequeue() : Instantiate(cavePrefab);
        if (go.name.IndexOf("_Pooled") < 0)
            go.name = cavePrefab.name + "_Pooled";

        go.transform.SetPositionAndRotation(caveStartWorldPos, Quaternion.identity);
        go.SetActive(true);
        foreach (var r in go.GetComponentsInChildren<IReinitializable>(true))
            r.Reinit();
        return go;
    }

    private void ReturnCaveToPool(GameObject go)
    {
        if (go == null) return;
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            mb.StopAllCoroutines();

        go.SetActive(false);
        go.transform.position = new Vector3(-999f, -999f, 0f);
        cavePool.Enqueue(go);
    }

    private IEnumerator CoCaveBackgroundTransition(GameObject fromBackground, GameObject toBackground, bool waitForRageEnd)
    {
        if (waitForRageEnd)
        {
            // Stage3 보스 처치 후 즉시 cave를 띄우지 않고, 플레이어 분노(rage)가 끝난 뒤 전환 시작
            while (GameData.Instance != null && GameData.Instance.rageMode)
            {
                if (gameplayPauseByTransform)
                {
                    yield return null;
                    continue;
                }
                yield return null;
            }
        }

        yield return WaitForSecondsRespectingGameplayPause(1f);

        ClearActiveCaveInstance();
        activeCaveObj = GetCaveFromPool();
        if (activeCaveObj == null)
        {
            yield return WaitForSecondsRespectingGameplayPause(stage4StartDelayAfterBgSwitch);
            yield break;
        }

        if (caveMoveRoutine != null)
            StopCoroutine(caveMoveRoutine);
        caveMoveRoutine = StartCoroutine(CoMoveCaveAndRecycle(activeCaveObj));

        bool switched = false;
        while (activeCaveObj != null && activeCaveObj.activeSelf)
        {
            if (!switched && activeCaveObj.transform.position.x <= caveBackgroundSwitchX)
            {
                switched = true;
                SetBackgroundVisible(fromBackground, false);
                SetBackgroundVisible(toBackground, true);
                yield return WaitForSecondsRespectingGameplayPause(Mathf.Max(0f, stage4StartDelayAfterBgSwitch));
                yield break;
            }
            yield return null;
        }

        // cave가 빨리 사라진 예외 케이스에서도 stage4 시작 지연은 보장
        yield return WaitForSecondsRespectingGameplayPause(Mathf.Max(0f, stage4StartDelayAfterBgSwitch));
    }

    private IEnumerator CoMoveCaveAndRecycle(GameObject caveObj)
    {
        if (caveObj == null) yield break;

        Vector3 end = caveEndWorldPos;
        float speed = Mathf.Max(0.01f, caveMoveSpeed);

        while (caveObj != null && caveObj.activeSelf)
        {
            if (gameplayPauseByTransform)
            {
                yield return null;
                continue;
            }

            caveObj.transform.position = Vector3.MoveTowards(caveObj.transform.position, end, speed * Time.deltaTime);
            if (caveObj.transform.position.x <= end.x)
                break;
            yield return null;
        }

        if (caveObj != null)
            ReturnCaveToPool(caveObj);

        if (activeCaveObj == caveObj)
            activeCaveObj = null;
        caveMoveRoutine = null;
    }

    private void ClearActiveCaveInstance()
    {
        if (caveMoveRoutine != null)
        {
            StopCoroutine(caveMoveRoutine);
            caveMoveRoutine = null;
        }

        if (activeCaveObj != null)
        {
            ReturnCaveToPool(activeCaveObj);
            activeCaveObj = null;
        }
    }

    private void StartStage4PhasePrefabSpawner()
    {
        if (stage4PhasePrefabSpawner == null)
            return;

        stage4PhasePrefabSpawner.SetBossSlimePaused(false);
        stage4PhasePrefabSpawner.BeginSpawn();
    }

    private void StopStage4PhasePrefabSpawner()
    {
        if (stage4PhasePrefabSpawner == null)
            return;

        stage4PhasePrefabSpawner.SetBossSlimePaused(true);
    }

    private void SetBackgroundVisible(GameObject bg, bool visible)
    {
        if (bg == null) return;

        var mrs = bg.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < mrs.Length; i++)
            if (mrs[i] != null) mrs[i].enabled = visible;
    }

    private void DeactivateBossTemplatesIfSceneObjects()
    {
        // 씬 레퍼런스로 들고 있는 보스 템플릿만 비활성화하면 충분하다.
        // 매 전투 시작마다 씬 전체 FindObjectsByType를 돌면 후반부에서 미세 스파이크가 날 수 있다.
        DeactivateIfSceneObject(stage3BossPrefab);
        DeactivateIfSceneObject(stage4BossPrefab);
        DeactivateIfSceneObject(bossA);
        DeactivateIfSceneObject(bossB);
    }

    private GameObject PickBossPrefabForStage(int stage)
    {
        if (stage == 3)
        {
            if (stage3BossPrefab != null) return stage3BossPrefab;
            if (bossA != null) return bossA;
            return bossB;
        }

        if (stage == 4)
        {
            if (stage4BossPrefab != null) return stage4BossPrefab;
            if (bossB != null) return bossB;
            return bossA;
        }

        if (stage3BossPrefab != null && stage4BossPrefab != null)
            return (Random.value < 0.5f) ? stage3BossPrefab : stage4BossPrefab;

        if (stage3BossPrefab != null) return stage3BossPrefab;
        if (stage4BossPrefab != null) return stage4BossPrefab;

        if (bossA != null && bossB != null)
            return (Random.value < 0.5f) ? bossA : bossB;
        if (bossA != null) return bossA;
        if (bossB != null) return bossB;

        return null;
    }

    private void ClearActiveBossInstance()
    {
        if (activeBoss == null) return;

        if (activeBossIsSceneObject)
        {
            if (activeBoss.activeSelf)
                activeBoss.SetActive(false);
        }
        else
        {
            Destroy(activeBoss);
        }

        activeBoss = null;
        activeBossIsSceneObject = false;
    }

    private void DeactivateIfSceneObject(GameObject go)
    {
        if (go == null) return;
        if (!go.scene.IsValid()) return; // 프리팹 에셋은 건드리지 않음
        if (go.activeSelf) go.SetActive(false);
    }

    private void HandleRageStart()
    {
        Debug.Log($"[StageManager] HandleRageStart mode(before)={currentSpawnMode}");

        if (bossRunning || bossTriggered)
        {
            Debug.Log("[StageManager] Rage start ignored because boss encounter is active.");
            return;
        }

        StartRagePhaseSpawn();
    }

    private void HandleRageEnd()
    {
    }

    private void HandleMachineGunSequenceStart()
    {
        if (bossRunning || bossTriggered)
            return;

        machineGunStagePrePauseActive = false;
        machineGunPhasePauseActive = true;
        spawnPaused = true;
    }

    private void HandleMachineGunTrigger()
    {
        ProtectActivePostMachineGunPhases();
    }

    private void HandleMachineGunSequenceEnd()
    {
        machineGunStagePrePauseActive = false;
        machineGunPhasePauseActive = false;

        if (bossRunning || (bossTriggered && !pendingBossExtraNormalPhase))
        {
            phasePassedDuringMachineGunPause = false;
            return;
        }

        if (pendingMachineGunBossStage != 0)
        {
            phasePassedDuringMachineGunPause = false;
            StartBossEncounterNow(pendingMachineGunBossStage);
            return;
        }

        if (pendingBossExtraNormalPhase)
        {
            phasePassedDuringMachineGunPause = false;
            SpawnExtraNormalPhaseBeforeBoss();
            return;
        }

        if (currentSpawnMode == SpawnMode.Rage && !bossRunning && !bossTriggered)
        {
            spawnPaused = gameplayPauseByTransform;

            if (!gameplayPauseByTransform)
            {
                if (pendingInitialRagePhaseSpawn)
                {
                    SpawnRagePhaseDirect();
                    pendingInitialRagePhaseSpawn = false;
                }
                else if (GetActiveRagePhaseCount() == 0)
                {
                    SpawnRagePhaseDirect();
                }
            }

            phasePassedDuringMachineGunPause = false;
            return;
        }

        spawnPaused = currentSpawnMode == SpawnMode.Cooldown;

        if (!spawnPaused && (phasePassedDuringMachineGunPause || GetActivePhaseCount() == 0))
            SpawnPhase();

        phasePassedDuringMachineGunPause = false;
    }

    private void StartRagePhaseSpawn()
    {
        if (!HasConfiguredPhasePrefabs(rageStagePrefabs))
        {
            Debug.LogWarning("[StageManager] Rage start skipped because no rageStagePrefabs are configured.");
            return;
        }

        if (ragePhaseRoutine != null)
            StopCoroutine(ragePhaseRoutine);

        machineGunStagePrePauseActive = false;
        stageSpawnPausedByRage = true;
        spawnPaused = machineGunPhasePauseActive;
        currentSpawnMode = SpawnMode.Rage;
        phasePassedDuringRageCooldown = false;
        pendingInitialRagePhaseSpawn = true;
        SuppressActivePhaseTriggers(false);

        // 분노 시작 시에는 기존 phase trigger를 기다리지 않고
        // 첫 rage phase를 즉시 스폰한다.
        if (!gameplayPauseByTransform && !machineGunPhasePauseActive)
        {
            SpawnRagePhaseDirect();
            pendingInitialRagePhaseSpawn = false;
        }
        else
        {
            Debug.Log($"[StageManager] Rage phase spawn deferred gameplayPause={gameplayPauseByTransform} machineGunPause={machineGunPhasePauseActive}");
        }

        ragePhaseRoutine = StartCoroutine(CoRunRagePhaseSequence());
    }

    private void StopRagePhaseSpawn()
    {
        Debug.Log($"[StageManager] StopRagePhaseSpawn mode(before)={currentSpawnMode}");
        if (ragePhaseRoutine != null)
        {
            StopCoroutine(ragePhaseRoutine);
            ragePhaseRoutine = null;
        }

        currentSpawnMode = SpawnMode.Normal;
        pendingInitialRagePhaseSpawn = false;
        machineGunStagePrePauseActive = false;
        stageSpawnPausedByRage = false;
        phasePassedDuringRageCooldown = false;
    }

    private IEnumerator CoRunRagePhaseSequence()
    {
        Debug.Log($"[StageManager] RageSequence enter mode={currentSpawnMode}");
        yield return WaitForSecondsRespectingGameplayPause(Mathf.Max(0f, ragePhaseDuration));

        Debug.Log("[StageManager] RageSequence -> Cooldown");
        currentSpawnMode = SpawnMode.Cooldown;
        spawnPaused = true;

        while (GameData.Instance != null && GameData.Instance.rageMode)
        {
            if (gameplayPauseByTransform)
            {
                yield return null;
                continue;
            }
            yield return null;
        }

        yield return CoResumeNormalPhaseAfterRage();
    }

    private IEnumerator CoResumeNormalPhaseAfterRage()
    {
        if (!bossRunning && !bossTriggered)
        {
            Debug.Log("[StageManager] RageSequence -> Normal resume");
            currentSpawnMode = SpawnMode.Normal;
            machineGunStagePrePauseActive = false;
            spawnPaused = false;
            stageSpawnPausedByRage = false;
            SuppressActivePhaseTriggers(true);
            SpawnPhase();
        }

        phasePassedDuringRageCooldown = false;
        ragePhaseRoutine = null;
        yield break;
    }
    private void SpawnRagePhaseDirect()
    {
        SpawnPhaseInternal(
            stage: RagePhaseStage,
            isRageSpawn: true,
            countTowardPhaseProgress: false,
            runPhaseRolls: false,
            warnLabel: "rage stage");
    }

    private int GetActivePhaseCount()
    {
        return GetActivePhaseCountInternal(null);
    }

    private int GetActiveRagePhaseCount()
    {
        return GetActivePhaseCountInternal(true);
    }

    private int GetActivePhaseCountInternal(bool? rageOnly)
    {
        int count = 0;
        for (int i = 0; i < activePhases.Count; i++)
        {
            var p = activePhases[i];
            if (p != null && p.obj != null)
            {
                if (rageOnly.HasValue && p.isRageSpawn != rageOnly.Value)
                    continue;

                count++;
            }
        }
        return count;
    }

    private void EnsureRagePhasePresence()
    {
        if (currentSpawnMode != SpawnMode.Rage)
            return;

        if (bossRunning || bossTriggered || machineGunPhasePauseActive || pendingInitialRagePhaseSpawn)
            return;

        if (GetActiveRagePhaseCount() > 0)
            return;

        Debug.Log("[StageManager] Rage phase missing during Rage mode. Respawning rage phase.");
        SpawnRagePhaseDirect();
    }

    private PhaseInfo FindPhaseInfoByTrigger(PhaseEndTrigger sourceTrigger)
    {
        if (sourceTrigger == null)
            return null;

        for (int i = 0; i < activePhases.Count; i++)
        {
            PhaseInfo phase = activePhases[i];
            if (phase == null || phase.cache == null || phase.cache.phaseEndTrigger == null)
                continue;

            if (phase.cache.phaseEndTrigger == sourceTrigger)
                return phase;
        }

        return null;
    }

    private void SuppressNextPhasePassForActivePhases(bool isRageSpawn)
    {
        for (int i = 0; i < activePhases.Count; i++)
        {
            PhaseInfo phase = activePhases[i];
            if (phase == null || phase.obj == null || phase.isRageSpawn != isRageSpawn)
                continue;

            phase.suppressNextPhasePass = true;
        }
    }

    private void SuppressActivePhaseTriggers(bool isRageSpawn)
    {
        SuppressNextPhasePassForActivePhases(isRageSpawn);

        for (int i = 0; i < activePhases.Count; i++)
        {
            PhaseInfo phase = activePhases[i];
            if (phase == null || phase.obj == null || phase.isRageSpawn != isRageSpawn)
                continue;

            if (phase.cache != null && phase.cache.phaseEndTrigger != null)
                phase.cache.phaseEndTrigger.SuppressFuturePasses();
        }
    }

    private void ClearActiveSpawnedPhases()
    {
        Debug.Log($"[StageManager] ClearActiveSpawnedPhases count={activePhases.Count} mode={currentSpawnMode}");
        for (int i = activePhases.Count - 1; i >= 0; i--)
        {
            var p = activePhases[i];
            if (p == null)
                continue;

            if (p.obj != null)
                ReturnToPool(FindMatchingPrefab(p.obj), p.obj);

            activePhases.RemoveAt(i);
        }
    }

    public bool IsGameplayTransformPaused => gameplayPauseByTransform;

    private IEnumerator WaitForSecondsRespectingGameplayPause(float seconds)
    {
        float remaining = Mathf.Max(0f, seconds);
        while (remaining > 0f)
        {
            if (gameplayPauseByTransform)
            {
                yield return null;
                continue;
            }

            remaining -= Time.deltaTime;
            yield return null;
        }
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    public int GetSpeedStage()
    {
        // Threshold는 "다음 단계로 넘어가기 전까지의 누적 스폰 수"로 해석한다.
        // 예) speedUp1=1, speedUp2=20 이면
        // 첫 1개는 Stage1, 그 다음부터 20 전까지는 Stage2가 된다.
        if (phaseSpawnCount < Mathf.Max(0, speedUp1)) return 1;
        if (phaseSpawnCount < Mathf.Max(speedUp1, speedUp2)) return 2;
        if (phaseSpawnCount < Mathf.Max(speedUp2, speedUp3)) return 3;
        return 4;
    }

    private int GetCurrentPhaseStage()
    {
        if (!testPhaseSequenceCompleted && HasConfiguredPhasePrefabs(testPhasePrefabs))
            return TestPhaseStage;

        return GetSpeedStage();
    }

    private List<GameObject> CollectAllPhasePrefabs()
    {
        var allPrefabs = new List<GameObject>();
        AddUniquePrefabs(allPrefabs, testPhasePrefabs);
        AddUniquePrefabs(allPrefabs, rageStagePrefabs);
        AddUniquePrefabs(allPrefabs, stage1PhasePrefabs);
        AddUniquePrefabs(allPrefabs, stage2PhasePrefabs);
        AddUniquePrefabs(allPrefabs, stage3PhasePrefabs);
        AddUniquePrefabs(allPrefabs, stage4PhasePrefabs);
        return allPrefabs;
    }

    private GameObject[] GetPhasePrefabsForStage(int stage)
    {
        if (stage == TestPhaseStage)
        {
            if (testPhasePrefabs != null && testPhasePrefabs.Length > 0)
                return testPhasePrefabs;

            return null;
        }

        if (stage == RagePhaseStage)
        {
            if (rageStagePrefabs != null && rageStagePrefabs.Length > 0)
                return rageStagePrefabs;

            return null;
        }

        switch (Mathf.Clamp(stage, MinPhaseStage, MaxPhaseStage))
        {
            case 4:
                if (stage4PhasePrefabs != null && stage4PhasePrefabs.Length > 0)
                    return stage4PhasePrefabs;
                break;
            case 3:
                if (stage3PhasePrefabs != null && stage3PhasePrefabs.Length > 0)
                    return stage3PhasePrefabs;
                break;
            case 2:
                if (stage2PhasePrefabs != null && stage2PhasePrefabs.Length > 0)
                    return stage2PhasePrefabs;
                break;
            default:
                if (stage1PhasePrefabs != null && stage1PhasePrefabs.Length > 0)
                    return stage1PhasePrefabs;
                break;
        }

        return null;
    }

    private GameObject GetNextPhasePrefabForStage(int stage)
    {
        return GetNextPhasePrefabForStage(stage, allowMachineGunStage: true);
    }

    private GameObject GetNextPhasePrefabForStage(int stage, bool allowMachineGunStage)
    {
        GameObject[] source = GetPhasePrefabsForStage(stage);
        SpecialPhaseEntry[] specialEntries = GetSpecialPhaseEntriesForStage(stage);
        if (!HasConfiguredPhasePrefabs(source) && !HasConfiguredSpecialPhasePrefabs(specialEntries))
            return null;

        if (!phaseShuffleByStage.TryGetValue(stage, out var shuffleList) || shuffleList == null)
        {
            shuffleList = new List<GameObject>(GetPhaseCycleCountForStage(stage));
            phaseShuffleByStage[stage] = shuffleList;
        }

        if (shuffleList.Count == 0)
        {
            if (stage == TestPhaseStage && testPhaseSequenceCompleted)
                return null;

            shuffleList.Clear();
            BuildPhaseCycleForStage(shuffleList, stage, source, specialEntries, allowMachineGunStage);

            if (shuffleList.Count == 0)
                return null;

            ShuffleList(shuffleList);
        }

        if (allowMachineGunStage)
        {
            GameObject prefab = shuffleList[0];
            shuffleList.RemoveAt(0);

            if (stage == TestPhaseStage && shuffleList.Count == 0)
                testPhaseSequenceCompleted = true;

            return prefab;
        }

        for (int i = 0; i < shuffleList.Count; i++)
        {
            GameObject prefab = shuffleList[i];
            if (prefab == null || prefab.CompareTag(MachineGunStageTag))
                continue;

            shuffleList.RemoveAt(i);

            if (stage == TestPhaseStage && shuffleList.Count == 0)
                testPhaseSequenceCompleted = true;

            return prefab;
        }

        return null;
    }

    private int GetPhaseCycleCountForStage(int stage)
    {
        switch (Mathf.Clamp(stage, MinPhaseStage, MaxPhaseStage))
        {
            case 4:
                return Mathf.Max(1, speedUp4 - speedUp3);
            case 3:
                return Mathf.Max(1, speedUp3 - speedUp2);
            case 2:
                return Mathf.Max(1, speedUp2 - speedUp1);
            default:
                return Mathf.Max(1, speedUp1);
        }
    }

    private SpecialPhaseEntry[] GetSpecialPhaseEntriesForStage(int stage)
    {
        if (stage < MinPhaseStage || stage > MaxPhaseStage)
            return null;

        switch (Mathf.Clamp(stage, MinPhaseStage, MaxPhaseStage))
        {
            case 4:
                return stage4SpecialPhasePrefabs;
            case 3:
                return stage3SpecialPhasePrefabs;
            case 2:
                return stage2SpecialPhasePrefabs;
            default:
                return stage1SpecialPhasePrefabs;
        }
    }

    private void BuildPhaseCycleForStage(List<GameObject> target, int stage, GameObject[] normalPrefabs, SpecialPhaseEntry[] specialEntries, bool allowMachineGunStage)
    {
        if (target == null)
            return;

        if (stage < MinPhaseStage || stage > MaxPhaseStage)
        {
            AddAvailableNormalPrefabs(target, normalPrefabs, allowMachineGunStage);
            return;
        }

        List<GameObject> availableNormals = new List<GameObject>();
        AddAvailableNormalPrefabs(availableNormals, normalPrefabs, allowMachineGunStage);
        ShuffleList(availableNormals);

        List<SpecialPhaseEntry> availableSpecials = new List<SpecialPhaseEntry>();
        if (specialEntries != null)
        {
            for (int i = 0; i < specialEntries.Length; i++)
            {
                SpecialPhaseEntry entry = specialEntries[i];
                if (entry == null || entry.prefab == null)
                    continue;

                if (!allowMachineGunStage && entry.prefab.CompareTag(MachineGunStageTag))
                    continue;

                availableSpecials.Add(entry);
            }
        }

        int totalCycleCount = GetPhaseCycleCountForStage(stage);
        int specialSlotCount = Mathf.Min(totalCycleCount, availableSpecials.Count);
        int normalSlotCount = Mathf.Max(0, totalCycleCount - specialSlotCount);
        int normalIndex = 0;

        for (int i = 0; i < normalSlotCount && normalIndex < availableNormals.Count; i++)
            target.Add(availableNormals[normalIndex++]);

        for (int i = 0; i < specialSlotCount; i++)
        {
            SpecialPhaseEntry special = availableSpecials[i];
            bool useSpecial = Random.value <= Mathf.Clamp01(special.spawnChance);

            if (useSpecial)
            {
                target.Add(special.prefab);
                continue;
            }

            if (normalIndex < availableNormals.Count)
            {
                target.Add(availableNormals[normalIndex++]);
                continue;
            }

            target.Add(special.prefab);
        }
    }

    private static void AddAvailableNormalPrefabs(List<GameObject> target, GameObject[] source, bool allowMachineGunStage)
    {
        if (target == null || source == null)
            return;

        for (int i = 0; i < source.Length; i++)
        {
            GameObject prefab = source[i];
            if (prefab == null)
                continue;

            if (!allowMachineGunStage && prefab.CompareTag(MachineGunStageTag))
                continue;

            target.Add(prefab);
        }
    }

    private void SpawnPhaseInternal(int stage, bool isRageSpawn, bool countTowardPhaseProgress, bool runPhaseRolls, string warnLabel)
    {
        SpawnPhaseInternal(stage, isRageSpawn, countTowardPhaseProgress, runPhaseRolls, warnLabel, allowMachineGunStage: true);
    }

    private void SpawnPhaseInternal(int stage, bool isRageSpawn, bool countTowardPhaseProgress, bool runPhaseRolls, string warnLabel, bool allowMachineGunStage)
    {
        GameObject prefab = GetNextPhasePrefabForStage(stage, allowMachineGunStage);
        if (prefab == null)
        {
            Debug.LogWarning($"[StageManager] No phase prefabs configured for {warnLabel}.");
            return;
        }

        var go = GetFromPool(prefab, GetPhaseSpawnPosition(isRageSpawn));
        if (go == null)
            return;

        Debug.Log($"[StageManager] SpawnPhaseInternal stage={stage} rage={isRageSpawn} prefab={prefab.name} mode={currentSpawnMode}");

        var cache = go.GetComponent<PhaseCache>();
        if (cache != null && cache.mover != null)
        {
            cache.mover.applyStageSpeedMultiplier = false;
            cache.mover.baseSpeed = phaseBaseSpeed;
        }

        activePhases.Add(new PhaseInfo
        {
            obj = go,
            cache = cache,
            spawnTime = Time.time,
            isRageSpawn = isRageSpawn,
            freezeUntil = 0f
        });

        if (countTowardPhaseProgress)
            phaseSpawnCount++;

        if (runPhaseRolls && GameData.Instance != null)
            GameData.Instance.CheckBossTriggerBeforeSpeedUp(this, phaseSpawnCount);

        if (!isRageSpawn &&
            !go.CompareTag(MachineGunStageTag) &&
            GameData.Instance != null &&
            GameData.Instance.ConsumeNextPostMachineGunPhaseObstacleProtection())
        {
            ApplyPostMachineGunObstacleProtection(go);
        }

        TryPauseSpawnForMachineGunStage(go, isRageSpawn);
        TryPauseSpawnForMiniBossPhase(go, cache, isRageSpawn);
    }

    private void ApplyPostMachineGunObstacleProtection(GameObject phaseObject)
    {
        if (phaseObject == null)
            return;

        Obstacle[] obstacles = phaseObject.GetComponentsInChildren<Obstacle>(true);
        for (int i = 0; i < obstacles.Length; i++)
        {
            if (obstacles[i] != null)
                obstacles[i].ActivateTemporarySpawnProtection(PostMachineGunObstacleProtectionDuration);
        }

        BulletObstacle[] bulletObstacles = phaseObject.GetComponentsInChildren<BulletObstacle>(true);
        for (int i = 0; i < bulletObstacles.Length; i++)
        {
            if (bulletObstacles[i] != null)
                bulletObstacles[i].ActivateTemporarySpawnProtection(PostMachineGunObstacleProtectionDuration);
        }
    }

    private void ProtectActivePostMachineGunPhases()
    {
        float minimumX = float.NegativeInfinity;
        MachineGunObstacle source = MachineGunObstacle.CurrentSource;
        if (source != null)
        {
            PhaseLayoutSnapshot sourcePhase = source.GetComponentInParent<PhaseLayoutSnapshot>(true);
            if (sourcePhase != null)
                minimumX = sourcePhase.transform.position.x - 0.01f;
        }

        for (int i = 0; i < activePhases.Count; i++)
        {
            PhaseInfo phaseInfo = activePhases[i];
            if (phaseInfo == null || phaseInfo.obj == null || phaseInfo.isRageSpawn)
                continue;

            GameObject phaseObject = phaseInfo.obj;
            if (phaseObject.CompareTag(MachineGunStageTag))
                continue;

            if (phaseObject.transform.position.x < minimumX)
                continue;

            ApplyPostMachineGunObstacleProtection(phaseObject);
        }
    }

    private void TryPauseSpawnForMachineGunStage(GameObject phaseObject, bool isRageSpawn)
    {
        if (phaseObject == null || isRageSpawn || bossRunning || bossTriggered)
            return;

        if (machineGunPhasePauseActive || machineGunStagePrePauseActive)
            return;

        if (!phaseObject.CompareTag(MachineGunStageTag))
            return;

        // Pause only the normal follow-up spawn until the machine gun trigger resolves.
        machineGunStagePrePauseActive = true;
        machineGunPhasePauseActive = false;
        phasePassedDuringMachineGunPause = false;
        spawnPaused = true;
    }

    private void TryPauseSpawnForMiniBossPhase(GameObject phaseObject, PhaseCache cache, bool isRageSpawn)
    {
        if (phaseObject == null || isRageSpawn || bossRunning || bossTriggered)
            return;

        if (miniBossPhasePauseActive || machineGunPhasePauseActive || machineGunStagePrePauseActive)
            return;

        if (!phaseObject.CompareTag(MiniBossStageTag))
            return;

        miniBossPhasePauseActive = true;
        spawnPaused = true;

        if (cache != null && cache.phaseEndTrigger != null)
            cache.phaseEndTrigger.SuppressFuturePasses();
    }

    private void ResolveMiniBossPhase()
    {
        if (!miniBossPhasePauseActive)
            return;

        miniBossPhasePauseActive = false;

        if (bossRunning || bossTriggered)
            return;

        if (machineGunPhasePauseActive || machineGunStagePrePauseActive)
        {
            spawnPaused = true;
            return;
        }

        if (currentSpawnMode == SpawnMode.Cooldown || gameplayPauseByTransform)
        {
            spawnPaused = true;
            return;
        }

        spawnPaused = false;
        SpawnPhase();
    }

    private static bool HasAliveMiniBoss()
    {
        MiniBoss[] activeMiniBosses = FindObjectsByType<MiniBoss>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return activeMiniBosses != null && activeMiniBosses.Length > 0;
    }

    private static bool HasPendingMiniBossTrigger()
    {
        MiniBossTrigger[] activeTriggers = FindObjectsByType<MiniBossTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return activeTriggers != null && activeTriggers.Length > 0;
    }

    private bool ShouldInsertExtraNormalPhaseBeforeBoss(PhaseInfo sourcePhase)
    {
        return sourcePhase != null &&
               sourcePhase.obj != null &&
               sourcePhase.obj.CompareTag(MachineGunStageTag);
    }

    private void SpawnExtraNormalPhaseBeforeBoss()
    {
        pendingBossExtraNormalPhase = false;

        int stage = Mathf.Clamp(bossTriggerStage, MinPhaseStage, MaxPhaseStage);
        int activePhaseCountBeforeSpawn = GetActivePhaseCount();
        bossAwaitingFinalPass = true;
        spawnPaused = true;

        SpawnPhaseInternal(
            stage: stage,
            isRageSpawn: false,
            countTowardPhaseProgress: false,
            runPhaseRolls: false,
            warnLabel: $"boss buffer stage {stage}",
            allowMachineGunStage: false);

        if (GetActivePhaseCount() <= activePhaseCountBeforeSpawn)
        {
            bossAwaitingFinalPass = false;
            if (bossFlowRoutine != null)
                StopCoroutine(bossFlowRoutine);
            bossFlowRoutine = StartCoroutine(CoRunBossEncounter());
        }
    }

    private static bool HasConfiguredPhasePrefabs(GameObject[] source)
    {
        if (source == null)
            return false;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                return true;
        }

        return false;
    }

    private static bool HasConfiguredSpecialPhasePrefabs(SpecialPhaseEntry[] source)
    {
        if (source == null)
            return false;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null && source[i].prefab != null)
                return true;
        }

        return false;
    }

    private static void AddUniquePrefabs(List<GameObject> target, GameObject[] source)
    {
        if (source == null || target == null)
            return;

        for (int i = 0; i < source.Length; i++)
        {
            GameObject prefab = source[i];
            if (prefab == null || target.Contains(prefab))
                continue;

            target.Add(prefab);
        }
    }

    private static void AddUniquePrefabs(List<GameObject> target, SpecialPhaseEntry[] source)
    {
        if (source == null || target == null)
            return;

        for (int i = 0; i < source.Length; i++)
        {
            GameObject prefab = source[i] != null ? source[i].prefab : null;
            if (prefab == null || target.Contains(prefab))
                continue;

            target.Add(prefab);
        }
    }
}
