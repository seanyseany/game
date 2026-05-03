using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Serialization;

public interface IReinitializable { void Reinit(); }

public class StageManager : MonoBehaviour
{
    public static StageManager Instance;

    [Header("Prefabs")]
    public GameObject[] normalPhasePrefabs;

    [Header("Spawn Settings")]
    public float phaseBaseSpeed = 3f;
    public float spawnX = 50f;
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
    }

    private readonly List<PhaseInfo> activePhases = new List<PhaseInfo>(256);
    private Dictionary<GameObject, Queue<GameObject>> poolDict;

    // ============================ RAGE OBSTACLE ============================
    [Header("Rage Obstacle Settings")]
    [Tooltip("분노 장애물이 생성될 월드 좌표 목록. 인스펙터에서 이 배열만 수정하면 됨")]
    public Vector3[] rageSpawnWorldPositions;
    public float rageSpawnInterval = 0.77f;
    public float rageSpawnDuration = 12f;
    public int rageObstaclesPerPoint = 1;
    public string[] rageObstaclePoolTags;

    private Coroutine rageSpawnRoutine;
    private List<GameObject> phaseShuffleList = new List<GameObject>();

    private const float rageSpawnPhaseMult = 1.8f;

    // ============================ BOSS ============================
    [Header("Boss Settings")]
    [FormerlySerializedAs("bossA")]
    public GameObject stage3BossPrefab;
    [FormerlySerializedAs("bossB")]
    public GameObject stage4BossPrefab;

    [Header("Boss Spawn Fallback (Optional)")]
    public GameObject bossA;
    public GameObject bossB;
    public float bossResumeDelay = 3f;

    private bool bossTriggered = false;
    private bool bossAwaitingFinalPass = false;
    private bool bossRunning = false;
    private bool stageSpawnPausedByRage = false;
    private int bossTriggerStage = 0;
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
    }

    void OnDisable()
    {
        GameData.OnRageStart -= HandleRageStart;
        GameData.OnRageEnd -= HandleRageEnd;
    }

    // ============================ Phase 풀 초기화 ============================
    private void InitPools()
    {
        poolDict = new Dictionary<GameObject, Queue<GameObject>>(normalPhasePrefabs.Length);

        foreach (var prefab in normalPhasePrefabs)
        {
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

        var snap = go.GetComponent<PhaseLayoutSnapshot>() ?? go.AddComponent<PhaseLayoutSnapshot>();
        snap.Capture();

        var cache = go.GetComponent<PhaseCache>();
        if (cache == null)
            cache = go.AddComponent<PhaseCache>();
        go.SetActive(false);
        return go;
    }

    private GameObject GetFromPool(GameObject prefab)
    {
        var queue = poolDict[prefab];
        GameObject go = (queue.Count > 0) ? queue.Dequeue() : CreatePhasePoolObject(prefab);

        if (go.name.IndexOf("_Pooled") < 0)
            go.name = prefab.name + "_Pooled";

        go.transform.position = new Vector3(spawnX, 0f, 0f);
        go.SetActive(true);
        ResetPhase(go);
        return go;
    }

    private void ReturnToPool(GameObject prefab, GameObject go)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            mb.StopAllCoroutines();

        var snap = go.GetComponent<PhaseLayoutSnapshot>();
        snap?.Restore();

        go.SetActive(false);
        go.transform.position = new Vector3(-999f, -999f, 0f);

        poolDict[prefab].Enqueue(go);
    }

    void Update()
    {
        despawnCheckTimer += Time.deltaTime;
        if (despawnCheckTimer >= 0.05f)
        {
            for (int i = activePhases.Count - 1; i >= 0; i--)
            {
                var p = activePhases[i];
                if (!p.obj) { activePhases.RemoveAt(i); continue; }
                if (p.obj.transform.position.x < despawnX)
                {
                    ReturnToPool(FindMatchingPrefab(p.obj), p.obj);
                    activePhases.RemoveAt(i);
                }
            }
            despawnCheckTimer = 0f;
        }
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

            float finalSpeed = frozen ? 0f : phaseBaseSpeed * stageFactor * currentMult;

            if (p.cache != null && p.cache.mover != null)
                p.cache.mover.baseSpeed = finalSpeed;
            else
                p.obj.transform.position += Vector3.left * finalSpeed * Time.fixedDeltaTime;
        }
    }

    private GameObject FindMatchingPrefab(GameObject go)
    {
        foreach (var kv in poolDict)
            if (go.name.StartsWith(kv.Key.name))
                return kv.Key;
        return normalPhasePrefabs.Length > 0 ? normalPhasePrefabs[0] : null;
    }

    // ============================ 트리거에서 호출 ============================
    public void OnPhasePassed()
    {
        // ✅ 보스 트리거 상태면 spawnPaused여도 "마지막 페이즈 감지"를 처리해야 함
        if (bossTriggered && bossAwaitingFinalPass)
        {
            bossAwaitingFinalPass = false;
            if (bossFlowRoutine != null) StopCoroutine(bossFlowRoutine);
            bossFlowRoutine = StartCoroutine(CoRunBossEncounter());
            return;
        }

        if (spawnPaused) return;
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
        phaseSpawnCount = 0;
    }

    public void ResetState()
    {
        phaseSpawnCount = 0;
        spawnPaused = false;
        stageSpawnPausedByRage = false;
        activePhases.Clear();
        isDelayActive = false;
        if (stageLoopRoutine != null)
        {
            StopCoroutine(stageLoopRoutine);
            stageLoopRoutine = null;
        }
        StopStage4PhasePrefabSpawner();

        // ✅ 보스 상태 리셋
        ResetBossState();
        ClearActiveCaveInstance();
        SetBackgroundVisible(background1Prefab, true);
        SetBackgroundVisible(background2Prefab, false);
    }

    private void SpawnPhase()
    {
        bool rageNow = (GameData.Instance != null && GameData.Instance.rageMode);

        if (phaseShuffleList.Count == 0)
        {
            phaseShuffleList.AddRange(normalPhasePrefabs);
            ShuffleList(phaseShuffleList);
        }

        var prefab = phaseShuffleList[0];
        phaseShuffleList.RemoveAt(0);

        var go = GetFromPool(prefab);

        if (GameData.Instance != null)
        {
            int stage = GetSpeedStage();
            GameData.Instance.RollManaForThisPhase(stage);
            GameData.Instance.RollHolyManaForThisPhase(stage);

            // ✅ GameData가 "speedUp3/4 직전" 보스 트리거 판단
            GameData.Instance.CheckBossTriggerBeforeSpeedUp(this, phaseSpawnCount);
        }

        var cache = go.GetComponent<PhaseCache>();
        if (cache != null && cache.mover != null)
            cache.mover.baseSpeed = phaseBaseSpeed;

        activePhases.Add(new PhaseInfo
        {
            obj = go,
            cache = cache,
            spawnTime = Time.time,
            isRageSpawn = rageNow,
            freezeUntil = 0f
        });

        phaseSpawnCount++;
    }

    private IEnumerator CoStartStageLoop()
    {
        spawnPaused = true;

        float delay = Mathf.Max(0f, startSpawnDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

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
        if (phaseSpawnCount >= speedUp4) return speedMult4;
        if (phaseSpawnCount >= speedUp3) return speedMult3;
        if (phaseSpawnCount >= speedUp2) return speedMult2;
        if (phaseSpawnCount >= speedUp1) return speedMult1;
        return 1f;
    }

    private IEnumerator DelayCooldownTimer()
    {
        float stageSpeedFactor = GetStageSpeedFactor();
        float baseCooldown = 7f;
        float adjustedCooldown = baseCooldown / stageSpeedFactor;

        yield return new WaitForSeconds(adjustedCooldown);
        isDelayActive = false;
    }

    private void ResetPhase(GameObject phase)
    {
        var cache = phase.GetComponent<PhaseCache>();
        if (cache != null)
        {
            cache.ResetCached();
        }

        // 스폰 프레임에는 활성 오브젝트만 재초기화한다.
        // 비활성 자식까지 강제 Reinit하면 activeSelf/pose가 꼬일 수 있다.
        foreach (var r in phase.GetComponentsInChildren<IReinitializable>(false))
            r.Reinit();
    }

    public void SetSpawnPaused(bool paused) => spawnPaused = paused;

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
            yield return new WaitForSeconds(bossResumeDelay);
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
            yield return new WaitForSeconds(bossResumeDelay);
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
            yield return new WaitForSeconds(bossResumeDelay);
            ResumeAfterBoss(startStage4PrefabSpawner: bossTriggerStage >= 4);
            yield break;
        }

        // 3) 보스 끝날 때까지 대기(비활성/파괴)
        while (activeBoss != null && activeBoss.activeInHierarchy)
            yield return null;

        int completedBossStage = bossTriggerStage;
        if (completedBossStage == 3)
        {
            yield return CoStage3To4Transition();
            ResumeAfterBoss(startStage4PrefabSpawner: true);
        }
        else
        {
            // 4) 3초 딜레이 후 스폰 재개
            yield return new WaitForSeconds(bossResumeDelay);
            ResumeAfterBoss(startStage4PrefabSpawner: completedBossStage >= 4);
        }
    }

    private void ResumeAfterBoss(bool startStage4PrefabSpawner = false)
    {
        bossRunning = false;
        bossTriggered = false;
        bossAwaitingFinalPass = false;
        bossTriggerStage = 0;

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
            yield return new WaitForSeconds(delay);

        StartStage4PhasePrefabSpawner();
    }

    private void ResetBossState()
    {
        bossTriggered = false;
        bossAwaitingFinalPass = false;
        bossRunning = false;
        bossTriggerStage = 0;

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

    private IEnumerator CoStage3To4Transition()
    {
        // 요청사항:
        // Stage3 보스 처치 후 즉시 cave를 띄우지 않고, 플레이어 분노(rage)가 끝난 뒤 전환 시작
        while (GameData.Instance != null && GameData.Instance.rageMode)
            yield return null;

        yield return new WaitForSeconds(1f);

        ClearActiveCaveInstance();
        activeCaveObj = GetCaveFromPool();
        if (activeCaveObj == null)
        {
            yield return new WaitForSeconds(stage4StartDelayAfterBgSwitch);
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
                SetBackgroundVisible(background1Prefab, false);
                SetBackgroundVisible(background2Prefab, true);
                yield return new WaitForSeconds(Mathf.Max(0f, stage4StartDelayAfterBgSwitch));
                yield break;
            }
            yield return null;
        }

        // cave가 빨리 사라진 예외 케이스에서도 stage4 시작 지연은 보장
        yield return new WaitForSeconds(Mathf.Max(0f, stage4StartDelayAfterBgSwitch));
    }

    private IEnumerator CoMoveCaveAndRecycle(GameObject caveObj)
    {
        if (caveObj == null) yield break;

        Vector3 end = caveEndWorldPos;
        float speed = Mathf.Max(0.01f, caveMoveSpeed);

        while (caveObj != null && caveObj.activeSelf)
        {
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

    // ============================ RAGE 장애물 스폰 ============================
    private void StartRageObstacleSpawn()
    {
        Debug.Log("🔥 Rage Obstacle Spawn Started");
        if (rageSpawnRoutine != null) StopCoroutine(rageSpawnRoutine);
        rageSpawnRoutine = StartCoroutine(RageObstacleSpawnCoroutine());
    }

    public void StopRageObstacleSpawn()
    {
        if (rageSpawnRoutine != null)
        {
            StopCoroutine(rageSpawnRoutine);
            rageSpawnRoutine = null;
        }
    }

    private void HandleRageStart()
    {
        StartRageObstacleSpawn();

        if (bossRunning || bossTriggered)
            return;

        stageSpawnPausedByRage = true;
        spawnPaused = true;
    }

    private void HandleRageEnd()
    {
        StopRageObstacleSpawn();

        if (!stageSpawnPausedByRage)
            return;

        stageSpawnPausedByRage = false;

        if (bossRunning || bossTriggered)
            return;

        spawnPaused = false;
        SpawnPhase();
    }

    public void ClearAllRageObstacles()
    {
        if (rageObstaclePoolTags != null && ObjectPool.Instance != null)
            ObjectPool.Instance.ReturnAllActive(rageObstaclePoolTags);
    }

    private IEnumerator RageObstacleSpawnCoroutine()
    {
        float elapsed = 0f;
        Debug.Log("⚡ Rage Coroutine RUNNING!");

        if (rageSpawnWorldPositions == null || rageSpawnWorldPositions.Length == 0)
        {
            Debug.LogWarning("[RageObstacle] rageSpawnWorldPositions is empty. Set world positions in StageManager inspector.");
            rageSpawnRoutine = null;
            yield break;
        }

        while (elapsed < rageSpawnDuration)
        {
            for (int p = 0; p < rageSpawnWorldPositions.Length; p++)
            {
                Vector3 spawnPos = rageSpawnWorldPositions[p];
                for (int i = 0; i < rageObstaclesPerPoint; i++)
                    SpawnRageObstacle(spawnPos);
            }

            yield return new WaitForSeconds(rageSpawnInterval);
            elapsed += rageSpawnInterval;
        }

        rageSpawnRoutine = null;
        Debug.Log("🔥 Rage Obstacle Spawn Ended");
    }

    private void SpawnRageObstacle(Vector3 spawnPos)
    {
        if (rageObstaclePoolTags == null || rageObstaclePoolTags.Length == 0)
        {
            Debug.LogWarning("⚠️ rageObstaclePoolTags 비어있음");
            return;
        }

        string tag = rageObstaclePoolTags[Random.Range(0, rageObstaclePoolTags.Length)];
        GameObject go = ObjectPool.Instance.SpawnFromPool(tag, spawnPos, Quaternion.identity);
        if (go == null)
        {
            Debug.LogWarning($"[RageObstacle] Pool '{tag}' is empty or not set.");
            return;
        }

        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.WakeUp();
        }

        var mover = go.GetComponent<Mover>();
        if (mover != null)
            mover.baseSpeed = phaseBaseSpeed;

        foreach (var r in go.GetComponentsInChildren<IReinitializable>(true))
            r.Reinit();

        go.SetActive(true);
        Debug.Log($"🧱 Rage Obstacle Spawned: {tag} at {spawnPos}");
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public int GetSpeedStage()
    {
        if (phaseSpawnCount >= speedUp4) return 4;
        if (phaseSpawnCount >= speedUp3) return 3;
        if (phaseSpawnCount >= speedUp2) return 2;
        if (phaseSpawnCount >= speedUp1) return 1;
        return 1;
    }
}
