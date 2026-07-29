using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntranceManagement : MonoBehaviour
{
    public static EntranceManagement Instance { get; private set; }

    private class WayAllocation
    {
        public Way way;
        public float score;
        public int remaining;
        public float exactShare;
        public float fractionalShare;
        public bool usesFallbackScore;
    }

    [Header("References")]
    [SerializeField] private CustomerBloodManagement customerBloodManagement;
    [SerializeField] private List<Way> ways = new List<Way>();

    [Header("Spawn Timing")]
    [SerializeField] private float spawnInterval = 1f;
    [SerializeField] private bool useFallbackTrafficWhenScoreZero = true;
    [SerializeField] private float fallbackScorePerWay = 0.1f;
    [SerializeField] private float respawnCooldownMin = 3f;
    [SerializeField] private float respawnCooldownMax = 7f;

    private readonly List<WayAllocation> activeAllocations = new List<WayAllocation>();
    private readonly List<CustomerBlood> activeCustomers = new List<CustomerBlood>();
    private readonly Dictionary<int, Queue<CustomerBlood>> pooledCustomers = new Dictionary<int, Queue<CustomerBlood>>();
    private Coroutine spawnRoutine;
    private int readySpawnTokens;
    private int cooldownTokens;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (customerBloodManagement == null)
            customerBloodManagement = FindFirstObjectByType<CustomerBloodManagement>();

        readySpawnTokens = customerBloodManagement != null ? customerBloodManagement.GetMaxActiveCustomerCount() : 0;
        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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

    public void RecycleCustomer(CustomerBlood customer)
    {
        if (customer == null)
            return;

        NotifyCustomerDespawned(customer);

        CustomerBlood prefabSource = customer.SourcePrefab;
        if (prefabSource == null)
        {
            Destroy(customer.gameObject);
            return;
        }

        int key = prefabSource.GetInstanceID();
        if (!pooledCustomers.TryGetValue(key, out Queue<CustomerBlood> pool))
        {
            pool = new Queue<CustomerBlood>();
            pooledCustomers.Add(key, pool);
        }

        customer.gameObject.SetActive(false);
        pool.Enqueue(customer);
    }

    public void NotifyBuildingTrafficChanged()
    {
        activeAllocations.Clear();
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

            WayAllocation allocation = ChooseAllocationWithRemaining();
            if (allocation == null)
                continue;

            if (!customerBloodManagement.TryGetSpawnPrefab(out CustomerBloodManagement.BloodEntry entry))
                continue;

            if (allocation.way == null ||
                !allocation.way.TryGetSpawnEntrance(out Entrance chosenEntrance) ||
                !TryChoosePath(allocation, out Path chosenPath))
            {
                allocation.remaining = 0;
                continue;
            }

            CustomerBlood customer = GetPooledCustomer(entry.prefab);
            string resolvedEntryId = customerBloodManagement.GetResolvedEntryId(entry.prefab);
            int routeSequenceIndex = allocation.way != null ? allocation.way.GetRandomRouteSequenceIndex() : int.MinValue;
            customer.InitializeSpawn(resolvedEntryId, this, chosenEntrance, allocation.way, chosenPath, entry.prefab, routeSequenceIndex);

            activeCustomers.Add(customer);
            customerBloodManagement.RegisterSpawn(resolvedEntryId);
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
        for (int i = 0; i < ways.Count; i++)
        {
            Way way = ways[i];
            if (way == null)
                continue;

            WayAllocation allocation = new WayAllocation
            {
                way = way,
                score = Mathf.Max(0f, way.GetTrafficScore()),
                usesFallbackScore = false
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
                activeAllocations[i].score = Mathf.Max(0f, fallbackScorePerWay);
                activeAllocations[i].usesFallbackScore = true;
                totalScore += activeAllocations[i].score;
            }

            if (totalScore <= 0f)
                return;
        }

        int assigned = 0;
        List<WayAllocation> positiveScores = new List<WayAllocation>();
        for (int i = 0; i < activeAllocations.Count; i++)
        {
            WayAllocation allocation = activeAllocations[i];
            if (allocation.score <= 0f)
                continue;

            allocation.exactShare = (allocation.score / totalScore) * totalAvailable;
            allocation.remaining = Mathf.FloorToInt(allocation.exactShare);
            allocation.fractionalShare = allocation.exactShare - allocation.remaining;
            positiveScores.Add(allocation);
            assigned += allocation.remaining;
        }

        if (positiveScores.Count == 1 && totalAvailable > 0)
        {
            positiveScores[0].remaining = totalAvailable;
            return;
        }

        int remainder = Mathf.Max(0, totalAvailable - assigned);
        while (remainder > 0 && positiveScores.Count > 0)
        {
            WayAllocation bonusAllocation = ChooseAllocationByFractionalShare(positiveScores);
            if (bonusAllocation == null)
                bonusAllocation = positiveScores[Random.Range(0, positiveScores.Count)];

            bonusAllocation.remaining++;
            bonusAllocation.fractionalShare = 0f;
            remainder--;
        }
    }

    private static WayAllocation ChooseAllocationByFractionalShare(List<WayAllocation> allocations)
    {
        if (allocations == null || allocations.Count == 0)
            return null;

        float totalFraction = 0f;
        for (int i = 0; i < allocations.Count; i++)
            totalFraction += Mathf.Max(0f, allocations[i].fractionalShare);

        if (totalFraction <= 0f)
            return null;

        float pick = Random.Range(0f, totalFraction);
        float cumulative = 0f;
        for (int i = 0; i < allocations.Count; i++)
        {
            cumulative += Mathf.Max(0f, allocations[i].fractionalShare);
            if (pick <= cumulative)
                return allocations[i];
        }

        return allocations[allocations.Count - 1];
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

    private WayAllocation ChooseAllocationWithRemaining()
    {
        List<WayAllocation> choices = new List<WayAllocation>();
        int total = 0;

        for (int i = 0; i < activeAllocations.Count; i++)
        {
            WayAllocation allocation = activeAllocations[i];
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

    private static bool TryChoosePath(WayAllocation allocation, out Path path)
    {
        if (allocation == null || allocation.way == null)
        {
            path = null;
            return false;
        }

        if (allocation.usesFallbackScore)
            return allocation.way.TryGetAnyPath(out path);

        return allocation.way.TryGetActivePath(out path);
    }

    private CustomerBlood GetPooledCustomer(CustomerBlood prefab)
    {
        if (prefab == null)
            return null;

        int key = prefab.GetInstanceID();
        if (!pooledCustomers.TryGetValue(key, out Queue<CustomerBlood> pool))
        {
            pool = new Queue<CustomerBlood>();
            pooledCustomers.Add(key, pool);
        }

        CustomerBlood customer = null;
        while (pool.Count > 0 && customer == null)
            customer = pool.Dequeue();

        if (customer == null)
            customer = Instantiate(prefab);

        customer.gameObject.SetActive(true);
        return customer;
    }
}
