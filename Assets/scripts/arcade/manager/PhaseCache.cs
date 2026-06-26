using UnityEngine;

public class PhaseCache : MonoBehaviour
{
    public Mover[] movers;
    public Rigidbody2D[] rbs;
    public Renderer[] renderers;
    public SpriteRenderer[] spriteRenderers;
    public Mover mover;
    public PhaseEndTrigger phaseEndTrigger;
    public bool hasChildMovers;

    private Sprite[] initialSprites;
    private Color[] initialColors;

    void Awake()
    {
        RefreshCache();
    }

    public void RefreshCache()
    {
        movers = GetComponentsInChildren<Mover>(true);
        rbs = GetComponentsInChildren<Rigidbody2D>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        mover = GetComponent<Mover>();
        phaseEndTrigger = GetComponentInChildren<PhaseEndTrigger>(true);

        if (spriteRenderers != null)
        {
            initialSprites = new Sprite[spriteRenderers.Length];
            initialColors = new Color[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                SpriteRenderer sr = spriteRenderers[i];
                if (sr == null)
                    continue;

                initialSprites[i] = sr.sprite;
                initialColors[i] = sr.color;
            }
        }

        hasChildMovers = false;
        for (int i = 0; i < movers.Length; i++)
        {
            var m = movers[i];
            if (m != null && m.transform != transform)
            {
                hasChildMovers = true;
                break;
            }
        }
    }

    public void ResetCached()
    {
        if (movers != null)
        {
            for (int i = 0; i < movers.Length; i++)
            {
                Mover cachedMover = movers[i];
                if (cachedMover == null)
                    continue;

                if (Mathf.Approximately(cachedMover.defaultBaseSpeed, 0f) && !Mathf.Approximately(cachedMover.baseSpeed, 0f))
                    cachedMover.defaultBaseSpeed = cachedMover.baseSpeed;

                cachedMover.applyStageSpeedMultiplier = true;
                cachedMover.baseSpeed = cachedMover.defaultBaseSpeed;
            }
        }

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                    renderer.enabled = true;
            }
        }

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                SpriteRenderer sr = spriteRenderers[i];
                if (sr == null)
                    continue;

                sr.enabled = true;

                if (initialSprites != null && i < initialSprites.Length && initialSprites[i] != null)
                    sr.sprite = initialSprites[i];

                if (initialColors != null && i < initialColors.Length)
                    sr.color = initialColors[i];
            }
        }

        // Rigidbody2D 복구
        foreach (var rb in rbs)
        {
            if (!rb) continue;
            if (!rb.gameObject.activeInHierarchy) continue;
            rb.simulated = true;
            if (rb.bodyType != RigidbodyType2D.Static)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.WakeUp();
            }
        }

    }

    public void SetActiveChildren(bool active)
    {
        Transform[] ts = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < ts.Length; i++)
        {
            if (ts[i] == null || ts[i] == transform) continue;
            ts[i].gameObject.SetActive(active);
        }
    }
}
