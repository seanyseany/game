using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class GameData : MonoBehaviour
{
    private const string ArcadeScenePrefix = "arcade";
    private static int pendingSelectedPlayerType = 1;
    private static int pendingSelectedPlayerLevel = 1;
    private static bool hasPendingSelectedPlayer;

    public static GameData Instance;

    [Header("References")]
    public Player playerRef;

    [Header("Player (compat)")]
    [Range(1, 5)] public int selectedPlayerType = 1;
    public int[] playerLevels = new int[6];

    [Header("Stage Speed")]
    public float stageSpeedMult = 1f;

    [Header("Speed Settings")]
    public float defaultStageSpeedMult = 1f;
    public float rageSpeedFactor = 1.5f;
    private float preRageSpeedMult = 1f;

    [Header("Rage")]
    public bool rageMode = false;
    public float rageDuration = 15f;
    public bool debugForceRage = false;
    public bool debugForceMachineGunTrigger = false;
    public bool rageReady = false;
    private float rageEndTime = -1f;
    public float rageStartTime = -1f;

    [Header("Score")]
    public int score = 0;
    private float survivalTime = 0f;
    private int o2Score = 0;
    private int totalKillCount = 0;

    public static System.Action OnRageStart;
    public static System.Action OnRageEnd;
    public static System.Action OnMachineGunTrigger;
    public static System.Action OnMachineGunSequenceStart;
    public static System.Action OnMachineGunObstacleSpawnStop;
    public static System.Action OnMachineGunSequenceEnd;
    public static System.Action OnGameOver;

    public bool gameOver = false;
    private Coroutine restartRoutine;
    private Coroutine speedTween;
    private float lastObstacleTouchTime = -999f;

    private const string POOL_MISSILE = "missile";
    private const string POOL_WARNING = "Warning";
    private const string POOL_SMOKE = "Smoke";
    private const string POOL_GATE_SMOKE = "GateSmoke";
    private const string POOL_GATE_SMOKE_BACK = "GateSmokeBack";
    private const string POOL_SPEED_EFFECT = "SpeedEffect";

    [Header("Obstacle Contact Tunables")]
    public float decelerateTime = 0.5f;
    public float pushSpeed = 1.2f;
    public float stopTime = 0.5f;
    public float recoverTime = 1.0f;

    public enum ObstacleContactState
    {
        None,
        Decelerating,
        Pushing,
        Stopped,
        Recovering
    }

    private ObstacleContactState obstacleState = ObstacleContactState.None;
    private Coroutine obstacleRoutine;
    private int obstacleContactCount = 0;
    private bool forceStopStage = false;
    private Coroutine gameOverSpeedRoutine;
    private Coroutine gameOverUiRoutine;
    private bool arcadeRewardsGranted;
    private bool machineGunTriggerPending;
    private bool machineGunSequenceActive;
    private bool protectNextPostMachineGunPhaseObstacles;
    private BombLauncher bombLauncherRef;
    private Train trainRef;
    private RageUIController rageUiRef;
    private GateHealth gateHealthRef;
    private ScoreUI scoreUiRef;
    private GameOverUI gameOverUiRef;

    [Header("Game Over")]
    public float gameOverUiDelay = 1.5f;

    // ===================== BOSS TRIGGER =====================
    [Header("Boss Trigger (Debug)")]
    public bool debugForceBossStage3 = false;
    public bool debugForceBossStage4 = false;

    private bool bossStage3Triggered = false;
    private bool bossStage4Triggered = false;
    private bool arcadeSceneActive;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ApplyPendingSelectedPlayer();
        SceneManager.sceneLoaded += HandleSceneLoaded;
        gameOver = false;
    }

    void Start()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        arcadeSceneActive = IsArcadeScene(activeScene);

        if (arcadeSceneActive)
            ResetGame();
        else
            SuspendForNonArcadeScene();
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void Update()
    {
        if (!arcadeSceneActive)
            return;

        if (gameOver) return;

        survivalTime += Time.deltaTime;

        if (debugForceRage)
        {
            debugForceRage = false;
            ActivateRageMode(rageDuration);
        }

        if (debugForceMachineGunTrigger)
        {
            debugForceMachineGunTrigger = false;
            TriggerMachineGun();
        }

        // ✅ 디버그 보스 트리거
        if (debugForceBossStage3)
        {
            debugForceBossStage3 = false;
            bossStage3Triggered = true;
            if (StageManager.Instance != null)
                StageManager.Instance.TriggerBossEncounter(3);
        }
        if (debugForceBossStage4)
        {
            debugForceBossStage4 = false;
            bossStage4Triggered = true;
            if (StageManager.Instance != null)
                StageManager.Instance.TriggerBossEncounter(4);
        }

        if (rageMode && Time.time >= rageEndTime)
        {
            stageSpeedMult = defaultStageSpeedMult;
            rageMode = false;

            if (speedTween != null) { StopCoroutine(speedTween); speedTween = null; }

            OnRageEnd?.Invoke();
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        arcadeSceneActive = IsArcadeScene(scene);
        ClearSceneReferences();
        ApplyPendingSelectedPlayer();

        if (arcadeSceneActive)
            ResetGame();
        else
            SuspendForNonArcadeScene();
    }

    // ✅ StageManager가 마지막 페이즈를 스폰한 직후 호출하는 훅
    public void CheckBossTriggerBeforeSpeedUp(StageManager sm, int currentPhaseSpawnCount)
    {
        if (gameOver) return;
        if (sm == null) return;

        // 지정된 누적 스폰 수에 도달한 "마지막 페이즈"가 지나간 뒤 보스가 나와야 하므로
        // 마지막 페이즈를 스폰한 직후 예약을 걸고, 실제 시작은 해당 페이즈의 PhaseEndTrigger에서 한다.
        if (!bossStage3Triggered && currentPhaseSpawnCount == sm.speedUp3)
        {
            bossStage3Triggered = true;
            sm.TriggerBossEncounter(3);
        }

        if (!bossStage4Triggered && currentPhaseSpawnCount == sm.speedUp4)
        {
            bossStage4Triggered = true;
            sm.TriggerBossEncounter(4);
        }
    }

    // ------------------ RAGE ------------------
    public void ActivateRageMode(float seconds)
    {
        if (speedTween != null) { StopCoroutine(speedTween); speedTween = null; }

        // 분노 진입 시 장애물 감속/밀림 시퀀스를 끊고 정상 분노 속도로 복귀시킨다.
        if (obstacleRoutine != null)
        {
            StopCoroutine(obstacleRoutine);
            obstacleRoutine = null;
        }
        obstacleContactCount = 0;
        obstacleState = ObstacleContactState.None;
        forceStopStage = false;

        preRageSpeedMult = stageSpeedMult;

        rageMode = true;
        rageStartTime = Time.time;
        rageEndTime = Time.time + seconds;
        rageReady = false;

        stageSpeedMult = defaultStageSpeedMult * rageSpeedFactor;

        var player = ResolvePlayer();
        if (player != null) player.ActivateRageMode(seconds);

        OnRageStart?.Invoke();
    }

    public float GetRageTimeLeft() => rageMode ? Mathf.Max(0f, rageEndTime - Time.time) : 0f;

    public bool IsMachineGunSequenceActive()
    {
        return machineGunTriggerPending || machineGunSequenceActive;
    }

    public bool TriggerMachineGun()
    {
        if (gameOver || rageMode || machineGunTriggerPending || machineGunSequenceActive)
            return false;

        if (OnMachineGunTrigger == null)
            return false;

        machineGunTriggerPending = true;
        protectNextPostMachineGunPhaseObstacles = true;
        OnMachineGunTrigger?.Invoke();
        return true;
    }

    public bool ConsumeNextPostMachineGunPhaseObstacleProtection()
    {
        if (!protectNextPostMachineGunPhaseObstacles)
            return false;

        protectNextPostMachineGunPhaseObstacles = false;
        return true;
    }

    public void NotifyMachineGunSequenceStarted()
    {
        machineGunTriggerPending = false;

        if (machineGunSequenceActive)
            return;

        machineGunSequenceActive = true;
        OnMachineGunSequenceStart?.Invoke();
    }

    public void NotifyMachineGunObstacleSpawnStop()
    {
        if (!machineGunSequenceActive)
            return;

        OnMachineGunObstacleSpawnStop?.Invoke();
    }

    public void NotifyMachineGunSequenceEnded()
    {
        bool wasActive = machineGunTriggerPending || machineGunSequenceActive;
        machineGunTriggerPending = false;
        machineGunSequenceActive = false;

        if (wasActive)
            OnMachineGunSequenceEnd?.Invoke();
    }

    private void ForceStopMachineGunSequence()
    {
        protectNextPostMachineGunPhaseObstacles = false;
        NotifyMachineGunSequenceEnded();
    }

    public float GetStageSpeedMult() => stageSpeedMult;
    public float GetStageSpeedMultIgnoringObstacleSlowdown()
    {
        if (obstacleState == ObstacleContactState.Decelerating ||
            obstacleState == ObstacleContactState.Pushing ||
            obstacleState == ObstacleContactState.Recovering)
        {
            return rageMode
                ? defaultStageSpeedMult * rageSpeedFactor
                : defaultStageSpeedMult;
        }

        return stageSpeedMult;
    }

    public void ConfigureSelectedPlayer(int playerType, int playerLevel)
    {
        playerType = Mathf.Clamp(playerType, 1, 5);
        playerLevel = Mathf.Max(1, playerLevel);
        selectedPlayerType = playerType;

        if (playerLevels == null || playerLevels.Length < 6)
            playerLevels = new int[6];

        playerLevels[playerType] = playerLevel;
    }

    public static void SetPendingSelectedPlayer(int playerType, int playerLevel)
    {
        pendingSelectedPlayerType = Mathf.Clamp(playerType, 1, 5);
        pendingSelectedPlayerLevel = Mathf.Max(1, playerLevel);
        hasPendingSelectedPlayer = true;

        if (Instance != null)
            Instance.ApplyPendingSelectedPlayer();
    }

    // ===== Obstacle contact control =====
    public void BeginObstacleContact()
    {
        if (rageMode) return;
        if (gameOver) return;

        obstacleContactCount++;
        lastObstacleTouchTime = Time.time;

        if (obstacleContactCount > 1)
            return;

        if (obstacleRoutine != null)
            StopCoroutine(obstacleRoutine);

        obstacleRoutine = StartCoroutine(ObstacleContactSequence());
    }

    public void EndObstacleContact()
    {
        if (rageMode) return;

        obstacleContactCount--;
        if (obstacleContactCount < 0)
            obstacleContactCount = 0;

        if (obstacleContactCount > 0)
            return;

        if (obstacleState == ObstacleContactState.Pushing)
            obstacleState = ObstacleContactState.None;
    }

    private IEnumerator ObstacleContactSequence()
    {
        float baseMult =
            rageMode
            ? defaultStageSpeedMult * rageSpeedFactor
            : defaultStageSpeedMult;

        obstacleState = ObstacleContactState.Decelerating;
        yield return TweenStageSpeedMultCoroutine(stageSpeedMult, 0.2f, decelerateTime);

        obstacleState = ObstacleContactState.Pushing;

        float pushRemain = 0.5f;
        float pushMult = pushSpeed / StageManager.Instance.phaseBaseSpeed;

        if (rageMode)
            pushMult *= rageSpeedFactor;

        stageSpeedMult = pushMult;

        while (pushRemain > 0f)
        {
            pushRemain -= Time.deltaTime;

            if (Time.time - lastObstacleTouchTime < 0.1f)
                pushRemain += 0.2f;

            yield return null;
        }

        obstacleState = ObstacleContactState.Recovering;
        yield return TweenStageSpeedMultCoroutine(pushMult, baseMult, recoverTime);

        obstacleState = ObstacleContactState.None;
    }

    // ------------------ RESET ------------------
    public void ResetGame()
    {
        Debug.Log("[GameData] ResetGame called");
        arcadeSceneActive = true;
        arcadeRewardsGranted = false;
        ForceStopRage();
        Hitbox.ClearBossTargetCache();
        ZigzagLightning.ClearBossTargetCache();

        if (StageManager.Instance != null)
            StageManager.Instance.ForceClearBossNow();

        ClearBossAndBombObjectsNow();

        // ✅ 보스 트리거 리셋
        bossStage3Triggered = false;
        bossStage4Triggered = false;
        debugForceBossStage3 = false;
        debugForceBossStage4 = false;

        if (obstacleRoutine != null)
        {
            StopCoroutine(obstacleRoutine);
            obstacleRoutine = null;
        }
        obstacleContactCount = 0;
        obstacleState = ObstacleContactState.None;
        forceStopStage = false;

        if (gameOverSpeedRoutine != null)
        {
            StopCoroutine(gameOverSpeedRoutine);
            gameOverSpeedRoutine = null;
        }
        if (gameOverUiRoutine != null)
        {
            StopCoroutine(gameOverUiRoutine);
            gameOverUiRoutine = null;
        }

        var launcher = ResolveBombLauncher();
        if (launcher != null)
        {
            launcher.ResetLauncherState();
        }

        var train = ResolveTrain();
        if (train != null)
            train.Reinit();

        stageSpeedMult = defaultStageSpeedMult;

        gameOver = false;
        rageMode = false;
        rageEndTime = -1f;
        rageReady = false;
        debugForceRage = false;
        debugForceMachineGunTrigger = false;
        ForceStopMachineGunSequence();

        if (speedTween != null) { StopCoroutine(speedTween); speedTween = null; }
        preRageSpeedMult = defaultStageSpeedMult;
        stageSpeedMult = defaultStageSpeedMult;

        o2Score = 0;
        survivalTime = 0f;
        totalKillCount = 0;

        MachineGunObstacle.ClearAllSpawnedObstacles();

        ResetUIAndObjects();

        var player = ResolvePlayer();
        if (player != null)
        {
            player.OnRageModeChanged(false);
            player.ResetPlayer();
        }

        if (StageManager.Instance != null)
            StartCoroutine(RestartStageLoopSafe());
    }

    public void PrepareForSceneTransition()
    {
        SuspendForNonArcadeScene();
    }

    private IEnumerator RestartStageLoopSafe()
    {
        if (restartRoutine != null) yield break;
        restartRoutine = StartCoroutine(_RestartStageLoopSafe());
        yield return restartRoutine;
        restartRoutine = null;
    }

    private IEnumerator _RestartStageLoopSafe()
    {
        if (StageManager.Instance != null)
        {
            StageManager.Instance.StopStageLoop();
            yield return new WaitUntil(() => StageManager.Instance.IsStageLoopStopped());

            StageManager.Instance.ClearAllPhases();
            ClearMiniBossEntities();
            MachineGunObstacle.ClearAllSpawnedObstacles();

            yield return new WaitForSeconds(0.1f);

            StageManager.Instance.StartStageLoop();
        }
    }

    private void ClearMiniBossEntities()
    {
        MiniBoss[] activeMiniBosses = FindObjectsByType<MiniBoss>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < activeMiniBosses.Length; i++)
        {
            if (activeMiniBosses[i] != null)
                Destroy(activeMiniBosses[i].gameObject);
        }

        MiniBossBomb[] activeMiniBossBombs = FindObjectsByType<MiniBossBomb>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < activeMiniBossBombs.Length; i++)
        {
            if (activeMiniBossBombs[i] != null)
                Destroy(activeMiniBossBombs[i].gameObject);
        }
    }

    private void ResetUIAndObjects()
    {
        var rageUI = ResolveRageUi();
        var gate = ResolveGateHealth();
        var scoreUI = ResolveScoreUi();

        gate?.ResetGate();
        rageUI?.ResetRageUI();

        if (ObjectPool.Instance != null)
        {
            ObjectPool.Instance.ReturnAllActive(
                POOL_MISSILE,
                POOL_WARNING,
                POOL_SMOKE,
                POOL_GATE_SMOKE,
                POOL_GATE_SMOKE_BACK,
                POOL_SPEED_EFFECT
            );
        }

        var missileSpawners = Object.FindObjectsByType<MissileSpawner>(FindObjectsSortMode.None);
        for (int i = 0; i < missileSpawners.Length; i++)
        {
            var s = missileSpawners[i];
            if (s && s.gameObject.scene.IsValid())
                s.ResetSpawner();
        }

        var stage2Spawners = Object.FindObjectsByType<stage2prefabSpawner>(FindObjectsSortMode.None);
        for (int i = 0; i < stage2Spawners.Length; i++)
        {
            var s = stage2Spawners[i];
            if (s && s.gameObject.scene.IsValid())
                s.ResetSpawner();
        }

        ReturnAllSceneObjectsOfType<Monster>();
        ReturnAllSceneObjectsOfType<speedEffect>();
        DestroyAllSceneObjectsOfType<Blood>();

        if (scoreUI != null)
        {
            scoreUI.scoreText.text = $"SCORE: {GetCleanScore()}";
            scoreUI.o2Text.text = $"O2: {GetO2Score()}";
        }
    }

    private void ClearBossAndBombObjectsNow()
    {
        if (ObjectPool.Instance != null)
        {
            ObjectPool.Instance.ReturnAllActive(
                "BossMissile",
                "BossRageMissile",
                "BossSlimeArm",
                "BossSlimeCanon",
                "BossSlimeJelly",
                "BossSlimeCanonBall",
                "BossSlimeDamaging",
                "BombHead",
                "Bomb",
                "BombHitBox",
                "MachineGunBullet"
            );
        }

        ReturnAllSceneObjectsOfType<BossMissile>();
        ReturnAllSceneObjectsOfType<BossRageMissile>();
        ReturnAllSceneObjectsOfType<BossSlimeArm>();
        ReturnAllSceneObjectsOfType<BossSlimeCanon>();
        ReturnAllSceneObjectsOfType<BossSlimeJelly>();
        ReturnAllSceneObjectsOfType<BossSlimeCanonBall>();
        ReturnAllSceneObjectsOfType<Bomb>();
        ReturnAllSceneObjectsOfType<BombHitBox>();
        ReturnAllSceneObjectsOfType<MachineGunBullet>();
    }

    private void ReturnAllSceneObjectsOfType<T>() where T : Component
    {
        var items = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item == null || !item.gameObject.scene.IsValid())
                continue;

            // Phase 내부 자식은 StageManager의 스냅샷/페이즈 풀로 복구해야 한다.
            // 여기서 Destroy해 버리면 pooled phase clone에서 해당 오브젝트가 영구 소실될 수 있다.
            if (item.GetComponentInParent<PhaseLayoutSnapshot>(true) != null)
                continue;

            if (ObjectPool.Instance != null && ObjectPool.Instance.TryReturnActive(item.gameObject))
                continue;

            Object.Destroy(item.gameObject);
        }
    }

    private void DestroyAllSceneObjectsOfType<T>() where T : Component
    {
        var all = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
        foreach (var c in all)
        {
            if (c && c.gameObject.scene.IsValid())
            {
                var rb = c.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }

                var mb = c as MonoBehaviour;
                if (mb != null) mb.StopAllCoroutines();

                Object.Destroy(c.gameObject);
            }
        }
    }

    public void TriggerGameOver()
    {
        if (gameOver) return;
        gameOver = true;
        AwardVillageArcadeRewardsOnce();
        OnGameOver?.Invoke();

        if (obstacleRoutine != null)
        {
            StopCoroutine(obstacleRoutine);
            obstacleRoutine = null;
        }
        obstacleContactCount = 0;
        obstacleState = ObstacleContactState.None;

        if (speedTween != null)
        {
            StopCoroutine(speedTween);
            speedTween = null;
        }

        if (gameOverSpeedRoutine != null)
            StopCoroutine(gameOverSpeedRoutine);

        gameOverSpeedRoutine = StartCoroutine(GameOverSlowStop());

        Debug.Log("💀 TriggerGameOver 실행됨");
        if (gameOverUiRoutine != null)
            StopCoroutine(gameOverUiRoutine);
        gameOverUiRoutine = StartCoroutine(CoShowGameOverUIAfterDelay());
    }

    private void AwardVillageArcadeRewardsOnce()
    {
        if (arcadeRewardsGranted)
            return;

        arcadeRewardsGranted = true;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null)
            return;

        villageManagement.ApplyArcadeResults(GetO2Score(), GetCleanScore());
    }

    private IEnumerator GameOverSlowStop()
    {
        float start = stageSpeedMult;
        float t = 0f;
        float duration = 1f;

        while (t < duration)
        {
            stageSpeedMult = Mathf.Lerp(start, 0f, t / duration);
            t += Time.deltaTime;
            yield return null;
        }

        stageSpeedMult = 0f;
    }

    private IEnumerator CoShowGameOverUIAfterDelay()
    {
        float delay = Mathf.Max(0f, gameOverUiDelay);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        var ui = ResolveGameOverUi();
        if (ui != null && gameOver)
            ui.Show();

        gameOverUiRoutine = null;
    }

    public void AddO2(int amount = 1) => o2Score = Mathf.Max(0, o2Score + amount);

    public void SpendO2(int amount = 1)
    {
        if (amount <= 0)
            return;

        o2Score = Mathf.Max(0, o2Score - amount);
    }

    public int GetCleanScore()
    {
        int timeScore = Mathf.FloorToInt(survivalTime / 2f);
        return timeScore + score;
    }

    public int GetO2Score() => o2Score;

    public Coroutine TweenStageSpeedMult(float targetMult, float duration)
    {
        if (speedTween != null) StopCoroutine(speedTween);
        speedTween = StartCoroutine(_TweenStageSpeedMult(targetMult, duration));
        return speedTween;
    }

    private IEnumerator _TweenStageSpeedMult(float targetMult, float duration)
    {
        float start = stageSpeedMult;
        float t = 0f;
        duration = Mathf.Max(0.0001f, duration);

        while (t < duration)
        {
            float k = t / duration;
            stageSpeedMult = Mathf.Lerp(start, targetMult, k);
            t += Time.deltaTime;
            yield return null;
        }
        stageSpeedMult = targetMult;
        speedTween = null;
    }

    private IEnumerator TweenStageSpeedMultCoroutine(float from, float to, float duration)
    {
        if (forceStopStage) yield break;

        float t = 0f;
        duration = Mathf.Max(0.0001f, duration);

        while (t < duration)
        {
            stageSpeedMult = Mathf.Lerp(from, to, t / duration);
            t += Time.deltaTime;
            yield return null;
        }
        stageSpeedMult = to;
    }

    private void ForceStopRage()
    {
        Debug.Log($"[GameData] ForceStopRage called rageMode={rageMode}");
        rageMode = false;
        rageEndTime = -1f;
        rageReady = false;
        debugForceRage = false;
        debugForceMachineGunTrigger = false;
        ForceStopMachineGunSequence();

        if (speedTween != null)
        {
            StopCoroutine(speedTween);
            speedTween = null;
        }

        stageSpeedMult = defaultStageSpeedMult;

        MachineGunObstacle.ClearAllSpawnedObstacles();

        OnRageEnd?.Invoke();
    }

    private Player ResolvePlayer()
    {
        if (playerRef == null)
            playerRef = Object.FindFirstObjectByType<Player>();

        return playerRef;
    }

    private BombLauncher ResolveBombLauncher()
    {
        if (bombLauncherRef == null)
            bombLauncherRef = Object.FindFirstObjectByType<BombLauncher>();

        return bombLauncherRef;
    }

    private Train ResolveTrain()
    {
        if (trainRef == null)
            trainRef = Object.FindFirstObjectByType<Train>();

        return trainRef;
    }

    private RageUIController ResolveRageUi()
    {
        if (rageUiRef == null)
            rageUiRef = Object.FindFirstObjectByType<RageUIController>();

        return rageUiRef;
    }

    private GateHealth ResolveGateHealth()
    {
        if (gateHealthRef == null)
            gateHealthRef = Object.FindFirstObjectByType<GateHealth>();

        return gateHealthRef;
    }

    private ScoreUI ResolveScoreUi()
    {
        if (scoreUiRef == null)
            scoreUiRef = Object.FindFirstObjectByType<ScoreUI>();

        return scoreUiRef;
    }

    private GameOverUI ResolveGameOverUi()
    {
        if (gameOverUiRef == null)
            gameOverUiRef = Object.FindFirstObjectByType<GameOverUI>();

        return gameOverUiRef;
    }

    private void SuspendForNonArcadeScene()
    {
        arcadeSceneActive = false;
        gameOver = true;
        ForceStopMachineGunSequence();
        StopRuntimeCoroutines();
        ClearSceneReferences();
    }

    private void ApplyPendingSelectedPlayer()
    {
        if (!hasPendingSelectedPlayer)
            return;

        ConfigureSelectedPlayer(pendingSelectedPlayerType, pendingSelectedPlayerLevel);
        hasPendingSelectedPlayer = false;
    }

    private void StopRuntimeCoroutines()
    {
        if (restartRoutine != null)
        {
            StopCoroutine(restartRoutine);
            restartRoutine = null;
        }

        if (speedTween != null)
        {
            StopCoroutine(speedTween);
            speedTween = null;
        }

        if (obstacleRoutine != null)
        {
            StopCoroutine(obstacleRoutine);
            obstacleRoutine = null;
        }

        if (gameOverSpeedRoutine != null)
        {
            StopCoroutine(gameOverSpeedRoutine);
            gameOverSpeedRoutine = null;
        }

        if (gameOverUiRoutine != null)
        {
            StopCoroutine(gameOverUiRoutine);
            gameOverUiRoutine = null;
        }
    }

    private void ClearSceneReferences()
    {
        playerRef = null;
        bombLauncherRef = null;
        trainRef = null;
        rageUiRef = null;
        gateHealthRef = null;
        scoreUiRef = null;
        gameOverUiRef = null;
    }

    private static bool IsArcadeScene(Scene scene)
    {
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
            return false;

        return scene.name.ToLowerInvariant().StartsWith(ArcadeScenePrefix);
    }
}
