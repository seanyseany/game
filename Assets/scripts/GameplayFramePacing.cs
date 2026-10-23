using System.Collections.Generic;
using UnityEngine;

// Share the default desktop limit across village and arcade lifetimes. One
// scene unloading must not restore an uncapped rate while the next is active.
internal static class GameplayFramePacing
{
    private static readonly HashSet<Object> owners = new HashSet<Object>();
    private static bool ownsLimit;
    private static int previousLimit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Restore();
        owners.Clear();
    }

    internal static void Acquire(Object owner)
    {
        if (owner == null || !owners.Add(owner)) return;
        if (!ownsLimit && !Application.isMobilePlatform && QualitySettings.vSyncCount == 0 && Application.targetFrameRate < 0)
        {
            previousLimit = Application.targetFrameRate;
            Application.targetFrameRate = 60;
            ownsLimit = true;
        }
    }

    internal static void Release(Object owner)
    {
        owners.Remove(owner);
        if (owners.Count == 0) Restore();
    }

    private static void Restore()
    {
        if (ownsLimit && Application.targetFrameRate == 60) Application.targetFrameRate = previousLimit;
        ownsLimit = false;
    }
}
