using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Player : MonoBehaviour
{
    // 0=Player1, 1=Player2, 2=Player3, 3=Player4, 4=Player5
    private const int T0 = 0, T1 = 1, T2 = 2, T3 = 3, T4 = 4, T5 = 5;

    [Header("Bomb Launcher (Mana 5 trigger)")]
    public BombLauncher bombLauncher;   // 인스펙터 연결
    public int manaForLauncher = 7;

    private int manaCount = 0;



    [Header("Blood FX")]
    public GameObject bloodNormal;
    public GameObject bloodMissile;
    public GameObject bloodSaw;
    public GameObject bloodHill;

    [Header("Jump Settings")]
    public float jumpSpeed = 8f;

    [Header("Player Info")]

    private int level;
    private PlayerStats stats;

    [Header("Hitboxes")]
    public GameObject hitboxPrefabP1;
    public GameObject hitboxPrefabP3;
    public GameObject hitboxPrefabP5;

    [Header("Type5 (Stomp)")]
    public float stompSpeed = -18f;
    public float landDuration = 0f;
    // Jump 타임 기록
    private float lastJumpTime = -999f;

    [Header("Type3 (Dash Attack)")]
    private float p3NormalAttackDistance = 3.3f;
    private float p3NormalAttackDuration = 0.15f;
    public float p3NormalAttackHitDuration = 0.15f;

    [Header("Smoke Prefabs")]
    public GameObject smokeEffectPrefab;    // 기본
    public GameObject smokeEffectPrefabP3;  // Player3 전용
    public GameObject smokeEffectPrefabP4;  // Player4 전용
    public GameObject smokeEffectPrefabP5;  // Player5 전용

    [Header("Effect Pooling")]
    public bool useEffectPool = true;
    public string projectilePoolTag = "ProjectileBall";
    public string dummyProjectilePoolTag = "DummyBallP4";
    public string lightningPoolTag = "P4LightningNormal";
    public string p4RageLightningPoolTag = "P4LightningRage";
    public string p2ProjectileSmokePoolTag = "P2ProjectileSmoke";
    public string smokePoolTag = "Smoke";
    public string smokeP3PoolTag = "Smoke";
    public string smokeP4PoolTag = "Smoke";
    public string smokeP5PoolTag = "Smoke";
    public string floorSmokePoolTag = "Smoke";
    public string rage24SmokePoolTag = "p24ragesmoke";
    public string rage1SmokePoolTag = "p1ragesmoke";
    public string rage3SmokePoolTag = "p3ragesmoke";
    public string rageSmokePoolTag = "transformSmoke";
    public string rageSmokePoolTagSecondary = "transformSmoke";
    public string rageAttackSmokePoolTag = "p5ragesmoke";
    public string hitboxP1PoolTag = "PlayerHitboxP1";
    public string hitboxP3PoolTag = "PlayerHitboxP3";
    public string hitboxP5PoolTag = "PlayerHitboxP5";
    public string p2RageLaserPoolTag = "P2RageLaser";
    public string speedEffectPoolTag = "SpeedEffect";

    [Header("Stats")]
    public int lives = 3;

    [Header("References")]
    public Rigidbody2D rb;
    public Animator anim;

    [Header("Walk Animation Speed")]
    public float baseWalkAnimSpeed = 1f;
    public bool useAbsoluteWalkSpeedMultiplier = true;
    public float walkAnimSpeedSmooth = 0f;

    [Header("Spawn Intro")]
    public float gameplayX = -3.25f;
    public Vector2 introAppearPosition = new Vector2(-1.8f, 1.1f);
    public float introTotalDuration = 1.5f;
    public float introStartDelay = 0.2f;
    public float introGateLeadTime = 0.15f;
    public float introDiagonalXSpeed = 3.5f;
    public float introClimbYSpeed = -2f;
    public float introMinDiagonalTime = 0.2f;
    public float introGroundProbeDistance = 3f;
    public bool closeGateAfterIntro = true;

    private bool isRageMode = false;
    private float rageEndTime = 0f;
    [Header("Rage Prefabs")]
    public GameObject p2RageLaserPrefab;   // Player2 분노 레이저
    public GameObject rage24SmokePrefab;
    public GameObject p4RageLightningPrefab; // Player4 분노 번개
    public GameObject rage1SmokePrefab;
    public GameObject rage3SmokePrefab;
    public Vector2 p5RageBrickSpawnWorldPos = new Vector2(4.65f, -2.42f);
    public float p5RageBrickSpawnZ = -0.5f;

    private int originalAttack;
    private int originalHealth;

    [Header("Rage Settings")]
    public float rageDuration;
    public GameObject rageSmokePrefab;
    public GameObject rageSmokePrefabSecondary;
    public GameObject rageAttackSmokePrefab;
    public Vector2 rageTransformHitboxSize = new Vector2(15f, 2f);
    public Vector2 rageTransformHitboxOffset = Vector2.zero;
    public Vector3 rageSmokeOffset = Vector3.zero;
    public Vector3 rageSmokeOffsetSecondary = Vector3.zero;

    [Header("Rage Speed Effect")]
    public GameObject speedEffectPrefab;
    public Vector2[] speedEffectSpawnWorldPositions = new Vector2[]
    {
        new Vector2(27f, -3.57f),
        new Vector2(27f, 4.27f)
    };
    public float speedEffectMoveSpeed = 10f;
    public float speedEffectSpawnInterval = 0.2f;
    public float speedEffectDespawnX = -20f;

    private float lastStompTime = -999f;

    private string lastGroundTag = null;
    private Collider2D lastGroundCol = null;
    bool attackQueued = false;
    private float attackQueuedTime = 0f;   // 공격키 예약 시각 기록
    private float attackQueueWindow = 0.2f;
    private bool p3NormalAttackQueued = false;
    private float p3ChainAttackWindow = 0.35f;
    private float p3LastAttackEndTime = -999f;
    private bool needRebind = false;
    private bool isTransformLock = false;
    private float transformLockEndTime = 0f;
    public float transformLockDuration = 0.9f;

    // ===== Ranged (Player2/Player4) =====
    [Header("P2/P4 Prefabs & VFX")]
    public ZigzagLightning lightningPrefab;       // P4 번개 프리팹 (ZigzagLightning)
    public ProjectileBall dummySmokeBallPrefab;   // P4 바닥 스모크용 더미 공 (데미지 0)
    public GameObject floorSmokePrefab;           // 바닥에 터질 때 쓸 스모크(없으면 기존 smokeEffectPrefab 사용)

    // 발사 각도/속도 공통 파라미터
    [Header("Ranged Tunables")]
    public float projectileSpeed = 20f; // P2 공 속도(요청: 20)
    public float dummyProjectileSpeed = 40f; 
    public float angle45Deg = 45f;      // 45도
    private float fireRecovery = 0.25f;
    



    // ===== Ranged (Player2/Player4) =====
    [Header("Ranged (Player2/4)")]
    public Transform p2Muzzle;                 // Player2 발사 위치
    public ProjectileBall projectilePrefab;     // Player2 발사체 프리팹
    public Transform p4Emitter;                // Player4 전기 시작점
    private int hp;     // 현재 체력
    private int maxHp;  // 최대 체력
    private bool rageWarningBlinkPlayed = false;
    private bool isLanding = false;
    private float lastGroundedTime = -999f;
    private float attackStateStartTime = -999f;
    private bool isSpawnIntroActive = false;
    private Coroutine spawnIntroRoutine;
    private bool jumpedThisAirborne = false;
    private Coroutine rangedFlyattackLatchRoutine;
    private Coroutine rageRangedGroundAttackRoutine;
    private const float rageRangedAttackAnimDuration = 0.5f;


    [Header("Obstacle SlowMo Reposition")]
    public float slowToReverseDuration = 1f;   // 6 -> -2 (보간 1초)
    private float reverseMagnitude = 0.8f;        // ★ 리버스 속도 배수(0.8배)
    public float baseMoverSpeed = 6f;          // Mover.baseSpeed
    private float backToNormalDuration = 2f;    // -2 -> +1 (1초)
    public float resumeDelay = 0f;
    public float holdDuration = 0.5f;            // ★ 고정 시간 1초

    private bool slowmoBusy = false;           // 진행 중 재진입 금지
    private int firstHitDir = 0;              // ★ 첫 충돌 방향 저장(-1: 아래로 이동, +1: 위로 이동)
    private ObstacleType lastHitObstacleType = ObstacleType.Normal;


    

    // 상태
    private bool isGrounded = true;
    private bool isAttacking = false;
    public bool isDead = false;
    private bool hasLanded = false;
    private bool isInvincible = false;

    private string currentAnim = "";
    private float cachedGravity = 1f;
    private float currentWalkAnimSpeed = 1f;
    private SpriteRenderer sr;
    private Collider2D bodyCollider;
    private RigidbodyConstraints2D p3AttackOriginalConstraints;
    private bool p3AttackPhysicsLocked = false;
    public HealthBarUI bar;

    [Header("Obstacle Bump")]
    public float bumpVelocityY = 4f;      // 위/아래로 확 튀기는 속도
    public float bumpDistance = 0.25f;     // 보장 이동 거리
    public float bumpDuration = 0.10f;    // 이 거리만큼 이동에 걸리는 시간
    private float lastBumpTime = -999f;
    private bool justRecoveredFromRage = false;
    public GameObject jumpParticleObject;
    public GameObject jumpParticleObject2;
    [Header("Jump Particle Materials")]
    public Material normalJumpParticleMaterial;
    public Material rageJumpParticleMaterial;
    private bool isInHurtWindow = false;
    private int obstacleTouchCount = 0;
    
    
    public static Player Instance { get; private set; }
    private bool effectPoolsPrewarmed = false;
    private GameObject rageTransformColliderObj;
    private BoxCollider2D rageTransformBox;
    private Hitbox rageTransformHitbox;
    private GameObject activeRageSmokePrimary;
    private GameObject activeRageSmokeSecondary;
    private readonly HashSet<int> activeHazardContactIds = new HashSet<int>();
    private readonly Dictionary<int, ObstacleType> cachedObstacleTypes = new Dictionary<int, ObstacleType>(64);
    private float nextSpeedEffectSpawnTime = -1f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!anim) anim = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();
        cachedGravity = rb.gravityScale;
        currentWalkAnimSpeed = baseWalkAnimSpeed;
        anim.speed = baseWalkAnimSpeed;
        SetupRageTransformCollider();
        ApplyJumpParticleMaterial(isRageMode);

        EnsureEffectPoolTagIsolation();
        PrewarmEffectPools();
    }

    void Update()
    {
        if (isDead) return;
        if (isSpawnIntroActive)
        {
            UpdateWalkAnimationSpeed();
            return;
        }
        if (isRageMode)
        {
            if (isTransformLock && Time.time >= transformLockEndTime)
            {
                isTransformLock = false;
                ApplyLocomotionAnimation();
            }
            // ✅ 분노 끝나기 2초 전에만 1번 실행
            if (!rageWarningBlinkPlayed && rageEndTime - Time.time <= 1.2f)
            {
                StartCoroutine(BlinkRageWarningEffect());
                rageWarningBlinkPlayed = true;
            }

            // ✅ 분노 끝났을 때
            if (Time.time >= rageEndTime)
            {
                isRageMode = false;
                rageWarningBlinkPlayed = false;
                StopRageSpeedEffect();
                ClearRageTransformSmokes();
                ApplyJumpParticleMaterial(false);

                stats.attack = originalAttack;
                lives = 3;
                if (bar != null)
                    bar.SetHealth(hp);

                var animOverride = GetComponent<PlayerAnimOverride>();
                if (animOverride != null)
                {
                    animOverride.SetRageMode(false);
                    animOverride.ApplyOverrides(force: true);
                }

                anim.ResetTrigger("Jump");
                anim.ResetTrigger("Attack");
                anim.ResetTrigger("Flyattack");
                anim.ResetTrigger("Land");
                anim.ResetTrigger("Walk");
                anim.ResetTrigger("Still");

                anim.Rebind();
                anim.Update(0f);
                currentAnim = "";

                justRecoveredFromRage = true;
                EndP3AttackPhysicsLock();
                isAttacking = false;
                isLanding = false;
                hasLanded = false;
                attackQueued = false;
                isInvincible = false;
            }
            else
            {
                TrySpawnRageSpeedEffect();
            }
        }

        if (isDead) return;
        SyncGroundedState();
        ResolveRageRangedGroundedFlyattack();
        ResolvePostRageAnimationRecovery();

        HandleMovement();
        HandleJump();
        HandleAttackInput();
        HandleLandSequencing();
        RecoverStuckGroundAttack();
        UpdateWalkAnimationSpeed();
    }

    void LateUpdate()
    {
        SyncRageTransformSmokes();
    }
    private void ChangeAnimation(string triggerName)
    {
        if (isTransformLock && triggerName != "Die" && triggerName != "Transform")
            return;
        if (isDead && triggerName != "Die") return;

        if (isInHurtWindow && currentAnim == "Hurt" && triggerName != "Die")
    return;

        ReleaseRangedFlyattackState(triggerName);

        if (currentAnim == triggerName && currentAnim != "Hurt" && IsAnimatorInLogicalState(triggerName))
            return;

        if (triggerName != "Walk")
            ResetWalkAnimationSpeedImmediate();

        string animatorTrigger = triggerName;

        if (HasParam(anim, animatorTrigger))
        {
            if (animatorTrigger != "Attack")
                anim.ResetTrigger("Attack");
            if (animatorTrigger != "Flyattack")
                anim.ResetTrigger("Flyattack");

            anim.ResetTrigger("Jump");
            anim.ResetTrigger("Land");
            anim.ResetTrigger("Walk");
            anim.ResetTrigger("Still");
            anim.ResetTrigger("Hurt");

            anim.SetTrigger(animatorTrigger);
            currentAnim = triggerName;
        }

        UpdateJumpParticleEmission(triggerName);
    }

    private bool IsAnimatorInLogicalState(string logicalName)
    {
        if (anim == null) return false;

        AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
        return logicalName switch
        {
            "Walk" => st.IsName("Base Walk"),
            "Jump" => st.IsName("Base Jump"),
            "Still" => st.IsName("Base Still"),
            "Attack" => st.IsName("Base Attack"),
            "Flyattack" => st.IsName("Base Flyattack"),
            "Land" => st.IsName("Base Land"),
            "Transform" => st.IsName("Base Transform"),
            "Die" => st.IsName("Base Die"),
            "Hurt" => st.IsName("Base Hurt"),
            _ => false,
        };
    }

    private bool IsAnimatorShowingLogicalState(string logicalName)
    {
        if (anim == null) return false;
        if (IsAnimatorStateLogical(anim.GetCurrentAnimatorStateInfo(0), logicalName))
            return true;

        return anim.IsInTransition(0) &&
               IsAnimatorStateLogical(anim.GetNextAnimatorStateInfo(0), logicalName);
    }

    private bool IsAnimatorStateLogical(AnimatorStateInfo st, string logicalName)
    {
        return logicalName switch
        {
            "Walk" => st.IsName("Base Walk"),
            "Jump" => st.IsName("Base Jump"),
            "Still" => st.IsName("Base Still"),
            "Attack" => st.IsName("Base Attack"),
            "Flyattack" => st.IsName("Base Flyattack"),
            "Land" => st.IsName("Base Land"),
            "Transform" => st.IsName("Base Transform"),
            "Die" => st.IsName("Base Die"),
            "Hurt" => st.IsName("Base Hurt"),
            _ => false,
        };
    }

    private void ForceAnimationState(string stateName, string logicalName)
    {
        if (anim == null) return;

        ReleaseRangedFlyattackState(logicalName);

        if (logicalName != "Walk")
            ResetWalkAnimationSpeedImmediate();

        anim.ResetTrigger("Jump");
        anim.ResetTrigger("Attack");
        anim.ResetTrigger("Flyattack");
        anim.ResetTrigger("Land");
        anim.ResetTrigger("Walk");
        anim.ResetTrigger("Still");
        anim.ResetTrigger("Hurt");

        anim.Play(stateName, 0, 0f);
        currentAnim = logicalName;
        UpdateJumpParticleEmission(logicalName);
    }

    private void UpdateJumpParticleEmission(string triggerName)
    {
        // 🔻 파티클 제어
        if (jumpParticleObject != null || jumpParticleObject2 != null)
        {
            var ps = jumpParticleObject != null ? jumpParticleObject.GetComponent<ParticleSystem>() : null;
            var ps1 = jumpParticleObject2 != null ? jumpParticleObject2.GetComponent<ParticleSystem>() : null;
            bool normalP3Attack = GameData.Instance != null &&
                                  GameData.Instance.selectedPlayerType == T3 &&
                                  !isRageMode &&
                                  (triggerName == "Attack" || triggerName == "Flyattack");
            bool enabled = !normalP3Attack && (triggerName == "Jump" || triggerName == "Flyattack");

            if (ps != null)
            {
                var emission = ps.emission;
                emission.enabled = enabled;
            }

            if (ps1 != null)
            {
                var emission1 = ps1.emission;
                emission1.enabled = enabled;
            }
        }
    }

    private void ApplyJumpParticleMaterial(bool rageActive)
    {
        Material targetMaterial = rageActive ? rageJumpParticleMaterial : normalJumpParticleMaterial;
        if (targetMaterial == null)
            return;

        ApplyJumpParticleMaterial(jumpParticleObject, targetMaterial);
        ApplyJumpParticleMaterial(jumpParticleObject2, targetMaterial);
    }

    private static void ApplyJumpParticleMaterial(GameObject particleObject, Material material)
    {
        if (particleObject == null || material == null)
            return;

        var renderer = particleObject.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
            return;

        if (renderer.sharedMaterial == material)
            return;

        renderer.sharedMaterial = material;
    }

    private void UpdateWalkAnimationSpeed()
    {
        if (anim == null) return;

        if (currentAnim != "Walk" || isDead)
        {
            ResetWalkAnimationSpeedImmediate();
            return;
        }

        float mult = 1f;
        if (GameData.Instance != null)
            mult = GameData.Instance.GetStageSpeedMult();

        if (useAbsoluteWalkSpeedMultiplier)
            mult = Mathf.Abs(mult);

        float targetSpeed = baseWalkAnimSpeed * mult;

        if (walkAnimSpeedSmooth > 0f)
            currentWalkAnimSpeed = Mathf.Lerp(currentWalkAnimSpeed, targetSpeed, 1f - Mathf.Exp(-walkAnimSpeedSmooth * Time.deltaTime));
        else
            currentWalkAnimSpeed = targetSpeed;

        anim.speed = currentWalkAnimSpeed;
    }

    private void ResetWalkAnimationSpeedImmediate()
    {
        if (anim == null) return;
        if (Mathf.Approximately(currentWalkAnimSpeed, baseWalkAnimSpeed) && Mathf.Approximately(anim.speed, baseWalkAnimSpeed))
            return;

        currentWalkAnimSpeed = baseWalkAnimSpeed;
        anim.speed = baseWalkAnimSpeed;
    }

    private static bool HasParam(Animator a, string name,
        AnimatorControllerParameterType t = AnimatorControllerParameterType.Trigger)
    {
        foreach (var p in a.parameters) if (p.type == t && p.name == name) return true;
        return false;
        }

    private bool IsRangedPlayerType()
    {
        if (GameData.Instance == null)
            return false;

        int t = GameData.Instance.selectedPlayerType;
        return t == T2 || t == T4;
    }

    private bool IsRageRangedPlayerType()
    {
        return isRageMode && IsRangedPlayerType();
    }

    private bool IsGroundedForRangedAttackNow()
    {
        bool risingFromJump = rb != null &&
                              Time.time - lastJumpTime < 0.08f &&
                              rb.linearVelocity.y > 0.05f;
        if (risingFromJump)
            return false;

        if (isGrounded)
            return true;

        if (!TryGetGroundSurface(out var groundCol))
            return false;

        isGrounded = true;
        jumpedThisAirborne = false;
        lastGroundedTime = Time.time;
        if (groundCol != null)
        {
            lastGroundCol = groundCol;
            lastGroundTag = groundCol.tag;
        }

        return true;
    }

    private bool ShouldUseRageRangedGroundAttack()
    {
        return isGrounded ||
               currentAnim == "Attack" ||
               IsAnimatorShowingLogicalState("Attack") ||
               IsGroundedForRangedAttackNow();
    }

    private void PlayRageRangedAttackAnimation(bool groundedAttack)
    {
        StopRangedFlyattackLatch();

        if (rageRangedGroundAttackRoutine != null)
            StopCoroutine(rageRangedGroundAttackRoutine);

        isAttacking = true;
        attackStateStartTime = Time.time;

        if (groundedAttack)
            ForceAnimationState("Base Attack", "Attack");
        else
            ChangeAnimation("Flyattack");

        rageRangedGroundAttackRoutine = StartCoroutine(FinishRageRangedAttackAnimation());
    }

    private void StopRageRangedGroundAttackTimer()
    {
        if (rageRangedGroundAttackRoutine == null)
            return;

        StopCoroutine(rageRangedGroundAttackRoutine);
        rageRangedGroundAttackRoutine = null;
    }

    private IEnumerator FinishRageRangedAttackAnimation()
    {
        yield return new WaitForSeconds(rageRangedAttackAnimDuration);
        rageRangedGroundAttackRoutine = null;

        if (isDead || !IsRageRangedPlayerType())
            yield break;

        isAttacking = false;
        attackStateStartTime = -999f;

        if (IsGroundedForRangedAttackNow())
            ChangeAnimation("Walk");
        else
            ChangeAnimation("Jump");
    }

    private void ResolveRageRangedGroundedFlyattack()
    {
        if (!IsRageRangedPlayerType() || !IsGroundedForRangedAttackNow())
            return;

        bool showingFlyattack = currentAnim == "Flyattack" || IsAnimatorShowingLogicalState("Flyattack");
        if (!showingFlyattack)
            return;

        StopRangedFlyattackLatch();

        if (isAttacking || Input.GetKey(KeyCode.DownArrow))
        {
            PlayRageRangedAttackAnimation(true);
            return;
        }

        StopRageRangedGroundAttackTimer();
        isAttacking = false;
        attackStateStartTime = -999f;
        ForceAnimationState("Base Walk", "Walk");
    }

    private void BeginRangedFlyattackLatch()
    {
        if (!IsRangedPlayerType())
            return;

        if (rangedFlyattackLatchRoutine != null)
            return;

        rangedFlyattackLatchRoutine = StartCoroutine(HoldRangedFlyattack());
    }

    private void StopRangedFlyattackLatch()
    {
        if (rangedFlyattackLatchRoutine == null)
            return;

        StopCoroutine(rangedFlyattackLatchRoutine);
        rangedFlyattackLatchRoutine = null;
    }

    private void ReleaseRangedFlyattackState(string nextTriggerName)
    {
        if (nextTriggerName == "Flyattack")
            return;

        bool leavingRangedFlyattack =
            IsRangedPlayerType() &&
            isAttacking &&
            currentAnim == "Flyattack";

        StopRangedFlyattackLatch();

        if (!leavingRangedFlyattack)
            return;

        if (nextTriggerName == "Attack")
            return;

        isAttacking = false;
        attackStateStartTime = -999f;
    }

    private IEnumerator HoldRangedFlyattack()
    {
        while (!isDead && isAttacking && !isGrounded && currentAnim == "Flyattack" && IsRangedPlayerType())
            yield return null;

        rangedFlyattackLatchRoutine = null;

        if (isDead || !isAttacking || !IsRangedPlayerType())
            yield break;

        if (!isGrounded || currentAnim != "Attack")
            yield break;

        yield return new WaitForSeconds(fireRecovery);

        if (!isDead && isAttacking && isGrounded && currentAnim == "Attack" && IsRangedPlayerType())
        {
            isAttacking = false;
            attackStateStartTime = -999f;
            ChangeAnimation("Walk");
        }
    }

    private void HandleMovement()
    {
        if (isTransformLock) return;
        if (justRecoveredFromRage) return;
        if (isDead) return;
        if (GameData.Instance.selectedPlayerType == T3 && isAttacking) return;

        transform.position = new Vector3(gameplayX, transform.position.y, transform.position.z);

        if (!isGrounded)
        {
            if (!isAttacking && !isLanding && currentAnim != "Hurt" && currentAnim != "Land")
                ChangeAnimation(GetAirborneAnimationTrigger());
            return;
        }

        if (isGrounded && !isAttacking && !isLanding)
        {
            ChangeAnimation("Walk");
        }

        if (!isGrounded) return;
        if (isLanding) return;
        if (currentAnim == "Hurt") return;
        if (currentAnim == "Land") return;

        // T2/T4는 공격 중에는 Walk로 덮어쓰지 않음
        if (GameData.Instance.selectedPlayerType == T2 || GameData.Instance.selectedPlayerType == T4)
        {
            if (isAttacking || currentAnim == "Attack" || currentAnim == "Flyattack")
                return;

            ChangeAnimation("Walk");
            return;
        }

        // T3는 기존처럼 착지 후 바로 Walk 복귀 유지
        if (GameData.Instance.selectedPlayerType == T3)
        {
            ChangeAnimation("Walk");
            return;
        }

        // 🎯 나머지 플레이어는 착지 직후 0.1초 슬라이드 보호
        //if (Time.time - lastGroundedTime < 0.01f) return;

        if (currentAnim == "Jump") return;
        if (currentAnim == "Attack") return;
        if (currentAnim == "Flyattack") return;
        if (currentAnim == "Land") return;
        ChangeAnimation("Walk");
    }

    private void ResolvePostRageAnimationRecovery()
    {
        if (!justRecoveredFromRage || anim == null) return;
        ApplyLocomotionAnimation();

        justRecoveredFromRage = false;
    }

    private void ApplyLocomotionAnimation()
    {
        if (anim == null) return;

        if (!isGrounded)
        {
            string airborneTrigger = GetAirborneAnimationTrigger();
            if (airborneTrigger == "Still")
                ForceAnimationState("Base Still", "Still");
            else
                ForceAnimationState("Base Jump", "Jump");
            return;
        }

        ForceAnimationState("Base Walk", "Walk");
    }

    // ---------- 점프 ----------
    private void HandleJump()
    {
        if (!isGrounded && Input.GetKey(KeyCode.DownArrow)) return;
        if (Input.GetKey(KeyCode.Space))
        {
            justRecoveredFromRage = false;
            int t = GameData.Instance.selectedPlayerType;

            // Player3 노멀 공격 중에는 Space 유지로 점프가 다시 걸리지 않게 막는다.
            // Rage 상태에서는 P2/P4처럼 공격 중 점프를 허용한다.
            if (t == T3 && isAttacking && !isRageMode)
                return;

            if (isAttacking && !isLanding)
            {
                // Player2,4 + Player3(Rage)는 공격 중에도 점프 허용
                if (t != T2 && t != T4 && !(isRageMode && t == T3))
                    return;
            }
            if (!isTransformLock && currentAnim != "Jump")
                ChangeAnimation("Jump");

            rb.gravityScale = 0.5f;
            int x = GameData.Instance.selectedPlayerType;

            float js = jumpSpeed;
            if (isRageMode && (x == T1 || x == T5))
                js *= 1.2f;

            rb.linearVelocity = new Vector2(rb.linearVelocity.x, js);

            isGrounded = false;
            lastJumpTime = Time.time;
            jumpedThisAirborne = true;
        }
        else
        {
            rb.gravityScale = cachedGravity;

            // 스페이스 키를 뗄 때 예약 공격 실행
            if (Input.GetKeyUp(KeyCode.Space) && attackQueued)
            {
                if (Time.time - attackQueuedTime <= attackQueueWindow)
                {
                    attackQueued = false;

                    int t = GameData.Instance.selectedPlayerType;

                    // ✅ 1️⃣ 분노 모드 공격을 최우선으로 실행
                    if (isRageMode)
                    {
                        switch (t)
                        {
                            case T2: StartCoroutine(P2_RageAttack()); return;
                            case T3: StartCoroutine(P3_RageAttack()); return; // 🔹 스톰프 대신 제자리 공격
                            case T4: StartCoroutine(P4_RageAttack()); return;
                        }
                    }

                    // ✅ 2️⃣ 일반 공격 (P2/P4는 원거리, 나머지는 공중 공격)
                    if (t == T2) { StartCoroutine(P2_FireRanged()); return; }
                    if (t == T4) { StartCoroutine(P4_FireLightning()); return; }

                    // ✅ 3️⃣ 나머지 (1,3,5): 공중 스톰프
                    if (!isGrounded)
                    {
                        switch (t)
                        {
                            case T1: StartCoroutine(P1_AirStomp()); break;
                            case T3: StartCoroutine(P3_AirStomp()); break;
                            case T5: StartCoroutine(P5_AirStomp()); break;
                        }
                    }
                }
                else attackQueued = false;
            }
        }
    }

    // ===================== Player2: 공 발사(45°, 속도 20) =====================
    private IEnumerator P2_FireRanged()
    {
        isAttacking = true;
        bool airborneAttack = !isGrounded;

        // 애니: 지상=Attack, 공중=Flyattack
        ChangeAnimation(airborneAttack ? "Flyattack" : "Attack");

        // 진행 방향 기준 45° 하향
        float dirX = (sr != null && sr.flipX) ? -1f : +1f;
        Vector2 shootDir = Quaternion.Euler(0, 0, -angle45Deg) * new Vector2(dirX, 0f);
        shootDir.Normalize();

        if (projectilePrefab && p2Muzzle)
        {
            bool fromPool;
            var proj = SpawnWithPool(projectilePrefab.gameObject, projectilePoolTag, p2Muzzle.position, Quaternion.identity, out fromPool, 10)
                ?.GetComponent<ProjectileBall>();
            if (proj != null)
            {
                proj.ConfigurePooling(fromPool, projectilePoolTag);
                proj.damage = 1;
                proj.smokePrefab = smokeEffectPrefab; // 바닥 충돌 시 스모크
                proj.ConfigureSmokePooling(p2ProjectileSmokePoolTag);
                proj.SetOwner(this);
                proj.Fire(p2Muzzle.position, shootDir, projectileSpeed); // 20
            }
        }

        if (airborneAttack)
        {
            BeginRangedFlyattackLatch();
            yield break;
        }

        yield return new WaitForSeconds(fireRecovery);
        isAttacking = false;
        if (isGrounded) ChangeAnimation("Walk");
    }


    // ===================== Player4: 번개(정지 + 45° 회전) + 더미공(45° 빠르게) =====================
    private IEnumerator P4_FireLightning()
    {
        isAttacking = true;
        bool airborneAttack = !isGrounded;
        ChangeAnimation(airborneAttack ? "Flyattack" : "Attack");

        bool left = (sr != null && sr.flipX);
        Vector2 down45 = Aim45(left, down: true); // 보이는 각도 45°(아래 대각선)

        // 1) 번개: "그 자리"에 생성 + 각도만 45° 로테이션
        if (lightningPrefab && p4Emitter)
        {
            bool fromPool;
            var zig = SpawnWithPool(lightningPrefab.gameObject, lightningPoolTag, p4Emitter.position, Quaternion.identity, out fromPool, 8)
                ?.GetComponent<ZigzagLightning>();
            if (zig != null)
            {
                zig.ConfigurePooling(fromPool, lightningPoolTag);
                zig.damage = stats.attack;
                zig.SetOrientation(down45);   // ← 각도만 설정, 이동 없음
            }
        }

        // 2) 바닥 스모크용 더미공: 45°로 "빠르게" 날아감
        if (dummySmokeBallPrefab && p4Emitter)
        {
            bool fromPool;
            var dummy = SpawnWithPool(dummySmokeBallPrefab.gameObject, dummyProjectilePoolTag, p4Emitter.position, Quaternion.identity, out fromPool, 10)
                ?.GetComponent<ProjectileBall>();
            if (dummy != null)
            {
                dummy.ConfigurePooling(fromPool, dummyProjectilePoolTag);
                dummy.damage = 0; // 데미지 없음(스모크용)
                // P4 노멀 공격 바닥 스모크는 전용 스모크를 우선 사용
                GameObject p4Smoke = smokeEffectPrefabP4 ? smokeEffectPrefabP4 : smokeEffectPrefab;
                string p4SmokeTag = smokeEffectPrefabP4 ? smokeP4PoolTag : smokePoolTag;
                dummy.smokePrefab = p4Smoke;
                dummy.ConfigureSmokePooling(p4SmokeTag);
                dummy.SetOwner(this);
                dummy.Fire(p4Emitter.position, down45, dummyProjectileSpeed); // ← 더 빠르게(예: 28~32)
            }
        }

        if (airborneAttack)
        {
            BeginRangedFlyattackLatch();
            yield break;
        }

        yield return new WaitForSeconds(fireRecovery);
        isAttacking = false;
        if (isGrounded) ChangeAnimation("Walk");
    }

    // 클래스 내부 아무 곳(예: 다른 메서드들 위/아래)에 추가
    private Vector2 Aim45(bool left, bool down)
    {
        const float s = 0.70710678f; // √0.5
        float x = left ? -s : s;
        float y = down ? -s : s;
        return new Vector2(x, y);
    }

    private void HandleAttackInput()
    {
        if (!Input.GetKeyDown(KeyCode.DownArrow)) return;
        int t = GameData.Instance.selectedPlayerType;

        // 1) Rage 우선 처리
        if (isRageMode)
        {
            switch (t)
            {
                case T2:
                    if (currentAnim == "Flyattack")
                        ResolveRageRangedGroundedFlyattack();
                    StartCoroutine(P2_RageAttack());
                    return;
                case T3:
                    StartCoroutine(P3_RageAttack());
                    return;
                case T4:
                    if (currentAnim == "Flyattack")
                        ResolveRageRangedGroundedFlyattack();
                    StartCoroutine(P4_RageAttack());
                    return;
            }
        }

        // 2) P2/P3/P4는 Space 눌러도 상관없이 즉시 공격 (중요)
        if (t == T2)
        {
            StartCoroutine(P2_FireRanged());
            return;
        }
        if (t == T3)
        {
            if (isAttacking)
            {
                p3NormalAttackQueued = true;
                return;
            }
            bool chainedAttack = !isGrounded && Time.time - p3LastAttackEndTime <= p3ChainAttackWindow;
            StartCoroutine(P3_AirStomp(chainedAttack));
            return;
        }
        if (t == T4)
        {
            StartCoroutine(P4_FireLightning());
            return;
        }

        if (IsStompPlayerType(t) && isGrounded)
        {
            if (Input.GetKey(KeyCode.Space))
            {
                attackQueued = true;
                attackQueuedTime = Time.time;
            }
            return;
        }

        // 3) 나머지 플레이어(1/3/5)는 기존 스톰프 구조 유지
        if (!isGrounded)
        {
            switch (t)
            {
                case T1: StartCoroutine(P1_AirStomp()); break;
                case T3:
                {
                    bool chainedAttack = Time.time - p3LastAttackEndTime <= p3ChainAttackWindow;
                    StartCoroutine(P3_AirStomp(chainedAttack));
                    break;
                }
                case T5: StartCoroutine(P5_AirStomp()); break;
            }
        }
    }



    // ---------- Player1 전용: 공중 스톰프 ----------
    private IEnumerator P1_AirStomp()
    {
        if (isGrounded) yield break;
        isAttacking = true;
        attackStateStartTime = Time.time;

        rb.gravityScale = cachedGravity;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, stompSpeed);
        ChangeAnimation("Attack"); // Player1 Attack

        yield return null; // 착지 시 HandleLandSequencing에서 처리
    }

    // ---------- Player5 전용: 공중 스톰프(하강), 착지 후 Attack 유지 -> Land 지연 ----------
    private IEnumerator P5_AirStomp()
    {
        if (isGrounded) yield break;
        isAttacking = true;
        attackStateStartTime = Time.time;

        rb.gravityScale = cachedGravity;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, stompSpeed*1.5f);
        ChangeAnimation("Attack"); // Player5 Attack

        yield return null; // 착지 시 HandleLandSequencing에서 P5_DelayedLand 호출
    }


    // ---------- 착지 처리: P1 & P5(지연 Land) ----------
    private void HandleLandSequencing()
    {
        if (!isAttacking || !isGrounded || hasLanded || isLanding) return;  // isLanding 추가
        if (GameData.Instance == null) return;

        hasLanded = true;
        isLanding = true;   // 추가

        switch (GameData.Instance.selectedPlayerType)
        {
            case T1: StartCoroutine(P1_Land_Wrap()); break;
            case T5: StartCoroutine(P5_Land_Wrap()); break;
            default:
                hasLanded = false;
                isLanding = false;
                break;
        }
    }

    private void SpawnSmokeEffect()
    {
        GameObject prefab = null;
            // 바닥 밑 콜라이더 확인
        Collider2D hit = Physics2D.OverlapCircle(transform.position + Vector3.down * 0.1f, 0.2f);
        bool onPlatform = (hit != null && hit.CompareTag("platform"));


        switch (GameData.Instance.selectedPlayerType)
        {
            case T3: // P3 전용
                prefab = smokeEffectPrefabP3;
                break;
            case T4: // P4 전용
                prefab = smokeEffectPrefabP4;
                break;
            case T5: // P5 전용
                prefab = smokeEffectPrefabP5;
                break;
            default: // P1, P2
                prefab = smokeEffectPrefab;
                break;
        }

        if (!prefab) return;
        // Rage + Platform → 차단
        if (isRageMode && onPlatform) return;
        Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
        string tag = smokePoolTag;
        if (prefab == smokeEffectPrefabP3) tag = smokeP3PoolTag;
        else if (prefab == smokeEffectPrefabP4) tag = smokeP4PoolTag;
        else if (prefab == smokeEffectPrefabP5) tag = smokeP5PoolTag;
        SpawnSmokeFromPrefab(prefab, pos, tag);
    }

    private IEnumerator MoveToWorldPosition(Vector3 worldStart, Vector3 worldTarget, float duration)
    {
        float safeDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            Vector3 next = Vector3.Lerp(worldStart, worldTarget, t);
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            yield return null;
        }

        transform.position = new Vector3(worldTarget.x, worldTarget.y, transform.position.z);
    }

    private IEnumerator MoveToXPosition(float startX, float targetX, float duration)
    {
        float safeDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            float nextX = Mathf.Lerp(startX, targetX, t);
            Vector3 current = transform.position;
            transform.position = new Vector3(nextX, current.y, current.z);
            yield return null;
        }

        Vector3 final = transform.position;
        transform.position = new Vector3(targetX, final.y, final.z);
    }

    private IEnumerator P3_AirStomp(bool chainedAttack = false)
    {
        if (isRageMode || isAttacking) yield break;
        p3NormalAttackQueued = false;
        isAttacking = true;
        BeginP3AttackPhysicsLock();
        isLanding = false;
        hasLanded = false;
        attackStateStartTime = Time.time;

        Vector3 startPos = transform.position;
        Vector3 attackPos = startPos + Vector3.right * p3NormalAttackDistance;
        Vector2 storedVelocity = rb.linearVelocity;
        float dashHalfDuration = Mathf.Max(0.01f, p3NormalAttackDuration * 0.5f);

        if (chainedAttack)
        {
            rb.gravityScale = cachedGravity;
        }
        else
        {
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
        }

        ChangeAnimation(isGrounded ? "Attack" : "Flyattack");
        SpawnP3NormalAttackSmoke();

        if (chainedAttack)
            yield return StartCoroutine(MoveToXPosition(startPos.x, attackPos.x, dashHalfDuration));
        else
            yield return StartCoroutine(MoveToWorldPosition(startPos, attackPos, dashHalfDuration));

        var hb = SpawnHitboxFromPool(hitboxPrefabP3, hitboxP3PoolTag, attackPos);
        if (hb != null)
        {
            var hbComp = hb.GetComponent<Hitbox>();
            if (hbComp) hbComp.damage = stats.attack;
            StartCoroutine(ActivateAndDestroyCollider(hb, 0f, Mathf.Max(0.05f, p3NormalAttackHitDuration)));
        }

        if (chainedAttack)
            yield return StartCoroutine(MoveToXPosition(attackPos.x, startPos.x, dashHalfDuration));
        else
            yield return StartCoroutine(MoveToWorldPosition(attackPos, startPos, dashHalfDuration));

        rb.gravityScale = cachedGravity;
        float restoredY = chainedAttack ? rb.linearVelocity.y : 0f;
        rb.linearVelocity = new Vector2(storedVelocity.x, restoredY);
        EndP3AttackPhysicsLock();

        isAttacking = false;
        isLanding = false;
        attackStateStartTime = -999f;
        p3LastAttackEndTime = Time.time;

        if (p3NormalAttackQueued && !isRageMode)
        {
            p3NormalAttackQueued = false;
            StartCoroutine(P3_AirStomp(true));
            yield break;
        }

        if (isGrounded) ChangeAnimation("Walk");
        else ChangeAnimation(GetAirborneAnimationTrigger());
    }

    private void BeginP3AttackPhysicsLock()
    {
        if (rb == null || p3AttackPhysicsLocked) return;

        p3AttackOriginalConstraints = rb.constraints;
        rb.constraints = p3AttackOriginalConstraints | RigidbodyConstraints2D.FreezePositionX;
        p3AttackPhysicsLocked = true;
    }

    private void EndP3AttackPhysicsLock()
    {
        if (rb == null || !p3AttackPhysicsLocked) return;

        rb.constraints = p3AttackOriginalConstraints;
        p3AttackPhysicsLocked = false;
    }

    private void SpawnP3NormalAttackSmoke()
    {
        GameObject prefab = smokeEffectPrefabP3 ? smokeEffectPrefabP3 : smokeEffectPrefab;
        if (prefab == null) return;

        string tag = smokeEffectPrefabP3 ? smokeP3PoolTag : smokePoolTag;
        Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
        SpawnSmokeFromPrefab(prefab, pos, tag);
    }


    // ---------- Player1 전용: 착지 처리 ----------
    private IEnumerator P1_Land()
    {
        ChangeAnimation("Land");
        bool landedOnFloor = (lastGroundTag == "floor");
        bool landedOnPlatform = (lastGroundTag == "platform");

        if (isRageMode)
        {
            // Rage 전용 히트박스
            var hb = SpawnHitboxFromPool(hitboxPrefabP1, hitboxP1PoolTag, transform.position);
            if (hb == null) yield break;
            hb.transform.parent = transform;
            AdjustHitboxIfRage(hb);
            var hbComp = hb.GetComponent<Hitbox>();
            if (hbComp) hbComp.damage = 1;
            StartCoroutine(ActivateAndDestroyCollider(hb, 0f, 0.2f));
            Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
            if (landedOnFloor && rage1SmokePrefab != null)
            {
                SpawnSmokeFromPrefab(rage1SmokePrefab, pos, rage1SmokePoolTag);
            }
            else if (landedOnPlatform && smokeEffectPrefab != null)
            {
                SpawnSmokeFromPrefab(smokeEffectPrefab, pos, smokePoolTag);
            }
                
        }
        else
        {
            var hb0 = SpawnHitboxFromPool(hitboxPrefabP1, hitboxP1PoolTag, transform.position);
            if (hb0 == null) yield break;
            hb0.transform.parent = transform;
            var hbComp0 = hb0.GetComponent<Hitbox>();
            if (hbComp0) hbComp0.damage = stats.attack;
            SpawnSmokeEffect();
        }

        // 🔹 P2/P3처럼 더 여유 있게 대기
        yield return new WaitForSeconds(landDuration * 1.5f);
        if (!isGrounded)
        {
            // 이미 Jump 애니 실행 중 → 착지 처리 스킵
            isAttacking = false;
            hasLanded = false;
            isLanding = false;
            yield break;
        }

        isAttacking = false;
        attackStateStartTime = -999f;
        hasLanded = false;
        isGrounded = true;
        lastStompTime = Time.time;
        ChangeAnimation("Walk");
        isLanding = false;
    }


    // ---------- P3 Land ----------
    private IEnumerator P3_Land()
    {
        ChangeAnimation("Land");
        bool landedOnFloor = (lastGroundTag == "floor");

        var hb = SpawnHitboxFromPool(hitboxPrefabP3, hitboxP3PoolTag, transform.position);
        if (hb == null) yield break;
        hb.transform.parent = transform;
        AdjustHitboxIfRage(hb);

        var hbComp = hb.GetComponent<Hitbox>();
        if (hbComp) hbComp.damage = stats.attack;

        StartCoroutine(ActivateAndDestroyCollider(hb, 0f, landDuration));

        if (isRageMode && landedOnFloor && rage3SmokePrefab != null)
        {
            // Rage 전용 (floor에서만)
            Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
            SpawnSmokeFromPrefab(rage3SmokePrefab, pos, rage3SmokePoolTag);
        }
        yield return new WaitForSeconds(landDuration * 1.5f);
        if (!isGrounded)
        {
            // 이미 Jump 애니 실행 중 → 착지 처리 스킵
            isAttacking = false;
            hasLanded = false;
            isLanding = false;
            yield break;
        }

        isAttacking = false;
        attackStateStartTime = -999f;
        hasLanded = false;
        isGrounded = true;
        lastStompTime = Time.time;
        ChangeAnimation("Walk");
        isLanding = false;
    }


    private IEnumerator P5_Land()
    {
        ChangeAnimation("Land");
        bool landedOnFloor = (lastGroundTag == "floor");
        bool landedOnPlatform = (lastGroundTag == "platform");

        var hb = SpawnHitboxFromPool(hitboxPrefabP5, hitboxP5PoolTag, transform.position);
        if (hb == null) yield break;
        hb.transform.parent = transform;
        AdjustHitboxIfRage(hb);

        var hbComp = hb.GetComponent<Hitbox>();
        if (hbComp) hbComp.damage = stats.attack;

        // ✅ Rage 상태일 때 연기 크기 1.5배
        if (landedOnFloor && isRageMode && isGrounded)
        {
            if (rageAttackSmokePrefab != null)
            {
                Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
                SpawnSmokeFromPrefab(rageAttackSmokePrefab, pos, rageAttackSmokePoolTag, 4f);  // 🔥 크기 4배
            }
        }
        else
        {
            SpawnSmokeEffect(); // 일반 연기
        }

        yield return new WaitForSeconds(landDuration * 2f);
        if (!isGrounded)
        {
            // 이미 Jump 애니 실행 중 → 착지 처리 스킵
            isAttacking = false;
            hasLanded = false;
            isLanding = false;
            yield break;
        }

        isAttacking = false;
        attackStateStartTime = -999f;
        hasLanded = false;
        isGrounded = true;
        lastStompTime = Time.time;
        ChangeAnimation("Walk");  
        isLanding = false;
    }

    private void OnCollisionEnter2D(Collision2D c)
    {
        if (c.gameObject.CompareTag("floor") || 
            c.gameObject.CompareTag("Obstacle") || 
            c.gameObject.CompareTag("platform"))
        {
            isGrounded = true;
            jumpedThisAirborne = false;
            lastGroundedTime = Time.time;
            lastGroundTag = c.collider.tag;
            lastGroundCol = c.collider;

            int t = GameData.Instance.selectedPlayerType;
            if (t == T3 && isAttacking && currentAnim == "Flyattack" &&
                (lastGroundTag == "floor" || lastGroundTag == "platform"))
            {
                ChangeAnimation("Attack");
            }

            if ((t == T2 || t == T4) && currentAnim == "Flyattack" &&
                (lastGroundTag == "floor" || lastGroundTag == "platform"))
            {
                if (isRageMode)
                {
                    ResolveRageRangedGroundedFlyattack();
                    return;
                }

                ChangeAnimation("Walk");
            }

            if ((t == T2 || t == T4) &&
                (lastGroundTag == "floor" || lastGroundTag == "platform") &&
                !isAttacking)
                ChangeAnimation("Walk");
        }

        if (c.gameObject.CompareTag("Obstacle"))
        {
            HandleHazardEnter(c.collider, c.gameObject);
        }
    }


    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Mana"))
        {
            Debug.Log("✨ Mana collected!");

            AddManaForLauncher(1);

            if (GameData.Instance != null)
                GameData.Instance.AddO2(1);

            Mana mana = other.GetComponent<Mana>();
            if (mana != null)
                mana.Collect();

            return;
        }

        if (other.CompareTag("holyMana"))
        {

            var hm = other.GetComponent<holyMana>();
            if (hm != null) hm.Collect();

            return;
        }



        if (other.CompareTag("Obstacle") || other.CompareTag("missile"))
        {
            HandleHazardEnter(other, other.gameObject);
        }
    }

    private void CacheObstacleType(GameObject obj)
    {
        if (obj == null)
        {
            lastHitObstacleType = ObstacleType.Normal;
            return;
        }

        lastHitObstacleType = GetCachedObstacleType(obj);
    }


    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Obstacle") || other.CompareTag("missile"))
        {
            HandleHazardExit(other);
        }
    }

    private void OnCollisionExit2D(Collision2D c)
    {
        if ((c.gameObject.CompareTag("floor") || c.gameObject.CompareTag("Obstacle") || c.gameObject.CompareTag("platform")) &&
            c.collider == lastGroundCol)
        {
            isGrounded = false;
            lastGroundTag = null;
            lastGroundCol = null;
        }

        if (c.gameObject.CompareTag("Obstacle"))
        {
            HandleHazardExit(c.collider);
        }
    }

    private void HandleHazardEnter(Collider2D hazardCollider, GameObject hazardObject)
    {
        if (hazardCollider == null && hazardObject == null) return;

        int contactId = GetHazardContactId(hazardCollider, hazardObject);
        if (contactId != 0 && !activeHazardContactIds.Add(contactId))
            return;

        GameObject source = hazardObject != null ? hazardObject : hazardCollider.gameObject;
        lastHitObstacleType = GetCachedObstacleType(source);

        if (IsP3AttackInvulnerable())
            return;

        if (hazardCollider != null && !(hazardCollider is PolygonCollider2D))
            DoObstacleBump(hazardCollider.transform.position.y);

        if (!isInvincible && !isRageMode)
        {
            TakeDamage(1);
            if (isAttacking) ForceLandFromHit();
        }

        obstacleTouchCount++;
        if (obstacleTouchCount == 1 && GameData.Instance != null)
            GameData.Instance.BeginObstacleContact();
    }

    private void HandleHazardExit(Collider2D hazardCollider)
    {
        int contactId = GetHazardContactId(hazardCollider, hazardCollider != null ? hazardCollider.gameObject : null);
        if (contactId != 0)
            activeHazardContactIds.Remove(contactId);

        obstacleTouchCount--;
        if (obstacleTouchCount <= 0)
        {
            obstacleTouchCount = 0;
            if (GameData.Instance != null)
                GameData.Instance.EndObstacleContact();
        }
    }

    private int GetHazardContactId(Collider2D hazardCollider, GameObject hazardObject)
    {
        if (hazardCollider != null && hazardCollider.attachedRigidbody != null)
            return hazardCollider.attachedRigidbody.GetInstanceID();
        if (hazardObject != null)
            return hazardObject.GetInstanceID();
        if (hazardCollider != null)
            return hazardCollider.GetInstanceID();
        return 0;
    }

    private ObstacleType GetCachedObstacleType(GameObject obj)
    {
        if (obj == null) return ObstacleType.Normal;

        int key = obj.GetInstanceID();
        if (cachedObstacleTypes.TryGetValue(key, out var cachedType))
            return cachedType;

        ObstacleType type = ObstacleType.Normal;
        var info = obj.GetComponent<ObstacleInfo>();
        if (info == null)
            info = obj.GetComponentInParent<ObstacleInfo>();
        if (info == null)
            info = obj.GetComponentInChildren<ObstacleInfo>(true);
        if (info != null)
            type = info.type;
        else if (obj.CompareTag("missile"))
            type = ObstacleType.Missile;

        cachedObstacleTypes[key] = type;
        return type;
    }


    private void Hit()
    {
        lives -= 1;
        if (lives <= 0) Die();
    }
    private bool IsP3AttackInvulnerable()
    {
        return GameData.Instance != null &&
               GameData.Instance.selectedPlayerType == T3 &&
               isAttacking;
    }
    private void Die()
    {
        if (isDead) return;
        isDead = true;
        EndP3AttackPhysicsLock();
        ClearRageTransformSmokes();
        SpawnBloodFx();
        
        ChangeAnimation("Die");
        GameData.Instance.TriggerGameOver();

        rb.simulated = false;

        var col = GetComponent<Collider2D>();
        if (col) col.enabled = false;

        StartCoroutine(SlideToDeathY());
    }

    private IEnumerator SlideToDeathY()
    {
        float duration = 0.7f;
        float elapsed = 0f;

        Vector3 start = transform.position;
        Vector3 end = new Vector3(start.x, -3.25f, start.z);

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            transform.position = Vector3.Lerp(start, end, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.position = end;
    }


    void Start()
    {
        int playerType = Mathf.Clamp(GameData.Instance.selectedPlayerType, 1, 5);
        PrewarmEffectPools();
        int playerLevel = GameData.Instance.playerLevels[playerType];
        if (playerLevel <= 0) playerLevel = 1;

        stats = StatsManager.GetPlayerStats(playerType, playerLevel);

        // ✅ 체력은 무조건 3으로 시작
        lives = 3;


        if (bar != null)
        {
            bar.SetHealth(lives); // 하트 3개 다 켜짐
        }
        var animOverride = GetComponent<PlayerAnimOverride>();
        if (animOverride != null)
            animOverride.PreWarm();
        anim.Rebind();
        anim.Update(0f);
        StartSpawnIntro();

    }

    public void TakeDamage(int dmg)
    {
        if (isSpawnIntroActive) return;
        if (isInvincible || isRageMode || IsP3AttackInvulnerable()) return;  // 🔹 Hurt 중엔 무시

        lives = Mathf.Max(0, lives - dmg);
        if (bar != null) bar.SetHealth(lives);

        if (lives <= 0)
        {
            Die();
            GameData.Instance.gameOver = true;
        }
        else
        {
            StartCoroutine(PlayHurtAnimation()); // 🔹 Hurt 1회만 실행
        }
    }

    private void AdjustHitboxIfRage(GameObject hb)
    {
        if (!isRageMode || hb == null) return;

        var box = hb.GetComponent<BoxCollider2D>();
        var capsule = hb.GetComponent<CapsuleCollider2D>();
        if (box == null && capsule == null) return;

        int t = (GameData.Instance != null) ? GameData.Instance.selectedPlayerType : 0;
        Vector2 size = box != null ? box.size : capsule.size;
        Vector2 offset = box != null ? box.offset : capsule.offset;

        if (GameData.Instance.selectedPlayerType == 5)
        {
            float extraX5 = 12f;
            float extraY5 = 20f;

            size = new Vector2(size.x + extraX5, size.y + extraY5);
            offset = new Vector2(offset.x + extraX5 * 0.5f, offset.y);
        }
        else if (GameData.Instance.selectedPlayerType == 1)
        {
            float extraX5 = 12f;
            float extraY5 = 8f;

            size = new Vector2(size.x + extraX5, size.y + extraY5);
            offset = new Vector2(offset.x + extraX5 * 0.5f, offset.y);
        }
        else if (GameData.Instance.selectedPlayerType == 3)
        {
            // Player3 Rage는 프리팹 자체 콜라이더 크기를 그대로 사용한다.
        }
        else
        {
            float extraX = 10f;
            float extraY = 2f;

            size = new Vector2(size.x + extraX, size.y + extraY);
            offset = new Vector2(offset.x + extraX * 0.5f, offset.y);
        }

        if (t == 1 || t == 5)
        {
            const float extraRightXP15 = 1.2f;
            size = new Vector2(size.x + extraRightXP15, size.y);
            offset = new Vector2(offset.x + extraRightXP15 * 0.5f, offset.y);
        }

        if (box != null)
        {
            box.size = size;
            box.offset = offset;
        }

        if (capsule != null)
        {
            capsule.size = size;
            capsule.offset = offset;
        }
    }


    // Player2 Rage 전용
    private IEnumerator P2_RageAttack()
    {
        bool groundedAttack = ShouldUseRageRangedGroundAttack();
        PlayRageRangedAttackAnimation(groundedAttack);

        if (p2RageLaserPrefab != null)
        {
            bool laserFromPool;
            var laser = SpawnWithPool(p2RageLaserPrefab, p2RageLaserPoolTag, p2Muzzle.position, Quaternion.identity, out laserFromPool, 6);
        }

        // ✅ 분노 상태 + 지면일 때 태그별로 연기 생성
        if (groundedAttack)
        {
            if (lastGroundTag == "floor" && rage24SmokePrefab != null)
            {
                Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
                SpawnSmokeFromPrefab(rage24SmokePrefab, pos, rage24SmokePoolTag);
            }
            else if (lastGroundTag == "platform" && smokeEffectPrefab != null)
            {
                Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
                SpawnSmokeFromPrefab(smokeEffectPrefab, pos, smokePoolTag);
            }
        }

        yield break;
    }
    
    private IEnumerator P3_RageAttack()
    {
        bool groundedAttack = ShouldUseRageRangedGroundAttack();
        PlayRageRangedAttackAnimation(groundedAttack);

        if (isRageMode && rage3SmokePrefab != null)
        {
            Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
            bool fromPool;
            var go = SpawnWithPool(rage3SmokePrefab, rage3SmokePoolTag, pos, Quaternion.identity, out fromPool, 8);
            var zig = go != null ? go.GetComponent<ZigzagLightning>() : null;
            if (zig)
            {
                zig.ConfigurePooling(fromPool, rage3SmokePoolTag);
                zig.damage = stats.attack;
            }
        }

        yield break;
    }


    // ===================== Player4 Rage: 직선 =====================
    private IEnumerator P4_RageAttack()
    {
        bool groundedAttack = ShouldUseRageRangedGroundAttack();
        PlayRageRangedAttackAnimation(groundedAttack);

        if (p4RageLightningPrefab != null && p4Emitter != null)
        {
            bool fromPool;
            var go = SpawnWithPool(p4RageLightningPrefab, p4RageLightningPoolTag, p4Emitter.position, Quaternion.identity, out fromPool, 8);
            var zig = go != null ? go.GetComponent<ZigzagLightning>() : null;
            if (zig)
            {
                zig.ConfigurePooling(fromPool, p4RageLightningPoolTag);
                zig.damage = stats.attack;
            }
        }

        // ✅ 분노 상태 + 지면일 때 태그별로 연기 생성
        if (groundedAttack)
        {
            if (lastGroundTag == "floor" && rage24SmokePrefab != null)
            {
                Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
                SpawnSmokeFromPrefab(rage24SmokePrefab, pos, rage24SmokePoolTag);
            }
            else if (lastGroundTag == "platform" && smokeEffectPrefab != null)
            {
                Vector3 pos = new Vector3(transform.position.x, transform.position.y - 0.5f, -0.5f);
                SpawnSmokeFromPrefab(smokeEffectPrefab, pos, smokePoolTag);
            }
        }

        yield break;
    }


    // Rage 끝나기 전 경고용 깜빡임 (0.2초 간격, 총 5번)
    private IEnumerator BlinkRageWarningEffect()
    {
        int count = 12; // 5번 반복 → 0.2초 * 6 = 1.2초
        for (int i = 0; i < count; i++)
        {
            sr.enabled = false;
            yield return new WaitForSeconds(0.05f); // 꺼짐 0.2초
            sr.enabled = true;
            yield return new WaitForSeconds(0.05f); // 켜짐 0.2초
        }
    }


    public void ActivateRageMode(float duration = -1f)
    {
        if (isRageMode) return;

        if (duration <= 0f) duration = rageDuration;
        isRageMode = true;
        rageEndTime = Time.time + duration;
        EndP3AttackPhysicsLock();
        isAttacking = false;
        isLanding = false;
        hasLanded = false;
        attackQueued = false;
        p3NormalAttackQueued = false;
        p3LastAttackEndTime = -999f;
        attackStateStartTime = -999f;
        StopRangedFlyattackLatch();
        StopRageRangedGroundAttackTimer();

        // 원래 값 저장
        originalAttack = stats.attack;
        originalHealth = maxHp;

        // Rage 능력치
        stats.attack = 1;
        maxHp = 1000;
        hp = maxHp;

        if (bar != null)
            bar.SetHealth(hp);

        ApplyJumpParticleMaterial(true);

        // Rage 애니 적용
        var animOverride = GetComponent<PlayerAnimOverride>();
        AnimationClip rageTransformClip = null;
        if (animOverride != null)
        {
            animOverride.SetRageMode(true);
            animOverride.ApplyOverrides(force: true);
            rageTransformClip = animOverride.GetCurrentRageTransformClip();
            anim.Rebind();
            anim.Update(0f);
            currentAnim = "";
        }

        
        PlayRageTransformAnimation();

        isTransformLock = true;
        float clipDuration = rageTransformClip != null ? rageTransformClip.length : 0f;
        transformLockEndTime = Time.time + Mathf.Max(transformLockDuration, clipDuration);

        SpawnRageTransformSmokes();

        StartRageSpeedEffect();

        StartCoroutine(SpawnRageTransformCollider());

        Debug.Log("🔥 Rage Mode ON");
    }

    private void PlayRageTransformAnimation()
    {
        if (anim == null) return;

        ResetWalkAnimationSpeedImmediate();
        anim.ResetTrigger("Jump");
        anim.ResetTrigger("Attack");
        anim.ResetTrigger("Flyattack");
        anim.ResetTrigger("Land");
        anim.ResetTrigger("Walk");
        anim.ResetTrigger("Still");
        anim.ResetTrigger("Hurt");
        anim.ResetTrigger("Die");

        anim.Play("Base Transform", 0, 0f);
        anim.Update(0f);
        currentAnim = "Transform";
        UpdateJumpParticleEmission("Transform");
    }



    public bool IsRageModeActive()
    {
        return isRageMode;
    }


    private IEnumerator ActivateAndDestroyCollider(GameObject hb, float delay, float duration)
    {
        if (hb == null) yield break;
        var col = hb.GetComponent<Collider2D>();
        if (col) col.enabled = false;         // 처음엔 꺼둠
        yield return new WaitForSeconds(delay);
        if (col) col.enabled = true;          // 0.2초 뒤 켬
        yield return new WaitForSeconds(duration);
        var pooledHitbox = hb.GetComponent<Hitbox>();
        if (pooledHitbox != null)
            pooledHitbox.DespawnNow();
        else
            Destroy(hb);
    }
    private IEnumerator SpawnRageTransformCollider()
    {
        if (rageTransformColliderObj == null)
            yield break;

        ApplyRageTransformColliderShape();
        rageTransformColliderObj.transform.position = transform.position;
        rageTransformColliderObj.transform.localPosition = Vector3.zero;
        if (rageTransformHitbox != null)
        {
            rageTransformHitbox.damage = stats.attack;
            rageTransformHitbox.SetOwner(this);
        }
        rageTransformColliderObj.SetActive(true);

        yield return new WaitForSeconds(0.5f);

        if (rageTransformColliderObj != null)
            rageTransformColliderObj.SetActive(false);
    }

    private void SpawnRageTransformSmokes()
    {
        ClearRageTransformSmokes();

        activeRageSmokePrimary = SpawnTrackedRageSmoke(
            rageSmokePrefab,
            string.IsNullOrEmpty(rageSmokePoolTag) ? "transformSmoke" : rageSmokePoolTag,
            rageSmokeOffset);

        GameObject secondaryPrefab = rageSmokePrefabSecondary != null ? rageSmokePrefabSecondary : rageSmokePrefab;
        string secondaryTag = string.IsNullOrEmpty(rageSmokePoolTagSecondary) ? rageSmokePoolTag : rageSmokePoolTagSecondary;
        activeRageSmokeSecondary = SpawnTrackedRageSmoke(
            secondaryPrefab,
            secondaryTag,
            rageSmokeOffsetSecondary);
    }

    private GameObject SpawnTrackedRageSmoke(GameObject prefab, string poolTag, Vector3 offset)
    {
        if (prefab == null)
            return null;

        return SpawnSmokeFromPrefab(prefab, transform.position + offset, poolTag);
    }

    private void SyncRageTransformSmokes()
    {
        SyncTrackedRageSmoke(activeRageSmokePrimary, rageSmokeOffset);
        SyncTrackedRageSmoke(activeRageSmokeSecondary, rageSmokeOffsetSecondary);
    }

    private void SyncTrackedRageSmoke(GameObject smoke, Vector3 offset)
    {
        if (smoke == null || !smoke.activeInHierarchy)
            return;

        smoke.transform.position = transform.position + offset;
    }

    private void ClearRageTransformSmokes()
    {
        activeRageSmokePrimary = ReturnRageTransformSmoke(activeRageSmokePrimary, rageSmokePoolTag);

        string secondaryTag = string.IsNullOrEmpty(rageSmokePoolTagSecondary) ? rageSmokePoolTag : rageSmokePoolTagSecondary;
        activeRageSmokeSecondary = ReturnRageTransformSmoke(activeRageSmokeSecondary, secondaryTag);
    }

    private GameObject ReturnRageTransformSmoke(GameObject smoke, string poolTag)
    {
        if (smoke == null)
            return null;

        if (smoke.activeInHierarchy)
        {
            if (ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
                ObjectPool.Instance.ReturnToPool(poolTag, smoke);
            else
                Destroy(smoke);
        }

        return null;
    }

    private IEnumerator NudgeY(float deltaY, float duration)
    {
        float startY = transform.position.y;
        float endY = startY + deltaY;
        float t = 0f;

        while (t < duration)
        {
            float y = Mathf.Lerp(startY, endY, t / duration);
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
            t += Time.deltaTime;
            yield return null;
        }
        // 스냅 보정
        transform.position = new Vector3(transform.position.x, endY, transform.position.z);
    }

    private void DoObstacleBump(float obstacleCenterY)
    {
        lastBumpTime = Time.time;

        float playerY = transform.position.y;
        int dir = (obstacleCenterY > playerY) ? -1 : +1; // 위쪽→아래로 / 아래쪽→위로

        // 즉시 위/아래로 튕김 + 최소 이동 보장
        rb.gravityScale = cachedGravity;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, dir * bumpVelocityY);
        StartCoroutine(NudgeY(dir * bumpDistance, bumpDuration));
    }

    public void ResetPlayer()
    {
        if (spawnIntroRoutine != null)
        {
            StopCoroutine(spawnIntroRoutine);
            spawnIntroRoutine = null;
        }

        manaCount = 0;
        isDead = false;
        lives = 3;
        hp = maxHp;
        isRageMode = false;
        StopRageSpeedEffect();
        ClearRageTransformSmokes();
        ApplyJumpParticleMaterial(false);
        EndP3AttackPhysicsLock();
        isAttacking = false;
        isLanding = false;
        hasLanded = false;
        justRecoveredFromRage = false;
        attackQueued = false;
        p3NormalAttackQueued = false;
        isTransformLock = false;
        isInvincible = false;
        obstacleTouchCount = 0;
        activeHazardContactIds.Clear();
        cachedObstacleTypes.Clear();

        rb.simulated = true;
        var col = GetComponent<Collider2D>();
        if (col) col.enabled = true;
        if (sr != null) sr.enabled = true;

        rb.gravityScale = cachedGravity;
        rb.linearVelocity = Vector2.zero;
        transform.position = new Vector3(gameplayX, introAppearPosition.y, transform.position.z);
        lastGroundTag = null;
        lastGroundCol = null;
        isGrounded = false;
        jumpedThisAirborne = false;
        lastGroundedTime = -999f;

        if (bar != null)
        {
            bar.SetHealth(lives);
        }
        
        anim.ResetTrigger("Jump");
        anim.ResetTrigger("Attack");
        anim.ResetTrigger("Flyattack");
        anim.ResetTrigger("Land");
        anim.ResetTrigger("Walk");
        anim.ResetTrigger("Still");
        
        anim.Rebind();
        anim.Update(0f);
        ForceAnimationState("Base Walk", "Walk");
        needRebind = false;
        StartSpawnIntro();
    }
    private void StartSpawnIntro()
    {
        if (spawnIntroRoutine != null)
            StopCoroutine(spawnIntroRoutine);
        spawnIntroRoutine = StartCoroutine(CoPlaySpawnIntro());
    }

    private IEnumerator CoPlaySpawnIntro()
    {
        isSpawnIntroActive = true;

        Collider2D col = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (rb != null)
        {
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0f;
        }

        if (col != null)
            col.enabled = false;
        if (sr != null)
            sr.enabled = false;

        GateHealth.Instance?.OpenGate();

        float totalDuration = Mathf.Max(0.05f, introTotalDuration);
        float startDelay = Mathf.Max(0f, introStartDelay);
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        float gateLead = Mathf.Clamp(introGateLeadTime, 0f, totalDuration);
        if (gateLead > 0f)
            yield return new WaitForSeconds(gateLead);

        transform.position = new Vector3(introAppearPosition.x, introAppearPosition.y, transform.position.z);
        if (sr != null)
            sr.enabled = true;
        if (col != null)
            col.enabled = true;

        ForceAnimationState("Base Walk", "Walk");

        float elapsed = gateLead;
        bool landed = false;
        float diagonalElapsed = 0f;
        float minDiagonalTime = Mathf.Max(0f, introMinDiagonalTime);

        while (elapsed < totalDuration)
        {
            float dt = Time.deltaTime;
            transform.position += new Vector3(Mathf.Abs(introDiagonalXSpeed), introClimbYSpeed, 0f) * dt;
            elapsed += dt;
            diagonalElapsed += dt;

            if (diagonalElapsed >= minDiagonalTime && TryGetIntroGroundSnapY(out float snapY, out var groundCol))
            {
                transform.position = new Vector3(transform.position.x, snapY, transform.position.z);
                isGrounded = true;
                lastGroundedTime = Time.time;
                lastGroundCol = groundCol;
                lastGroundTag = groundCol != null ? groundCol.tag : null;
                landed = true;
                break;
            }

            yield return null;
        }

        float targetX = gameplayX;
        float remaining = Mathf.Max(0f, totalDuration - elapsed);
        if (landed && remaining > 0f)
        {
            Vector3 startPos = transform.position;
            Vector3 targetPos = new Vector3(targetX, startPos.y, startPos.z);
            float t = 0f;

            while (t < remaining)
            {
                t += Time.deltaTime;
                float ratio = remaining <= 0f ? 1f : Mathf.Clamp01(t / remaining);
                transform.position = Vector3.Lerp(startPos, targetPos, ratio);
                yield return null;
            }
        }

        transform.position = new Vector3(targetX, transform.position.y, transform.position.z);
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = cachedGravity;
        }

        if (!isGrounded && TryGetGroundSurface(out var fallbackGround))
        {
            isGrounded = true;
            lastGroundedTime = Time.time;
            lastGroundCol = fallbackGround;
            lastGroundTag = fallbackGround != null ? fallbackGround.tag : null;
        }

        if (closeGateAfterIntro)
            GateHealth.Instance?.CloseGateOnBloodHit();

        ForceAnimationState("Base Walk", "Walk");
        isSpawnIntroActive = false;
        spawnIntroRoutine = null;
    }

    private bool TryGetIntroGroundSnapY(out float snappedY, out Collider2D groundCol)
    {
        snappedY = transform.position.y;
        groundCol = null;

        if (bodyCollider == null)
            return false;

        Bounds bounds = bodyCollider.bounds;
        float startY = bounds.min.y + 0.05f;
        float inset = Mathf.Min(bounds.extents.x * 0.8f, 0.2f);
        float[] xs = { bounds.center.x, bounds.center.x - inset, bounds.center.x + inset };
        float bestTop = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < xs.Length; i++)
        {
            RaycastHit2D hit = Physics2D.Raycast(new Vector2(xs[i], startY), Vector2.down, introGroundProbeDistance);
            if (hit.collider == null || hit.collider == bodyCollider) continue;
            if (!IsGroundSurfaceTag(hit.collider.tag)) continue;

            float top = hit.collider.bounds.max.y;
            if (!found || top > bestTop)
            {
                bestTop = top;
                groundCol = hit.collider;
                found = true;
            }
        }

        if (!found)
            return false;

        float delta = bestTop - bounds.min.y;
        snappedY = transform.position.y + delta;
        return true;
    }


    public void OnRageModeChanged(bool active)
    {
        isRageMode = active;
        if (active)
        {
            obstacleTouchCount = 0;
            activeHazardContactIds.Clear();
            StartRageSpeedEffect();
        }
        else
        {
            StopRageSpeedEffect();
        }

        var animOverride = GetComponent<PlayerAnimOverride>();
        if (animOverride != null)
            animOverride.SetRageMode(active);

        if (!active)
        {
            anim.Rebind();      // 🔥 애니메이터 리셋
            anim.Update(0f);
            currentAnim = "";
            ApplyLocomotionAnimation();
        }
    }
    // 공격을 Land로 강제 연결
    private void ForceLandFromHit()
    {
        if (isLanding) return;
        isLanding = true;
        isGrounded = true;
        hasLanded = false;

        switch (GameData.Instance.selectedPlayerType)
        {
            case T1: StartCoroutine(P1_Land_Wrap()); break;
            case T5: StartCoroutine(P5_Land_Wrap()); break;
            case T2:
            case T3:
            case T4:
                CancelAttackToWalk();
                isLanding = false;
                break;
        }
    }


    private void CancelAttackToWalk()
    {
        EndP3AttackPhysicsLock();
        isAttacking = false;
        attackStateStartTime = -999f;
        hasLanded = false;
        isLanding = false;
        attackQueued = false;
        p3NormalAttackQueued = false;
        p3LastAttackEndTime = -999f;

        rb.gravityScale = cachedGravity;
        ChangeAnimation("Walk");
    }
    private IEnumerator PlayHurtAnimation()
    {
        if (isRageMode || isInvincible) yield break;

        // 1) 무적 시작
        isInvincible = true;
        StartCoroutine(ResetInvincibleAfterDelay());
        

        isInHurtWindow = true;
        // 2) Hurt 재생
        ChangeAnimation("Hurt");
        SpawnBloodFx();

        // 3) Hurt 애니메이션을 0.5초 동안만 유지
        yield return new WaitForSeconds(0.5f);
        isInHurtWindow = false;
        StartCoroutine(InvincibleAlphaAfterHurt(2.5f));
        // 4) 이후에 Walk/Jump로 정상 전환
        if (isGrounded)
            ChangeAnimation("Walk");
        else
            ChangeAnimation(GetAirborneAnimationTrigger());
    }
    private void SpawnBloodFx()
    {
        GameObject prefab = bloodNormal;

        switch (lastHitObstacleType)
        {
            case ObstacleType.Missile: prefab = bloodMissile; break;
            case ObstacleType.Saw:     prefab = bloodSaw; break;
            case ObstacleType.Hill:    prefab = bloodHill; break;
        }

        if (!prefab) return;

        Vector3 localPos = Vector3.zero;
        localPos.z = 0.1f;
        
        var fx = Instantiate(prefab, transform);
        fx.transform.localPosition = new Vector3(0f, 0f, -1f);

        Destroy(fx, 1.5f);
    }

    private IEnumerator InvincibleAlphaAfterHurt(float remainDuration)
    {
        float elapsed = 0f;
        bool toggle = false;

        while (elapsed < remainDuration)
        {
            toggle = !toggle;
            SetTransparent(toggle); // true=반투명, false=불투명

            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        // 종료 시 원래대로
        SetTransparent(false);
    }
    private IEnumerator ResetInvincibleAfterDelay()
    {
        yield return new WaitForSeconds(3f);
        isInvincible = false;
    }
    private void SetTransparent(bool enable)
    {
        if (sr == null) return;

        Color c = sr.color;
        c.a = enable ? 0.3f : 1f;   // 🔥 반투명 0.5
        sr.color = c;
    }

    private void AddManaForLauncher(int amount = 1)
    {
        manaCount += amount;

        if (manaCount >= manaForLauncher)
        {
            manaCount = 0;

            if (bombLauncher != null)
            {
                bombLauncher.ActivateLauncher();
            }
            else
            {
                Debug.LogWarning("[Player] bombLauncher is null (Inspector 연결 필요)");
            }
        }
        Debug.Log($"[Mana] bombLauncher null? {bombLauncher==null}");
        if (bombLauncher != null)
        {
            Debug.Log($"[Mana] bombLauncher activeSelf={bombLauncher.gameObject.activeSelf}, activeInHierarchy={bombLauncher.gameObject.activeInHierarchy}");
            Debug.Log($"[Mana] bombLauncher name={bombLauncher.gameObject.name}");
            Debug.Log($"[Mana] bombLauncher scene={bombLauncher.gameObject.scene.name}");
        }
    }



    private IEnumerator P1_Land_Wrap() { yield return StartCoroutine(P1_Land()); isLanding = false; }
    private IEnumerator P3_Land_Wrap() { yield return StartCoroutine(P3_Land()); isLanding = false; }
    private IEnumerator P5_Land_Wrap() { yield return StartCoroutine(P5_Land()); isLanding = false; }

    private bool IsStompPlayerType(int type)
    {
        return type == T1 || type == T5;
    }

    private bool IsGroundSurfaceTag(string tag)
    {
        return tag == "floor" || tag == "platform" || tag == "Obstacle";
    }

    private bool IsStandingOnGround()
    {
        return TryGetGroundSurface(out _);
    }

    private bool TryGetGroundSurface(out Collider2D groundCol)
    {
        groundCol = null;
        if (bodyCollider == null) return false;

        Bounds bounds = bodyCollider.bounds;
        float y = bounds.min.y + 0.02f;
        float inset = Mathf.Min(bounds.extents.x * 0.8f, 0.2f);
        float[] xs = { bounds.center.x, bounds.center.x - inset, bounds.center.x + inset };

        for (int i = 0; i < xs.Length; i++)
        {
            RaycastHit2D hit = Physics2D.Raycast(new Vector2(xs[i], y), Vector2.down, 0.18f);
            if (hit.collider == null || hit.collider == bodyCollider) continue;
            if (!IsGroundSurfaceTag(hit.collider.tag)) continue;

            groundCol = hit.collider;
            return true;
        }

        return false;
    }

    private void SyncGroundedState()
    {
        if (bodyCollider == null || rb == null || isDead) return;

        bool risingFromJump = Time.time - lastJumpTime < 0.08f && rb.linearVelocity.y > 0.05f;
        if (risingFromJump) return;

        bool wasGrounded = isGrounded;
        bool groundedNow = TryGetGroundSurface(out var groundCol);
        if (groundedNow)
        {
            if (!wasGrounded)
                lastGroundedTime = Time.time;
            isGrounded = true;
            jumpedThisAirborne = false;
            if (groundCol != null)
            {
                lastGroundCol = groundCol;
                lastGroundTag = groundCol.tag;
            }
        }
        else
        {
            isGrounded = false;
        }
    }

    private string GetAirborneAnimationTrigger()
    {
        if (isRageMode)
            return "Jump";

        if (!isGrounded && !jumpedThisAirborne)
            return "Still";

        return "Jump";
    }

    private void RecoverStuckGroundAttack()
    {
        if (GameData.Instance == null || rb == null) return;

        int t = GameData.Instance.selectedPlayerType;
        if (!IsStompPlayerType(t)) return;
        if (!isAttacking || currentAnim != "Attack") return;

        bool groundedNow = isGrounded || IsStandingOnGround();
        if (!groundedNow) return;

        float groundedFor = Time.time - lastGroundedTime;
        float attackFor = Time.time - attackStateStartTime;

        if (!isLanding && !hasLanded && groundedFor >= 0.02f && attackFor >= 0.05f)
        {
            HandleLandSequencing();
            return;
        }

        if (groundedFor >= 0.35f && Mathf.Abs(rb.linearVelocity.y) <= 0.05f)
        {
            CancelAttackToWalk();
        }
    }

    private GameObject SpawnWithPool(GameObject prefab, string tag, Vector3 pos, Quaternion rot, out bool spawnedFromPool, int initialSize = 0)
    {
        spawnedFromPool = false;
        if (prefab == null) return null;

        string resolvedTag = tag;
        if (useEffectPool && ObjectPool.Instance != null && !string.IsNullOrEmpty(tag))
        {
            resolvedTag = ResolvePoolTag(tag);
            if (!ObjectPool.Instance.HasPool(resolvedTag))
                ObjectPool.Instance.RegisterPool(resolvedTag, prefab, initialSize);

            if (ObjectPool.Instance.HasPool(resolvedTag))
            {
                var pooled = ObjectPool.Instance.SpawnFromPool(resolvedTag, pos, rot);
                if (pooled != null)
                {
                    spawnedFromPool = true;
                    return pooled;
                }
            }
        }

        return Instantiate(prefab, pos, rot);
    }

    private string ResolvePoolTag(string tag)
    {
        if (ObjectPool.Instance == null || string.IsNullOrEmpty(tag)) return tag;
        if (ObjectPool.Instance.HasPool(tag)) return tag;

        // 흔한 오탈자 보정
        if (tag == "Lightning" && ObjectPool.Instance.HasPool("Lightening")) return "Lightening";
        if (tag == "Lightening" && ObjectPool.Instance.HasPool("Lightning")) return "Lightning";

        var defs = ObjectPool.Instance.pools;
        if (defs == null) return tag;

        for (int i = 0; i < defs.Count; i++)
        {
            var p = defs[i];
            if (p == null || string.IsNullOrEmpty(p.tag)) continue;
            if (string.Equals(p.tag, tag, System.StringComparison.OrdinalIgnoreCase))
                return p.tag;
        }
        return tag;
    }

    private GameObject SpawnSmokeFromPrefab(GameObject prefab, Vector3 pos, string poolTag, float scaleMult = 1f)
    {
        bool fromPool;
        var go = SpawnWithPool(prefab, poolTag, pos, Quaternion.identity, out fromPool, 8);
        if (go == null) return null;

        go.transform.position = new Vector3(pos.x, pos.y, -0.5f);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = prefab.transform.localScale * Mathf.Max(0.01f, scaleMult);

        var mover = go.GetComponent<SmokeMover>();
        if (mover != null)
            mover.ConfigurePooling(fromPool, poolTag);

        return go;
    }

    private void StartRageSpeedEffect()
    {
        if (speedEffectPrefab == null) return;
        nextSpeedEffectSpawnTime = Time.time;
    }

    private void TrySpawnRageSpeedEffect()
    {
        if (!isRageMode || speedEffectPrefab == null) return;

        if (nextSpeedEffectSpawnTime < 0f)
            nextSpeedEffectSpawnTime = Time.time;

        if (Time.time < nextSpeedEffectSpawnTime)
            return;

        nextSpeedEffectSpawnTime = Time.time + Mathf.Max(0.01f, speedEffectSpawnInterval);
        SpawnRageSpeedEffectOnce();
    }

    private void SpawnRageSpeedEffectOnce()
    {
        if (speedEffectSpawnWorldPositions == null || speedEffectSpawnWorldPositions.Length == 0)
            return;

        for (int i = 0; i < speedEffectSpawnWorldPositions.Length; i++)
        {
            Vector2 spawnWorldPos = speedEffectSpawnWorldPositions[i];
            Vector3 spawnPos = new Vector3(spawnWorldPos.x, spawnWorldPos.y, speedEffectPrefab.transform.position.z);

            bool fromPool;
            var go = SpawnWithPool(speedEffectPrefab, speedEffectPoolTag, spawnPos, Quaternion.identity, out fromPool, 4);
            if (go == null) continue;

            Vector3 pos = go.transform.position;
            pos.x = spawnWorldPos.x;
            pos.y = spawnWorldPos.y;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = speedEffectPrefab.transform.localScale;

            var effect = go.GetComponent<speedEffect>();
            if (effect == null)
                effect = go.AddComponent<speedEffect>();

            effect.Configure(speedEffectMoveSpeed, spawnWorldPos, speedEffectDespawnX, fromPool, speedEffectPoolTag);
        }
    }

    private void StopRageSpeedEffect()
    {
        nextSpeedEffectSpawnTime = -1f;

        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(speedEffectPoolTag) && ObjectPool.Instance.HasPool(speedEffectPoolTag))
        {
            ObjectPool.Instance.ReturnAllActive(speedEffectPoolTag);
        }
    }

    private GameObject SpawnHitboxFromPool(GameObject prefab, string tag, Vector3 pos)
    {
        bool fromPool;
        var go = SpawnWithPool(prefab, tag, pos, Quaternion.identity, out fromPool, 8);
        if (go == null) return null;

        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = prefab.transform.localScale;
        ResetHitboxColliderToPrefab(go, prefab);

        var hb = go.GetComponent<Hitbox>();
        if (hb != null)
        {
            hb.ConfigurePooling(fromPool, tag);
            hb.SetOwner(this);
        }

        return go;
    }

    private void ResetHitboxColliderToPrefab(GameObject instance, GameObject prefab)
    {
        if (instance == null || prefab == null) return;

        var instanceBox = instance.GetComponent<BoxCollider2D>();
        var prefabBox = prefab.GetComponent<BoxCollider2D>();
        if (instanceBox != null && prefabBox != null)
        {
            instanceBox.size = prefabBox.size;
            instanceBox.offset = prefabBox.offset;
            instanceBox.enabled = prefabBox.enabled;
            instanceBox.isTrigger = prefabBox.isTrigger;
        }

        var instanceCapsule = instance.GetComponent<CapsuleCollider2D>();
        var prefabCapsule = prefab.GetComponent<CapsuleCollider2D>();
        if (instanceCapsule != null && prefabCapsule != null)
        {
            instanceCapsule.size = prefabCapsule.size;
            instanceCapsule.offset = prefabCapsule.offset;
            instanceCapsule.direction = prefabCapsule.direction;
            instanceCapsule.enabled = prefabCapsule.enabled;
            instanceCapsule.isTrigger = prefabCapsule.isTrigger;
        }
    }

    private void SetupRageTransformCollider()
    {
        if (rageTransformColliderObj != null) return;

        rageTransformColliderObj = new GameObject("RageTransformCollider");
        rageTransformColliderObj.transform.SetParent(transform, false);
        rageTransformColliderObj.transform.localPosition = Vector3.zero;

        rageTransformBox = rageTransformColliderObj.AddComponent<BoxCollider2D>();
        rageTransformBox.isTrigger = true;
        ApplyRageTransformColliderShape();

        rageTransformHitbox = rageTransformColliderObj.AddComponent<Hitbox>();
        rageTransformHitbox.lifeTime = 0.5f;
        rageTransformHitbox.ConfigurePooling(false, "");
        rageTransformHitbox.SetAutoDespawnOnEnable(false);
        rageTransformHitbox.SetOwner(this);

        rageTransformColliderObj.SetActive(false);
    }

    private void ApplyRageTransformColliderShape()
    {
        if (rageTransformBox == null)
            return;

        rageTransformBox.size = rageTransformHitboxSize;
        rageTransformBox.offset = rageTransformHitboxOffset;
    }

    private void EnsureEffectPoolTagIsolation()
    {
        if (string.IsNullOrEmpty(lightningPoolTag))
            lightningPoolTag = "P4LightningNormal";
        if (string.IsNullOrEmpty(p4RageLightningPoolTag))
            p4RageLightningPoolTag = "P4LightningRage";
        if (string.IsNullOrEmpty(p2ProjectileSmokePoolTag))
            p2ProjectileSmokePoolTag = "P2ProjectileSmoke";
        if (string.IsNullOrEmpty(rage24SmokePoolTag))
            rage24SmokePoolTag = "P24RageSmoke";
        if (string.IsNullOrEmpty(rage1SmokePoolTag))
            rage1SmokePoolTag = "P1RageSmoke";
        if (string.IsNullOrEmpty(rage3SmokePoolTag))
            rage3SmokePoolTag = "P3RageSmoke";
        if (string.IsNullOrEmpty(rageSmokePoolTag))
            rageSmokePoolTag = "TransformRageSmoke";
        if (string.IsNullOrEmpty(rageSmokePoolTagSecondary))
            rageSmokePoolTagSecondary = rageSmokePoolTag;
        if (rageSmokePrefabSecondary != null &&
            rageSmokePrefabSecondary != rageSmokePrefab &&
            rageSmokePoolTagSecondary == rageSmokePoolTag)
        {
            rageSmokePoolTagSecondary = rageSmokePoolTag + "_Secondary";
        }
        if (string.IsNullOrEmpty(rageAttackSmokePoolTag))
            rageAttackSmokePoolTag = "P5RageAttackSmoke";
        if (string.IsNullOrEmpty(floorSmokePoolTag))
            floorSmokePoolTag = "FloorSmoke";
        if (string.IsNullOrEmpty(speedEffectPoolTag))
            speedEffectPoolTag = "SpeedEffect";

        // 기본 스모크(P1/P2 일반)가 미사일과 같은 "Smoke" 풀을 쓰면 잘못된 프리팹이 재사용될 수 있다.
        if (smokeEffectPrefab != null)
        {
            var mover = smokeEffectPrefab.GetComponent<SmokeMover>();
            if (mover != null && !string.IsNullOrEmpty(mover.PoolTag) && mover.PoolTag != "Smoke")
                smokePoolTag = mover.PoolTag;
            else if (string.IsNullOrEmpty(smokePoolTag) || smokePoolTag == "Smoke")
                smokePoolTag = "Smoke_P1";
        }

        // 노멀/분노 번개가 같은 풀 태그를 쓰면 프리팹이 섞여서 오동작한다.
        if (lightningPoolTag == p4RageLightningPoolTag)
            p4RageLightningPoolTag = lightningPoolTag + "_Rage";

        // P4 더미공이 일반 ProjectileBall 풀 태그를 쓰면 실제 프리팹이 섞일 수 있다.
        if (dummyProjectilePoolTag == projectilePoolTag)
            dummyProjectilePoolTag = projectilePoolTag + "_DummyP4";

        // P2 바닥 스모크는 기본 Smoke 풀과 분리해 미사일 스모크 오염을 방지한다.
        if (p2ProjectileSmokePoolTag == smokePoolTag)
            p2ProjectileSmokePoolTag = smokePoolTag + "_P2Ball";

        // P3 스모크는 프리팹에 지정된 전용 poolTag를 우선 사용
        if (smokeEffectPrefabP3 != null)
        {
            var mover = smokeEffectPrefabP3.GetComponent<SmokeMover>();
            if (mover != null && !string.IsNullOrEmpty(mover.PoolTag))
                smokeP3PoolTag = mover.PoolTag;
            else if (smokeP3PoolTag == smokePoolTag)
                smokeP3PoolTag = smokePoolTag + "_P3";
        }

        // P4 스모크는 프리팹에 지정된 전용 poolTag를 우선 사용
        if (smokeEffectPrefabP4 != null)
        {
            var mover = smokeEffectPrefabP4.GetComponent<SmokeMover>();
            if (mover != null && !string.IsNullOrEmpty(mover.PoolTag))
                smokeP4PoolTag = mover.PoolTag;
            else if (smokeP4PoolTag == smokePoolTag)
                smokeP4PoolTag = smokePoolTag + "_P4";
        }

        // P5 스모크도 기본 Smoke 풀과 분리해 미사일 스모크 오염을 방지한다.
        if (smokeEffectPrefabP5 != null)
        {
            var mover = smokeEffectPrefabP5.GetComponent<SmokeMover>();
            if (mover != null && !string.IsNullOrEmpty(mover.PoolTag))
                smokeP5PoolTag = mover.PoolTag;
            else if (smokeP5PoolTag == smokePoolTag)
                smokeP5PoolTag = smokePoolTag + "_P5";
        }

        if (floorSmokePrefab != null)
        {
            var mover = floorSmokePrefab.GetComponent<SmokeMover>();
            if (mover != null && !string.IsNullOrEmpty(mover.PoolTag))
                floorSmokePoolTag = mover.PoolTag;
        }
    }

    private void PrewarmEffectPools()
    {
        if (effectPoolsPrewarmed) return;
        if (!useEffectPool || ObjectPool.Instance == null) return;

        EnsureEffectPoolTagIsolation();

        PrewarmPool(projectilePoolTag, projectilePrefab ? projectilePrefab.gameObject : null, 24);

        GameObject projectileHitbox = projectilePrefab != null ? projectilePrefab.hitboxPrefab : null;
        PrewarmPool("ProjectileHitbox", projectileHitbox, 24);

        PrewarmPool(lightningPoolTag, lightningPrefab ? lightningPrefab.gameObject : null, 16);
        PrewarmPool(p4RageLightningPoolTag, p4RageLightningPrefab, 12);
        PrewarmPool(dummyProjectilePoolTag, dummySmokeBallPrefab ? dummySmokeBallPrefab.gameObject : null, 12);
        PrewarmPool(hitboxP1PoolTag, hitboxPrefabP1, 12);
        PrewarmPool(hitboxP3PoolTag, hitboxPrefabP3, 12);
        PrewarmPool(hitboxP5PoolTag, hitboxPrefabP5, 12);
        PrewarmPool(p2RageLaserPoolTag, p2RageLaserPrefab, 6);

        PrewarmPool(p2ProjectileSmokePoolTag, smokeEffectPrefab, 12);
        PrewarmPool(smokePoolTag, smokeEffectPrefab, 12);
        PrewarmPool(smokeP3PoolTag, smokeEffectPrefabP3, 12);
        PrewarmPool(smokeP4PoolTag, smokeEffectPrefabP4, 12);
        PrewarmPool(smokeP5PoolTag, smokeEffectPrefabP5, 12);
        PrewarmPool(floorSmokePoolTag, floorSmokePrefab, 12);
        PrewarmPool(rage24SmokePoolTag, rage24SmokePrefab, 12);
        PrewarmPool(rage1SmokePoolTag, rage1SmokePrefab, 12);
        PrewarmPool(rage3SmokePoolTag, rage3SmokePrefab, 12);
        int rageTransformSmokePoolSize = rageSmokePrefabSecondary == null && rageSmokePoolTagSecondary == rageSmokePoolTag
            ? 16
            : 8;
        PrewarmPool(rageSmokePoolTag, rageSmokePrefab, rageTransformSmokePoolSize);
        if (rageSmokePrefabSecondary != null || rageSmokePoolTagSecondary != rageSmokePoolTag)
        {
            GameObject secondaryPrefab = rageSmokePrefabSecondary != null ? rageSmokePrefabSecondary : rageSmokePrefab;
            PrewarmPool(rageSmokePoolTagSecondary, secondaryPrefab, 8);
        }
        PrewarmPool(rageAttackSmokePoolTag, rageAttackSmokePrefab, 8);
        int speedEffectPoolSize = speedEffectSpawnWorldPositions != null && speedEffectSpawnWorldPositions.Length > 0
            ? speedEffectSpawnWorldPositions.Length * 2
            : 4;
        PrewarmPool(speedEffectPoolTag, speedEffectPrefab, speedEffectPoolSize);

        effectPoolsPrewarmed = true;
    }

    private void PrewarmPool(string tag, GameObject prefab, int size)
    {
        if (ObjectPool.Instance == null || prefab == null || string.IsNullOrEmpty(tag)) return;
        ObjectPool.Instance.EnsurePoolSize(tag, prefab, size);
    }

}
