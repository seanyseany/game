#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using Unity.Profiling;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ArcadePerformanceTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private delegate bool GroundProbe(out Collider2D collider, out string tag, out bool both);
    private delegate bool ParameterProbe(Animator animator, string name, AnimatorControllerParameterType type);

    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator MeasureNativePlayerInputAndIdleHudAllocations()
    {
        yield return new EnterPlayMode();
        MeasureNativePlayerInput();
        yield return null;
    }

    private static void MeasureNativePlayerInput()
    {
        new GameObject("GameData").AddComponent<GameData>();
        new GameObject("ObjectPool").AddComponent<ObjectPool>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/player.prefab");
        Assert.IsNotNull(prefab);
        GameObject hero = Object.Instantiate(prefab);
        Assert.IsNotNull(hero);
        Player player = hero.GetComponentInChildren<Player>(true);
        Assert.IsNotNull(player, "Native player script must bind");
        player.StopAllCoroutines();
        player.enabled = false;
        player.rb.gravityScale = 0f;
        var floor = new GameObject("Floor", typeof(BoxCollider2D));
        floor.tag = "floor";
        floor.transform.position = player.GetComponent<Collider2D>().bounds.min - Vector3.up * 0.5f;
        floor.GetComponent<BoxCollider2D>().size = new Vector2(20f, 1f);
        Physics2D.SyncTransforms();
        MethodInfo probeInfo = typeof(Player).GetMethod("TryGetGroundSurface", PrivateInstance, null,
            new[] { typeof(Collider2D).MakeByRefType(), typeof(string).MakeByRefType(), typeof(bool).MakeByRefType() }, null);
        var probe = (GroundProbe)probeInfo.CreateDelegate(typeof(GroundProbe), player);
        MethodInfo parameterInfo = typeof(Player).GetMethod("HasParam", PrivateInstance | BindingFlags.Static);
        var parameter = (ParameterProbe)parameterInfo.CreateDelegate(typeof(ParameterProbe), parameterInfo.IsStatic ? null : player);
        ScoreUI hud = new GameObject("ScoreUI").AddComponent<ScoreUI>();
        hud.enabled = false;
        var updateHud = (Action)typeof(ScoreUI).GetMethod("Update", PrivateInstance).CreateDelegate(typeof(Action), hud);
        string result = Measure("ground probes", 2000, () => probe(out _, out _, out _), out long groundBytes);
        result += "\n" + Measure("jump/attack animator parameters", 2000, () => parameter(player.anim, "Jump", AnimatorControllerParameterType.Trigger), out long parameterBytes);
        result += "\n" + Measure("unchanged score HUD with optional texts absent", 2000, updateHud, out long hudBytes);
        UnityEngine.Debug.Log("[ARCADE PERFORMANCE]\n" + result);
        System.IO.File.AppendAllText("/tmp/arcade-performance-measurements.txt", result + "\n\n");
        Assert.Less(groundBytes, 4096, "Repeated ground checks must not allocate temporary arrays or tag strings");
        Assert.Less(parameterBytes, 4096, "Input must use cached Animator parameters");
        Assert.Less(hudBytes, 4096, "Unchanged scores must reuse their labels and avoid repeated scene searches");
    }

    [UnityTest] public IEnumerator FrameLimitSurvivesOverlappingVillageAndArcadeLifetimes()
    {
        yield return new EnterPlayMode();
        int originalLimit = Application.targetFrameRate;
        int originalVsync = QualitySettings.vSyncCount;
        try
        {
            Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
            var runtime = Tycoon.DeliveryRuntime.Ensure();
            Assert.AreEqual(60, Application.targetFrameRate);
            GameData data = new GameObject("ArcadeData").AddComponent<GameData>();
            Scene arcade = SceneManager.CreateScene("arcade frame pacing checks");
            typeof(GameData).GetMethod("InitializeForScene", PrivateInstance).Invoke(data, new object[] { arcade });
            Object.DestroyImmediate(runtime.gameObject);
            Assert.AreEqual(60, Application.targetFrameRate, "Unloading village interactions must not uncap an active arcade scene");
            data.PrepareForSceneTransition();
            Assert.AreEqual(-1, Application.targetFrameRate);
            runtime = Tycoon.DeliveryRuntime.Ensure();
            Application.targetFrameRate = 120;
            Object.DestroyImmediate(runtime.gameObject);
            Assert.AreEqual(120, Application.targetFrameRate, "An explicit frame limit must remain in effect");
        }
        finally { Application.targetFrameRate = originalLimit; QualitySettings.vSyncCount = originalVsync; }
    }

    [UnityTest] public IEnumerator NativePlayerAnimationOverridesAreReleasedWithTheirOwner()
    {
        yield return new EnterPlayMode();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/player.prefab");
        int before = CountRuntimeOverrides();
        for (int i = 0; i < 4; i++)
        {
            GameObject hero = Object.Instantiate(prefab);
            yield return null;
            Object.Destroy(hero);
            yield return null;
            yield return null;
        }
        Assert.AreEqual(before, CountRuntimeOverrides(), "Character retries must not retain native AnimatorOverrideControllers");
    }

    [UnityTest] public IEnumerator PinningPlayerXDoesNotWriteInterpolatedYBackIntoPhysics()
    {
        yield return new EnterPlayMode();
        GameObject hero = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/player.prefab"));
        Player player = hero.GetComponent<Player>();
        player.enabled = false;
        player.StopAllCoroutines();
        Set(player, "isAttacking", true);
        Set(player, "isGrounded", false);
        player.rb.gravityScale = 0f;
        Assert.AreEqual(RigidbodyInterpolation2D.Interpolate, player.rb.interpolation,
            "Arcade movement must interpolate its 50 Hz physics on the display frames");
        player.rb.position = new Vector2(player.gameplayX, 8f);
        player.rb.linearVelocity = new Vector2(0f, 3f);
        var movement = (Action)typeof(Player).GetMethod("HandleMovement", PrivateInstance).CreateDelegate(typeof(Action), player);
        int recorded = -1;
        for (int i = 0; i < 25;)
        {
            yield return null;
            if (recorded == Time.frameCount) continue;
            recorded = Time.frameCount;
            float physicalY = player.rb.position.y;
            movement();
            Physics2D.SyncTransforms();
            Assert.That(player.rb.position.y, Is.EqualTo(physicalY).Within(0.00001f), "A display update must not rewind vertical physics to the interpolated pose");
            Assert.That(player.rb.linearVelocity.y, Is.EqualTo(3f).Within(0.0001f));
            i++;
        }
        player.rb.position += Vector2.right;
        float y = player.rb.position.y;
        movement();
        Physics2D.SyncTransforms();
        Assert.That(player.rb.position.x, Is.EqualTo(player.gameplayX).Within(0.0001f));
        Assert.That(player.rb.position.y, Is.EqualTo(y).Within(0.00001f));
    }

    [UnityTest] public IEnumerator VillageArcadeRoundTripsReleaseSceneWorkAndUiSubscribers()
    {
        yield return new EnterPlayMode();
        GameObject root = new GameObject("PersistentVillageSave");
        root.SetActive(false);
        VillageManagement village = root.AddComponent<VillageManagement>();
        Set(village, "loaded", true); Set(village, "autoSaveOnChange", false);
        root.SetActive(true);
        village.SaveData.currentEnergy = 73;
        village.SaveData.boxInventories.Add(new Tycoon.BoxInventoryState { inventoryId = "round-trip-check" });
        int previousOverrides = -1;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Village.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitForSceneFrames("Village", 2);
            Assert.AreSame(village, VillageManagement.Instance);
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/arcade 1.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitForSceneFrames("arcade 1", 3);
            Assert.AreEqual(1, Object.FindObjectsByType<VillageManagement>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(73, village.CurrentEnergy);
            Assert.AreEqual("round-trip-check", village.SaveData.boxInventories[0].inventoryId);
            Assert.IsNull(Get(village, "restoreSceneRoutine"), "Arcade must not schedule village restoration. active=" +
                SceneManager.GetActiveScene().name + ", handle=" + SceneManager.GetActiveScene().handle + ", prepared=" + Get(village, "preparedSceneHandle") +
                ", paths=" + Object.FindObjectsByType<global::Path>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            Assert.IsNull(Get(village, "debugProxyAttachRoutine"), "Arcade must not keep searching for village debug hosts");
            Assert.AreEqual(0, Object.FindObjectsByType<Building>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            Assert.AreEqual(0, Object.FindObjectsByType<CustomerBlood>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            Assert.AreEqual(0, Object.FindObjectsByType<Tycoon.DeliveryRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            foreach (string eventName in new[] { "ResourceChanged", "SaveDataChanged", "BoxInventoriesReloaded" })
            {
                var callback = Get(village, eventName) as Delegate;
                Assert.AreEqual(0, callback?.GetInvocationList().Length ?? 0, eventName + " must not retain unloaded UI subscribers");
            }
            AssertNoDestroyedSubscribers(GameData.OnRageStart);
            int currentOverrides = CountRuntimeOverrides();
            if (previousOverrides >= 0) Assert.AreEqual(previousOverrides, currentOverrides, "Scene reloads must not accumulate native animation controllers");
            previousOverrides = currentOverrides;
            UnityEngine.Debug.Log("[ARCADE ROUND TRIP] cycle=" + cycle + ", overrides=" + currentOverrides + ", village UI subscribers=0");
        }
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, PrivateInstance).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, PrivateInstance).SetValue(target, value);

    private static IEnumerator WaitForSceneFrames(string name, int frames)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (SceneManager.GetActiveScene().name != name)
        {
            Assert.Less(Time.realtimeSinceStartup, deadline, "Scene did not finish loading: " + name);
            yield return null;
        }
        int until = Time.frameCount + frames;
        while (Time.frameCount < until) yield return null;
    }

    private static void AssertNoDestroyedSubscribers(Delegate callback)
    {
        if (callback == null) return;
        foreach (Delegate entry in callback.GetInvocationList())
            if (entry.Target is Object target) Assert.IsTrue(target != null, "Unloaded scene listener remains in a persistent event");
    }

    [UnityTest] public IEnumerator VillagePoolsReleaseUnloadedReferencesAndKeepOtherLoadedScenes()
    {
        yield return new EnterPlayMode();
        Scene original = SceneManager.GetActiveScene();
        Scene first = SceneManager.CreateScene("VillagePoolFirst");
        Scene second = SceneManager.CreateScene("VillagePoolSecond");
        SceneManager.SetActiveScene(first);
        Villan villanPrefab = new GameObject("VillanTemplate", typeof(BoxCollider2D), typeof(Rigidbody2D)).AddComponent<Villan>();
        TurretBullet bulletPrefab = new GameObject("BulletTemplate", typeof(BoxCollider2D), typeof(Rigidbody2D)).AddComponent<TurretBullet>();
        Villan firstVillan = Villan.Spawn(villanPrefab, null, 1);
        TurretBullet firstBullet = TurretBullet.Spawn(bulletPrefab, Vector3.zero, Vector2.right, Quaternion.identity);
        typeof(Villan).GetMethod("ReturnToPool", PrivateInstance).Invoke(firstVillan, null);
        typeof(TurretBullet).GetMethod("ReturnToPool", PrivateInstance).Invoke(firstBullet, null);
        SceneManager.SetActiveScene(second);
        Villan otherVillan = Villan.Spawn(villanPrefab, null, 1);
        TurretBullet otherBullet = TurretBullet.Spawn(bulletPrefab, Vector3.zero, Vector2.right, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(otherVillan.gameObject, second);
        SceneManager.MoveGameObjectToScene(otherBullet.gameObject, second);
        typeof(Villan).GetMethod("ReturnToPool", PrivateInstance).Invoke(otherVillan, null);
        typeof(TurretBullet).GetMethod("ReturnToPool", PrivateInstance).Invoke(otherBullet, null);
        yield return SceneManager.UnloadSceneAsync(first);
        var villans = (Dictionary<int, Queue<Villan>>)typeof(Villan).GetField("PoolsByPrefab", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var bullets = (Dictionary<int, Queue<TurretBullet>>)typeof(TurretBullet).GetField("PoolsByPrefab", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (var pool in villans.Values) foreach (Villan item in pool) Assert.IsTrue(item != null);
        foreach (var pool in bullets.Values) foreach (TurretBullet item in pool) Assert.IsTrue(item != null);
        Assert.IsTrue(otherVillan != null); Assert.IsTrue(otherBullet != null);
        SceneManager.SetActiveScene(original);
        yield return SceneManager.UnloadSceneAsync(second);
        Assert.AreEqual(0, villans.Count);
        Assert.AreEqual(0, bullets.Count);
    }

    [UnityTest] public IEnumerator PoolReturnsToItsOriginalTagOnceAndReusesWithoutGrowth()
    {
        yield return new EnterPlayMode();
        var prefabA = new GameObject("PoolA", typeof(SpriteRenderer)); prefabA.SetActive(false);
        var prefabB = new GameObject("PoolB", typeof(SpriteRenderer)); prefabB.SetActive(false);
        ObjectPool pool = new GameObject("Pool").AddComponent<ObjectPool>();
        pool.RegisterPool("A", prefabA, 1); pool.RegisterPool("B", prefabB, 1);
        GameObject instance = pool.SpawnFromPool("A", Vector3.one, Quaternion.identity);
        pool.ReturnToPool("B", instance);
        pool.ReturnToPool("A", instance);
        var seen = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            GameObject a = pool.SpawnFromPool("A", Vector3.zero, Quaternion.identity);
            GameObject b = pool.SpawnFromPool("B", Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(a); Assert.IsNotNull(b); Assert.AreNotSame(a, b);
            Assert.AreEqual("PoolA(Clone)", a.name); Assert.AreEqual("PoolB(Clone)", b.name);
            seen.Add(a.GetInstanceID()); seen.Add(b.GetInstanceID());
            pool.ReturnToPool("A", a); pool.ReturnToPool("A", a); pool.ReturnToPool("B", b);
        }
        Assert.AreEqual(2, seen.Count, "Repeated returns must not grow or corrupt a pool");
        Object.Destroy(pool.gameObject);
        yield return null;
        Assert.IsTrue(instance == null, "Destroying the pool must also release its inactive instances");
        Assert.IsTrue(ObjectPool.Instance == null);
    }

    [UnityTest] public IEnumerator BloodSpawnerReusesExistingBloodAndRetriesKeepThePoolAlive()
    {
        yield return new EnterPlayMode();
        GameData data = new GameObject("Data").AddComponent<GameData>(); data.enabled = false;
        ObjectPool pool = new GameObject("Pool").AddComponent<ObjectPool>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Blood.prefab");
        pool.RegisterPool("Blood", prefab, 4);
        BloodSpawner spawner = new GameObject("BloodSpawner").AddComponent<BloodSpawner>();
        spawner.bloodPrefab = prefab; spawner.enabled = false;
        IEnumerator routine = (IEnumerator)typeof(BloodSpawner).GetMethod("SpawnRoutine", PrivateInstance).Invoke(spawner, null);
        Assert.IsTrue(routine.MoveNext());
        var seen = new HashSet<int>();
        for (int i = 0; i < 100; i++)
        {
            Assert.IsTrue(routine.MoveNext());
            Blood[] active = Object.FindObjectsByType<Blood>(FindObjectsSortMode.None);
            Assert.AreEqual(1, active.Length);
            seen.Add(active[0].GetInstanceID());
            typeof(Blood).GetMethod("ReturnSelf", PrivateInstance).Invoke(active[0], null);
        }
        Assert.LessOrEqual(seen.Count, 4, "A spawner must reuse returned blood rather than permanently grow the pool");
        GameObject blood = pool.SpawnFromPool("Blood", Vector3.zero, Quaternion.identity);
        typeof(GameData).GetMethod("ResetUIAndObjects", PrivateInstance).Invoke(data, null);
        yield return null;
        Assert.IsTrue(blood != null, "Retry must return a pooled blood instance rather than destroy it");
        var cache = Get(pool, "cacheByObject") as System.Collections.IDictionary;
        Assert.AreEqual(4, cache.Count);
    }

    [UnityTest] public IEnumerator RepeatedBloodContactChecksAndShovelCleanupDoNotAllocate()
    {
        yield return new EnterPlayMode();
        MeasureBloodContactAllocations();
        yield return null;
    }

    private static void MeasureBloodContactAllocations()
    {
        var shovel = new GameObject("Shovel", typeof(BoxCollider2D)).AddComponent<GateShovel>();
        shovel.enabled = false;
        var blood = new GameObject("Blood", typeof(Rigidbody2D), typeof(CircleCollider2D)).AddComponent<Blood>();
        blood.enabled = false;
        var unrelated = new GameObject("UnrelatedCollider").AddComponent<BoxCollider2D>();
        MethodInfo contactMethod = typeof(GateShovel).GetMethod("OnTriggerStay2D", PrivateInstance);
        MethodInfo cleanupMethod = typeof(GateShovel).GetMethod("Update", PrivateInstance);
        MethodInfo shovelMethod = typeof(Blood).GetMethod("GetGateShovel", PrivateInstance);
        MethodInfo playerMethod = typeof(Blood).GetMethod("IsPlayerCollider", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(contactMethod, "GateShovel contact callback must be available");
        Assert.IsNotNull(cleanupMethod, "GateShovel cleanup must be available");
        Assert.IsNotNull(shovelMethod, "Blood shovel lookup must be available");
        Assert.IsNotNull(playerMethod, "Blood player lookup must be available");
        var contact = (Action<Collider2D>)contactMethod.CreateDelegate(typeof(Action<Collider2D>), shovel);
        var cleanup = (Action)cleanupMethod.CreateDelegate(typeof(Action), shovel);
        var getShovel = (Func<Collider2D, GateShovel>)shovelMethod.CreateDelegate(typeof(Func<Collider2D, GateShovel>), blood);
        var isPlayer = (Func<Collider2D, bool>)playerMethod.CreateDelegate(typeof(Func<Collider2D, bool>));
        string result = Measure("blood contact checks and shovel cleanup", 2000,
            () => { contact(unrelated); cleanup(); getShovel(unrelated); isPlayer(unrelated); }, out long bytes);
        UnityEngine.Debug.Log("[ARCADE CONTACT ALLOCATIONS] " + result);
        Assert.AreEqual(0, bytes, "Persistent contacts must not allocate missing-component messages or new cleanup delegates");
    }

    [UnityTest] public IEnumerator ShovelSuctionCountsEachBloodOnceAndReturnsItForReuse()
    {
        yield return new EnterPlayMode();
        var data = new GameObject("SuctionData").AddComponent<GameData>();
        data.enabled = false;
        var pool = new GameObject("SuctionPool").AddComponent<ObjectPool>();
        pool.RegisterPool("Blood", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Blood.prefab"), 1);
        var shovel = new GameObject("SuctionShovel", typeof(BoxCollider2D)).AddComponent<GateShovel>();
        shovel.baseRequiredBloodCount = 1;
        shovel.closeDelay = 0.01f;
        GameObject previous = null;
        int initialScore = data.GetO2Score();
        for (int cycle = 0; cycle < 2; cycle++)
        {
            GameObject instance = pool.SpawnFromPool("Blood", shovel.transform.position, Quaternion.identity);
            if (previous != null) Assert.AreSame(previous, instance);
            previous = instance;
            Blood blood = instance.GetComponent<Blood>();
            shovel.RegisterBloodInstance(blood);
            shovel.RegisterBloodInstance(blood);
            float deadline = Time.time + 2f;
            while ((instance.activeSelf || Get(shovel, "cycleRoutine") != null) && Time.time < deadline)
                yield return null;
            Assert.IsFalse(instance.activeSelf, "Suction must return the collected blood to its pool");
            Assert.IsNull(Get(shovel, "cycleRoutine"), "The collection cycle must finish before the next reuse");
            Assert.AreEqual(initialScore + cycle + 1, data.GetO2Score(), "Repeated contact must not count one blood twice");
        }
    }

    private static int CountRuntimeOverrides()
    {
        int count = 0;
        foreach (AnimatorOverrideController controller in Resources.FindObjectsOfTypeAll<AnimatorOverrideController>())
            if (!AssetDatabase.Contains(controller)) count++;
        return count;
    }

    private static string Measure(string label, int count, Action action, out long bytes)
    {
        for (int i = 0; i < 30; i++) action();
        var timer = new Stopwatch();
        var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 131072,
            ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        timer.Start();
        for (int i = 0; i < count; i++) action();
        timer.Stop();
        allocations.Stop();
        bytes = 0;
        for (int i = 0; i < Math.Min(allocations.Count, allocations.Capacity); i++) bytes += allocations.GetSample(i).Value;
        allocations.Dispose();
        return label + ": calls=" + count + ", bytes=" + bytes + ", ms=" + timer.Elapsed.TotalMilliseconds.ToString("F3");
    }
}
#endif
