using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntranceManagement : MonoBehaviour
{
    [System.Serializable]
    public class EntranceBinding
    {
        public Entrance entrance;
        public List<Path> connectedPaths = new List<Path>();
    }

    private class EntranceAllocation
    {
        public EntranceBinding binding;
        public float score;
        public int remaining;
        public float exactShare;
    }

    [Header("References")]
    [SerializeField] private CustomerBloodManagement customerBloodManagement;
    [SerializeField] private List<EntranceBinding> entranceBindings = new List<EntranceBinding>();

    [Header("Spawn Timing")]
    [SerializeField] private float spawnInterval = 1f;
    [SerializeField] private bool useFallbackTrafficWhenScoreZero = true;
    [SerializeField] private float fallbackScorePerEntrance = 0.1f;
    [SerializeField] private float respawnCooldownMin = 3f;
    [SerializeField] private float respawnCooldownMax = 7f;

    private readonly List<EntranceAllocation> activeAllocations = new List<EntranceAllocation>();
    private readonly List<CustomerBlood> activeCustomers = new List<CustomerBlood>();
    private Coroutine spawnRoutine;
    private int readySpawnTokens;
    private int cooldownTokens;

    private void Start()
    {
        if (customerBloodManagement == null)
            customerBloodManagement = FindFirstObjectByType<CustomerBloodManagement>();

        readySpawnTokens = customerBloodManagement != null ? customerBloodManagement.GetMaxActiveCustomerCount() : 0;
        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    public void NotifyCustomerDespawned(CustomerBlood customer)
    {
        if (customer == null)
            return;

        activeCustomers.Remove(customer);

        if (customerBloodManagement != null)
            customerBloodManagement.RegisterDespawn(customer.SpawnEntryId);

        activeAllocations.Clear();
        cooldownTokens++;
        StartCoroutine(ReturnSpawnTokenAfterCooldown());
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);

            if (customerBloodManagement == null)
                continue;

            int maxActive = customerBloodManagement.GetMaxActiveCustomerCount();
            SyncSpawnTokensToCapacity(maxActive);
            if (maxActive <= 0 || readySpawnTokens <= 0 || activeCustomers.Count >= maxActive)
                continue;

            if (!HasRemainingAllocation())
                RecalculateAllocations();

            EntranceAllocation allocation = ChooseAllocationWithRemaining();
            if (allocation == null)
                continue;

            if (!customerBloodManagement.TryGetSpawnPrefab(out CustomerBloodManagement.BloodEntry entry))
                continue;

            Path chosenPath = ChoosePathForEntrance(allocation.binding);
            if (chosenPath == null || allocation.binding.entrance == null)
            {
                allocation.remaining = 0;
                continue;
            }

            CustomerBlood customer = Instantiate(entry.prefab);
            customer.InitializeSpawn(entry.id, this, allocation.binding.entrance, chosenPath);

            activeCustomers.Add(customer);
            customerBloodManagement.RegisterSpawn(entry.id);
            readySpawnTokens = Mathf.Max(0, readySpawnTokens - 1);
            allocation.remaining = Mathf.Max(0, allocation.remaining - 1);
        }
    }

    private void RecalculateAllocations()
    {
        activeAllocations.Clear();

        int totalAvailable = Mathf.Max(0, readySpawnTokens);
        if (totalAvailable == 0)
            return;

        float totalScore = 0f;
        for (int i = 0; i < entranceBindings.Count; i++)
        {
            EntranceBinding binding = entranceBindings[i];
            if (binding == null || binding.entrance == null)
                continue;

            EntranceAllocation allocation = new EntranceAllocation
            {
                binding = binding,
                score = CalculateEntranceScore(binding)
            };

            activeAllocations.Add(allocation);
            totalScore += allocation.score;
        }

        if (activeAllocations.Count == 0)
            return;

        if (totalScore <= 0f)
        {
            if (!useFallbackTrafficWhenScoreZero)
                return;

            totalScore = 0f;
            for (int i = 0; i < activeAllocations.Count; i++)
            {
                activeAllocations[i].score = fallbackScorePerEntrance;
                totalScore += fallbackScorePerEntrance;
            }
        }

        int assigned = 0;
        List<EntranceAllocation> positiveScores = new List<EntranceAllocation>();
        for (int i = 0; i < activeAllocations.Count; i++)
        {
            EntranceAllocation allocation = activeAllocations[i];
            if (allocation.score <= 0f)
                continue;

            allocation.exactShare = (allocation.score / totalScore) * totalAvailable;
            allocation.remaining = Mathf.FloorToInt(allocation.exactShare);
            positiveScores.Add(allocation);
            assigned += allocation.remaining;
        }

        if (positiveScores.Count == 1 && totalAvailable > 0)
        {
            positiveScores[0].remaining = Mathf.Max(1, positiveScores[0].remaining);
        }
        else
        {
            for (int i = 0; i < positiveScores.Count && assigned < totalAvailable; i++)
            {
                EntranceAllocation allocation = positiveScores[i];
                if (allocation.remaining == 0)
                {
                    allocation.remaining = 1;
                    assigned++;
                }
            }
        }

        int remainder = Mathf.Max(0, totalAvailable - assigned);
        while (remainder > 0 && positiveScores.Count > 0)
        {
            EntranceAllocation bonusAllocation = positiveScores[Random.Range(0, positiveScores.Count)];
            bonusAllocation.remaining++;
            remainder--;
        }
    }

    private float CalculateEntranceScore(EntranceBinding binding)
    {
        if (binding == null || binding.connectedPaths == null)
            return 0f;

        float score = 0f;
        for (int i = 0; i < binding.connectedPaths.Count; i++)
        {
            Path path = binding.connectedPaths[i];
            if (path == null)
                continue;

            score += path.GetActivationScore();
        }

        return score;
    }

    private bool HasRemainingAllocation()
    {
        for (int i = 0; i < activeAllocations.Count; i++)
        {
            if (activeAllocations[i].remaining > 0)
                return true;
        }

        return false;
    }

    private EntranceAllocation ChooseAllocationWithRemaining()
    {
        List<EntranceAllocation> choices = new List<EntranceAllocation>();
        int total = 0;

        for (int i = 0; i < activeAllocations.Count; i++)
        {
            EntranceAllocation allocation = activeAllocations[i];
            if (allocation.remaining <= 0)
                continue;

            choices.Add(allocation);
            total += allocation.remaining;
        }

        if (choices.Count == 0 || total <= 0)
            return null;

        int pick = Random.Range(0, total);
        int cumulative = 0;
        for (int i = 0; i < choices.Count; i++)
        {
            cumulative += choices[i].remaining;
            if (pick < cumulative)
                return choices[i];
        }

        return choices[choices.Count - 1];
    }

    private Path ChoosePathForEntrance(EntranceBinding binding)
    {
        if (binding == null || binding.connectedPaths == null || binding.connectedPaths.Count == 0)
            return null;

        List<Path> valid = new List<Path>();
        float totalScore = 0f;
        for (int i = 0; i < binding.connectedPaths.Count; i++)
        {
            Path path = binding.connectedPaths[i];
            if (path != null)
            {
                valid.Add(path);
                totalScore += Mathf.Max(0.1f, path.GetActivationScore());
            }
        }

        if (valid.Count == 0)
            return null;

        float pick = Random.Range(0f, totalScore);
        float cumulative = 0f;
        for (int i = 0; i < valid.Count; i++)
        {
            cumulative += Mathf.Max(0.1f, valid[i].GetActivationScore());
            if (pick <= cumulative)
                return valid[i];
        }

        return valid[valid.Count - 1];
    }

    private IEnumerator ReturnSpawnTokenAfterCooldown()
    {
        float cooldown = Random.Range(respawnCooldownMin, respawnCooldownMax);
        yield return new WaitForSeconds(cooldown);

        int maxActive = customerBloodManagement != null ? customerBloodManagement.GetMaxActiveCustomerCount() : 0;
        cooldownTokens = Mathf.Max(0, cooldownTokens - 1);
        readySpawnTokens = Mathf.Min(maxActive, readySpawnTokens + 1);
        activeAllocations.Clear();
    }

    private void SyncSpawnTokensToCapacity(int maxActive)
    {
        int tracked = activeCustomers.Count + readySpawnTokens + cooldownTokens;
        if (tracked < maxActive)
            readySpawnTokens += maxActive - tracked;

        int maxReady = Mathf.Max(0, maxActive - activeCustomers.Count - cooldownTokens);
        readySpawnTokens = Mathf.Clamp(readySpawnTokens, 0, maxReady);
    }
}
