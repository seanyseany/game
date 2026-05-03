using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class BossSlime : MonoBehaviour, IReinitializable
{
    public enum State { Inactive, Entering, Active, TimeoutAttacking, Dead }
    public State state = State.Inactive;

    [Header("HP / Timer")]
    public int maxHp = 80;
    public float lifeTimeSeconds = 120f;

    [Header("Boss UI (World Space)")]
    public Slider hpSlider;
    public Slider timerSlider;

    [Header("Entry")]
    public Vector3 entryFxWorldPos = new Vector3(12f, 1.4f, 0f);
    public float entryFxDuration = 2.5f;
    public float entranceStartDelay = 2f;
    public float bossAppearDelayAfterEntrance = 2f;
    public Vector3 enterFromWorldPos = new Vector3(12f, 1.4f, 0f);
    public bool forceEnterFromWorldPos = false;
    public string entryFxPoolTag = "BossSlimeEntry";
    public GameObject entryFxPrefab;

    [Header("Movement")]
    public Vector3 startWorldPos = new Vector3(-2.5f, 1.2f, 0f);
    public float moveToStartTime = 1f;
    public Vector2 roamMinWorld = new Vector2(-4f, -2f);
    public Vector2 roamMaxWorld = new Vector2(0f, 2f);
    public bool roamBoundsAreOffsetsFromStart = true;
    public float roamSpeed = 2f;
    public float roamPickInterval = 0.7f;

    [Header("Arm Attack")]
    public string armPoolTag = "BossSlimeArm";
    public GameObject armPrefab;
    public Vector3 armStartWorldPos = new Vector3(-0.6f, -2f, -1f);
    public Vector3 armEndWorldPos = new Vector3(-0.6f, -9f, -1f);
    public Vector3[] jellySpawnWorldPoints = new Vector3[] { new Vector3(3.136576f, -2.2f, 0f), new Vector3(4.53618f, -2f, 0f) };
    public int armRepeatCount = 5;
    public float armRepeatGap = 0.15f;

    [Header("Canon")]
    public string canonPoolTag = "BossSlimeCanon";
    public GameObject canonPrefab;
    public Vector3 canonLocalOffset = new Vector3(0.4f, 0f, 0f);
    public float canonIdleZ = -90f;
    public float canonAttackEnterDeltaZ = 90f;
    public float canonAttackEnterDelay = 0.1f;
    public float canonAttackExitDelay = 1f;
    public float[] canonAimAngles = new float[] { 165f, 180f, 200f, 220f };
    public float canonShotInterval = 0.15f;
    public int canonShotsPerSelection = 5;
    public int canonSelectionsPerCycle = 2;
    public float canonReAimDelay = 1f;
    public int canonCycleCount = 5;

    [Header("Monster Spawn")]
    public GameObject monsterPrefab;
    public string monsterPoolTag = "BossSlimeMonster";
    public int monsterPoolSize = 16;
    public Vector3 monsterSpawnWorldPos = new Vector3(16f, -3f, 0f);
    public int monsterSpawnCount = 13;
    public float monsterSpawnInterval = 0.3f;

    [Header("Loop Delay")]
    public float loopDelayAfterEnemy = 5f;

    [Header("Destroy / Timeout")]
    public string slimeDestroyPoolTag = "BossSlimeDestroy";
    public GameObject slimeDestroyPrefab;
    public float deathShakeDuration = 1f;
    public float deathShakeCycle = 0.08f;
    public float timeoutCanonMoveDuration = 1f;
    public float timeoutCanonFireDelay = 0.1f;
    public Vector3[] timeoutExtraCanonStartLocalOffsets = new Vector3[3]
    {
        new Vector3(5.5f, 2.5f, 0f),
        new Vector3(5.5f, 0f, 0f),
        new Vector3(5.5f, -2.5f, 0f)
    };
    public Vector3[] timeoutExtraCanonEndLocalOffsets = new Vector3[3]
    {
        new Vector3(2.8f, 1.5f, 0f),
        new Vector3(2.8f, 0f, 0f),
        new Vector3(2.8f, -1.5f, 0f)
    };

    [Header("Damage FX (LOCAL)")]
    public string slimeDamagingPoolTag = "BossSlimeDamaging";
    public GameObject slimeDamagingPrefab;
    public Vector3 slimeDamagingLocalOffset = Vector3.zero;
    public float slimeDamagingFxLifeTime = 0.6f;

    [Header("HP Animator Triggers")]
    public string hp60TriggerName = "60";
    public string hp30TriggerName = "30";

    [Header("Pool")]
    public bool usePool = true;
    public Animator slimeAnimator;
    
    [Header("Hit Flash Whole Prefab")]
    public Color hitColor = Color.red;
    public float hitFlashDuration = 0.15f;

    [Header("Hit Shake")]
    public Transform visualRoot;
    public float shakeLeft = 0.08f;
    public float shakeRight = 0.12f;
    public float shakeTotalDuration = 0.12f;

    private int hp;
    private float spawnTime;
    private Coroutine mainRoutine;
    private Coroutine roamRoutine;
    private Coroutine timeoutAttackRoutine;

    private Vector3 roamTarget;
    private float nextRoamPickTime;

    private GameObject activeCanon;
    private readonly List<GameObject> timeoutExtraCanons = new List<GameObject>();
    private Renderer[] cachedRenderers;
    private Collider2D[] cachedColliders;
    private SpriteRenderer[] cachedSpriteRenderers;
    private Color[] originalSpriteColors;
    private Coroutine flashRoutine;
    private Coroutine shakeRoutine;
    private Coroutine deathRoutine;
    private Vector3 visualRootBaseLocalPos;
    private readonly List<BossSlimeArm> activeArms = new List<BossSlimeArm>();
    private float flashUntilTime = -1f;

    private bool movedAt60;
    private bool movedAt30;
    private bool firedHp60Trigger;
    private bool firedHp30Trigger;
    private float pendingDamage = 0f;
    private Vector3 initialWorldPos;
    private Quaternion initialWorldRot;
    private Vector3 initialLocalScale;
    private Transform initialParent;
    private bool poolsEnsured;
    private bool hasBegunEncounter;

    public void Reinit() => ResetBossRuntime();

    void Awake()
    {
        if (slimeAnimator == null)
            slimeAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (slimeAnimator != null)
            slimeAnimator.applyRootMotion = false;

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedColliders = GetComponentsInChildren<Collider2D>(true);
        CacheRendererColors();

        if (visualRoot == null)
            visualRoot = transform;
        visualRootBaseLocalPos = visualRoot.localPosition;
        initialParent = transform.parent;
        initialWorldPos = transform.position;
        initialWorldRot = transform.rotation;
        initialLocalScale = transform.localScale;

        EnsureBossPoolsReady();
    }

    public void Begin()
    {
        EnsureBossPoolsReady();
        hasBegunEncounter = true;
        ResetBossRuntime();
        spawnTime = Time.time;
        SetBossUIVisible(true);
        UpdateBossUI();
        mainRoutine = StartCoroutine(CoMain());
    }

    public void ResetBossRuntime()
    {
        state = State.Inactive;
        if (initialParent != null && transform.parent != initialParent)
            transform.SetParent(initialParent, true);
        transform.SetPositionAndRotation(initialWorldPos, initialWorldRot);
        transform.localScale = initialLocalScale;

        hp = maxHp;
        spawnTime = Time.time;
        movedAt60 = false;
        movedAt30 = false;
        firedHp60Trigger = false;
        firedHp30Trigger = false;
        pendingDamage = 0f;

        if (mainRoutine != null)
        {
            StopCoroutine(mainRoutine);
            mainRoutine = null;
        }

        if (roamRoutine != null)
        {
            StopCoroutine(roamRoutine);
            roamRoutine = null;
        }
        if (timeoutAttackRoutine != null)
        {
            StopCoroutine(timeoutAttackRoutine);
            timeoutAttackRoutine = null;
        }
        
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }
        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }
        if (activeCanon != null)
            SafeReturnOrDestroy(activeCanon, canonPoolTag);
        activeCanon = null;
        ClearTimeoutExtraCanonsNow();

        ClearActiveArmsNow();
        ClearSpawnedSubProjectilesNow();
        ResetHpAnimationState();

        if (slimeAnimator != null)
            slimeAnimator.applyRootMotion = false;

        RestoreRendererColors();
        flashUntilTime = -1f;
        if (visualRoot == null) visualRoot = transform;
        visualRoot.localPosition = visualRootBaseLocalPos;

        SetBossUIVisible(true);
        UpdateBossUI();
    }

    void OnEnable()
    {
        GameData.OnGameOver += HandleGameOver;
        EnsureBossPoolsReady();
        ResetBossRuntime();
    }

    void OnDisable()
    {
        GameData.OnGameOver -= HandleGameOver;
    }

    void Update()
    {
        if (state != State.Active) return;

        UpdateBossUI();

        if (Time.time - spawnTime >= lifeTimeSeconds)
        {
            if (mainRoutine != null)
            {
                StopCoroutine(mainRoutine);
                mainRoutine = null;
            }
            if (timeoutAttackRoutine == null)
                timeoutAttackRoutine = StartCoroutine(CoTimeoutAttackLoop());
        }
    }

    private IEnumerator CoMain()
    {
        state = State.Entering;
        SetBossVisualActive(false);
        SetBossUIVisible(false);

        if (entranceStartDelay > 0f)
            yield return new WaitForSeconds(entranceStartDelay);

        GameObject entryFx = Spawn(entryFxPoolTag, entryFxPrefab, entryFxWorldPos, Quaternion.identity);
        if (entryFxDuration > 0f)
            yield return new WaitForSeconds(entryFxDuration);
        SafeReturnOrDestroy(entryFx, entryFxPoolTag);

        if (bossAppearDelayAfterEntrance > 0f)
            yield return new WaitForSeconds(bossAppearDelayAfterEntrance);

        // 기본은 씬 배치 위치에서 시작하고, 필요할 때만 강제 시작 위치 사용
        if (forceEnterFromWorldPos)
            transform.position = enterFromWorldPos;

        SetBossVisualActive(true);
        yield return MoveToOverTime(startWorldPos, moveToStartTime);

        EnsureCanon();

        state = State.Active;
        SetBossUIVisible(true);
        spawnTime = Time.time;
        PickNewRoamTarget();
        nextRoamPickTime = Time.time + roamPickInterval;

        if (roamRoutine != null)
            StopCoroutine(roamRoutine);
        roamRoutine = StartCoroutine(CoRoamLoop());

        // 첫 등장 직후에만 Arm 공격 시작을 1초 지연
        yield return new WaitForSeconds(1f);

        while (state == State.Active)
        {
            yield return CoArmAndJellyPhase();
            if (state != State.Active) break;

            yield return CoCanonPhase();
            if (state != State.Active) break;

            yield return CoSpawnEnemyPhase();
            if (state != State.Active) break;

            if (loopDelayAfterEnemy > 0f)
                yield return new WaitForSeconds(loopDelayAfterEnemy);
        }
    }

    private IEnumerator CoRoamLoop()
    {
        while (state == State.Active)
        {
            if (Time.time >= nextRoamPickTime)
            {
                PickNewRoamTarget();
                nextRoamPickTime = Time.time + Mathf.Max(0.05f, roamPickInterval);
            }

            transform.position = Vector3.MoveTowards(transform.position, roamTarget, roamSpeed * Time.deltaTime);
            ClampToRoamBounds();
            yield return null;
        }
    }

    private void PickNewRoamTarget()
    {
        GetRoamBounds(out float minX, out float maxX, out float minY, out float maxY);

        float x = Random.Range(minX, maxX);
        float y = Random.Range(minY, maxY);
        roamTarget = new Vector3(x, y, transform.position.z);
    }

    private void ClampToRoamBounds()
    {
        GetRoamBounds(out float minX, out float maxX, out float minY, out float maxY);

        Vector3 p = transform.position;
        p.x = Mathf.Clamp(p.x, minX, maxX);
        p.y = Mathf.Clamp(p.y, minY, maxY);
        transform.position = p;
    }

    private void GetRoamBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        float rawMinX = Mathf.Min(roamMinWorld.x, roamMaxWorld.x);
        float rawMaxX = Mathf.Max(roamMinWorld.x, roamMaxWorld.x);
        float rawMinY = Mathf.Min(roamMinWorld.y, roamMaxWorld.y);
        float rawMaxY = Mathf.Max(roamMinWorld.y, roamMaxWorld.y);

        if (roamBoundsAreOffsetsFromStart)
        {
            minX = startWorldPos.x + rawMinX;
            maxX = startWorldPos.x + rawMaxX;
            minY = startWorldPos.y + rawMinY;
            maxY = startWorldPos.y + rawMaxY;
        }
        else
        {
            minX = rawMinX;
            maxX = rawMaxX;
            minY = rawMinY;
            maxY = rawMaxY;
        }
    }

    private IEnumerator CoArmAndJellyPhase()
    {
        int repeat = Mathf.Max(1, armRepeatCount);
        for (int i = 0; i < repeat && state == State.Active; i++)
        {
            float armCycleDuration = 1.35f; // fallback
            GameObject armObj = Spawn(armPoolTag, armPrefab, armStartWorldPos, Quaternion.identity);
            if (armObj != null)
            {
                armObj.transform.SetParent(null, true);
                armObj.transform.position = armStartWorldPos;
                armObj.transform.rotation = Quaternion.identity;

                var arm = armObj.GetComponent<BossSlimeArm>();
                if (arm != null)
                {
                    arm.usePool = usePool;
                    arm.armPoolTag = armPoolTag;
                    arm.armStartWorldPos = armStartWorldPos;
                    arm.armEndWorldPos = armEndWorldPos;
                    arm.jellySpawnWorldPoints = jellySpawnWorldPoints;
                    arm.transform.position = armStartWorldPos;
                    RegisterActiveArm(arm);
                    arm.PlayOnce(this);
                    armCycleDuration = Mathf.Max(0.01f, arm.moveTime) + Mathf.Max(0f, arm.jellySpawnDelay) + Mathf.Max(0f, arm.stayAfterJellySpawn);
                }
                else
                {
                    SafeReturnOrDestroy(armObj, armPoolTag);
                }
            }

            float cycleWait = armCycleDuration + Mathf.Max(0f, armRepeatGap);
            if (cycleWait > 0f)
                yield return new WaitForSeconds(cycleWait);
        }
    }

    private IEnumerator CoCanonPhase()
    {
        EnsureCanon();

        var canon = activeCanon != null ? activeCanon.GetComponent<BossSlimeCanon>() : null;
        if (canon == null)
            yield break;

        // 기본 대기 각도
        canon.SetZRotation(canonIdleZ);

        // 공격 시퀀스 진입: +90 회전
        float enterTargetZ = canonIdleZ + canonAttackEnterDeltaZ;
        yield return CoRotateCanonToZ(canon, enterTargetZ, canonAttackEnterDelay);

        // 시퀀스:
        // [선택(딜레이) -> 간격 발사]를 selection 횟수만큼 수행하고, cycle 횟수만큼 반복
        int cycles = 5;
        int shotCount = 5;
        float shotInterval = 0.15f;
        float selectDelay = GetCanonAimRotateDuration();

        for (int cycle = 0; cycle < cycles && state == State.Active; cycle++)
        {
            // 캐논볼 인덱스와 에임 앵글 인덱스를 동일하게 사용
            int angleIndex = PickCanonAngleIndex();
            float aimAngle = GetArrayValue(canonAimAngles, angleIndex, 180f);
            yield return CoRotateCanonToZ(canon, aimAngle, selectDelay);

            for (int shot = 0; shot < shotCount && state == State.Active; shot++)
            {
                canon.PlayFireAnimation();
                canon.SpawnCanonBall(aimAngle, angleIndex);

                if (shot < shotCount - 1 && state == State.Active)
                    yield return new WaitForSeconds(shotInterval);
            }
        }

        // 루프 종료 후 기본 각도(-90)로 복귀 (1초 부드럽게 회전)
        if (state == State.Active)
            yield return CoRotateCanonToZ(canon, canonIdleZ, canonAttackExitDelay);
    }

    private IEnumerator CoRotateCanonToZ(BossSlimeCanon canon, float targetZ, float duration)
    {
        if (canon == null) yield break;

        float d = Mathf.Max(0f, duration);
        if (d <= 0.0001f)
        {
            canon.SetZRotation(targetZ);
            yield break;
        }

        float startZ = canon.transform.localEulerAngles.z;
        float elapsed = 0f;

        while (elapsed < d && state == State.Active)
        {
            float t = elapsed / d;
            float z = Mathf.LerpAngle(startZ, targetZ, t);
            canon.SetZRotation(z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (state == State.Active)
            canon.SetZRotation(targetZ);
    }

    private int PickCanonAngleIndex()
    {
        int nA = (canonAimAngles != null) ? canonAimAngles.Length : 0;
        int n = nA;
        if (n <= 0) return 0;
        return Random.Range(0, n);
    }

    private IEnumerator CoSpawnEnemyPhase()
    {
        int count = Mathf.Max(0, monsterSpawnCount);
        float interval = Mathf.Max(0.01f, monsterSpawnInterval);

        for (int i = 0; i < count && state == State.Active; i++)
        {
            GameObject monsterObj = SpawnMonster(monsterSpawnWorldPos, Quaternion.identity);
            if (monsterObj != null)
                ConfigureSpawnedMonster(monsterObj);
            yield return new WaitForSeconds(interval);
        }
    }

    private void EnsureBossPoolsReady()
    {
        if (poolsEnsured || !Application.isPlaying)
            return;

        var pool = ObjectPool.Instance;
        if (pool == null)
            return;

        EnsurePool(pool, entryFxPoolTag, entryFxPrefab, 1);
        EnsurePool(pool, armPoolTag, armPrefab, Mathf.Max(armRepeatCount, 6));
        EnsurePool(pool, canonPoolTag, canonPrefab, 4);
        EnsurePool(pool, slimeDestroyPoolTag, slimeDestroyPrefab, 1);
        EnsurePool(pool, slimeDamagingPoolTag, slimeDamagingPrefab, 3);
        EnsurePool(pool, monsterPoolTag, monsterPrefab, Mathf.Max(monsterPoolSize, monsterSpawnCount));

        var arm = armPrefab != null ? armPrefab.GetComponent<BossSlimeArm>() : null;
        if (arm != null)
            EnsurePool(pool, arm.jellyPoolTag, arm.jellyPrefab, Mathf.Max(armRepeatCount * 2, 8));

        var jelly = arm != null && arm.jellyPrefab != null ? arm.jellyPrefab.GetComponent<BossSlimeJelly>() : null;
        if (jelly != null)
            EnsurePool(pool, jelly.destroyPoolTag, jelly.destroyFxPrefab, 8);

        var canon = canonPrefab != null ? canonPrefab.GetComponent<BossSlimeCanon>() : null;
        if (canon != null)
            EnsurePool(pool, canon.canonBallPoolTag, canon.canonBallPrefab, Mathf.Max(canonCycleCount * canonShotsPerSelection, 28));

        var canonBall = canon != null && canon.canonBallPrefab != null ? canon.canonBallPrefab.GetComponent<BossSlimeCanonBall>() : null;
        if (canonBall != null)
            EnsurePool(pool, canonBall.destroyPoolTag, canonBall.destroyFxPrefab, 10);

        poolsEnsured = true;
    }

    private static void EnsurePool(ObjectPool pool, string tag, GameObject prefab, int minSize)
    {
        if (pool == null || string.IsNullOrEmpty(tag) || prefab == null)
            return;

        pool.EnsurePoolSize(tag, prefab, Mathf.Max(1, minSize));
    }

    private GameObject SpawnMonster(Vector3 pos, Quaternion rot)
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(monsterPoolTag) && ObjectPool.Instance.HasPool(monsterPoolTag))
            return ObjectPool.Instance.SpawnFromPool(monsterPoolTag, pos, rot);

        if (monsterPrefab != null)
            return Instantiate(monsterPrefab, pos, rot);

        return null;
    }

    private void ConfigureSpawnedMonster(GameObject monsterObj)
    {
        if (monsterObj == null)
            return;

        var monster = monsterObj.GetComponent<Monster>();
        if (monster == null)
            return;

        monster.ConfigurePooling(usePool, monsterPoolTag);
    }

    private void EnsureCanon()
    {
        if (activeCanon != null) return;

        activeCanon = Spawn(canonPoolTag, canonPrefab, transform.position, Quaternion.identity);
        if (activeCanon != null)
        {
            activeCanon.transform.SetParent(transform, worldPositionStays: false);
            activeCanon.transform.localPosition = canonLocalOffset;
            activeCanon.transform.localRotation = Quaternion.identity;

            var canon = activeCanon.GetComponent<BossSlimeCanon>();
            if (canon != null)
            {
                canon.usePool = usePool;
                canon.idleZ = canonIdleZ;
                canon.SetZRotation(canonIdleZ);
            }
        }
    }

    public void TakeDamage(float amount)
    {
        if (state == State.Dead) return;

        amount = Mathf.Abs(amount);
        if (amount <= 0f) amount = 1f;

        pendingDamage += amount;
        int appliedDamage = Mathf.FloorToInt(pendingDamage);
        if (appliedDamage <= 0) return;
        pendingDamage -= appliedDamage;

        hp -= appliedDamage;
        if (hp < 0) hp = 0;

        SpawnDamagingFx();
        TryTriggerHpAnimations();

        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(CoHitShake());

        StartOrExtendFlash();

        TriggerChildHitFlash();

        TryThresholdMove();
        UpdateBossUI_HPOnly();

        if (hp <= 0)
            DieByHp();
    }

    // Hitbox/ZigzagLightning에서 SendMessage("Hit", damage)로 들어오는 데미지 호환
    public void Hit(int damage)
    {
        TakeDamage(damage);
    }

    public void RegisterActiveArm(BossSlimeArm arm)
    {
        if (arm == null) return;
        if (!activeArms.Contains(arm))
            activeArms.Add(arm);
    }

    public void UnregisterActiveArm(BossSlimeArm arm)
    {
        if (arm == null) return;
        activeArms.Remove(arm);
    }

    private void TryThresholdMove()
    {
        if (maxHp <= 0) return;

        float ratio = (float)hp / (float)maxHp;

        if (!movedAt60 && ratio <= 0.6f)
        {
            movedAt60 = true;
            SnapToRandomInRange();
        }

        if (!movedAt30 && ratio <= 0.3f)
        {
            movedAt30 = true;
            SnapToRandomInRange();
        }
    }

    private void SnapToRandomInRange()
    {
        GetRoamBounds(out float minX, out float maxX, out float minY, out float maxY);

        float x = Random.Range(minX, maxX);
        float y = Random.Range(minY, maxY);
        transform.position = new Vector3(x, y, transform.position.z);
        PickNewRoamTarget();
    }

    private void DieByHp()
    {
        if (state == State.Dead) return;

        state = State.Dead;
        SetBossUIVisible(false);
        StopActiveRoutinesForEnding();
        deathRoutine = StartCoroutine(CoDeathSequence(false));
    }

    private IEnumerator CoTimeoutAttackLoop()
    {
        if (state == State.Dead || state == State.TimeoutAttacking)
            yield break;

        state = State.TimeoutAttacking;
        StopActiveRoutinesForTimeoutAttack();
        ClearActiveArmsNow();
        ClearSpawnedSubProjectilesNow();

        yield return CoPrepareTimeoutCanons();

        while (state == State.TimeoutAttacking && !IsGateBroken())
        {
            yield return CoFireTimeoutCanonVolley();
        }

        timeoutAttackRoutine = null;
    }

    private void StopActiveRoutinesForEnding()
    {
        if (mainRoutine != null)
        {
            StopCoroutine(mainRoutine);
            mainRoutine = null;
        }
        if (roamRoutine != null)
        {
            StopCoroutine(roamRoutine);
            roamRoutine = null;
        }
        if (timeoutAttackRoutine != null)
        {
            StopCoroutine(timeoutAttackRoutine);
            timeoutAttackRoutine = null;
        }
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }
    }

    private void StopActiveRoutinesForTimeoutAttack()
    {
        if (mainRoutine != null)
        {
            StopCoroutine(mainRoutine);
            mainRoutine = null;
        }
        if (roamRoutine != null)
        {
            StopCoroutine(roamRoutine);
            roamRoutine = null;
        }
    }

    private IEnumerator CoDeathSequence(bool triggerGameOver)
    {
        if (visualRoot == null) visualRoot = transform;
        Vector3 baseLocal = visualRoot.localPosition;
        float duration = Mathf.Max(0f, deathShakeDuration);
        float cycle = Mathf.Max(0.01f, deathShakeCycle);

        float t = 0f;
        while (t < duration)
        {
            float phase = (t / cycle) * Mathf.PI * 2f;
            float s = Mathf.Sin(phase);
            float xOffset = (s >= 0f) ? (s * shakeRight) : (s * shakeLeft);
            visualRoot.localPosition = new Vector3(baseLocal.x + xOffset, baseLocal.y, baseLocal.z);
            t += Time.deltaTime;
            yield return null;
        }
        visualRoot.localPosition = baseLocal;

        Spawn(slimeDestroyPoolTag, slimeDestroyPrefab, transform.position, Quaternion.identity);

        if (triggerGameOver)
        {
            var gate = GateHealth.Instance;
            if (gate != null)
                gate.SendMessage("ForceBreakByBoss", SendMessageOptions.DontRequireReceiver);
            else if (GameData.Instance != null)
                GameData.Instance.TriggerGameOver();
        }

        if (activeCanon != null)
            SafeReturnOrDestroy(activeCanon, canonPoolTag);
        activeCanon = null;
        ClearTimeoutExtraCanonsNow();

        state = State.Dead;
        deathRoutine = null;
        gameObject.SetActive(false);
    }

    private IEnumerator CoPrepareTimeoutCanons()
    {
        EnsureCanon();

        ClearTimeoutExtraCanonsNow();

        for (int i = 0; i < 3; i++)
        {
            GameObject extraCanonObj = Spawn(canonPoolTag, canonPrefab, transform.position, Quaternion.identity);
            if (extraCanonObj == null)
                continue;

            extraCanonObj.transform.SetParent(transform, worldPositionStays: false);
            extraCanonObj.transform.localRotation = Quaternion.identity;
            extraCanonObj.transform.localPosition = GetArrayValue(timeoutExtraCanonStartLocalOffsets, i, canonLocalOffset);

            var extraCanon = extraCanonObj.GetComponent<BossSlimeCanon>();
            if (extraCanon != null)
            {
                extraCanon.usePool = usePool;
                extraCanon.idleZ = canonIdleZ;
                extraCanon.SetZRotation(canonIdleZ);
            }

            timeoutExtraCanons.Add(extraCanonObj);
        }

        float moveDuration = Mathf.Max(0f, timeoutCanonMoveDuration);
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            float t = moveDuration <= 0.0001f ? 1f : (elapsed / moveDuration);
            UpdateTimeoutExtraCanonPositions(t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        UpdateTimeoutExtraCanonPositions(1f);

        float fireDelay = Mathf.Max(0f, timeoutCanonFireDelay);
        if (fireDelay > 0f)
            yield return new WaitForSeconds(fireDelay);
    }

    private IEnumerator CoFireTimeoutCanonVolley()
    {
        List<BossSlimeCanon> attackCanons = GetTimeoutAttackCanons();
        int fireCount = attackCanons.Count;
        if (fireCount <= 0)
        {
            yield return null;
            yield break;
        }

        List<Coroutine> aimRoutines = new List<Coroutine>(fireCount);

        for (int i = 0; i < fireCount; i++)
        {
            var canon = attackCanons[i];
            if (canon == null) continue;

            float aimAngle = GetArrayValue(canonAimAngles, i, 180f);
            aimRoutines.Add(StartCoroutine(CoRotateCanonToZ_IgnoreState(canon, aimAngle, GetCanonAimRotateDuration())));
        }

        for (int i = 0; i < aimRoutines.Count; i++)
        {
            if (aimRoutines[i] != null)
                yield return aimRoutines[i];
        }

        for (int i = 0; i < fireCount; i++)
        {
            var canon = attackCanons[i];
            if (canon == null) continue;

            float aimAngle = GetArrayValue(canonAimAngles, i, 180f);
            canon.PlayFireAnimation();
            canon.SpawnCanonBall(aimAngle, i);
        }

        float interval = 0.15f;
        int burstCount = 5;
        for (int shot = 1; shot < burstCount && state == State.TimeoutAttacking && !IsGateBroken(); shot++)
        {
            yield return new WaitForSeconds(interval);

            for (int i = 0; i < fireCount; i++)
            {
                var canon = attackCanons[i];
                if (canon == null) continue;

                float aimAngle = GetArrayValue(canonAimAngles, i, 180f);
                canon.PlayFireAnimation();
                canon.SpawnCanonBall(aimAngle, i);
            }
        }

        if (state == State.TimeoutAttacking && !IsGateBroken())
        {
            float reAimDelay = Mathf.Max(0.01f, canonReAimDelay);
            yield return new WaitForSeconds(reAimDelay);
        }
    }

    private void UpdateTimeoutExtraCanonPositions(float t)
    {
        float k = Mathf.Clamp01(t);
        for (int i = 0; i < timeoutExtraCanons.Count; i++)
        {
            var extraCanonObj = timeoutExtraCanons[i];
            if (extraCanonObj == null) continue;

            Vector3 start = GetArrayValue(timeoutExtraCanonStartLocalOffsets, i, canonLocalOffset);
            Vector3 end = GetArrayValue(timeoutExtraCanonEndLocalOffsets, i, start);
            extraCanonObj.transform.localPosition = Vector3.Lerp(start, end, k);
        }
    }

    private IEnumerator CoRotateCanonToZ_IgnoreState(BossSlimeCanon canon, float targetZ, float duration)
    {
        if (canon == null) yield break;

        float d = Mathf.Max(0f, duration);
        if (d <= 0.0001f)
        {
            canon.SetZRotation(targetZ);
            yield break;
        }

        float startZ = canon.transform.localEulerAngles.z;
        float elapsed = 0f;

        while (elapsed < d)
        {
            float t = elapsed / d;
            float z = Mathf.LerpAngle(startZ, targetZ, t);
            canon.SetZRotation(z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        canon.SetZRotation(targetZ);
    }

    private void ClearTimeoutExtraCanonsNow()
    {
        for (int i = timeoutExtraCanons.Count - 1; i >= 0; i--)
        {
            var extraCanonObj = timeoutExtraCanons[i];
            if (extraCanonObj == null) continue;
            SafeReturnOrDestroy(extraCanonObj, canonPoolTag);
        }
        timeoutExtraCanons.Clear();
    }

    private List<BossSlimeCanon> GetTimeoutAttackCanons()
    {
        List<BossSlimeCanon> attackCanons = new List<BossSlimeCanon>(4);

        var mainCanon = activeCanon != null ? activeCanon.GetComponent<BossSlimeCanon>() : null;
        if (mainCanon != null)
            attackCanons.Add(mainCanon);

        for (int i = 0; i < timeoutExtraCanons.Count; i++)
        {
            var extraCanonObj = timeoutExtraCanons[i];
            if (extraCanonObj == null) continue;

            var extraCanon = extraCanonObj.GetComponent<BossSlimeCanon>();
            if (extraCanon != null)
                attackCanons.Add(extraCanon);
        }

        return attackCanons;
    }

    private bool IsGateBroken()
    {
        var gate = GateHealth.Instance;
        return gate != null && gate.IsBroken;
    }

    private void SpawnDamagingFx()
    {
        Vector3 world = transform.TransformPoint(slimeDamagingLocalOffset);
        GameObject fx = Spawn(slimeDamagingPoolTag, slimeDamagingPrefab, world, Quaternion.identity);
        if (fx == null)
            return;

        var autoReturn = fx.GetComponent<AutoReturnToPool>();
        if (autoReturn == null)
            autoReturn = fx.AddComponent<AutoReturnToPool>();

        bool hasPool = usePool
            && ObjectPool.Instance != null
            && !string.IsNullOrEmpty(slimeDamagingPoolTag)
            && ObjectPool.Instance.HasPool(slimeDamagingPoolTag);

        autoReturn.usePool = hasPool;
        autoReturn.poolTag = slimeDamagingPoolTag;
        autoReturn.lifeTime = Mathf.Max(0.05f, slimeDamagingFxLifeTime);
    }

    private void TryTriggerHpAnimations()
    {
        if (slimeAnimator == null) return;
        if (maxHp <= 0) return;

        float ratio = (float)hp / (float)maxHp;

        if (!firedHp60Trigger && ratio <= 0.60f)
        {
            firedHp60Trigger = true;
            if (!string.IsNullOrEmpty(hp60TriggerName))
                slimeAnimator.SetTrigger(hp60TriggerName);
        }

        if (!firedHp30Trigger && ratio <= 0.30f)
        {
            firedHp30Trigger = true;
            if (!string.IsNullOrEmpty(hp30TriggerName))
                slimeAnimator.SetTrigger(hp30TriggerName);
        }
    }

    private void ResetHpAnimationState()
    {
        if (slimeAnimator == null) return;

        if (!string.IsNullOrEmpty(hp60TriggerName))
            slimeAnimator.ResetTrigger(hp60TriggerName);
        if (!string.IsNullOrEmpty(hp30TriggerName))
            slimeAnimator.ResetTrigger(hp30TriggerName);

        slimeAnimator.Rebind();
        slimeAnimator.Update(0f);
        slimeAnimator.applyRootMotion = false;
    }

    private IEnumerator MoveToOverTime(Vector3 target, float time)
    {
        Vector3 start = transform.position;
        float duration = Mathf.Max(0.01f, time);

        float t = 0f;
        while (t < duration)
        {
            float k = t / duration;
            transform.position = Vector3.Lerp(start, target, k);
            t += Time.deltaTime;
            yield return null;
        }

        transform.position = target;
    }

    private GameObject Spawn(string poolTag, GameObject prefab, Vector3 pos, Quaternion rot)
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            return ObjectPool.Instance.SpawnFromPool(poolTag, pos, rot);

        if (prefab != null)
            return Instantiate(prefab, pos, rot);

        return null;
    }

    private void SafeReturnOrDestroy(GameObject obj, string poolTag)
    {
        if (obj == null) return;

        obj.transform.SetParent(null, true);

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, obj);
        else
            Destroy(obj);
    }

    private static float GetArrayValue(float[] arr, int index, float fallback)
    {
        if (arr == null || arr.Length == 0) return fallback;
        int i = Mathf.Clamp(index, 0, arr.Length - 1);
        return arr[i];
    }

    private float GetCanonAimRotateDuration()
    {
        return 2f;
    }

    private static Vector3 GetArrayValue(Vector3[] arr, int index, Vector3 fallback)
    {
        if (arr == null || arr.Length == 0) return fallback;
        int i = Mathf.Clamp(index, 0, arr.Length - 1);
        return arr[i];
    }

    private void ClearSpawnedSubProjectilesNow()
    {
        string jellyTag = ResolveJellyPoolTag();
        string canonBallTag = ResolveCanonBallPoolTag();

        if (ObjectPool.Instance != null)
        {
            List<string> tags = new List<string>(2);
            if (!string.IsNullOrEmpty(jellyTag)) tags.Add(jellyTag);
            if (!string.IsNullOrEmpty(canonBallTag)) tags.Add(canonBallTag);
            if (tags.Count > 0)
                ObjectPool.Instance.ReturnAllActive(tags.ToArray());
        }

        if (!ShouldRunGlobalSubProjectileCleanup())
            return;

        // 풀 미사용/태그 불일치 같은 예외 케이스 대비
        var jellies = FindObjectsByType<BossSlimeJelly>(FindObjectsSortMode.None);
        for (int i = 0; i < jellies.Length; i++)
        {
            var j = jellies[i];
            if (j == null || !j.gameObject.scene.IsValid()) continue;
            if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(j.poolTag) && ObjectPool.Instance.HasPool(j.poolTag))
                ObjectPool.Instance.ReturnToPool(j.poolTag, j.gameObject);
            else
                Destroy(j.gameObject);
        }

        var balls = FindObjectsByType<BossSlimeCanonBall>(FindObjectsSortMode.None);
        for (int i = 0; i < balls.Length; i++)
        {
            var b = balls[i];
            if (b == null || !b.gameObject.scene.IsValid()) continue;
            if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(b.poolTag) && ObjectPool.Instance.HasPool(b.poolTag))
                ObjectPool.Instance.ReturnToPool(b.poolTag, b.gameObject);
            else
                Destroy(b.gameObject);
        }
    }

    private string ResolveJellyPoolTag()
    {
        if (armPrefab != null)
        {
            var arm = armPrefab.GetComponent<BossSlimeArm>();
            if (arm != null && !string.IsNullOrEmpty(arm.jellyPoolTag))
                return arm.jellyPoolTag;
        }
        return "BossSlimeJelly";
    }

    private string ResolveCanonBallPoolTag()
    {
        if (canonPrefab != null)
        {
            var canon = canonPrefab.GetComponent<BossSlimeCanon>();
            if (canon != null && !string.IsNullOrEmpty(canon.canonBallPoolTag))
                return canon.canonBallPoolTag;
        }
        return "BossSlimeCanonBall";
    }

    private void UpdateBossUI()
    {
        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHp;
            hpSlider.value = hp;
        }

        if (timerSlider != null)
        {
            timerSlider.maxValue = lifeTimeSeconds;
            float elapsed = Time.time - spawnTime;
            float remain = Mathf.Max(0f, lifeTimeSeconds - elapsed);
            timerSlider.value = remain;
        }
    }

    private void UpdateBossUI_HPOnly()
    {
        if (hpSlider == null) return;
        hpSlider.maxValue = maxHp;
        hpSlider.value = hp;
    }

    private void SetBossUIVisible(bool visible)
    {
        if (hpSlider != null)
            hpSlider.gameObject.SetActive(visible);
        if (timerSlider != null)
            timerSlider.gameObject.SetActive(visible);
    }

    private void SetBossVisualActive(bool visible)
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0)
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
        if (cachedColliders == null || cachedColliders.Length == 0)
            cachedColliders = GetComponentsInChildren<Collider2D>(true);

        for (int i = 0; i < cachedRenderers.Length; i++)
            if (cachedRenderers[i] != null) cachedRenderers[i].enabled = visible;

        for (int i = 0; i < cachedColliders.Length; i++)
            if (cachedColliders[i] != null) cachedColliders[i].enabled = visible;
    }

    private void CacheRendererColors()
    {
        cachedSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalSpriteColors = new Color[cachedSpriteRenderers.Length];
        for (int i = 0; i < cachedSpriteRenderers.Length; i++)
        {
            var sr = cachedSpriteRenderers[i];
            originalSpriteColors[i] = (sr != null) ? sr.color : Color.white;
        }
    }

    private void SetAllRenderersColor(Color c)
    {
        if (cachedSpriteRenderers == null || originalSpriteColors == null || cachedSpriteRenderers.Length != originalSpriteColors.Length)
            CacheRendererColors();

        for (int i = 0; i < cachedSpriteRenderers.Length; i++)
        {
            var sr = cachedSpriteRenderers[i];
            if (sr == null) continue;

            // 알파는 원래값 유지: "사라짐" 방지
            Color baseCol = originalSpriteColors[i];
            sr.color = new Color(c.r, c.g, c.b, baseCol.a);
        }
    }

    private void RestoreRendererColors()
    {
        if (cachedSpriteRenderers == null || originalSpriteColors == null) return;
        if (cachedSpriteRenderers.Length != originalSpriteColors.Length) return;

        for (int i = 0; i < cachedSpriteRenderers.Length; i++)
        {
            var sr = cachedSpriteRenderers[i];
            if (sr == null) continue;
            sr.color = originalSpriteColors[i];
        }
    }

    private IEnumerator CoFlashWholePrefab()
    {
        if (cachedSpriteRenderers == null || originalSpriteColors == null || cachedSpriteRenderers.Length != originalSpriteColors.Length)
            CacheRendererColors();

        SetAllRenderersColor(hitColor);
        while (Time.time < flashUntilTime)
            yield return null;

        RestoreRendererColors();
        flashRoutine = null;
    }

    private IEnumerator CoHitShake()
    {
        if (visualRoot == null) visualRoot = transform;
        // 현재 위치를 기준으로 흔들어야 순간 텔레포트(깜빡임)처럼 보이지 않는다.
        Vector3 baseLocal = visualRoot.localPosition;
        float total = Mathf.Max(0.01f, shakeTotalDuration);

        float t0 = total * 0.33f;
        float t1 = total * 0.33f;
        float t2 = total - t0 - t1;

        visualRoot.localPosition = baseLocal + Vector3.left * shakeLeft;
        yield return new WaitForSeconds(t0);

        visualRoot.localPosition = baseLocal + Vector3.right * shakeRight;
        yield return new WaitForSeconds(t1);

        visualRoot.localPosition = baseLocal;
        if (t2 > 0f) yield return new WaitForSeconds(t2);
    }

    private void StartOrExtendFlash()
    {
        flashUntilTime = Mathf.Max(flashUntilTime, Time.time + Mathf.Max(0.01f, hitFlashDuration));
        if (flashRoutine == null)
            flashRoutine = StartCoroutine(CoFlashWholePrefab());
    }

    private void TriggerChildHitFlash()
    {
        var canon = activeCanon != null ? activeCanon.GetComponent<BossSlimeCanon>() : null;
        if (canon != null && canon.gameObject.activeInHierarchy)
            canon.TriggerHitFlash(hitColor, hitFlashDuration);

        for (int i = activeArms.Count - 1; i >= 0; i--)
        {
            var arm = activeArms[i];
            if (arm == null || !arm.gameObject.activeInHierarchy)
            {
                activeArms.RemoveAt(i);
                continue;
            }
            arm.TriggerHitFlash(hitColor, hitFlashDuration);
        }
    }

    private void HandleGameOver()
    {
        if (!gameObject.activeInHierarchy) return;

        SetBossUIVisible(false);
        state = State.Dead;

        if (mainRoutine != null)
        {
            StopCoroutine(mainRoutine);
            mainRoutine = null;
        }
        if (roamRoutine != null)
        {
            StopCoroutine(roamRoutine);
            roamRoutine = null;
        }
        if (timeoutAttackRoutine != null)
        {
            StopCoroutine(timeoutAttackRoutine);
            timeoutAttackRoutine = null;
        }
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }

        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }
        RestoreRendererColors();
    }

    private void ClearActiveArmsNow()
    {
        for (int i = activeArms.Count - 1; i >= 0; i--)
        {
            var arm = activeArms[i];
            if (arm == null) continue;
            SafeReturnOrDestroy(arm.gameObject, armPoolTag);
        }
        activeArms.Clear();

        if (!ShouldRunGlobalSubProjectileCleanup())
            return;

        // 리스트 누락 대비 안전망
        var arms = FindObjectsByType<BossSlimeArm>(FindObjectsSortMode.None);
        for (int i = 0; i < arms.Length; i++)
        {
            var arm = arms[i];
            if (arm == null || !arm.gameObject.scene.IsValid()) continue;
            SafeReturnOrDestroy(arm.gameObject, armPoolTag);
        }
    }

    private bool ShouldRunGlobalSubProjectileCleanup()
    {
        if (!hasBegunEncounter)
            return false;

        if (activeArms.Count > 0)
            return true;

        if (state != State.Inactive)
            return true;

        return false;
    }
}
