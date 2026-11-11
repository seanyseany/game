#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class OxygenProductionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private VillageManagement village;
    private VillageManagement previousInstance;
    private float previousTimeScale;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        previousTimeScale = Time.timeScale;
        Time.timeScale = 5f;
        previousInstance = VillageManagement.Instance;
        var root = new GameObject("Oxygen test village");
        root.SetActive(false);
        village = root.AddComponent<VillageManagement>();
        Set(village, "autoSaveOnChange", false);
        typeof(VillageManagement).GetProperty("Instance").SetValue(null, village);
        village.SaveData.currentOxygen = 0;
        village.SaveData.oxygenCapacity = 10000;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Time.timeScale = previousTimeScale;
        foreach (Oxygen oil in Object.FindObjectsByType<Oxygen>(FindObjectsSortMode.None))
            Object.DestroyImmediate(oil.gameObject);
        typeof(VillageManagement).GetProperty("Instance").SetValue(null, previousInstance);
        if (village != null) Object.DestroyImmediate(village.gameObject);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator FourOilsCollectedFiveSecondsApartEachWaitTwentyThreeSeconds()
    {
        var oils = new Oxygen[4];
        var collectedAt = new float[4];
        for (int i = 0; i < oils.Length; i++)
            oils[i] = CreateOil(23f);
        yield return null; // Let Start establish the original production cycles.
        yield return new WaitForSeconds(7f);

        for (int i = 0; i < oils.Length; i++)
        {
            if (i > 0) yield return WaitUntilTime(collectedAt[i - 1] + 5f);
            Set(oils[i], "storedOxygen", 20);
            collectedAt[i] = Time.time;
            oils[i].CollectStoredOxygen();
            Assert.AreEqual(0, oils[i].StoredOxygen);
        }
        Assert.AreEqual(80, village.CurrentOxygen);

        for (int i = 0; i < oils.Length; i++)
        {
            yield return WaitUntilTime(collectedAt[i] + 22.5f);
            Assert.AreEqual(0, oils[i].StoredOxygen, "Oil " + i + " produced before its own 23-second interval.");
            yield return WaitUntilTime(collectedAt[i] + 23.5f);
            Assert.AreEqual(20, oils[i].StoredOxygen, "Oil " + i + " must use its own collection time.");
            for (int later = i + 1; later < oils.Length; later++)
                Assert.AreEqual(0, oils[later].StoredOxygen, "Collecting an earlier oil must not advance another oil.");
        }
    }

    [UnityTest]
    public IEnumerator CollectionUsesEachInspectorIntervalAndEmptyCollectionDoesNotDelayProduction()
    {
        Oxygen faster = CreateOil(18f);
        Oxygen slower = CreateOil(23f);
        yield return null;
        yield return new WaitForSeconds(7f);
        Set(faster, "storedOxygen", 20);
        Set(slower, "storedOxygen", 20);
        float collectedAt = Time.time;
        faster.CollectStoredOxygen();
        slower.CollectStoredOxygen();

        yield return WaitUntilTime(collectedAt + 17.5f);
        Assert.AreEqual(0, faster.StoredOxygen);
        Assert.AreEqual(0, slower.StoredOxygen);
        faster.CollectStoredOxygen(); // An empty click must leave the pending cycle alone.
        yield return WaitUntilTime(collectedAt + 18.5f);
        Assert.AreEqual(20, faster.StoredOxygen);
        Assert.AreEqual(0, slower.StoredOxygen);
        yield return WaitUntilTime(collectedAt + 23.5f);
        Assert.AreEqual(20, slower.StoredOxygen);

        // Subsequent collections must also discard the previous cycle's remaining wait.
        float recollectedAt = Time.time;
        faster.CollectStoredOxygen();
        yield return WaitUntilTime(recollectedAt + 17.5f);
        Assert.AreEqual(0, faster.StoredOxygen);
        yield return WaitUntilTime(recollectedAt + 18.5f);
        Assert.AreEqual(20, faster.StoredOxygen);
    }

    private static Oxygen CreateOil(float interval)
    {
        var root = new GameObject("Oil " + interval);
        root.SetActive(false);
        Oxygen oil = root.AddComponent<Oxygen>();
        var settings = new SerializedObject(oil);
        settings.FindProperty("productionInterval").floatValue = interval;
        settings.FindProperty("energyUsage").intValue = 0;
        settings.FindProperty("energyConsumptionInterval").floatValue = 0f;
        settings.FindProperty("oxygenProduction").intValue = 20;
        settings.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(true);
        return oil;
    }

    private static IEnumerator WaitUntilTime(float deadline)
    {
        while (Time.time < deadline)
            yield return null;
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
}
#endif
