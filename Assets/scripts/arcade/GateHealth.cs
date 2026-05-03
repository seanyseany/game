using UnityEngine;
using System.Collections;

public class GateHealth : MonoBehaviour
{
    public static GateHealth Instance;

    [Header("Sprites - 단계별(0,1,2)")]
    public Sprite[] closedSprites;
    public Sprite[] halfOpenSprites;
    public Sprite[] openSprites;
    public Sprite brokenSprite;

    [Header("Settings")]
    public int maxHits = 3; // 3회 맞으면 파괴

    [Header("Smoke Spawn Positions")]
    public Vector3 smokeFrontLocalOffset = Vector3.zero;
    public Vector3 smokeFrontLocalOffset2 = Vector3.zero;
    public Vector3 smokeBackLocalOffset  = Vector3.zero;

    [Header("Damage FX")]
    public GameObject gateDamageFxPrefab;
    public string gateDamageFxPoolTag = "GateDamageFx";
    public Vector3 gateDamageFxLocalOffset = Vector3.zero;
    [Min(0)] public int gateDamageFxPoolSize = 4;

    private int hitCount = 0;
    private SpriteRenderer sr;
    private Coroutine animCo;

    private enum GateState { Closed, Opening, Open, Closing, Broken }
    private enum GateVisualPhase { Closed, HalfOpen, Open }
    private GateState state = GateState.Closed;
    private GateVisualPhase visualPhase = GateVisualPhase.Closed;
    private bool smokePlayed = false;

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
        SpawnGateDamageFx();

        if (hitCount == 1)
        {
            smokePlayed = true;
            if (ObjectPool.Instance != null && ObjectPool.Instance.HasPool("GateSmoke"))
                ObjectPool.Instance.SpawnFromPool("GateSmoke", transform.TransformPoint(smokeFrontLocalOffset), Quaternion.identity);
        }
        else if (hitCount == 2)
        {
            if (ObjectPool.Instance != null && ObjectPool.Instance.HasPool("GateSmokeBack"))
                ObjectPool.Instance.SpawnFromPool("GateSmokeBack", transform.TransformPoint(smokeBackLocalOffset), Quaternion.identity);
        }

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

    public void OpenGate()
    {
        if (state == GateState.Broken) return;
        if (state == GateState.Open || state == GateState.Opening) return;

        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(OpenAnimation());
    }

    public void CloseGateOnBloodHit()
    {
        if (state == GateState.Broken) return;
        if (state == GateState.Closed || state == GateState.Closing) return;

        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(CloseAnimation());
    }

    private IEnumerator OpenAnimation()
    {
        state = GateState.Opening;
        SetVisualPhase(GateVisualPhase.Closed);
        yield return new WaitForSeconds(0.1f);
        SetVisualPhase(GateVisualPhase.HalfOpen);
        yield return new WaitForSeconds(0.1f);
        SetVisualPhase(GateVisualPhase.Open);

        state = GateState.Open;
        animCo = null;
    }

    private IEnumerator CloseAnimation()
    {
        state = GateState.Closing;
        SetVisualPhase(GateVisualPhase.Open);
        yield return new WaitForSeconds(0.1f);
        SetVisualPhase(GateVisualPhase.HalfOpen);
        yield return new WaitForSeconds(0.1f);
        SetVisualPhase(GateVisualPhase.Closed);

        state = GateState.Closed;
        animCo = null;
    }

    public void ResetGate()
    {
        if (animCo != null) StopCoroutine(animCo);
        animCo = null;

        hitCount = 0;
        state = GateState.Closed;
        visualPhase = GateVisualPhase.Closed;
        ApplyCurrentVisualSprite();
        smokePlayed = false;
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
        if (autoReturn != null)
        {
            autoReturn.usePool = !string.IsNullOrEmpty(gateDamageFxPoolTag);
            autoReturn.poolTag = gateDamageFxPoolTag;
        }
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
