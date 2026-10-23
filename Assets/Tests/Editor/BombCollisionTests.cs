#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class BombCollisionTests
{
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator FastBombsHitThinTriggerTargetsWithoutAProjectileRigidbody()
    {
        yield return new EnterPlayMode();
        VerifyFastTargets();
        yield return null;
    }

    private static void VerifyFastTargets()
    {
        float[] frameTimes = { 1f / 240f, 1f / 60f, 1f / 15f, 0.2f };
        foreach (float dt in frameTimes)
        {
            Collider2D target = Target("ThinTarget", new Vector3(1.13f, dt * 100f, 1f));
            target.GetComponent<CircleCollider2D>().radius = 0.015f;
            Bomb bomb = Projectile(new Vector3(0f, target.transform.position.y, 0f));
            bomb.speed = 60f;
            bomb.SetTarget(target.transform);
            Physics2D.SyncTransforms();
            for (int i = 0; i < 100 && !Exploded(bomb); i++) Advance(bomb, dt);
            Assert.IsTrue(Exploded(bomb), "Missed target at frame duration " + dt);
            Assert.IsFalse(target.gameObject.activeSelf, "Confirmed impact must damage before another physics step");
            Assert.Less(bomb.transform.position.x, target.transform.position.x, "Stop at contact, not beyond the target");
            int explosions = Object.FindObjectsByType<BombHitBox>(FindObjectsSortMode.None).Length;
            Advance(bomb, dt);
            Assert.AreEqual(explosions, Object.FindObjectsByType<BombHitBox>(FindObjectsSortMode.None).Length);
        }
    }

    [UnityTest]
    public IEnumerator SweepsIgnoreOtherObstaclesAndFollowTheMovingTarget()
    {
        yield return new EnterPlayMode();
        VerifyMovingTarget();
        yield return null;
    }

    private static void VerifyMovingTarget()
    {
        Collider2D decoy = Target("OtherObstacle", new Vector3(0.6f, 0f, 0f));
        Collider2D target = Target("SwingingObstacle", new Vector3(3f, 0f, 0f));
        Bomb bomb = Projectile(Vector3.zero);
        bomb.SetTarget(target.transform);
        Physics2D.SyncTransforms();
        Advance(bomb, 0.05f);
        Assert.IsFalse(Exploded(bomb));
        Assert.IsTrue(decoy.gameObject.activeSelf);
        // Move the selected saw as a RopeAnchor's dynamic child would move.
        target.attachedRigidbody.position = new Vector2(3f, 1f);
        Physics2D.SyncTransforms();
        for (int i = 0; i < 20 && !Exploded(bomb); i++) Advance(bomb, 0.05f);
        Assert.IsTrue(Exploded(bomb));
        Assert.IsFalse(target.gameObject.activeSelf);
        Assert.IsTrue(decoy.gameObject.activeSelf, "Only the selected obstacle may stop the projectile");
    }

    [UnityTest]
    public IEnumerator TargetCrossingTheBombBetweenFramesMustStillHit()
    {
        yield return new EnterPlayMode();
        VerifyCrossingTarget();
        yield return null;
    }

    private static void VerifyCrossingTarget()
    {
        Collider2D target = Target("FastSwingingSaw", new Vector3(0f, 1f, 0f));
        Bomb bomb = Projectile(Vector3.zero);
        bomb.speed = 1f;
        bomb.SetTarget(target.transform);
        Physics2D.SyncTransforms();
        target.attachedRigidbody.position = new Vector2(0f, -1f);
        Physics2D.SyncTransforms();
        Advance(bomb, 0.01f);
        Assert.IsTrue(Exploded(bomb), "The saw swept across the bomb between frames");
        Assert.IsFalse(target.gameObject.activeSelf);
    }

    [UnityTest]
    public IEnumerator VisibleTargetCrossingStillHitsBeforePhysicsTransformsSynchronize()
    {
        yield return new EnterPlayMode();
        Collider2D target = Target("SawBeforeNextPhysicsStep", new Vector3(4f, 0f, 0f));
        Bomb bomb = Projectile(new Vector3(-1f, 0f, 0f));
        bomb.speed = 10f;
        Physics2D.SyncTransforms();
        bomb.SetTarget(target.transform);
        // A scrolling parent/render pose can change before the next fixed-step sync.
        target.transform.position = Vector3.zero;
        Advance(bomb, 0.2f);
        Assert.IsTrue(Exploded(bomb), "Crossing the visible target must not depend on the physics query's older pose");
        Assert.IsFalse(target.enabled, "A confirmed target hit must apply damage immediately");
        yield return null;
    }

    [UnityTest]
    public IEnumerator MatchingXDoesNotHitATargetAboveTheBombOrOutsideItsCapsuleWidth()
    {
        yield return new EnterPlayMode();
        foreach (float y in new[] { 3f, 0.16f })
        {
            Collider2D target = Target("SawPassingAbove", new Vector3(4f, y, 0f));
            Bomb bomb = Projectile(Vector3.zero);
            bomb.maxTiltAngle = 0f;
            Physics2D.SyncTransforms();
            bomb.SetTarget(target.transform);
            target.transform.position = new Vector3(-4f, y, 0f);
            Advance(bomb, 0f);
            Assert.IsFalse(Exploded(bomb), "X crossing alone must not explode a bomb at Y=" + y);
            Assert.IsTrue(target.enabled);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator TriggerBombTargetingAndFlightReuseBuffersWithoutManagedAllocations()
    {
        yield return new EnterPlayMode();
        Collider2D target = Target("CachedCircleTarget", new Vector3(50f, 0f, 0f));
        Bomb bomb = Projectile(Vector3.zero);
        var advance = (System.Action<float>)System.Delegate.CreateDelegate(typeof(System.Action<float>), bomb,
            typeof(Bomb).GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic));
        for (int i = 0; i < 100; i++) { bomb.SetTarget(target.transform); advance(0f); }
        using var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192,
            ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 2000; i++) { bomb.SetTarget(target.transform); advance(0f); }
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        allocations.Stop();
        Assert.IsTrue(allocations.Valid);
        long bytes = 0;
        for (int i = 0; i < System.Math.Min(allocations.Count, allocations.Capacity); i++) bytes += allocations.GetSample(i).Value;
        Debug.Log("[BOMB TARGET SWEEP] shots=2000, gc_bytes=" + bytes + ", ms=" +
            elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        Assert.AreEqual(0L, bytes, "Target assignment and circle sweep should reuse their collections");
        Assert.IsFalse(Exploded(bomb));
        yield return null;
    }

    [UnityTest]
    public IEnumerator NonCircularTargetKeepsItsShapeCastAndIgnoresOtherObstacles()
    {
        yield return new EnterPlayMode();
        Collider2D decoy = Target("DecoyBeforeBox", new Vector3(0.5f, 0f, 0f));
        var target = new GameObject("BoxTarget");
        target.transform.position = new Vector3(2f, 0f, 0f);
        var collider = target.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one * 0.2f;
        collider.isTrigger = true;
        target.AddComponent<Obstacle>();
        Bomb bomb = Projectile(Vector3.zero);
        bomb.SetTarget(target.transform);
        Physics2D.SyncTransforms();
        Advance(bomb, 0.2f);
        Assert.IsTrue(Exploded(bomb));
        Assert.IsFalse(collider.enabled);
        Assert.IsTrue(decoy.enabled);
        Assert.Less(bomb.transform.position.x, target.transform.position.x);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ActualPooledBombsHitActualRopeAnchorPrefabs()
    {
        yield return new EnterPlayMode();
        System.Action launch = PrepareActualPrefabs(out System.Func<bool> allHit);
        yield return new WaitForSeconds(0.15f);
        launch();
        float deadline = Time.time + 3f;
        while (!allHit() && Time.time < deadline) yield return null;
        Assert.IsTrue(allHit(), "Actual prefab obstacles must receive actual pooled bomb impacts");
    }

    private static System.Action PrepareActualPrefabs(out System.Func<bool> allHit)
    {
        var pool = new GameObject("ObjectPool").AddComponent<ObjectPool>();
        pool.RegisterPool("Bomb", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Bomb.prefab"), 8);
        pool.RegisterPool("BombHitBox", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/BombHitBox.prefab"), 8);
        var ropePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/RopeAnchor.prefab");
        var targets = new Collider2D[8];
        for (int i = 0; i < targets.Length; i++)
        {
            GameObject rope = Object.Instantiate(ropePrefab, new Vector3(5f + (i % 3), i * 25f, 0f), Quaternion.identity);
            targets[i] = rope.GetComponentInChildren<Obstacle>().GetComponent<Collider2D>();
        }
        allHit = () => System.Array.TrueForAll(targets, target => !target.enabled);
        return () =>
        {
            for (int i = 0; i < targets.Length; i++)
            {
                Bomb bomb = pool.SpawnFromPool("Bomb", new Vector3(-3f, i * 25f, -4f), Quaternion.identity).GetComponent<Bomb>();
                bomb.SetTarget(targets[i].transform);
            }
        };
    }

    [UnityTest]
    public IEnumerator Stage421RopePlacementsHitWhileTheStageScrolls()
    {
        yield return new EnterPlayMode();
        System.Func<bool> step = PrepareStage421Ropes();
        float deadline = Time.time + 9f;
        bool complete = false;
        while (!(complete = step()) && Time.time < deadline) yield return null;
        Assert.IsTrue(complete, "All nine stage 4-21 rope placements must be hit as they scroll past the launcher");
    }

    [UnityTest]
    public IEnumerator Stage421RopesStillHitWhenObstacleContactSlowsAnInFlightBomb()
    {
        yield return new EnterPlayMode();
        int previousLimit = Application.targetFrameRate;
        int previousVSync = QualitySettings.vSyncCount;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        try
        {
            var data = new GameObject("SlowdownGameData").AddComponent<GameData>();
            data.enabled = false;
            data.decelerateTime = 0.05f;
            data.recoverTime = 0.1f;
            data.pushSpeed = 0.3f;
            var manager = new GameObject("SlowdownStageManager").AddComponent<StageManager>();
            manager.enabled = false;
            manager.phaseBaseSpeed = 6f;
            System.Func<bool> step = PrepareStage421Ropes(data);
            float deadline = Time.time + 14f;
            bool complete = false;
            while (!(complete = step()) && Time.time < deadline) yield return null;
            if (!complete)
            {
                Debug.Log("[BOMB SLOWDOWN] multiplier=" + data.stageSpeedMult);
                foreach (Obstacle obstacle in Object.FindObjectsByType<Obstacle>(FindObjectsSortMode.None))
                    Debug.Log("[BOMB SLOWDOWN TARGET] visible=" + obstacle.transform.position + ", body=" + obstacle.GetComponent<Rigidbody2D>().position);
                foreach (Bomb bomb in Object.FindObjectsByType<Bomb>(FindObjectsSortMode.None))
                    Debug.Log("[BOMB SLOWDOWN PROJECTILE] position=" + bomb.transform.position);
            }
            Assert.IsTrue(complete, "All nine real rope targets must hit through contact deceleration, pushing, and recovery");
        }
        finally
        {
            Application.targetFrameRate = previousLimit;
            QualitySettings.vSyncCount = previousVSync;
        }
    }

    private static System.Func<bool> PrepareStage421Ropes(GameData slowdownData = null)
    {
        var camera = new GameObject("MainCamera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.aspect = 16f / 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/stage 4/stage 4-21.prefab");
        SwingMotor[] originals = prefab.GetComponentsInChildren<SwingMotor>(true);
        Assert.AreEqual(9, originals.Length);
        var phase = new GameObject("Stage421RopePlacements");
        phase.SetActive(false);
        phase.transform.position = new Vector3(8f, 0f, 0f);
        phase.AddComponent<PhaseLayoutSnapshot>();
        phase.AddComponent<Mover>().baseSpeed = prefab.GetComponent<Mover>().baseSpeed;
        var targets = new Collider2D[originals.Length];
        var launched = new bool[targets.Length];
        var bombs = new Bomb[targets.Length];
        for (int i = 0; i < originals.Length; i++)
        {
            Transform source = originals[i].transform.parent;
            GameObject rope = Object.Instantiate(source.gameObject, phase.transform, false);
            rope.transform.localPosition = source.localPosition;
            rope.transform.localRotation = source.localRotation;
            targets[i] = rope.GetComponentInChildren<Obstacle>().GetComponent<Collider2D>();
        }
        phase.SetActive(true);
        var pool = new GameObject("ObjectPool").AddComponent<ObjectPool>();
        pool.RegisterPool("Bomb", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Bomb.prefab"), targets.Length);
        pool.RegisterPool("BombHitBox", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/BombHitBox.prefab"), targets.Length);
        float contactStarted = -1f;
        bool contactEnded = false;
        return () =>
        {
            if (slowdownData != null && contactStarted >= 0f && !contactEnded && Time.time - contactStarted > 0.35f)
            {
                slowdownData.EndObstacleContact();
                contactEnded = true;
            }
            bool allHit = true;
            for (int i = 0; i < targets.Length; i++)
            {
                if (!targets[i].enabled) continue;
                allHit = false;
                if (launched[i] || targets[i].bounds.center.x > 5.5f) continue;
                bombs[i] = pool.SpawnFromPool("Bomb", new Vector3(-7f, 1.4f, -4f), Quaternion.identity).GetComponent<Bomb>();
                bombs[i].SetTarget(targets[i].transform);
                launched[i] = true;
                if (slowdownData != null && contactStarted < 0f)
                {
                    contactStarted = Time.time;
                    slowdownData.BeginObstacleContact();
                }
            }
            return allHit;
        };
    }

    [UnityTest]
    public IEnumerator BombHitsATriggerObstacleSwingingOnARopeHinge()
    {
        yield return new EnterPlayMode();
        System.Func<bool> wasHit = PrepareSwingingObstacle();
        float deadline = Time.time + 2f;
        while (!wasHit() && Time.time < deadline) yield return null;
        Assert.IsTrue(wasHit(), "A moving, trigger-based obstacle on a rope must receive the bomb impact");
    }

    private static System.Func<bool> PrepareSwingingObstacle()
    {
        var anchor = new GameObject("RopeAnchor", typeof(Rigidbody2D));
        anchor.transform.position = new Vector3(4f, 2f, 0f);
        anchor.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        Collider2D target = Target("Obstacle", new Vector3(4f, 0f, 1f));
        target.transform.SetParent(anchor.transform, true);
        var body = target.attachedRigidbody;
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 1f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        var hinge = target.gameObject.AddComponent<HingeJoint2D>();
        hinge.connectedBody = anchor.GetComponent<Rigidbody2D>();
        hinge.autoConfigureConnectedAnchor = false;
        hinge.anchor = new Vector2(0f, 2f);
        hinge.connectedAnchor = Vector2.zero;
        body.linearVelocity = new Vector2(4f, 0f);
        Bomb bomb = Projectile(new Vector3(-3f, 0f, 0f));
        bomb.enabled = true;
        bomb.SetTarget(target.transform);
        return () => !target.enabled;
    }

    [UnityTest]
    public IEnumerator ForcedTargetArrivalDoesNotOvershootAndRetargetingDoesNotKeepOldColliders()
    {
        yield return new EnterPlayMode();
        VerifyForcedAndRetargeted();
        yield return null;
    }

    private static void VerifyForcedAndRetargeted()
    {
        GameObject forced = new GameObject("ForcedTarget");
        forced.transform.position = new Vector3(0.13f, 3f, 0f);
        Bomb bomb = Projectile(new Vector3(0f, 3f, 0f));
        bomb.SetTarget(forced.transform);
        Advance(bomb, 0.2f);
        Assert.IsTrue(Exploded(bomb));
        Assert.AreEqual(forced.transform.position, bomb.transform.position);

        Collider2D first = Target("OldTarget", new Vector3(0.5f, 5f, 0f));
        Collider2D second = Target("NewTarget", new Vector3(2f, 5f, 0f));
        Bomb reused = Projectile(new Vector3(0f, 5f, 0f));
        reused.SetTarget(first.transform);
        reused.gameObject.SetActive(false);
        reused.gameObject.SetActive(true);
        reused.SetTarget(second.transform);
        Physics2D.SyncTransforms();
        Advance(reused, 0.2f);
        Assert.IsTrue(Exploded(reused));
        Assert.IsTrue(first.gameObject.activeSelf);
        Assert.IsFalse(second.gameObject.activeSelf);
    }

    private static Collider2D Target(string name, Vector3 position)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        var collider = root.AddComponent<CircleCollider2D>();
        collider.radius = 0.1f;
        collider.isTrigger = true;
        var body = root.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        root.AddComponent<ObstacleInfo>().type = ObstacleType.Saw;
        root.AddComponent<Obstacle>();
        return collider;
    }

    private static Bomb Projectile(Vector3 position)
    {
        var root = new GameObject("TestBomb");
        root.transform.position = position;
        var collider = root.AddComponent<CapsuleCollider2D>();
        collider.size = new Vector2(0.2f, 0.08f);
        collider.direction = CapsuleDirection2D.Horizontal;
        collider.isTrigger = true;
        Bomb bomb = root.AddComponent<Bomb>();
        bomb.enabled = false; // deterministic frame durations, retain the real collider
        bomb.speed = 20f;
        bomb.rotateLerpSpeed = 0f;
        bomb.hitboxFallbackPrefab = new GameObject("ImpactTemplate", typeof(BombHitBox));
        Assert.IsNull(root.GetComponent<Rigidbody2D>(), "Match the supplied Bomb prefab");
        return bomb;
    }

    private static void Advance(Bomb bomb, float dt) => typeof(Bomb).GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bomb, new object[] { dt });
    private static bool Exploded(Bomb bomb) => (bool)typeof(Bomb).GetField("exploded", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bomb);
}
#endif
