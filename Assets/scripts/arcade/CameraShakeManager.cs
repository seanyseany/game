using System.Collections.Generic;
using UnityEngine;

public class CameraShakeManager : MonoBehaviour
{
    public enum ShakeEvent
    {
        NormalPlayer1, NormalPlayer2, NormalPlayer3, NormalPlayer4, NormalPlayer5,
        RagePlayer1, RagePlayer2, RagePlayer3, RagePlayer4, RagePlayer5,
        RageTransform, RagePlayer3Landing,
        MissileHit, CholesterolBombExplosion, FishBombExplosion, BossSlimeArmImpact,
        BossDeath, MiniBossLanding, BossSlimeCannonBallExplosion, GateHit,
        GenericHalf, GenericQuarter
    }

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
    [Tooltip("모든 상황에서 공통으로 사용하는 흔들림 시간입니다.")]
    public float defaultDuration = 0.16f;
    public float defaultMagnitudeX = 0.08f;
    public float defaultMagnitudeY = 0.05f;
    public float defaultFrequency = 28f;
    public bool defaultDecay = true;
    [Tooltip("모든 상황의 가로 흔들림을 허용합니다.")]
    [SerializeField] private bool allowHorizontalShake = false;
    [Tooltip("모든 상황의 세로 흔들림을 허용합니다.")]
    [SerializeField] private bool allowVerticalShake = true;

    [Header("Normal Player Attack Multipliers")]
    [Tooltip("공격 중 floor 접촉. 기본 X/Y 세기 × 배율 (0이면 끔)")]
    [Min(0f)] public float normalPlayer1Multiplier = 0.25f;
    [Tooltip("공의 floor 충돌. 기본 X/Y 세기 × 배율 (0이면 끔)")]
    [Min(0f)] public float normalPlayer2Multiplier = 0.25f;
    [Tooltip("공격 시작. 기본 X/Y 세기 × 배율 (0이면 끔)")]
    [Min(0f)] public float normalPlayer3Multiplier = 0.25f;
    [Tooltip("EmptyBall의 floor 충돌. 기본 X/Y 세기 × 배율 (0이면 끔)")]
    [Min(0f)] public float normalPlayer4Multiplier = 0.25f;
    [Tooltip("공격 중 floor 접촉. 기본 X/Y 세기 × 배율 (0이면 끔)")]
    [Min(0f)] public float normalPlayer5Multiplier = 0.25f;

    [Header("Rage Player Attack Multipliers")]
    [Min(0f)] public float ragePlayer1Multiplier = 0.5f;
    [Min(0f)] public float ragePlayer2Multiplier = 0.25f;
    [Min(0f)] public float ragePlayer3Multiplier = 0.25f;
    [Min(0f)] public float ragePlayer4Multiplier = 0.25f;
    [Min(0f)] public float ragePlayer5Multiplier = 0.5f;

    [Header("Rage Effect Multipliers")]
    [Min(0f)] public float rageTransformMultiplier = 0.5f;
    [Min(0f)] public float ragePlayer3LandingMultiplier = 0.5f;

    [Header("Hit And Explosion Multipliers")]
    [Min(0f)] public float missileHitMultiplier = 1f;
    [Min(0f)] public float cholesterolBombExplosionMultiplier = 1f;
    [Min(0f)] public float fishBombExplosionMultiplier = 1f;
    [Min(0f)] public float bossSlimeArmImpactMultiplier = 1f;
    [Min(0f)] public float bossDeathMultiplier = 1f;
    [Min(0f)] public float miniBossLandingMultiplier = 0.5f;
    [Min(0f)] public float bossSlimeCannonBallExplosionMultiplier = 0.5f;
    [Min(0f)] public float gateHitMultiplier = 0.5f;

    [Header("Generic Preset Multipliers")]
    [Min(0f)] public float genericHalfMultiplier = 0.5f;
    [Min(0f)] public float genericQuarterMultiplier = 0.25f;

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
        Shake(ShakeEvent.GenericHalf);
    }

    public static void ShakeDefaultQuarter()
    {
        Shake(ShakeEvent.GenericQuarter);
    }

    public static void ShakePlayerAttack(int playerType, bool rage)
    {
        if (playerType < 1 || playerType > 5) return;
        Shake((ShakeEvent)((rage ? (int)ShakeEvent.RagePlayer1 : (int)ShakeEvent.NormalPlayer1) + playerType - 1));
    }

    public static void Shake(ShakeEvent source)
    {
        CameraShakeManager manager = Instance;
        if (manager == null) return;
        float multiplier = manager.GetMultiplier(source);
        if (multiplier <= 0f || manager.defaultDuration <= 0f) return;
        manager.EnqueueShake(manager.defaultDuration, manager.defaultMagnitudeX * multiplier,
            manager.defaultMagnitudeY * multiplier, manager.defaultFrequency, manager.defaultDecay);
    }

    public float GetMultiplier(ShakeEvent source)
    {
        return source switch
        {
            ShakeEvent.NormalPlayer1 => normalPlayer1Multiplier,
            ShakeEvent.NormalPlayer2 => normalPlayer2Multiplier,
            ShakeEvent.NormalPlayer3 => normalPlayer3Multiplier,
            ShakeEvent.NormalPlayer4 => normalPlayer4Multiplier,
            ShakeEvent.NormalPlayer5 => normalPlayer5Multiplier,
            ShakeEvent.RagePlayer1 => ragePlayer1Multiplier,
            ShakeEvent.RagePlayer2 => ragePlayer2Multiplier,
            ShakeEvent.RagePlayer3 => ragePlayer3Multiplier,
            ShakeEvent.RagePlayer4 => ragePlayer4Multiplier,
            ShakeEvent.RagePlayer5 => ragePlayer5Multiplier,
            ShakeEvent.RageTransform => rageTransformMultiplier,
            ShakeEvent.RagePlayer3Landing => ragePlayer3LandingMultiplier,
            ShakeEvent.MissileHit => missileHitMultiplier,
            ShakeEvent.CholesterolBombExplosion => cholesterolBombExplosionMultiplier,
            ShakeEvent.FishBombExplosion => fishBombExplosionMultiplier,
            ShakeEvent.BossSlimeArmImpact => bossSlimeArmImpactMultiplier,
            ShakeEvent.BossDeath => bossDeathMultiplier,
            ShakeEvent.MiniBossLanding => miniBossLandingMultiplier,
            ShakeEvent.BossSlimeCannonBallExplosion => bossSlimeCannonBallExplosionMultiplier,
            ShakeEvent.GateHit => gateHitMultiplier,
            ShakeEvent.GenericHalf => genericHalfMultiplier,
            ShakeEvent.GenericQuarter => genericQuarterMultiplier,
            _ => 0f
        };
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
            defaultDecay);
    }

}
