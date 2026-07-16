using System.Collections;
using UnityEngine;

public class AnimatorAutoDespawn : MonoBehaviour
{
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private bool usePool = true;
    [SerializeField] private string poolTag = "";
    [SerializeField] private float fallbackLifetime = 1f;

    private Coroutine despawnRoutine;

    private void Awake()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
    }

    private void OnEnable()
    {
        if (despawnRoutine != null)
            StopCoroutine(despawnRoutine);

        despawnRoutine = StartCoroutine(CoDespawnAfterAnimation());
    }

    private void OnDisable()
    {
        if (despawnRoutine != null)
        {
            StopCoroutine(despawnRoutine);
            despawnRoutine = null;
        }
    }

    public void ConfigurePooling(bool enablePool, string tag)
    {
        usePool = enablePool;
        poolTag = tag;
    }

    private IEnumerator CoDespawnAfterAnimation()
    {
        yield return null;

        float waitTime = GetAnimationDuration();
        yield return new WaitForSeconds(Mathf.Max(0.01f, waitTime));

        despawnRoutine = null;
        Despawn();
    }

    private float GetAnimationDuration()
    {
        if (targetAnimator == null)
            return fallbackLifetime;

        AnimatorClipInfo[] clips = targetAnimator.GetCurrentAnimatorClipInfo(0);
        if (clips != null && clips.Length > 0 && clips[0].clip != null)
            return clips[0].clip.length;

        return fallbackLifetime;
    }

    private void Despawn()
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
        {
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
            return;
        }

        gameObject.SetActive(false);
    }
}
