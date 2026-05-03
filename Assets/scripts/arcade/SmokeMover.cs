using UnityEngine;
using System.Collections;

public class SmokeMover : MonoBehaviour
{
    public float speed = 4f;
    public float lifetime = 1f;
    [SerializeField] private bool usePool = true;
    [SerializeField] private string poolTag = "";
    private Coroutine lifeCo;
    public string PoolTag => poolTag;

    void OnEnable()
    {
        if (lifeCo != null) StopCoroutine(lifeCo);
        lifeCo = StartCoroutine(CoLife());
    }

    void OnDisable()
    {
        if (lifeCo != null)
        {
            StopCoroutine(lifeCo);
            lifeCo = null;
        }
    }

    private IEnumerator CoLife()
    {
        yield return new WaitForSeconds(lifetime);
        lifeCo = null;
        Despawn();
    }

    public void ConfigurePooling(bool enablePool, string tag)
    {
        usePool = enablePool;
        poolTag = tag;
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
        float mult = GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;
        transform.position += Vector3.left * speed * mult * Time.deltaTime;
    }
}
