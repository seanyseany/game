#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class BombLauncherSmokeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string SmokeTag = "TestBombLaunchSmoke";

    [UnityTest]
    public IEnumerator ManaRefillsOnlyEmptyCapsAndAllSixCanFireAgain([Values(false, true)] bool fireFromLast)
    {
        yield return new EnterPlayMode();
        BombLauncher launcher = PrepareLauncher(CreateParticleSmoke(), out _);
        launcher.fireFromLast = fireFromLast;
        Player player = CreateManaPlayer(launcher);
        CollectMana(player);
        yield return WaitForReady(launcher);
        GameObject[] originalHeads = Heads(launcher).ToArray();
        Transform target = Target();
        for (int i = 0; i < 4; i++) launcher.FireAt(target);
        Assert.AreEqual(4, Object.FindObjectsByType<Bomb>(FindObjectsSortMode.None).Length);
        var remaining = Heads(launcher).ToArray();
        int loaded = 0;
        foreach (GameObject head in remaining) if (head != null) loaded++;
        Assert.AreEqual(2, loaded);

        Vector3 position = launcher.transform.position;
        CollectMana(player);
        Assert.AreEqual(position, launcher.transform.position);
        Assert.AreEqual(6, Heads(launcher).Count);
        for (int i = 0; i < 6; i++)
        {
            GameObject head = Heads(launcher)[i];
            Assert.IsTrue(head.activeInHierarchy);
            Assert.AreEqual(launcher.headLocalPositions[i], head.transform.localPosition);
            if (remaining[i] != null) Assert.AreSame(originalHeads[i], head, "Keep caps that have not fired.");
        }
        GameObject[] refilledHeads = Heads(launcher).ToArray();
        CollectMana(player);
        CollectionAssert.AreEqual(refilledHeads, Heads(launcher), "A full launcher must not create duplicate caps.");
        for (int i = 0; i < 6; i++)
        {
            Assert.IsTrue(launcher.CanFire());
            launcher.FireAt(target);
        }
        Assert.IsFalse(launcher.CanFire());
        Assert.AreEqual(10, Object.FindObjectsByType<Bomb>(FindObjectsSortMode.None).Length,
            "The four existing projectiles stay in flight while all six reloaded bombs can fire.");
    }

    [UnityTest]
    public IEnumerator ManaDuringEntryOrReturnKeepsTheLauncherLoaded([Values("Entry", "PendingReturn", "Returning")] string timing)
    {
        yield return new EnterPlayMode();
        BombLauncher launcher = PrepareLauncher(CreateParticleSmoke(), out _);
        launcher.moveDuration = 0.25f;
        launcher.autoReturnDelay = 0.05f;
        Player player = CreateManaPlayer(launcher);
        CollectMana(player);
        GameObject[] enteringHeads = Heads(launcher).ToArray();
        if (timing != "Entry")
        {
            yield return WaitForReady(launcher);
            if (timing == "Returning") launcher.moveDuration = 1f;
            Transform target = Target();
            for (int i = 0; i < 6; i++) launcher.FireAt(target);
            if (timing == "Returning")
            {
                float deadline = Time.time + 1f;
                while (!(bool)typeof(BombLauncher).GetField("isMoving", Private).GetValue(launcher) && Time.time < deadline)
                    yield return null;
                Assert.IsTrue((bool)typeof(BombLauncher).GetField("isMoving", Private).GetValue(launcher));
            }
        }

        Vector3 position = launcher.transform.position;
        CollectMana(player);
        Assert.AreEqual(position, launcher.transform.position, "Reloading must not teleport the launcher.");
        if (timing == "Entry") CollectionAssert.AreEqual(enteringHeads, Heads(launcher));
        yield return WaitForReady(launcher);
        yield return new WaitForSeconds(0.4f);
        Assert.IsTrue(launcher.CanFire(), "A cancelled return must not later hide the refilled launcher.");
        Assert.AreEqual(launcher.activePos, launcher.transform.position);
        Assert.AreEqual(6, Heads(launcher).Count);
        foreach (GameObject head in Heads(launcher)) Assert.IsTrue(head.activeInHierarchy);
        launcher.ResetLauncherState();
        Assert.IsNull(typeof(BombLauncher).GetField("returnAndReloadCoroutine", Private).GetValue(launcher));
        Assert.IsFalse(launcher.CanFire());
    }

    private static Player CreateManaPlayer(BombLauncher launcher)
    {
        var root = new GameObject("ManaPickupPlayer");
        root.SetActive(false);
        Player player = root.AddComponent<Player>();
        player.bombLauncher = launcher;
        player.manaForLauncher = 1;
        return player;
    }

    private static void CollectMana(Player player) =>
        typeof(Player).GetMethod("AddManaForLauncher", Private).Invoke(player, new object[] { 1 });

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SmokeSpawnsAtTheFiringCapAndFinishesBeforeReturning([Values(false, true)] bool fireFromLast)
    {
        yield return new EnterPlayMode();
        BombLauncher launcher = PrepareLauncher(CreateParticleSmoke(), out ObjectPool pool);
        launcher.fireFromLast = fireFromLast;
        launcher.ActivateLauncher();
        yield return WaitForReady(launcher);
        launcher.transform.rotation = Quaternion.Euler(0f, 0f, 20f);
        launcher.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
        List<GameObject> heads = Heads(launcher);
        GameObject head = heads[fireFromLast ? heads.Count - 1 : 0];
        Vector3 expectedPosition = head.transform.position;
        Quaternion expectedRotation = head.transform.rotation * launcher.launchSmokePrefab.transform.rotation;
        launcher.FireAt(Target());

        BombLaunchSmoke effect = Object.FindFirstObjectByType<BombLaunchSmoke>();
        Assert.IsNotNull(effect);
        Assert.That(Vector3.Distance(effect.transform.position, expectedPosition), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(effect.transform.rotation, expectedRotation), Is.LessThan(0.01f));
        Assert.IsNull(effect.transform.parent, "Launch smoke must be independent of the cap and launcher");
        Assert.IsFalse(head.activeSelf);
        var particles = effect.GetComponentInChildren<ParticleSystem>();
        Assert.IsFalse(particles.main.loop);
        Assert.AreEqual(ParticleSystemStopAction.None, particles.main.stopAction);
        particles.Emit(5);
        launcher.transform.position += Vector3.right * 3f;
        yield return new WaitForSeconds(0.05f);
        Assert.IsTrue(effect.gameObject.activeSelf, "Old smoke timers must not cut off live particles");
        Assert.Greater(particles.particleCount, 0);
        Assert.That(Vector3.Distance(effect.transform.position, expectedPosition), Is.LessThan(0.0001f));

        float deadline = Time.time + 2f;
        while (effect.gameObject.activeSelf && Time.time < deadline) yield return null;
        Assert.IsFalse(effect.gameObject.activeSelf);
        Assert.AreEqual(0, particles.particleCount);
        Assert.AreSame(pool.transform, effect.transform.parent);
    }

    [UnityTest]
    public IEnumerator ForcedVolleysResetAndReuseSmoke([Values("Reset", "Stop", "Disable", "GameReset")] string reset)
    {
        yield return new EnterPlayMode();
        BombLauncher launcher = PrepareLauncher(CreateParticleSmoke(), out ObjectPool pool);
        var seen = new HashSet<GameObject>();
        bool reused = false;
        for (int volley = 0; volley < 3; volley++)
        {
            launcher.ActivateLauncher();
            yield return WaitForReady(launcher);
            var expectedPositions = new List<Vector3>();
            foreach (GameObject head in Heads(launcher)) expectedPositions.Add(head.transform.position);
            typeof(BombLauncher).GetMethod("ForceFireRemainingBombs", Private).Invoke(launcher, null);
            BombLaunchSmoke[] effects = Object.FindObjectsByType<BombLaunchSmoke>(FindObjectsSortMode.None);
            Assert.AreEqual(launcher.ammoCount, effects.Length);
            foreach (BombLaunchSmoke effect in effects)
            {
                Assert.IsTrue(expectedPositions.Remove(effect.transform.position));
                reused |= !seen.Add(effect.gameObject);
                ParticleSystem particles = effect.GetComponentInChildren<ParticleSystem>();
                Assert.IsTrue(particles.isPlaying, "Every pooled reuse must restart the smoke");
                Assert.Less(particles.time, 0.01f);
                particles.Emit(3);
            }
            Assert.IsEmpty(expectedPositions);

            if (reset == "Stop") launcher.ForceStopAndReturnHome();
            else if (reset == "Disable") launcher.gameObject.SetActive(false);
            else if (reset == "GameReset")
            {
                var data = new GameObject("SmokeResetGameData").AddComponent<GameData>();
                typeof(GameData).GetMethod("ClearBossAndBombObjectsNow", Private).Invoke(data, null);
                Object.DestroyImmediate(data.gameObject);
                launcher.ResetLauncherState();
            }
            else launcher.ResetLauncherState();

            foreach (BombLaunchSmoke effect in effects)
            {
                Assert.IsFalse(effect.gameObject.activeSelf);
                Assert.AreEqual(0, effect.GetComponentInChildren<ParticleSystem>(true).particleCount);
            }
            Assert.IsEmpty(Object.FindObjectsByType<BombLaunchSmoke>(FindObjectsSortMode.None));
            pool.ReturnAllActive("Bomb");
        }
        Assert.IsTrue(reused, "Later volleys must reuse returned instances");
    }

    [UnityTest]
    public IEnumerator FailedBombSpawnDoesNotEmitSmokeOrConsumeACap()
    {
        yield return new EnterPlayMode();
        BombLauncher launcher = PrepareLauncher(CreateParticleSmoke(), out ObjectPool pool);
        launcher.ActivateLauncher();
        yield return WaitForReady(launcher);
        for (int i = 0; i < 12; i++) Assert.IsNotNull(pool.SpawnFromPool("Bomb", Vector3.zero, Quaternion.identity));
        LogAssert.Expect(LogType.Error, "[BombLauncher] Bomb spawn failed. ObjectPool tag/prefab 확인 필요: Bomb");
        launcher.FireAt(Target());
        Assert.IsEmpty(Object.FindObjectsByType<BombLaunchSmoke>(FindObjectsSortMode.None));
        foreach (GameObject head in Heads(launcher)) Assert.IsTrue(head.activeInHierarchy);
        Assert.IsTrue(launcher.CanFire());
    }

    [UnityTest]
    public IEnumerator AnimatedSmokeReturnsAfterItsAnimation()
    {
        yield return new EnterPlayMode();
        BombLauncher launcher = PrepareLauncher(
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/BombSmoke.prefab"), out _);
        launcher.ActivateLauncher();
        yield return WaitForReady(launcher);
        launcher.FireAt(Target());
        BombLaunchSmoke effect = Object.FindFirstObjectByType<BombLaunchSmoke>();
        Animator animator = effect.GetComponent<Animator>();
        float duration = animator.GetCurrentAnimatorStateInfo(0).length;
        Assert.Greater(duration, 0f);
        yield return new WaitForSeconds(duration * 0.5f);
        Assert.IsTrue(effect.gameObject.activeSelf, "Keep the animation visible until it finishes");
        float deadline = Time.time + duration + 1f;
        while (effect.gameObject.activeSelf && Time.time < deadline) yield return null;
        Assert.IsFalse(effect.gameObject.activeSelf);
    }

    [UnityTest]
    public IEnumerator DisablingAPartlyLoadedLauncherReturnsSmokeAndAllowsReload()
    {
        yield return new EnterPlayMode();
        BombLauncher launcher = PrepareLauncher(CreateParticleSmoke(), out _);
        launcher.ActivateLauncher();
        yield return WaitForReady(launcher);
        launcher.FireAt(Target());
        BombLaunchSmoke effect = Object.FindFirstObjectByType<BombLaunchSmoke>();
        launcher.gameObject.SetActive(false);
        Assert.IsFalse(effect.gameObject.activeSelf);
        launcher.ActivateLauncher();
        yield return WaitForReady(launcher);
        Assert.AreEqual(launcher.ammoCount, Heads(launcher).Count);
        foreach (GameObject head in Heads(launcher)) Assert.IsTrue(head.activeInHierarchy);
    }

    private static BombLauncher PrepareLauncher(GameObject smokePrefab, out ObjectPool pool)
    {
        pool = new GameObject("SmokeTestPool").AddComponent<ObjectPool>();
        pool.RegisterPool("Bomb", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Bomb.prefab"), 12);
        var launcher = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/BombLauncher.prefab"))
            .GetComponent<BombLauncher>();
        launcher.launchSmokePrefab = smokePrefab;
        launcher.launchSmokePoolTag = SmokeTag;
        launcher.moveDuration = 0.001f;
        launcher.rageForcedFireDelay = 100f;
        return launcher;
    }

    private static GameObject CreateParticleSmoke()
    {
        var template = new GameObject("LaunchSmokeTemplate");
        template.SetActive(false);
        template.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var particles = template.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = 0.3f;
        main.loop = true;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = particles.emission;
        emission.rateOverTime = 100f;
        var mover = template.AddComponent<SmokeMover>();
        mover.speed = 0f;
        mover.lifetime = 0.01f;
        template.AddComponent<AutoReturnToPool>().lifeTime = 0.01f;
        return template;
    }

    private static List<GameObject> Heads(BombLauncher launcher) =>
        (List<GameObject>)typeof(BombLauncher).GetField("loadedHeads", Private).GetValue(launcher);

    private static Transform Target()
    {
        var target = new GameObject("LaunchTarget");
        target.transform.position = Vector3.right * 100f;
        return target.transform;
    }

    private static IEnumerator WaitForReady(BombLauncher launcher)
    {
        float deadline = Time.time + 2f;
        while (!launcher.CanFire() && Time.time < deadline) yield return null;
        Assert.IsTrue(launcher.CanFire());
    }
}
#endif
