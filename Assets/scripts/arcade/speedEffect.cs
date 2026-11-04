using UnityEngine;

[DefaultExecutionOrder(100)]
public class speedEffect : MonoBehaviour, IReinitializeOnEnable
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private Vector2 spawnWorldPosition = new Vector2(-0.01f, 3.8f);
    [SerializeField] private float despawnX = -20f;
    [SerializeField] private bool usePool = true;
    [SerializeField] private string poolTag = "";
    private float spawnSpeedMultiplier = 1f;
    private SpriteRenderer[] spriteRenderers;
    private float[] originalAlphas;
    private float opacity = 1f;
    private float fadeOutDuration = -1f;

    internal float HorizontalSpan
    {
        get
        {
            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            foreach (SpriteRenderer sprite in spriteRenderers)
            {
                if (sprite == null || sprite.sprite == null) continue;
                left = Mathf.Min(left, sprite.bounds.min.x);
                right = Mathf.Max(right, sprite.bounds.max.x);
            }
            return right > left ? right - left : 0f;
        }
    }

    internal void SetTravelSpeedMultiplier(float multiplier)
    {
        // Finished phases keep their last travel speed while fading to their endpoint.
        if (fadeOutDuration < 0f)
            spawnSpeedMultiplier = multiplier > 0f && !float.IsInfinity(multiplier) ? multiplier : 1f;
    }

    private void Awake()
    {
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalAlphas = new float[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
            originalAlphas[i] = spriteRenderers[i].color.a;
    }

    internal void SetOpacity(float value)
    {
        // An ended phase must keep fading even if another speed phase starts.
        if (fadeOutDuration >= 0f)
            return;
        ApplyOpacity(value);
    }

    internal void FadeOut(float duration)
    {
        if (fadeOutDuration >= 0f)
            return;
        fadeOutDuration = Mathf.Max(0f, duration);
        if (fadeOutDuration == 0f)
            ApplyOpacity(0f);
    }

    private void ApplyOpacity(float value)
    {
        opacity = Mathf.Clamp01(value);
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;
            Color color = spriteRenderers[i].color;
            color.a = originalAlphas[i] * opacity;
            spriteRenderers[i].color = color;
        }
    }

    private void OnEnable()
    {
        GameData.OnGameOver += HandleGameOver;
        CaptureSpawnSpeed();
        ResetToSpawnState();
    }

    private void OnDisable()
    {
        GameData.OnGameOver -= HandleGameOver;
    }

    public void Configure(float moveSpeed, Vector2 worldPos, float targetDespawnX, bool enablePool, string tag)
    {
        speed = moveSpeed;
        spawnWorldPosition = worldPos;
        despawnX = targetDespawnX;
        usePool = enablePool;
        poolTag = tag;

        CaptureSpawnSpeed();
        ResetToSpawnState();
    }

    public void Reinit()
    {
        CaptureSpawnSpeed();
        ResetToSpawnState();
    }

    public void DespawnNow()
    {
        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
        {
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
            return;
        }

        if (ObjectPool.Instance != null && ObjectPool.Instance.TryReturnActive(gameObject))
            return;

        Destroy(gameObject);
    }

    private void HandleGameOver()
    {
        DespawnNow();
    }

    private void Update()
    {
        if (RageTransformFreezeController.ShouldSkipGameplayFrame())
            return;

        if (fadeOutDuration > 0f)
            ApplyOpacity(Mathf.MoveTowards(opacity, 0f, Time.deltaTime / fadeOutDuration));

        Vector3 pos = transform.position;
        pos.x -= speed * spawnSpeedMultiplier * Time.deltaTime;
        pos.y = spawnWorldPosition.y;
        transform.position = pos;

        if (transform.position.x <= despawnX)
            DespawnNow();
    }

    private void ResetToSpawnState()
    {
        fadeOutDuration = -1f;
        ApplyOpacity(1f);
        Vector3 pos = transform.position;
        pos.x = spawnWorldPosition.x;
        pos.y = spawnWorldPosition.y;
        transform.SetPositionAndRotation(pos, Quaternion.identity);
    }

    private void CaptureSpawnSpeed()
    {
        // Emission follows stage speed, but existing streaks must finish even while the stage is stopped.
        float normalSpeed = GameData.Instance != null ? GameData.Instance.GetStageSpeedMultIgnoringObstacleSlowdown() : 1f;
        spawnSpeedMultiplier = normalSpeed > 0f && !float.IsInfinity(normalSpeed) ? normalSpeed : 1f;
    }
}
