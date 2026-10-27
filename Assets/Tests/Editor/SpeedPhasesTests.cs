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
        manager.stage1SpecialPhasePrefabs = new[] { new SpecialPhaseEntry { prefab = wrapper, spawnChance = 1f } };
        Call("InitPools");
        manager.ResetState();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Random.state = randomState;
        if (data != null) Object.Destroy(data.gameObject);
        if (Application.isPlaying) yield return new ExitPlayMode();
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
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            Assert.AreEqual(0.8f, data.GetStageSpeedMult(), 0.0001f);
            Assert.AreSame(normal, Prefabs[LastObject]);
            Assert.AreEqual(4, Active.Count);
            detector.SetActive(false);
            unrelated.SetActive(false);
            Object.Destroy(detector);
            Object.Destroy(unrelated);
            manager.ResetState();
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator ColliderOnlyMoversCannotScrollAheadOfTheirPhaseAtHigherSpeed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/stage/stage 21.prefab");
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
    public IEnumerator PauseAndMachineGunHoldTheSequenceWhileRageCancelsItSafely()
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
    public IEnumerator SpeedOverridesResolveEachPlayerAndLeaveRageWalkOverridesUnchanged()
    {
        Player hero = MakePlayer(out var overrides);
        Assert.IsNotNull(overrides.baseSpeed, "The player prefab must reference base_speed.");
        Assert.AreEqual(5, overrides.speedClips.Length);
        var controller = (AnimatorOverrideController)hero.anim.runtimeAnimatorController;
        for (int i = 0; i < 5; i++)
        {
            data.selectedPlayerType = i + 1;
            overrides.speedClips[i] = overrides.jumpClips[i];
            overrides.ApplyOverrides(true);
            Assert.AreSame(overrides.jumpClips[i], controller[overrides.baseSpeed]);
            Assert.AreSame(overrides.speedStartClips[i], controller[overrides.baseSpeedStart]);
            overrides.SetRageMode(true);
            Assert.AreSame(overrides.rageWalkClips[i], controller[overrides.baseWalk]);
            Assert.AreSame(overrides.baseSpeed, controller[overrides.baseSpeed], "There is no rage speed override.");
            overrides.SetRageMode(false);
            overrides.speedClips[i] = null;
            overrides.ApplyOverrides(true);
            Assert.AreSame(overrides.jumpClips[i], controller[overrides.baseSpeed], "An empty speed slot keeps the correct player's jump art.");
        }
        yield return null;
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
        Assert.AreEqual(Time.time + hero.speedEffectSpawnInterval, nextSpawn, 0.0001f);
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
    public IEnumerator SpeedEffectsStopDuringObstacleSlowdownAndResumeAtTheCurrentNormalSpeed()
    {
        new GameObject("Effect pool").AddComponent<ObjectPool>();
        Player hero = MakePlayer(out _);
        hero.speedEffectSpawnWorldPositions = new[] { new Vector2(1000f, -3.2f), new Vector2(1000f, 4f) };
        settings.speedEffectPrefab = hero.speedEffectPrefab;
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
                Call("TrySpawnSpeedPhaseEffects");
                Assert.AreEqual(2, Effects.Count, "Slowdown must stop new waves, including during recovery.");
                foreach (speedEffect effect in Effects)
                {
                    Assert.IsTrue(effect.gameObject.activeSelf);
                    float x = effect.transform.position.x;
                    typeof(speedEffect).GetMethod("Update", Private).Invoke(effect, null);
                    Assert.AreEqual(x - settings.speedEffectMoveSpeed * 2f * Time.deltaTime,
                        effect.transform.position.x, 0.0001f, "Existing effects retain their spawn speed through slowdown, stop, reverse and recovery.");
                }
            }
        }
        data.stageSpeedMult = 0.8f;
        Assert.IsTrue(data.CanSpawnSpeedEffects());
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(4, Effects.Count, "The due wave resumes immediately at normal speed.");
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
        Assert.AreEqual(2, Effects.Count);
        float deadline = Time.time + 0.5f;
        while (Effects.Count > 0 && Time.time < deadline) yield return null;
        Assert.AreEqual(0, Effects.Count, "Stage slowdown must not strand existing effects on screen.");
        foreach (speedEffect effect in spawned) Assert.IsFalse(effect.gameObject.activeSelf);
        Assert.AreEqual(1f, data.GetStageSpeedMult(), 0.0001f, "During speed phases, an obstacle's zero-speed request is clamped to half of the normal speed of 2.");

        data.stageSpeedMult = 0.8f;
        Call("TrySpawnSpeedPhaseEffects");
        Assert.AreEqual(2, Effects.Count, "Emission resumes only once normal stage speed returns.");
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
    public IEnumerator SpeedStartFinishesBeforeLocomotionAndPhaseEndPlaysItOnceBeforeRestoringJump()
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
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));
        RawPlay(hero, "ChangeAnimation", "Jump");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));
        RawPlay(hero, "SyncSpeedPhaseAirAnimation");
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Speed Start"));
        FinishSpeedTransition(hero);
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
            Assert.AreEqual(1f, manager.SpecialPhaseSpeedMultiplier);
            manager.ResetState();
        }
        yield return null;
    }

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
