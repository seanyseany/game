using System.Collections.Generic;
using UnityEngine;

public static class VillagePointerCapture
{
    private static readonly HashSet<int> ActiveCaptures = new HashSet<int>();

    public static bool HasActiveCapture => ActiveCaptures.Count > 0;

    public static bool ContainsPoint(Transform target, Vector2 worldPoint)
    {
        foreach (Collider2D collider in target.GetComponentsInChildren<Collider2D>())
            if (collider.enabled && collider.gameObject.activeInHierarchy && collider.OverlapPoint(worldPoint))
                return true;
        return false;
    }

    public static void Acquire(Object owner)
    {
        int key = GetKey(owner);
        if (key != 0)
            ActiveCaptures.Add(key);
    }

    public static void Release(Object owner)
    {
        int key = GetKey(owner);
        if (key != 0)
            ActiveCaptures.Remove(key);
    }

    public static void ReleaseAll()
    {
        ActiveCaptures.Clear();
    }

    private static int GetKey(Object owner)
    {
        return owner != null ? owner.GetInstanceID() : 0;
    }
}
