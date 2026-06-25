using UnityEngine;
using System.Collections;

public class Obstacle : MonoBehaviour, IReinitializable
{
    public Animator anim;   // Animator (Idle/Move + Die 포함)
    private const float DestroyAnimationDuration = 0.5f;

    private bool destroyed = false;
    private static Player cachedPlayer;

    private Collider2D col;
    private Rigidbody2D rb;
    private ObstacleMover obstacleMover;
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

    private void Awake()
    {
        if (!anim) anim = GetComponent<Animator>();
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        obstacleMover = GetComponent<ObstacleMover>();
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
        if (destroyRoutine != null)
        {
            StopCoroutine(destroyRoutine);
            destroyRoutine = null;
        }

        ResetRuntimePose();
        destroyed = false;

        if (col)
        {
            col.enabled = true;
        }

        if (rb != null)
        {
            ResetRigidbody(rb, true);
        }

        if (obstacleMover == null)
            obstacleMover = GetComponent<ObstacleMover>();

        if (obstacleMover != null)
            obstacleMover.Reinit();

        if (anim != null)
        {
            anim.ResetTrigger("Die");
            anim.Rebind();
            anim.Update(0f);
        }
    }

    private void OnDisable()
    {
        if (destroyRoutine != null)
        {
            StopCoroutine(destroyRoutine);
            destroyRoutine = null;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (destroyed) return;
        

        if (other.GetComponent<BombHitBox>() != null || other.GetComponentInParent<BombHitBox>() != null)
        {
            Die();
            return;
        }

        // 1) 플레이어 충돌
        var player = other.GetComponent<Player>();
        if (player != null)
        {
            if (player.IsRageModeActive())
            {
                Die();
            }
            return;
        }

        // 2) Rage 공격 Hitbox (Hitbox 컴포넌트가 owner를 갖는 경우 owner에서 체크하는게 더 안정적)
        var hitbox = other.GetComponent<Hitbox>();
        if (hitbox != null)
        {
            var owner = GetCachedPlayer();
            if (owner != null && owner.IsRageModeActive())
            {
                Die();
            }
            return;
        }

        // 3) Rage Projectile (번개, 공)
        if (other.GetComponent<ZigzagLightning>() != null ||
            other.GetComponent<ProjectileBall>() != null)
        {
            var owner = GetCachedPlayer();
            if (owner != null && owner.IsRageModeActive())
            {
                Die();
            }
            return;
        }
    }

    private void OnCollisionEnter2D(Collision2D colInfo)
    {
        if (destroyed) return;

        var player = colInfo.gameObject.GetComponent<Player>();
        if (player != null && player.IsRageModeActive())
        {
            Die();
        }
    }

    public void Hit(int damage)
    {
        Die();
    }

    private void Die()
    {
        if (destroyed) return;
        destroyed = true;

        if (obstacleMover == null)
            obstacleMover = GetComponent<ObstacleMover>();

        if (obstacleMover != null)
            obstacleMover.NotifyDeathStarted();

        if (col != null) col.enabled = false;

        if (rb != null)
        {
            ResetRigidbody(rb, true);
        }

        if (anim != null)
        {
            anim.SetTrigger("Die");
            if (destroyRoutine != null)
                StopCoroutine(destroyRoutine);

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
        // Rigidbody 초기화
        if (rb != null)
        {
            ResetRigidbody(rb, true);
        }

        // 콜라이더 끄기(풀에 들어가기 전에)
        if (col != null)
            col.enabled = false;

        // 애니 초기화(다음에 OnEnable에서 다시 초기화됨)
        if (anim != null)
            anim.ResetTrigger("Die");

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

    private void LateUpdate()
    {
        if (!runtimeSpawned && initialParent != null && transform.parent != initialParent)
            transform.SetParent(initialParent, false);
    }

    private bool IsPhaseOwnedObstacle()
    {
        return GetComponentInParent<PhaseLayoutSnapshot>(true) != null;
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
