using UnityEngine;
using System.Collections;

public class GateHealth : MonoBehaviour
{
    private const string GateSmoke1PoolTag = "GateSmoke";
    private const string GateSmoke2PoolTag = "GateSmokeBack";

    public static GateHealth Instance;
    public static System.Action OnOpenHoldStarted;

    [Header("Sprites - 단계별(0,1,2)")]
    public Sprite[] closedSprites;
    public Sprite[] halfOpenSprites;
    public Sprite[] openSprites;
    public Sprite brokenSprite;

    [Header("Settings")]
    public int maxHits = 3; // 3회 맞으면 파괴

    [Header("Gate Smoke Spawn Positions")]
    [Tooltip("트레인 하위에 만든 GateSmoke 스폰 위치를 등록하세요.")]
    public Transform gateSmokeSpawnPoint;
    [Tooltip("트레인 하위에 만든 GateSmokeBack 스폰 위치를 등록하세요.")]
    public Transform gateSmokeBackSpawnPoint;

    [Header("Damage FX")]
    public GameObject gateDamageFxPrefab;
    public string gateDamageFxPoolTag = "GateDamageFx";
    public Vector3 gateDamageFxLocalOffset = Vector3.zero;
    [Min(0)] public int gateDamageFxPoolSize = 4;

    private int hitCount = 0;
    private SpriteRenderer sr;
    private Coroutine animCo;
    private GameObject activeGateSmoke1;
    private GameObject activeGateSmoke2;

    private enum GateState { Closed, Opening, Open, Closing, Broken }
    private enum GateVisualPhase { Closed, HalfOpen, Open }
    private GateState state = GateState.Closed;
    private GateVisualPhase visualPhase = GateVisualPhase.Closed;
    private bool machineGunReturnGateLocked;
    private int openHoldCount;

    public bool IsBroken => state == GateState.Broken;
    public int CurrentHits => hitCount;
    public int RemainingHits => Mathf.Max(0, maxHits - hitCount);

    void Awake()
    {
        Instance = this;
        sr = GetComponent<SpriteRenderer>();
        state = GateState.Closed;
        visualPhase = GateVisualPhase.Closed;
        EnsureDamageFxPoolReady();
        ApplyCurrentVisualSprite();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Enemy-Slime") || other.CompareTag("Enemy-Vacteria"))
        {
            Destroy(other.gameObject);
            TakeHit();
        }
    }

    // ✅ [추가] 보스 미사일 1발당 문 목숨 -1
    public void TakeBossMissileHit()
    {
        if (state == GateState.Broken) return;
        TakeHit();
    }

    private void TakeHit()
    {
        hitCount++;
        CameraShakeManager.ShakeDefaultHalf();
        SpawnGateDamageFx();
        SpawnSmokeForCurrentHit();

        if (hitCount >= maxHits)
        {
            if (animCo != null) StopCoroutine(animCo);
            animCo = null;
            state = GateState.Broken;
            if (sr != null)
                sr.sprite = brokenSprite;
            GameData.Instance.TriggerGameOver();
            return;
        }

        ApplyCurrentVisualSprite();
    }

    private void SpawnSmokeForCurrentHit()
    {
        if (hitCount == 1)
            activeGateSmoke1 = SpawnGateSmoke(GateSmoke1PoolTag, gateSmokeSpawnPoint);
        else if (hitCount == 2)
            activeGateSmoke2 = SpawnGateSmoke(GateSmoke2PoolTag, gateSmokeBackSpawnPoint);
    }

    private static GameObject SpawnGateSmoke(string poolTag, Transform spawnPoint)
    {
        if (spawnPoint == null || ObjectPool.Instance == null || !ObjectPool.Instance.HasPool(poolTag))
            return null;

        GameObject smoke = ObjectPool.Instance.SpawnFromPool(poolTag, spawnPoint.position, spawnPoint.rotation);
        if (smoke == null)
            return null;

        smoke.transform.SetParent(spawnPoint, true);
        smoke.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        return smoke;
    }

    private static void ReturnGateSmoke(string poolTag, GameObject smoke)
    {
        if (smoke == null)
            return;

        if (ObjectPool.Instance != null && ObjectPool.Instance.HasPool(poolTag))
        {
            ObjectPool.Instance.ReturnToPool(poolTag, smoke);
            return;
        }

        smoke.transform.SetParent(null, true);
        smoke.SetActive(false);
    }

    private void ResetGateSmokes()
    {
        ReturnGateSmoke(GateSmoke1PoolTag, activeGateSmoke1);
        ReturnGateSmoke(GateSmoke2PoolTag, activeGateSmoke2);
        activeGateSmoke1 = null;
        activeGateSmoke2 = null;
    }

    public void OpenGate()
    {
        if (machineGunReturnGateLocked) return;
        if (state == GateState.Broken) return;
        if (state == GateState.Open || state == GateState.Opening) return;

        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(OpenAnimation());
    }

    public void BeginOpenHold()
    {
        bool wasClosedByAllOwners = openHoldCount == 0;
        openHoldCount++;

        // O2 suction and player exit holds must visually open the gate even if a
        // previous machine-gun return sequence left its close lock behind.
        machineGunReturnGateLocked = false;
        OpenGate();

        if (wasClosedByAllOwners)
            OnOpenHoldStarted?.Invoke();
    }

    public void EndOpenHold()
    {
        if (openHoldCount <= 0)
            return;

        openHoldCount--;
        if (openHoldCount == 0)
            CloseGateInternal();
    }

    public void CloseGate()
    {
        if (openHoldCount > 0) return;
        CloseGateInternal();
    }

    private void CloseGateInternal()
    {
        if (state == GateState.Broken) return;
        if (state == GateState.Closed || state == GateState.Closing) return;

        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(CloseAnimation());
    }

    public void CloseGateOnBloodHit()
    {
        CloseGate();
    }

    public void SetMachineGunReturnGateLocked(bool locked)
    {
        machineGunReturnGateLocked = locked;

        if (locked && openHoldCount == 0)
            CloseGateInternal();
    }

    private IEnumerator OpenAnimation()
    {
        state = GateState.Opening;
        SetVisualPhase(GateVisualPhase.Closed);
        yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(0.1f);
        SetVisualPhase(GateVisualPhase.HalfOpen);
        yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(0.1f);
        SetVisualPhase(GateVisualPhase.Open);

        state = GateState.Open;
        animCo = null;
    }

    private IEnumerator CloseAnimation()
    {
        state = GateState.Closing;
        SetVisualPhase(GateVisualPhase.Open);
        yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(0.1f);
        SetVisualPhase(GateVisualPhase.HalfOpen);
        yield return RageTransformFreezeController.WaitForSecondsRespectingGameplayPause(0.1f);
        SetVisualPhase(GateVisualPhase.Closed);

        state = GateState.Closed;
        animCo = null;
    }

    public void ResetGate()
    {
        if (animCo != null) StopCoroutine(animCo);
        animCo = null;

        ResetGateSmokes();

        hitCount = 0;
        openHoldCount = 0;
        machineGunReturnGateLocked = false;
        state = GateState.Closed;
        visualPhase = GateVisualPhase.Closed;
        ApplyCurrentVisualSprite();
    }

    private void ApplyCurrentVisualSprite()
    {
        if (sr == null)
            return;

        int idx = GetSpriteIndex();

        switch (visualPhase)
        {
            case GateVisualPhase.Open:
                sr.sprite = GetSprite(openSprites, idx, sr.sprite);
                break;
            case GateVisualPhase.Closed:
                sr.sprite = GetSprite(closedSprites, idx, sr.sprite);
                break;
            default:
                sr.sprite = GetSprite(halfOpenSprites, idx, sr.sprite);
                break;
        }
    }

    private void SetVisualPhase(GateVisualPhase phase)
    {
        visualPhase = phase;
        ApplyCurrentVisualSprite();
    }

    private int GetSpriteIndex()
    {
        int maxLen = Mathf.Max(closedSprites != null ? closedSprites.Length : 0,
                               halfOpenSprites != null ? halfOpenSprites.Length : 0,
                               openSprites != null ? openSprites.Length : 0);
        if (maxLen <= 0) return 0;
        return Mathf.Clamp(hitCount, 0, maxLen - 1);
    }

    private static Sprite GetSprite(Sprite[] sprites, int idx, Sprite fallback)
    {
        if (sprites == null || sprites.Length == 0)
            return fallback;

        return sprites[Mathf.Clamp(idx, 0, sprites.Length - 1)];
    }

    private void SpawnGateDamageFx()
    {
        if (gateDamageFxPrefab == null)
            return;

        Vector3 spawnPos = transform.TransformPoint(gateDamageFxLocalOffset);
        GameObject fx = null;

        if (ObjectPool.Instance != null && !string.IsNullOrEmpty(gateDamageFxPoolTag))
        {
            if (!ObjectPool.Instance.HasPool(gateDamageFxPoolTag))
                ObjectPool.Instance.RegisterPool(gateDamageFxPoolTag, gateDamageFxPrefab, Mathf.Max(1, gateDamageFxPoolSize));
            else
                ObjectPool.Instance.EnsurePoolSize(gateDamageFxPoolTag, gateDamageFxPrefab, Mathf.Max(1, gateDamageFxPoolSize));

            if (ObjectPool.Instance.HasPool(gateDamageFxPoolTag))
                fx = ObjectPool.Instance.SpawnFromPool(gateDamageFxPoolTag, spawnPos, Quaternion.identity);
        }

        if (fx == null)
            fx = Instantiate(gateDamageFxPrefab, spawnPos, Quaternion.identity);

        if (fx == null)
            return;

        var autoReturn = fx.GetComponent<AutoReturnToPool>();
        if (autoReturn == null)
            autoReturn = fx.AddComponent<AutoReturnToPool>();

        bool hasPool = ObjectPool.Instance != null
            && !string.IsNullOrEmpty(gateDamageFxPoolTag)
            && ObjectPool.Instance.HasPool(gateDamageFxPoolTag);

        autoReturn.usePool = hasPool;
        autoReturn.poolTag = gateDamageFxPoolTag;
    }

    private void EnsureDamageFxPoolReady()
    {
        if (gateDamageFxPrefab == null || ObjectPool.Instance == null || string.IsNullOrEmpty(gateDamageFxPoolTag))
            return;

        if (!ObjectPool.Instance.HasPool(gateDamageFxPoolTag))
            ObjectPool.Instance.RegisterPool(gateDamageFxPoolTag, gateDamageFxPrefab, Mathf.Max(1, gateDamageFxPoolSize));
        else
            ObjectPool.Instance.EnsurePoolSize(gateDamageFxPoolTag, gateDamageFxPrefab, Mathf.Max(1, gateDamageFxPoolSize));
    }

    // (선택) 보스 타임아웃 자폭에서 문을 즉시 부수는 호출용
    public void ForceBreakByBoss()
    {
        if (state == GateState.Broken) return;
        hitCount = maxHits;
        state = GateState.Broken;
        if (sr != null)
            sr.sprite = brokenSprite;
        GameData.Instance.TriggerGameOver();
    }
}
