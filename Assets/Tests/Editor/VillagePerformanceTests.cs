#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class VillagePerformanceTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator RepeatedVillageTargetAndUnchangedUiChecksDoNotAllocate()
    {
        yield return new EnterPlayMode();
        MeasureHotPaths();
        yield return null;
    }

    private static void MeasureHotPaths()
    {
        VillageManagement village = PrepareVillage();
        Turret turret = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/TURRET1.prefab"))
            .GetComponent<Turret>();
        turret.enabled = false;
        turret.transform.position = Vector3.zero;
        for (int i = 0; i < 10; i++)
            CreateVillan("Candidate" + i, new Vector3(i * 0.25f, 0f, 0f));
        var findTarget = Bind<Func<Villan>>(turret, "FindClosestTargetInRange");
        var launchers = Bind<Func<List<int>>>(turret, "GetActiveLauncherIndices");

        GameObject oxygenRoot = new GameObject("OxygenSource");
        oxygenRoot.SetActive(false);
        Oxygen oxygen = oxygenRoot.AddComponent<Oxygen>();
        Set(oxygen, "storedOxygen", 42);
        var icon = new GameObject("OxygenIcon", typeof(CircleCollider2D)).AddComponent<O2Icon>();
        icon.enabled = false;
        TMP_Text amount = new GameObject("Amount", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        Set(icon, "amountText", amount);
        icon.Bind(oxygen);
        var refreshIcon = Bind<Action>(icon, "Refresh");

        var bankUi = new GameObject("BankUi").AddComponent<BankUI>();
        bankUi.enabled = false;
        TMP_Text bankText = new GameObject("BankPrice", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        Set(bankUi, "upgradeText", bankText);
        var refreshBank = Bind<Action>(bankUi, "Refresh");

        var turretUi = new GameObject("TurretUi", typeof(RectTransform)).AddComponent<TurretUI>();
        turretUi.enabled = false;
        Set(turret, "ammoCapacity", 100);
        Set(turret, "ammoCurrent", 50);
        turretUi.Open(null, turret);

        var allocationCounts = new List<long>();
        string report = Measure("turret target acquisition, 10 candidates", () => findTarget(), out long count); allocationCounts.Add(count);
        report += "\n" + Measure("turret launcher enumeration", () => launchers(), out count); allocationCounts.Add(count);
        report += "\n" + Measure("unchanged oxygen label", refreshIcon, out count); allocationCounts.Add(count);
        report += "\n" + Measure("unchanged bank price", refreshBank, out count); allocationCounts.Add(count);
        report += "\n" + Measure("unchanged turret management labels", turretUi.Refresh, out count); allocationCounts.Add(count);
        UnityEngine.Debug.Log("[VILLAGE PERFORMANCE]\n" + report);
        File.WriteAllText("/private/tmp/village-performance-measurements.txt", report);
        Assert.AreEqual("42", amount.text);
        foreach (long allocation in allocationCounts)
            Assert.AreEqual(0, allocation, "Repeated target checks and unchanged UI values must reuse their buffers and labels");
    }

    private static string Measure(string label, Action action, out long allocationCount)
    {
        for (int i = 0; i < 32; i++) action();
        var timer = new Stopwatch();
        using var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 65536,
            ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        if (!allocations.Valid) throw new InvalidOperationException("GC.Alloc recorder is unavailable");
        timer.Start();
        for (int i = 0; i < 2000; i++) action();
        timer.Stop();
        allocations.Stop();
        allocationCount = 0;
        for (int i = 0; i < Math.Min(allocations.Count, allocations.Capacity); i++) allocationCount += allocations.GetSample(i).Count;
        return label + ": calls=2000, allocations=" + allocationCount + ", ms=" + timer.Elapsed.TotalMilliseconds.ToString("F3");
    }

    [UnityTest] public IEnumerator TargetingTracksDisablePoolingReactivationAndDestruction()
    {
        yield return new EnterPlayMode();
        Turret turret = CreateTurret();
        var range = new GameObject("TargetRange");
        turret.ConfigureTargetRange(range.transform, new Vector2(-10f, -10f), new Vector2(10f, 10f));
        var pick = Bind<Func<Villan>>(turret, "FindClosestTargetInRange");
        Villan first = CreateVillan("NearestToRangeStart", new Vector3(-8f, -8f, 0f));
        Villan second = CreateVillan("OtherTarget", Vector3.zero);
        Villan outside = CreateVillan("OutsideRange", Vector3.right * 20f);
        Assert.AreSame(first, pick());
        first.enabled = false;
        Assert.AreSame(second, pick());
        first.enabled = true;
        Assert.AreSame(first, pick());
        first.gameObject.SetActive(false);
        Assert.AreSame(second, pick());
        first.gameObject.SetActive(true);
        Assert.AreSame(first, pick());
        Object.DestroyImmediate(first.gameObject);
        Assert.AreSame(second, pick());
        Object.DestroyImmediate(second.gameObject);
        Assert.IsNull(pick());

        Villan template = CreateVillan("PooledTemplate", Vector3.right * 30f);
        Villan pooled = Villan.Spawn(template, null, 1);
        pooled.transform.position = new Vector3(-8f, -8f, 0f);
        Assert.AreSame(pooled, pick());
        Bind<Action>(pooled, "ReturnToPool")();
        Assert.IsNull(pick());
        Assert.AreSame(pooled, Villan.Spawn(template, null, 1));
        Assert.AreSame(pooled, pick());
        Object.DestroyImmediate(pooled.gameObject);
        outside.transform.position = Vector3.zero;
        Assert.AreSame(outside, pick());
    }

    [UnityTest] public IEnumerator LauncherAvailabilityPreservesIndicesWhenReferencesChange()
    {
        yield return new EnterPlayMode();
        Turret turret = CreateTurret();
        var enumerate = Bind<Func<List<int>>>(turret, "GetActiveLauncherIndices");
        var hasSpawnPoint = Bind<Func<bool>>(turret, "HasSpawnPoint");
        var entries = (IList)typeof(Turret).GetField("launchers", Private).GetValue(turret);
        var spawnFields = new List<FieldInfo>();
        var points = new List<Transform>();
        foreach (object entry in entries)
        {
            FieldInfo field = entry.GetType().GetField("bulletSpawnPoint");
            spawnFields.Add(field);
            points.Add((Transform)field.GetValue(entry));
        }
        Assert.IsTrue(hasSpawnPoint());
        List<int> indices = enumerate();
        int originalCount = indices.Count;
        Assert.Greater(originalCount, 0);
        Assert.IsTrue(hasSpawnPoint());
        Assert.AreEqual(originalCount, indices.Count, "Availability checks must not invalidate an in-progress firing pass");
        for (int i = 0; i < entries.Count; i++) spawnFields[i].SetValue(entries[i], null);
        Assert.IsFalse(hasSpawnPoint());
        Assert.IsEmpty(enumerate());
        for (int i = 0; i < entries.Count; i++) spawnFields[i].SetValue(entries[i], points[i]);
        Assert.IsTrue(hasSpawnPoint());
        Assert.AreEqual(originalCount, enumerate().Count);
    }

    [UnityTest] public IEnumerator CachedLabelsStillFollowProductionAmmoPricesAffordabilityAndReopening()
    {
        yield return new EnterPlayMode();
        VillageManagement village = PrepareVillage();
        var source = new GameObject("OxygenSource");
        source.SetActive(false);
        Oxygen oxygen = source.AddComponent<Oxygen>();
        Set(oxygen, "storedOxygen", 12);
        var icon = new GameObject("OxygenIcon", typeof(CircleCollider2D)).AddComponent<O2Icon>();
        icon.enabled = false;
        TMP_Text amount = CreateText("Amount");
        Set(icon, "amountText", amount);
        icon.Bind(oxygen);
        Assert.AreEqual("12", amount.text);
        Set(oxygen, "storedOxygen", 24);
        Bind<Action>(icon, "Refresh")();
        Assert.AreEqual("24", amount.text);
        TMP_Text replacement = CreateText("ReplacementAmount");
        Set(icon, "amountText", replacement);
        Bind<Action>(icon, "Refresh")();
        Assert.AreEqual("24", replacement.text);

        var bankRoot = new GameObject("Bank", typeof(BoxCollider2D));
        bankRoot.SetActive(false);
        Bank bank = bankRoot.AddComponent<Bank>();
        var bankUi = new GameObject("BankUI").AddComponent<BankUI>();
        bankUi.enabled = false;
        TMP_Text bankPrice = CreateText("BankPrice");
        Set(bankUi, "upgradeText", bankPrice);
        bankUi.Open(bank);
        Assert.AreEqual("Upgrade O2 " + bank.GetUpgradePriceForLevel(2), bankPrice.text);
        village.SaveData.bankLevel = 2;
        Bind<Action>(bankUi, "Refresh")();
        Assert.AreEqual("Upgrade O2 " + bank.GetUpgradePriceForLevel(3), bankPrice.text);
        village.SaveData.bankLevel = 3;
        Bind<Action>(bankUi, "Refresh")();
        Assert.IsEmpty(bankPrice.text);

        Turret turret = CreateTurret();
        Set(turret, "ammoCapacity", 100);
        Set(turret, "ammoCurrent", 50);
        var ui = new GameObject("TurretUI", typeof(RectTransform)).AddComponent<TurretUI>();
        ui.enabled = false;
        ui.Open(null, turret);
        TMP_Text ammoPrice = (TMP_Text)typeof(TurretUI).GetField("ammo100Text", Private).GetValue(ui);
        Assert.AreEqual("O2 " + turret.GetFullAmmoRefillPrice(), ammoPrice.text);
        Set(turret, "ammoCurrent", 0);
        village.SaveData.currentOxygen = 0;
        ui.Refresh();
        Assert.AreEqual("O2 " + turret.GetFullAmmoRefillPrice(), ammoPrice.text);
        var buy = (UnityEngine.UI.Button)typeof(TurretUI).GetField("ammo100Button", Private).GetValue(ui);
        Assert.IsFalse(buy.interactable);
        village.SaveData.currentOxygen = 100;
        ui.Refresh();
        Assert.IsTrue(buy.interactable, "Resource changes must update affordability even when the price is unchanged");
        Set(turret, "ammoCurrent", 100);
        ui.Refresh();
        Assert.AreEqual("O2 0", ammoPrice.text);
        Assert.IsFalse(buy.interactable);
        ui.Close();
        Set(turret, "ammoCurrent", 0);
        ui.Open(null, turret);
        Assert.AreEqual("O2 " + turret.GetFullAmmoRefillPrice(), ammoPrice.text);
    }

    private static Turret CreateTurret()
    {
        Turret turret = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/TURRET1.prefab"))
            .GetComponent<Turret>();
        turret.enabled = false;
        turret.transform.position = Vector3.zero;
        return turret;
    }

    private static TMP_Text CreateText(string name) =>
        new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();

    private static VillageManagement PrepareVillage()
    {
        var root = new GameObject("VillageTestData");
        root.SetActive(false);
        VillageManagement village = root.AddComponent<VillageManagement>();
        Set(village, "loaded", true);
        Set(village, "autoSaveOnChange", false);
        root.SetActive(true);
        village.SaveData.currentOxygen = 100;
        village.SaveData.oxygenCapacity = 100;
        return village;
    }

    private static Villan CreateVillan(string name, Vector3 position)
    {
        var root = new GameObject(name, typeof(Rigidbody2D), typeof(CircleCollider2D));
        root.transform.position = position;
        return root.AddComponent<Villan>();
    }

    private static T Bind<T>(object target, string method) where T : Delegate =>
        (T)target.GetType().GetMethod(method, Private).CreateDelegate(typeof(T), target);

    private static void Set(object target, string field, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo info = type.GetField(field, Private);
            if (info != null) { info.SetValue(target, value); return; }
        }
        throw new MissingFieldException(target.GetType().Name, field);
    }
}
#endif
