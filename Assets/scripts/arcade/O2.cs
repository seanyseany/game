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

    [Header("Destroy Animation")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private string destroyTriggerName = "Die";
    [SerializeField] private AnimationClip destroyAnimationClip;
    [SerializeField] private float destroyAnimationDuration = 0.5f;

    [Header("Pooling")]
    [SerializeField] private string poolTag = "O2";
    [SerializeField] private bool usePool = true;

    private SpriteRenderer spriteRenderer;
    private Collider2D hitCollider;
    private Coroutine collectRoutine;
    private bool collected;
    private Color initialColor;

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
            initialColor = spriteRenderer.color;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || !IsPlayer(other))
            return;

        Collect();
    }

    public void Collect()
    {
        if (collected)
            return;

        collected = true;

        if (GameData.Instance != null)
            GameData.Instance.AddO2(O2Amount);

        if (hitCollider != null)
            hitCollider.enabled = false;

        if (collectRoutine != null)
            StopCoroutine(collectRoutine);

        collectRoutine = StartCoroutine(CoCollectAndReturn());
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

        if (spriteRenderer != null)
            spriteRenderer.color = initialColor;

        if (targetAnimator != null)
        {
            if (!string.IsNullOrEmpty(destroyTriggerName))
                targetAnimator.ResetTrigger(destroyTriggerName);

            targetAnimator.Rebind();
            targetAnimator.Update(0f);
        }
    }

    private IEnumerator CoCollectAndReturn()
    {
        float waitTime = 0f;

        if (targetAnimator != null && !string.IsNullOrEmpty(destroyTriggerName))
        {
            targetAnimator.SetTrigger(destroyTriggerName);
            waitTime = GetDestroyAnimationDuration();
        }

        if (waitTime > 0f)
            yield return new WaitForSeconds(waitTime);

        collectRoutine = null;
        ReturnToPoolOrDisable();
    }

    private float GetDestroyAnimationDuration()
    {
        if (destroyAnimationClip != null)
            return Mathf.Max(0f, destroyAnimationClip.length);

        return Mathf.Max(0f, destroyAnimationDuration);
    }

    private void ReturnToPoolOrDisable()
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
        {
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
            return;
        }

        gameObject.SetActive(false);
    }

    private bool IsPlayer(Collider2D other)
    {
        if (other == null)
            return false;

        if (other.GetComponent<Player>() != null || other.GetComponentInParent<Player>() != null)
            return true;

        return !string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag);
    }

    private void Reset()
    {
        hitCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);

        if (hitCollider != null)
            hitCollider.isTrigger = true;
    }
}
