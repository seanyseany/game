using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Serialization;

public interface IReinitializable { void Reinit(); }
// Activation already resets these components; pools must not reset them a second time.
public interface IReinitializeOnEnable : IReinitializable { }

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
    private const int PostBossMixedStage = 104;
    private const string MachineGunStageTag = "MachineGunStage";
    private const string MiniBossStageTag = "miniBoss";
    private const int BossStage3 = 3;
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
    public float despawnX = -20f;
    private float startSpawnDelay = 2f;

    [Header("Speed Up Thresholds")]
    public int speedUp1 = 3;
    public int speedUp2 = 6;
    [Tooltip("스테이지 3 이후 보스 전투를 예약하는 누적 페이즈 스폰 수")]
    public int speedUp3 = 9;

    [Header("Speed Up Multipliers")]
    public float speedMult1 = 1.1f;
    public float speedMult2 = 1.25f;
    public float speedMult3 = 1.4f;
    [Tooltip("보스 이후 스테이지 3·4 혼합 구간의 속도 배율")]
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
        public bool passedEndTrigger;
    }

    private readonly List<PhaseInfo> activePhases = new List<PhaseInfo>(256);
    private Dictionary<GameObject, Queue<GameObject>> poolDict;
    private readonly Dictionary<GameObject, GameObject> pooledInstanceToPrefab = new Dictionary<GameObject, GameObject>(256);
    private readonly HashSet<GameObject> activePhaseObjects = new HashSet<GameObject>();
    private readonly Dictionary<GameObject, PhaseCache> phaseCaches = new Dictionary<GameObject, PhaseCache>();
    private readonly Dictionary<GameObject, int> createdCountByPrefab = new Dictionary<GameObject, int>();
    private readonly Queue<GameObject> prewarmQueue = new Queue<GameObject>();
    private readonly HashSet<GameObject> pendingPrewarm = new HashSet<GameObject>();
    private Coroutine prewarmRoutine;
    private GameObject[] mixedPhasePrefabs;
    private SpecialPhaseEntry[] mixedSpecialPhasePrefabs;

    [Header("Rage Phase Settings")]
    public float ragePhaseDuration = 12f;
    public float ragePhaseResumeDelay = 3f;

    private Coroutine ragePhaseRoutine;
    private readonly Dictionary<int, List<GameObject>> phaseShuffleByStage = new Dictionary<int, List<GameObject>>(MaxPhaseStage);
    private bool testPhaseSequenceCompleted;
    private SpawnMode currentSpawnMode = SpawnMode.Normal;
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
    [FormerlySerializedAs("stage3BossPrefab")]
    [FormerlySerializedAs("bossA")]
    public GameObject bossPrefab;
    public string bossPhaseTriggerTag = "Boss";
    public float bossResumeDelay = 3f;

    private bool bossTriggered = false;
    private bool bossAwaitingFinalPass = false;
    private bool bossRunning = false;
    private bool gameplayPauseByTransform = false;
    private int bossTriggerStage = 0;
    private int pendingMachineGunBossStage = 0;
    private bool pendingBossExtraNormalPhase = false;
    private bool postBossMixedPhaseUnlocked = false;
    private GameObject activeBoss;
    public bool HasActiveBoss => activeBoss != null && activeBoss.activeInHierarchy;
    private bool activeBossIsSceneObject = false;
    private Coroutine bossFlowRoutine;

    [Header("Background")]
    [FormerlySerializedAs("background1Prefab")]
    public GameObject backgroundPrefab;

    private Coroutine stageLoopRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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
        if (poolDict != null)
            return;

        List<GameObject> allPrefabs = CollectAllPhasePrefabs();
        poolDict = new Dictionary<GameObject, Queue<GameObject>>(allPrefabs.Count);

        foreach (var prefab in allPrefabs)
        {
            if (prefab == null || poolDict.ContainsKey(prefab))
                continue;

            poolDict[prefab] = new Queue<GameObject>();
        }
    }

    private void RequestStagePrewarm(int stage)
    {
        GameObject[] normal = GetPhasePrefabsForStage(stage);
        if (normal != null)
            for (int i = 0; i < normal.Length; i++)
                RequestPrefabPrewarm(normal[i]);

        SpecialPhaseEntry[] special = GetSpecialPhaseEntriesForStage(stage);
        if (special != null)
            for (int i = 0; i < special.Length; i++)
                RequestPrefabPrewarm(special[i]?.prefab);

        if (prewarmRoutine == null && prewarmQueue.Count > 0)
            prewarmRoutine = StartCoroutine(PrewarmQueuedPhases());
    }

    private int InitialPoolCount => Mathf.Clamp(initialPoolSizePerPrefab, 0, Mathf.Max(0, poolSizePerPrefab));

    private void RequestPrefabPrewarm(GameObject prefab)
    {
        if (prefab == null || InitialPoolCount == 0)
            return;
        if (createdCountByPrefab.TryGetValue(prefab, out int count) && count >= InitialPoolCount)
            return;
        if (pendingPrewarm.Add(prefab))
            prewarmQueue.Enqueue(prefab);
    }

    private IEnumerator PrewarmQueuedPhases()
    {
        // One clone per frame, including special phases. Active clones also count as warmed.
        while (prewarmQueue.Count > 0)
        {
            yield return null;
            if (gameplayPauseByTransform || (GameData.Instance != null && GameData.Instance.gameOver))
                continue;

            GameObject prefab = prewarmQueue.Peek();
            createdCountByPrefab.TryGetValue(prefab, out int count);
            if (count < InitialPoolCount)
                poolDict[prefab].Enqueue(CreatePhasePoolObject(prefab));

            createdCountByPrefab.TryGetValue(prefab, out count);
            if (count >= InitialPoolCount)
            {
                prewarmQueue.Dequeue();
                pendingPrewarm.Remove(prefab);
            }
        }
        prewarmRoutine = null;
    }

    private void PrewarmUpcomingStage()
    {
        int stage = GetCurrentPhaseStage();
        RequestStagePrewarm(stage);
        if (stage == TestPhaseStage)
            RequestStagePrewarm(MinPhaseStage);
        else if (stage >= MinPhaseStage && stage < MaxPhaseStage)
            RequestStagePrewarm(stage + 1);
    }

    private GameObject CreatePhasePoolObject(GameObject prefab)
    {
        var go = Instantiate(prefab, new Vector3(-999f, -999f, 0f), prefab.transform.rotation);
        go.name = prefab.name + "_Pooled";
        pooledInstanceToPrefab[go] = prefab;

        var snap = go.GetComponent<PhaseLayoutSnapshot>() ?? go.AddComponent<PhaseLayoutSnapshot>();
        snap.Capture();

        var cache = go.GetComponent<PhaseCache>();
        if (cache == null)
            cache = go.AddComponent<PhaseCache>();
        cache.EnsureCached();
        phaseCaches[go] = cache;
        createdCountByPrefab.TryGetValue(prefab, out int count);
        createdCountByPrefab[prefab] = count + 1;
        go.SetActive(false);
        cache.layout.Restore(false);
        return go;
    }

    private GameObject GetFromPool(GameObject prefab, Vector3 spawnPos)
    {
        if (prefab == null)
            return null;

        if (!poolDict.TryGetValue(prefab, out var queue) || queue == null)
        {
            queue = new Queue<GameObject>();
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

        go.transform.position = spawnPos;
        PhaseCache cache = phaseCaches[go];
        cache.ResetCached();
        go.SetActive(true);
        cache.ReinitializeAfterActivation();
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

        PhaseCache cache = phaseCaches[go];
        cache.StopRuntimeActivity();
        // Restore once, under an inactive root, so offscreen children do not restart gameplay.
        go.SetActive(false);
        cache.layout.Restore(false);
        go.transform.position = new Vector3(-999f, -999f, 0f);
        activePhaseObjects.Remove(go);

        if (!poolDict.TryGetValue(prefab, out var queue) || queue == null)
        {
            queue = new Queue<GameObject>();
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
        if (sourcePhase != null)
            sourcePhase.passedEndTrigger = true;

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

            return;
        }

        SpawnPhase();
    }

    // ============================ 일반 Phase 스폰 ============================
    public void StartStageLoop()
    {
        InitPools();
        ResetState();
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
        // Cancel delayed spawns too; none of the previous run may resume after a retry.
        StopAllCoroutines();
        stageLoopRoutine = null;
        ragePhaseRoutine = null;
        bossFlowRoutine = null;
        prewarmRoutine = null;
        prewarmQueue.Clear();
        pendingPrewarm.Clear();
        mixedPhasePrefabs = null;
        mixedSpecialPhasePrefabs = null;
        gameplayPauseByTransform = false;

        ClearAllPhases();
        spawnPaused = false;
        currentSpawnMode = SpawnMode.Normal;
        pendingInitialRagePhaseSpawn = false;
        machineGunPhasePauseActive = false;
        machineGunStagePrePauseActive = false;
        phasePassedDuringMachineGunPause = false;
        miniBossPhasePauseActive = false;
        phaseShuffleByStage.Clear();
        isDelayActive = false;

        // ✅ 보스 상태 리셋
        ResetBossState();
        SetBackgroundVisible(backgroundPrefab, true);
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
            runPhaseRolls: !isTestPhase);
    }

    private IEnumerator CoStartStageLoop()
    {
        spawnPaused = true;
        // GameData finishes resetting the run before warming or spawning any phase.
        yield return null;

        float startedAt = Time.time;
        RequestStagePrewarm(GetCurrentPhaseStage());
        RequestStagePrewarm(RagePhaseStage);
        while (pendingPrewarm.Count > 0)
            yield return null;

        PrewarmUpcomingStage();

        float delay = Mathf.Max(0f, startSpawnDelay - (Time.time - startedAt));
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
        if (stage != BossStage3 || postBossMixedPhaseUnlocked || bossRunning || bossTriggered)
            return;

        bossTriggered = true;
        bossTriggerStage = stage;
        bossAwaitingFinalPass = true;

        // ✅ 즉시 스폰 중단 (마지막 페이즈가 detector 찍을 때까지 기다림)
        spawnPaused = true;

    }

    public void QueueBossEncounterAfterMachineGun(int stage)
    {
        if (stage != BossStage3 || postBossMixedPhaseUnlocked || bossRunning || bossTriggered)
            return;

        pendingMachineGunBossStage = stage;
    }

    public bool TryResolveBossStageFromTaggedObject(GameObject source, out int stage)
    {
        stage = 0;
        if (source == null || postBossMixedPhaseUnlocked)
            return false;

        string tag = source.tag;
        if (!string.IsNullOrEmpty(bossPhaseTriggerTag) && tag == bossPhaseTriggerTag)
        {
            stage = BossStage3;
            return true;
        }

        return false;
    }

    private void StartBossEncounterNow(int stage)
    {
        if (stage != BossStage3 || postBossMixedPhaseUnlocked || bossRunning || bossTriggered)
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

    }

    private IEnumerator CoRunBossEncounter()
    {
        bossRunning = true;
        DeactivateBossTemplateIfSceneObject();

        // 1) 등록된 보스 활성화
        ClearActiveBossInstance();

        GameObject source = bossPrefab;
        if (source == null)
        {
            yield return WaitForSecondsRespectingGameplayPause(bossResumeDelay);
            ResumeAfterBoss();
            yield break;
        }

        // StageManager는 좌표를 정하지 않는다.
        // 씬에 배치된 보스 오브젝트를 그대로 활성화해서 사용한다.
        activeBossIsSceneObject = source.scene.IsValid();
        if (!activeBossIsSceneObject)
        {
            yield return WaitForSecondsRespectingGameplayPause(bossResumeDelay);
            ResumeAfterBoss();
            yield break;
        }

        activeBoss = source;

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
            yield return WaitForSecondsRespectingGameplayPause(bossResumeDelay);
            ResumeAfterBoss();
            yield break;
        }

        // 3) 보스 끝날 때까지 대기(비활성/파괴)
        while (activeBoss != null && activeBoss.activeInHierarchy)
            yield return null;

        yield return WaitForSecondsRespectingGameplayPause(bossResumeDelay);
        ResumeAfterBoss();
    }

    private void ResumeAfterBoss()
    {
        // The single boss unlocks the combined stage 3/4 pool for the rest of this run.
        postBossMixedPhaseUnlocked = true;
        bossRunning = false;
        bossTriggered = false;
        bossAwaitingFinalPass = false;
        bossTriggerStage = 0;
        pendingMachineGunBossStage = 0;
        pendingBossExtraNormalPhase = false;
        spawnPaused = false;

        RequestStagePrewarm(PostBossMixedStage);
        SpawnPhase();
    }

    private void ResetBossState()
    {
        bossTriggered = false;
        bossAwaitingFinalPass = false;
        bossRunning = false;
        bossTriggerStage = 0;
        pendingMachineGunBossStage = 0;
        pendingBossExtraNormalPhase = false;
        postBossMixedPhaseUnlocked = false;

        if (bossFlowRoutine != null)
        {
            StopCoroutine(bossFlowRoutine);
            bossFlowRoutine = null;
        }

        ClearActiveBossInstance();

        DeactivateBossTemplateIfSceneObject();
    }

    // 게임오버 시 보스 전투 오브젝트 즉시 정리용
    public void ForceClearBossNow()
    {
        ResetBossState();
    }

    private void SetBackgroundVisible(GameObject bg, bool visible)
    {
        if (bg == null) return;

        var mrs = bg.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < mrs.Length; i++)
            if (mrs[i] != null) mrs[i].enabled = visible;
    }

    private void DeactivateBossTemplateIfSceneObject()
    {
        DeactivateIfSceneObject(bossPrefab);
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
        if (bossRunning || bossTriggered)
        {
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
        BombHitBox.ClearActiveMachineGunCholesterolExplosions();
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
            return;
        }

        if (ragePhaseRoutine != null)
            StopCoroutine(ragePhaseRoutine);

        machineGunStagePrePauseActive = false;
        spawnPaused = machineGunPhasePauseActive;
        currentSpawnMode = SpawnMode.Rage;
        pendingInitialRagePhaseSpawn = true;
        SuppressActivePhaseTriggers(false);

        // 분노 시작 시에는 기존 phase trigger를 기다리지 않고
        // 첫 rage phase를 즉시 스폰한다.
        if (!gameplayPauseByTransform && !machineGunPhasePauseActive)
        {
            SpawnRagePhaseDirect();
            pendingInitialRagePhaseSpawn = false;
        }

        ragePhaseRoutine = StartCoroutine(CoRunRagePhaseSequence());
    }

    private void StopRagePhaseSpawn()
    {
        if (ragePhaseRoutine != null)
        {
            StopCoroutine(ragePhaseRoutine);
            ragePhaseRoutine = null;
        }

        currentSpawnMode = SpawnMode.Normal;
        pendingInitialRagePhaseSpawn = false;
        machineGunStagePrePauseActive = false;
    }

    private IEnumerator CoRunRagePhaseSequence()
    {
        yield return WaitForSecondsRespectingGameplayPause(Mathf.Max(0f, ragePhaseDuration));

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
            // Let the final rage phase reach the detector before starting the normal sequence.
            // Its pass during cooldown is recorded even though spawning is paused.
            PhaseInfo lastRagePhase = null;
            for (int i = activePhases.Count - 1; i >= 0; i--)
            {
                if (activePhases[i].isRageSpawn && activePhases[i].obj != null)
                {
                    lastRagePhase = activePhases[i];
                    break;
                }
            }

            while (lastRagePhase != null && lastRagePhase.obj != null &&
                   activePhaseObjects.Contains(lastRagePhase.obj) &&
                   !lastRagePhase.passedEndTrigger)
            {
                if (bossRunning || bossTriggered)
                    break;
                yield return null;
            }

            while ((machineGunPhasePauseActive || miniBossPhasePauseActive || gameplayPauseByTransform) &&
                   !bossRunning && !bossTriggered)
                yield return null;

            SuppressActivePhaseTriggers(true);
        }

        currentSpawnMode = SpawnMode.Normal;
        if (!bossRunning && !bossTriggered)
        {
            machineGunStagePrePauseActive = false;
            spawnPaused = false;
            SpawnPhase();
        }

        ragePhaseRoutine = null;
        yield break;
    }
    private void SpawnRagePhaseDirect()
    {
        SpawnPhaseInternal(
            stage: RagePhaseStage,
            isRageSpawn: true,
            countTowardPhaseProgress: false,
            runPhaseRolls: false);
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

        int speedStage = GetSpeedStage();
        if (postBossMixedPhaseUnlocked)
            return PostBossMixedStage;

        return speedStage;
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
        AddUniquePrefabs(allPrefabs, stage1SpecialPhasePrefabs);
        AddUniquePrefabs(allPrefabs, stage2SpecialPhasePrefabs);
        AddUniquePrefabs(allPrefabs, stage3SpecialPhasePrefabs);
        AddUniquePrefabs(allPrefabs, stage4SpecialPhasePrefabs);
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

        if (stage == PostBossMixedStage)
            return mixedPhasePrefabs ??= CombinePhasePrefabs(stage3PhasePrefabs, stage4PhasePrefabs);

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
            ReserveNormalPhaseBeforeBoss(shuffleList, stage, source, specialEntries);
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

    private void ReserveNormalPhaseBeforeBoss(List<GameObject> cycle, int stage, GameObject[] normalPrefabs, SpecialPhaseEntry[] specialEntries)
    {
        if (postBossMixedPhaseUnlocked || bossTriggered || bossRunning || stage != BossStage3)
            return;

        int bossPhaseCount = speedUp3;
        int finalPhaseIndex = bossPhaseCount - phaseSpawnCount - 1;
        // A stage can refill its cycle before reaching the boss threshold.
        if (finalPhaseIndex < 0 || finalPhaseIndex >= cycle.Count)
            return;

        List<GameObject> availableNormals = new List<GameObject>();
        AddAvailableNormalPrefabs(availableNormals, normalPrefabs, allowMachineGunStage: false);
        availableNormals.RemoveAll(prefab => prefab.CompareTag(MiniBossStageTag));
        if (specialEntries != null)
        {
            foreach (SpecialPhaseEntry entry in specialEntries)
            {
                if (entry != null && entry.prefab != null)
                    availableNormals.RemoveAll(prefab => prefab == entry.prefab);
            }
        }

        if (availableNormals.Contains(cycle[finalPhaseIndex]))
            return;

        // Keep the rolled specials by moving the final one to an earlier normal slot.
        for (int i = 0; i < finalPhaseIndex; i++)
        {
            if (!availableNormals.Contains(cycle[i]))
                continue;

            GameObject normal = cycle[i];
            cycle[i] = cycle[finalPhaseIndex];
            cycle[finalPhaseIndex] = normal;
            return;
        }

        // Even a one-phase stage or an all-special roll must end with a normal phase.
        if (availableNormals.Count > 0)
        {
            cycle[finalPhaseIndex] = availableNormals[Random.Range(0, availableNormals.Count)];
            return;
        }

        Debug.LogWarning($"[StageManager] Stage {stage} needs a normal phase prefab before the boss.", this);
    }

    private int GetPhaseCycleCountForStage(int stage)
    {
        if (stage == PostBossMixedStage || stage == MaxPhaseStage)
        {
            GameObject[] normalPrefabs = GetPhasePrefabsForStage(stage);
            SpecialPhaseEntry[] specialEntries = GetSpecialPhaseEntriesForStage(stage);
            return Mathf.Max(1, (normalPrefabs?.Length ?? 0) + (specialEntries?.Length ?? 0));
        }

        switch (Mathf.Clamp(stage, MinPhaseStage, MaxPhaseStage))
        {
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
        if (stage == PostBossMixedStage)
            return mixedSpecialPhasePrefabs ??= CombineSpecialPhaseEntries(stage3SpecialPhasePrefabs, stage4SpecialPhasePrefabs);

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

        if (stage != PostBossMixedStage && (stage < MinPhaseStage || stage > MaxPhaseStage))
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

        if (stage == PostBossMixedStage)
        {
            // Endless mixed cycles include every normal prefab, independent of speed thresholds.
            // Specials are additional chance-based entries, so they cannot replace all normal phases.
            target.AddRange(availableNormals);
            for (int i = 0; i < availableSpecials.Count; i++)
            {
                SpecialPhaseEntry special = availableSpecials[i];
                float chance = Mathf.Clamp01(special.spawnChance);
                if (chance > 0f && (chance >= 1f || Random.value < chance))
                    target.Add(special.prefab);
            }
            return;
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

    private static GameObject[] CombinePhasePrefabs(params GameObject[][] sources)
    {
        List<GameObject> merged = new List<GameObject>();
        if (sources == null)
            return merged.ToArray();

        for (int i = 0; i < sources.Length; i++)
            AddUniquePrefabs(merged, sources[i]);

        return merged.ToArray();
    }

    private static SpecialPhaseEntry[] CombineSpecialPhaseEntries(params SpecialPhaseEntry[][] sources)
    {
        List<SpecialPhaseEntry> merged = new List<SpecialPhaseEntry>();
        HashSet<GameObject> seenPrefabs = new HashSet<GameObject>();

        if (sources == null)
            return merged.ToArray();

        for (int i = 0; i < sources.Length; i++)
        {
            SpecialPhaseEntry[] source = sources[i];
            if (source == null)
                continue;

            for (int j = 0; j < source.Length; j++)
            {
                SpecialPhaseEntry entry = source[j];
                GameObject prefab = entry != null ? entry.prefab : null;
                if (prefab == null || !seenPrefabs.Add(prefab))
                    continue;

                merged.Add(entry);
            }
        }

        return merged.ToArray();
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

    private void SpawnPhaseInternal(int stage, bool isRageSpawn, bool countTowardPhaseProgress, bool runPhaseRolls)
    {
        SpawnPhaseInternal(stage, isRageSpawn, countTowardPhaseProgress, runPhaseRolls, allowMachineGunStage: true);
    }

    private void SpawnPhaseInternal(int stage, bool isRageSpawn, bool countTowardPhaseProgress, bool runPhaseRolls, bool allowMachineGunStage)
    {
        if (GameData.Instance != null && (GameData.Instance.IsResetting || GameData.Instance.gameOver))
            return;
        GameObject prefab = GetNextPhasePrefabForStage(stage, allowMachineGunStage);
        if (prefab == null)
        {
            return;
        }

        var go = GetFromPool(prefab, GetPhaseSpawnPosition(isRageSpawn));
        if (go == null)
            return;

        var cache = phaseCaches[go];
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
        {
            phaseSpawnCount++;
            PrewarmUpcomingStage();
        }

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
