using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

public class MissileSmoke : MonoBehaviour
{
    [SerializeField, FormerlySerializedAs("lifeTime")]
    private float fallbackLifetime = 0.5f;

    private Animator animator;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>(true);
    }

    private void OnEnable()
    {
        StopAllCoroutines();
        StartCoroutine(DespawnAfterPlayback());
    }

    private IEnumerator DespawnAfterPlayback()
    {
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
        {
            // Let the Animator enter its state after activation or a pool rebind.
            do
            {
                yield return null;
            }
            while (animator != null && (!animator.isInitialized ||
                (animator.layerCount > 0 && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)));
        }
        else
        {
            yield return new WaitForSeconds(fallbackLifetime);
        }

        ObjectPool pool = ObjectPool.Instance;
        if (pool == null || !pool.TryReturnActive(gameObject))
            Destroy(gameObject);
    }
}
