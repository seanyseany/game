using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class MiniBossTrigger : MonoBehaviour
{
    [SerializeField] private bool consumeOnGateContact = true;

    private bool consumed;
    private readonly System.Collections.Generic.List<MiniBossSpawner> resolvedSpawners = new System.Collections.Generic.List<MiniBossSpawner>(8);

    private void OnEnable()
    {
        consumed = false;
    }

    private void Reset()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHandleGateContact(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null)
            return;

        TryHandleGateContact(collision.collider);
    }

    private void TryHandleGateContact(Collider2D other)
    {
        if (consumed || other == null || !IsGateCollider(other))
            return;

        consumed = true;

        SpawnAllResolvedMiniBosses();

        if (consumeOnGateContact)
            gameObject.SetActive(false);
    }

    private static bool IsGateCollider(Collider2D other)
    {
        return other.GetComponent<GateHealth>() != null || other.GetComponentInParent<GateHealth>() != null;
    }

    private void SpawnAllResolvedMiniBosses()
    {
        resolvedSpawners.Clear();

        PhaseLayoutSnapshot phaseRoot = GetComponentInParent<PhaseLayoutSnapshot>(true);
        if (phaseRoot != null)
        {
            AddSpawners(phaseRoot.GetComponentsInChildren<MiniBossSpawner>(true));
        }

        AddSpawners(GetComponentsInParent<MiniBossSpawner>(true));
        AddSpawners(GetComponentsInChildren<MiniBossSpawner>(true));

        for (int i = 0; i < resolvedSpawners.Count; i++)
            resolvedSpawners[i]?.SpawnIfNeeded();
    }

    private void AddSpawners(MiniBossSpawner[] spawners)
    {
        if (spawners == null)
            return;

        for (int i = 0; i < spawners.Length; i++)
        {
            MiniBossSpawner spawner = spawners[i];
            if (spawner == null || resolvedSpawners.Contains(spawner))
                continue;

            resolvedSpawners.Add(spawner);
        }
    }
}
