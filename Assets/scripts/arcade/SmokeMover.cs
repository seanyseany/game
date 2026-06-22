using UnityEngine;
using System.Collections;

public class SmokeMover : MonoBehaviour, IRageTransformPauseHandler
{
    private const string TransformSmokePoolTag = "transformSmoke";
    public float speed = 4f;
    public float lifetime = 1f;
    [SerializeField] private bool usePool = true;
    [SerializeField] private string poolTag = "";
    private Coroutine lifeCo;
    private float despawnAtTime = -1f;
    private float pausedRemainingLife = -1f;
    public string PoolTag => poolTag;

    void OnEnable()
    {
        if (lifeCo != null) StopCoroutine(lifeCo);
        despawnAtTime = Time.time + Mathf.Max(0.01f, lifetime);
        pausedRemainingLife = -1f;
        lifeCo = StartCoroutine(CoLife());
    }

    void OnDisable()
    {
        if (lifeCo != null)
        {
            StopCoroutine(lifeCo);
            lifeCo = null;
        }
        despawnAtTime = -1f;
        pausedRemainingLife = -1f;
    }

    private IEnumerator CoLife()
    {
        yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(lifetime);
        lifeCo = null;
        Despawn();
    }

    private IEnumerator CoLife(float duration)
    {
        yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(duration);
        lifeCo = null;
        Despawn();
    }

    public void ConfigurePooling(bool enablePool, string tag)
    {
        usePool = enablePool;
        poolTag = tag;
    }

    public bool IsTransformRageSmoke()
    {
        if (!string.IsNullOrEmpty(poolTag) && poolTag == TransformSmokePoolTag)
            return true;

        string objectName = gameObject.name;
        return !string.IsNullOrEmpty(objectName) && objectName.Contains("TransformSmoke");
    }

    private void Despawn()
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }

    void Update()
    {
        float stageMult = GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;

        if (IsTransformRageSmoke())
        {
            transform.position += Vector3.left * speed * stageMult * Time.deltaTime;
            return;
        }

        if (RageTransformFreezeController.ShouldSkipGameplayFrame())
            return;

        transform.position += Vector3.left * speed * stageMult * Time.deltaTime;
    }

    public void OnRageTransformPauseStarted()
    {
        if (IsTransformRageSmoke())
            return;

        if (!isActiveAndEnabled || despawnAtTime < 0f)
            return;

        pausedRemainingLife = Mathf.Max(0f, despawnAtTime - Time.time);
        if (lifeCo != null)
        {
            StopCoroutine(lifeCo);
            lifeCo = null;
        }
    }

    public void OnRageTransformPauseEnded()
    {
        if (IsTransformRageSmoke())
            return;

        if (!isActiveAndEnabled || pausedRemainingLife < 0f)
            return;

        despawnAtTime = Time.time + pausedRemainingLife;
        if (lifeCo != null)
            StopCoroutine(lifeCo);
        lifeCo = StartCoroutine(CoLife(Mathf.Max(0.01f, pausedRemainingLife)));
        pausedRemainingLife = -1f;
    }
}
