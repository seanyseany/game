using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BossSlimeArm : MonoBehaviour, IReinitializable
{
    [Header("Move (WORLD)")]
    [HideInInspector]
    public Vector3 armStartWorldPos = new Vector3(-0.6f, -2f, -1f);
    [HideInInspector]
    public Vector3 armEndWorldPos = new Vector3(-0.6f, -9f, -1f);
    public float moveTime = 1f;

    [Header("Jelly Spawn")]
    [HideInInspector]
    public Vector3[] jellySpawnWorldPoints;
    public float jellySpawnDelay = 0.85f;
    public float stayAfterJellySpawn = 0.5f;
    public string jellyPoolTag = "BossSlimeJelly";
    public GameObject jellyPrefab;

    [Header("Pool")]
    public bool usePool = true;
    public string armPoolTag = "BossSlimeArm";

    private Coroutine routine;
    private Coroutine flashRoutine;
    private BossSlime ownerBoss;
    private Renderer[] cachedRenderers;
    private List<Color[]> originalColors = new List<Color[]>();

    void Awake()
    {
        CacheRendererColors();
    }

    public void Reinit()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.position = armStartWorldPos;

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        RestoreRendererColors();

        // 풀 재사용 시 비활성화된 렌더러가 남아있으면 다시 켠다.
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = true;
    }

    public void PlayOnce(BossSlime owner)
    {
        ownerBoss = owner;
        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(CoPlay(owner));
    }

    private IEnumerator CoPlay(BossSlime owner)
    {
        Vector3 start = armStartWorldPos;
        Vector3 end = armEndWorldPos;
        transform.position = start;

        float duration = Mathf.Max(0.01f, moveTime);
        float t = 0f;
        while (t < duration)
        {
            float k = t / duration;
            transform.position = Vector3.Lerp(start, end, k);
            t += Time.deltaTime;
            yield return null;
        }
        transform.position = end;

        if (jellySpawnDelay > 0f)
            yield return new WaitForSeconds(jellySpawnDelay);

        if (owner != null && owner.isActiveAndEnabled)
            SpawnTwoRandomJellies(owner.transform);

        if (stayAfterJellySpawn > 0f)
            yield return new WaitForSeconds(stayAfterJellySpawn);

        transform.SetParent(null, true);
        if (ownerBoss != null)
            ownerBoss.UnregisterActiveArm(this);

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(armPoolTag) && ObjectPool.Instance.HasPool(armPoolTag))
            ObjectPool.Instance.ReturnToPool(armPoolTag, gameObject);
        else
            Destroy(gameObject);

        routine = null;
    }

    private void SpawnTwoRandomJellies(Transform bossTransform)
    {
        if (jellyPrefab == null && string.IsNullOrEmpty(jellyPoolTag)) return;

        Vector3[] points = jellySpawnWorldPoints;
        if (points == null || points.Length == 0)
            points = new Vector3[] { new Vector3(3.136576f, -2.2f, 0f), new Vector3(4.53618f, -2f, 0f) };

        int[] versionIndices = GetJellySpawnIndices();
        if (versionIndices == null || versionIndices.Length == 0)
            versionIndices = new int[] { -1 };

        // 선택된 그룹의 버전을 전부 스폰한다. (포인트가 부족하면 순환 사용)
        for (int i = 0; i < versionIndices.Length; i++)
        {
            Vector3 spawnPoint = points[i % points.Length];
            SpawnOneJellyAtWorld(spawnPoint, -1f, versionIndices[i]);
        }
    }

    // 선택된 그룹의 버전 인덱스를 모두 가져온다.
    private int[] GetJellySpawnIndices()
    {
        int versionCount = 0;
        if (jellyPrefab != null)
        {
            var refJelly = jellyPrefab.GetComponent<BossSlimeJelly>();
            if (refJelly != null)
            {
                if (refJelly.TryGetRandomVersionGroupIndices(out int[] groupIndices))
                    return groupIndices;

                versionCount = refJelly.GetVersionCount();
            }
        }

        if (versionCount <= 0)
            return null;

        int[] fallback = new int[versionCount];
        for (int i = 0; i < versionCount; i++)
            fallback[i] = i;
        return fallback;
    }

    private void SpawnOneJellyAtWorld(Vector3 worldPos, float dirX, int forcedVersionIndex)
    {
        GameObject jellyObj = null;
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(jellyPoolTag) && ObjectPool.Instance.HasPool(jellyPoolTag))
            jellyObj = ObjectPool.Instance.SpawnFromPool(jellyPoolTag, worldPos, Quaternion.identity);
        else if (jellyPrefab != null)
            jellyObj = Instantiate(jellyPrefab, worldPos, Quaternion.identity);

        if (jellyObj == null) return;

        var jelly = jellyObj.GetComponent<BossSlimeJelly>();
        if (jelly == null) return;

        jelly.usePool = usePool;
        jelly.poolTag = jellyPoolTag;

        int vCount = jelly.GetVersionCount();
        if (vCount > 0)
        {
            if (forcedVersionIndex >= 0)
                jelly.ApplyVersion(Mathf.Clamp(forcedVersionIndex, 0, vCount - 1));
            else
                jelly.ApplyVersion(Random.Range(0, vCount));
        }

        jelly.Launch(new Vector2(dirX, 1f));
    }

    public void TriggerHitFlash(Color color, float duration)
    {
        if (flashRoutine != null)
            StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(CoFlash(color, duration));
    }

    private IEnumerator CoFlash(Color color, float duration)
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0 || originalColors.Count != cachedRenderers.Length)
            CacheRendererColors();

        SetAllRenderersColor(color);
        yield return new WaitForSeconds(Mathf.Max(0.01f, duration));
        RestoreRendererColors();
        flashRoutine = null;
    }

    private void CacheRendererColors()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        originalColors.Clear();

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            var r = cachedRenderers[i];
            if (r == null)
            {
                originalColors.Add(null);
                continue;
            }

            var mats = r.materials;
            Color[] cols = new Color[mats.Length];
            for (int m = 0; m < mats.Length; m++)
                cols[m] = (mats[m] != null && mats[m].HasProperty("_Color")) ? mats[m].color : Color.white;
            originalColors.Add(cols);
        }
    }

    private void SetAllRenderersColor(Color c)
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0)
            cachedRenderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            var r = cachedRenderers[i];
            if (r == null) continue;
            var mats = r.materials;
            for (int m = 0; m < mats.Length; m++)
                if (mats[m] != null && mats[m].HasProperty("_Color"))
                    mats[m].color = c;
        }
    }

    private void RestoreRendererColors()
    {
        if (cachedRenderers == null || originalColors == null) return;
        if (originalColors.Count != cachedRenderers.Length) return;

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            var r = cachedRenderers[i];
            var cols = originalColors[i];
            if (r == null || cols == null) continue;

            var mats = r.materials;
            for (int m = 0; m < mats.Length && m < cols.Length; m++)
                if (mats[m] != null && mats[m].HasProperty("_Color"))
                    mats[m].color = cols[m];
        }
    }
}
