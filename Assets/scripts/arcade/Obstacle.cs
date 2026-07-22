using UnityEngine;
using System.Collections;

public class Obstacle : MonoBehaviour, IReinitializable
{
    private static readonly int DieTrigger = Animator.StringToHash("Die");

    public Animator anim;   // Animator (Idle/Move + Die 포함)
    private const float DestroyAnimationDuration = 0.5f;
    private const float OffscreenHitProtectionMargin = 0.25f;

    private bool destroyed = false;
    private static Player cachedPlayer;
    private static Camera cachedMainCamera;

    private Collider2D col;
    private Rigidbody2D rb;
    private ObstacleMover obstacleMover;
    private ObstacleRageMover obstacleRageMover;
    private MachineGunLastSpawnNotifier machineGunNotifier;
    private CholesterolBomb cholesterolBomb;
    private Coroutine destroyRoutine;
    private Transform[] cachedTransforms;
    private Vector3[] cachedLocalPositions;
    private Quaternion[] cachedLocalRotations;
    private Vector3[] cachedLocalScales;
    private bool[] cachedActiveStates;
    private Collider2D[] cachedColliders;
    private SpriteRenderer[] cachedRenderers;
    private Sprite[] cachedSprites;
    private Color[] cachedRendererColors;
    private Rigidbody2D[] cachedRigidbodies;
    private Animator[] cachedAnimators;
    private bool[] cachedAnimatorEnabledStates;
    private float[] cachedAnimatorSpeeds;
    private Transform initialParent;
    private bool runtimeSpawned;
    private float spawnProtectionUntil;

    private void Awake()
    {
        if (!anim) anim = GetComponent<Animator>();
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        obstacleMover = GetComponent<ObstacleMover>();
        obstacleRageMover = GetComponent<ObstacleRageMover>();
        machineGunNotifier = GetComponent<MachineGunLastSpawnNotifier>() ?? GetComponentInParent<MachineGunLastSpawnNotifier>();
        cholesterolBomb = GetComponent<CholesterolBomb>() ?? GetComponentInParent<CholesterolBomb>();
        initialParent = transform.parent;
        runtimeSpawned = false;
        CacheLocalPose();
        cachedColliders = GetComponentsInChildren<Collider2D>(true);
        cachedRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        cachedRigidbodies = GetComponentsInChildren<Rigidbody2D>(true);
        CacheRendererStates();
        CacheAnimatorStates();
    }

    // 풀에서 꺼낼 때마다 상태 초기화 (이게 가장 중요)
    private void OnEnable()
    {
        if (!runtimeSpawned)
            runtimeSpawned = (initialParent == null && GetComponentInParent<PhaseCache>() == null);

        if (!runtimeSpawned && initialParent != null && transform.parent != initialParent)
            transform.SetParent(initialParent, false);

        Reinit();
    }

    public void Reinit()
    {
        StopDestroyRoutine();

        ResetRuntimePose();
        destroyed = false;
        spawnProtectionUntil = 0f;

        if (col != null)
            col.enabled = true;

        ResetRigidbody(rb, true);

        if (obstacleMover != null)
            obstacleMover.Reinit();

        if (obstacleRageMover != null)
        {
            if (IsPhaseOwnedObstacle())
            {
                obstacleRageMover.ResumeMovement();
                obstacleRageMover.enabled = true;
                obstacleRageMover.Reinit();
            }
            else
            {
                obstacleRageMover.enabled = false;
            }
        }

        if (anim != null)
        {
            anim.ResetTrigger(DieTrigger);
            anim.Rebind();
            anim.Update(0f);
        }
    }

    private void OnDisable()
    {
        StopDestroyRoutine();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (destroyed || other == null)
            return;

        if (!CanReactToHit())
            return;

        if (TryHandleBombHit(other))
            return;

        Player player = other.GetComponent<Player>();
        if (player != null)
        {
            if (player.IsRageModeActive())
                Die();
            return;
        }

        if (IsRageAttackCollider(other))
            Die();
    }

    private void OnCollisionEnter2D(Collision2D colInfo)
    {
        if (destroyed || colInfo == null)
            return;

        if (!CanReactToHit())
            return;

        Player player = colInfo.gameObject.GetComponent<Player>();
        if (player != null && player.IsRageModeActive())
            Die();
    }

    public void Hit(int damage)
    {
        if (!CanReactToHit())
            return;

        Die();
    }

    public void TriggerDestroySequence()
    {
        if (!CanReactToHit())
            return;

        Die();
    }

    private void Die()
    {
        if (destroyed)
            return;

        destroyed = true;

        if (machineGunNotifier != null)
            machineGunNotifier.NotifyDestroyTriggered();

        if (obstacleMover != null)
            obstacleMover.NotifyDeathStarted();

        if (col != null)
            col.enabled = false;

        ResetRigidbody(rb, true);

        if (anim != null)
        {
            anim.SetTrigger(DieTrigger);
            StopDestroyRoutine();
            destroyRoutine = StartCoroutine(CoDeactivateAfterDeath());
        }
        else
        {
            FinishDeath();
        }
    }

    private IEnumerator CoDeactivateAfterDeath()
    {
        if (DestroyAnimationDuration > 0f)
            yield return new WaitForSeconds(DestroyAnimationDuration);

        destroyRoutine = null;
        FinishDeath();
    }

    private void FinishDeath()
    {
        ResetRigidbody(rb, true);

        if (col != null)
            col.enabled = false;

        if (anim != null)
            anim.ResetTrigger(DieTrigger);

        if (IsPhaseOwnedObstacle())
        {
            gameObject.SetActive(false);
            return;
        }

        if (obstacleMover != null && obstacleMover.TryReturnToObjectPool())
            return;

        gameObject.SetActive(false);
    }

    public void PrepareSpawnAt(Vector3 worldPos)
    {
        runtimeSpawned = true;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(worldPos, Quaternion.identity);
        ResetRuntimePose();
    }

    public void ActivateTemporarySpawnProtection(float duration)
    {
        if (duration <= 0f)
            return;

        spawnProtectionUntil = Mathf.Max(spawnProtectionUntil, Time.time + duration);
    }

    private void LateUpdate()
    {
        if (!runtimeSpawned && initialParent != null && transform.parent != initialParent)
            transform.SetParent(initialParent, false);
    }

    private bool IsPhaseOwnedObstacle()
    {
        return GetComponentInParent<PhaseLayoutSnapshot>(true) != null;
    }

    private bool HasTemporarySpawnProtection()
    {
        return spawnProtectionUntil > Time.time;
    }

    private void StopDestroyRoutine()
    {
        if (destroyRoutine == null)
            return;

        StopCoroutine(destroyRoutine);
        destroyRoutine = null;
    }

    private bool TryHandleBombHit(Collider2D other)
    {
        if (other.GetComponent<BombHitBox>() == null && other.GetComponentInParent<BombHitBox>() == null)
            return false;

        if (cholesterolBomb != null)
        {
            cholesterolBomb.TriggerExplosionFromExternalHit();
            return true;
        }

        Die();
        return true;
    }

    private static bool IsRageAttackCollider(Collider2D other)
    {
        if (other.GetComponent<Hitbox>() == null &&
            other.GetComponent<ZigzagLightning>() == null &&
            other.GetComponent<ProjectileBall>() == null)
        {
            return false;
        }

        Player owner = GetCachedPlayer();
        return owner != null && owner.IsRageModeActive();
    }

    private void CacheLocalPose()
    {
        cachedTransforms = GetComponentsInChildren<Transform>(true);
        int n = cachedTransforms != null ? cachedTransforms.Length : 0;
        cachedLocalPositions = new Vector3[n];
        cachedLocalRotations = new Quaternion[n];
        cachedLocalScales = new Vector3[n];
        cachedActiveStates = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var t = cachedTransforms[i];
            if (t == null) continue;
            cachedLocalPositions[i] = t.localPosition;
            cachedLocalRotations[i] = t.localRotation;
            cachedLocalScales[i] = t.localScale;
            cachedActiveStates[i] = t.gameObject.activeSelf;
        }
    }

    private void ResetRuntimePose()
    {
        if (cachedTransforms == null || cachedLocalPositions == null)
            CacheLocalPose();

        Transform root = transform;
        bool phaseOwned = IsPhaseOwnedObstacle();
        int n = cachedTransforms != null ? cachedTransforms.Length : 0;
        for (int i = 0; i < n; i++)
        {
            var t = cachedTransforms[i];
            if (t == null) continue;
            if (cachedActiveStates != null && i < cachedActiveStates.Length)
                t.gameObject.SetActive(cachedActiveStates[i]);
            if (t == root)
            {
                if (phaseOwned)
                {
                    t.localPosition = cachedLocalPositions[i];
                    t.localRotation = cachedLocalRotations[i];
                    t.localScale = cachedLocalScales[i];
                }
                continue;
            }
            t.localPosition = cachedLocalPositions[i];
            t.localRotation = cachedLocalRotations[i];
            t.localScale = cachedLocalScales[i];
        }

        if (cachedColliders != null)
        {
            for (int i = 0; i < cachedColliders.Length; i++)
            {
                var c = cachedColliders[i];
                if (c != null) c.enabled = true;
            }
        }

        if (cachedRenderers != null)
        {
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                var r = cachedRenderers[i];
                if (r == null) continue;
                r.enabled = true;
                if (cachedSprites != null && i < cachedSprites.Length)
                    r.sprite = cachedSprites[i];

                if (cachedRendererColors != null && i < cachedRendererColors.Length)
                    r.color = cachedRendererColors[i];
                else
                {
                    var c = r.color;
                    c.a = 1f;
                    r.color = c;
                }
            }
        }

        if (cachedAnimators != null)
        {
            for (int i = 0; i < cachedAnimators.Length; i++)
            {
                var cachedAnimator = cachedAnimators[i];
                if (cachedAnimator == null)
                    continue;

                if (cachedAnimatorEnabledStates != null && i < cachedAnimatorEnabledStates.Length)
                    cachedAnimator.enabled = cachedAnimatorEnabledStates[i];

                if (cachedAnimatorSpeeds != null && i < cachedAnimatorSpeeds.Length)
                    cachedAnimator.speed = cachedAnimatorSpeeds[i];
            }
        }

        if (cachedRigidbodies != null)
        {
            for (int i = 0; i < cachedRigidbodies.Length; i++)
            {
                var body = cachedRigidbodies[i];
                if (body == null) continue;
                if (body.bodyType != RigidbodyType2D.Static)
                {
                    body.simulated = true;
                    body.linearVelocity = Vector2.zero;
                    body.angularVelocity = 0f;
                }
            }
        }
    }

    private static void ResetRigidbody(Rigidbody2D body, bool sleep)
    {
        if (body == null) return;
        if (body.bodyType != RigidbodyType2D.Static)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        if (sleep)
            body.Sleep();
    }

    private static Player GetCachedPlayer()
    {
        if (cachedPlayer == null)
            cachedPlayer = Object.FindFirstObjectByType<Player>();

        return cachedPlayer;
    }

    private bool CanReactToHit()
    {
        if (HasTemporarySpawnProtection())
            return false;

        if (!IsPhaseOwnedObstacle())
            return true;

        Camera cam = GetMainCamera();
        if (cam == null || !cam.orthographic)
            return true;

        float cameraRightX = cam.transform.position.x + cam.orthographicSize * cam.aspect;

        if (cachedRenderers != null)
        {
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                SpriteRenderer renderer = cachedRenderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (renderer.bounds.min.x <= cameraRightX + OffscreenHitProtectionMargin)
                    return true;
            }
        }

        return transform.position.x <= cameraRightX + OffscreenHitProtectionMargin;
    }

    private static Camera GetMainCamera()
    {
        if (cachedMainCamera == null)
            cachedMainCamera = Camera.main;

        return cachedMainCamera;
    }

    private void CacheRendererStates()
    {
        if (cachedRenderers == null)
            cachedRenderers = GetComponentsInChildren<SpriteRenderer>(true);

        int count = cachedRenderers != null ? cachedRenderers.Length : 0;
        cachedSprites = new Sprite[count];
        cachedRendererColors = new Color[count];

        for (int i = 0; i < count; i++)
        {
            SpriteRenderer renderer = cachedRenderers[i];
            if (renderer == null)
                continue;

            cachedSprites[i] = renderer.sprite;
            cachedRendererColors[i] = renderer.color;
        }
    }

    private void CacheAnimatorStates()
    {
        cachedAnimators = GetComponentsInChildren<Animator>(true);
        int count = cachedAnimators != null ? cachedAnimators.Length : 0;
        cachedAnimatorEnabledStates = new bool[count];
        cachedAnimatorSpeeds = new float[count];

        for (int i = 0; i < count; i++)
        {
            Animator cachedAnimator = cachedAnimators[i];
            if (cachedAnimator == null)
                continue;

            cachedAnimatorEnabledStates[i] = cachedAnimator.enabled;
            cachedAnimatorSpeeds[i] = cachedAnimator.speed;
        }
    }
}
