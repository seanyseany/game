using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Missile : MonoBehaviour, IRageTransformPauseHandler
{
    [SerializeField] private float baseSpeed = 12.5f; 
    [SerializeField] private float life = 5f;

    [Header("Explosion Effects")]
    public GameObject[] explosionPrefabs;

    private Rigidbody2D rb;
    private float spawnTime;
    private float pausedRemainingLife = -1f;
    private bool returned = false;
    private bool forceDisabledByPool = false;
    private bool waitingForParticles;
    private ParticleSystem[] flightParticles;
    private ParticleSystemCullingMode[] initialParticleCullingModes;
    private Renderer[] renderers;
    private bool[] initialRendererEnabled;
    private Collider2D[] colliders;
    private bool[] initialColliderEnabled;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;

        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        flightParticles = GetComponentsInChildren<ParticleSystem>(true);
        initialParticleCullingModes = new ParticleSystemCullingMode[flightParticles.Length];
        for (int i = 0; i < flightParticles.Length; i++)
            initialParticleCullingModes[i] = flightParticles[i].main.cullingMode;
        renderers = GetComponentsInChildren<Renderer>(true);
        initialRendererEnabled = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            initialRendererEnabled[i] = renderers[i].enabled;
        colliders = GetComponentsInChildren<Collider2D>(true);
        initialColliderEnabled = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
            initialColliderEnabled[i] = colliders[i].enabled;
    }

    void OnEnable()
    {
        returned = false;
        forceDisabledByPool = false;
        waitingForParticles = false;
        pausedRemainingLife = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = initialRendererEnabled[i];
        }
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) colliders[i].enabled = initialColliderEnabled[i];
        }
        for (int i = 0; i < flightParticles.Length; i++)
        {
            ParticleSystem particles = flightParticles[i];
            if (particles == null) continue;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.cullingMode = initialParticleCullingModes[i];
            if (particles.gameObject.activeInHierarchy && main.playOnAwake)
                particles.Play(false);
        }

        spawnTime = Time.time;
        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.left * baseSpeed;
        rb.angularVelocity = 0f;
    }

    public void ForceLaunch(float speedOverride)
    {
        if (returned || forceDisabledByPool) return;
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;

        rb.simulated = true;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.left * Mathf.Abs(speedOverride);
        rb.angularVelocity = 0f;
    }


    void Update()
    {
        if (forceDisabledByPool) return;
        if (returned)
        {
            if (waitingForParticles && !HasLiveFlightParticles()) CompleteReturnToPool();
            return;
        }
        if (RageTransformFreezeController.IsGameplayPauseActive)
            return;

        // 🔥 life가 끝나면 자동 회수
        if (!returned && !forceDisabledByPool && Time.time - spawnTime >= life)
        {
            ReturnToPool();
        }
    }

    public void OnRageTransformPauseStarted()
    {
        if (!isActiveAndEnabled || returned || forceDisabledByPool)
            return;

        pausedRemainingLife = Mathf.Max(0f, life - (Time.time - spawnTime));
    }

    public void OnRageTransformPauseEnded()
    {
        if (!isActiveAndEnabled || returned || forceDisabledByPool || pausedRemainingLife < 0f)
            return;

        spawnTime = Time.time - (life - pausedRemainingLife);
        pausedRemainingLife = -1f;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (returned || forceDisabledByPool) return;

        // 플레이어 충돌
        var player = other.GetComponent<Player>();
        if (player != null)
        {
            if (GameData.Instance != null &&
                GameData.Instance.selectedPlayerType == 3)
            {
                Explode();
                ReturnToPool();
                return;
            }

            if (!player.IsRageModeActive())
            {
                CameraShakeManager.Shake(CameraShakeManager.ShakeEvent.MissileHit);
                player.TakeDamageFromSource(1, ObstacleType.Missile);
            }

            Explode();
            ReturnToPool();
            return;
        }

        // 적 충돌은 무시
        if (other.CompareTag("Enemy"))
            return;
    }

    private void Explode()
    {
        ArcadeFeedback.ShowBoom(transform.position);

        // 풀에 Smoke가 있으면 우선 사용
        if (ObjectPool.Instance != null)
        {
            GameObject effect = ObjectPool.Instance.SpawnFromPool("Smoke", transform.position, Quaternion.identity);
            if (effect == null && explosionPrefabs.Length > 0)
            {
                int idx = Random.Range(0, explosionPrefabs.Length);
                Instantiate(explosionPrefabs[idx], transform.position, Quaternion.identity);
            }
            return;
        }

        // 풀 없을 때
        if (explosionPrefabs.Length > 0)
        {
            int idx = Random.Range(0, explosionPrefabs.Length);
            Instantiate(explosionPrefabs[idx], transform.position, Quaternion.identity);
        }
    }

    // 🔥 풀 반환을 하나의 메서드로 통일
    private void ReturnToPool()
    {
        // 두 번 반환 방지
        if (returned || forceDisabledByPool) return;
        returned = true;

        // Rigidbody 안정화
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.Sleep();
        rb.simulated = false;

        // Remove the projectile immediately, but keep its existing smoke alive
        // at the final pose until the particles finish their normal lifetime.
        foreach (Collider2D collider in colliders)
        {
            if (collider != null) collider.enabled = false;
        }
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && !(renderer is ParticleSystemRenderer)) renderer.enabled = false;
        }
        foreach (ParticleSystem particles in flightParticles)
        {
            if (particles == null) continue;
            var main = particles.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        waitingForParticles = HasLiveFlightParticles();
        if (!waitingForParticles) CompleteReturnToPool();
    }

    private bool HasLiveFlightParticles()
    {
        foreach (ParticleSystem particles in flightParticles)
        {
            if (particles != null && particles.gameObject.activeInHierarchy && particles.IsAlive(false))
                return true;
        }
        return false;
    }

    private void CompleteReturnToPool()
    {
        waitingForParticles = false;
        // 풀로 반환
        if (ObjectPool.Instance != null)
            ObjectPool.Instance.ReturnToPool("missile", gameObject);
        else
            Destroy(gameObject);
    }

    private void OnDisable()
    {
        waitingForParticles = false;
        pausedRemainingLife = -1f;
        foreach (ParticleSystem particles in flightParticles)
        {
            if (particles != null)
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    public void NotifyReturnedByPool()
    {
        forceDisabledByPool = true;
        waitingForParticles = false;
    }
}
