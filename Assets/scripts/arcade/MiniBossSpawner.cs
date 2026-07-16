using UnityEngine;

[DisallowMultipleComponent]
public class MiniBossSpawner : MonoBehaviour, IReinitializable
{
    public MiniBoss miniBossPrefab;

    private MiniBoss spawnedMiniBoss;

    public MiniBoss SpawnIfNeeded()
    {
        if (spawnedMiniBoss != null)
            return spawnedMiniBoss;

        if (miniBossPrefab == null)
            return null;

        spawnedMiniBoss = Instantiate(miniBossPrefab, transform.position, miniBossPrefab.transform.rotation);
        return spawnedMiniBoss;
    }

    public void Reinit()
    {
        if (spawnedMiniBoss == null)
            return;

        if (!spawnedMiniBoss.gameObject)
            spawnedMiniBoss = null;
    }
}
