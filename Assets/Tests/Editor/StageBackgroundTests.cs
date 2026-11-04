#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class StageBackgroundTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private StageManager manager;
    private GameObject first;
    private GameObject second;
    private GameObject normal;
    private Random.State randomState;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        randomState = Random.state;
        manager = new GameObject("Background stage test").AddComponent<StageManager>();
        first = new GameObject("First background");
        second = new GameObject("Second background");
        normal = new GameObject("Normal phase");
        normal.SetActive(false);
        manager.initialPoolSizePerPrefab = 0;
        manager.backgroundPrefab = first;
        manager.backgroundPrefabs = new[] { first, second, second, null };
        manager.stage1PhasePrefabs = new[] { normal };
        manager.stage2PhasePrefabs = new[] { normal };
        manager.stage3PhasePrefabs = new[] { normal };
        manager.stage4PhasePrefabs = new[] { normal };
        manager.speedUp1 = 1;
        manager.speedUp2 = 2;
        manager.speedUp3 = 100;
        Call("InitPools");
        manager.ResetState();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Random.state = randomState;
        if (Application.isPlaying)
            yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Stage1RollsOnceAtItsEndAndOnlySuccessfulRollsScheduleStage3()
    {
        int successes = 0;
        for (int seed = 0; seed < 64; seed++)
        {
            manager.ResetState();
            manager.OnPhasePassed(); // First and only stage 1 phase.
            Assert.AreEqual(0, Get<int>("pendingBackgroundChanges"));
            Random.InitState(seed);
            bool expectedChange = Random.value < 0.5f;
            Random.InitState(seed);
            manager.OnPhasePassed(); // Stage 1 ends; stage 2 begins.
            Assert.AreEqual(expectedChange, Get<bool>("stage1BackgroundChangeTriggered"));
            Assert.AreEqual(expectedChange ? 1 : 0, Get<int>("pendingBackgroundChanges"));
            int target = Get<int>("stage3BackgroundChangeAt");
            if (expectedChange)
            {
                successes++;
                Assert.That(target, Is.InRange(5, 12));
            }

            for (int count = 1; count <= 14; count++)
            {
                manager.OnPhasePassed();
                Assert.AreEqual(expectedChange && count >= target,
                    Get<bool>("stage3BackgroundChangeTriggered"));
                Assert.AreEqual(expectedChange ? (count >= target ? 2 : 1) : 0,
                    Get<int>("pendingBackgroundChanges"), "A stage 3 change is guaranteed exactly once.");
            }
        }
        Assert.That(successes, Is.InRange(16, 48), "Both branches of the 50% roll must be exercised.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator BackgroundChangesKeepStage2AndStage3SpawningAndAdvancing()
    {
        manager.stage1BackgroundChangeChance = 1f;
        manager.speedUp2 = 6;
        Set("phaseSpawnCount", manager.speedUp1);
        Call("SpawnPhase");
        Assert.AreEqual(1, Get<int>("pendingBackgroundChanges"));
        Assert.AreEqual(2, Call("GetCurrentPhaseStage"));
        Assert.AreEqual(manager.speedUp1 + 1, manager.GetPhaseSpawnCount());
        for (int i = 0; i < 3; i++) Call("SpawnPhase");
        Assert.AreEqual(manager.speedUp1 + 4, manager.GetPhaseSpawnCount());
        for (int frame = 0; frame < 5 && !second.activeSelf; frame++)
            yield return null;
        Assert.IsTrue(second.activeSelf);
        Assert.AreEqual(2, Call("GetCurrentPhaseStage"));

        Set("phaseSpawnCount", manager.speedUp2);
        Set("stage3BackgroundChangeAt", 1);
        Call("SpawnPhase");
        Assert.IsTrue(Get<bool>("stage3BackgroundChangeTriggered"));
        Assert.AreEqual(3, Call("GetCurrentPhaseStage"));
        for (int i = 0; i < 4; i++) Call("SpawnPhase");
        Assert.AreEqual(manager.speedUp2 + 5, manager.GetPhaseSpawnCount());
        for (int frame = 0; frame < 5 && !first.activeSelf; frame++)
            yield return null;
        Assert.IsTrue(first.activeSelf);
        Assert.AreEqual(3, Call("GetCurrentPhaseStage"));
        Assert.AreSame(normal, NextPhase(3));
    }

    [UnityTest]
    public IEnumerator MovingPreBackgroundDoesNotReplaceNormalOrSpecialPhaseCycles()
    {
        var special = new GameObject("Stage 2 and 3 special");
        special.SetActive(false);
        var specials = new[] { new SpecialPhaseEntry { prefab = special, spawnChance = 1f } };
        manager.stage2SpecialPhasePrefabs = specials;
        manager.stage3SpecialPhasePrefabs = specials;
        manager.speedUp2 = 6;
        manager.speedUp3 = 12;
        var pre = new GameObject("Pre background template");
        pre.SetActive(false);
        manager.preBackgroundPrefab = pre;
        manager.preBackgroundPoint = new GameObject("Transition start").transform;
        manager.preBackgroundPoint.position = Vector3.right * 10f;
        manager.preBackgroundPoint2 = new GameObject("Transition end").transform;
        manager.preBackgroundPoint2.position = Vector3.left * 10f;
        Set("phaseSpawnCount", manager.speedUp1);
        Call("RequestBackgroundChange");
        Assert.AreEqual(2, Call("GetCurrentPhaseStage"));
        foreach (int stage in new[] { 2, 3 })
        {
            var cycle = new List<GameObject>();
            for (int i = 0; i < (stage == 2 ? 5 : 6); i++) cycle.Add(NextPhase(stage));
            CollectionAssert.Contains(cycle, normal);
            CollectionAssert.Contains(cycle, special);
        }
        yield return WaitGameSeconds(0.2f);
        Assert.IsTrue(first.activeSelf);
        GameObject effect = Get<GameObject>("preBackgroundInstance");
        Assert.IsNotNull(effect);
        effect.transform.position = Vector3.zero;
        Call("LateUpdate");
        Assert.IsTrue(second.activeSelf);
        Assert.AreEqual(2, Call("GetCurrentPhaseStage"));
        effect.transform.position = Vector3.left * 11f;
        for (int frame = 0; frame < 5 && Get<GameObject>("preBackgroundInstance") != null; frame++)
            yield return null;
        Assert.IsNull(Get<GameObject>("preBackgroundInstance"));
        Assert.AreEqual(2, Call("GetCurrentPhaseStage"));
    }

    [UnityTest]
    public IEnumerator CancelledResetMissingAndPostBossBackgroundChangesKeepTheirPhasePools()
    {
        manager.stage1BackgroundChangeChance = 0f;
        Set("phaseSpawnCount", manager.speedUp1);
        Call("TryRollStage1BackgroundChange");
        Assert.AreEqual(0, Get<int>("pendingBackgroundChanges"));
        Assert.AreEqual(2, Call("GetCurrentPhaseStage"));
        Call("RequestBackgroundChange");
        manager.enabled = false;
        Assert.AreEqual(0, Get<int>("pendingBackgroundChanges"));
        Assert.AreEqual(2, Call("GetCurrentPhaseStage"));
        manager.enabled = true;
        Call("RequestBackgroundChange");
        manager.ResetState();
        Assert.AreEqual(1, Call("GetCurrentPhaseStage"));
        manager.backgroundPrefabs = new[] { first };
        manager.ResetState();
        Set("phaseSpawnCount", manager.speedUp1);
        Assert.IsFalse((bool)Call("RequestBackgroundChange"));
        Assert.AreEqual(2, Call("GetCurrentPhaseStage"));

        var stage4 = new GameObject("Stage 4 normal phase");
        stage4.SetActive(false);
        manager.stage4PhasePrefabs = new[] { stage4 };
        manager.backgroundPrefabs = new[] { second };
        manager.ResetState();
        Call("ResumeAfterBoss");
        Assert.AreEqual(104, Call("GetCurrentPhaseStage"));
        Assert.AreEqual(1, Get<int>("pendingBackgroundChanges"));
        for (int cycle = 0; cycle < 4; cycle++)
        {
            // Start a fresh two-phase cycle while the background change is still queued.
            Get<Dictionary<int, List<GameObject>>>("phaseShuffleByStage").Clear();
            CollectionAssert.AreEquivalent(new[] { normal, stage4 }, new[] { NextPhase(104), NextPhase(104) });
        }
        int count = manager.GetPhaseSpawnCount();
        Call("SpawnPhase");
        Assert.AreEqual(count + 1, manager.GetPhaseSpawnCount());
        Assert.AreEqual(104, Call("GetCurrentPhaseStage"));
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpecialRageTestMachineGunAndMiniBossSpawnsDoNotAdvanceNormalCount()
    {
        var special = new GameObject("Special phase");
        special.SetActive(false);
        var machineGun = new GameObject("Machine gun phase") { tag = "MachineGunStage" };
        machineGun.SetActive(false);
        var miniBoss = new GameObject("Mini boss phase") { tag = "miniBoss" };
        miniBoss.SetActive(false);
        manager.stage3SpecialPhasePrefabs = new[] { new SpecialPhaseEntry { prefab = special, spawnChance = 1f } };
        Set("stage1BackgroundChangeTriggered", true);
        Set("stage3BackgroundChangeAt", 5);
        for (int i = 0; i < 6; i++)
        {
            Spawn(3, special);
            Spawn(-1, normal, true);
            Spawn(0, normal);
            Spawn(3, machineGun);
            Spawn(3, miniBoss);
        }
        Assert.AreEqual(0, Get<int>("stage3NormalPhaseSpawnCount"));
        Assert.AreEqual(0, Get<int>("pendingBackgroundChanges"));
        for (int i = 0; i < 4; i++) Spawn(3, normal);
        Assert.AreEqual(0, Get<int>("pendingBackgroundChanges"));
        Spawn(3, normal);
        Assert.AreEqual(1, Get<int>("pendingBackgroundChanges"));
        Set("stage4BackgroundChangeAt", 2);
        for (int i = 0; i < 6; i++) Spawn(104, special);
        Assert.AreEqual(0, Get<int>("stage4NormalPhaseSpawnCount"));
        Spawn(104, normal);
        Assert.AreEqual(1, Get<int>("pendingBackgroundChanges"));
        Spawn(104, normal);
        Assert.AreEqual(2, Get<int>("pendingBackgroundChanges"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator PostBossSpecialsAppearOnceAcrossRepeatedCyclesAndResetRestoresThem()
    {
        var stage3Special = new GameObject("Stage 3 one-shot special");
        stage3Special.SetActive(false);
        var stage4Special = new GameObject("Stage 4 one-shot special");
        stage4Special.SetActive(false);
        manager.stage3SpecialPhasePrefabs = new[] { new SpecialPhaseEntry { prefab = stage3Special, spawnChance = 1f } };

        for (int configuredCount = 1; configuredCount <= 3; configuredCount++)
        {
            manager.stage4SpecialPhasePrefabs = configuredCount == 1 ? null :
                configuredCount == 2 ? new[] { new SpecialPhaseEntry { prefab = stage4Special, spawnChance = 1f } } :
                new[]
                {
                    new SpecialPhaseEntry { prefab = stage4Special, spawnChance = 1f },
                    new SpecialPhaseEntry { prefab = stage3Special, spawnChance = 1f }
                };
            for (int run = 0; run < 2; run++)
            {
                manager.stage3PhasePrefabs = run == 0 ? new[] { normal } : new[] { normal, stage3Special };
                manager.ResetState();
                Set("postBossMixedPhaseUnlocked", true);
                int stage3Count = 0;
                int stage4Count = 0;
                int normalCount = 0;
                for (int spawn = 0; spawn < 64; spawn++)
                {
                    GameObject next = NextPhase(104);
                    if (next == stage3Special) stage3Count++;
                    else if (next == stage4Special) stage4Count++;
                    else
                    {
                        Assert.AreSame(normal, next, "Normal phases must continue after the specials are exhausted.");
                        normalCount++;
                    }
                }
                Assert.AreEqual(configuredCount == 3 ? 2 : 1, stage3Count);
                Assert.AreEqual(configuredCount >= 2 ? 1 : 0, stage4Count);
                Assert.AreEqual(64 - configuredCount, normalCount);
            }
        }
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator BossAlwaysChangesBackgroundAndMixedStageIntervalsGrowByThree()
    {
        Call("ResumeAfterBoss");
        Assert.AreEqual(1, Get<int>("pendingBackgroundChanges"));
        Assert.AreEqual(1, Get<int>("stage4NormalPhaseSpawnCount"), "The first resumed normal phase counts.");
        for (int cycle = 0; cycle < 8; cycle++)
        {
            int minimum = 12 + cycle * 3;
            int target = Get<int>("stage4BackgroundChangeAt");
            Assert.That(target, Is.InRange(minimum, minimum + 3));
            int spawned = Get<int>("stage4NormalPhaseSpawnCount");
            for (int i = spawned; i < target - 1; i++) manager.OnPhasePassed();
            Assert.AreEqual(cycle + 1, Get<int>("pendingBackgroundChanges"));
            manager.OnPhasePassed();
            Assert.AreEqual(cycle + 2, Get<int>("pendingBackgroundChanges"));
            Assert.AreEqual(0, Get<int>("stage4NormalPhaseSpawnCount"), "Each interval counts from the previous change.");
        }
        manager.ResetState(); // Do not leave the queued transitions running.
        yield return null;
    }

    [UnityTest]
    public IEnumerator ChildCenterWorldPositionControlsSwapAndPauseAndRetryAreRespected()
    {
        var pre = new GameObject("Pre background template");
        pre.SetActive(false);
        pre.transform.position = new Vector3(4f, 5f, 6f);
        pre.transform.localScale = new Vector3(2f, 1f, 1f);
        var center = new GameObject("center").transform;
        center.SetParent(pre.transform, false);
        center.localPosition = Vector3.right * 2f;
        manager.preBackgroundPrefab = pre;
        manager.preBackgroundPoint = new GameObject("Pre Background Point").transform;
        manager.preBackgroundPoint.position = new Vector3(10f, 2f, 3f);
        manager.preBackgroundPoint2 = new GameObject("Pre Background Point 2").transform;
        manager.preBackgroundPoint2.position = new Vector3(-10f, 0f, 0f);
        Assert.IsTrue((bool)Call("RequestBackgroundChange"));
        for (int frame = 0; frame < 5 && Get<GameObject>("preBackgroundInstance") == null; frame++)
            yield return null;
        GameObject effect = Get<GameObject>("preBackgroundInstance");
        Assert.IsNotNull(effect);
        Assert.AreEqual(manager.preBackgroundPoint.position, effect.transform.position);
        Assert.IsTrue(first.activeSelf);
        Assert.IsFalse(second.activeSelf);
        effect.transform.position = new Vector3(-3f, 2f, 3f);
        yield return WaitGameSeconds(0.2f);
        Assert.AreEqual(1f, effect.transform.Find("center").position.x);
        Assert.IsTrue(first.activeSelf, "A negative root X must not swap while the child center is still positive.");
        manager.SetGameplayPause(true);
        effect.transform.position = new Vector3(-4f, 2f, 3f);
        yield return WaitGameSeconds(0.2f);
        Assert.IsTrue(first.activeSelf);
        Assert.IsFalse(second.activeSelf);
        manager.SetGameplayPause(false);
        Call("LateUpdate");
        Assert.IsFalse(first.activeSelf);
        Assert.IsTrue(second.activeSelf);
        Assert.IsTrue(effect.activeSelf);

        manager.ResetState();
        Assert.IsTrue(first.activeSelf);
        Assert.IsFalse(second.activeSelf);
        Assert.IsNull(Get<GameObject>("preBackgroundInstance"));
        Assert.IsFalse(effect.activeSelf);
        Assert.AreEqual(0, Get<int>("pendingBackgroundChanges"));
        Assert.IsFalse(Get<bool>("stage1BackgroundRollCompleted"));
        Call("RequestBackgroundChange");
        yield return null;
        manager.ResetState();
        yield return WaitGameSeconds(0.2f);
        Assert.IsTrue(first.activeSelf, "A previous run's pending swap must stay cancelled.");
        Assert.IsFalse(second.activeSelf);
    }

    [UnityTest]
    public IEnumerator CaveMovesFromPointToPoint2AndOutlivesTheOldThreeSecondLimit()
    {
        manager.preBackgroundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Cave.prefab");
        manager.preBackgroundPoint = new GameObject("Pre Background Point").transform;
        manager.preBackgroundPoint.position = new Vector3(8f, 2f, 3f);
        manager.preBackgroundPoint2 = new GameObject("Pre Background Point 2").transform;
        manager.preBackgroundPoint2.position = new Vector3(-24f, -100f, 50f);
        Call("RequestBackgroundChange");
        for (int frame = 0; frame < 5 && Get<GameObject>("preBackgroundInstance") == null; frame++)
            yield return null;
        GameObject cave = Get<GameObject>("preBackgroundInstance");
        Assert.IsNotNull(cave);
        Assert.IsNotNull(cave.GetComponent<SpriteRenderer>());
        Assert.AreEqual(8f, cave.GetComponent<Mover>().baseSpeed);
        float startX = cave.transform.position.x;
        yield return WaitGameSeconds(3.2f);
        Assert.IsTrue(cave != null && cave.activeSelf, "Travel time, rather than a lifetime timer, controls deletion.");
        Assert.Less(cave.transform.position.x, startX);
        Assert.Greater(cave.transform.position.x, manager.preBackgroundPoint2.position.x);
        Assert.AreEqual(manager.preBackgroundPoint.position.y, cave.transform.position.y);
        Assert.IsTrue(second.activeSelf, "The background swaps when Cave's child center crosses world X = 0.");

        float deadline = Time.time + 2f;
        while (Get<GameObject>("preBackgroundInstance") != null && Time.time < deadline)
            yield return null;
        Assert.IsNull(Get<GameObject>("preBackgroundInstance"), "Crossing Point 2's X removes Cave even with different Y/Z coordinates.");
        yield return null;
        Assert.IsTrue(cave == null);
    }

    [UnityTest]
    public IEnumerator CaveCrossingCenterAndPoint2InOneFrameSwapsBeforeDeletion()
    {
        manager.preBackgroundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Cave.prefab");
        manager.preBackgroundPoint = new GameObject("Pre Background Point").transform;
        manager.preBackgroundPoint.position = new Vector3(5f, 0f, 3f);
        manager.preBackgroundPoint2 = new GameObject("Pre Background Point 2").transform;
        manager.preBackgroundPoint2.position = new Vector3(-0.5f, 0f, 3f);
        Call("RequestBackgroundChange");
        for (int frame = 0; frame < 5 && Get<GameObject>("preBackgroundInstance") == null; frame++)
            yield return null;
        GameObject cave = Get<GameObject>("preBackgroundInstance");
        Assert.IsNotNull(cave);
        Assert.IsTrue(first.activeSelf);
        cave.transform.position = new Vector3(-1f, 0f, 3f);
        Call("LateUpdate");
        Assert.IsNull(Get<GameObject>("preBackgroundInstance"));
        Assert.IsFalse(first.activeSelf);
        Assert.IsTrue(second.activeSelf);
    }

    [UnityTest]
    public IEnumerator CenterSwapGameOverAndMissingMarkerCannotDeleteCaveBeforeItsDestination()
    {
        float previousTimeScale = Time.timeScale;
        GameData data = null;
        try
        {
            Time.timeScale = 4f;
            manager.preBackgroundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Cave.prefab");
            manager.preBackgroundPoint = new GameObject("Cave Point").transform;
            manager.preBackgroundPoint.position = new Vector3(51.1f, -0.7f, 3f);
            manager.preBackgroundPoint2 = new GameObject("Cave Point 2").transform;
            manager.preBackgroundPoint2.position = new Vector3(-63.9f, -0.7f, 3f);
            Call("RequestBackgroundChange");
            for (int frame = 0; frame < 5 && Get<GameObject>("preBackgroundInstance") == null; frame++)
                yield return null;
            GameObject cave = Get<GameObject>("preBackgroundInstance");
            Assert.IsNotNull(cave);
            yield return WaitGameSeconds(7.1f);
            Assert.IsTrue(second.activeSelf);
            Assert.IsTrue(cave != null && cave.activeSelf, "Swapping backgrounds at center must keep Cave alive.");
            Assert.Greater(cave.transform.position.x, -63.9f);

            data = new GameObject("Background game-over check").AddComponent<GameData>();
            data.enabled = false;
            data.gameOver = true;
            yield return WaitGameSeconds(0.2f);
            Assert.IsTrue(cave != null && cave.activeSelf, "Ending the transition coroutine on game over must not delete Cave.");
            Object.Destroy(manager.preBackgroundPoint2.gameObject);
            yield return null;
            Assert.IsTrue(cave != null && cave.activeSelf, "Losing the marker must not count as reaching it.");
            manager.enabled = false;
            Assert.IsTrue(cave.activeSelf, "Cancelling background transitions must not delete Cave.");
            manager.enabled = true;

            cave.transform.position = new Vector3(-63.8f, -0.7f, 3f);
            float deadline = Time.time + 2f;
            while (Get<GameObject>("preBackgroundInstance") != null && Time.time < deadline)
                yield return null;
            Assert.IsNull(Get<GameObject>("preBackgroundInstance"), "The captured Point 2 coordinate still controls deletion.");
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            if (data != null) Object.Destroy(data.gameObject);
        }
    }

    [UnityTest]
    public IEnumerator CaveMustCrossPoint2InsteadOfDisappearingBecauseItStartsOnItsLeft()
    {
        manager.preBackgroundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Cave.prefab");
        manager.preBackgroundPoint = new GameObject("Cave Point").transform;
        manager.preBackgroundPoint.position = new Vector3(-10f, 0f, 3f);
        manager.preBackgroundPoint2 = new GameObject("Cave Point 2").transform;
        manager.preBackgroundPoint2.position = new Vector3(10f, 0f, 3f);
        Call("RequestBackgroundChange");
        for (int frame = 0; frame < 5 && Get<GameObject>("preBackgroundInstance") == null; frame++)
            yield return null;
        GameObject cave = Get<GameObject>("preBackgroundInstance");
        Assert.IsNotNull(cave);
        yield return WaitGameSeconds(0.2f);
        Assert.IsTrue(cave != null && cave.activeSelf, "A one-sided X comparison must not delete Cave at spawn.");
        cave.transform.position = new Vector3(11f, 0f, 3f);
        yield return null;
        Assert.IsNull(Get<GameObject>("preBackgroundInstance"));
    }

    [UnityTest]
    public IEnumerator PrefabBackgroundsAreReusedAndCurrentBackgroundIsNeverSelected()
    {
        manager.backgroundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Background1.prefab");
        GameObject other = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Background2.prefab");
        manager.backgroundPrefabs = new[] { manager.backgroundPrefab, other, other };
        manager.ResetState();
        var instances = Get<Dictionary<GameObject, GameObject>>("backgroundInstances");
        GameObject start = instances[manager.backgroundPrefab];
        for (int i = 0; i < 6; i++)
        {
            GameObject previous = Get<GameObject>("currentBackgroundSource");
            Call("RequestBackgroundChange");
            for (int frame = 0; frame < 5 && Get<GameObject>("currentBackgroundSource") == previous; frame++)
                yield return null;
            GameObject current = Get<GameObject>("currentBackgroundSource");
            Assert.AreNotSame(previous, current);
            Assert.IsFalse(instances[previous].activeSelf);
            Assert.IsTrue(instances[current].activeSelf);
        }
        Assert.AreSame(start, instances[manager.backgroundPrefab]);
        manager.ResetState();
        Assert.IsTrue(start.activeSelf);
        Assert.IsFalse(instances[other].activeSelf);
        Assert.AreEqual(2, Get<List<GameObject>>("backgroundSources").Count);
        Object.Destroy(manager.gameObject);
        yield return null;
        yield return null;
        Assert.IsTrue(start == null, "Manager destruction must release prefab instances.");
    }

    private void Spawn(int stage, GameObject prefab, bool rage = false)
    {
        Get<Dictionary<int, List<GameObject>>>("phaseShuffleByStage")[stage] = new List<GameObject> { prefab };
        if (stage == -1) manager.rageStagePrefabs = new[] { prefab };
        if (stage == 0) manager.testPhasePrefabs = new[] { prefab };
        typeof(StageManager).GetMethod("SpawnPhaseInternal", Private, null,
            new[] { typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
            .Invoke(manager, new object[] { stage, rage, false, false, true });
    }

    private static IEnumerator WaitGameSeconds(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until)
            yield return null;
    }

    private GameObject NextPhase(int stage) => (GameObject)typeof(StageManager)
        .GetMethod("GetNextPhasePrefabForStage", Private, null, new[] { typeof(int) }, null)
        .Invoke(manager, new object[] { stage });

    private object Call(string name) => typeof(StageManager).GetMethod(name, Private).Invoke(manager, null);
    private T Get<T>(string name) => (T)typeof(StageManager).GetField(name, Private).GetValue(manager);
    private void Set(string name, object value) => typeof(StageManager).GetField(name, Private).SetValue(manager, value);
}
#endif
