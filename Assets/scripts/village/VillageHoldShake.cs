using UnityEngine;

// Shared feedback for the hold before a placed village object can be dragged.
internal struct VillageHoldShake
{
    private const float Amplitude = 0.035f;
    private const float Frequency = 18f;
    private const float StartDelay = 0.5f;
    private const float FullSpeedLeadTime = 0.5f;

    private Transform target;
    private Transform originalParent;
    private Vector3 originalLocalPosition;

    // Hit testing follows the unshaken object so feedback cannot cancel a stationary press.
    public Vector3 WorldOffset => target != null && target.parent == originalParent
        ? target.position - (originalParent != null ? originalParent.TransformPoint(originalLocalPosition) : originalLocalPosition)
        : Vector3.zero;

    public void Begin(Transform nextTarget)
    {
        Stop();
        target = nextTarget;
        if (target == null)
            return;

        originalParent = target.parent;
        originalLocalPosition = target.localPosition;
    }

    public void Tick(float elapsed, float holdDuration)
    {
        if (target == null)
            return;
        if (target.parent != originalParent)
        {
            target = null;
            return;
        }

        float shakeElapsed = Mathf.Max(0f, elapsed - StartDelay);
        float accelerationDuration = Mathf.Max(0f, holdDuration - StartDelay - FullSpeedLeadTime);
        float cycles;
        if (accelerationDuration > 0f)
        {
            float acceleratingTime = Mathf.Min(shakeElapsed, accelerationDuration);
            float initialFrequency = Frequency * 0.5f;
            // Integrate frequency so the oscillation stays continuous while speeding up.
            cycles = initialFrequency * acceleratingTime
                + 0.5f * (Frequency - initialFrequency) * acceleratingTime * acceleratingTime / accelerationDuration
                + Frequency * Mathf.Max(0f, shakeElapsed - accelerationDuration);
        }
        else
        {
            cycles = shakeElapsed * Frequency;
        }

        float offset = Mathf.Sin(cycles * Mathf.PI * 2f) * Amplitude;
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
