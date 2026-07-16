using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class MiniBoss : MonoBehaviour
{
    private const string WalkTriggerName = "walk";
    private const string TransformTriggerName = "transform";
    private const string AttackTriggerName = "attack";
    private const string LandTriggerName = "land";
    private const string SteadyTriggerName = "steady";
    private const string DieTriggerName = "die";
    private const string GateTag = "Gate";
    private const string FloorTag = "floor";
    private const string PlayerTag = "player";
    private const string ExcavatorTag = "Excavator";
    private enum MiniBossState
    {
        Firing,
        Rising,
        FallingToAttack,
        Attacking,
        FallingAfterImpact,
        Returning,
        Dying,
        Steady
    }

    [Header("Movement")]
    public float xSpeed = 2.5f;
    public float returnSpeed = 3.5f;
    public float fireRangeMinX = -1.5f;
    public float fireRangeMaxX = 1.5f;
    public float fireDuration = 4f;
    public float attackSpeed = 8f;
    public float riseOffsetY = 0.5f;
    public float riseSpeed = 2.5f;
    public float moveSmoothTime = 0.18f;
    public float riseSmoothTime = 0.14f;

    [Header("Player Hit Counts")]
    [Min(1)] public int player1HitCount = 6;
    [Min(1)] public int player2HitCount = 6;
    [Min(1)] public int player3HitCount = 6;
    [Min(1)] public int player4HitCount = 6;
    [Min(1)] public int player5HitCount = 6;

    [Header("Attack")]
    public Vector2 fireLocalPosition = Vector2.zero;
    public float fireInterval1 = 0.35f;
    public float fireInterval2 = 0.55f;
    public float fireInterval3 = 0.8f;
    public List<Vector2> attackTargetPositions = new List<Vector2>
    {
        new Vector2(-6f, -1f),
        new Vector2(-6f, 0f),
        new Vector2(-6f, 1f)
    };

    [Header("References")]
    public Slider lifeBarSlider;
    public HealthBarUI legacyLifeBar;
    public GameObject fireSmokePrefab;
    public GameObject miniBossBombPrefab;
    public GameObject hitEffectPrefab;
    public Animator targetAnimator;
    public Transform visualRoot;

    [Header("Animation")]
    [SerializeField] private float transformAnimationDuration = 0.4f;
    [SerializeField] private float postDieDelay = 0.5f;
    [SerializeField] private float blinkDuration = 0.5f;
    [SerializeField] private float blinkInterval = 0.1f;
    [SerializeField] private float hitFlashDuration = 0.1f;
    [SerializeField] private float shakeLeft = 0.1f;
    [SerializeField] private float shakeRight = 0.1f;
    [SerializeField] private float shakeTotalDuration = 0.1f;
    [SerializeField] private float groundCheckDistance = 0.2f;
    [SerializeField] private float groundContactThreshold = 0.05f;
    [SerializeField] private float floorSnapOffset = 0.02f;
    [SerializeField] private float initialGroundSearchDistance = 20f;
    [SerializeField] private float minAttackTravelTime = 0.45f;
    [SerializeField] private float maxAttackTravelTime = 1.15f;
    [SerializeField] private float postImpactFallSpeed = 2.5f;
    [SerializeField] private float postImpactBounceX = 0.3f;
    [SerializeField] private float postImpactBounceY = 1.2f;
    [SerializeField] private float postImpactBounceDuration = 0.18f;
    [SerializeField] private float minFallDistanceAfterImpact = 0.35f;
    [SerializeField] private float postImpactGroundIgnoreTime = 0.12f;
    [SerializeField] private float landTriggerDistance = 2f;

    private Rigidbody2D body2D;
    private Collider2D hitCollider;
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private Coroutine mainRoutine;
    private Coroutine hitFlashRoutine;
    private Coroutine deathRoutine;
    private Coroutine shakeRoutine;
    private MiniBossState state;
    private float currentGroundY;
    private float fireTargetX;
    private float returnTargetX;
    private int currentHits;
    private int requiredHits;
    private bool attackLaunchPending;
    private bool impactResolved;
    private bool isDead;
    private bool deathPendingUntilGround;
    private float impactStartY = float.NaN;
    private float horizontalMoveVelocity;
    private float verticalMoveVelocity;
    private float postImpactGroundRestoreTime;
    private float baseColliderHeight = 1f;
    private float baseColliderWidth = 1f;
    private bool landTriggerPlayedThisFall;
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
    private readonly List<Collider2D> ignoredColliders = new List<Collider2D>(64);
    private readonly List<Collider2D> temporarilyIgnoredGateColliders = new List<Collider2D>(8);
    private readonly List<Collider2D> temporarilyIgnoredImpactColliders = new List<Collider2D>(16);
    private readonly List<Collider2D> temporarilyIgnoredGroundColliders = new List<Collider2D>(16);

    private void Awake()
    {
        body2D = GetComponent<Rigidbody2D>();
        hitCollider = GetComponent<Collider2D>();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);

        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (visualRoot == null && targetAnimator != null)
            visualRoot = targetAnimator.transform;

        if (body2D != null)
        {
            body2D.freezeRotation = true;
            body2D.gravityScale = 0f;
            body2D.bodyType = RigidbodyType2D.Kinematic;
            body2D.useFullKinematicContacts = true;
            body2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        if (hitCollider != null)
        {
            Bounds bounds = hitCollider.bounds;
            baseColliderHeight = Mathf.Max(0.01f, bounds.size.y);
            baseColliderWidth = Mathf.Max(0.01f, bounds.size.x);
        }

        CacheOriginalColors();
    }

    private void OnEnable()
    {
        GameData.OnGameOver += HandleGameOver;
        InitializeRuntime();
        RefreshCollisionInteractions();
    }

    private void OnDisable()
    {
        GameData.OnGameOver -= HandleGameOver;

        if (mainRoutine != null)
        {
            StopCoroutine(mainRoutine);
            mainRoutine = null;
        }

        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }

        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }

        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }

        RestoreTemporarilyIgnoredImpactCollisions();
        RestoreTemporarilyIgnoredGateCollisions();
        RestoreTemporarilyIgnoredGroundCollisions();
        RestoreIgnoredCollisions();
    }

    private void Update()
    {
        if (isDead || state == MiniBossState.Steady)
            return;

        if (ShouldEnterSteadyState())
        {
            if (IsTouchingFloorSurface())
                EnterSteadyState();
            return;
        }

        if (RageTransformFreezeController.ShouldSkipGameplayFrame())
            return;

        float stageMult = GetStageSpeedMultiplier();

        switch (state)
        {
            case MiniBossState.Firing:
                MoveHorizontallyTowards(fireTargetX, xSpeed * stageMult, lockToGround: true);
                break;
            case MiniBossState.Rising:
                MoveVerticallyTowards(currentGroundY + riseOffsetY, riseSpeed * stageMult);
                break;
            case MiniBossState.Returning:
                MoveHorizontallyTowards(returnTargetX, returnSpeed * stageMult, lockToGround: true);
                break;
        }

        if (state == MiniBossState.FallingToAttack || state == MiniBossState.Attacking || state == MiniBossState.FallingAfterImpact)
            CheckGroundContactManually();

        if (state == MiniBossState.FallingAfterImpact)
            TryRestoreGroundCollisionsAfterImpact();

        if ((state == MiniBossState.Attacking || state == MiniBossState.FallingAfterImpact) && IsTouchingWalkRecoveryFloor())
            ForceRecoverToWalkOnFloor();

        if (state == MiniBossState.FallingAfterImpact)
            TryPlayLandTriggerBeforeTouchdown();

        if (deathPendingUntilGround && IsTouchingWalkRecoveryFloor())
            StartDeathNow();
    }

    public void RegisterHitFromPlayerAttack()
    {
        RegisterHitFromPlayerAttack(0);
    }

    public void RegisterHitFromPlayerAttack(int sourcePlayerType)
    {
        if (isDead || state == MiniBossState.Steady)
            return;

        if (sourcePlayerType > 0)
            requiredHits = GetRequiredHitCountForPlayerType(sourcePlayerType);

        currentHits++;
        UpdateLifeBar();
        PlayHitFlash();

        if (currentHits >= requiredHits)
            StartDeath();
    }

    public void Hit(int damage)
    {
        RegisterHitFromPlayerAttack();
    }

    public void TakeDamage(int damage)
    {
        RegisterHitFromPlayerAttack();
    }

    private void InitializeRuntime()
    {
        isDead = false;
        state = MiniBossState.Firing;
        attackLaunchPending = false;
        impactResolved = false;
        horizontalMoveVelocity = 0f;
        verticalMoveVelocity = 0f;
        currentGroundY = transform.position.y;
        currentHits = 0;
        requiredHits = GetRequiredHitCountForCurrentPlayer();
        fireTargetX = GetRandomFireX();
        returnTargetX = fireTargetX;
        deathPendingUntilGround = false;
        landTriggerPlayedThisFall = false;

        RestoreSpriteColors();
        ConfigureForKinematicMovement();
        RestoreTemporarilyIgnoredImpactCollisions();
        RestoreTemporarilyIgnoredGateCollisions();
        RestoreTemporarilyIgnoredGroundCollisions();
        impactStartY = float.NaN;
        postImpactGroundRestoreTime = 0f;
        SnapToInitialGroundIfPossible();
        ApplyWalkAnimation();
        UpdateLifeBar();

        if (mainRoutine != null)
            StopCoroutine(mainRoutine);
        mainRoutine = StartCoroutine(CoMainLoop());
    }

    private IEnumerator CoMainLoop()
    {
        while (!isDead)
        {
            state = MiniBossState.Firing;
            ApplyWalkAnimation();
            yield return CoFirePhase();
            if (ShouldStopLoop()) yield break;

            yield return CoRiseAndTransform();
            if (ShouldStopLoop()) yield break;

            yield return CoAttackPhase();
            if (ShouldStopLoop()) yield break;

            yield return CoReturnPhase();
            if (ShouldStopLoop()) yield break;
        }
    }

    private IEnumerator CoFirePhase()
    {
        float elapsed = 0f;
        float nextRetargetTime = 0f;
        float fireTime = Mathf.Min(GetRandomFireInterval(), Mathf.Max(0f, fireDuration));
        bool hasFired = false;

        while (elapsed < fireDuration && !ShouldStopLoop())
        {
            if (elapsed >= nextRetargetTime)
            {
                fireTargetX = GetRandomFireX();
                nextRetargetTime = elapsed + Random.Range(0.35f, 0.8f);
            }

            if (!hasFired && elapsed >= fireTime)
            {
                FireBombs();
                hasFired = true;
            }

            yield return null;
            elapsed += Time.deltaTime;
        }
    }

    private IEnumerator CoRiseAndTransform()
    {
        state = MiniBossState.Rising;
        ConfigureForKinematicMovement();
        SetAnimatorTrigger(TransformTriggerName);

        while (!ShouldStopLoop() && Mathf.Abs(transform.position.y - (currentGroundY + GetScaledRiseOffsetY())) > 0.01f)
            yield return null;
    }

    private IEnumerator CoAttackPhase()
    {
        state = MiniBossState.FallingToAttack;
        attackLaunchPending = true;
        impactResolved = false;

        SetAnimatorTrigger(AttackTriggerName);
        if (transformAnimationDuration > 0f)
            yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(transformAnimationDuration);
        ConfigureForDynamicMovement();

        while (!ShouldStopLoop() && !impactResolved)
            yield return null;
    }

    private IEnumerator CoReturnPhase()
    {
        state = MiniBossState.Returning;
        ConfigureForKinematicMovement();
        ApplyWalkAnimation();
        returnTargetX = GetRandomFireX();

        while (!ShouldStopLoop() && Mathf.Abs(transform.position.x - returnTargetX) > 0.02f)
            yield return null;

        currentGroundY = transform.position.y;
        RestoreTemporarilyIgnoredImpactCollisions();
        RestoreTemporarilyIgnoredGateCollisions();
    }

    private void FireBombs()
    {
        if (miniBossBombPrefab == null)
            return;

        Vector3 fireWorldPosition = transform.TransformPoint(new Vector3(fireLocalPosition.x, fireLocalPosition.y, 0f));

        if (fireSmokePrefab != null)
            Instantiate(fireSmokePrefab, fireWorldPosition, fireSmokePrefab.transform.rotation);

        GameObject spawnedBomb = Instantiate(miniBossBombPrefab, fireWorldPosition, miniBossBombPrefab.transform.rotation);
        Transform playerTarget = FindFirstObjectByType<Player>()?.transform;
        if (spawnedBomb != null && playerTarget != null)
            spawnedBomb.SendMessage("SetTarget", playerTarget, SendMessageOptions.DontRequireReceiver);
    }

    private void LaunchAttackArc()
    {
        Vector2 attackTarget = GetRandomAttackTarget();
        float targetX = attackTarget.x;
        float targetY = attackTarget.y;
        float startX = transform.position.x;
        float startY = transform.position.y;
        float horizontalDistance = targetX - startX;
        if (Mathf.Abs(horizontalDistance) < 0.25f)
            horizontalDistance = horizontalDistance < 0f ? -0.25f : 0.25f;

        float desiredHorizontalSpeed = Mathf.Max(0.01f, Mathf.Abs(attackSpeed) * GetWorldScaleFactorX());
        float timeToTarget = Mathf.Abs(horizontalDistance) / desiredHorizontalSpeed;
        timeToTarget = Mathf.Clamp(timeToTarget, minAttackTravelTime, maxAttackTravelTime);
        float velocityX = horizontalDistance / timeToTarget;
        float gravity = Physics2D.gravity.y * Mathf.Max(0.01f, body2D.gravityScale);
        float velocityY = (targetY - startY - (0.5f * gravity * timeToTarget * timeToTarget)) / timeToTarget;

        body2D.linearVelocity = new Vector2(velocityX, velocityY);
        state = MiniBossState.Attacking;
    }

    private void ResolveImpactWithGate()
    {
        GateHealth gate = GateHealth.Instance;
        if (gate != null)
            gate.TakeBossMissileHit();

        ResolveAttackImpact(gate, null);
    }

    private void ResolveImpactWithPlayer(Player player)
    {
        ResolveAttackImpact(null, player);
    }

    private void ResolveAttackImpact(GateHealth gate, Player player)
    {
        if (state != MiniBossState.Attacking)
            return;

        float bounceDuration = Mathf.Max(0.01f, postImpactBounceDuration);
        float bounceVelocityX = Mathf.Abs(postImpactBounceX) * GetWorldScaleFactorX() / bounceDuration;
        float bounceVelocityY = Mathf.Max(0.1f, postImpactBounceY * GetWorldScaleFactorY());
        impactStartY = transform.position.y;
        body2D.linearVelocity = new Vector2(bounceVelocityX, bounceVelocityY);
        state = MiniBossState.FallingAfterImpact;
        postImpactGroundRestoreTime = Time.time + Mathf.Max(0f, postImpactGroundIgnoreTime);
        landTriggerPlayedThisFall = false;

        RestoreTemporarilyIgnoredImpactCollisions();
        IgnoreGateAndPlayerCollisionsAfterImpact(gate, player);
        RestoreTemporarilyIgnoredGroundCollisions();
        IgnoreGroundCollisionsAfterImpact();
    }

    private void IgnoreGateAndPlayerCollisionsAfterImpact(GateHealth gate, Player player)
    {
        if (hitCollider == null)
            return;

        GateHealth gateTarget = gate != null ? gate : GateHealth.Instance;
        if (gateTarget != null)
        {
            Collider2D[] gateColliders = gateTarget.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < gateColliders.Length; i++)
            {
                Collider2D gateCollider = gateColliders[i];
                if (gateCollider == null)
                    continue;

                Physics2D.IgnoreCollision(hitCollider, gateCollider, true);
                if (!temporarilyIgnoredImpactColliders.Contains(gateCollider))
                    temporarilyIgnoredImpactColliders.Add(gateCollider);
            }
        }

        Player playerTarget = player != null ? player : Player.Instance;
        if (playerTarget != null)
        {
            Collider2D[] playerColliders = playerTarget.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < playerColliders.Length; i++)
            {
                Collider2D playerCollider = playerColliders[i];
                if (playerCollider == null)
                    continue;

                Physics2D.IgnoreCollision(hitCollider, playerCollider, true);
                if (!temporarilyIgnoredImpactColliders.Contains(playerCollider))
                    temporarilyIgnoredImpactColliders.Add(playerCollider);
            }
        }
    }

    private void HandleLandingOnFloor(Collider2D floorCollider, float? groundSurfaceY = null)
    {
        if (state == MiniBossState.FallingToAttack && attackLaunchPending)
        {
            SnapToGround(floorCollider, groundSurfaceY);
            attackLaunchPending = false;
            LaunchAttackArc();
            return;
        }

        if (state == MiniBossState.Attacking)
        {
            if (!IsWalkRecoveryFloor(floorCollider))
                return;

            CompleteGroundRecovery(floorCollider, groundSurfaceY);
            return;
        }

        if (state == MiniBossState.FallingAfterImpact)
        {
            if (!IsWalkRecoveryFloor(floorCollider))
                return;

            if (!CanResolvePostImpactLanding() && !IsTouchingWalkRecoveryFloor())
                return;

            CompleteGroundRecovery(floorCollider, groundSurfaceY);
        }
    }

    private void StartDeath()
    {
        if (isDead || deathPendingUntilGround)
            return;

        if (!IsTouchingWalkRecoveryFloor())
        {
            deathPendingUntilGround = true;
            return;
        }

        StartDeathNow();
    }

    private void StartDeathNow()
    {
        if (isDead)
            return;

        isDead = true;
        deathPendingUntilGround = false;
        state = MiniBossState.Dying;

        if (mainRoutine != null)
        {
            StopCoroutine(mainRoutine);
            mainRoutine = null;
        }

        if (hitCollider != null)
            hitCollider.enabled = false;

        body2D.linearVelocity = Vector2.zero;
        body2D.angularVelocity = 0f;
        body2D.simulated = false;

        SetAnimatorTrigger(DieTriggerName);

        if (deathRoutine != null)
            StopCoroutine(deathRoutine);
        deathRoutine = StartCoroutine(CoDeathSequence());
    }

    private IEnumerator CoDeathSequence()
    {
        if (postDieDelay > 0f)
            yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(postDieDelay);

        float elapsed = 0f;
        bool visible = false;

        while (elapsed < blinkDuration)
        {
            visible = !visible;
            SetSpritesAlpha(visible ? 0.5f : 0f);
            yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(blinkInterval);
            elapsed += blinkInterval;
        }

        deathRoutine = null;
        Destroy(gameObject);
    }

    private void PlayHitFlash()
    {
        SpawnHitEffect();

        if (hitFlashRoutine != null)
            StopCoroutine(hitFlashRoutine);
        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);

        hitFlashRoutine = StartCoroutine(CoHitFlash());
        shakeRoutine = StartCoroutine(CoHitShake());
    }

    private void SpawnHitEffect()
    {
        if (hitEffectPrefab == null)
            return;

        Vector3 spawnPosition = hitCollider != null ? hitCollider.bounds.center : transform.position;
        Instantiate(hitEffectPrefab, spawnPosition, hitEffectPrefab.transform.rotation);
    }

    private IEnumerator CoHitFlash()
    {
        SetSpriteColors(Color.red);

        if (hitFlashDuration > 0f)
            yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(hitFlashDuration);
        RestoreSpriteColors();
        hitFlashRoutine = null;
    }

    private IEnumerator CoHitShake()
    {
        if (visualRoot == null)
        {
            shakeRoutine = null;
            yield break;
        }

        Vector3 baseLocal = visualRoot.localPosition;
        float total = Mathf.Max(0.01f, shakeTotalDuration);
        float t0 = total * 0.33f;
        float t1 = total * 0.33f;
        float t2 = total - t0 - t1;

        visualRoot.localPosition = baseLocal + Vector3.left * shakeLeft;
        yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(t0);

        visualRoot.localPosition = baseLocal + Vector3.right * shakeRight;
        yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(t1);

        visualRoot.localPosition = baseLocal;
        if (t2 > 0f)
            yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(t2);

        shakeRoutine = null;
    }

    private void UpdateLifeBar()
    {
        int remain = Mathf.Max(0, requiredHits - currentHits);

        if (lifeBarSlider != null)
        {
            lifeBarSlider.wholeNumbers = true;
            lifeBarSlider.maxValue = requiredHits;
            lifeBarSlider.value = remain;
        }

        if (legacyLifeBar != null)
            legacyLifeBar.SetHealth(remain);
    }

    private void EnterSteadyState()
    {
        if (isDead || state == MiniBossState.Steady)
            return;

        SnapToTouchingFloorIfPossible();

        state = MiniBossState.Steady;

        if (mainRoutine != null)
        {
            StopCoroutine(mainRoutine);
            mainRoutine = null;
        }

        body2D.linearVelocity = Vector2.zero;
        body2D.angularVelocity = 0f;
        body2D.simulated = false;

        SetAnimatorTrigger(SteadyTriggerName);
    }

    private void HandleGameOver()
    {
        if (IsTouchingFloorSurface())
            EnterSteadyState();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleCollision(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null)
            return;

        HandleCollision(collision.collider);
    }

    private void HandleCollision(Collider2D other)
    {
        if (other == null || isDead)
            return;

        if (IsFloorCollider(other))
        {
            return;
        }

        if (IsPlayerAttackCollider(other))
            return;

        if (state != MiniBossState.Attacking)
            return;

        if (IsGateImpactCollider(other))
        {
            ResolveImpactWithGate();
            return;
        }

        if (IsPlayerBodyCollider(other, out Player player))
        {
            ResolveImpactWithPlayer(player);
        }
    }

    private void MoveHorizontallyTowards(float targetX, float speed, bool lockToGround)
    {
        Vector3 current = transform.position;
        float smoothTime = Mathf.Max(0.01f, moveSmoothTime);
        float maxSpeed = Mathf.Max(0.01f, speed * GetWorldScaleFactorX());
        float nextX = Mathf.SmoothDamp(current.x, targetX, ref horizontalMoveVelocity, smoothTime, maxSpeed);
        float y = lockToGround ? currentGroundY : current.y;
        transform.position = new Vector3(nextX, y, current.z);
    }

    private void MoveVerticallyTowards(float targetY, float speed)
    {
        Vector3 current = transform.position;
        float smoothTime = Mathf.Max(0.01f, riseSmoothTime);
        float maxSpeed = Mathf.Max(0.01f, speed * GetWorldScaleFactorY());
        float nextY = Mathf.SmoothDamp(current.y, targetY, ref verticalMoveVelocity, smoothTime, maxSpeed);
        transform.position = new Vector3(current.x, nextY, current.z);
    }

    private void ConfigureForKinematicMovement()
    {
        if (body2D == null)
            return;

        body2D.simulated = true;
        body2D.bodyType = RigidbodyType2D.Kinematic;
        body2D.gravityScale = 0f;
        body2D.linearVelocity = Vector2.zero;
        body2D.angularVelocity = 0f;
        horizontalMoveVelocity = 0f;
        verticalMoveVelocity = 0f;
    }

    private void ConfigureForDynamicMovement()
    {
        if (body2D == null)
            return;

        body2D.simulated = true;
        body2D.bodyType = RigidbodyType2D.Dynamic;
        body2D.gravityScale = 1f;
        body2D.linearVelocity = Vector2.zero;
        body2D.angularVelocity = 0f;
        horizontalMoveVelocity = 0f;
        verticalMoveVelocity = 0f;
    }

    private void ApplyWalkAnimation()
    {
        SetAnimatorTrigger(WalkTriggerName);
    }

    private void SetAnimatorTrigger(string triggerName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(triggerName))
            return;

        targetAnimator.ResetTrigger(WalkTriggerName);
        targetAnimator.ResetTrigger(TransformTriggerName);
        targetAnimator.ResetTrigger(AttackTriggerName);
        targetAnimator.ResetTrigger(LandTriggerName);
        targetAnimator.ResetTrigger(SteadyTriggerName);
        targetAnimator.ResetTrigger(DieTriggerName);
        targetAnimator.SetTrigger(triggerName);
    }

    private float GetRandomFireX()
    {
        float min = Mathf.Min(fireRangeMinX, fireRangeMaxX);
        float max = Mathf.Max(fireRangeMinX, fireRangeMaxX);
        return Random.Range(min, max);
    }

    private float GetRandomFireInterval()
    {
        float[] intervals = { fireInterval1, fireInterval2, fireInterval3 };
        List<float> valid = new List<float>(3);

        for (int i = 0; i < intervals.Length; i++)
        {
            if (intervals[i] > 0f)
                valid.Add(intervals[i]);
        }

        if (valid.Count == 0)
            return 0.5f;

        return valid[Random.Range(0, valid.Count)];
    }

    private Vector2 GetRandomAttackTarget()
    {
        if (attackTargetPositions == null || attackTargetPositions.Count == 0)
            return new Vector2(transform.position.x - 3f, transform.position.y);

        return attackTargetPositions[Random.Range(0, attackTargetPositions.Count)];
    }

    private int GetRequiredHitCountForCurrentPlayer()
    {
        int playerType = GameData.Instance != null ? Mathf.Clamp(GameData.Instance.selectedPlayerType, 1, 5) : 1;
        return GetRequiredHitCountForPlayerType(playerType);
    }

    private int GetRequiredHitCountForPlayerType(int playerType)
    {
        playerType = Mathf.Clamp(playerType, 1, 5);
        switch (playerType)
        {
            case 1: return Mathf.Max(1, player1HitCount);
            case 2: return Mathf.Max(1, player2HitCount);
            case 3: return Mathf.Max(1, player3HitCount);
            case 4: return Mathf.Max(1, player4HitCount);
            case 5: return Mathf.Max(1, player5HitCount);
            default: return Mathf.Max(1, player1HitCount);
        }
    }

    private float GetStageSpeedMultiplier()
    {
        if (GameData.Instance == null)
            return 1f;

        return Mathf.Max(0f, GameData.Instance.GetStageSpeedMult());
    }

    private bool ShouldStopLoop()
    {
        return isDead || state == MiniBossState.Steady || (GameData.Instance != null && GameData.Instance.gameOver);
    }

    private bool ShouldEnterSteadyState()
    {
        if (GameData.Instance == null)
            return false;

        return GameData.Instance.gameOver || GameData.Instance.GetStageSpeedMult() <= 0f;
    }

    private bool IsGateCollider(Collider2D other)
    {
        return other.CompareTag(GateTag)
            || other.GetComponent<GateHealth>() != null
            || other.GetComponentInParent<GateHealth>() != null;
    }

    private bool IsGateImpactCollider(Collider2D other)
    {
        return other != null && IsGateCollider(other);
    }

    private bool IsFloorCollider(Collider2D other)
    {
        if (other == null)
            return false;

        return other.CompareTag(FloorTag);
    }

    private bool IsWalkRecoveryFloor(Collider2D other)
    {
        return other != null && other.CompareTag(FloorTag);
    }

    private void RefreshCollisionInteractions()
    {
        RestoreIgnoredCollisions();

        if (hitCollider == null)
            return;

        Collider2D[] allColliders = FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider2D other = allColliders[i];
            if (other == null || other == hitCollider)
                continue;

            if (other.transform == transform || other.transform.IsChildOf(transform) || transform.IsChildOf(other.transform))
                continue;

            if (ShouldAllowPhysicalInteraction(other))
                continue;

            Physics2D.IgnoreCollision(hitCollider, other, true);
            ignoredColliders.Add(other);
        }
    }

    private void RestoreIgnoredCollisions()
    {
        if (hitCollider == null || ignoredColliders.Count == 0)
            return;

        for (int i = 0; i < ignoredColliders.Count; i++)
        {
            Collider2D other = ignoredColliders[i];
            if (other != null)
                Physics2D.IgnoreCollision(hitCollider, other, false);
        }

        ignoredColliders.Clear();
    }

    private void RestoreTemporarilyIgnoredGateCollisions()
    {
        if (hitCollider == null || temporarilyIgnoredGateColliders.Count == 0)
            return;

        for (int i = 0; i < temporarilyIgnoredGateColliders.Count; i++)
        {
            Collider2D other = temporarilyIgnoredGateColliders[i];
            if (other != null)
                Physics2D.IgnoreCollision(hitCollider, other, false);
        }

        temporarilyIgnoredGateColliders.Clear();
    }

    private static bool ShouldAllowPhysicalInteraction(Collider2D other)
    {
        if (other == null)
            return false;

        if (other.CompareTag(FloorTag) || other.CompareTag(PlayerTag) || other.CompareTag(GateTag))
            return true;

        return other.GetComponentInParent<GateHealth>() != null
            || IsPlayerAttackCollider(other);
    }

    private void CheckGroundContactManually()
    {
        if (hitCollider == null || body2D == null)
            return;

        if (body2D.linearVelocity.y > 0f)
            return;

        Bounds bounds = hitCollider.bounds;
        float castDistance = Mathf.Max(GetScaledGroundCheckDistance(), Mathf.Abs(body2D.linearVelocity.y * Time.deltaTime) + GetScaledGroundContactThreshold());
        Vector2 origin = new Vector2(bounds.center.x, bounds.min.y + 0.02f);
        Vector2 size = new Vector2(Mathf.Max(0.05f, bounds.size.x * 0.8f), Mathf.Max(0.05f, bounds.size.y * 0.08f));

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false
        };

        int hitCount = Physics2D.BoxCast(origin, size, 0f, Vector2.down, filter, groundHits, castDistance);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = groundHits[i];
            Collider2D other = hit.collider;
            if (!IsFloorCollider(other))
                continue;

            if (hit.distance > GetScaledGroundContactThreshold())
                continue;

            if (!CanTreatAsLandingSurface(hit))
                continue;

            if (state == MiniBossState.FallingAfterImpact && !CanResolvePostImpactLanding())
                continue;

            HandleLandingOnFloor(other, hit.point.y);
            return;
        }
    }

    private bool IsTouchingFloorSurface()
    {
        if (hitCollider == null)
            return false;

        Bounds bounds = hitCollider.bounds;
        Vector2 origin = new Vector2(bounds.center.x, bounds.min.y + 0.02f);
        Vector2 size = new Vector2(Mathf.Max(0.05f, bounds.size.x * 0.8f), Mathf.Max(0.05f, bounds.size.y * 0.08f));

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false
        };

        int hitCount = Physics2D.BoxCast(origin, size, 0f, Vector2.down, filter, groundHits, GetScaledGroundContactThreshold() + 0.02f);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D other = groundHits[i].collider;
            if (!IsFloorCollider(other))
                continue;

            if (groundHits[i].distance <= GetScaledGroundContactThreshold() + 0.02f)
                return true;
        }

        return false;
    }

    private bool IsTouchingWalkRecoveryFloor()
    {
        if (hitCollider == null)
            return false;

        Bounds bounds = hitCollider.bounds;
        Vector2 origin = new Vector2(bounds.center.x, bounds.min.y + 0.02f);
        Vector2 size = new Vector2(Mathf.Max(0.05f, bounds.size.x * 0.8f), Mathf.Max(0.05f, bounds.size.y * 0.08f));

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false
        };

        int hitCount = Physics2D.BoxCast(origin, size, 0f, Vector2.down, filter, groundHits, GetScaledGroundContactThreshold() + 0.02f);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D other = groundHits[i].collider;
            if (!IsWalkRecoveryFloor(other))
                continue;

            if (groundHits[i].distance <= GetScaledGroundContactThreshold() + 0.02f)
                return true;
        }

        return false;
    }

    private void SnapToTouchingFloorIfPossible()
    {
        if (hitCollider == null)
            return;

        Bounds bounds = hitCollider.bounds;
        Vector2 origin = new Vector2(bounds.center.x, bounds.min.y + 0.02f);
        Vector2 size = new Vector2(Mathf.Max(0.05f, bounds.size.x * 0.8f), Mathf.Max(0.05f, bounds.size.y * 0.08f));

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false
        };

        int hitCount = Physics2D.BoxCast(origin, size, 0f, Vector2.down, filter, groundHits, GetScaledGroundContactThreshold() + 0.02f);
        float nearestDistance = float.MaxValue;
        Collider2D bestFloor = null;
        float bestSurfaceY = 0f;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = groundHits[i];
            Collider2D other = hit.collider;
            if (!IsFloorCollider(other))
                continue;

            if (hit.distance > nearestDistance)
                continue;

            nearestDistance = hit.distance;
            bestFloor = other;
            bestSurfaceY = hit.point.y;
        }

        if (bestFloor != null)
            SnapToGround(bestFloor, bestSurfaceY);
    }

    private static bool IsPlayerAttackCollider(Collider2D other)
    {
        if (other == null)
            return false;

        if (other.GetComponent<Hitbox>() != null || other.GetComponent<ZigzagLightning>() != null || other.GetComponent<ProjectileBall>() != null)
            return true;

        Transform parent = other.transform.parent;
        if (parent != null)
        {
            if (parent.GetComponent<Hitbox>() != null || parent.GetComponent<ZigzagLightning>() != null || parent.GetComponent<ProjectileBall>() != null)
                return true;
        }

        Transform root = other.transform.root;
        if (root != null)
        {
            if (root.GetComponent<Hitbox>() != null || root.GetComponent<ZigzagLightning>() != null || root.GetComponent<ProjectileBall>() != null)
                return true;
        }

        return false;
    }

    private static bool IsPlayerBodyCollider(Collider2D other, out Player player)
    {
        player = null;
        if (other == null)
            return false;

        if (IsPlayerAttackCollider(other))
            return false;

        player = other.GetComponent<Player>() ?? other.GetComponentInParent<Player>();
        if (player == null && !other.CompareTag(PlayerTag))
            return false;

        if (other.isTrigger)
            return false;

        return true;
    }

    private static bool CanTreatAsLandingSurface(RaycastHit2D hit)
    {
        if (hit.collider == null)
            return false;

        return hit.normal.y >= 0.45f;
    }

    private void SnapToGround(Collider2D floorCollider, float? groundSurfaceY = null)
    {
        if (hitCollider == null || floorCollider == null)
            return;

        float surfaceY = groundSurfaceY ?? GetGroundSurfaceY(floorCollider);
        float snappedY = surfaceY + hitCollider.bounds.extents.y - hitCollider.offset.y + GetScaledFloorSnapOffset();
        Vector3 pos = transform.position;
        transform.position = new Vector3(pos.x, snappedY, pos.z);
        currentGroundY = snappedY;
    }

    private void SnapToInitialGroundIfPossible()
    {
        if (hitCollider == null)
        {
            currentGroundY = transform.position.y;
            return;
        }

        Bounds bounds = hitCollider.bounds;
        Vector2 origin = new Vector2(bounds.center.x, bounds.min.y + 0.02f);
        Vector2 size = new Vector2(Mathf.Max(0.05f, bounds.size.x * 0.8f), Mathf.Max(0.05f, bounds.size.y * 0.08f));

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false
        };

        int hitCount = Physics2D.BoxCast(origin, size, 0f, Vector2.down, filter, groundHits, Mathf.Max(initialGroundSearchDistance * GetWorldScaleFactorY(), GetScaledGroundCheckDistance()));
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D other = groundHits[i].collider;
            if (!IsFloorCollider(other))
                continue;

            float groundSurfaceY = groundHits[i].point.y;
            SnapToGround(other, groundSurfaceY);
            return;
        }

        currentGroundY = transform.position.y;
    }

    private float GetGroundSurfaceY(Collider2D floorCollider)
    {
        if (hitCollider == null || floorCollider == null)
            return transform.position.y;

        ColliderDistance2D distance = hitCollider.Distance(floorCollider);
        if (distance.isValid)
            return distance.pointB.y;

        return floorCollider.ClosestPoint(hitCollider.bounds.center).y;
    }

    private void RestoreTemporarilyIgnoredImpactCollisions()
    {
        if (hitCollider == null || temporarilyIgnoredImpactColliders.Count == 0)
            return;

        for (int i = 0; i < temporarilyIgnoredImpactColliders.Count; i++)
        {
            Collider2D other = temporarilyIgnoredImpactColliders[i];
            if (other != null)
                Physics2D.IgnoreCollision(hitCollider, other, false);
        }

        temporarilyIgnoredImpactColliders.Clear();
    }

    private void IgnoreGroundCollisionsAfterImpact()
    {
        if (hitCollider == null)
            return;

        Collider2D[] allColliders = FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider2D other = allColliders[i];
            if (!IsFloorCollider(other) || other == hitCollider)
                continue;

            Physics2D.IgnoreCollision(hitCollider, other, true);
            if (!temporarilyIgnoredGroundColliders.Contains(other))
                temporarilyIgnoredGroundColliders.Add(other);
        }
    }

    private void RestoreTemporarilyIgnoredGroundCollisions()
    {
        if (hitCollider == null || temporarilyIgnoredGroundColliders.Count == 0)
            return;

        for (int i = 0; i < temporarilyIgnoredGroundColliders.Count; i++)
        {
            Collider2D other = temporarilyIgnoredGroundColliders[i];
            if (other != null)
                Physics2D.IgnoreCollision(hitCollider, other, false);
        }

        temporarilyIgnoredGroundColliders.Clear();
    }

    private void TryRestoreGroundCollisionsAfterImpact()
    {
        if (temporarilyIgnoredGroundColliders.Count == 0)
            return;

        if (Time.time < postImpactGroundRestoreTime)
            return;

        RestoreTemporarilyIgnoredGroundCollisions();

        if (state == MiniBossState.FallingAfterImpact && IsTouchingWalkRecoveryFloor())
            CompleteGroundRecovery(null, null);
    }

    private bool CanResolvePostImpactLanding()
    {
        if (Time.time < postImpactGroundRestoreTime)
            return false;

        if (body2D != null && body2D.linearVelocity.y > -0.01f)
            return false;

        if (!float.IsNaN(impactStartY))
        {
            float fallenDistance = impactStartY - transform.position.y;
            if (fallenDistance < GetScaledMinFallDistanceAfterImpact())
                return false;
        }

        return true;
    }

    private void CompleteGroundRecovery(Collider2D floorCollider, float? groundSurfaceY)
    {
        if (floorCollider != null)
            SnapToGround(floorCollider, groundSurfaceY);

        bool landedFromAttack = state == MiniBossState.Attacking || state == MiniBossState.FallingAfterImpact;

        impactResolved = true;
        impactStartY = float.NaN;
        postImpactGroundRestoreTime = 0f;
        landTriggerPlayedThisFall = false;

        if (body2D != null)
        {
            body2D.linearVelocity = Vector2.zero;
            body2D.angularVelocity = 0f;
        }

        RestoreTemporarilyIgnoredGroundCollisions();
        ApplyWalkAnimation();

        if (landedFromAttack)
            CameraShakeManager.ShakeDefaultHalf();
    }

    private void ForceRecoverToWalkOnFloor()
    {
        if (body2D != null && body2D.linearVelocity.y > 0.05f)
            return;

        CompleteGroundRecovery(null, null);
    }

    private void TryPlayLandTriggerBeforeTouchdown()
    {
        if (landTriggerPlayedThisFall || targetAnimator == null)
            return;

        if (body2D != null && body2D.linearVelocity.y > 0f)
            return;

        if (!TryGetWalkRecoveryFloorHit(Mathf.Max(0.01f, landTriggerDistance), out _))
            return;

        landTriggerPlayedThisFall = true;
        SetAnimatorTrigger(LandTriggerName);
    }

    private bool TryGetWalkRecoveryFloorHit(float castDistance, out RaycastHit2D floorHit)
    {
        floorHit = default;

        if (hitCollider == null)
            return false;

        Bounds bounds = hitCollider.bounds;
        Vector2 origin = new Vector2(bounds.center.x, bounds.min.y + 0.02f);
        Vector2 size = new Vector2(Mathf.Max(0.05f, bounds.size.x * 0.8f), Mathf.Max(0.05f, bounds.size.y * 0.08f));

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false
        };

        int hitCount = Physics2D.BoxCast(origin, size, 0f, Vector2.down, filter, groundHits, castDistance);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = groundHits[i];
            Collider2D other = hit.collider;
            if (!IsWalkRecoveryFloor(other))
                continue;

            if (!CanTreatAsLandingSurface(hit))
                continue;

            floorHit = hit;
            return true;
        }

        return false;
    }

    private float GetWorldScaleFactorX()
    {
        if (hitCollider == null)
            return Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.x));

        float currentWidth = Mathf.Max(0.01f, hitCollider.bounds.size.x);
        return Mathf.Max(0.01f, currentWidth / Mathf.Max(0.01f, baseColliderWidth));
    }

    private float GetWorldScaleFactorY()
    {
        if (hitCollider == null)
            return Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.y));

        float currentHeight = Mathf.Max(0.01f, hitCollider.bounds.size.y);
        return Mathf.Max(0.01f, currentHeight / Mathf.Max(0.01f, baseColliderHeight));
    }

    private float GetScaledRiseOffsetY()
    {
        return riseOffsetY * GetWorldScaleFactorY();
    }

    private float GetScaledGroundCheckDistance()
    {
        return Mathf.Max(0.02f, groundCheckDistance * GetWorldScaleFactorY());
    }

    private float GetScaledGroundContactThreshold()
    {
        return Mathf.Max(0.01f, groundContactThreshold * GetWorldScaleFactorY());
    }

    private float GetScaledFloorSnapOffset()
    {
        return floorSnapOffset * GetWorldScaleFactorY();
    }

    private float GetScaledMinFallDistanceAfterImpact()
    {
        return minFallDistanceAfterImpact * GetWorldScaleFactorY();
    }

    private void CacheOriginalColors()
    {
        if (spriteRenderers == null)
            return;

        originalColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                originalColors[i] = spriteRenderers[i].color;
        }
    }

    private void RestoreSpriteColors()
    {
        if (spriteRenderers == null || originalColors == null)
            return;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null && i < originalColors.Length)
                spriteRenderers[i].color = originalColors[i];
        }
    }

    private void SetSpriteColors(Color color)
    {
        if (spriteRenderers == null)
            return;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
                spriteRenderers[i].color = color;
        }
    }

    private void SetSpritesAlpha(float alpha)
    {
        if (spriteRenderers == null)
            return;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;

            Color color = spriteRenderers[i].color;
            color.a = Mathf.Clamp01(alpha);
            spriteRenderers[i].color = color;
        }
    }
}
