using UnityEngine;

public class speedEffect : MonoBehaviour, IReinitializeOnEnable
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private Vector2 spawnWorldPosition = new Vector2(-0.01f, 3.8f);
    [SerializeField] private float despawnX = -20f;
    [SerializeField] private bool usePool = true;
    [SerializeField] private string poolTag = "";
    private float spawnSpeedMultiplier = 1f;

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

        Vector3 pos = transform.position;
        pos.x -= speed * spawnSpeedMultiplier * Time.deltaTime;
        pos.y = spawnWorldPosition.y;
        transform.position = pos;

        if (transform.position.x <= despawnX)
            DespawnNow();
    }

    private void ResetToSpawnState()
    {
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
