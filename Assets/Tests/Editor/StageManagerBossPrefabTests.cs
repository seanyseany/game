#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class StageManagerBossPrefabTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private StageManager manager;
    private string assetFolder;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        var data = new GameObject("Boss prefab game data").AddComponent<GameData>();
        data.enabled = false;
        manager = new GameObject("Boss prefab manager").AddComponent<StageManager>();
        string folderName = "__BossPrefabTests_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folderName);
        assetFolder = "Assets/" + folderName;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (manager != null)
        {
            manager.ForceClearBossNow();
            Object.Destroy(manager.gameObject);
        }
        if (GameData.Instance != null) Object.Destroy(GameData.Instance.gameObject);
        yield return null;
        if (!string.IsNullOrEmpty(assetFolder)) AssetDatabase.DeleteAsset(assetFolder);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PrefabBossSpawnsAtItsAuthoredPoseAndIsDestroyedOnReset()
    {
        GameObject template = MakeBoss();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(template, assetFolder + "/Boss.prefab");
        Object.DestroyImmediate(template);
        manager.bossPrefab = prefab;
        StartEncounter();
        GameObject spawned = GetBoss();
        Assert.IsTrue(manager.HasActiveBoss, "A registered prefab asset must start a boss encounter.");
        Assert.AreNotSame(prefab, spawned);
        Assert.AreEqual(prefab.transform.position, spawned.transform.position);
        Assert.AreEqual(Boss.State.Entering, spawned.GetComponent<Boss>().state);
        manager.ForceClearBossNow();
        yield return null;
        Assert.IsTrue(spawned == null);
        Assert.IsNotNull(prefab);
        Assert.IsFalse(prefab.activeSelf, "The prefab asset itself must not be activated or modified.");
    }

    [UnityTest]
    public IEnumerator SceneBossIsReusedAndDeactivatedOnReset()
    {
        GameObject sceneBoss = MakeBoss();
        manager.bossPrefab = sceneBoss;
        StartEncounter();
        Assert.AreSame(sceneBoss, GetBoss());
        Assert.IsTrue(sceneBoss.activeSelf);
        manager.ForceClearBossNow();
        Assert.IsFalse(sceneBoss.activeSelf);
        StartEncounter();
        Assert.AreSame(sceneBoss, GetBoss());
        Assert.IsTrue(sceneBoss.activeSelf);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RegisteredManagerContainsPortableBossBackgroundsAndTransitionPoints()
    {
        string path = AssetDatabase.GUIDToAssetPath("4910a0bf96f07411daab974c5d6de453");
        Assert.IsNotEmpty(path, "The configured manager asset must still exist after renaming.");
        StageManager prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<StageManager>();
        Assert.IsNotNull(prefab.bossPrefab);
        Assert.IsTrue(PrefabUtility.IsPartOfPrefabAsset(prefab.bossPrefab));
        Assert.IsNotNull(prefab.bossPrefab.GetComponentInChildren<Boss>(true));
        Assert.IsNotNull(prefab.backgroundPrefab);
        Assert.IsTrue(PrefabUtility.IsPartOfPrefabAsset(prefab.backgroundPrefab));
        Assert.Greater(prefab.backgroundPrefabs.Length, 0);
        foreach (GameObject background in prefab.backgroundPrefabs)
        {
            Assert.IsNotNull(background, "Background entries must not retain missing scene references.");
            Assert.IsTrue(PrefabUtility.IsPartOfPrefabAsset(background));
        }
        Assert.IsNotNull(prefab.preBackgroundPoint);
        Assert.IsNotNull(prefab.preBackgroundPoint2);
        Assert.IsTrue(prefab.preBackgroundPoint.IsChildOf(prefab.transform));
        Assert.IsTrue(prefab.preBackgroundPoint2.IsChildOf(prefab.transform));
        yield break;
    }

    private static GameObject MakeBoss()
    {
        var root = new GameObject("Boss template");
        root.SetActive(false);
        root.transform.position = new Vector3(30f, 4f, 0f);
        Boss boss = root.AddComponent<Boss>();
        boss.usePool = false;
        boss.enterMoveTime = 100f;
        return root;
    }

    private void StartEncounter() => typeof(StageManager).GetMethod("StartBossEncounterNow", Private).Invoke(manager, new object[] { 3 });
    private GameObject GetBoss() => (GameObject)typeof(StageManager).GetField("activeBoss", Private).GetValue(manager);
}
#endif
