using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

public class Boss : MonoBehaviour, IReinitializable
{
    public enum State { Inactive, Entering, Active, DashingToGate, Dead }
    public State state = State.Inactive;

    [Header("HP / Timer")]
    public int maxHp = 50;
    public float lifeTimeSeconds = 100f;

    [Header("Boss UI (World Space)")]
    public Slider hpSlider;
    public Slider timerSlider;

    [Header("Enter (World)")]
    public Vector3 enterTargetWorldPos = new Vector3(-2.5f, 1.4f, 0f);
    public float enterMoveTime = 2f;

    [Header("Roam Center (World)")]
    public Vector3 roamCenterWorldPos = new Vector3(-2.5f, 1.4f, 0f);
    public float roamRangeLeft = 1.5f;
    public float roamRangeRight = 1.5f;
    public float roamRangeUp = 1.2f;
    public float roamRangeDown = 1.2f;
    public float roamSpeed = 2.5f;
    public float roamPickInterval = 0.8f;

    [Header("Dash To Gate (World)")]
    public Vector3 gateDashTargetWorldPos = new Vector3(-9f, 1.4f, 0f);
    public float dashDuration = 1.2f;
    public float deathByHpDelay = 1.5f;
    public float deathByHpTargetY = -2.3f;
    public float deathByHpShakeCycle = 0.1f;

    // ===================== LOCAL OFFSETS (FOLLOW BOSS) =====================
    [Header("Missile Spawn (LOCAL offsets from Boss)")]
    public Vector3 rightFireLocalOffset = new Vector3(1.0f, 0.2f, 0f);
    public Vector3 leftFireLocalOffset  = new Vector3(-1.0f, 0.2f, 0f);

    [Header("Cap Positions (LOCAL offsets from Boss)")]
    public Vector3 rightCapStartLocalOffset = new Vector3(0.4f, 0.6f, 0f);
    public Vector3 leftCapStartLocalOffset  = new Vector3(-0.4f, 0.6f, 0f);
    public Vector3 rightCapFireLocalOffset  = new Vector3(0.9f, 0.25f, 0f);
    public Vector3 leftCapFireLocalOffset   = new Vector3(-0.9f, 0.25f, 0f);

    [Header("Attack Pattern")]
    public float loadDelay = 1f;
    public float moveAfterLoad = 2f;
    public float preFireStop = 0.2f;
    public float moveBetweenShots = 0.5f;
    public float moveAfterShots = 1f;
    public int fireRepeatCount = 5;
    public float restAfterRepeats = 5f;

    [Header("Monster Spawn After Rage")]
    public GameObject monsterPrefab;
    public Vector3 monsterSpawnWorldPos = new Vector3(16f, -3f, 0f);
    public int monsterSpawnCount = 13;
    public float monsterSpawnInterval = 0.3f;

    [Header("Missile")]
    public float missileSpeed = 6f;

    [Header("Rage Laser Sequence")]
    public GameObject bossRageMissilePrefab;
    public Vector3 bossRageMissileLocalOffset = new Vector3(1.2f, 0f, 0f);
    public GameObject bossRageFirePrefab;
    public float rageDoorOpenTime = 3.5f;
    public float rageFirstShotDelay = 1f;
    public float rageMoveToStopYTime = 0.3f;
    public int rageBurstCount = 4;
    public float rageBurstFireDuration = 2f;
    public float rageBurstMoveDuration = 1.5f;
    public float rageBurstShiftSpeed = 1.5f;
    public float rageBurstRoamDuration = 1.3f;
    public float rageLaserInterval = 0.1f;
    public float rageMissileSpeed = 12f;
    private float[] rageStopYOffsetCandidates = new float[] { 2.5f, 1.5f, 0.7f, -0.3f };
    public float rageOffAnimEventFallbackTime = 4f;

    [Header("Boss Animator (Trigger Names)")]
    public Animator bossAnimator;
    public string triggerBossRage = "BossRage";
    public string triggerBossRageOff = "BossRageOff";
    public string triggerBossRageFly = "BossRageFly";
    public string triggerBossNormal = "BossNormal";
    public string rageOffStateName = "BossRageOff";

    // ===================== POOL / PREFABS =====================
    [Header("Pool Use")]
    public bool usePool = true;

    [Header("Pool Tags")]
    public string bossMissilePoolTag = "BossMissile";
    public string bossDestroyPoolTag = "BossDestroy";
    public string bossCapRightPoolTag = "BossCapRight";
    public string bossCapLeftPoolTag = "BossCapLeft";
    public string bossRageMissilePoolTag = "BossRageMissile";
    public string bossRageFirePoolTag = "";

    [Header("Fallback Prefabs")]
    public GameObject bossMissilePrefab;
    public GameObject bossDestroyPrefab;
    public GameObject bossCapRightPrefab;
    public GameObject bossCapLeftPrefab;

    // ===================== FLASH (WHOLE PREFAB) =====================
    [Header("Hit Flash Whole Prefab")]
    public Color hitColor = Color.red;
    public float hitFlashDuration = 0.5f;

    // ===================== HIT SHAKE =====================
    [Header("Hit Shake")]
    [Tooltip("보스 움직임(Enter/Roam)은 transform이 하고, 흔들림은 visualRoot만 흔들면 충돌이 적음. 비워두면 transform이 흔들림.")]
    public Transform visualRoot;
    [Tooltip("좌로 0.1, 우로 0.2 (월드 기준).")]
    public float shakeLeft = 0.1f;
    public float shakeRight = 0.2f;
    [Tooltip("총 흔들림 시간(권장 0.1초).")]
    public float shakeTotalDuration = 0.1f;

    // ===================== ANIM PREFABS (INSPECTOR) =====================
    [Header("Animations (Prefabs + LOCAL offsets from Boss)")]
    public GameObject breakAnimPrefab;
    public Vector3 breakAnimLocalOffset = new Vector3(0f, 0f, 0f);

    public GameObject hp60AnimPrefab;
    public Vector3 hp60AnimLocalOffset = new Vector3(0f, 0f, 0f);

    public GameObject hp20AnimPrefab;
    public Vector3 hp20AnimLocalOffset = new Vector3(0f, 0f, 0f);

    [Header("Optional: Parent to follow Boss")]
    public bool parentAnimsToBoss = true;

    [Header("Optional Pool Tags for These Anims (blank = no pool)")]
    public string breakAnimPoolTag = "";
    public string hp60AnimPoolTag = "";
    public string hp20AnimPoolTag = "";

    private int hp;
    private float spawnTime;
    private float shootDisabledUntil = -999f;

    private Coroutine mainRoutine;
    private Coroutine flashRoutine;
    private Coroutine shakeRoutine;
    private Coroutine gameOverHideRoutine;

    private SpriteRenderer rightCapSR;
    private SpriteRenderer leftCapSR;

    private Vector3 roamTarget;
    private float nextRoamPickTime;
    private Vector3 initialWorldPos;
    private Quaternion initialWorldRot;
    private Vector3 initialLocalScale;
    private Transform initialParent;

    private GameObject rightCapObj;
    private GameObject leftCapObj;
    private GameObject hp60AnimObj;
    private GameObject hp20AnimObj;

    private Renderer[] allRenderers;
    private List<Color[]> originalColors = new List<Color[]>();

    private bool spawnedHp60Anim = false;
    private bool spawnedHp20Anim = false;
    private int lastRageStopIndex = -1;
    private float pendingDamage = 0f;

    // ===================== PUBLIC =====================
    public void Reinit() => ResetBossRuntime();

    public void Begin()
    {
        if (state != State.Inactive) return;
        spawnTime = Time.time;
        UpdateBossUI();
        mainRoutine = StartCoroutine(CoMain());
    }

    public void ResetBossRuntime()
    {
        state = State.Inactive;
        hp = maxHp;
        shootDisabledUntil = -999f;
        if (initialParent != null && transform.parent != initialParent)
            transform.SetParent(initialParent, true);
        transform.SetPositionAndRotation(initialWorldPos, initialWorldRot);
        transform.localScale = initialLocalScale;

        roamTarget = transform.position;
        nextRoamPickTime = -999f;
        spawnTime = Time.time;

        spawnedHp60Anim = false;
        spawnedHp20Anim = false;
        lastRageStopIndex = -1;
        pendingDamage = 0f;

        if (mainRoutine != null) { StopCoroutine(mainRoutine); mainRoutine = null; }
        if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }
        if (shakeRoutine != null) { StopCoroutine(shakeRoutine); shakeRoutine = null; }
        if (gameOverHideRoutine != null) { StopCoroutine(gameOverHideRoutine); gameOverHideRoutine = null; }

        if (rightCapObj) SafeReturnOrDestroy(rightCapObj, bossCapRightPoolTag);
        if (leftCapObj) SafeReturnOrDestroy(leftCapObj, bossCapLeftPoolTag);
        rightCapObj = null;
        leftCapObj = null;
        ClearThresholdAnimObjects();

        SetBossUIVisible(true);
        RestoreOriginalColors();
        SetBossAnimTrigger(triggerBossNormal);
        UpdateBossUI();
    }

    void Awake()
    {
        CacheAllRenderers();
        hp = maxHp;
        initialParent = transform.parent;
        initialWorldPos = transform.position;
        initialWorldRot = transform.rotation;
        initialLocalScale = transform.localScale;
        if (visualRoot == null) visualRoot = transform;
        if (bossAnimator == null)
            bossAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
    }

    void OnEnable()
    {
        GameData.OnGameOver += HandleGameOver;
        ResetBossRuntime();
    }

    void OnDisable()
    {
        GameData.OnGameOver -= HandleGameOver;
        gameOverHideRoutine = null;
    }

    void Update()
    {
        if (state != State.Active) return;

        UpdateBossUI();

        if (Time.time - spawnTime >= lifeTimeSeconds)
        {
            if (mainRoutine != null) { StopCoroutine(mainRoutine); mainRoutine = null; }
            StartCoroutine(CoTimeoutDash());
        }
    }

    // ===================== MAIN LOOP =====================
    private IEnumerator CoMain()
    {
        state = State.Entering;
        yield return MoveToOverTime(enterTargetWorldPos, enterMoveTime);

        state = State.Active;
        spawnTime = Time.time;
        SetBossUIVisible(true);
        UpdateBossUI();
        SetBossAnimTrigger(triggerBossNormal);
        PickNewRoamTarget();
        nextRoamPickTime = Time.time + roamPickInterval;

        while (state == State.Active)
        {
            for (int i = 0; i < fireRepeatCount; i++)
            {
                if (state != State.Active) yield break;

                yield return CoLoadCaps(loadDelay);
                yield return CoRoamForSeconds(moveAfterLoad);

                yield return CoWait(preFireStop);
                yield return CoFireOneSide(true);

                yield return CoRoamForSeconds(moveBetweenShots);

                yield return CoWait(preFireStop);
                yield return CoFireOneSide(false);

                yield return CoRoamForSeconds(moveAfterShots);
            }

            yield return CoRageLaserSequence();
            yield return CoRestAndSpawnEnemies(restAfterRepeats);
        }
    }

    // ===================== ROAM =====================
    private void PickNewRoamTarget()
    {
        Vector3 c = roamCenterWorldPos;
        float x = Random.Range(c.x - roamRangeLeft, c.x + roamRangeRight);
        float y = Random.Range(c.y - roamRangeDown, c.y + roamRangeUp);
        roamTarget = new Vector3(x, y, transform.position.z);
    }

    private void RoamTick()
    {
        if (Time.time >= nextRoamPickTime)
        {
            PickNewRoamTarget();
            nextRoamPickTime = Time.time + roamPickInterval;
        }

        transform.position = Vector3.MoveTowards(transform.position, roamTarget, roamSpeed * Time.deltaTime);
    }

    private IEnumerator CoRoamForSeconds(float sec)
    {
        float t = 0f;
        while (t < sec && state == State.Active)
        {
            RoamTick();
            t += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator CoWait(float sec)
    {
        float t = 0f;
        while (t < sec && state == State.Active)
        {
            t += Time.deltaTime;
            yield return null;
        }
    }

    // ===================== CAPS =====================
    private void EnsureCaps()
    {
        if (rightCapObj == null)
        {
            rightCapObj = Spawn(bossCapRightPoolTag, bossCapRightPrefab, transform.position, Quaternion.identity);
            if (rightCapObj)
            {
                rightCapObj.transform.SetParent(transform, worldPositionStays: false);
                rightCapObj.transform.localRotation = Quaternion.identity;
                rightCapObj.transform.localScale = Vector3.one;
            }
        }
        if (leftCapObj == null)
        {
            leftCapObj = Spawn(bossCapLeftPoolTag, bossCapLeftPrefab, transform.position, Quaternion.identity);
            if (leftCapObj)
            {
                leftCapObj.transform.SetParent(transform, worldPositionStays: false);
                leftCapObj.transform.localRotation = Quaternion.identity;
                leftCapObj.transform.localScale = Vector3.one;
            }
        }

        if (rightCapObj && rightCapSR == null)
            rightCapSR = rightCapObj.GetComponent<SpriteRenderer>() ?? rightCapObj.GetComponentInChildren<SpriteRenderer>(true);

        if (leftCapObj && leftCapSR == null)
            leftCapSR = leftCapObj.GetComponent<SpriteRenderer>() ?? leftCapObj.GetComponentInChildren<SpriteRenderer>(true);
            
        if (rightCapSR) rightCapSR.enabled = true;
        if (leftCapSR)  leftCapSR.enabled  = true;
    }

    private IEnumerator CoLoadCaps(float sec)
    {
        if (state != State.Active) yield break;

        EnsureCaps();
        if (rightCapSR) rightCapSR.enabled = true;
        if (leftCapSR)  leftCapSR.enabled  = true;

        if (rightCapObj) rightCapObj.transform.localPosition = rightCapStartLocalOffset;
        if (leftCapObj)  leftCapObj.transform.localPosition  = leftCapStartLocalOffset;

        float t = 0f;
        while (t < sec && state == State.Active)
        {
            float k = sec <= 0.0001f ? 1f : (t / sec);

            if (rightCapObj) rightCapObj.transform.localPosition = Vector3.Lerp(rightCapStartLocalOffset, rightCapFireLocalOffset, k);
            if (leftCapObj)  leftCapObj.transform.localPosition  = Vector3.Lerp(leftCapStartLocalOffset,  leftCapFireLocalOffset,  k);

            t += Time.deltaTime;
            yield return null;
        }

        if (rightCapObj) rightCapObj.transform.localPosition = rightCapFireLocalOffset;
        if (leftCapObj)  leftCapObj.transform.localPosition  = leftCapFireLocalOffset;
    }

    // ===================== FIRE =====================
    private Vector3 RightFireWorldPos() => transform.TransformPoint(rightFireLocalOffset);
    private Vector3 LeftFireWorldPos()  => transform.TransformPoint(leftFireLocalOffset);

    private IEnumerator CoFireOneSide(bool right)
    {
        if (state != State.Active) yield break;
        if (Time.time < shootDisabledUntil) yield break;

        EnsureCaps();

        if (right)
        {
            if (rightCapSR) rightCapSR.enabled = false;
            if (rightCapObj) rightCapObj.transform.localPosition = rightCapStartLocalOffset; // ✅ 추가
        }
        else
        {
            if (leftCapSR) leftCapSR.enabled = false;
            if (leftCapObj) leftCapObj.transform.localPosition = leftCapStartLocalOffset;   // ✅ 추가
        }

        Vector3 firePos = right ? RightFireWorldPos() : LeftFireWorldPos();

        GameObject missile = Spawn(bossMissilePoolTag, bossMissilePrefab, firePos, Quaternion.identity);
        if (missile)
        {
            var bm = missile.GetComponent<BossMissile>();
            if (bm)
            {
                bm.speed = missileSpeed;
                bm.usePool = usePool;
                bm.poolTag = bossMissilePoolTag;
            }
        }

        yield return null;
    }

    // ===================== MONSTER SPAWN =====================
    private IEnumerator CoRestAndSpawnEnemies(float restSec)
    {
        if (state != State.Active) yield break;

        int spawned = 0;
        int total = Mathf.Max(0, monsterSpawnCount);
        float interval = Mathf.Max(0.01f, monsterSpawnInterval);

        while (state == State.Active && spawned < total)
        {
            if (monsterPrefab != null)
                Instantiate(monsterPrefab, monsterSpawnWorldPos, Quaternion.identity);

            spawned++;
            yield return CoWait(interval);
        }

        // 스폰 후 텀(기본 5초) 동안 로밍 유지
        yield return CoRoamForSeconds(restSec);
    }

    private IEnumerator CoRageLaserSequence()
    {
        if (state != State.Active) yield break;

        SetBossAnimTrigger(triggerBossRage);

        yield return CoWait(rageDoorOpenTime);
        if (state != State.Active)
        {
            EndRageModeImmediate();
            yield break;
        }

        SetBossAnimTrigger(triggerBossRageFly);
        PickNewRoamTarget();
        nextRoamPickTime = Time.time + roamPickInterval;
        yield return CoRoamForSeconds(rageFirstShotDelay);
        if (state != State.Active)
        {
            EndRageModeImmediate();
            yield break;
        }

        int burstCount = Mathf.Max(1, rageBurstCount);
        for (int burst = 0; burst < burstCount && state == State.Active; burst++)
        {
            if (burst > 0)
                SetBossAnimTrigger(triggerBossRageFly);

            float fireElapsed = 0f;
            float shotTimer = 0f;
            float fireDuration = Mathf.Max(0.01f, rageBurstFireDuration);
            float interval = Mathf.Max(0.01f, rageLaserInterval);
            float phaseDuration = fireDuration / 3f;
            float burstAnchorY = transform.position.y;
            float midMoveTargetY = PickRageStopYExcept(burstAnchorY);
            float midMoveSpeed = Mathf.Max(0.01f, rageBurstShiftSpeed);
            bool startedShiftMove = false;
            while (fireElapsed < fireDuration && state == State.Active)
            {
                fireElapsed += Time.deltaTime;
                shotTimer += Time.deltaTime;

                if (!startedShiftMove && fireElapsed >= phaseDuration)
                    startedShiftMove = true;

                if (startedShiftMove)
                {
                    Vector3 pos = transform.position;
                    float nextY = Mathf.MoveTowards(pos.y, midMoveTargetY, midMoveSpeed * Time.deltaTime);
                    transform.position = new Vector3(pos.x, nextY, pos.z);
                }

                while (shotTimer >= interval)
                {
                    shotTimer -= interval;
                    SpawnRageMissile();
                }

                yield return null;
            }

            if (state != State.Active) break;

            yield return ReturnToRoamBoundsIfNeeded();
            PickNewRoamTarget();
            nextRoamPickTime = Time.time + roamPickInterval;

            if (burst < burstCount - 1)
                yield return CoRoamForSeconds(rageBurstRoamDuration);
        }

        SetBossAnimTrigger(triggerBossRageOff);
        yield return WaitForRageOffAnimToFinish();
        EndRageModeImmediate();
    }


    private void SpawnRageMissile()
    {
        if (state != State.Active) return;

        Vector3 firePos = transform.TransformPoint(bossRageMissileLocalOffset);
        GameObject missile = Spawn(bossRageMissilePoolTag, bossRageMissilePrefab, firePos, Quaternion.identity);
        if (missile == null) return;

        var rageMissile = missile.GetComponent<BossRageMissile>();
        if (rageMissile != null)
        {
            rageMissile.speed = rageMissileSpeed;
            rageMissile.direction = Vector3.left;
            rageMissile.usePool = usePool;
            rageMissile.poolTag = bossRageMissilePoolTag;
            rageMissile.destroyPoolTag = bossRageFirePoolTag;
            rageMissile.destroyFxPrefab = bossRageFirePrefab;
        }
    }

    private float PickRageStopY()
    {
        if (rageStopYOffsetCandidates == null || rageStopYOffsetCandidates.Length == 0)
            return transform.position.y;

        int index = Random.Range(0, rageStopYOffsetCandidates.Length);
        lastRageStopIndex = index;
        return ResolveRageStopTargetY(transform.position.y, index);
    }

    private float PickRageStopYExcept(float currentY)
    {
        if (rageStopYOffsetCandidates == null || rageStopYOffsetCandidates.Length == 0)
            return currentY;
        if (rageStopYOffsetCandidates.Length == 1)
        {
            lastRageStopIndex = 0;
            return ResolveRageStopTargetY(currentY, 0);
        }

        int excludeIndex = lastRageStopIndex;

        List<int> candidates = new List<int>(rageStopYOffsetCandidates.Length);
        for (int i = 0; i < rageStopYOffsetCandidates.Length; i++)
        {
            if (i != excludeIndex)
                candidates.Add(i);
        }

        if (candidates.Count == 0)
        {
            lastRageStopIndex = Mathf.Clamp(excludeIndex, -1, rageStopYOffsetCandidates.Length - 1);
            return currentY;
        }

        int pickedIndex = candidates[Random.Range(0, candidates.Count)];
        lastRageStopIndex = pickedIndex;
        return ResolveRageStopTargetY(currentY, pickedIndex);
    }

    private float PickRageDirectionalStopY(float currentY, bool moveUp)
    {
        if (rageStopYOffsetCandidates == null || rageStopYOffsetCandidates.Length == 0)
            return currentY;

        List<int> preferred = new List<int>(rageStopYOffsetCandidates.Length);
        List<int> fallback = new List<int>(rageStopYOffsetCandidates.Length);

        for (int i = 0; i < rageStopYOffsetCandidates.Length; i++)
        {
            float candidateDelta = rageStopYOffsetCandidates[i];
            bool sameDirection = moveUp ? candidateDelta > 0f : candidateDelta < 0f;

            if (sameDirection)
                preferred.Add(i);
            else if (!Mathf.Approximately(candidateDelta, 0f))
                fallback.Add(i);
        }

        List<int> source = preferred.Count > 0 ? preferred : fallback;
        if (source.Count == 0)
            return currentY;

        int pickedIndex = source[Random.Range(0, source.Count)];
        lastRageStopIndex = pickedIndex;
        return ResolveRageStopTargetY(currentY, pickedIndex);
    }

    private int FindNearestRageStopIndex(float y)
    {
        if (rageStopYOffsetCandidates == null || rageStopYOffsetCandidates.Length == 0)
            return -1;

        const float epsilon = 0.05f;
        int nearest = -1;
        float best = float.MaxValue;
        for (int i = 0; i < rageStopYOffsetCandidates.Length; i++)
        {
            float candidateY = ResolveRageStopTargetY(y, i);
            float d = Mathf.Abs(candidateY - y);
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }

        if (best <= epsilon) return nearest;
        return -1;
    }

    private float ResolveRageStopTargetY(float currentY, int candidateIndex)
    {
        if (rageStopYOffsetCandidates == null || candidateIndex < 0 || candidateIndex >= rageStopYOffsetCandidates.Length)
            return currentY;

        return currentY + rageStopYOffsetCandidates[candidateIndex];
    }

    private float GetRoamMinY() => roamCenterWorldPos.y - roamRangeDown;
    private float GetRoamMaxY() => roamCenterWorldPos.y + roamRangeUp;

    private IEnumerator ReturnToRoamBoundsIfNeeded()
    {
        float clampedY = Mathf.Clamp(transform.position.y, GetRoamMinY(), GetRoamMaxY());
        if (Mathf.Approximately(clampedY, transform.position.y))
            yield break;

        float returnTime = Mathf.Max(0.01f, rageMoveToStopYTime);
        yield return MoveToYOverTimeWhileActive(clampedY, returnTime);
    }


    private IEnumerator MoveToYOverTimeWhileActive(float targetY, float time)
    {
        Vector3 start = transform.position;
        Vector3 end = new Vector3(start.x, targetY, start.z);

        float t = 0f;
        while (t < time && state == State.Active)
        {
            float k = time <= 0.0001f ? 1f : (t / time);
            transform.position = Vector3.Lerp(start, end, k);
            t += Time.deltaTime;
            yield return null;
        }

        if (state == State.Active)
            transform.position = end;
    }

    private void EndRageModeImmediate()
    {
        SetBossAnimTrigger(triggerBossNormal);
    }

    private void SetBossAnimTrigger(string triggerName)
    {
        if (bossAnimator == null) return;

        ResetBossAnimTriggers();
        if (!string.IsNullOrEmpty(triggerName))
            bossAnimator.SetTrigger(triggerName);
    }

    private void ResetBossAnimTriggers()
    {
        if (bossAnimator == null) return;

        if (!string.IsNullOrEmpty(triggerBossRage))
            bossAnimator.ResetTrigger(triggerBossRage);
        if (!string.IsNullOrEmpty(triggerBossRageOff))
            bossAnimator.ResetTrigger(triggerBossRageOff);
        if (!string.IsNullOrEmpty(triggerBossRageFly))
            bossAnimator.ResetTrigger(triggerBossRageFly);
        if (!string.IsNullOrEmpty(triggerBossNormal))
            bossAnimator.ResetTrigger(triggerBossNormal);
    }

    private IEnumerator WaitForRageOffAnimToFinish()
    {
        if (bossAnimator == null) yield break;

        float elapsed = 0f;
        float fallback = Mathf.Max(0.1f, rageOffAnimEventFallbackTime);

        while (state == State.Active && elapsed < fallback)
        {
            AnimatorStateInfo st = bossAnimator.GetCurrentAnimatorStateInfo(0);
            if (!string.IsNullOrEmpty(rageOffStateName) && st.IsName(rageOffStateName) && st.normalizedTime >= 0.98f)
                yield break;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ===================== DAMAGE / HITBOX =====================
    public void TakeDamage(float amount)
    {
        if (state == State.Dead || state == State.DashingToGate) return;

        amount = Mathf.Abs(amount);
        if (amount <= 0f) amount = 1f;

        pendingDamage += amount;
        int appliedDamage = Mathf.FloorToInt(pendingDamage);
        if (appliedDamage <= 0) return;
        pendingDamage -= appliedDamage;

        hp -= appliedDamage;
        if (hp < 0) hp = 0;

        SpawnFollowLocal(breakAnimPoolTag, breakAnimPrefab, breakAnimLocalOffset);

        UpdateBossUI_HPOnly();
        TrySpawnHpThresholdAnims();

        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(CoHitShake());

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(CoFlashWholePrefab());

        if (hp <= 0)
            DieByPlayer();
    }

    public void TakeDamage(int amount)
    {
        TakeDamage((float)amount);
    }

    private void TrySpawnHpThresholdAnims()
    {
        if (maxHp <= 0) return;

        float ratio = (float)hp / (float)maxHp;

        if (!spawnedHp60Anim && ratio <= 0.60f)
        {
            spawnedHp60Anim = true;
            hp60AnimObj = SpawnFollowLocal(hp60AnimPoolTag, hp60AnimPrefab, hp60AnimLocalOffset);
        }

        if (!spawnedHp20Anim && ratio <= 0.20f)
        {
            spawnedHp20Anim = true;
            hp20AnimObj = SpawnFollowLocal(hp20AnimPoolTag, hp20AnimPrefab, hp20AnimLocalOffset);
        }
    }

    private IEnumerator CoHitShake()
    {
        if (visualRoot == null) yield break;

        float total = Mathf.Max(0.01f, shakeTotalDuration);

        Vector3 basePos = visualRoot.position;

        float t0 = total * 0.33f;
        float t1 = total * 0.33f;
        float t2 = total - t0 - t1;

        visualRoot.position = basePos + Vector3.left * shakeLeft;
        yield return new WaitForSeconds(t0);

        visualRoot.position = basePos + Vector3.right * shakeRight;
        yield return new WaitForSeconds(t1);

        visualRoot.position = basePos;
        if (t2 > 0f) yield return new WaitForSeconds(t2);
    }

    private void DieByPlayer()
    {
        if (state == State.Dead) return;

        state = State.Dead;
        SetBossUIVisible(false);
        StartCoroutine(CoDieByPlayerDelayed());
    }

    private IEnumerator CoDieByPlayerDelayed()
    {
        float delay = Mathf.Max(0f, deathByHpDelay);
        Vector3 startPos = transform.position;
        Vector3 endPos = new Vector3(startPos.x, deathByHpTargetY, startPos.z);

        if (shakeRoutine != null) { StopCoroutine(shakeRoutine); shakeRoutine = null; }

        Vector3 visualBaseLocal = Vector3.zero;
        bool canShakeVisual = (visualRoot != null);
        if (canShakeVisual)
            visualBaseLocal = visualRoot.localPosition;

        float t = 0f;
        float cycle = Mathf.Max(0.01f, deathByHpShakeCycle);
        while (t < delay)
        {
            float k = delay <= 0.0001f ? 1f : (t / delay);
            transform.position = Vector3.Lerp(startPos, endPos, k);

            if (canShakeVisual)
            {
                float phase = (t / cycle) * Mathf.PI * 2f;
                float s = Mathf.Sin(phase);
                float xOffset = (s >= 0f) ? (s * shakeRight) : (s * shakeLeft);
                visualRoot.localPosition = new Vector3(visualBaseLocal.x + xOffset, visualBaseLocal.y, visualBaseLocal.z);
            }

            t += Time.deltaTime;
            yield return null;
        }

        transform.position = endPos;
        if (canShakeVisual)
            visualRoot.localPosition = visualBaseLocal;

        Spawn(bossDestroyPoolTag, bossDestroyPrefab, transform.position, Quaternion.identity);

        SpawnFollowLocal(breakAnimPoolTag, breakAnimPrefab, breakAnimLocalOffset);

        EndRageModeImmediate();

        if (rightCapObj) SafeReturnOrDestroy(rightCapObj, bossCapRightPoolTag);
        if (leftCapObj) SafeReturnOrDestroy(leftCapObj, bossCapLeftPoolTag);
        rightCapObj = null;
        leftCapObj = null;

        gameObject.SetActive(false);
    }

    // ===================== TIMEOUT DASH =====================
    private IEnumerator CoTimeoutDash()
    {
        if (state == State.Dead) yield break;

        state = State.DashingToGate;
        SetBossUIVisible(false);

        Vector3 start = transform.position;
        Vector3 end = gateDashTargetWorldPos;

        float t = 0f;
        while (t < dashDuration)
        {
            float k = dashDuration <= 0.0001f ? 1f : (t / dashDuration);
            transform.position = Vector3.Lerp(start, end, k);
            t += Time.deltaTime;
            yield return null;
        }
        transform.position = end;

        Spawn(bossDestroyPoolTag, bossDestroyPrefab, transform.position, Quaternion.identity);

        SpawnFollowLocal(breakAnimPoolTag, breakAnimPrefab, breakAnimLocalOffset);
        EndRageModeImmediate();

        var gate = GateHealth.Instance;
        if (gate != null)
            gate.SendMessage("ForceBreakByBoss", SendMessageOptions.DontRequireReceiver);
        else if (GameData.Instance != null)
            GameData.Instance.TriggerGameOver();

        state = State.Dead;
        gameObject.SetActive(false);
    }

    // ===================== MOVE =====================
    private IEnumerator MoveToOverTime(Vector3 target, float time)
    {
        Vector3 start = transform.position;

        float t = 0f;
        while (t < time)
        {
            float k = time <= 0.0001f ? 1f : (t / time);
            transform.position = Vector3.Lerp(start, target, k);
            t += Time.deltaTime;
            yield return null;
        }
        transform.position = target;
    }

    // ===================== WHOLE PREFAB FLASH =====================
    private void CacheAllRenderers()
    {
        allRenderers = GetComponentsInChildren<Renderer>(true);
        originalColors.Clear();

        for (int i = 0; i < allRenderers.Length; i++)
        {
            var r = allRenderers[i];
            if (r == null) { originalColors.Add(null); continue; }

            var mats = r.materials;
            Color[] cols = new Color[mats.Length];

            for (int m = 0; m < mats.Length; m++)
                cols[m] = (mats[m] != null && mats[m].HasProperty("_Color")) ? mats[m].color : Color.white;

            originalColors.Add(cols);
        }
    }

    private void SetAllRenderersColor(Color c)
    {
        if (allRenderers == null || allRenderers.Length == 0) return;

        for (int i = 0; i < allRenderers.Length; i++)
        {
            var r = allRenderers[i];
            if (r == null) continue;

            var mats = r.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] != null && mats[m].HasProperty("_Color"))
                    mats[m].color = c;
            }
        }
    }

    private void RestoreOriginalColors()
    {
        if (allRenderers == null || originalColors == null) return;
        if (originalColors.Count != allRenderers.Length) return;

        for (int i = 0; i < allRenderers.Length; i++)
        {
            var r = allRenderers[i];
            if (r == null) continue;

            var cols = originalColors[i];
            if (cols == null) continue;

            var mats = r.materials;
            for (int m = 0; m < mats.Length && m < cols.Length; m++)
            {
                if (mats[m] != null && mats[m].HasProperty("_Color"))
                    mats[m].color = cols[m];
            }
        }
    }

    private IEnumerator CoFlashWholePrefab()
    {
        if (allRenderers == null || allRenderers.Length == 0 || originalColors.Count != allRenderers.Length)
            CacheAllRenderers();

        SetAllRenderersColor(hitColor);
        yield return new WaitForSeconds(hitFlashDuration);
        RestoreOriginalColors();
    }

    // ===================== SPAWN / RETURN =====================
    private GameObject Spawn(string poolTag, GameObject prefab, Vector3 pos, Quaternion rot)
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            return ObjectPool.Instance.SpawnFromPool(poolTag, pos, rot);

        if (prefab != null)
            return Instantiate(prefab, pos, rot);

        return null;
    }

    private GameObject SpawnFollowLocal(string poolTag, GameObject prefab, Vector3 localOffset)
    {
        GameObject obj;

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            obj = ObjectPool.Instance.SpawnFromPool(poolTag, transform.position, Quaternion.identity);
        else if (prefab != null)
            obj = Instantiate(prefab, transform.position, Quaternion.identity);
        else
            return null;

        if (obj == null) return null;

        if (parentAnimsToBoss)
        {
            obj.transform.SetParent(transform, worldPositionStays: false);
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;
            obj.transform.localPosition = localOffset;
        }
        else
        {
            obj.transform.position = transform.TransformPoint(localOffset);
        }

        return obj;
    }

    private void SafeReturnOrDestroy(GameObject obj, string poolTag)
    {
        if (!obj) return;

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, obj);
        else
            Destroy(obj);
    }

    private void ClearThresholdAnimObjects()
    {
        if (hp60AnimObj != null)
            SafeReturnOrDestroy(hp60AnimObj, hp60AnimPoolTag);
        if (hp20AnimObj != null)
            SafeReturnOrDestroy(hp20AnimObj, hp20AnimPoolTag);

        hp60AnimObj = null;
        hp20AnimObj = null;
    }

    private void HandleGameOver()
    {
        if (!gameObject.activeInHierarchy) return;

        SetBossUIVisible(false);
        EndRageModeImmediate();

        if (mainRoutine != null) { StopCoroutine(mainRoutine); mainRoutine = null; }
        if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }
        if (shakeRoutine != null) { StopCoroutine(shakeRoutine); shakeRoutine = null; }
        if (gameOverHideRoutine != null) { StopCoroutine(gameOverHideRoutine); gameOverHideRoutine = null; }

        RestoreOriginalColors();
        ClearThresholdAnimObjects();
        state = State.Dead;
    }

    private void ClearBossProjectilesNow()
    {
        if (ObjectPool.Instance != null)
            ObjectPool.Instance.ReturnAllActive(bossMissilePoolTag, bossRageMissilePoolTag);

        var missiles = FindObjectsByType<BossMissile>(FindObjectsSortMode.None);
        for (int i = 0; i < missiles.Length; i++)
        {
            var m = missiles[i];
            if (m == null || !m.gameObject.scene.IsValid()) continue;
            if (m.usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(m.poolTag) && ObjectPool.Instance.HasPool(m.poolTag))
                ObjectPool.Instance.ReturnToPool(m.poolTag, m.gameObject);
            else
                Destroy(m.gameObject);
        }

        var rageMissiles = FindObjectsByType<BossRageMissile>(FindObjectsSortMode.None);
        for (int i = 0; i < rageMissiles.Length; i++)
        {
            var m = rageMissiles[i];
            if (m == null || !m.gameObject.scene.IsValid()) continue;
            if (m.usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(m.poolTag) && ObjectPool.Instance.HasPool(m.poolTag))
                ObjectPool.Instance.ReturnToPool(m.poolTag, m.gameObject);
            else
                Destroy(m.gameObject);
        }
    }

    public void Hit(int damage)
    {
        TakeDamage(damage);
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
}
