#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class BombDetectionStagesTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [UnityTearDown] public IEnumerator Cleanup() { if (Application.isPlaying) yield return new ExitPlayMode(); }

    [UnityTest] public IEnumerator DepthDoesNotReducePlanarHomingSpeed()
    {
        yield return new EnterPlayMode();
        var target = new GameObject("DeepSaw", typeof(CircleCollider2D), typeof(ObstacleInfo), typeof(Obstacle));
        target.transform.position = new Vector3(2f, 0f, 100f);
        var bomb = new GameObject("Bomb", typeof(CapsuleCollider2D), typeof(Bomb)).GetComponent<Bomb>();
        bomb.enabled = false;
        bomb.transform.position = new Vector3(0f, 0f, -4f);
        bomb.speed = 20f;
        bomb.SetTarget(target.transform);
        typeof(Bomb).GetMethod("Advance", Private).Invoke(bomb, new object[] { 0.2f });
        Assert.IsTrue((bool)typeof(Bomb).GetField("exploded", Private).GetValue(bomb), "Z depth must not consume the XY flight distance");
        Assert.That(bomb.transform.position.z, Is.EqualTo(-4f).Within(0.00001f));
        yield return null;
    }

    [UnityTest] public IEnumerator SawEnteringDuringCooldownIsRetriedWhileItStays()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out var detector, out _);
        launcher.ActivateLauncher();
        yield return WaitForReady(launcher);
        typeof(SawDetector).GetField("nextDetectTime", Private).SetValue(detector, Time.time + 0.2f);
        Collider2D target = CreateSaw(detector);
        yield return WaitForHit(target);
    }

    [UnityTest] public IEnumerator SawEnteringBeforeLauncherReadyIsRetriedAfterLoading()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out var detector, out _);
        Collider2D target = CreateSaw(detector);
        yield return WaitGameSeconds(0.1f);
        Assert.IsTrue(target.enabled);
        launcher.ActivateLauncher();
        yield return WaitForHit(target);
    }

    [UnityTest] public IEnumerator StayingSawDoesNotConsumeEveryHeadBeforeTheFirstBombArrives()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out var detector, out var active);
        launcher.ActivateLauncher();
        yield return WaitForReady(launcher);
        Collider2D target = CreateSaw(detector);
        float deadline = Time.time + 1f;
        while (active.Count == 0 && Time.time < deadline) yield return null;
        Assert.AreEqual(1, active.Count);
        yield return WaitGameSeconds(0.25f);
        Assert.IsTrue(target.enabled, "The target should still be ahead of the first projectile");
        Assert.AreEqual(1, active.Count, "OnTriggerStay must not duplicate an in-flight target");
        yield return WaitForHit(target);
    }

    [UnityTest] public IEnumerator OneRandomSawPerColumnEvenAfterItsBombHitsDuringSlowdown()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out var detector, out _);
        launcher.ActivateLauncher(); yield return WaitForReady(launcher);
        var targets = new Collider2D[3];
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i] = CreateSaw(detector);
            targets[i].transform.position += Vector3.up * ((i - 1) * 3f);
        }
        Physics2D.SyncTransforms();
        yield return WaitGameSeconds(2f);
        int remaining = 0;
        foreach (var head in (List<GameObject>)typeof(BombLauncher).GetField("loadedHeads", Private).GetValue(launcher))
            if (head != null && head.activeInHierarchy) remaining++;
        Assert.AreEqual(5, remaining, "A column consumes one head, including after the selected saw is destroyed");
        int alive = 0;
        foreach (var target in targets) if (target != null && target.enabled && target.gameObject.activeInHierarchy) alive++;
        Assert.AreEqual(2, alive, "Only one random saw in this X column may be hit");

        // The next column must still fire after all of this column leaves.
        foreach (var target in targets) if (target != null) target.gameObject.SetActive(false);
        yield return WaitGameSeconds(0.1f);
        Collider2D next = CreateSaw(detector);
        yield return WaitForHit(next);
        remaining = 0;
        foreach (var head in (List<GameObject>)typeof(BombLauncher).GetField("loadedHeads", Private).GetValue(launcher))
            if (head != null && head.activeInHierarchy) remaining++;
        Assert.AreEqual(4, remaining, "A new column must rearm detection");
    }

    [UnityTest] public IEnumerator ColumnSelectionRemainsRandomAndReusesItsBuffers()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out _, out var detector, out _);
        var targets = new Collider2D[3];
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i] = CreateSaw(detector);
            targets[i].transform.position += Vector3.up * ((i - 1) * 3f);
        }
        Physics2D.SyncTransforms();
        var pick = (System.Func<Transform>)System.Delegate.CreateDelegate(typeof(System.Func<Transform>), detector,
            typeof(SawDetector).GetMethod("PickRandomSawTarget", Private));
        var selected = new HashSet<Transform>();
        for (int i = 0; i < 100; i++) selected.Add(pick());
        selected.Clear();
        var previousRandom = Random.state;
        Random.InitState(421);
        using var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192,
            ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 2000; i++) selected.Add(pick());
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        allocations.Stop();
        Random.state = previousRandom;
        long bytes = 0;
        for (int i = 0; i < System.Math.Min(allocations.Count, allocations.Capacity); i++) bytes += allocations.GetSample(i).Value;
        Debug.Log("[SAW COLUMN SELECTION] selections=2000, gc_bytes=" + bytes + ", ms=" +
            elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        Assert.IsTrue(allocations.Valid);
        Assert.AreEqual(0L, bytes);
        Assert.AreEqual(3, selected.Count, "All three same-X candidates must be reachable by random selection");
        foreach (var target in targets) Assert.IsTrue(selected.Contains(target.transform));
        yield return null;
    }

    [UnityTest] public IEnumerator InFlightReservationsReleaseOnRetargetPoolingAndTargetDestruction()
    {
        yield return new EnterPlayMode();
        var root = new GameObject("RopeRoot", typeof(Obstacle));
        var child = new GameObject("SawShape", typeof(CircleCollider2D)); child.transform.SetParent(root.transform);
        var first = child.GetComponent<Collider2D>();
        var otherRoot = new GameObject("OtherSaw", typeof(CircleCollider2D), typeof(Obstacle));
        var second = otherRoot.GetComponent<Collider2D>();
        var a = new GameObject("BombA", typeof(CapsuleCollider2D), typeof(Bomb)).GetComponent<Bomb>();
        var b = new GameObject("BombB", typeof(CapsuleCollider2D), typeof(Bomb)).GetComponent<Bomb>();
        a.GetComponent<Collider2D>().isTrigger = true; b.GetComponent<Collider2D>().isTrigger = true;
        var reserved = (System.Func<Collider2D, bool>)System.Delegate.CreateDelegate(typeof(System.Func<Collider2D, bool>),
            typeof(Bomb).GetMethod("IsTargetInFlight", BindingFlags.Static | BindingFlags.NonPublic));
        a.SetTarget(root.transform); b.SetTarget(child.transform);
        Assert.IsTrue(reserved(first), "Root and child target references must reserve the same shape");
        a.gameObject.SetActive(false);
        Assert.IsTrue(reserved(first), "A second in-flight bomb still owns this target");
        b.SetTarget(otherRoot.transform);
        Assert.IsFalse(reserved(first)); Assert.IsTrue(reserved(second));
        Object.DestroyImmediate(otherRoot);
        b.gameObject.SetActive(false);
        var reservations = (System.Collections.IDictionary)typeof(Bomb).GetField("targetsInFlight", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Assert.AreEqual(0, reservations.Count, "Destroyed collider keys must not survive pooling");
        a.gameObject.SetActive(true); a.SetTarget(root.transform);
        Assert.IsTrue(reserved(first));
        a.gameObject.SetActive(false);
        Assert.IsFalse(reserved(first));
        yield return null;
    }

    [UnityTest] public IEnumerator RageForcedFireKeepsCoordinateShotsSeparateFromDetectedSawTargets()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out _, out var active);
        launcher.rageForcedFireDelay = 0.05f;
        launcher.ActivateLauncher();
        yield return WaitForReady(launcher);
        GameData.OnRageStart?.Invoke();
        float deadline = Time.time + 2f;
        while (active.Count == 0 && Time.time < deadline) yield return null;
        Assert.AreEqual(6, active.Count);
        foreach (GameObject shot in active)
        {
            Transform target = (Transform)typeof(Bomb).GetField("target", Private).GetValue(shot.GetComponent<Bomb>());
            StringAssert.StartsWith("BombLauncherForcedTarget_", target.name);
        }
    }

    [UnityTest] public IEnumerator LoadedLauncherWaitsForRageThenFiresTwoSecondsAfterTheTrigger()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out _, out var active);
        launcher.rageForcedFireDelay = 0.05f;
        launcher.ActivateLauncher(); yield return WaitForReady(launcher);
        yield return WaitGameSeconds(0.15f);
        Assert.AreEqual(0, active.Count, "Launcher activation must not start the forced-fire timer");
        Assert.IsTrue(launcher.CanFire());
        launcher.rageForcedFireDelay = 2f;
        var data = new GameObject("RageTimingData").AddComponent<GameData>(); data.enabled = false;
        float triggered = Time.time;
        data.ActivateRageMode(15f);
        yield return WaitGameSeconds(1.85f);
        Assert.AreEqual(0, active.Count, "Rage must not fire before two seconds");
        float deadline = triggered + 2.5f;
        while (active.Count == 0 && Time.time < deadline) yield return null;
        Assert.AreEqual(6, active.Count);
        Assert.That(Time.time - triggered, Is.InRange(1.99f, 2.35f));
    }

    [UnityTest] public IEnumerator RageDuringLauncherEntryKeepsTheTriggerDeadline()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out _, out var active);
        launcher.moveDuration = 0.35f; launcher.rageForcedFireDelay = 0.1f;
        float triggered = Time.time;
        launcher.ActivateLauncher();
        GameData.OnRageStart?.Invoke();
        float deadline = triggered + 1f;
        while (active.Count == 0 && Time.time < deadline) yield return null;
        Assert.AreEqual(6, active.Count);
        Assert.That(Time.time - triggered, Is.InRange(0.34f, 0.44f), "An elapsed rage timer must fire when entry completes, rather than restarting");
    }

    [UnityTest] public IEnumerator ResetCancelsPendingRageFireAndDisableUnsubscribesTheEvent()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out _, out var active);
        launcher.rageForcedFireDelay = 0.1f;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            launcher.ActivateLauncher(); yield return WaitForReady(launcher);
            GameData.OnRageStart?.Invoke();
            launcher.ResetLauncherState();
            yield return WaitGameSeconds(0.15f);
            Assert.AreEqual(0, active.Count, "Reset must cancel the pending rage volley");
            launcher.gameObject.SetActive(false);
            Assert.AreEqual(0, CountRageSubscriptions(launcher));
            GameData.OnRageStart?.Invoke();
            launcher.gameObject.SetActive(true);
            Assert.AreEqual(1, CountRageSubscriptions(launcher), "Reactivation must not accumulate event handlers");
        }
    }

    private static int CountRageSubscriptions(BombLauncher launcher)
    {
        int count = 0;
        if (GameData.OnRageStart != null)
            foreach (var handler in GameData.OnRageStart.GetInvocationList()) if (ReferenceEquals(handler.Target, launcher)) count++;
        return count;
    }

    [UnityTest] public IEnumerator FailedForcedSpawnKeepsAmmoAndExitsItsLoop()
    {
        yield return new EnterPlayMode();
        PreparePipeline(out var launcher, out _, out var active);
        launcher.ActivateLauncher(); yield return WaitForReady(launcher);
        var pool = ObjectPool.Instance;
        for (int i = 0; i < 12; i++) Assert.IsNotNull(pool.SpawnFromPool("Bomb", Vector3.zero, Quaternion.identity));
        Assert.AreEqual(12, active.Count);
        LogAssert.Expect(LogType.Error, "[BombLauncher] Bomb spawn failed. ObjectPool tag/prefab 확인 필요: Bomb");
        typeof(BombLauncher).GetMethod("ForceFireRemainingBombs", Private).Invoke(launcher, null);
        Assert.IsTrue(launcher.CanFire(), "Pool exhaustion must not consume an unfired head");
        Assert.AreEqual(12, active.Count);
        yield return null;
    }

    [UnityTest] public IEnumerator SlowdownBeforeDetectionUsesTheActualRopeTarget() { yield return new EnterPlayMode(); yield return CheckSlowdownStage(0); }
    [UnityTest] public IEnumerator SlowdownAtTargetAssignmentUsesTheActualRopeTarget() { yield return new EnterPlayMode(); yield return CheckSlowdownStage(1); }
    [UnityTest] public IEnumerator SlowdownAfterLaunchUsesTheActualRopeTarget() { yield return new EnterPlayMode(); yield return CheckSlowdownStage(2); }

    private static IEnumerator CheckSlowdownStage(int stage)
    {
        int previousLimit = Application.targetFrameRate, previousVSync = QualitySettings.vSyncCount;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        try
        {
            PreparePipeline(out var launcher, out _, out var active);
            var data = new GameObject("GameData").AddComponent<GameData>(); data.enabled = false;
            data.decelerateTime = 0.05f; data.recoverTime = 0.1f; data.pushSpeed = 0.3f;
            var manager = new GameObject("StageManager").AddComponent<StageManager>(); manager.enabled = false; manager.phaseBaseSpeed = 6f;
            var phase = new GameObject("RopePhase"); phase.SetActive(false);
            phase.AddComponent<PhaseLayoutSnapshot>(); phase.AddComponent<Mover>().baseSpeed = 6f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/stage 4/stage 4-21.prefab");
            var original = prefab.GetComponentInChildren<SwingMotor>(true).transform.parent;
            GameObject rope = Object.Instantiate(original.gameObject, phase.transform, false);
            rope.transform.localPosition = new Vector3(12f, 1f, 4f);
            Collider2D target = rope.GetComponentInChildren<Obstacle>(true).GetComponent<Collider2D>();
            launcher.ActivateLauncher(); yield return WaitForReady(launcher);
            phase.SetActive(true);
            if (stage == 0)
            {
                data.BeginObstacleContact();
                yield return WaitGameSeconds(0.07f);
                Assert.Less(data.GetStageSpeedMult(), 1f, "Slowdown must begin before the rope reaches detection");
            }
            float deadline = Time.time + 6f;
            while (active.Count == 0 && Time.time < deadline) yield return null;
            Assert.AreEqual(1, active.Count, "The real detector must assign the rope before testing flight");
            Bomb projectile = null;
            foreach (GameObject obj in active) { projectile = obj.GetComponent<Bomb>(); break; }
            Assert.AreSame(target.transform, typeof(Bomb).GetField("target", Private).GetValue(projectile));
            if (stage == 2) yield return WaitGameSeconds(0.12f);
            if (stage != 0) data.BeginObstacleContact();
            yield return WaitForHit(target);
            if (stage != 0) Assert.That(data.GetStageSpeedMult(), Is.LessThan(1f), "The shot must be checked during an actual slowdown");
        }
        finally { Application.targetFrameRate = previousLimit; QualitySettings.vSyncCount = previousVSync; }
    }

    private static void PreparePipeline(out BombLauncher launcher, out SawDetector detector, out HashSet<GameObject> active)
    {
        var pool = new GameObject("ObjectPool").AddComponent<ObjectPool>();
        var launcherPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/BombLauncher.prefab");
        pool.RegisterPool("Bomb", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Bomb.prefab"), 12);
        pool.RegisterPool("BombHitBox", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/BombHitBox.prefab"), 12);
        pool.RegisterPool("BombHead", launcherPrefab.GetComponent<BombLauncher>().bombHeadPrefab, 18);
        launcher = Object.Instantiate(launcherPrefab).GetComponent<BombLauncher>();
        launcher.rageForcedFireDelay = 100f; launcher.moveDuration = 0.01f;
        detector = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/SawDetector.prefab")).GetComponent<SawDetector>();
        detector.launcher = launcher; detector.detectCooldown = 0.05f;
        Physics2D.SyncTransforms();
        active = ((Dictionary<string, HashSet<GameObject>>)typeof(ObjectPool).GetField("activeByTag", Private).GetValue(pool))["Bomb"];
    }

    private static Collider2D CreateSaw(SawDetector detector)
    {
        var saw = new GameObject("DetectedSaw", typeof(CircleCollider2D), typeof(Rigidbody2D), typeof(ObstacleInfo), typeof(Obstacle));
        saw.transform.position = detector.GetComponent<Collider2D>().bounds.center;
        saw.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        saw.GetComponent<ObstacleInfo>().type = ObstacleType.Saw;
        var collider = saw.GetComponent<CircleCollider2D>(); collider.radius = 0.1f; collider.isTrigger = true;
        Physics2D.SyncTransforms();
        return collider;
    }

    private static IEnumerator WaitForReady(BombLauncher launcher)
    {
        float deadline = Time.time + 2f;
        while (!launcher.CanFire() && Time.time < deadline) yield return null;
        Assert.IsTrue(launcher.CanFire());
    }
    private static IEnumerator WaitGameSeconds(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }
    private static IEnumerator WaitForHit(Collider2D target)
    {
        float deadline = Time.time + 3f;
        while (target != null && target.enabled && Time.time < deadline) yield return null;
        Assert.IsTrue(target == null || !target.enabled, "The detected target must receive the shot, including after an initially blocked detection");
    }
}
#endif
