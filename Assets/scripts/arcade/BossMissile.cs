using UnityEngine;

public class BossMissile : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 8f;
    public Vector3[] targetPoints = new Vector3[4];

    [Header("Hit")]
    public string playerTag = "player";
    public string gateTag = "Gate"; // Gate 오브젝트에 이 태그 달아줘
    public string destroyPoolTag = "BossMissileDestroy"; // 풀에서 폭발 이펙트 쓰면

    [Header("Pooling")]
    public bool usePool = true;
    public string poolTag = "BossMissile";

    private Vector3 target;
    private bool hasTarget = false;

    private void OnEnable()
    {
        PickTarget();
    }

    private void Update()
    {
        if (!hasTarget) return;

        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

        // 목표점 도착하면 그냥 소멸(필요하면 여기서 폭발)
        if ((transform.position - target).sqrMagnitude <= 0.01f)
        {
            ExplodeAndDie();
        }
    }

    private void PickTarget()
    {
        int n = (targetPoints != null) ? targetPoints.Length : 0;
        if (n <= 0)
        {
            // 기본: 그냥 왼쪽으로
            target = transform.position + Vector3.left * 30f;
            hasTarget = true;
            return;
        }

        // 빈(0,0,0)인 포인트가 섞일 수 있으니 유효한 것만 골라
        int guard = 20;
        while (guard-- > 0)
        {
            int i = Random.Range(0, n);
            Vector3 p = targetPoints[i];
            if (p != Vector3.zero)
            {
                target = p;
                hasTarget = true;
                return;
            }
        }

        target = transform.position + Vector3.left * 30f;
        hasTarget = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 플레이어 맞으면: 플레이어가 대신 맞아주는 구조라면 여기서 데미지 처리
        if (other.CompareTag(playerTag))
        {
            ExplodeAndDie();
            return;
        }

        // 문(Gate) 맞으면: 문 목숨 -1
        if (other.CompareTag(gateTag) || other.GetComponent<GateHealth>() != null)
        {
            if (GateHealth.Instance != null)
                GateHealth.Instance.TakeBossMissileHit();

            ExplodeAndDie();
            return;
        }
    }

    private void ExplodeAndDie()
    {
        // 폭발 이펙트(풀 사용 가능)
        if (usePool && ObjectPool.Instance != null && ObjectPool.Instance.HasPool(destroyPoolTag))
        {
            ObjectPool.Instance.SpawnFromPool(destroyPoolTag, transform.position, Quaternion.identity);
        }

        // 미사일 본체 회수
        if (usePool && ObjectPool.Instance != null && ObjectPool.Instance.HasPool(poolTag))
        {
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // 보스에서 발사할 때 호출용(타겟 포인트를 런타임에 주고 싶으면)
    public void SetTargetPoints(Vector3[] points)
    {
        targetPoints = points;
        PickTarget();
    }
}