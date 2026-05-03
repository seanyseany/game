using UnityEngine;
using System.Collections;

public class BombHitBox : MonoBehaviour
{
    public string poolTag = "BombHitBox";

    private Coroutine lifeCo;

    private void OnEnable()
    {
        // 혹시 풀에서 다시 나왔는데도 코루틴 남아있는 경우 방지
        if (lifeCo != null)
        {
            StopCoroutine(lifeCo);
            lifeCo = null;
        }
    }

    public void Activate(float lifeTime)
    {
        if (lifeCo != null) StopCoroutine(lifeCo);
        lifeCo = StartCoroutine(CoLife(lifeTime));
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
}
