using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Pool
{
    public string tag;
    public GameObject prefab;
    public int size;
}

public class ObjectPool : MonoBehaviour
{
    public static ObjectPool Instance;

    [Header("Pooling Settings")]
    public List<Pool> pools;
    [Min(1)] public int defaultExpandCount = 5;
    public bool allowRuntimeExpand = false;

    private Dictionary<string, Queue<GameObject>> poolDictionary;
    private Dictionary<string, HashSet<GameObject>> activeByTag;
    private Dictionary<string, GameObject> prefabByTag;
    private Dictionary<GameObject, CachedRefs> cacheByObject;
    private HashSet<string> exhaustedWarnedTags;
    private bool initialized;

    private sealed class CachedRefs
    {
        public Rigidbody2D rb;
        public Collider2D[] colliders;
        public Renderer[] renderers;
        public Animator[] animators;
        public IReinitializable[] reinitializables;
        public Missile missile;
    }

    private void EnsureInitialized()
    {
        if (initialized) return;

        poolDictionary = new Dictionary<string, Queue<GameObject>>();
        activeByTag = new Dictionary<string, HashSet<GameObject>>();
        prefabByTag = new Dictionary<string, GameObject>();
        cacheByObject = new Dictionary<GameObject, CachedRefs>();
        exhaustedWarnedTags = new HashSet<string>();

        if (pools != null)
        {
            for (int i = 0; i < pools.Count; i++)
            {
                var def = pools[i];
                if (def == null || string.IsNullOrEmpty(def.tag) || def.prefab == null)
                    continue;
                if (poolDictionary.ContainsKey(def.tag))
                    continue;

                var q = new Queue<GameObject>();
                int create = Mathf.Max(0, def.size);
                for (int n = 0; n < create; n++)
                    q.Enqueue(CreatePooledObject(def.prefab));

                poolDictionary[def.tag] = q;
                activeByTag[def.tag] = new HashSet<GameObject>();
                prefabByTag[def.tag] = def.prefab;
            }
        }

        initialized = true;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureInitialized();
    }

    void Start()
    {
        EnsureInitialized();
    }

    // ========= UTILITIES =========
    public bool HasPool(string tag)
    {
        EnsureInitialized();
        return poolDictionary.ContainsKey(tag);
    }

    public void RegisterPool(string tag, GameObject prefab, int size)
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(tag) || prefab == null) return;
        if (poolDictionary.ContainsKey(tag)) return;

        var q = new Queue<GameObject>();
        int create = Mathf.Max(0, size);
        for (int i = 0; i < create; i++)
            q.Enqueue(CreatePooledObject(prefab));

        poolDictionary[tag] = q;
        activeByTag[tag] = new HashSet<GameObject>();
        prefabByTag[tag] = prefab;
    }

    public void EnsurePoolSize(string tag, GameObject prefab, int minTotalSize)
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(tag) || prefab == null) return;

        int targetSize = Mathf.Max(0, minTotalSize);
        if (!poolDictionary.ContainsKey(tag))
        {
            RegisterPool(tag, prefab, targetSize);
            return;
        }

        if (!prefabByTag.ContainsKey(tag) || prefabByTag[tag] == null)
            prefabByTag[tag] = prefab;

        if (!activeByTag.TryGetValue(tag, out var activeSet))
        {
            activeSet = new HashSet<GameObject>();
            activeByTag[tag] = activeSet;
        }

        var q = poolDictionary[tag];
        int currentTotal = q.Count + activeSet.Count;
        int addCount = targetSize - currentTotal;
        for (int i = 0; i < addCount; i++)
            q.Enqueue(CreatePooledObject(prefab));
    }

    private bool EnsureCapacity(string tag, int addCount)
    {
        EnsureInitialized();
        if (!allowRuntimeExpand) return false;
        if (!poolDictionary.TryGetValue(tag, out var q)) return false;
        if (!prefabByTag.TryGetValue(tag, out var prefab) || prefab == null) return false;

        int count = Mathf.Max(1, addCount);
        for (int i = 0; i < count; i++)
            q.Enqueue(CreatePooledObject(prefab));

        exhaustedWarnedTags.Remove(tag);
        return true;
    }

    private GameObject CreatePooledObject(GameObject prefab)
    {
        var obj = Instantiate(prefab);
        obj.SetActive(false);
        obj.tag = "Untagged";
        GetOrCreateRefs(obj);
        return obj;
    }

    private CachedRefs GetOrCreateRefs(GameObject obj)
    {
        if (obj == null) return null;
        if (cacheByObject.TryGetValue(obj, out var refs) && refs != null) return refs;

        refs = new CachedRefs
        {
            rb = obj.GetComponent<Rigidbody2D>(),
            colliders = obj.GetComponentsInChildren<Collider2D>(true),
            renderers = obj.GetComponentsInChildren<Renderer>(true),
            animators = obj.GetComponentsInChildren<Animator>(true),
            reinitializables = obj.GetComponentsInChildren<IReinitializable>(true),
            missile = obj.GetComponent<Missile>()
        };
        cacheByObject[obj] = refs;
        return refs;
    }

    private GameObject DequeueAlive(Queue<GameObject> q)
    {
        while (q.Count > 0)
        {
            var obj = q.Dequeue();
            if (obj != null) return obj;
        }
        return null;
    }

    private void ResetSpawned(GameObject obj, string tag)
    {
        var refs = GetOrCreateRefs(obj);
        obj.tag = tag;
        obj.SetActive(true);

        var rb = refs != null ? refs.rb : null;
        if (rb)
        {
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.WakeUp();
        }

        if (refs != null && refs.colliders != null)
        {
            for (int i = 0; i < refs.colliders.Length; i++)
            {
                var c = refs.colliders[i];
                if (c != null) c.enabled = true;
            }
        }

        if (refs != null && refs.renderers != null)
        {
            for (int i = 0; i < refs.renderers.Length; i++)
            {
                var r = refs.renderers[i];
                if (r != null) r.enabled = true;
            }
        }

        if (refs != null && refs.animators != null)
        {
            for (int i = 0; i < refs.animators.Length; i++)
            {
                var a = refs.animators[i];
                if (a == null) continue;

                a.Rebind();
                a.Update(0f);
            }
        }

        if (refs != null && refs.reinitializables != null)
        {
            for (int i = 0; i < refs.reinitializables.Length; i++)
            {
                var r = refs.reinitializables[i];
                if (r != null) r.Reinit();
            }
        }
    }

    // ========= MAIN API =========
    public GameObject SpawnFromPool(string tag, Vector3 pos, Quaternion rot)
    {
        EnsureInitialized();
        if (!poolDictionary.ContainsKey(tag))
        {
            Debug.LogWarning($"[Pool] '{tag}' not found");
            return null;
        }

        var q = poolDictionary[tag];
        var obj = DequeueAlive(q);

        if (obj == null)
        {
            EnsureCapacity(tag, defaultExpandCount);
            obj = DequeueAlive(q);
        }

        if (obj == null)
        {
            if (!exhaustedWarnedTags.Contains(tag))
            {
                exhaustedWarnedTags.Add(tag);
                Debug.LogWarning($"[Pool] '{tag}' exhausted. Increase size or enable runtime expand.");
            }
            return null;
        }

        obj.transform.SetPositionAndRotation(pos, rot);
        ResetSpawned(obj, tag);

        activeByTag[tag].Add(obj);
        return obj;
    }

    public void ReturnToPool(string tag, GameObject obj)
    {
        EnsureInitialized();
        if (obj == null) return;

        if (!poolDictionary.ContainsKey(tag))
        {
            // 태그 불일치여도 현재 active 집합에서 찾아 복귀를 시도한다.
            if (TryReturnActive(obj))
                return;
            obj.SetActive(false);
            return;
        }

        var refs = GetOrCreateRefs(obj);
        var missile = refs != null ? refs.missile : null;
        if (missile != null)
            missile.NotifyReturnedByPool();

        var rb = refs != null ? refs.rb : null;
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
            rb.Sleep();
        }

        if (refs != null && refs.colliders != null)
        {
            for (int i = 0; i < refs.colliders.Length; i++)
            {
                var c = refs.colliders[i];
                if (c != null) c.enabled = false;
            }
        }

        obj.SetActive(false);
        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;

        if (activeByTag.TryGetValue(tag, out var set))
            set.Remove(obj);

        poolDictionary[tag].Enqueue(obj);
    }

    public bool TryReturnActive(GameObject obj)
    {
        EnsureInitialized();
        if (obj == null) return false;

        foreach (var kv in activeByTag)
        {
            if (!kv.Value.Contains(obj)) continue;
            ReturnToPool(kv.Key, obj);
            return true;
        }
        return false;
    }

    public void ReturnAllActive(params string[] tags)
    {
        EnsureInitialized();
        for (int t = 0; t < tags.Length; t++)
        {
            var tag = tags[t];
            if (!activeByTag.TryGetValue(tag, out var set)) continue;
            var arr = new List<GameObject>(set);
            for (int i = 0; i < arr.Count; i++)
            {
                var o = arr[i];
                if (o) ReturnToPool(tag, o);
            }
            set.Clear();
        }
    }
}
