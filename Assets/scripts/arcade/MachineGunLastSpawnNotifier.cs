using System;
using UnityEngine;

public class MachineGunLastSpawnNotifier : MonoBehaviour
{
    private Action<MachineGunLastSpawnNotifier> onDestroyTriggered;
    private Action<MachineGunLastSpawnNotifier> onDisabled;
    private bool notified;

    public void Bind(Action<MachineGunLastSpawnNotifier> destroyCallback, Action<MachineGunLastSpawnNotifier> disabledCallback = null)
    {
        onDestroyTriggered = destroyCallback;
        onDisabled = disabledCallback;
        notified = false;
    }

    public void ClearBinding()
    {
        onDestroyTriggered = null;
        onDisabled = null;
        notified = false;
    }

    public void NotifyDestroyTriggered()
    {
        if (notified)
            return;

        notified = true;
        onDestroyTriggered?.Invoke(this);
    }

    private void OnDisable()
    {
        onDisabled?.Invoke(this);
    }
}
