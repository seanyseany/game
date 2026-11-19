#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class StageManagerSelectionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string assetFolder;
    private StageManager first;
    private StageManager second;
    private float previousTimeScale;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        previousTimeScale = Time.timeScale;
        Time.timeScale = 10f;
        SceneManager.SetActiveScene(SceneManager.CreateScene("arcade manager selection test"));
        string folderName = "__StageManagerSelectionTests_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folderName);
        assetFolder = "Assets/" + folderName;
        first = MakePrefab("First", 3f);
        second = MakePrefab("Second", 7f);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Time.timeScale = previousTimeScale;
        if (GameData.Instance != null) Object.Destroy(GameData.Instance.gameObject);
        foreach (StageManager manager in Object.FindObjectsByType<StageManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (manager != null) typeof(StageManager).GetMethod("Retire", Private).Invoke(manager, null);
        yield return null;
        if (!string.IsNullOrEmpty(assetFolder)) AssetDatabase.DeleteAsset(assetFolder);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PrefabObjectPickerRegistersTheComponentAndRunsTheSelectedPrefab()
    {
        GameData data = MakeData(Entry(null, true));
        var serialized = new SerializedObject(data);
        SerializedProperty field = serialized.FindProperty("stageManagers").GetArrayElementAtIndex(0).FindPropertyRelative("prefab");
        Assert.IsTrue(PrefabUtility.IsPartOfPrefabAsset(first.gameObject));
        Assert.IsTrue(GameDataStageManagerEntryDrawer.AssignPrefab(field, first.gameObject));
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreSame(first, data.stageManagers[0].prefab);
        yield return RunReady(data);
        Assert.AreEqual(first.phaseBaseSpeed, StageManager.Instance.phaseBaseSpeed);
        yield return FirstPhase(StageManager.Instance);
    }

    [UnityTest]
    public IEnumerator PrefabObjectPickerRejectsUnrelatedAssetsAndAllowsClearingTheField()
    {
        GameData data = MakeData(Entry(first, true));
        var serialized = new SerializedObject(data);
        SerializedProperty field = serialized.FindProperty("stageManagers").GetArrayElementAtIndex(0).FindPropertyRelative("prefab");
        GameObject unrelated = AssetDatabase.LoadAssetAtPath<GameObject>(assetFolder + "/FirstBackground.prefab");
        LogAssert.Expect(LogType.Warning, "StageManager 컴포넌트가 있는 프리팹을 등록하세요.");
        Assert.IsFalse(GameDataStageManagerEntryDrawer.AssignPrefab(field, unrelated));
        Assert.AreSame(first, field.objectReferenceValue, "Invalid choices must preserve the previous registration.");
        Assert.IsTrue(GameDataStageManagerEntryDrawer.AssignPrefab(field, null));
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.IsNull(data.stageManagers[0].prefab);
        yield return RunReady(data);
        Assert.IsNull(StageManager.Instance);
    }

    [UnityTest]
    public IEnumerator CheckedPrefabReplacesSceneManagerAndSpawnsItsOwnPhase()
    {
        StageManager legacy = new GameObject("Legacy scene manager").AddComponent<StageManager>();
        GameData data = MakeData(Entry(first, false), Entry(second, true));
        yield return RunReady(data);
        Assert.IsTrue(legacy == null);
        StageManager selected = StageManager.Instance;
        Assert.IsNotNull(selected);
        Assert.AreEqual(second.phaseBaseSpeed, selected.phaseBaseSpeed);
        Assert.AreEqual(SceneManager.GetActiveScene(), selected.gameObject.scene);
        Assert.IsTrue(selected.isActiveAndEnabled, "Inactive prefab assets must be activated for the run.");
        yield return FirstPhase(selected);
        Assert.AreEqual(second.stage1PhasePrefabs[0].name + "_Pooled", ActivePhase(selected).name);
        Assert.AreEqual(1, Object.FindObjectsByType<StageManager>(FindObjectsSortMode.None).Length);
    }

    [UnityTest]
    public IEnumerator RetryReusesTheSelectedManagerAndItsPhasePool()
    {
        GameData data = MakeData(Entry(first, true), Entry(second, false));
        yield return RunReady(data);
        StageManager selected = StageManager.Instance;
        yield return FirstPhase(selected);
        GameObject phase = ActivePhase(selected);
        data.ResetGame();
        yield return RunReady(data);
        Assert.AreSame(selected, StageManager.Instance);
        yield return FirstPhase(selected);
        Assert.AreSame(phase, ActivePhase(selected));
        Assert.AreEqual(1, Active(selected).Count);
    }

    [UnityTest]
    public IEnumerator ChangingSelectionRemovesOldPhasesBackgroundAndEventSubscriptions()
    {
        GameData data = MakeData(Entry(first, true), Entry(second, false));
        yield return RunReady(data);
        StageManager previous = StageManager.Instance;
        yield return FirstPhase(previous);
        GameObject oldPhase = ActivePhase(previous);
        var backgrounds = (Dictionary<GameObject, GameObject>)typeof(StageManager).GetField("backgroundInstances", Private).GetValue(previous);
        GameObject oldBackground = backgrounds[previous.backgroundPrefab];
        data.stageManagers[0].use = false;
        data.stageManagers[1].use = true;
        data.ResetGame();
        yield return RunReady(data);
        Assert.IsTrue(previous == null);
        Assert.IsTrue(oldPhase == null, "Replacing a manager must remove its scene-root pool objects.");
        Assert.IsTrue(oldBackground == null);
        StageManager selected = StageManager.Instance;
        Assert.AreEqual(second.phaseBaseSpeed, selected.phaseBaseSpeed);
        int subscriptions = 0;
        foreach (Delegate callback in GameData.OnRageStart.GetInvocationList())
        {
            if (callback.Target is StageManager) subscriptions++;
            Assert.IsFalse(ReferenceEquals(callback.Target, previous), "The old manager must unsubscribe.");
        }
        Assert.AreEqual(1, subscriptions);
        yield return FirstPhase(selected);
        Assert.AreEqual(second.stage1PhasePrefabs[0].name + "_Pooled", ActivePhase(selected).name);
    }

    [UnityTest]
    public IEnumerator EmptyListKeepsTheExistingSceneManager()
    {
        StageManager legacy = new GameObject("Legacy scene manager").AddComponent<StageManager>();
        legacy.initialPoolSizePerPrefab = 0;
        legacy.stage1PhasePrefabs = first.stage1PhasePrefabs;
        GameData data = MakeData();
        yield return RunReady(data);
        Assert.AreSame(legacy, StageManager.Instance);
        yield return FirstPhase(legacy);
        Assert.AreEqual(1, Active(legacy).Count);
    }

    [UnityTest]
    public IEnumerator UncheckedEntriesDoNotRunAndCanBeSelectedForTheNextRetry()
    {
        StageManager legacy = new GameObject("Legacy scene manager").AddComponent<StageManager>();
        GameData data = MakeData(Entry(first, false), Entry(second, false));
        yield return RunReady(data);
        Assert.IsTrue(legacy == null);
        Assert.IsNull(StageManager.Instance);
        data.stageManagers[1].use = true;
        data.ResetGame();
        yield return RunReady(data);
        Assert.AreEqual(second.phaseBaseSpeed, StageManager.Instance.phaseBaseSpeed);
    }

    [UnityTest]
    public IEnumerator SelectionSurvivesVillageAndCreatesANewManagerInTheNextArcadeScene()
    {
        GameData data = MakeData(Entry(first, true), Entry(second, false));
        yield return RunReady(data);
        StageManager old = StageManager.Instance;
        Scene arcade = SceneManager.GetActiveScene();
        Scene village = SceneManager.CreateScene("Village selection test");
        SceneManager.SetActiveScene(village);
        typeof(GameData).GetMethod("InitializeForScene", Private).Invoke(data, new object[] { village });
        yield return SceneManager.UnloadSceneAsync(arcade);
        Assert.IsTrue(old == null);
        Assert.IsNull(StageManager.Instance);
        Assert.AreSame(data, GameData.Instance);
        Scene next = SceneManager.CreateScene("arcade next selection test");
        SceneManager.SetActiveScene(next);
        typeof(GameData).GetMethod("InitializeForScene", Private).Invoke(data, new object[] { next });
        yield return RunReady(data);
        Assert.AreEqual(first.phaseBaseSpeed, StageManager.Instance.phaseBaseSpeed);
        Assert.AreEqual(next, StageManager.Instance.gameObject.scene);
    }

    private StageManager MakePrefab(string name, float speed)
    {
        var phase = new GameObject(name + " phase");
        phase.SetActive(false);
        var end = new GameObject("End", typeof(BoxCollider2D), typeof(PhaseEndTrigger));
        end.transform.SetParent(phase.transform, false);
        end.GetComponent<BoxCollider2D>().isTrigger = true;
        GameObject phaseAsset = PrefabUtility.SaveAsPrefabAsset(phase, assetFolder + "/" + name + "Phase.prefab");
        Object.DestroyImmediate(phase);
        var background = new GameObject(name + " background", typeof(SpriteRenderer));
        GameObject backgroundAsset = PrefabUtility.SaveAsPrefabAsset(background, assetFolder + "/" + name + "Background.prefab");
        Object.DestroyImmediate(background);
        var root = new GameObject(name + " manager");
        root.SetActive(false);
        StageManager manager = root.AddComponent<StageManager>();
        manager.phaseBaseSpeed = speed;
        manager.initialPoolSizePerPrefab = 0;
        manager.poolSizePerPrefab = 2;
        manager.stage1PhasePrefabs = new[] { phaseAsset };
        manager.backgroundPrefab = backgroundAsset;
        StageManager asset = PrefabUtility.SaveAsPrefabAsset(root, assetFolder + "/" + name + ".prefab").GetComponent<StageManager>();
        Object.DestroyImmediate(root);
        return asset;
    }

    private static GameData.StageManagerEntry Entry(StageManager prefab, bool use) =>
        new GameData.StageManagerEntry { prefab = prefab, use = use };

    private static GameData MakeData(params GameData.StageManagerEntry[] entries)
    {
        GameData data = new GameObject("Selection game data").AddComponent<GameData>();
        data.stageManagers = new List<GameData.StageManagerEntry>(entries);
        return data;
    }

    private static IEnumerator RunReady(GameData data)
    {
        yield return null;
        yield return null;
        float deadline = Time.unscaledTime + 3f;
        while (data.IsResetting && Time.unscaledTime < deadline) yield return null;
        Assert.IsFalse(data.IsResetting);
        Assert.IsFalse(data.gameOver);
        yield return null; // Destruction of a replaced singleton finishes at the end of the frame.
    }

    private static IEnumerator FirstPhase(StageManager manager)
    {
        float deadline = Time.time + 5f;
        while (Active(manager).Count == 0 && Time.time < deadline) yield return null;
        Assert.Greater(Active(manager).Count, 0);
    }

    private static IList Active(StageManager manager) => (IList)typeof(StageManager).GetField("activePhases", Private).GetValue(manager);
    private static GameObject ActivePhase(StageManager manager) => (GameObject)Active(manager)[0].GetType().GetField("obj").GetValue(Active(manager)[0]);
}
#endif
