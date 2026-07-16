using System.Collections.Generic;
using UnityEngine;

public class CameraShakeManager : MonoBehaviour
{
    private sealed class ShakeRequest
    {
        public float duration;
        public float magnitudeX;
        public float magnitudeY;
        public float frequency;
        public float elapsed;
        public bool decay;
        public float noiseSeed;
    }

    private static CameraShakeManager instance;

    [Header("Defaults")]
    public float defaultDuration = 0.16f;
    public float defaultMagnitudeX = 0.08f;
    public float defaultMagnitudeY = 0.05f;
    public float defaultFrequency = 28f;
    [SerializeField] private bool allowHorizontalShake = false;
    [SerializeField] private bool allowVerticalShake = true;

    private readonly List<ShakeRequest> activeShakes = new List<ShakeRequest>(8);
    private Vector3 lastAppliedOffset;

    public static CameraShakeManager Instance
    {
        get
        {
            if (instance != null)
                return instance;

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
                return null;

            instance = mainCamera.GetComponent<CameraShakeManager>();
            if (instance == null)
                instance = mainCamera.gameObject.AddComponent<CameraShakeManager>();

            return instance;
        }
    }

    public static void Shake(float duration, float magnitudeX, float magnitudeY, float frequency = 28f, bool decay = true)
    {
        CameraShakeManager manager = Instance;
        if (manager == null)
            return;

        manager.EnqueueShake(duration, magnitudeX, magnitudeY, frequency, decay);
    }

    public static void ShakeDefault()
    {
        CameraShakeManager manager = Instance;
        if (manager == null)
            return;

        manager.EnqueueDefaultShakeImmediate();
    }

    public static void ShakeDefaultHalf()
    {
        CameraShakeManager manager = Instance;
        if (manager == null)
            return;

        manager.EnqueueShake(
            manager.defaultDuration,
            manager.defaultMagnitudeX * 0.5f,
            manager.defaultMagnitudeY * 0.5f,
            manager.defaultFrequency,
            decay: true);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
    }

    private void OnDisable()
    {
        transform.position -= lastAppliedOffset;
        lastAppliedOffset = Vector3.zero;
        activeShakes.Clear();
    }

    private void LateUpdate()
    {
        transform.position -= lastAppliedOffset;
        lastAppliedOffset = Vector3.zero;

        if (activeShakes.Count == 0)
            return;

        Vector2 combinedOffset = Vector2.zero;

        for (int i = activeShakes.Count - 1; i >= 0; i--)
        {
            ShakeRequest request = activeShakes[i];
            if (request == null)
            {
                activeShakes.RemoveAt(i);
                continue;
            }

            request.elapsed += Time.deltaTime;
            float progress = request.duration <= 0.0001f ? 1f : Mathf.Clamp01(request.elapsed / request.duration);
            if (progress >= 1f)
            {
                activeShakes.RemoveAt(i);
                continue;
            }

            float amplitude = request.decay ? (1f - progress) : 1f;
            float sampleTime = request.noiseSeed + (request.elapsed * request.frequency);
            float noiseX = (Mathf.PerlinNoise(sampleTime, 0.17f) * 2f) - 1f;
            float noiseY = (Mathf.PerlinNoise(0.29f, sampleTime) * 2f) - 1f;
            combinedOffset.x += noiseX * request.magnitudeX * amplitude;
            combinedOffset.y += noiseY * request.magnitudeY * amplitude;
        }

        if (!allowHorizontalShake)
            combinedOffset.x = 0f;

        if (!allowVerticalShake)
            combinedOffset.y = 0f;

        lastAppliedOffset = new Vector3(combinedOffset.x, combinedOffset.y, 0f);
        transform.position += lastAppliedOffset;
    }

    private void EnqueueShake(float duration, float magnitudeX, float magnitudeY, float frequency, bool decay)
    {
        ShakeRequest request = new ShakeRequest
        {
            duration = Mathf.Max(0.01f, duration),
            magnitudeX = Mathf.Abs(magnitudeX),
            magnitudeY = Mathf.Abs(magnitudeY),
            frequency = Mathf.Max(0.01f, frequency),
            decay = decay,
            elapsed = 0f,
            noiseSeed = Random.Range(0f, 1000f)
        };

        activeShakes.Add(request);
    }

    private void EnqueueDefaultShakeImmediate()
    {
        EnqueueShake(
            defaultDuration,
            defaultMagnitudeX,
            defaultMagnitudeY,
            defaultFrequency,
            decay: true);
    }

}
