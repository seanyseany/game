using UnityEngine;
using System.Collections;

public class AutoReturnToPool : MonoBehaviour
{
    public bool usePool = true;
    public string poolTag = "";      // 이 오브젝트가 돌아갈 풀 태그
    public float lifeTime = 0.6f;    // 폭발 애니 길이에 맞춰

    private Coroutine co;

    private void OnEnable()
    {
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(CoLife());
    }

    private IEnumerator CoLife()
    {
        yield return new WaitForSeconds(lifeTime);

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);

        co = null;
    }
}