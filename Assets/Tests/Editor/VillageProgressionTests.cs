#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class VillageProgressionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new List<GameObject>();
    private VillageManagement village;
    private VillageManagement previousInstance;

    [SetUp]
    public void SetUp()
    {
        previousInstance = VillageManagement.Instance;
        GameObject root = Root("ProgressionTest");
        root.SetActive(false);
        village = root.AddComponent<VillageManagement>();
        Set(village, "autoSaveOnChange", false);
        typeof(VillageManagement).GetProperty("Instance").SetValue(null, village);
        village.SaveData.currentOxygen = 100;
        village.SaveData.currentEnergy = 30;
    }

    [TearDown]
    public void TearDown()
    {
        typeof(VillageManagement).GetProperty("Instance").SetValue(null, previousInstance);
        foreach (GameObject root in roots) if (root != null) Object.DestroyImmediate(root);
        roots.Clear();
    }

    [Test]
    public void TradeAwardsVillageScoreWithoutSpendingOrAwardingResources()
    {
        Building building = Root("Fruit").AddComponent<Building>();
        Set(building, "villageScorePerTrade", 17);
        building.AwardTradeVillageScore();
        Assert.AreEqual(17, village.VillageScore);
        Assert.AreEqual(100, village.CurrentOxygen);
        Assert.AreEqual(30, village.CurrentEnergy);
    }

    [Test]
    public void ExactThresholdResetsGaugeAndRaisesEmergency()
    {
        village.AddVillageScore(100);
        Assert.AreEqual(2, village.VillageLevel);
        Assert.AreEqual(0, village.VillageScore);
        Assert.AreEqual(2, village.EmergencyDifficulty);
        Assert.AreEqual(200, village.ScoreToNextVillageLevel);
    }

    [Test]
    public void OverflowCarriesAcrossMultipleLevelsAndBroadcastsFinalState()
    {
        int broadcasts = 0;
        village.SaveDataChanged += _ => broadcasts++;
        village.AddVillageScore(650);
        Assert.AreEqual(4, village.VillageLevel);
        Assert.AreEqual(50, village.VillageScore);
        Assert.AreEqual(4, village.EmergencyDifficulty);
        Assert.AreEqual(1, broadcasts);
    }

    [Test]
    public void InspectorRequirementsAreUsedAndInvalidThresholdCannotLoopForever()
    {
        var requirements = new VillageManagement.VillageLevelRequirements { level1To2 = 7, level2To3 = 0 };
        Set(village, "villageLevelRequirements", requirements);
        village.AddVillageScore(8);
        Assert.AreEqual(3, village.VillageLevel);
        Assert.AreEqual(0, village.VillageScore);
    }

    [Test]
    public void InspectorCanSetCurrentProgressAndImmediatelyRefreshScoreUi()
    {
        VillageScoreUI ui = Root("ScoreUI").AddComponent<VillageScoreUI>();
        TMP_Text level = Root("Level", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        TMP_Text score = Root("Points", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        Set(ui, "levelText", level);
        Set(ui, "scoreText", score);
        Call(ui, "Bind", village);
        village.SetVillageProgress(4, 125);
        Assert.AreEqual(4, village.VillageLevel);
        Assert.AreEqual(125, village.VillageScore);
        Assert.AreEqual(4, village.EmergencyDifficulty);
        Assert.AreEqual("Village Lv. 4", level.text);
        Assert.AreEqual("125 / 400", score.text);
        village.SetVillageProgress(1, -10);
        Assert.AreEqual(0, village.VillageScore);
        Assert.AreEqual(1, village.EmergencyDifficulty);
        Assert.AreEqual("Village Lv. 1", level.text);
    }

    [Test]
    public void InspectorScoreOverflowAdvancesLevelsAndPreservesMaxLevelScore()
    {
        village.SetVillageProgress(2, 550);
        Assert.AreEqual(4, village.VillageLevel);
        Assert.AreEqual(50, village.VillageScore);
        village.SetVillageProgress(9, 5_000_000_000L);
        Assert.AreEqual(9, village.VillageLevel);
        Assert.AreEqual(5_000_000_000L, village.VillageScore);
    }

    [Test]
    public void InitialSettingsAreUsedForFreshProgressAndNormalizeExcessScore()
    {
        Set(village, "initialVillageLevel", 2);
        Set(village, "initialVillageScore", 250L);
        var initial = (VillageManagement.VillageSaveData)Call(village, "CreateInitialSaveData");
        Assert.AreEqual(3, initial.villageLevel);
        Assert.AreEqual(50, initial.villageScore);
        Assert.AreEqual(3, initial.emergencyDifficulty);
        Assert.AreEqual(1, village.VillageLevel, "Configuring a new save must leave the current save alone.");
    }

    [Test]
    public void InspectorSerializedPropertiesExposeEveryLevelTotalAndInitialValues()
    {
        var settings = new SerializedObject(village);
        Assert.IsNotNull(settings.FindProperty("initialVillageLevel"));
        Assert.IsNotNull(settings.FindProperty("initialVillageScore"));
        SerializedProperty requirements = settings.FindProperty("villageLevelRequirements");
        for (int level = 1; level < 9; level++)
            Assert.IsNotNull(requirements.FindPropertyRelative($"level{level}To{level + 1}"));
        requirements.FindPropertyRelative("level1To2").intValue = 15;
        settings.ApplyModifiedPropertiesWithoutUndo();
        village.AddVillageScore(15);
        Assert.AreEqual(2, village.VillageLevel);
        Assert.AreEqual(0, village.VillageScore);
    }

    [Test]
    public void MaxLevelKeepsAccumulatingAndDamageNeverLowersLevel()
    {
        village.AddVillageScore(3605);
        Assert.AreEqual(9, village.VillageLevel);
        Assert.AreEqual(5, village.VillageScore);
        Assert.AreEqual(0, village.ScoreToNextVillageLevel);
        village.AddVillageScore(int.MaxValue);
        village.AddVillageScore(int.MaxValue);
        Assert.AreEqual(4294967299L, village.VillageScore);
        village.RemoveVillageScore(int.MaxValue);
        Assert.AreEqual(2147483652L, village.VillageScore);
        Assert.AreEqual(9, village.VillageLevel);
    }

    [Test]
    public void DamageStopsAtZeroWithoutRevokingUnlocks()
    {
        village.AddVillageScore(303);
        village.RemoveVillageScore(20);
        Assert.AreEqual(0, village.VillageScore);
        Assert.AreEqual(3, village.VillageLevel);
        Assert.AreEqual(3, village.EmergencyDifficulty);
        village.AddVillageScore(-5);
        village.RemoveVillageScore(-5);
        Assert.AreEqual(0, village.VillageScore);
    }

    [Test]
    public void PlayerPurchaseRequiresVillageLevelAndFundsAndPersistsOwnership()
    {
        Assert.IsTrue(village.IsArcadePlayerAvailable(1));
        Assert.IsFalse(village.TryPurchasePlayer(2));
        village.AddVillageScore(100);
        Assert.IsFalse(village.IsArcadePlayerAvailable(2));
        village.SaveData.currentOxygen = 0;
        Assert.IsFalse(village.TryPurchasePlayer(2));
        village.SaveData.currentOxygen = 100;
        Assert.IsTrue(village.TryPurchasePlayer(2));
        Assert.IsTrue(village.IsArcadePlayerAvailable(2));
        Assert.AreEqual(90, village.CurrentOxygen);
        Assert.IsFalse(village.TryPurchasePlayer(2));
        var restored = JsonUtility.FromJson<VillageManagement.VillageSaveData>(JsonUtility.ToJson(village.SaveData));
        Assert.Contains(2, restored.purchasedPlayerTypes);
        Assert.AreEqual(2, restored.villageLevel);
        Assert.AreEqual(0, restored.villageScore);
    }

    [Test]
    public void LegacySaveStartsAtVillageOneAndRetainsExistingResources()
    {
        var restored = JsonUtility.FromJson<VillageManagement.VillageSaveData>("{\"currentOxygen\":57,\"bankLevel\":2}");
        Set(village, "saveData", restored);
        Call(village, "SanitizeSaveData");
        Assert.AreEqual(1, village.VillageLevel);
        Assert.AreEqual(0, village.VillageScore);
        Assert.AreEqual(57, village.CurrentOxygen);
        Assert.AreEqual(2, village.BankLevel);
        Assert.IsTrue(village.HasPurchasedPlayer(1));
    }

    [TestCase("Fruit", 1)]
    [TestCase("Fish", 2)]
    [TestCase("Pharmacy", 3)]
    [TestCase("ChickenTruck", 4)]
    [TestCase("Icecream", 5)]
    [TestCase("Bread", 6)]
    [TestCase("Coffee", 7)]
    public void BuildingUnlocksMatchRequestedLevels(string name, int level)
    {
        Assert.AreEqual(level, VillageManagement.GetBuildingUnlockLevel(name));
        Building building = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Village/{name}.prefab").GetComponent<Building>();
        Assert.AreEqual(level, building.RequiredVillageLevel);
    }

    [TestCase("hotSpring", 3)]
    [TestCase("hotel", 6)]
    [TestCase("FutureSpecialBuilding", 8)]
    public void SpecialBuildingUnlocksMatchRequestedLevels(string name, int level)
    {
        Assert.AreEqual(level, VillageManagement.GetSpecialBuildingUnlockLevel(name));
    }

    [TestCase(0, 1)] [TestCase(1, 2)] [TestCase(2, 4)] [TestCase(3, 6)]
    public void OilUnlocksMatchRequestedLevels(int index, int level)
    {
        Assert.AreEqual(level, VillageManagement.GetOilUnlockLevel(index));
    }

    [Test]
    public void LockedPathPurchaseDoesNotSpendOxygenOrCreateConstruction()
    {
        Path path = Root("Path", typeof(BoxCollider2D)).AddComponent<Path>();
        Building bread = Root("Bread").AddComponent<Building>();
        path.TryBuildSelected(bread);
        Assert.AreEqual(100, village.CurrentOxygen);
        Assert.IsNull(path.Building);
        Assert.IsFalse(path.HasActiveConstruction);
    }

    [Test]
    public void ScoreUiHidesOnlyGaugeAtNineAndKeepsShowingScore()
    {
        VillageScoreUI ui = Root("ScoreUI").AddComponent<VillageScoreUI>();
        TMP_Text level = Root("Level", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        TMP_Text score = Root("Points", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        Slider slider = Root("Gauge", typeof(RectTransform)).AddComponent<Slider>();
        Set(ui, "levelText", level); Set(ui, "scoreText", score);
        Set(ui, "scoreSlider", slider); Set(ui, "gaugeRoot", slider.gameObject);
        Call(ui, "Bind", village);
        village.AddVillageScore(3607);
        Assert.AreEqual("Village Lv. 9", level.text);
        Assert.AreEqual("7", score.text);
        Assert.IsFalse(slider.gameObject.activeSelf);
        Assert.IsTrue(score.gameObject.activeSelf);
        village.AddVillageScore(3);
        Assert.AreEqual("10", score.text);
    }

    [Test]
    public void TownHallActivatesAssignedObjectsAtRequiredLevel()
    {
        VillageTownHall hall = Root("Hall", typeof(BoxCollider2D)).AddComponent<VillageTownHall>();
        GameObject unlock = Root("Level3Object");
        Set(hall, "levelActivations", new List<VillageTownHall.LevelActivation>
        {
            new VillageTownHall.LevelActivation { requiredVillageLevel = 3, linkedPrefab = unlock }
        });
        Call(hall, "Bind", village);
        Assert.IsFalse(unlock.activeSelf);
        village.AddVillageScore(300);
        Assert.IsTrue(unlock.activeSelf);
        village.RemoveVillageScore(9999);
        Assert.IsTrue(unlock.activeSelf);
    }

    [Test]
    public void VillageSceneHasConnectedGaugeAndUsableTownHallCollider()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Village.unity", OpenSceneMode.Additive);
        try
        {
            VillageScoreUI score = null;
            VillageTownHall hall = null;
            VillanPath route = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (score == null) score = root.GetComponentInChildren<VillageScoreUI>(true);
                if (hall == null) hall = root.GetComponentInChildren<VillageTownHall>(true);
                if (route == null) route = root.GetComponentInChildren<VillanPath>(true);
            }
            Assert.IsNotNull(score);
            Assert.AreEqual("CanvasDynamic", score.transform.parent.name);
            Assert.IsNotNull(Get<Slider>(score, "scoreSlider").fillRect);
            Assert.IsNotNull(Get<TMP_Text>(score, "levelText"));
            Assert.IsNotNull(Get<TMP_Text>(score, "scoreText"));
            Assert.IsNotNull(hall);
            Assert.AreEqual(RigidbodyType2D.Static, hall.GetComponent<Rigidbody2D>().bodyType);
            Assert.Greater(hall.GetComponent<BoxCollider2D>().size.x, 0.5f);
            Assert.IsNotNull(route);
            Assert.Less(Vector3.Distance(hall.transform.position, route.GetMovePointPosition(route.MovePoints.Count - 1)), 0.1f);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private GameObject Root(string name, params Type[] components)
    {
        var root = new GameObject(name, components); roots.Add(root); return root;
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
}

public sealed class VillageTownHallPlayTests
{
    [UnityTest]
    public IEnumerator MonsterCollisionDeductsOnceFlashesRedAndRestoresAfterPointTwoSeconds()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        VillageManagement village = VillageManagement.EnsureInstance();
        typeof(VillageManagement).GetField("autoSaveOnChange", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(village, false);
        village.SaveData.villageLevel = 1;
        village.SaveData.villageScore = 55;
        village.SaveData.currentOxygen = 77;
        village.SaveData.currentEnergy = 33;
        var hallRoot = new GameObject("Hall", typeof(BoxCollider2D));
        var visual = new GameObject("Visual", typeof(SpriteRenderer));
        visual.transform.SetParent(hallRoot.transform, false);
        SpriteRenderer sprite = visual.GetComponent<SpriteRenderer>();
        Color originalColor = new Color(0.3f, 0.8f, 0.4f, 0.7f);
        sprite.color = originalColor;
        VillageTownHall hall = hallRoot.AddComponent<VillageTownHall>();
        typeof(VillageTownHall).GetField("visualRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hall, visual.transform);
        var enemyRoot = new GameObject("Enemy", typeof(BoxCollider2D), typeof(Rigidbody2D));
        Villan enemy = enemyRoot.AddComponent<Villan>();
        typeof(Villan).GetField("villageScoreDamage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(enemy, 7);
        float deadline = Time.time + 0.5f;
        while (village.VillageScore == 55 && Time.time < deadline) yield return null;
        Assert.AreEqual(48, village.VillageScore);
        Assert.AreEqual(new Color(1, 0, 0, originalColor.a), sprite.color);
        Assert.AreEqual(77, village.CurrentOxygen);
        Assert.AreEqual(33, village.CurrentEnergy);
        deadline = Time.time + 0.06f;
        while (Time.time < deadline) yield return null;
        Assert.Greater(Mathf.Abs(visual.transform.localPosition.x), 0.001f);
        hall.TakeDamage(2); // Restarting feedback must restore the original color and position first.
        deadline = Time.time + 0.25f;
        while (Time.time < deadline) yield return null;
        Assert.AreEqual(46, village.VillageScore);
        Assert.AreEqual(originalColor, sprite.color);
        Assert.AreEqual(Vector3.zero, visual.transform.localPosition);
        Object.Destroy(hallRoot); Object.Destroy(enemyRoot); Object.Destroy(village.gameObject);
        yield return null;
        yield return new ExitPlayMode();
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
#endif
