using UnityEngine;
using System.Collections.Generic;

public class BossSlimeJelly : MonoBehaviour, IReinitializable
{
    [System.Serializable]
    public struct LaunchVersion
    {
        public float speed;
        public float upSpeed;
    }

    [System.Serializable]
    public struct LaunchVersionGroup
    {
        public LaunchVersion[] versions;
    }

    [Header("Movement")]
    public float rotateSpeed = 360f;
    public float lifeTime = 8f;

    [Header("Launch Versions (grouped list)")]
    public LaunchVersionGroup[] launchVersions;

    [Header("Hit")]
    public string playerTag = "player";
    public string gateTag = "Gate";
    public bool damageGateOnHit = true;

    [Header("FX")]
    public string destroyPoolTag = "";
    public GameObject destroyFxPrefab;

    [Header("Pool")]
    public bool usePool = true;
    public string poolTag = "BossSlimeJelly";

    private Rigidbody2D rb;
    private float alive;
    private bool dead;
    private float runtimeLaunchSpeed = 4f;
    private float runtimeLaunchUpSpeed = 6f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        alive = 0f;
        dead = false;

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = true;
        }

        // 버전이 지정되지 않은 경우에도 동작하도록 기본값 유지
        runtimeLaunchSpeed = 4f;
        runtimeLaunchUpSpeed = 6f;
    }

    void Update()
    {
        if (dead) return;

        transform.Rotate(0f, 0f, rotateSpeed * Time.deltaTime, Space.Self);

        alive += Time.deltaTime;
        if (alive >= lifeTime)
            ExplodeAndDie(false);
    }

    public void Launch(Vector2 direction)
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (rb == null) return;

        Vector2 dir = direction;
        if (dir.sqrMagnitude <= 0.0001f) dir = Vector2.left;
        dir.Normalize();

        rb.linearVelocity = new Vector2(dir.x * runtimeLaunchSpeed, runtimeLaunchUpSpeed);
    }

    public int GetVersionCount()
    {
        return GetFlatVersionCount();
    }

    public bool TryGetRandomVersionPair(out int first, out int second)
    {
        first = -1;
        second = -1;

        int count = GetVersionCount();
        if (count < 2) return false;

        if (launchVersions != null && launchVersions.Length > 0)
        {
            List<int> validGroups = new List<int>();
            for (int g = 0; g < launchVersions.Length; g++)
            {
                int c = (launchVersions[g].versions != null) ? launchVersions[g].versions.Length : 0;
                if (c > 0) validGroups.Add(g);
            }

            if (validGroups.Count > 0)
            {
                int groupIndex = validGroups[Random.Range(0, validGroups.Count)];
                var group = launchVersions[groupIndex].versions;
                int c = group.Length;

                int localA = Random.Range(0, c);
                int localB = (c <= 1) ? localA : Random.Range(0, c - 1);
                if (c > 1 && localB >= localA) localB++;

                first = ToFlatIndex(groupIndex, localA);
                second = ToFlatIndex(groupIndex, localB);

                if (first >= 0 && second >= 0)
                    return true;
            }
        }

        // 폴백: 그룹이 비어있으면 기존 순차 페어 방식 유지
        int pairCount = Mathf.Max(1, count / 2);
        int pickedPair = Random.Range(0, pairCount);
        first = pickedPair * 2;
        second = Mathf.Min(first + 1, count - 1);
        return true;
    }

    public bool TryGetRandomVersionGroupIndices(out int[] flatIndices)
    {
        flatIndices = null;

        int total = GetVersionCount();
        if (total <= 0) return false;

        if (launchVersions != null && launchVersions.Length > 0)
        {
            List<int> validGroups = new List<int>();
            for (int g = 0; g < launchVersions.Length; g++)
            {
                int c = (launchVersions[g].versions != null) ? launchVersions[g].versions.Length : 0;
                if (c > 0) validGroups.Add(g);
            }

            if (validGroups.Count > 0)
            {
                int groupIndex = validGroups[Random.Range(0, validGroups.Count)];
                var group = launchVersions[groupIndex].versions;
                int c = group.Length;

                flatIndices = new int[c];
                for (int i = 0; i < c; i++)
                    flatIndices[i] = ToFlatIndex(groupIndex, i);
                return true;
            }
        }

        // 폴백: 그룹 정보가 없으면 전체 버전을 전부 사용
        flatIndices = new int[total];
        for (int i = 0; i < total; i++)
            flatIndices[i] = i;
        return true;
    }

    public void ApplyVersion(int index)
    {
        if (index < 0) return;
        if (!TryGetVersionByFlatIndex(index, out LaunchVersion v)) return;

        runtimeLaunchSpeed = v.speed;
        runtimeLaunchUpSpeed = v.upSpeed;
    }

    private int GetFlatVersionCount()
    {
        if (launchVersions == null) return 0;
        int total = 0;
        for (int g = 0; g < launchVersions.Length; g++)
            total += (launchVersions[g].versions != null) ? launchVersions[g].versions.Length : 0;
        return total;
    }

    private int ToFlatIndex(int groupIndex, int localIndex)
    {
        if (launchVersions == null || groupIndex < 0 || groupIndex >= launchVersions.Length)
            return -1;
        if (launchVersions[groupIndex].versions == null)
            return -1;
        if (localIndex < 0 || localIndex >= launchVersions[groupIndex].versions.Length)
            return -1;

        int flat = 0;
        for (int g = 0; g < groupIndex; g++)
            flat += (launchVersions[g].versions != null) ? launchVersions[g].versions.Length : 0;
        flat += localIndex;
        return flat;
    }

    private bool TryGetVersionByFlatIndex(int flatIndex, out LaunchVersion version)
    {
        version = default;
        if (launchVersions == null || flatIndex < 0) return false;

        int acc = 0;
        for (int g = 0; g < launchVersions.Length; g++)
        {
            var arr = launchVersions[g].versions;
            int c = (arr != null) ? arr.Length : 0;
            if (c <= 0) continue;

            if (flatIndex < acc + c)
            {
                version = arr[flatIndex - acc];
                return true;
            }
            acc += c;
        }
        return false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (dead) return;
        HandleHit(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (dead) return;
        if (collision == null) return;
        HandleHit(collision.collider);
    }

    private void HandleHit(Collider2D other)
    {
        if (other == null) return;
        if (other.CompareTag(playerTag))
        {
            ExplodeAndDie(true);
            return;
        }

        if (other.CompareTag(gateTag) || other.GetComponent<GateHealth>() != null)
        {
            if (damageGateOnHit && GateHealth.Instance != null)
                GateHealth.Instance.TakeBossMissileHit();

            ExplodeAndDie(true);
        }
    }

    private void ExplodeAndDie(bool spawnFx)
    {
        if (dead) return;
        dead = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        if (spawnFx)
        {
            if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(destroyPoolTag) && ObjectPool.Instance.HasPool(destroyPoolTag))
                ObjectPool.Instance.SpawnFromPool(destroyPoolTag, transform.position, Quaternion.identity);
            else if (destroyFxPrefab != null)
            {
                GameObject fx = Instantiate(destroyFxPrefab, transform.position, Quaternion.identity);
                if (fx != null)
                    Destroy(fx, 1.5f);
            }
        }

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            Destroy(gameObject);
    }
}
