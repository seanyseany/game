using UnityEngine;
using System.Collections;

public class BombHitBox : MonoBehaviour
{
    public string poolTag = "BombHitBox";
    public bool affectsCholesterolBomb = true;

    private Coroutine lifeCo;
    private Coroutine delayedEnableCo;
    private Collider2D[] cachedColliders;

    private void OnEnable()
    {
        // 혹시 풀에서 다시 나왔는데도 코루틴 남아있는 경우 방지
        if (lifeCo != null)
        {
            StopCoroutine(lifeCo);
            lifeCo = null;
        }

        if (delayedEnableCo != null)
        {
            StopCoroutine(delayedEnableCo);
            delayedEnableCo = null;
        }

        EnsureCachedColliders();
        SetCollidersEnabled(true);
    }

    public void Activate(float lifeTime)
    {
        if (lifeCo != null) StopCoroutine(lifeCo);
        lifeCo = StartCoroutine(CoLife(lifeTime));
    }

    public void ActivateAfterDelay(float delaySeconds)
    {
        if (delayedEnableCo != null)
            StopCoroutine(delayedEnableCo);

        EnsureCachedColliders();
        SetCollidersEnabled(false);
        delayedEnableCo = StartCoroutine(CoEnableAfterDelay(delaySeconds));
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryAffectTarget(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryAffectTarget(other);
    }

    private void TryAffectTarget(Collider2D other)
    {
        if (other == null)
            return;

        CholesterolBomb cholesterolBomb = other.GetComponent<CholesterolBomb>() ?? other.GetComponentInParent<CholesterolBomb>();
        if (cholesterolBomb != null)
        {
            if (affectsCholesterolBomb)
                cholesterolBomb.TriggerExplosionFromExternalHit();
            return;
        }

        BulletObstacle bulletObstacle = other.GetComponent<BulletObstacle>() ?? other.GetComponentInParent<BulletObstacle>();
        if (bulletObstacle != null)
        {
            bulletObstacle.ForceDestroyImmediate();
            return;
        }

        Obstacle obstacle = other.GetComponent<Obstacle>() ?? other.GetComponentInParent<Obstacle>();
        if (obstacle != null)
        {
            obstacle.Hit(1);
            return;
        }
    }

    private IEnumerator CoEnableAfterDelay(float delaySeconds)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));
        SetCollidersEnabled(true);
        delayedEnableCo = null;
    }

    IEnumerator CoLife(float t)
    {
        yield return new WaitForSeconds(t);

        if (ObjectPool.Instance != null)
        {
            if (!string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            {
                ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
            }
            else if (!ObjectPool.Instance.TryReturnActive(gameObject))
            {
                gameObject.SetActive(false);
            }
        }
        else
            Destroy(gameObject);

        lifeCo = null;
    }

    private void EnsureCachedColliders()
    {
        if (cachedColliders == null || cachedColliders.Length == 0)
            cachedColliders = GetComponents<Collider2D>();
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (cachedColliders == null)
            return;

        for (int i = 0; i < cachedColliders.Length; i++)
        {
            if (cachedColliders[i] != null)
                cachedColliders[i].enabled = enabled;
        }
    }
}
