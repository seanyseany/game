using UnityEngine;

public class speedEffect : MonoBehaviour, IReinitializable
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private Vector2 spawnWorldPosition = new Vector2(-0.01f, 3.8f);
    [SerializeField] private float despawnX = -20f;
    [SerializeField] private bool usePool = true;
    [SerializeField] private string poolTag = "";

    private void OnEnable()
    {
        GameData.OnGameOver += HandleGameOver;
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

        ResetToSpawnState();
    }

    public void Reinit()
    {
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
        float mult = GameData.Instance != null ? GameData.Instance.GetStageSpeedMult() : 1f;
        Vector3 pos = transform.position;
        pos.x -= speed * mult * Time.deltaTime;
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
}
