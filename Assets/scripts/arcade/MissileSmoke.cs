using UnityEngine;
using System.Collections;

public class MissileSmoke : MonoBehaviour
{
    public float lifeTime = 0.5f; // 연기 지속 시간

    void OnEnable()
    {
        // 활성화될 때마다 타이머 재시작 보장
        StopAllCoroutines();
        StartCoroutine(LifeCo());
    }

    private IEnumerator LifeCo()
    {
        yield return new WaitForSeconds(lifeTime);

        // 태그가 "Smoke" 이고 풀을 쓰는 경우 풀로 반납, 아니면 파괴
        if (ObjectPool.Instance != null && CompareTag("Smoke"))
            ObjectPool.Instance.ReturnToPool("Smoke", gameObject);
        else
            Destroy(gameObject);
    }
}
