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

public sealed class SpeedPhasesTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private StageManager manager;
    private GameData data;
    private GameObject normal;
    private speedPhases settings;
    private Random.State randomState;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        randomState = Random.state;
        data = new GameObject("Speed phase game data").AddComponent<GameData>();
        data.enabled = false;
        data.stageSpeedMult = 0.8f;
        manager = new GameObject("Speed phase manager").AddComponent<StageManager>();
        manager.initialPoolSizePerPrefab = 0;
        manager.speedUp1 = 100;
        manager.speedUp2 = 200;
        manager.speedUp3 = 300;
        normal = MakePhase("Normal phase");
        manager.stage1PhasePrefabs = new[] { normal };
        manager.stage2PhasePrefabs = new[] { normal };
        manager.stage3PhasePrefabs = new[] { normal };
        manager.stage4PhasePrefabs = new[] { normal };
        var wrapper = new GameObject("Speed special settings");
        wrapper.SetActive(false);
        settings = wrapper.AddComponent<speedPhases>();
        settings.startPhasePrefab = MakePhase("Speed phase intro");
        settings.endPhasePrefab = MakePhase("Speed phase outro");
        settings.phasePrefabs = new[] { MakePhase("Speed phase A"), MakePhase("Speed phase B") };
        settings.phaseSpawnCount = 3;
        settings.speedMultiplier = 2.5f;
        settings.entryHoldDuration = 0f;
        settings.entryReturnDuration = 0f;
        manager.stage1SpecialPhasePrefabs = new[] { new SpecialPhaseEntry { prefab = wrapper, spawnChance = 1f } };
        Call("InitPools");
        manager.ResetState();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        RageTransformFreezeController.EndActivePause();
        Random.state = randomState;
        if (data != null) Object.Destroy(data.gameObject);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SpeedEntryShowsSpeedModeOnceAndRunScoreAccumulatesAtOneAndAHalfRate()
    {
        Player hero = MakePlayer(out _);
        ArcadeFeedback feedback = hero.GetComponent<ArcadeFeedback>();
        Assert.IsNotNull(typeof(ArcadeFeedback).GetField("speedModePrefab", Private).GetValue(feedback));
        data.defaultStageSpeedMult = data.stageSpeedMult = 0.8f;
        Begin(false);
        AdvanceScore(4);
        Assert.AreEqual(2, data.GetRunScore(), "Waiting for the start detector uses the ordinary score rate.");
        var intro = LastObject.GetComponentInChildren<PhaseEndTrigger>();
        manager.OnPhasePassed(intro);
        Transform root = (Transform)typeof(ArcadeFeedback).GetField("popupRoot", Private).GetValue(feedback);
        Assert.IsNotNull(root);
        ArcadePopup[] popups = root.GetComponentsInChildren<ArcadePopup>();
        Assert.AreEqual(1, popups.Length);
        var configured = (ArcadePopup)typeof(ArcadeFeedback).GetField("speedModePrefab", Private).GetValue(feedback);
        Assert.AreEqual(configured.name + "(Clone)", popups[0].name);
        Assert.AreSame(configured.GetComponentInChildren<SpriteRenderer>().sprite,
            popups[0].GetComponentInChildren<SpriteRenderer>().sprite);
        Assert.IsTrue(popups[0].GetComponentInChildren<SpriteRenderer>().enabled);
        manager.OnPhasePassed(intro);
        Assert.AreEqual(1, root.GetComponentsInChildren<ArcadePopup>().Length);
        RageTransformFreezeController.EndActivePause();
        typeof(GameData).GetField("runScore", Private).SetValue(data, 0);
        typeof(GameData).GetField("runScoreElapsed", Private).SetValue(data, 0f);
        AdvanceScore(4);
        Assert.AreEqual(3, data.GetRunScore(), "The accelerated window scores 1.5 times the ordinary rate.");
        for (int i = 0; i < settings.phaseSpawnCount + 1; i++) PassLastPhase();
        typeof(GameData).GetField("runScore", Private).SetValue(data, 0);
        typeof(GameData).GetField("runScoreElapsed", Private).SetValue(data, 0f);
        AdvanceScore(4);
        Assert.AreEqual(2, data.GetRunScore(), "The normal rate returns after the outro detector.");
        feedback.ResetFeedback();
        manager.ResetState();
        yield break;
    }

    private void AdvanceScore(int ticks)
    {
        for (int i = 0; i < ticks; i++)
            typeof(GameData).GetMethod("UpdateRunScore", Private).Invoke(data, new object[] { 0.2f });
    }

    [UnityTest]
    public IEnumerator DedicatedIntroAlwaysComesFirstAndOnlyItsDetectorContactEnablesSpeed()
    {
        for (int seed = 0; seed < 8; seed++)
        {
            Random.InitState(seed);
            Begin(false);
            Assert.AreSame(settings.startPhasePrefab, Prefabs[LastObject]);
            Assert.AreEqual(0.8f, data.GetStageSpeedMult(), 0.0001f);
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
            manager.OnPhasePassed(normal.GetComponentInChildren<PhaseEndTrigger>(true));
            Assert.AreEqual(1, Active.Count);
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);

            var trigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
            var unrelated = new GameObject("Not a phase detector", typeof(BoxCollider2D));
            trigger.SendMessage("OnTriggerEnter2D", unrelated.GetComponent<Collider2D>());
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);

            var detector = new GameObject("Phase detector", typeof(BoxCollider2D), typeof(Rigidbody2D), typeof(PhaseDetector));
            detector.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            trigger.SendMessage("OnTriggerEnter2D", detector.GetComponent<Collider2D>());
            Assert.AreEqual(2.5f, manager.SpecialPhaseSpeedMultiplier);
            Assert.AreEqual(2f, data.GetStageSpeedMult(), 0.0001f);
            Assert.AreEqual(2, Active.Count);
            CollectionAssert.Contains(settings.phasePrefabs, Prefabs[LastObject]);
            detector.SetActive(false);
            unrelated.SetActive(false);
            Object.Destroy(detector);
            Object.Destroy(unrelated);
            manager.ResetState();
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator DedicatedOutroAlwaysComesLastAndOnlyItsDetectorContactEndsSpeed()
    {
        settings.phaseSpawnCount = 1;
        for (int seed = 0; seed < 8; seed++)
        {
            Random.InitState(seed);
            Begin();
            var randomTrigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
            manager.OnPhasePassed(randomTrigger);
            Assert.AreSame(settings.endPhasePrefab, Prefabs[LastObject]);
            Assert.AreEqual(3, Active.Count, "One intro, one random phase, then the fixed outro.");
            Assert.AreEqual(2.5f, manager.SpecialPhaseSpeedMultiplier);
            manager.OnPhasePassed(randomTrigger);
            Assert.AreEqual(3, Active.Count);

            var trigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
            var unrelated = new GameObject("Not a phase detector", typeof(BoxCollider2D));
            trigger.SendMessage("OnTriggerEnter2D", unrelated.GetComponent<Collider2D>());
            Assert.AreEqual(2.5f, manager.SpecialPhaseSpeedMultiplier);
            var detector = new GameObject("Phase detector", typeof(BoxCollider2D), typeof(Rigidbody2D), typeof(PhaseDetector));
            detector.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            trigger.SendMessage("OnTriggerEnter2D", detector.GetComponent<Collider2D>());
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            Assert.AreEqual(2.5f, manager.SpecialPhaseSpeedMultiplier);
            Assert.AreEqual(2f, data.GetStageSpeedMult(), 0.0001f);
            Assert.AreSame(normal, Prefabs[LastObject]);
            Assert.AreEqual(4, Active.Count);
            AdvanceExitHold(1.5f);
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            Assert.AreEqual(0.8f, data.GetStageSpeedMult(), 0.0001f);
            detector.SetActive(false);
            unrelated.SetActive(false);
            Object.Destroy(detector);
            Object.Destroy(unrelated);
            manager.ResetState();
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator TrainMovesBackDuringSpeedPhaseAndReturnsOnSlowdownAndPhaseEnd()
    {
        Train train = MakeTrain();
        float initialX = train.transform.localPosition.x;
        data.defaultStageSpeedMult = 0.8f;
        settings.phaseSpawnCount = 1;
        Begin(false);
        yield return null;
        Assert.AreEqual(initialX, train.transform.localPosition.x, "The intro must not push the train back before acceleration starts.");
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance * 0.5f);

        var obstacleState = typeof(GameData).GetField("obstacleState", Private);
        obstacleState.SetValue(data, GameData.ObstacleContactState.Stopped);
        data.stageSpeedMult = 0f;
        yield return AssertTrainReachesX(train, initialX);
        obstacleState.SetValue(data, GameData.ObstacleContactState.Recovering);
        data.stageSpeedMult = 0.79f;
        yield return null;
        Assert.AreEqual(initialX, train.transform.localPosition.x, 0.0001f, "Leaving the obstacle must not push back before speed recovers.");
        data.stageSpeedMult = 0.8f;
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance * 0.5f);

        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        yield return null;
        Assert.AreEqual(initialX - train.ragePushBackDistance * 0.5f, train.transform.localPosition.x, 0.0001f,
            "The train stays back through the accelerated outro.");
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        yield return AssertTrainReachesX(train, initialX);
        Assert.AreEqual(2f, train.transform.localPosition.y);
        Assert.AreEqual(3f, train.transform.localPosition.z);
        manager.ResetState();
    }

    [UnityTest]
    public IEnumerator TrainSharesOnePushBackMovementAcrossRageAndSpeedPhases()
    {
        Train train = MakeTrain();
        float initialX = train.transform.localPosition.x;
        GameData.OnRageStart?.Invoke();
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance);
        var movementField = typeof(Train).GetField("rageMovementRoutine", Private);
        object movement = movementField.GetValue(train);
        Begin();
        yield return null;
        Assert.AreSame(movement, movementField.GetValue(train), "Starting a speed phase during rage must reuse the existing movement.");
        GameData.OnRageEnd?.Invoke();
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance * 0.5f);
        manager.ResetState();
        yield return AssertTrainReachesX(train, initialX);

        Begin();
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance * 0.5f);
        float speedPhaseX = train.transform.localPosition.x;
        GameData.OnRageStart?.Invoke();
        Assert.AreEqual(speedPhaseX, train.transform.localPosition.x, "Changing distance must start from the visible position.");
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance);
        manager.ResetState();
        yield return null;
        Assert.AreEqual(initialX - train.ragePushBackDistance, train.transform.localPosition.x, 0.0001f,
            "Ending a speed phase must preserve an ongoing rage movement.");
        GameData.OnRageEnd?.Invoke();
        yield return AssertTrainReachesX(train, initialX);
    }

    [UnityTest]
    public IEnumerator TrainSpeedPhaseMovementResetsOnReinitAndDisableWithoutAccumulatingOffsets()
    {
        Train train = MakeTrain();
        float initialX = train.transform.localPosition.x;
        Begin();
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance * 0.5f);
        train.Reinit();
        Assert.AreEqual(initialX, train.transform.localPosition.x);
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance * 0.5f);
        train.enabled = false;
        Assert.AreEqual(initialX, train.transform.localPosition.x);
        train.enabled = true;
        yield return AssertTrainReachesX(train, initialX - train.ragePushBackDistance * 0.5f);
        manager.ResetState();
        yield return AssertTrainReachesX(train, initialX);
    }

    [UnityTest]
    public IEnumerator ColliderOnlyMoversCannotScrollAheadOfTheirPhaseAtHigherSpeed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/speed Stage/stage 21.prefab");
        Assert.IsNotNull(prefab);
        settings.phasePrefabs = new[] { prefab };
        Begin();
        GameObject phase = LastObject;
        var movers = phase.GetComponentsInChildren<Mover>(true);
        var offsets = new Dictionary<Mover, Vector3>();
        foreach (Mover mover in movers)
        {
            if (mover.transform != phase.transform && mover.GetComponent<Collider2D>() != null &&
                mover.GetComponentInChildren<Renderer>(true) == null)
                offsets[mover] = mover.transform.localPosition;
        }
        Assert.AreEqual(2, offsets.Count, "This real prefab contains two independently scrolling collision edges.");
        float rootX = phase.transform.position.x;
        float until = Time.time + 0.2f;
        while (Time.time < until) yield return null;
        Assert.Less(phase.transform.position.x, rootX, "The entire phase must still scroll at the multiplied speed.");
        foreach (var entry in offsets)
        {
            Assert.AreEqual(entry.Value.x, entry.Key.transform.localPosition.x, 0.0001f,
                "Collision edges must retain their X offset from the visible phase.");
            Assert.AreEqual(phase.transform.position.x + entry.Value.x, entry.Key.GetComponent<Rigidbody2D>().position.x, 0.001f,
                "The collider's physics pose must scroll with the same phase, without an extra Mover offset.");
        }
        manager.ResetState();
    }

    [UnityTest]
    public IEnumerator ExactCountWaitsForFinalTriggerThenRestoresSpeedAndGuaranteesNormal()
    {
        var all = (List<GameObject>)Call("CollectAllPhasePrefabs");
        foreach (GameObject prefab in settings.phasePrefabs) CollectionAssert.Contains(all, prefab);
        CollectionAssert.Contains(all, settings.endPhasePrefab);
        Begin();
        Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
        Assert.AreEqual(2f, data.GetStageSpeedMult(), 0.0001f);
        Assert.AreEqual(2f, data.GetStageSpeedMultIgnoringObstacleSlowdown(), 0.0001f);
        Assert.AreEqual(0.8f, data.stageSpeedMult, "The underlying speed must never be overwritten.");
        Assert.AreEqual(1, manager.GetPhaseSpawnCount(), "The whole special occupies one stage slot.");
        Assert.AreEqual(2, Active.Count);
        Call("FixedUpdate");
        Assert.AreEqual(manager.phaseBaseSpeed * manager.speedMult1 * 2f,
            LastObject.GetComponent<Mover>().baseSpeed, 0.0001f);
        Assert.AreEqual(1f, CallMoverMultiplier(LastObject.GetComponent<Mover>()), "Root Movers must not multiply twice.");

        var firstTrigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
        manager.OnPhasePassed(firstTrigger);
        Assert.AreEqual(3, Active.Count);
        manager.OnPhasePassed(firstTrigger);
        manager.OnPhasePassed();
        Assert.AreEqual(3, Active.Count, "Duplicates and unrelated passes must not advance this sequence.");
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.AreEqual(4, Active.Count);
        Assert.IsTrue(manager.IsSpeedPhaseSequenceActive, "Spawning the last phase does not complete it.");

        // Even a queued special must yield to one normal after the final speed phase.
        Cycles[1] = new List<GameObject> { settings.gameObject };
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.AreEqual(5, Active.Count);
        Assert.AreSame(settings.endPhasePrefab, Prefabs[LastObject]);
        Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
        Assert.AreEqual(2.5f, manager.SpecialPhaseSpeedMultiplier, "Spawning the outro must not end the speed multiplier.");
        var finalTrigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
        manager.OnPhasePassed(finalTrigger);
        Assert.AreEqual(6, Active.Count);
        Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
        Assert.AreEqual(2.5f, manager.SpecialPhaseSpeedMultiplier);
        AdvanceExitHold(1.5f);
        Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
        Assert.AreEqual(0.8f, data.GetStageSpeedMult(), 0.0001f);
        Assert.AreSame(normal, Prefabs[LastObject]);
        Assert.AreEqual(2, manager.GetPhaseSpawnCount());
        manager.OnPhasePassed(finalTrigger);
        Assert.AreEqual(6, Active.Count);
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedChangesComposeWithSlowdownAndMoverAndDoNotLeakAcrossGameOverRetryOrDisable()
    {
        Begin();
        data.stageSpeedMult = 0.4f;
        Assert.AreEqual(1f, data.GetStageSpeedMult(), 0.0001f);
        typeof(GameData).GetField("obstacleState", Private).SetValue(data, GameData.ObstacleContactState.Decelerating);
        data.defaultStageSpeedMult = 0.8f;
        Assert.AreEqual(2f, data.GetStageSpeedMultIgnoringObstacleSlowdown(), 0.0001f);
        var standalone = new GameObject("Standalone mover").AddComponent<Mover>();
        Assert.AreEqual(1f, CallMoverMultiplier(standalone), 0.0001f);
        data.stageSpeedMult = 0.8f;
        Assert.AreEqual(2f, CallMoverMultiplier(standalone), 0.0001f);

        int activeCount = Active.Count;
        var trigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
        data.gameOver = true;
        GameData.OnGameOver?.Invoke();
        Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
        Assert.AreEqual(0.8f, data.GetStageSpeedMult(), 0.0001f);
        manager.OnPhasePassed(trigger);
        Assert.AreEqual(activeCount, Active.Count, "Game over cannot resume spawning.");
        manager.ResetState();
        data.gameOver = false;
        Assert.AreEqual(0, Active.Count);
        Begin();
        manager.enabled = false;
        Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
        Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
        manager.enabled = true;
        manager.ResetState();
        Begin();
        manager.StopStageLoop();
        Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
        Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PauseAndMachineGunHoldTheSequenceWhileRageInterruptsItSafely()
    {
        Begin();
        var trigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
        manager.SetGameplayPause(true);
        manager.OnPhasePassed(trigger);
        Assert.AreEqual(2, Active.Count);
        manager.SetGameplayPause(false);
        Call("HandleMachineGunSequenceStart");
        manager.OnPhasePassed(trigger);
        Assert.AreEqual(2, Active.Count);
        Call("HandleMachineGunSequenceEnd");
        Assert.AreEqual(3, Active.Count);
        Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
        manager.rageStagePrefabs = new[] { normal };
        Call("StartRagePhaseSpawn");
        Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
        Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
        int count = Active.Count;
        manager.OnPhasePassed(trigger);
        Assert.AreEqual(count, Active.Count);
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator RageAtEverySpeedSequenceStepReplaysFromIntroAfterExactlyTwoNormalPasses()
    {
        manager.rageStagePrefabs = new[] { MakePhase("Rage phase") };
        manager.ragePhaseDuration = 0f;
        // Cover the waiting intro, every random phase, and the spawned outro.
        for (int step = 0; step <= settings.phaseSpawnCount + 1; step++)
        {
            manager.ResetState();
            Begin(false);
            for (int i = 0; i < step; i++) PassLastPhase();
            var interruptedTrigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
            StartTestRage();
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            Assert.AreEqual(1, manager.GetPhaseSpawnCount());
            int count = Active.Count;
            manager.OnPhasePassed(interruptedTrigger);
            Assert.AreEqual(count, Active.Count, "Interrupted speed triggers cannot spawn rage or recovery phases.");

            // Ending rage alone cannot skip the final rage phase's detector.
            var rageTrigger = LastObject.GetComponentInChildren<PhaseEndTrigger>();
            data.rageMode = false;
            yield return null;
            yield return null;
            Assert.AreEqual(count, Active.Count);
            yield return FinishTestRage();
            Assert.AreEqual(2, manager.GetPhaseSpawnCount());
            var firstNormal = LastObject.GetComponentInChildren<PhaseEndTrigger>();
            manager.OnPhasePassed();
            manager.OnPhasePassed(normal.GetComponentInChildren<PhaseEndTrigger>(true));
            manager.OnPhasePassed(rageTrigger);
            Assert.AreEqual(2, manager.GetPhaseSpawnCount(), "Only the current recovery normal may advance the replay.");
            PassLastPhase();
            Assert.AreSame(normal, Prefabs[LastObject]);
            Assert.AreEqual(3, manager.GetPhaseSpawnCount());
            count = Active.Count;
            manager.OnPhasePassed(firstNormal);
            Assert.AreEqual(count, Active.Count, "Duplicate recovery passes cannot skip the second normal.");
            PassLastPhase();
            Assert.AreSame(settings.startPhasePrefab, Prefabs[LastObject]);
            Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            Assert.AreEqual(3, manager.GetPhaseSpawnCount(), "Replaying the selected special must not consume another stage slot.");
            PassLastPhase();
            Assert.AreEqual(settings.speedMultiplier, manager.SpecialPhaseSpeedMultiplier);
            for (int i = 0; i < settings.phaseSpawnCount; i++)
            {
                CollectionAssert.Contains(settings.phasePrefabs, Prefabs[LastObject]);
                PassLastPhase();
            }
            Assert.AreSame(settings.endPhasePrefab, Prefabs[LastObject]);
            Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
            PassLastPhase();
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            Assert.AreSame(normal, Prefabs[LastObject]);
            Assert.AreEqual(4, manager.GetPhaseSpawnCount());
        }
    }

    [UnityTest]
    public IEnumerator BossThresholdOnOriginalSpecialOrEitherRecoveryNormalWaitsForReplayedOutro()
    {
        manager.speedUp1 = manager.speedUp2 = 0;
        manager.rageStagePrefabs = new[] { MakePhase("Boss recovery rage phase") };
        manager.ragePhaseDuration = 0f;
        manager.bossResumeDelay = 0.01f;
        var stage4 = MakePhase("Post-boss stage 4 phase");
        manager.stage4PhasePrefabs = new[] { stage4 };
        for (int threshold = 1; threshold <= 3; threshold++)
        {
            manager.ResetState();
            typeof(GameData).GetField("bossStage3Triggered", Private).SetValue(data, false);
            manager.speedUp3 = threshold;
            Cycles[3] = new List<GameObject> { settings.gameObject };
            Call("SpawnPhase");
            StartTestRage();
            yield return FinishTestRage();
            PassLastPhase();
            Assert.AreSame(normal, Prefabs[LastObject]);
            Assert.AreEqual(3, manager.GetPhaseSpawnCount());
            Assert.AreEqual(3, Call("GetCurrentPhaseStage"), "Recovery must not switch to stage 4 before the boss.");
            PassLastPhase();
            Assert.AreSame(settings.startPhasePrefab, Prefabs[LastObject]);
            AssertBossNotStarted();
            Assert.IsFalse(manager.IsStageLoopStopped(), "A deferred boss cannot pause the replay.");
            for (int i = 0; i <= settings.phaseSpawnCount; i++)
            {
                PassLastPhase();
                AssertBossNotStarted();
            }
            Assert.AreSame(settings.endPhasePrefab, Prefabs[LastObject]);
            Assert.AreEqual(3, manager.GetPhaseSpawnCount());
            int count = Active.Count;
            var outro = LastObject.GetComponentInChildren<PhaseEndTrigger>();
            PassLastPhase();
            Assert.IsTrue((bool)typeof(StageManager).GetField("bossRunning", Private).GetValue(manager));
            manager.OnPhasePassed(outro);
            Assert.AreEqual(count, Active.Count, "The boss starts after the outro without spawning an extra normal.");
            float deadline = Time.time + 2f;
            while (!(bool)typeof(StageManager).GetField("postBossMixedPhaseUnlocked", Private).GetValue(manager) && Time.time < deadline)
                yield return null;
            Assert.AreEqual(104, Call("GetCurrentPhaseStage"));
            CollectionAssert.Contains(new[] { normal, stage4 }, Prefabs[LastObject]);
        }
    }

    [UnityTest]
    public IEnumerator RepeatedRageDuringEitherNormalOrReplayedSpeedRestartsTheTwoNormalGap()
    {
        manager.rageStagePrefabs = new[] { MakePhase("Repeated rage phase") };
        manager.ragePhaseDuration = 0f;
        for (int repeatAt = 0; repeatAt < 3; repeatAt++)
        {
            manager.ResetState();
            Begin();
            StartTestRage();
            yield return FinishTestRage();
            if (repeatAt >= 1) PassLastPhase();
            if (repeatAt == 2)
            {
                PassLastPhase();
                PassLastPhase();
            }
            int count = manager.GetPhaseSpawnCount();
            StartTestRage();
            yield return FinishTestRage();
            Assert.AreEqual(count + 1, manager.GetPhaseSpawnCount());
            PassLastPhase();
            Assert.AreSame(normal, Prefabs[LastObject]);
            Assert.AreEqual(count + 2, manager.GetPhaseSpawnCount());
            PassLastPhase();
            Assert.AreSame(settings.startPhasePrefab, Prefabs[LastObject]);
            Assert.AreEqual(count + 2, manager.GetPhaseSpawnCount());
            for (int i = 0; i < settings.phaseSpawnCount + 2; i++) PassLastPhase();
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            Assert.AreSame(normal, Prefabs[LastObject]);
        }
    }

    [UnityTest]
    public IEnumerator BossRequestsDuringRageAndReplayedIntroOrOutroCannotInterruptRecovery()
    {
        manager.rageStagePrefabs = new[] { MakePhase("Deferred boss rage phase") };
        manager.ragePhaseDuration = 0f;
        for (int requestAt = 0; requestAt < 3; requestAt++)
        {
            manager.ResetState();
            Begin();
            StartTestRage();
            if (requestAt == 0) manager.TriggerBossEncounter(3);
            yield return FinishTestRage();
            PassLastPhase();
            PassLastPhase();
            if (requestAt == 1) manager.QueueBossEncounterAfterMachineGun(3);
            AssertBossNotStarted();
            for (int i = 0; i <= settings.phaseSpawnCount; i++) PassLastPhase();
            Assert.AreSame(settings.endPhasePrefab, Prefabs[LastObject]);
            if (requestAt == 2) manager.TriggerBossEncounter(3);
            AssertBossNotStarted();
            PassLastPhase();
            Assert.IsTrue((bool)typeof(StageManager).GetField("bossRunning", Private).GetValue(manager));
        }
    }

    [UnityTest]
    public IEnumerator RecoveryRespectsGameplayAndMachineGunPausesAndCancellationClearsReplayAndBoss()
    {
        manager.rageStagePrefabs = new[] { MakePhase("Paused recovery rage phase") };
        manager.ragePhaseDuration = 0f;
        Begin();
        StartTestRage();
        yield return FinishTestRage();
        manager.SetGameplayPause(true);
        PassLastPhase();
        Assert.AreEqual(2, manager.GetPhaseSpawnCount());
        manager.SetGameplayPause(false);
        Call("HandleMachineGunSequenceStart");
        PassLastPhase();
        Assert.AreEqual(2, manager.GetPhaseSpawnCount());
        Call("HandleMachineGunSequenceEnd");
        Assert.AreEqual(3, manager.GetPhaseSpawnCount());
        PassLastPhase();
        Assert.AreSame(settings.startPhasePrefab, Prefabs[LastObject]);

        for (int cancellation = 0; cancellation < 4; cancellation++)
        {
            manager.ResetState();
            data.gameOver = false;
            Begin();
            StartTestRage();
            manager.TriggerBossEncounter(3);
            yield return FinishTestRage();
            var stale = LastObject.GetComponentInChildren<PhaseEndTrigger>();
            if (cancellation == 0) manager.ResetState();
            else if (cancellation == 1) manager.StopStageLoop();
            else if (cancellation == 2) manager.enabled = false;
            else
            {
                data.gameOver = true;
                GameData.OnGameOver?.Invoke();
            }
            Assert.IsNull(typeof(StageManager).GetField("interruptedSpeedPhaseSettings", Private).GetValue(manager));
            Assert.AreEqual(0, typeof(StageManager).GetField("deferredSpeedPhaseBossStage", Private).GetValue(manager));
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            manager.OnPhasePassed(stale);
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            AssertBossNotStarted();
            manager.enabled = true;
        }
        data.gameOver = false;
        manager.ResetState();
    }

    private void StartTestRage()
    {
        data.rageMode = true;
        GameData.OnRageStart?.Invoke();
    }

    private IEnumerator FinishTestRage()
    {
        data.rageMode = false;
        float deadline = Time.time + 2f;
        while (typeof(StageManager).GetField("currentSpawnMode", Private).GetValue(manager).ToString() != "Cooldown" && Time.time < deadline)
            yield return null;
        Assert.AreEqual("Cooldown", typeof(StageManager).GetField("currentSpawnMode", Private).GetValue(manager).ToString());
        Assert.Greater(Active.Count, 0, "The last rage phase must still be waiting for its detector.");
        PassLastPhase();
        deadline = Time.time + 2f;
        while (Active.Count > 0 && Prefabs[LastObject] != normal && Time.time < deadline)
            yield return null;
        Assert.Greater(Active.Count, 0, "Recovery must retain its current phase.");
        Assert.AreSame(normal, Prefabs[LastObject], "Rage completion must resume with the first recovery normal.");
        AssertBossNotStarted();
    }

    private void PassLastPhase() => manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());

    private void AssertBossNotStarted()
    {
        Assert.IsFalse((bool)typeof(StageManager).GetField("bossRunning", Private).GetValue(manager));
        Assert.IsFalse((bool)typeof(StageManager).GetField("bossTriggered", Private).GetValue(manager));
    }

    [UnityTest]
    public IEnumerator SpeedOverridesResolveEachPlayerAndLeaveRageWalkOverridesUnchanged()
    {
        Player hero = MakePlayer(out var overrides);
        Assert.IsNotNull(overrides.baseSpeed, "The player prefab must reference base_speed.");
        Assert.AreEqual(5, overrides.speedClips.Length);
        Assert.IsNotNull(overrides.baseSpeedUp);
        Assert.IsNotNull(overrides.baseSpeedDown);
        Assert.AreEqual(5, overrides.speedUpClips.Length);
        Assert.AreEqual(5, overrides.speedDownClips.Length);
        var controller = (AnimatorOverrideController)hero.anim.runtimeAnimatorController;
        for (int i = 0; i < 5; i++)
        {
            data.selectedPlayerType = i + 1;
            overrides.ApplyOverrides(true);
            Assert.AreEqual($"player{i + 1}_speed", controller[overrides.baseSpeed].name);
            Assert.AreEqual($"player{i + 1}_speed_up", controller[overrides.baseSpeedUp].name);
            Assert.AreEqual($"player{i + 1}_speed_down", controller[overrides.baseSpeedDown].name);
            overrides.speedClips[i] = overrides.jumpClips[i];
            overrides.ApplyOverrides(true);
            Assert.AreSame(overrides.jumpClips[i], controller[overrides.baseSpeed]);
            Assert.AreSame(overrides.speedStartClips[i], controller[overrides.baseSpeedStart]);
            overrides.SetRageMode(true);
            Assert.AreSame(overrides.rageWalkClips[i], controller[overrides.baseWalk]);
            Assert.AreSame(overrides.baseSpeed, controller[overrides.baseSpeed], "There is no rage speed override.");
            Assert.AreSame(overrides.baseSpeedUp, controller[overrides.baseSpeedUp]);
            Assert.AreSame(overrides.baseSpeedDown, controller[overrides.baseSpeedDown]);
            overrides.SetRageMode(false);
            overrides.speedClips[i] = null;
            overrides.ApplyOverrides(true);
            Assert.AreSame(overrides.jumpClips[i], controller[overrides.baseSpeed], "An empty speed slot keeps the correct player's jump art.");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedAirVelocityThresholdsKeepAllThreePosesInJumpGameplayState()
    {
        Player hero = MakePlayer(out _);
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        Begin();
        Play(hero, "ChangeAnimation", "Jump");
        float[] velocities = { 0f, 0.499f, 0.5f, 3f, 0.1f, -0.499f, -0.5f, -3f, 0f };
        string[] poses = { "Speed", "Speed", "Speed Up", "Speed Up", "Speed", "Speed", "Speed Down", "Speed Down", "Speed" };
        for (int i = 0; i < velocities.Length; i++)
        {
            hero.rb.linearVelocity = new Vector2(0f, velocities[i]);
            RawPlay(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base " + poses[i]), $"Y velocity {velocities[i]}");
            Assert.AreEqual("Jump", typeof(Player).GetField("currentAnim", Private).GetValue(hero));
            Assert.IsTrue((bool)typeof(Player).GetMethod("IsAnimatorShowingLogicalState", Private).Invoke(hero, new object[] { "Jump" }));
            Assert.IsFalse((bool)typeof(Player).GetMethod("IsFlyattackStateActive", Private).Invoke(hero, null));
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator SpeedAirPosesSurviveTheFullPlayerUpdateForEveryCharacter()
    {
        Player hero = MakePlayer(out var overrides);
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        Begin();
        Play(hero, "ChangeAnimation", "Jump");
        for (int type = 1; type <= 5; type++)
        {
            data.selectedPlayerType = type;
            overrides.ApplyOverrides(true);
            typeof(Player).GetField("stats", Private).SetValue(hero, StatsManager.GetPlayerStats(type, 1));
            if (type == 2 || type == 4)
            {
                // Recover from the real ranged attack, including its airborne latch.
                var attack = (IEnumerator)typeof(Player).GetMethod(type == 2 ? "P2_FireRanged" : "P4_FireLightning", Private)
                    .Invoke(hero, null);
                attack.MoveNext();
                hero.anim.Update(0f);
                Play(hero, "ChangeAnimation", "Jump");
                Assert.IsFalse((bool)typeof(Player).GetField("isAttacking", Private).GetValue(hero),
                    "Jump must release Flyattack before the speed intro consumes the animation request.");
                Assert.IsNull(typeof(Player).GetField("rangedFlyattackLatchRoutine", Private).GetValue(hero));
            }
            foreach (float velocity in new[] { 0f, 2f, 0f, -2f, 0f })
            {
                string pose = velocity > 0f ? "Speed Up" : velocity < 0f ? "Speed Down" : "Speed";
                for (int frame = 0; frame < 4; frame++)
                {
                    hero.rb.linearVelocity = new Vector2(0f, velocity);
                    typeof(Player).GetMethod("Update", Private).Invoke(hero, null);
                    hero.anim.Update(1f / 60f);
                    Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base " + pose),
                        $"Player {type}, Y {velocity}, frame {frame}");
                }
                AnimationClip expected = velocity > 0f ? overrides.speedUpClips[type - 1] :
                    velocity < 0f ? overrides.speedDownClips[type - 1] : overrides.speedClips[type - 1];
                Assert.AreSame(expected, hero.anim.GetCurrentAnimatorClipInfo(0)[0].clip);
            }
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator SpeedAirReversalsHoldLevelForPointThreeSecondsInBothDirections()
    {
        Player hero = MakePlayer(out _);
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        Begin();
        Play(hero, "ChangeAnimation", "Jump");
        foreach (float velocity in new[] { 2f, -2f })
        {
            hero.anim.Play(velocity > 0 ? "Base Speed Down" : "Base Speed Up", 0, 0.4f);
            hero.anim.Update(0f);
            hero.rb.linearVelocity = new Vector2(0f, velocity);
            float started = Time.time;
            RawPlay(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
            while (Time.time < started + 0.3f)
            {
                RawPlay(hero, "SyncSpeedPhaseAirAnimation");
                Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"), "The neutral bridge must last the full 0.3 seconds.");
                yield return null;
            }
            RawPlay(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName(velocity > 0 ? "Base Speed Up" : "Base Speed Down"));
        }
    }

    [UnityTest]
    public IEnumerator SpeedAirSwitchesPreserveFramePositionForEveryCharacter()
    {
        Player hero = MakePlayer(out var overrides);
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        Begin();
        Play(hero, "ChangeAnimation", "Jump");
        var controller = (AnimatorOverrideController)hero.anim.runtimeAnimatorController;
        for (int i = 0; i < 5; i++)
        {
            data.selectedPlayerType = i + 1;
            overrides.ApplyOverrides(true);
            // Use populated, equal-length timelines even when the authored directional clips are still empty.
            controller[overrides.baseSpeedUp] = overrides.speedClips[i];
            controller[overrides.baseSpeedDown] = overrides.speedClips[i];
            foreach (float phase in new[] { 0.17f, 0.53f, 0.91f, 2.53f })
            {
                hero.anim.Play("Base Speed", 0, phase);
                hero.anim.Update(0f);
                Sprite frame = hero.GetComponent<SpriteRenderer>().sprite;
                foreach (float velocity in new[] { 2f, 0f, -2f, 0f })
                {
                    hero.rb.linearVelocity = new Vector2(0f, velocity);
                    typeof(Player).GetMethod("SyncSpeedPhaseAirAnimation", Private).Invoke(hero, null);
                    hero.anim.Update(0f);
                    Assert.AreEqual(Mathf.Repeat(phase, 1f), Mathf.Repeat(hero.anim.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f), 0.0001f);
                    Assert.AreSame(frame, hero.GetComponent<SpriteRenderer>().sprite, "A pose switch must keep the matching sprite frame.");
                }
            }
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator SpeedAirBridgeCancelsWhenVelocityChangesOrAnotherActionStarts()
    {
        Player hero = MakePlayer(out _);
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        Begin();
        Play(hero, "ChangeAnimation", "Jump");
        hero.anim.Play("Base Speed Up", 0, 0.4f);
        hero.anim.Update(0f);
        hero.rb.linearVelocity = new Vector2(0f, -2f);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        hero.rb.linearVelocity = new Vector2(0f, 2f);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Up"));
        hero.rb.linearVelocity = new Vector2(0f, -2f);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        hero.anim.SetTrigger("Speed Up");
        hero.anim.SetTrigger("Speed Down");
        RawPlay(hero, "ChangeAnimation", "Flyattack");
        yield return new WaitForSeconds(0.35f);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Flyattack"));
        Assert.IsFalse((bool)typeof(Player).GetField("speedAirBridgeActive", Private).GetValue(hero));
        Play(hero, "ChangeAnimation", "Jump");
        manager.ResetState();
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
    }

    [UnityTest]
    public IEnumerator SpeedJumpTracksTheWindowWhileWalkFlyattackAndGroundAttackStayUnchanged()
    {
        Player hero = MakePlayer(out _);
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        typeof(Player).GetField("isAttacking", Private).SetValue(hero, true);
        settings.phaseSpawnCount = 1;
        Begin(false);
        Play(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.IsTrue(manager.IsSpeedPhaseSpeedActive);
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        Assert.AreEqual("Jump", typeof(Player).GetField("currentAnim", Private).GetValue(hero));
        Assert.IsTrue((bool)typeof(Player).GetField("isAttacking", Private).GetValue(hero));
        Play(hero, "ChangeAnimation", "Flyattack");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Flyattack"));
        Play(hero, "ForceAnimationState", "Base Jump", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        Play(hero, "ChangeAnimation", "Walk");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Walk"));
        Play(hero, "ChangeAnimation", "Attack");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Attack"));
        Play(hero, "ForceAnimationState", "Base Jump", "Jump");
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.AreSame(settings.endPhasePrefab, Prefabs[LastObject]);
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        AdvanceExitHold(1.5f);
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        Assert.IsTrue((bool)typeof(Player).GetField("isAttacking", Private).GetValue(hero));

        Begin();
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        data.gameOver = true;
        GameData.OnGameOver?.Invoke();
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedVisualKeepsJumpGameplayStateForAllFivePlayers()
    {
        Player hero = MakePlayer(out var overrides);
        Begin();
        for (int type = 1; type <= 5; type++)
        {
            data.selectedPlayerType = type;
            overrides.ApplyOverrides(true);
            typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
            typeof(Player).GetField("isAttacking", Private).SetValue(hero, false);
            Play(hero, "ChangeAnimation", "Jump");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
            Assert.AreEqual("Jump", typeof(Player).GetField("currentAnim", Private).GetValue(hero));
            Assert.IsFalse((bool)typeof(Player).GetMethod("IsFlyattackStateActive", Private).Invoke(hero, null), "The speed jump must not become an air attack.");
            Assert.IsFalse((bool)typeof(Player).GetField("isAttacking", Private).GetValue(hero));
        }
        manager.ResetState();
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator RageNeverUsesSpeedTriggerAndQueuedSpeedCannotOverrideAnotherAction()
    {
        Player hero = MakePlayer(out var overrides);
        Begin();
        Play(hero, "ChangeAnimation", "Walk");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Walk"));
        Play(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        hero.anim.SetTrigger("Speed");
        Play(hero, "ChangeAnimation", "Flyattack");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Flyattack"));

        typeof(Player).GetField("isRageMode", Private).SetValue(hero, true);
        overrides.SetRageMode(true);
        Play(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        Play(hero, "ChangeAnimation", "Speed");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        var controller = (AnimatorOverrideController)hero.anim.runtimeAnimatorController;
        Assert.AreSame(overrides.rageWalkClips[0], controller[overrides.baseWalk]);
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedEffectsUseConfiguredSpeedAndPlayerSpawnSettingsDuringTheAcceleratedWindow()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(27f, -3.2f), new Vector2(27f, 4f) };
        hero.speedEffectMoveSpeed = 26f;
        settings.speedEffectMoveSpeed = 13f;
        hero.speedEffectSpawnInterval = 0.6f;
        hero.speedEffectDespawnX = -20f;
        settings.speedEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/SpeedEffect.prefab");
        settings.phaseSpawnCount = 1;
        Begin(false);
        Assert.AreEqual(0, Effects.Count);
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.AreEqual(2, Effects.Count);
        float effectSpawnMultiplier = data.GetStageSpeedMult();
        for (int i = 0; i < Effects.Count; i++)
        {
            Assert.AreEqual((Vector3)hero.speedEffectSpawnWorldPositions[i], Effects[i].transform.position);
            Assert.AreEqual(settings.speedEffectPrefab.transform.localScale, Effects[i].transform.localScale);
            Assert.AreEqual(Quaternion.identity, Effects[i].transform.rotation);
        }
        float nextSpawn = (float)typeof(StageManager).GetField("nextSpeedPhaseEffectSpawnTime", Private).GetValue(manager);
        Assert.Greater(nextSpawn, Time.time);
        Assert.LessOrEqual(nextSpawn, Time.time + hero.speedEffectSpawnInterval + 0.0001f,
            "The configured interval is shortened when needed to keep neighboring strips connected.");
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(2, Effects.Count, "The player's configured spawn interval must be respected.");

        speedEffect first = Effects[0];
        float firstX = first.transform.position.x;
        typeof(speedEffect).GetMethod("Update", Private).Invoke(first, null);
        Assert.AreEqual(firstX - settings.speedEffectMoveSpeed * effectSpawnMultiplier * Time.deltaTime,
            first.transform.position.x, 0.0001f);
        Assert.AreEqual(26f, hero.speedEffectMoveSpeed, "The phase's speed setting must not change rage effect tuning.");
        Assert.AreEqual(hero.speedEffectSpawnWorldPositions[0].y, first.transform.position.y);
        first.transform.position = new Vector3(hero.speedEffectDespawnX - 1f, first.transform.position.y, 0f);
        typeof(speedEffect).GetMethod("Update", Private).Invoke(first, null);
        Assert.IsFalse(first.gameObject.activeSelf, "Effects return to the pool at the same despawn X as rage effects.");
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(1, Effects.Count);

        typeof(StageManager).GetField("nextSpeedPhaseEffectSpawnTime", Private).SetValue(manager, Time.time);
        manager.SetGameplayPause(true);
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(1, Effects.Count);
        manager.SetGameplayPause(false);
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(3, Effects.Count, "A resumed due wave uses the same two player spawn positions.");
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.AreSame(settings.endPhasePrefab, Prefabs[LastObject]);
        Assert.AreEqual(3, Effects.Count, "Effects continue through the fixed outro.");
        var active = Effects.ToArray();
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.AreEqual(active.Length, Effects.Count);
        foreach (speedEffect effect in active) Assert.IsTrue(effect.gameObject.activeSelf, "A normal ending only stops emission.");
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(active.Length, Effects.Count, "No new waves may spawn after the speed phase ends.");
        foreach (speedEffect effect in active)
        {
            float x = effect.transform.position.x;
            typeof(speedEffect).GetMethod("Update", Private).Invoke(effect, null);
            Assert.AreEqual(x - settings.speedEffectMoveSpeed * effectSpawnMultiplier * Time.deltaTime,
                effect.transform.position.x, 0.0001f);
            effect.transform.position = new Vector3(hero.speedEffectDespawnX - 1f, effect.transform.position.y, 0f);
            typeof(speedEffect).GetMethod("Update", Private).Invoke(effect, null);
            Assert.IsFalse(effect.gameObject.activeSelf);
        }
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(0, Effects.Count);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedEffectsKeepSpawningAndFadeDuringObstacleSlowdownThenBecomeVisibleAtNormalSpeed()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1000f, -3.2f), new Vector2(1000f, 4f) };
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        settings.speedEffectFadeDuration = 0f;
        data.defaultStageSpeedMult = 0.8f;
        Begin();
        Assert.AreEqual(2, Effects.Count);
        Assert.AreEqual(2f, data.GetStageSpeedMult(), 0.0001f);
        var stateField = typeof(GameData).GetField("obstacleState", Private);
        var nextSpawnField = typeof(StageManager).GetField("nextSpeedPhaseEffectSpawnTime", Private);
        foreach (var state in new[]
        {
            GameData.ObstacleContactState.Decelerating, GameData.ObstacleContactState.Pushing,
            GameData.ObstacleContactState.Stopped, GameData.ObstacleContactState.Recovering
        })
        {
            stateField.SetValue(data, state);
            foreach (float speed in new[] { 0.4f, 0f, -0.2f, 0.79f })
            {
                data.stageSpeedMult = speed;
                nextSpawnField.SetValue(manager, Time.time);
                Assert.IsFalse(data.CanSpawnSpeedEffects(), "Compare with the multiplied normal speed, not a hardcoded speed of 1.");
                int previousCount = Effects.Count;
                Call("TrySpawnSpeedPhaseEffects");
                Assert.AreEqual(previousCount + 2, Effects.Count, "Slowdown keeps spawning waves, including during recovery.");
                foreach (speedEffect effect in Effects)
                {
                    Assert.IsTrue(effect.gameObject.activeSelf);
                    Assert.AreEqual(0f, effect.GetComponent<SpriteRenderer>().color.a,
                        "Both existing and newly spawned effects must inherit the fade without flashing.");
                    float x = effect.transform.position.x;
                    typeof(speedEffect).GetMethod("Update", Private).Invoke(effect, null);
                    Assert.AreEqual(x - settings.speedEffectMoveSpeed * 2f * Time.deltaTime,
                        effect.transform.position.x, 0.0001f, "Existing effects retain their spawn speed through slowdown, stop, reverse and recovery.");
                }
            }
        }
        data.stageSpeedMult = 0.8f;
        Assert.IsTrue(data.CanSpawnSpeedEffects());
        int fadedCount = Effects.Count;
        nextSpawnField.SetValue(manager, Time.time);
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(fadedCount + 2, Effects.Count);
        foreach (speedEffect effect in Effects)
            Assert.AreEqual(1f, effect.GetComponent<SpriteRenderer>().color.a, "Normal speed restores visibility.");
        manager.ResetState();

        // The same gate applies to Player's rage speed effects, with rage's own normal-speed baseline.
        typeof(Player).GetField("isRageMode", Private).SetValue(hero, true);
        data.rageMode = true;
        stateField.SetValue(data, GameData.ObstacleContactState.Decelerating);
        data.stageSpeedMult = 0.6f;
        Play(hero, "TrySpawnRageSpeedEffect");
        var activeByTag = (Dictionary<string, HashSet<GameObject>>)typeof(ObjectPool)
            .GetField("activeByTag", Private).GetValue(ObjectPool.Instance);
        Assert.IsTrue(!activeByTag.TryGetValue(hero.speedEffectPoolTag, out var rageEffects) || rageEffects.Count == 0);
        data.stageSpeedMult = data.defaultStageSpeedMult * data.rageSpeedFactor;
        Play(hero, "TrySpawnRageSpeedEffect");
        Assert.AreEqual(2, activeByTag[hero.speedEffectPoolTag].Count);
        data.gameOver = true;
        Assert.IsFalse(data.CanSpawnSpeedEffects());
        GameData.OnGameOver?.Invoke();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedEffectFadeIsGradualReversibleAndInheritedByNewWaves()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1000f, 4f) };
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        Assert.AreEqual(1f, settings.speedEffectFadeDuration, "The inspector default is one second.");
        settings.speedEffectFadeDuration = Time.deltaTime * 4f;
        data.defaultStageSpeedMult = 0.8f;
        Begin();
        var first = Effects[0].GetComponent<SpriteRenderer>();
        typeof(GameData).GetField("obstacleState", Private).SetValue(data, GameData.ObstacleContactState.Stopped);
        data.stageSpeedMult = 0f;
        typeof(StageManager).GetField("nextSpeedPhaseEffectSpawnTime", Private).SetValue(manager, Time.time);
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(0.75f, first.color.a, 0.0001f);
        Assert.AreEqual(first.color.a, Effects[1].GetComponent<SpriteRenderer>().color.a, 0.0001f);
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(0.5f, first.color.a, 0.0001f, "Fade progresses even between spawn intervals.");
        data.stageSpeedMult = 0.8f;
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(0.75f, first.color.a, 0.0001f, "Recovery reverses a fade already in progress.");
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(1f, first.color.a, 0.0001f);
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedPhaseEndFadesMovingEffectsAndPoolReuseRestoresTheirOriginalAlpha()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1000f, 4f) };
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        settings.speedEffectFadeDuration = Time.deltaTime * 4f;
        settings.phaseSpawnCount = 1;
        Begin();
        speedEffect effect = Effects[0];
        var sprite = effect.GetComponent<SpriteRenderer>();
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        float x = effect.transform.position.x;
        typeof(speedEffect).GetMethod("Update", Private).Invoke(effect, null);
        Assert.Less(effect.transform.position.x, x, "Ending the phase must not stop movement.");
        Assert.AreEqual(0.75f, sprite.color.a, 0.0001f);
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(1, Effects.Count, "The completed phase must stop emission.");

        Begin();
        Assert.AreEqual(0.75f, sprite.color.a, 0.0001f, "A new phase cannot restore the previous phase's fading effects.");
        for (int i = 0; i < 3; i++)
            typeof(speedEffect).GetMethod("Update", Private).Invoke(effect, null);
        Assert.AreEqual(0f, sprite.color.a, 0.0001f);
        manager.ResetState();
        Begin();
        foreach (speedEffect reused in Effects)
            Assert.AreEqual(1f, reused.GetComponent<SpriteRenderer>().color.a, "Pool reuse resets opacity and pending fades.");
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedPhaseObstacleContactDropsToHalfAndThenRecoversToNormal()
    {
        data.defaultStageSpeedMult = data.stageSpeedMult = 0.8f;
        data.decelerateTime = 0.05f;
        data.recoverTime = 0.05f;
        Begin();
        float normalSpeed = data.GetStageSpeedMult();
        Assert.AreEqual(2f, normalSpeed, 0.0001f);
        data.BeginObstacleContact();
        typeof(GameData).GetField("lastObstacleTouchTime", Private).SetValue(data, -100f);
        var routine = typeof(GameData).GetField("obstacleRoutine", Private);
        var state = typeof(GameData).GetField("obstacleState", Private);
        bool contactReleased = false;
        float minimum = normalSpeed;
        float deadline = Time.time + 2f;
        while (routine.GetValue(data) != null && Time.time < deadline)
        {
            float speed = data.GetStageSpeedMult();
            minimum = Mathf.Min(minimum, speed);
            Assert.GreaterOrEqual(speed, normalSpeed * 0.5f - 0.0001f);
            Assert.LessOrEqual(speed, normalSpeed + 0.0001f);
            if (!contactReleased && (GameData.ObstacleContactState)state.GetValue(data) == GameData.ObstacleContactState.Pushing)
            {
                data.EndObstacleContact();
                contactReleased = true;
                Assert.AreEqual(GameData.ObstacleContactState.Pushing, state.GetValue(data));
                Assert.IsFalse(data.CanSpawnSpeedEffects(), "Leaving the collider must not resume effects before speed recovery.");
            }
            yield return null;
        }
        Assert.IsNull(routine.GetValue(data), "Contact slowdown must finish its recovery.");
        Assert.IsTrue(contactReleased);
        Assert.AreEqual(normalSpeed * 0.5f, minimum, 0.0001f);
        Assert.AreEqual(normalSpeed, data.GetStageSpeedMult(), 0.0001f);
        Assert.AreEqual(0.8f, data.stageSpeedMult, 0.0001f);
        manager.ResetState();
    }

    [UnityTest]
    public IEnumerator NormalContactsKeepTheirOriginalSlowdownAndSpeedFloorDoesNotSurviveThePhaseOrGameOver()
    {
        data.defaultStageSpeedMult = data.stageSpeedMult = 0.8f;
        data.decelerateTime = data.recoverTime = 0.05f;
        data.pushSpeed = 0.3f;
        data.BeginObstacleContact();
        typeof(GameData).GetField("lastObstacleTouchTime", Private).SetValue(data, -100f);
        data.EndObstacleContact();
        var routine = typeof(GameData).GetField("obstacleRoutine", Private);
        float minimum = 0.8f;
        float deadline = Time.time + 2f;
        while (routine.GetValue(data) != null && Time.time < deadline)
        {
            minimum = Mathf.Min(minimum, data.GetStageSpeedMult());
            yield return null;
        }
        Assert.IsNull(routine.GetValue(data));
        Assert.AreEqual(data.pushSpeed / manager.phaseBaseSpeed, minimum, 0.0001f,
            "Ordinary stages retain the existing push-speed setting without a half-speed floor.");
        Assert.AreEqual(0.8f, data.GetStageSpeedMult(), 0.0001f);

        Begin();
        var state = typeof(GameData).GetField("obstacleState", Private);
        foreach (var contactState in new[]
        {
            GameData.ObstacleContactState.Decelerating, GameData.ObstacleContactState.Pushing,
            GameData.ObstacleContactState.Stopped, GameData.ObstacleContactState.Recovering
        })
        {
            state.SetValue(data, contactState);
            data.stageSpeedMult = 0f;
            Assert.AreEqual(1f, data.GetStageSpeedMult(), 0.0001f);
        }
        while (manager.IsSpeedPhaseSequenceActive)
            manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.AreEqual(0f, data.GetStageSpeedMult(), "The half-speed floor only belongs to the active speed phase.");
        data.stageSpeedMult = 0.8f;
        Begin();
        data.stageSpeedMult = 0f;
        data.gameOver = true;
        Assert.AreEqual(0f, data.GetStageSpeedMult(), "Game over must still allow the stage to stop completely.");
        GameData.OnGameOver?.Invoke();
    }

    [UnityTest]
    public IEnumerator ExistingSpeedEffectsReachTheEndEvenWhenStageSpeedIsClampedToHalf()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1f, -3.2f), new Vector2(1f, 4f) };
        hero.speedEffectDespawnX = 0f;
        hero.speedEffectSpawnInterval = 10f;
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        settings.speedEffectMoveSpeed = 20f;
        data.defaultStageSpeedMult = 0.8f;
        Begin();
        var spawned = Effects.ToArray();
        Assert.AreEqual(2, spawned.Length);
        typeof(GameData).GetField("obstacleState", Private).SetValue(data, GameData.ObstacleContactState.Stopped);
        data.stageSpeedMult = 0f;
        typeof(StageManager).GetField("nextSpeedPhaseEffectSpawnTime", Private).SetValue(manager, Time.time);
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(4, Effects.Count, "Slowdown continues emission while existing effects keep moving.");
        float deadline = Time.time + 0.5f;
        while (Effects.Count > 0 && Time.time < deadline) yield return null;
        Assert.AreEqual(0, Effects.Count, "Stage slowdown must not strand existing effects on screen.");
        foreach (speedEffect effect in spawned) Assert.IsFalse(effect.gameObject.activeSelf);
        Assert.AreEqual(1f, data.GetStageSpeedMult(), 0.0001f, "During speed phases, an obstacle's zero-speed request is clamped to half of the normal speed of 2.");

        data.stageSpeedMult = 0.8f;
        typeof(StageManager).GetField("nextSpeedPhaseEffectSpawnTime", Private).SetValue(manager, Time.time);
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(2, Effects.Count);
        manager.ResetState();
        settings.speedMultiplier = 1.25f;
        Begin();
        speedEffect reused = Effects[0];
        float x = reused.transform.position.x;
        typeof(speedEffect).GetMethod("Update", Private).Invoke(reused, null);
        Assert.AreEqual(x - settings.speedEffectMoveSpeed * 1f * Time.deltaTime, reused.transform.position.x, 0.0001f,
            "Pool reuse must capture the new spawn speed rather than retaining the previous run's multiplier.");
        manager.ResetState();
    }

    [UnityTest]
    public IEnumerator CompletedSpeedPhaseEffectsReachTheirEndpointNaturallyAndRetryClearsRemainingEffects()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1f, -3.2f), new Vector2(1f, 4f) };
        hero.speedEffectDespawnX = 0f;
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        settings.speedEffectMoveSpeed = 20f;
        settings.phaseSpawnCount = 1;
        Begin();
        var effects = Effects.ToArray();
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        foreach (speedEffect effect in effects) Assert.IsTrue(effect.gameObject.activeSelf);
        float deadline = Time.time + 0.5f;
        while (Effects.Count > 0 && Time.time < deadline) yield return null;
        Assert.AreEqual(0, Effects.Count);
        foreach (speedEffect effect in effects) Assert.IsFalse(effect.gameObject.activeSelf);

        Begin();
        effects = Effects.ToArray();
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.AreEqual(2, Effects.Count);
        manager.ResetState();
        Assert.AreEqual(0, Effects.Count);
        foreach (speedEffect effect in effects) Assert.IsFalse(effect.gameObject.activeSelf);
    }

    [UnityTest]
    public IEnumerator RageSpeedEffectsFadeOnRageEndAndNewRageWavesStartVisible()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1000f, 4f) };
        Assert.AreEqual(1f, hero.speedEffectFadeDuration);
        hero.speedEffectFadeDuration = Time.deltaTime * 4f;
        var rageEffects = (List<speedEffect>)typeof(Player).GetField("activeRageSpeedEffects", Private).GetValue(hero);
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        Begin();
        var phaseSprite = Effects[0].GetComponent<SpriteRenderer>();

        foreach (bool pooled in new[] { true, false })
        {
            hero.useEffectPool = pooled;
            hero.OnRageModeChanged(true);
            Play(hero, "TrySpawnRageSpeedEffect");
            Assert.AreEqual(1, rageEffects.Count);
            speedEffect effect = rageEffects[0];
            var sprite = effect.GetComponent<SpriteRenderer>();
            hero.OnRageModeChanged(false);
            Assert.IsTrue(effect.gameObject.activeSelf, "Rage end must fade existing effects instead of immediately returning them.");
            Play(hero, "TrySpawnRageSpeedEffect");
            Assert.AreEqual(1, rageEffects.Count, "Rage end stops emission.");
            float x = effect.transform.position.x;
            typeof(speedEffect).GetMethod("Update", Private).Invoke(effect, null);
            Assert.Less(effect.transform.position.x, x);
            Assert.AreEqual(0.75f, sprite.color.a, 0.0001f);
            Assert.AreEqual(1f, phaseSprite.color.a, "Ending rage must not fade speed-phase effects.");

            hero.OnRageModeChanged(true);
            Play(hero, "TrySpawnRageSpeedEffect");
            Assert.AreEqual(2, rageEffects.Count);
            Assert.AreEqual(1f, rageEffects[1].GetComponent<SpriteRenderer>().color.a);
            Assert.AreEqual(0.75f, sprite.color.a, 0.0001f, "Starting rage again must not restore older fading effects.");
            for (int i = 0; i < 3; i++)
                typeof(speedEffect).GetMethod("Update", Private).Invoke(effect, null);
            Assert.AreEqual(0f, sprite.color.a, 0.0001f);
            Play(hero, "StopRageSpeedEffect", true);
            Assert.AreEqual(0, rageEffects.Count);
        }
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator RageSpeedEffectsAreClearedOnResetDisableAndGameOver()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1000f, 4f) };
        var rageEffects = (List<speedEffect>)typeof(Player).GetField("activeRageSpeedEffects", Private).GetValue(hero);
        foreach (bool pooled in new[] { true, false })
        {
            hero.useEffectPool = pooled;
            for (int cancellation = 0; cancellation < 3; cancellation++)
            {
                hero.OnRageModeChanged(true);
                Play(hero, "TrySpawnRageSpeedEffect");
                speedEffect effect = rageEffects[rageEffects.Count - 1];
                hero.OnRageModeChanged(false);
                if (cancellation == 0)
                {
                    hero.ResetPlayer();
                    hero.StopAllCoroutines();
                    hero.rb.simulated = false;
                }
                else if (cancellation == 1)
                {
                    hero.enabled = true;
                    hero.enabled = false;
                }
                else
                {
                    data.gameOver = true;
                    GameData.OnGameOver?.Invoke();
                    yield return null; // Unpooled effects use Unity's deferred destruction.
                }
                Assert.IsTrue(effect == null || !effect.gameObject.activeSelf);
                data.gameOver = false;
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator EndingSpeedPhaseEffectsDoesNotReturnRageEffectsFromTheirOwnPool()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        var rageEffects = new List<speedEffect>();
        hero.SpawnSpeedEffectWave(hero.speedEffectPrefab, hero.speedEffectPoolTag, rageEffects);
        Assert.AreEqual(2, rageEffects.Count);
        Begin();
        Assert.AreEqual(2, Effects.Count);
        var ownEffects = Effects.ToArray();
        manager.ResetState();
        foreach (speedEffect effect in ownEffects) Assert.IsFalse(effect.gameObject.activeSelf);
        foreach (speedEffect effect in rageEffects) Assert.IsTrue(effect.gameObject.activeSelf);
        foreach (speedEffect effect in rageEffects) effect.DespawnNow();
        yield return null;
    }

    [UnityTest]
    public IEnumerator UnpooledSpeedEffectsAreRemovedOnResetGameOverAndDisable()
    {
        Player hero = MakePlayer(out _);
        hero.useEffectPool = false;
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        for (int cancellation = 0; cancellation < 3; cancellation++)
        {
            Begin();
            var active = Effects.ToArray();
            Assert.AreEqual(2, active.Length);
            if (cancellation == 0) manager.ResetState();
            else if (cancellation == 1)
            {
                data.gameOver = true;
                GameData.OnGameOver?.Invoke();
            }
            else manager.enabled = false;
            Assert.AreEqual(0, Effects.Count);
            foreach (speedEffect effect in active) Assert.IsTrue(effect == null || !effect.gameObject.activeSelf);
            yield return null;
            foreach (speedEffect effect in active) Assert.IsTrue(effect == null);
            data.gameOver = false;
            manager.enabled = true;
            manager.ResetState();
        }
    }

    [UnityTest]
    public IEnumerator SpeedStartFinishesBeforeLocomotionAndPhaseEndKeepsSpeedUntilHoldExpires()
    {
        Player hero = MakePlayer(out _);
        settings.phaseSpawnCount = 1;
        Play(hero, "ChangeAnimation", "Walk");
        Begin(false);
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));
        RawPlay(hero, "ChangeAnimation", "Walk");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));
        RawPlay(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));
        FinishSpeedTransition(hero);
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        for (int jump = 0; jump < 2; jump++)
        {
            RawPlay(hero, "ChangeAnimation", "Walk");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Walk"));
            if (jump == 0)
                RawPlay(hero, "ChangeAnimation", "Jump");
            else
                RawPlay(hero, "ForceAnimationState", "Base Jump", "Jump");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"), "Every new speed jump must play its intro first.");
            RawPlay(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));
            FinishSpeedTransition(hero);
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
            RawPlay(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"), "An ongoing jump must not restart its intro.");
        }
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        RawPlay(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        AdvanceExitHold(1.49f);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        AdvanceExitHold(0.01f);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        RawPlay(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"), "The completed phase must not restart its exit animation.");

        foreach (string endAnimation in new[] { "Walk", "Attack", "Land", "Hurt" })
        {
            manager.ResetState();
            Begin();
            Play(hero, "ChangeAnimation", endAnimation);
            manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
            manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
            AdvanceExitHold(1.5f);
            RawPlay(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsFalse(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"),
                $"Ending during {endAnimation} must not play the exit animation.");
            RawPlay(hero, "ChangeAnimation", "Jump");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"),
                "A later jump must not play an exit animation queued by a non-jump phase end.");
        }
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpeedJumpParticlesReplaceNormalParticlesAndRageRetainsItsOwnParticles()
    {
        Player hero = MakePlayer(out var overrides);
        var speedParticles = new GameObject("Speed jump particles", typeof(ParticleSystem));
        speedParticles.transform.SetParent(hero.transform, false);
        speedParticles.SetActive(false);
        hero.speedJumpParticleObject = speedParticles;
        settings.phaseSpawnCount = 1;
        Begin(false);
        Play(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.normalJumpParticleObject.activeSelf);
        Assert.IsFalse(speedParticles.activeSelf);
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsFalse(hero.normalJumpParticleObject.activeSelf);
        Assert.IsFalse(hero.rageJumpParticleObject.activeSelf);
        Assert.IsTrue(speedParticles.activeSelf);
        Assert.IsTrue(speedParticles.GetComponent<ParticleSystem>().emission.enabled);
        FinishSpeedTransition(hero);
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(speedParticles.activeSelf);
        Assert.IsFalse(hero.normalJumpParticleObject.activeSelf);
        AdvanceExitHold(1.5f);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.normalJumpParticleObject.activeSelf);
        Assert.IsFalse(speedParticles.activeSelf);
        FinishSpeedTransition(hero);
        Begin();
        Play(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(speedParticles.activeSelf);
        typeof(Player).GetField("isRageMode", Private).SetValue(hero, true);
        overrides.SetRageMode(true);
        RawPlay(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.rageJumpParticleObject.activeSelf);
        Assert.IsFalse(hero.normalJumpParticleObject.activeSelf);
        Assert.IsFalse(speedParticles.activeSelf);
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator HurtRequiresSpeedStartOnTheNextJumpAndSteadyIsBlockedDuringSpeed()
    {
        Player hero = MakePlayer(out _);
        Begin();
        Play(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        RawPlay(hero, "ChangeAnimation", "Hurt");
        Assert.AreEqual("Hurt", typeof(Player).GetField("currentAnim", Private).GetValue(hero));
        typeof(Player).GetField("isInHurtWindow", Private).SetValue(hero, true);
        RawPlay(hero, "ChangeAnimation", "Walk");
        Assert.AreEqual("Hurt", typeof(Player).GetField("currentAnim", Private).GetValue(hero));
        typeof(Player).GetField("isInHurtWindow", Private).SetValue(hero, false);
        RawPlay(hero, "ChangeAnimation", "Walk");
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Walk"), "Hurt recovery on the ground waits for the next jump before restarting speed.");
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        RawPlay(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));
        FinishSpeedTransition(hero);
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        hero.anim.SetTrigger("Steady");
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        RawPlay(hero, "ChangeAnimation", "Steady");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        typeof(Player).GetField("jumpedThisAirborne", Private).SetValue(hero, false);
        Assert.AreEqual("Jump", typeof(Player).GetMethod("GetAirborneAnimationTrigger", Private).Invoke(hero, null));
        RawPlay(hero, "ChangeAnimation", "Still");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator ObstacleSlowdownUsesNormalJumpUntilRecoveryForEveryCharacter()
    {
        Player hero = MakePlayer(out var overrides);
        var speedParticles = new GameObject("Recovery speed particles", typeof(ParticleSystem));
        speedParticles.transform.SetParent(hero.transform, false);
        speedParticles.SetActive(false);
        hero.speedJumpParticleObject = speedParticles;
        var obstacleState = typeof(GameData).GetField("obstacleState", Private);
        data.defaultStageSpeedMult = data.stageSpeedMult = 0.8f;
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        Begin();

        for (int type = 1; type <= 5; type++)
        {
            data.selectedPlayerType = type;
            overrides.ApplyOverrides(true);
            Play(hero, "ChangeAnimation", "Jump");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
            obstacleState.SetValue(data, GameData.ObstacleContactState.Decelerating);
            Play(hero, "ChangeAnimation", "Hurt");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("base_hurt"));

            foreach (var state in new[]
            {
                GameData.ObstacleContactState.Decelerating, GameData.ObstacleContactState.Pushing,
                GameData.ObstacleContactState.Stopped, GameData.ObstacleContactState.Recovering
            })
            {
                obstacleState.SetValue(data, state);
                data.stageSpeedMult = state == GameData.ObstacleContactState.Recovering ? 0.799f : 0.4f;
                Play(hero, "ChangeAnimation", "Jump");
                hero.rb.linearVelocity = Vector2.up * 3f;
                Play(hero, "SyncSpeedPhaseAirAnimation");
                Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"), $"Player {type}, {state}");
                Assert.IsTrue(hero.normalJumpParticleObject.activeSelf);
                Assert.IsFalse(speedParticles.activeSelf);
                Play(hero, "ForceAnimationState", "Base Jump", "Jump");
                Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
            }

            data.stageSpeedMult = 0.8f;
            obstacleState.SetValue(data, GameData.ObstacleContactState.None);
            Play(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"), "An ongoing jump resumes speed as soon as recovery completes.");
            Assert.AreEqual("Jump", typeof(Player).GetField("currentAnim", Private).GetValue(hero));
            Assert.IsFalse(hero.normalJumpParticleObject.activeSelf);
            Assert.IsTrue(speedParticles.activeSelf);
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator ObstacleContactCancelsSpeedIntroAndGroundRecoveryWaitsForJump()
    {
        Player hero = MakePlayer(out _);
        data.defaultStageSpeedMult = data.stageSpeedMult = 0.8f;
        data.decelerateTime = 0.05f;
        data.recoverTime = 0.1f;
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        Begin();
        RawPlay(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));

        data.BeginObstacleContact();
        typeof(GameData).GetField("lastObstacleTouchTime", Private).SetValue(data, -100f);
        data.EndObstacleContact();
        Assert.IsTrue(data.IsObstacleSlowdownActive);
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"), "Contact also cancels a speed intro already playing.");
        Play(hero, "ChangeAnimation", "Hurt");
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, true);
        Play(hero, "ChangeAnimation", "Walk");

        float deadline = Time.time + 3f;
        while (data.IsObstacleSlowdownActive && Time.time < deadline)
        {
            Play(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Walk"));
            yield return null;
        }
        Assert.IsFalse(data.IsObstacleSlowdownActive, "The actual contact coroutine must finish recovery.");
        Assert.AreEqual(0.8f, data.stageSpeedMult, 0.0001f);
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Walk"));
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        Play(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed"));
    }

    [UnityTest]
    public IEnumerator EndingSpeedPhaseDuringObstacleRecoveryKeepsNormalJump()
    {
        Player hero = MakePlayer(out _);
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        settings.phaseSpawnCount = 1;
        Begin();
        Play(hero, "ChangeAnimation", "Jump");
        var obstacleState = typeof(GameData).GetField("obstacleState", Private);
        obstacleState.SetValue(data, GameData.ObstacleContactState.Recovering);
        Play(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        PassLastPhase();
        PassLastPhase();
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        obstacleState.SetValue(data, GameData.ObstacleContactState.None);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        yield break;
    }

    [UnityTest]
    public IEnumerator SonicBoomRequiresSpeedPoseAndNormalSpeedAndReusesItsInstanceAfterRecovery()
    {
        Player hero = MakePlayer(out _);
        hero.sonicBoomPrefabs[0] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/sonicBoom1.prefab");
        hero.sonicBoomLocalOffset = new Vector3(0.5f, 0.2f, -0.1f);
        data.defaultStageSpeedMult = 0.8f;
        var effectField = typeof(Player).GetField("activeSonicBoom", Private);
        Play(hero, "ChangeAnimation", "Jump");
        RawPlay(hero, "SyncSonicBoom");
        Assert.IsNull(effectField.GetValue(hero), "Ordinary jumps must not create a sonic boom.");

        Begin();
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        RawPlay(hero, "SyncSonicBoom");
        Assert.IsNull(effectField.GetValue(hero), "The speed intro must finish before the boom appears.");
        FinishSpeedTransition(hero);
        RawPlay(hero, "SyncSonicBoom");
        var effect = (GameObject)effectField.GetValue(hero);
        Assert.IsNotNull(effect);
        Assert.IsTrue(effect.activeSelf);
        Assert.AreSame(hero.transform, effect.transform.parent);
        Assert.AreEqual(hero.sonicBoomLocalOffset, effect.transform.localPosition);
        hero.transform.position += Vector3.up;
        Assert.That(Vector3.Distance(hero.transform.TransformPoint(hero.sonicBoomLocalOffset), effect.transform.position), Is.LessThan(0.0001f));

        var obstacleState = typeof(GameData).GetField("obstacleState", Private);
        foreach (var state in new[]
        {
            GameData.ObstacleContactState.Decelerating, GameData.ObstacleContactState.Pushing,
            GameData.ObstacleContactState.Stopped, GameData.ObstacleContactState.Recovering
        })
        {
            obstacleState.SetValue(data, state);
            data.stageSpeedMult = 0.4f;
            RawPlay(hero, "SyncSonicBoom");
            Assert.IsFalse(effect.activeSelf, "Hide the existing boom throughout slowdown and recovery.");
        }
        data.stageSpeedMult = 0.8f;
        obstacleState.SetValue(data, GameData.ObstacleContactState.None);
        RawPlay(hero, "SyncSonicBoom");
        Assert.IsTrue(effect.activeSelf);
        Assert.AreSame(effect, effectField.GetValue(hero));

        RawPlay(hero, "ChangeAnimation", "Hurt");
        RawPlay(hero, "SyncSonicBoom");
        Assert.IsFalse(effect.activeSelf);
        Play(hero, "ChangeAnimation", "Jump");
        RawPlay(hero, "SyncSonicBoom");
        Assert.IsTrue(effect.activeSelf);
        hero.enabled = true;
        hero.enabled = false;
        Assert.IsFalse(effect.activeSelf, "Disabling the player must immediately hide its boom.");
        manager.ResetState();
        yield return null;
    }

    [UnityTest]
    public IEnumerator SonicBoomSelectsEachCharacterPrefabAndClearsOnMissingPrefabAndPhaseEnd()
    {
        Player hero = MakePlayer(out var overrides);
        var first = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/sonicBoom1.prefab");
        var second = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/sonicBoom2.prefab");
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        hero.sonicBoomPrefabs = new[] { first, second, first, second, first };
        var effectField = typeof(Player).GetField("activeSonicBoom", Private);
        Begin();
        for (int type = 1; type <= 5; type++)
        {
            data.selectedPlayerType = type;
            overrides.ApplyOverrides(true);
            Play(hero, "ChangeAnimation", "Jump");
            RawPlay(hero, "SyncSonicBoom");
            var effect = (GameObject)effectField.GetValue(hero);
            Assert.IsNotNull(effect);
            Assert.IsTrue(effect.activeSelf);
            Assert.AreEqual(hero.sonicBoomPrefabs[type - 1].name + "(Clone)", effect.name);
        }
        var lastEffect = (GameObject)effectField.GetValue(hero);
        hero.sonicBoomPrefabs[4] = null;
        RawPlay(hero, "SyncSonicBoom");
        Assert.IsFalse(lastEffect.activeSelf);
        hero.sonicBoomPrefabs[4] = first;
        RawPlay(hero, "SyncSonicBoom");
        Assert.IsTrue(lastEffect.activeSelf);
        manager.ResetState();
        RawPlay(hero, "SyncSonicBoom");
        Assert.IsFalse(lastEffect.activeSelf);
        yield return null;
    }

    [UnityTest]
    public IEnumerator OnePhaseAndRepeatedPoolActivationResetTheirCountersAndSpeed()
    {
        settings.phaseSpawnCount = 1;
        settings.phasePrefabs = new[] { settings.phasePrefabs[0] };
        for (int run = 0; run < 3; run++)
        {
            Begin();
            Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
            manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
            Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
            Assert.AreSame(settings.endPhasePrefab, Prefabs[LastObject]);
            Assert.AreEqual(2.5f, manager.SpecialPhaseSpeedMultiplier);
            manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            Assert.AreSame(normal, Prefabs[LastObject]);
            Assert.AreEqual(2.5f, manager.SpecialPhaseSpeedMultiplier);
            AdvanceExitHold(1.5f);
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            manager.ResetState();
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator Stage2SpeedSequenceAndBackgroundTransitionCannotSkipStage3BeforeBoss()
    {
        manager.speedUp1 = 3;
        manager.speedUp2 = 14;
        manager.speedUp3 = 31;
        settings.phaseSpawnCount = 6;
        var stage3 = MakePhase("Stage 3 normal phase");
        manager.stage3PhasePrefabs = new[] { stage3 };
        manager.stage2SpecialPhasePrefabs = new[]
        {
            new SpecialPhaseEntry { prefab = settings.gameObject, spawnChance = 1f }
        };
        manager.backgroundPrefab = new GameObject("First background");
        var second = new GameObject("Second background");
        manager.backgroundPrefabs = new[] { second };
        manager.preBackgroundPrefab = new GameObject("Background transition template");
        manager.preBackgroundPrefab.SetActive(false);
        manager.preBackgroundPoint = new GameObject("Transition spawn").transform;
        manager.preBackgroundPoint.position = Vector3.right * 10f;
        manager.preBackgroundPoint2 = new GameObject("Transition destination").transform;
        manager.preBackgroundPoint2.position = Vector3.left * 10f;
        manager.ResetState();
        typeof(StageManager).GetField("phaseSpawnCount", Private).SetValue(manager, 13);
        typeof(StageManager).GetField("stage1BackgroundRollCompleted", Private).SetValue(manager, true);
        typeof(StageManager).GetField("stage1BackgroundChangeTriggered", Private).SetValue(manager, true);
        typeof(StageManager).GetField("stage3BackgroundChangeAt", Private).SetValue(manager, 1);
        Cycles[2] = new List<GameObject> { settings.gameObject };

        Call("SpawnPhase");
        Assert.IsTrue(manager.IsSpeedPhaseSequenceActive);
        Assert.AreEqual(14, manager.GetPhaseSpawnCount(), "The complete speed sequence occupies one stage slot.");
        for (int i = 0; i < 8; i++)
            manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
        Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
        Assert.AreSame(stage3, Prefabs[LastObject]);
        Assert.AreEqual(15, manager.GetPhaseSpawnCount());
        Assert.AreEqual(3, Call("GetCurrentPhaseStage"));

        for (int i = 0; i < 4; i++)
        {
            manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
            Assert.AreSame(stage3, Prefabs[LastObject]);
            Assert.AreEqual(16 + i, manager.GetPhaseSpawnCount());
            Assert.AreEqual(3, Call("GetCurrentPhaseStage"));
            Assert.IsFalse((bool)typeof(StageManager).GetField("bossTriggered", Private).GetValue(manager));
        }
        for (int frame = 0; frame < 5 && !second.activeSelf; frame++)
            yield return null;
        GameObject effect = (GameObject)typeof(StageManager).GetField("preBackgroundInstance", Private).GetValue(manager);
        Assert.IsNotNull(effect);
        Assert.AreEqual(3, Call("GetCurrentPhaseStage"));
        effect.transform.position = Vector3.left * 11f;
        for (int frame = 0; frame < 5 && effect != null; frame++)
            yield return null;
        Assert.IsTrue(effect == null);
        Assert.AreEqual(3, Call("GetCurrentPhaseStage"));

        while (manager.GetPhaseSpawnCount() < manager.speedUp3)
        {
            Assert.IsFalse((bool)typeof(StageManager).GetField("bossTriggered", Private).GetValue(manager));
            manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
            Assert.AreSame(stage3, Prefabs[LastObject]);
        }
        Assert.IsTrue((bool)typeof(StageManager).GetField("bossTriggered", Private).GetValue(manager));
        Assert.IsFalse((bool)typeof(StageManager).GetField("bossRunning", Private).GetValue(manager),
            "The last stage 3 phase must pass before the boss begins.");
    }

    [UnityTest]
    public IEnumerator SpeedTriggerImmediatelyAcceleratesWithoutPausingPhysicsOrShakingCamera()
    {
        Player hero = MakePlayer(out _);
        hero.transform.position = new Vector3(3f, 8f, -5f);
        hero.rb.position = hero.transform.position;
        hero.rb.simulated = true;
        hero.rb.linearVelocity = Vector2.down * 4f;
        float gravity = hero.rb.gravityScale;
        RigidbodyConstraints2D constraints = hero.rb.constraints;
        var cameraObject = new GameObject("Speed trigger camera", typeof(Camera));
        cameraObject.tag = "MainCamera";
        var shake = cameraObject.AddComponent<CameraShakeManager>();
        var scrolling = new GameObject("Scrolling world", typeof(Mover));
        Begin(false);
        hero.enabled = true;
        manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());

        Assert.IsTrue(manager.IsSpeedPhaseSpeedActive);
        Assert.AreEqual(settings.speedMultiplier, manager.SpecialPhaseSpeedMultiplier);
        Assert.IsFalse(RageTransformFreezeController.IsGameplayPauseActive);
        Assert.IsFalse(manager.IsGameplayTransformPaused);
        Assert.IsTrue(hero.rb.simulated);
        Assert.AreEqual(gravity, hero.rb.gravityScale);
        Assert.AreEqual(constraints, hero.rb.constraints);
        Assert.IsTrue(scrolling.GetComponent<Mover>().enabled);
        Assert.AreEqual(0, ((IList)typeof(CameraShakeManager).GetField("activeShakes", Private).GetValue(shake)).Count);
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.Greater(hero.anim.speed, 0f);
        Assert.IsFalse(RageTransformFreezeController.IsGameplayPauseActive,
            "Speed Start presentation must not pause gameplay or wait for animation completion.");
        hero.enabled = false;
        float initialY = hero.rb.position.y;
        float initialWorldX = scrolling.transform.position.x;
        float until = Time.time + 0.08f;
        while (Time.time < until)
            yield return null;
        Assert.Less(hero.rb.position.y, initialY, "Player physics continue immediately after the speed trigger.");
        Assert.Less(scrolling.transform.position.x, initialWorldX);
        manager.ResetState();
    }

    [UnityTest]
    public IEnumerator EntrySpeedAppliesImmediatelyHoldsThenReturnsToTheNormalSpeedPhaseMultiplier()
    {
        settings.speedMultiplier = 1.6f;
        settings.entrySpeedMultiplier = 1.7f;
        settings.entryHoldDuration = settings.entryReturnDuration = 0.5f;
        Begin(false);
        Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
        float startedAt = Time.time;
        PassLastPhase();
        Assert.AreEqual(1.7f, manager.SpecialPhaseSpeedMultiplier);
        Assert.AreEqual(0.8f * 1.7f, data.GetStageSpeedMult(), 0.00001f);
        Assert.IsFalse(RageTransformFreezeController.IsGameplayPauseActive);
        bool sawReturn = false;
        while (Time.time < startedAt + 1.1f)
        {
            float elapsed = Time.time - startedAt;
            float multiplier = manager.SpecialPhaseSpeedMultiplier;
            if (elapsed < 0.4f)
                Assert.AreEqual(1.7f, multiplier, 0.00001f, "The entry speed must hold without ramping up.");
            if (multiplier < 1.7f && multiplier > 1.6f)
                sawReturn = true;
            yield return null;
        }
        Assert.IsTrue(sawReturn, "Return to the sustained speed must be gradual.");
        Assert.AreEqual(1.6f, manager.SpecialPhaseSpeedMultiplier, 0.00001f);
        Assert.AreEqual(0.8f * 1.6f, data.GetStageSpeedMult(), 0.00001f);
        manager.ResetState();
    }

    [UnityTest]
    public IEnumerator EntrySpeedUsesIndependentInspectorValuesAndPausePreservesItsProgress()
    {
        var inspector = new SerializedObject(settings);
        inspector.FindProperty("speedMultiplier").floatValue = 1.4f;
        inspector.FindProperty("entrySpeedMultiplier").floatValue = 2.2f;
        inspector.FindProperty("entryHoldDuration").floatValue = 0.2f;
        inspector.FindProperty("entryReturnDuration").floatValue = 0.4f;
        inspector.ApplyModifiedPropertiesWithoutUndo();
        Begin();
        Assert.AreEqual(2.2f, manager.SpecialPhaseSpeedMultiplier);
        AdvanceEntrySpeed(0.2f);
        Assert.AreEqual(2.2f, manager.SpecialPhaseSpeedMultiplier);
        manager.SetGameplayPause(true);
        AdvanceEntrySpeed(10f);
        Assert.AreEqual(2.2f, manager.SpecialPhaseSpeedMultiplier);
        manager.SetGameplayPause(false);
        AdvanceEntrySpeed(0.2f);
        Assert.AreEqual(1.8f, manager.SpecialPhaseSpeedMultiplier, 0.00001f);
        PassLastPhase();
        Assert.AreEqual(1.8f, manager.SpecialPhaseSpeedMultiplier, 0.00001f,
            "Spawning the next speed phase must not restart the entry burst.");
        AdvanceEntrySpeed(0.2f);
        Assert.AreEqual(1.4f, manager.SpecialPhaseSpeedMultiplier, 0.00001f);
        manager.ResetState();
        yield break;
    }

    [UnityTest]
    public IEnumerator EntrySpeedRestartsAtTheDetectorAfterResetOrDisable()
    {
        settings.speedMultiplier = 1.6f;
        settings.entrySpeedMultiplier = 1.7f;
        settings.entryHoldDuration = settings.entryReturnDuration = 0.5f;
        for (int run = 0; run < 2; run++)
        {
            Begin(false);
            AdvanceEntrySpeed(10f);
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            PassLastPhase();
            Assert.AreEqual(1.7f, manager.SpecialPhaseSpeedMultiplier);
            AdvanceEntrySpeed(0.75f);
            Assert.AreEqual(1.65f, manager.SpecialPhaseSpeedMultiplier, 0.00001f);
            if (run == 0)
                manager.ResetState();
            else
            {
                manager.enabled = false;
                manager.enabled = true;
            }
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            manager.ResetState();
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator ZeroEntryDurationsSkipTheHoldOrReturnAsConfigured()
    {
        settings.speedMultiplier = 1.6f;
        settings.entrySpeedMultiplier = 1.7f;
        for (int mode = 0; mode < 3; mode++)
        {
            settings.entryHoldDuration = mode == 0 ? 0.5f : 0f;
            settings.entryReturnDuration = mode == 1 ? 0.5f : 0f;
            Begin();
            Assert.AreEqual(mode == 2 ? 1.6f : 1.7f, manager.SpecialPhaseSpeedMultiplier);
            AdvanceEntrySpeed(0.5f);
            Assert.AreEqual(1.6f, manager.SpecialPhaseSpeedMultiplier, 0.00001f);
            manager.ResetState();
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator SpeedEffectsStayVisibleAboveSustainedSpeedAndFadeOnlyBelowItDuringEntry()
    {
        new GameObject("Entry effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1000f, 4f) };
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        settings.speedEffectFadeDuration = 0f;
        settings.speedMultiplier = 1.6f;
        settings.entrySpeedMultiplier = 3f;
        settings.entryHoldDuration = 10f;
        settings.entryReturnDuration = 1f;
        data.defaultStageSpeedMult = data.stageSpeedMult = 0.8f;
        Begin();
        Assert.IsTrue(data.CanSpawnSpeedEffects());
        Assert.AreEqual(0.8f * 3f, data.GetStageSpeedMult(), 0.00001f);
        Assert.AreEqual(1, Effects.Count);
        typeof(GameData).GetField("obstacleState", Private).SetValue(data, GameData.ObstacleContactState.Recovering);
        foreach (float baseSpeed in new[] { 0.8f, 0.5f, 0.8f * 1.6f / 3f })
        {
            data.stageSpeedMult = baseSpeed;
            Assert.IsTrue(data.CanSpawnSpeedEffects(), "At or above the sustained speed, effects remain visible.");
            Call("TrySpawnSpeedPhaseEffects");
            foreach (speedEffect effect in Effects)
                Assert.AreEqual(1f, effect.GetComponent<SpriteRenderer>().color.a);
        }
        data.stageSpeedMult = 0.4f;
        Assert.IsFalse(data.CanSpawnSpeedEffects());
        Call("TrySpawnSpeedPhaseEffects");
        foreach (speedEffect effect in Effects)
            Assert.AreEqual(0f, effect.GetComponent<SpriteRenderer>().color.a);
        data.stageSpeedMult = 0.8f;
        Assert.IsTrue(data.CanSpawnSpeedEffects());
        Call("TrySpawnSpeedPhaseEffects");
        foreach (speedEffect effect in Effects)
            Assert.AreEqual(1f, effect.GetComponent<SpriteRenderer>().color.a, "Faster speed also reverses a prior fade.");
        AdvanceEntrySpeed(11f);
        Assert.AreEqual(1.6f, manager.SpecialPhaseSpeedMultiplier);
        Assert.IsTrue(data.CanSpawnSpeedEffects());
        manager.ResetState();
        yield break;
    }

    [UnityTest]
    public IEnumerator SpeedEffectWavesStayConnectedThroughTheEntryBurstAndReturn()
    {
        new GameObject("Continuous effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(27f, 4f) };
        hero.speedEffectDespawnX = -200f;
        hero.speedEffectSpawnInterval = 0.6f;
        settings.speedEffectPrefab = hero.speedEffectPrefab;
        settings.speedEffectMoveSpeed = 18f;
        settings.speedMultiplier = 1.6f;
        settings.entrySpeedMultiplier = 5f;
        settings.entryHoldDuration = 0.35f;
        settings.entryReturnDuration = 0.5f;
        Begin();
        float nextSpawn = (float)typeof(StageManager).GetField("nextSpeedPhaseEffectSpawnTime", Private).GetValue(manager);
        Assert.Less(nextSpawn - Time.time, hero.speedEffectSpawnInterval,
            "The burst must emit the next wave sooner instead of letting the first one escape.");
        bool sawMultipleWaves = false;
        float deadline = Time.time + 1.3f;
        while (Time.time < deadline)
        {
            yield return null;
            for (int i = 1; i < Effects.Count; i++)
            {
                sawMultipleWaves = true;
                SpriteRenderer previous = Effects[i - 1].GetComponent<SpriteRenderer>();
                SpriteRenderer next = Effects[i].GetComponent<SpriteRenderer>();
                Assert.LessOrEqual(next.bounds.min.x, previous.bounds.max.x + 0.001f,
                    "The new strip must overlap the previous strip's tail throughout the speed transition.");
            }
        }
        Assert.IsTrue(sawMultipleWaves);
        Assert.GreaterOrEqual(Effects.Count, 3);
        speedEffect oldest = Effects[0];
        speedEffect newest = Effects[Effects.Count - 1];
        float oldX = oldest.transform.position.x;
        float newX = newest.transform.position.x;
        typeof(speedEffect).GetMethod("Update", Private).Invoke(oldest, null);
        typeof(speedEffect).GetMethod("Update", Private).Invoke(newest, null);
        Assert.AreEqual(oldX - oldest.transform.position.x, newX - newest.transform.position.x, 0.0001f,
            "Old and new strips must travel at the same current speed after the entry burst.");
        manager.ResetState();
    }

    private void AdvanceEntrySpeed(float seconds) => typeof(StageManager)
        .GetMethod("UpdateSpeedPhaseEntrySpeed", Private).Invoke(manager, new object[] { seconds });

    private Player MakePlayer(out PlayerAnimOverride overrides)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/player.prefab");
        Player hero = Object.Instantiate(prefab).GetComponent<Player>();
        hero.StopAllCoroutines();
        hero.enabled = false;
        hero.rb.simulated = false;
        data.playerRef = hero;
        hero.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        overrides = hero.GetComponent<PlayerAnimOverride>();
        overrides.StopAllCoroutines();
        overrides.enabled = false;
        overrides.ApplyOverrides(true);
        hero.anim.Rebind();
        hero.anim.Update(0f);
        return hero;
    }

    private static void Play(Player hero, string method, params object[] args)
    {
        RawPlay(hero, method, args);
        FinishSpeedTransition(hero);
    }

    private static void RawPlay(Player hero, string method, params object[] args)
    {
        typeof(Player).GetMethod(method, Private).Invoke(hero, args);
        hero.anim.Update(0.02f);
    }

    private static void FinishSpeedTransition(Player hero)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            AnimatorStateInfo state = hero.anim.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("Base Speed Start"))
                return;
            hero.anim.Update(Mathf.Max(0.02f, state.length) + 0.02f);
            RawPlay(hero, "UpdateSpeedPhasePresentation");
        }
    }

    private static Train MakeTrain()
    {
        var go = new GameObject("Speed phase train");
        go.transform.localPosition = new Vector3(6f, 2f, 3f);
        Train train = go.AddComponent<Train>();
        train.ragePushBackDuration = train.rageReturnDuration = 0.05f;
        train.rageRandomMinOffset = train.rageRandomMaxOffset = 0f;
        train.normalRandomMinOffset = train.normalRandomMaxOffset = 0f;
        return train;
    }

    private static IEnumerator AssertTrainReachesX(Train train, float targetX)
    {
        float deadline = Time.time + 1f;
        while (Mathf.Abs(train.transform.localPosition.x - targetX) > 0.0001f && Time.time < deadline)
            yield return null;
        Assert.AreEqual(targetX, train.transform.localPosition.x, 0.0001f);
    }

    [UnityTest]
    public IEnumerator ExitHoldKeepsDirectionalSpeedPosesUntilSpeedAndJumpReturnTogether()
    {
        Player hero = MakePlayer(out var overrides);
        settings.phaseSpawnCount = 1;
        typeof(Player).GetField("isGrounded", Private).SetValue(hero, false);
        for (int type = 1; type <= 5; type++)
        {
            data.selectedPlayerType = type;
            overrides.ApplyOverrides(true);
            Begin();
            Play(hero, "ChangeAnimation", "Jump");
            PassLastPhase();
            PassLastPhase();
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            Assert.IsFalse(manager.IsSpeedPhaseSpeedActive);
            Assert.IsTrue(manager.IsSpeedPhaseAnimationActive);
            Assert.AreSame(normal, Prefabs[LastObject]);

            float[] velocities = { 2f, 0f, -2f };
            string[] poses = { "Base Speed Up", "Base Speed", "Base Speed Down" };
            for (int i = 0; i < velocities.Length; i++)
            {
                hero.rb.linearVelocity = Vector2.up * velocities[i];
                RawPlay(hero, "SyncSpeedPhaseAirAnimation");
                Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName(poses[i]));
                Assert.AreEqual(2f, data.GetStageSpeedMult(), 0.0001f);
                AdvanceExitHold(i < 2 ? 0.5f : 0.49f);
            }
            RawPlay(hero, "SyncSpeedPhaseAirAnimation");
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Down"));
            Assert.AreEqual(2f, data.GetStageSpeedMult(), 0.0001f);
            AdvanceExitHold(0.01f);
            hero.anim.Update(0f);
            Assert.AreEqual(0.8f, data.GetStageSpeedMult(), 0.0001f);
            Assert.IsFalse(manager.IsSpeedPhaseAnimationActive);
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
            manager.ResetState();
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator ExitSpeedHoldsForOneAndAHalfGameplaySecondsAndCancelsOnResetDisableGameOverOrRage()
    {
        for (int mode = 0; mode < 5; mode++)
        {
            Begin();
            for (int i = 0; i < settings.phaseSpawnCount + 1; i++) PassLastPhase();
            Assert.IsFalse(manager.IsSpeedPhaseSequenceActive);
            Assert.AreSame(normal, Prefabs[LastObject]);
            Assert.AreEqual(2f, data.GetStageSpeedMult(), 0.0001f);

            AdvanceExitHold(1f);
            manager.SetGameplayPause(true);
            AdvanceExitHold(10f);
            manager.SetGameplayPause(false);
            AdvanceExitHold(0.49f);
            Assert.AreEqual(2f, data.GetStageSpeedMult(), 0.0001f);

            switch (mode)
            {
                case 0: AdvanceExitHold(0.02f); break;
                case 1: manager.ResetState(); break;
                case 2: manager.enabled = false; manager.enabled = true; break;
                case 3:
                    data.gameOver = true;
                    GameData.OnGameOver?.Invoke();
                    data.gameOver = false;
                    break;
                case 4: Call("InterruptSpeedPhaseSequenceForRage"); break;
            }
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            Assert.AreEqual(0.8f, data.GetStageSpeedMult(), 0.0001f);
            manager.ResetState();
        }
        yield break;
    }

    private void AdvanceExitHold(float seconds) => typeof(StageManager)
        .GetMethod("UpdateSpeedPhaseExitHold", Private).Invoke(manager, new object[] { seconds });

    private void Begin(bool activateSpeed = true)
    {
        Cycles[1] = new List<GameObject> { settings.gameObject };
        Call("SpawnPhase");
        if (activateSpeed) manager.OnPhasePassed(LastObject.GetComponentInChildren<PhaseEndTrigger>());
    }

    private static GameObject MakePhase(string name)
    {
        var phase = new GameObject(name);
        phase.SetActive(false);
        phase.AddComponent<Mover>();
        var trigger = new GameObject("End trigger", typeof(BoxCollider2D), typeof(PhaseEndTrigger));
        trigger.transform.SetParent(phase.transform, false);
        trigger.GetComponent<BoxCollider2D>().isTrigger = true;
        return phase;
    }

    private IList Active => (IList)typeof(StageManager).GetField("activePhases", Private).GetValue(manager);
    private List<speedEffect> Effects => (List<speedEffect>)typeof(StageManager)
        .GetField("activeSpeedPhaseEffects", Private).GetValue(manager);
    private GameObject LastObject => (GameObject)Active[Active.Count - 1].GetType().GetField("obj").GetValue(Active[Active.Count - 1]);
    private Dictionary<GameObject, GameObject> Prefabs => (Dictionary<GameObject, GameObject>)typeof(StageManager)
        .GetField("pooledInstanceToPrefab", Private).GetValue(manager);
    private Dictionary<int, List<GameObject>> Cycles => (Dictionary<int, List<GameObject>>)typeof(StageManager)
        .GetField("phaseShuffleByStage", Private).GetValue(manager);
    private static float CallMoverMultiplier(Mover mover) => (float)typeof(Mover).GetMethod("GetSpeedMultiplier", Private).Invoke(mover, null);
    private object Call(string name) => typeof(StageManager).GetMethod(name, Private).Invoke(manager, null);
}
#endif
