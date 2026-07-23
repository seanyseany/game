using System.Collections.Generic;
using UnityEngine;

public static class VillagePointerCapture
{
    private static readonly HashSet<int> ActiveCaptures = new HashSet<int>();

    public static bool HasActiveCapture => ActiveCaptures.Count > 0;

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
