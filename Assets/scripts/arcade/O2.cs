using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class O2 : MonoBehaviour, IReinitializable
{
    public enum O2Level
    {
        Level1 = 1,
        Level2 = 2,
        Level3 = 3
    }

    [Header("O2 Settings")]
    [SerializeField] private O2Level level = O2Level.Level1;
    [SerializeField] private string playerTag = "Player";

    [Header("Level Sprites")]
    [SerializeField] private Sprite level1Sprite;
    [SerializeField] private Sprite level2Sprite;
    [SerializeField] private Sprite level3Sprite;

    [Header("Destroy Animation")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private string destroyTriggerName = "Die";
    [SerializeField] private AnimationClip destroyAnimationClip;
    [SerializeField] private float destroyAnimationDuration = 0.5f;

    private SpriteRenderer spriteRenderer;
    private Collider2D hitCollider;
    private Coroutine collectRoutine;
    private bool collected;
    private Color initialColor;
    private Sprite initialSprite;

    public int O2Amount => (int)level;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        hitCollider = GetComponent<Collider2D>();

        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (hitCollider != null)
            hitCollider.isTrigger = true;

        if (spriteRenderer != null)
        {
            ApplyLevelSprite();
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

        Reinit();
    }

    private void OnDisable()
    {
        GameData.OnRageStart -= HandleRageStart;
        RestoreIdleVisualState();

        if (collectRoutine != null)
        {
            StopCoroutine(collectRoutine);
            collectRoutine = null;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || !IsPlayerBodyCollider(other))
            return;

        Collect();
    }

    public void Collect()
    {
        StartDestroySequence(grantReward: true);
    }

    public void Reinit()
    {
        collected = false;

        if (collectRoutine != null)
        {
            StopCoroutine(collectRoutine);
            collectRoutine = null;
        }

        if (hitCollider != null)
        {
            hitCollider.enabled = true;
            hitCollider.isTrigger = true;
        }

        RestoreIdleVisualState();
    }

    private void LateUpdate()
    {
        if (collected)
            return;

        Sprite expectedSprite = GetSpriteForLevel(level);
        bool needsRestore =
            (targetAnimator != null && targetAnimator.enabled) ||
            (spriteRenderer != null && !spriteRenderer.enabled) ||
            (spriteRenderer != null && expectedSprite != null && spriteRenderer.sprite != expectedSprite) ||
            (spriteRenderer != null && spriteRenderer.color.a < 0.999f);

        if (needsRestore)
            RestoreIdleVisualState();
    }

    private void RestoreIdleVisualState()
    {
        if (targetAnimator != null)
        {
            if (!string.IsNullOrEmpty(destroyTriggerName))
                targetAnimator.ResetTrigger(destroyTriggerName);

            targetAnimator.Rebind();
            targetAnimator.Update(0f);
            targetAnimator.enabled = false;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            ApplyLevelSprite();

            if (spriteRenderer.sprite == null && initialSprite != null)
                spriteRenderer.sprite = initialSprite;

            Color restoredColor = initialColor;
            restoredColor.a = 1f;
            spriteRenderer.color = restoredColor;
        }
    }

    private IEnumerator CoCollectAndReturn()
    {
        float waitTime = 0f;

        if (targetAnimator != null && !string.IsNullOrEmpty(destroyTriggerName))
        {
            targetAnimator.enabled = true;
            targetAnimator.Rebind();
            targetAnimator.Update(0f);
            targetAnimator.SetTrigger(destroyTriggerName);
            waitTime = GetDestroyAnimationDuration();
        }

        if (waitTime > 0f)
            yield return new WaitForSeconds(waitTime);

        collectRoutine = null;
        gameObject.SetActive(false);
    }

    private void HandleRageStart()
    {
        if (!isActiveAndEnabled || collected)
            return;

        StartDestroySequence(grantReward: false);
    }

    private void StartDestroySequence(bool grantReward)
    {
        if (collected)
            return;

        collected = true;

        if (grantReward && GameData.Instance != null)
            GameData.Instance.AddO2(O2Amount);

        if (hitCollider != null)
            hitCollider.enabled = false;

        if (collectRoutine != null)
            StopCoroutine(collectRoutine);

        collectRoutine = StartCoroutine(CoCollectAndReturn());
    }

    private float GetDestroyAnimationDuration()
    {
        if (destroyAnimationClip != null)
            return Mathf.Max(0f, destroyAnimationClip.length);

        return Mathf.Max(0f, destroyAnimationDuration);
    }

    public void SetLevel(O2Level newLevel)
    {
        level = newLevel;
        ApplyLevelSprite();
    }

    public void SetLevel(int newLevel)
    {
        SetLevel((O2Level)Mathf.Clamp(newLevel, (int)O2Level.Level1, (int)O2Level.Level3));
    }

    private void ApplyLevelSprite()
    {
        if (spriteRenderer == null)
            return;

        Sprite targetSprite = GetSpriteForLevel(level);
        if (targetSprite != null)
            spriteRenderer.sprite = targetSprite;
    }

    private Sprite GetSpriteForLevel(O2Level targetLevel)
    {
        switch (targetLevel)
        {
            case O2Level.Level3:
                return level3Sprite != null ? level3Sprite : level2Sprite != null ? level2Sprite : level1Sprite;
            case O2Level.Level2:
                return level2Sprite != null ? level2Sprite : level1Sprite;
            default:
                return level1Sprite != null ? level1Sprite : level2Sprite != null ? level2Sprite : level3Sprite;
        }
    }

    private bool IsPlayerBodyCollider(Collider2D other)
    {
        if (other == null)
            return false;

        if (other.GetComponent<Hitbox>() != null ||
            other.GetComponent<ProjectileBall>() != null ||
            other.GetComponent<ZigzagLightning>() != null ||
            other.GetComponent<BombHitBox>() != null)
            return false;

        Player player = other.GetComponent<Player>() ?? other.GetComponentInParent<Player>();
        if (player == null)
            return !string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag);

        Collider2D playerBodyCollider = player.GetComponent<Collider2D>();
        return playerBodyCollider != null && other == playerBodyCollider;
    }

    private void Reset()
    {
        hitCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (hitCollider != null)
            hitCollider.isTrigger = true;

        if (spriteRenderer != null)
            ApplyLevelSprite();

        if (spriteRenderer != null && initialSprite == null)
            initialSprite = spriteRenderer.sprite;
    }
}
