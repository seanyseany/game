#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using IOPath = System.IO.Path;
using Object = UnityEngine.Object;

public sealed class PlayerOwnershipTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> roots = new List<GameObject>();
    private VillageManagement village;
    private VillageManagement previousInstance;
    private string savePath;

    [SetUp]
    public void SetUp()
    {
        previousInstance = VillageManagement.Instance;
        var root = new GameObject("Player ownership test");
        roots.Add(root);
        root.SetActive(false);
        village = root.AddComponent<VillageManagement>();
        Set(village, "autoSaveOnChange", false);
        typeof(VillageManagement).GetProperty("Instance").SetValue(null, village);
        savePath = IOPath.Combine(IOPath.GetTempPath(), "player-ownership-" + Guid.NewGuid() + ".json");
        Set(village, "saveFileName", savePath);
        village.SaveData.villageLevel = 9;
        village.SaveData.purchasedPlayerTypes = new List<int> { 1, 2, 4, 3, 5 };
    }

    [TearDown]
    public void TearDown()
    {
        typeof(VillageManagement).GetProperty("Instance").SetValue(null, previousInstance);
        foreach (GameObject root in roots) if (root != null) Object.DestroyImmediate(root);
        roots.Clear();
        if (File.Exists(savePath)) File.Delete(savePath);
    }

    [Test]
    public void PurchasedPlayersStayPlayableWithStaleCatalogFlags()
    {
        Set(village, "arcadePlayers", Catalog());
        AssertAllPlayers(village);
        Assert.IsFalse(village.TryPurchasePlayer(3), "Owned players cannot be charged again.");
    }

    [Test]
    public void LevelRequirementsGateNewPurchasesWithoutHidingOwnedPlayers()
    {
        village.SaveData.villageLevel = 1;
        village.SaveData.purchasedPlayerTypes = new List<int> { 1, 3 };
        Assert.IsTrue(village.IsArcadePlayerAvailable(3));
        Assert.IsFalse(village.IsArcadePlayerAvailable(2));
        Assert.IsFalse(village.TryPurchasePlayer(2));
        CollectionAssert.AreEqual(new[] { 1, 3 }, village.GetAvailableArcadePlayers().Select(entry => entry.playerType));
    }

    [Test]
    public void MissingAndDuplicateCatalogEntriesCannotHideOrDuplicateOwnedPlayers()
    {
        Set(village, "arcadePlayers", new List<VillageManagement.ArcadePlayerEntry>
        {
            new VillageManagement.ArcadePlayerEntry { playerType = 2 },
            null,
            new VillageManagement.ArcadePlayerEntry { playerType = 2 },
            new VillageManagement.ArcadePlayerEntry { playerType = 1 }
        });
        AssertAllPlayers(village);
        Assert.IsFalse(village.IsArcadePlayerAvailable(0));
        Assert.IsFalse(village.TryGetArcadePlayer(6, out _));
    }

    [Test]
    public void DiskReloadPreservesOwnershipAndSelectionAndNormalizesInvalidIds()
    {
        village.SaveData.purchasedPlayerTypes.AddRange(new[] { 3, 0, 6, -1 });
        village.SaveData.selectedArcadePlayerType = 5;
        village.Save();
        var saved = JsonUtility.FromJson<VillageManagement.VillageSaveData>(File.ReadAllText(savePath));
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5 }, saved.purchasedPlayerTypes);

        var root = new GameObject("Reloaded player ownership");
        roots.Add(root);
        root.SetActive(false);
        VillageManagement reloaded = root.AddComponent<VillageManagement>();
        Set(reloaded, "autoSaveOnChange", false);
        Set(reloaded, "saveFileName", savePath);
        Set(reloaded, "arcadePlayers", Catalog());
        reloaded.Load();
        AssertAllPlayers(reloaded);
        Assert.AreEqual(5, reloaded.SelectedArcadePlayerType);
    }

    internal static List<VillageManagement.ArcadePlayerEntry> Catalog()
    {
        var entries = new List<VillageManagement.ArcadePlayerEntry>();
        for (int type = 1; type <= 5; type++)
            entries.Add(new VillageManagement.ArcadePlayerEntry { playerType = type, available = type <= 2 });
        return entries;
    }

    internal static void AssertAllPlayers(VillageManagement manager)
    {
        for (int type = 1; type <= 5; type++) Assert.IsTrue(manager.IsArcadePlayerAvailable(type), "Player " + type);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, manager.GetAvailableArcadePlayers().Select(entry => entry.playerType));
    }

    internal static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
}

public sealed class PlayerOwnershipPlayTests
{
    private static string SavePath => IOPath.Combine(IOPath.GetTempPath(), "player-ownership-ui-test.json");
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }

    [UnityTest]
    public IEnumerator PurchasesRefreshPlayImmediatelyAndSurviveManagerRestartWithoutOpeningShop()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        VillageManagement village = CreateManager(false);
        village.SetVillageProgress(5, 0);
        village.SetOxygenCapacity(1000);
        village.SetCurrentOxygen(1000);
        Assert.IsTrue(village.TryPurchasePlayer(2));
        var canvas = new GameObject("Ownership canvas", typeof(Canvas));
        var button = new GameObject("Play", typeof(RectTransform), typeof(Image), typeof(Button));
        button.transform.SetParent(canvas.transform, false);
        Play play = button.AddComponent<Play>();
        play.OpenPlayMenu();
        CollectionAssert.AreEqual(new[] { 1, 2 }, DisplayedPlayers(play));

        // The menu must update while open, without going through the shop first.
        for (int type = 3; type <= 5; type++) Assert.IsTrue(village.TryPurchasePlayer(type));
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, DisplayedPlayers(play));
        village.SetSelectedArcadePlayerType(5);
        play.ClosePlayMenu();
        play.enabled = false;
        Object.DestroyImmediate(village.gameObject);
        village = CreateManager(true);
        play.enabled = true;
        play.OpenPlayMenu();
        Assert.AreEqual(5, village.SelectedArcadePlayerType);
        Assert.AreEqual(5, (int)typeof(Play).GetField("selectedPlayerType", Private).GetValue(play));
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, DisplayedPlayers(play));

        // A shop visit must agree with Play even if the current level/catalog differs.
        village.SetVillageProgress(1, 0);
        var shopRoot = new GameObject("Player section");
        ShopPlaceholderUI shop = shopRoot.AddComponent<ShopPlaceholderUI>();
        PlayerOwnershipTests.Set(shop, "sectionTitle", "Player");
        var content = new GameObject("Shop content", typeof(RectTransform));
        content.transform.SetParent(canvas.transform, false);
        shop.ShowSection(content.GetComponent<RectTransform>());
        var shopButtons = (List<Button>)typeof(ShopPlaceholderUI).GetField("buttons", Private).GetValue(shop);
        Assert.AreEqual(5, shopButtons.Count);
        foreach (Button ownedButton in shopButtons)
        {
            StringAssert.Contains("구매 완료", ownedButton.GetComponentInChildren<Text>().text);
            Assert.IsFalse(ownedButton.interactable);
        }
        shop.HideSection();
        play.OpenPlayMenu();
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, DisplayedPlayers(play));
        Assert.AreEqual(960, village.CurrentOxygen, "Four purchases should be charged exactly once.");
        yield return null;
    }

    private static VillageManagement CreateManager(bool staleCatalog)
    {
        var root = new GameObject("Ownership manager");
        root.SetActive(false);
        VillageManagement manager = root.AddComponent<VillageManagement>();
        PlayerOwnershipTests.Set(manager, "dontDestroyOnLoad", false);
        PlayerOwnershipTests.Set(manager, "saveFileName", SavePath);
        if (staleCatalog) PlayerOwnershipTests.Set(manager, "arcadePlayers", PlayerOwnershipTests.Catalog());
        root.SetActive(true);
        return manager;
    }

    private static List<int> DisplayedPlayers(Play play) =>
        (List<int>)typeof(Play).GetField("displayedPlayerTypes", Private).GetValue(play);
}
#endif
