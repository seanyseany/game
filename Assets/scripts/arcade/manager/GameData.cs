using UnityEngine;
using System.Collections;

public class GameData : MonoBehaviour
{
    public static GameData Instance;

    [Header("Mana Random Spawn")]
    public bool manaRandomEnabled = true;
    [Range(0f, 1f)] public float manaOffChanceStage1 = 0.10f;
    [Range(0f, 1f)] public float manaOffChanceStage2 = 0.30f;
    [Range(0f, 1f)] public float manaOffChanceStage3 = 0.50f;
    [Range(0f, 1f)] public float manaOffChanceStage4 = 0.80f;

    [Header("Holy Mode")]
    public bool holyMode = true;

    [Tooltip("holyMana 활성 여부")]
    public bool holyRandomEnabled = true;

    [Range(0f, 1f)] public float holyOffChanceStage1 = 0.20f;
    [Range(0f, 1f)] public float holyOffChanceStage2 = 0.40f;
    [Range(0f, 1f)] public float holyOffChanceStage3 = 0.60f;
    [Range(0f, 1f)] public float holyOffChanceStage4 = 0.80f;

    [Header("Holy Prefabs")]
    public GameObject mosesPrefab;
    public GameObject holyLightPrefab;

    [Header("Holy Positions")]
    public Vector3 mosesStartPos = new Vector3(-6f, 1.4f, 0f);
    public Vector3 mosesTargetPos = new Vector3(-3f, 1.4f, 0f);
    public float mosesMoveTime = 1f;

    [Header("Holy Duration")]
    public float holyLightDuration = 10f;

    public bool holyActive = false;
    private Coroutine holyRoutine;

    public int currentHolyManaNumber = 1;
    public bool currentHolyManaDisabled = false;

    private MosesController activeMoses;
    private GameObject activeHolyLight;

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
    public static System.Action OnGameOver;

    public bool gameOver = false;
    private Coroutine restartRoutine;
    private Coroutine speedTween;
    private float lastObstacleTouchTime = -999f;
    public static System.Action OnHolyStart;
    public static System.Action OnHolyEnd;

    private const string POOL_MISSILE = "missile";
    private const string POOL_WARNING = "Warning";
    private const string POOL_SMOKE = "Smoke";
    private const string POOL_GATE_SMOKE = "GateSmoke";
    private const string POOL_GATE_SMOKE_BACK = "GateSmokeBack";
    private const string POOL_SPEED_EFFECT = "SpeedEffect";
    public Vector3 holyLightPos = new Vector3(-4.729428f, 2.01729f, 0f);

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

    [Header("Game Over")]
    public float gameOverUiDelay = 1.5f;

    public int currentManaNumber = 1;
    public bool currentManaDisabled = false;

    // ===================== BOSS TRIGGER =====================
    [Header("Boss Trigger (Debug)")]
    public bool debugForceBossStage3 = false;
    public bool debugForceBossStage4 = false;

    private bool bossStage3Triggered = false;
    private bool bossStage4Triggered = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameOver = false;
    }

    void Start() => ResetGame();

    void Update()
    {
        if (gameOver) return;

        survivalTime += Time.deltaTime;

        if (debugForceRage)
        {
            debugForceRage = false;
            ActivateRageMode(rageDuration);
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

    // ✅ StageManager가 SpawnPhase 직전에 호출하는 훅
    public void CheckBossTriggerBeforeSpeedUp(StageManager sm, int currentPhaseSpawnCount)
    {
        if (gameOver) return;
        if (sm == null) return;

        // “speedUp3/4 시작 직전” = (speedUp3-1), (speedUp4-1)
        // StageManager.phaseSpawnCount는 SpawnPhase() 끝에서 증가하므로,
        // 여기서는 "현재 값" 기준으로 비교하면 된다.
        if (!bossStage3Triggered && currentPhaseSpawnCount == sm.speedUp3 - 1)
        {
            bossStage3Triggered = true;
            sm.TriggerBossEncounter(3);
        }

        if (!bossStage4Triggered && currentPhaseSpawnCount == sm.speedUp4 - 1)
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

        var player = playerRef != null ? playerRef : Object.FindFirstObjectByType<Player>();
        if (player != null) player.ActivateRageMode(seconds);

        OnRageStart?.Invoke();
    }

    public float GetRageTimeLeft() => rageMode ? Mathf.Max(0f, rageEndTime - Time.time) : 0f;
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

    // ===================== HOLY API =====================
    public void CollectHolyMana()
    {
        if (!holyMode) return;
        if (holyActive) return;
        if (gameOver) return;
        if (rageMode) return;

        if (holyRoutine != null) StopCoroutine(holyRoutine);
        holyRoutine = StartCoroutine(HolySequenceCoroutine());
    }

    private IEnumerator HolySequenceCoroutine()
    {
        holyActive = true;

        if (mosesPrefab != null)
        {
            var go = Instantiate(mosesPrefab, mosesStartPos, Quaternion.identity);
            activeMoses = go.GetComponent<MosesController>();

            if (activeMoses != null)
            {
                activeMoses.SnapTo(mosesStartPos);
                activeMoses.SetOrigin(mosesStartPos);

                activeMoses.MoveTo(mosesTargetPos, mosesMoveTime, () =>
                {
                    activeMoses.PlayAppear();
                });
            }
        }
        yield return new WaitForSeconds(2f);

        OnHolyStart?.Invoke();
        yield return new WaitForSeconds(mosesMoveTime);

        if (holyLightPrefab != null)
            activeHolyLight = Instantiate(holyLightPrefab, holyLightPos, Quaternion.identity);

        yield return new WaitForSeconds(holyLightDuration);

        if (activeHolyLight != null)
        {
            Destroy(activeHolyLight);
            activeHolyLight = null;
        }

        holyActive = false;
        OnHolyEnd?.Invoke();

        if (activeMoses != null)
        {
            bool done = false;

            activeMoses.ReturnToOrigin(mosesMoveTime, () => { done = true; });
            yield return new WaitUntil(() => done);

            activeMoses.PlayVanish();
            Destroy(activeMoses.gameObject, 0.3f);
            activeMoses = null;
        }

        holyRoutine = null;
    }

    public void RollHolyManaForThisPhase(int speedStage)
    {
        if (!holyMode)
        {
            currentHolyManaDisabled = true;
            currentHolyManaNumber = 1;
            return;
        }

        if (!holyRandomEnabled)
        {
            currentHolyManaDisabled = false;
            currentHolyManaNumber = 1;
            return;
        }

        float offChance = 0f;
        switch (speedStage)
        {
            case 4: offChance = holyOffChanceStage4; break;
            case 3: offChance = holyOffChanceStage3; break;
            case 2: offChance = holyOffChanceStage2; break;
            default: offChance = holyOffChanceStage1; break;
        }

        currentHolyManaDisabled = Random.value < offChance;
        currentHolyManaNumber = 1;
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
        ForceStopRage();

        if (StageManager.Instance != null)
            StageManager.Instance.ForceClearBossNow();

        ClearBossAndBombObjectsNow();

        // ✅ 보스 트리거 리셋
        bossStage3Triggered = false;
        bossStage4Triggered = false;
        debugForceBossStage3 = false;
        debugForceBossStage4 = false;

        if (holyRoutine != null)
        {
            StopCoroutine(holyRoutine);
            holyRoutine = null;
        }
        holyActive = false;

        if (activeHolyLight != null)
        {
            Destroy(activeHolyLight);
            activeHolyLight = null;
        }
        if (activeMoses != null)
        {
            Destroy(activeMoses.gameObject);
            activeMoses = null;
        }

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

        var launcher = Object.FindFirstObjectByType<BombLauncher>();
        if (launcher != null)
        {
            launcher.ResetLauncherState();
        }

        stageSpeedMult = defaultStageSpeedMult;

        gameOver = false;
        rageMode = false;
        rageEndTime = -1f;
        rageReady = false;
        debugForceRage = false;

        if (speedTween != null) { StopCoroutine(speedTween); speedTween = null; }
        preRageSpeedMult = defaultStageSpeedMult;
        stageSpeedMult = defaultStageSpeedMult;

        o2Score = 0;
        survivalTime = 0f;
        totalKillCount = 0;

        if (StageManager.Instance != null)
        {
            StageManager.Instance.StopRageObstacleSpawn();
            StageManager.Instance.ClearAllRageObstacles();
        }

        ResetUIAndObjects();

        var player = playerRef != null ? playerRef : Object.FindFirstObjectByType<Player>();
        if (player != null)
        {
            player.OnRageModeChanged(false);
            player.ResetPlayer();
        }

        if (StageManager.Instance != null)
            StartCoroutine(RestartStageLoopSafe());
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
            StageManager.Instance.ClearAllRageObstacles();

            yield return new WaitForSeconds(0.1f);

            StageManager.Instance.StartStageLoop();
        }
    }

    private void ResetUIAndObjects()
    {
        var rageUI = Object.FindFirstObjectByType<RageUIController>();
        var gate = Object.FindFirstObjectByType<GateHealth>();
        var scoreUI = Object.FindFirstObjectByType<ScoreUI>();

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

        foreach (var s in Object.FindObjectsOfType<MissileSpawner>())
            if (s && s.gameObject.scene.IsValid()) s.ResetSpawner();

        foreach (var s in Object.FindObjectsOfType<stage2prefabSpawner>())
            if (s && s.gameObject.scene.IsValid()) s.ResetSpawner();

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
                "BombHitBox"
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
    }

    private void ReturnAllSceneObjectsOfType<T>() where T : Component
    {
        var items = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item == null || !item.gameObject.scene.IsValid())
                continue;

            if (ObjectPool.Instance != null && ObjectPool.Instance.TryReturnActive(item.gameObject))
                continue;

            Object.Destroy(item.gameObject);
        }
    }

    private void DestroyAllSceneObjectsOfType<T>() where T : Component
    {
        var all = Resources.FindObjectsOfTypeAll<T>();
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
        OnGameOver?.Invoke();

        if (holyRoutine != null)
        {
            StopCoroutine(holyRoutine);
            holyRoutine = null;
        }
        holyActive = false;

        if (activeHolyLight != null)
        {
            Destroy(activeHolyLight);
            activeHolyLight = null;
        }
        if (activeMoses != null)
        {
            Destroy(activeMoses.gameObject);
            activeMoses = null;
        }

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

        var ui = Object.FindFirstObjectByType<GameOverUI>();
        if (ui != null && gameOver)
            ui.Show();

        gameOverUiRoutine = null;
    }


    public void AddO2(int amount = 1) => o2Score += amount;

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

    public void RollManaForThisPhase(int speedStage)
    {
        if (!manaRandomEnabled)
        {
            currentManaDisabled = false;
            currentManaNumber = 1;
            return;
        }

        float offChance = 0f;
        switch (speedStage)
        {
            case 4: offChance = manaOffChanceStage4; break;
            case 3: offChance = manaOffChanceStage3; break;
            case 2: offChance = manaOffChanceStage2; break;
            default: offChance = manaOffChanceStage1; break;
        }

        currentManaDisabled = Random.value < offChance;
        currentManaNumber = (Random.value < 0.5f) ? 1 : 2;
    }

    private void ForceStopRage()
    {
        rageMode = false;
        rageEndTime = -1f;
        rageReady = false;
        debugForceRage = false;

        if (speedTween != null)
        {
            StopCoroutine(speedTween);
            speedTween = null;
        }

        stageSpeedMult = defaultStageSpeedMult;

        if (StageManager.Instance != null)
        {
            StageManager.Instance.StopRageObstacleSpawn();
            StageManager.Instance.ClearAllRageObstacles();
        }

        OnRageEnd?.Invoke();
    }
}
