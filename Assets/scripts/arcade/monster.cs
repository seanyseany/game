using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Monster : MonoBehaviour, IReinitializable
{
    [Header("Animation")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private string destroyTriggerName = "die";
    [SerializeField] private AnimationClip destroyAnimationClip;
    [SerializeField] private float destroyAnimationDuration = 0.6f;

    [Header("Gate")]
    [SerializeField] private string gateTag = "Gate";
    [SerializeField] private bool damageGateOnContact = true;
    private const float OffscreenHitProtectionMargin = 0.25f;

    private SpriteRenderer spriteRenderer;
    private Collider2D hitCollider;
    private Rigidbody2D body2D;
    private ObstacleRageMover rageMover;
    private Coroutine destroyRoutine;
    private bool dead;
    private bool rageCounted;
    private Color initialColor;
    private Sprite initialSprite;
    private RageUIController rageUi;
    private static Camera cachedMainCamera;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        hitCollider = GetComponent<Collider2D>();
        body2D = GetComponent<Rigidbody2D>();
        rageMover = GetComponent<ObstacleRageMover>();

        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (spriteRenderer != null)
        {
            initialColor = spriteRenderer.color;
            initialSprite = spriteRenderer.sprite;
        }
    }

    private void OnEnable()
    {
        GameData.OnRageStart += HandleRageStart;

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (hitCollider == null)
            hitCollider = GetComponent<Collider2D>();

        if (body2D == null)
            body2D = GetComponent<Rigidbody2D>();

        if (rageMover == null)
            rageMover = GetComponent<ObstacleRageMover>();

        Reinit();
    }

    private void OnDisable()
    {
        GameData.OnRageStart -= HandleRageStart;

        if (destroyRoutine != null)
        {
            StopCoroutine(destroyRoutine);
            destroyRoutine = null;
        }
    }

    public void Reinit()
    {
        dead = false;
        rageCounted = false;

        if (destroyRoutine != null)
        {
            StopCoroutine(destroyRoutine);
            destroyRoutine = null;
        }

        if (hitCollider != null)
            hitCollider.enabled = true;

        if (body2D != null)
        {
            body2D.simulated = true;
            if (body2D.bodyType != RigidbodyType2D.Static)
            {
                body2D.linearVelocity = Vector2.zero;
                body2D.angularVelocity = 0f;
                body2D.WakeUp();
            }
        }

        bool phaseOwned = IsPhaseOwnedMonster();
        if (rageMover != null)
        {
            if (phaseOwned)
            {
                rageMover.ResumeMovement();
                rageMover.enabled = true;
            }
            else
            {
                rageMover.enabled = false;
            }
        }

        if (targetAnimator != null)
        {
            if (!string.IsNullOrEmpty(destroyTriggerName))
                targetAnimator.ResetTrigger(destroyTriggerName);

            targetAnimator.Rebind();
            targetAnimator.Update(0f);
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;

            if (initialSprite != null)
                spriteRenderer.sprite = initialSprite;

            Color restoredColor = initialColor;
            restoredColor.a = 1f;
            spriteRenderer.color = restoredColor;
        }
    }

    public void Hit(int damage)
    {
        Hit(damage, true);
    }

    public void Hit(int damage, bool countAsPlayerKill)
    {
        if (dead)
            return;

        if (!CanReactToHits())
            return;

        StartDie(byPlayerKill: countAsPlayerKill);
    }

    public void TakeDamage(int damage)
    {
        Hit(damage);
    }

    private void HandleRageStart()
    {
        if (!isActiveAndEnabled || dead)
            return;

        if (!CanReactToHits())
            return;

        StartDie(byPlayerKill: false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (dead || other == null)
            return;

        if (ShouldDamageGate(other))
        {
            if (damageGateOnContact && GateHealth.Instance != null)
                GateHealth.Instance.TakeBossMissileHit();

            StartDie(byPlayerKill: false);
            return;
        }

        if (IsPlayerAttackCollider(other) && CanReactToHits())
            StartDie(byPlayerKill: true);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (dead || collision == null)
            return;

        Collider2D other = collision.collider;
        if (other == null)
            return;

        if (ShouldDamageGate(other))
        {
            if (damageGateOnContact && GateHealth.Instance != null)
                GateHealth.Instance.TakeBossMissileHit();

            StartDie(byPlayerKill: false);
        }
    }

    private void StartDie(bool byPlayerKill)
    {
        if (dead)
            return;

        dead = true;

        if (hitCollider != null)
            hitCollider.enabled = false;

        if (body2D != null)
        {
            if (body2D.bodyType != RigidbodyType2D.Static)
            {
                body2D.linearVelocity = Vector2.zero;
                body2D.angularVelocity = 0f;
            }
            body2D.simulated = false;
        }

        if (rageMover != null && rageMover.enabled)
            rageMover.FreezeAtCurrentPosition();

        if (targetAnimator != null && !string.IsNullOrEmpty(destroyTriggerName))
            targetAnimator.SetTrigger(destroyTriggerName);

        if (byPlayerKill)
            AddRageOneKill();

        if (destroyRoutine != null)
            StopCoroutine(destroyRoutine);

        destroyRoutine = StartCoroutine(CoDestroyAndReturn());
    }

    private IEnumerator CoDestroyAndReturn()
    {
        float waitTime = GetDestroyAnimationDuration();
        if (waitTime > 0f)
            yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(waitTime);

        destroyRoutine = null;

        if (IsPhaseOwnedMonster())
            gameObject.SetActive(false);
        else
            Destroy(gameObject);
    }

    private float GetDestroyAnimationDuration()
    {
        if (destroyAnimationClip != null)
            return Mathf.Max(0f, destroyAnimationClip.length);

        return Mathf.Max(0f, destroyAnimationDuration);
    }

    private bool IsPhaseOwnedMonster()
    {
        return GetComponentInParent<PhaseLayoutSnapshot>(true) != null;
    }

    private void AddRageOneKill()
    {
        if (rageCounted)
            return;

        rageCounted = true;

        if (rageUi == null)
            rageUi = Object.FindFirstObjectByType<RageUIController>();

        if (rageUi != null)
            rageUi.AddKill();
    }

    private bool ShouldDamageGate(Collider2D other)
    {
        if (other.CompareTag(gateTag))
            return true;

        if (other.GetComponent<GateHealth>() != null)
            return true;

        if (other.GetComponentInParent<GateHealth>() != null)
            return true;

        return false;
    }

    private static bool IsPlayerAttackCollider(Collider2D other)
    {
        if (other == null)
            return false;

        if (other.GetComponent<Hitbox>() != null ||
            other.GetComponent<ProjectileBall>() != null ||
            other.GetComponent<ZigzagLightning>() != null ||
            other.GetComponent<BombHitBox>() != null)
            return true;

        Transform parent = other.transform.parent;
        if (parent != null)
        {
            if (parent.GetComponent<Hitbox>() != null ||
                parent.GetComponent<ProjectileBall>() != null ||
                parent.GetComponent<ZigzagLightning>() != null ||
                parent.GetComponent<BombHitBox>() != null)
                return true;
        }

        Transform root = other.transform.root;
        if (root != null)
        {
            if (root.GetComponent<Hitbox>() != null ||
                root.GetComponent<ProjectileBall>() != null ||
                root.GetComponent<ZigzagLightning>() != null ||
                root.GetComponent<BombHitBox>() != null)
                return true;
        }

        return false;
    }

    private bool CanReactToHits()
    {
        if (!IsPhaseOwnedMonster())
            return true;

        Camera cam = GetMainCamera();
        if (cam == null || !cam.orthographic)
            return true;

        if (spriteRenderer != null && spriteRenderer.enabled)
        {
            float cameraRightX = cam.transform.position.x + cam.orthographicSize * cam.aspect;
            return spriteRenderer.bounds.min.x <= cameraRightX + OffscreenHitProtectionMargin;
        }

        return transform.position.x <= cam.transform.position.x + cam.orthographicSize * cam.aspect + OffscreenHitProtectionMargin;
    }

    private static Camera GetMainCamera()
    {
        if (cachedMainCamera == null)
            cachedMainCamera = Camera.main;

        return cachedMainCamera;
    }

    private void Reset()
    {
        hitCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        body2D = GetComponent<Rigidbody2D>();
        rageMover = GetComponent<ObstacleRageMover>();
        targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
    }
}
