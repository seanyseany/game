using UnityEngine;

// Shared feedback for the hold before a placed village object can be dragged.
internal struct VillageHoldShake
{
    private const float Amplitude = 0.035f;
    private const float Frequency = 18f;

    private Transform target;
    private Transform originalParent;
    private Vector3 originalLocalPosition;

    public void Begin(Transform nextTarget)
    {
        Stop();
        target = nextTarget;
        if (target == null)
            return;

        originalParent = target.parent;
        originalLocalPosition = target.localPosition;
    }

    public void Tick(float elapsed)
    {
        if (target == null)
            return;
        if (target.parent != originalParent)
        {
            target = null;
            return;
        }

        float offset = Mathf.Sin(elapsed * Frequency * Mathf.PI * 2f) * Amplitude;
        target.localPosition = originalLocalPosition + Vector3.right * offset;
    }

    public void Stop()
    {
        if (target != null && target.parent == originalParent)
            target.localPosition = originalLocalPosition;
        target = null;
        originalParent = null;
    }
}
