using UnityEngine;

public class PhaseCache : MonoBehaviour
{
    public Rigidbody2D[] rbs;
    public Mover mover;
    public bool hasChildMovers;

    void Awake()
    {
        RefreshCache();
    }

    public void RefreshCache()
    {
        rbs = GetComponentsInChildren<Rigidbody2D>(true);
        mover = GetComponent<Mover>();

        hasChildMovers = false;
        var movers = GetComponentsInChildren<Mover>(true);
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
