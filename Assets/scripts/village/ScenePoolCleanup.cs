using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

internal static class ScenePoolCleanup
{
    internal static void RemoveUnloaded<T>(Dictionary<int, Queue<T>> pools, Scene unloaded) where T : Component
    {
        var emptyKeys = ListPool<int>.Get();
        foreach (var entry in pools)
        {
            Queue<T> queue = entry.Value;
            int count = queue.Count;
            for (int i = 0; i < count; i++)
            {
                T instance = queue.Dequeue();
                if (instance != null && instance.gameObject.scene != unloaded) queue.Enqueue(instance);
            }
            if (queue.Count == 0) emptyKeys.Add(entry.Key);
        }
        foreach (int key in emptyKeys) pools.Remove(key);
        ListPool<int>.Release(emptyKeys);
    }
}
