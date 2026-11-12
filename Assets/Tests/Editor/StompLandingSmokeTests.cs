#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class StompSmokeInputPlayer : Player
{
    public int selectedType;
    public bool jumpHeld;
    public bool attackHeld;
    public bool attackPressed;
    protected override bool UsesArcadeSystems => false;
    public override int CharacterType => selectedType;
    protected override bool ReadKey(KeyCode key) =>
        (key == KeyCode.Space && jumpHeld) || (key == KeyCode.DownArrow && attackHeld);
    protected override bool ReadKeyDown(KeyCode key) => key == KeyCode.DownArrow && attackPressed;
    protected override bool ReadKeyUp(KeyCode key) => false;
    protected override void Start() { }
}

public sealed class StompLandingSmokeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnitySetUp] public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        new GameObject("Pool").AddComponent<ObjectPool>();
    }

    [UnityTearDown] public IEnumerator TearDown()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator InterruptedGroundAttackDoesNotSuppressTheNextStompSmoke()
    {
        foreach (int type in new[] { 1, 5 })
        foreach (bool pooled in new[] { false, true })
        {
            var hero = CreatePlayer(type, pooled);
            Begin(hero, type == 1 ? "P1_GroundAttack" : "P5_GroundAttack");
            AssertSmoke(hero, 1);
            hero.transform.position = Vector3.up * 3f;
            Set(hero, "isGrounded", false);
            Attack(hero);
            Land(hero);
            AssertSmoke(hero, 2);
            Object.DestroyImmediate(hero.gameObject);
            ClearSmoke();
        }
        yield break;
    }

    [UnityTest] public IEnumerator GroundedRecoveryProcessesTheImpactBeforeClearingAttackState()
    {
        foreach (int type in new[] { 1, 5 })
        {
            var hero = CreatePlayer(type, true);
            Set(hero, "isGrounded", false);
            Attack(hero);
            Set(hero, "isGrounded", true);
            Set(hero, "lastGroundedTime", Time.time - 0.2f);
            Set(hero, "attackStateStartTime", Time.time - 0.4f);
            Set(hero, "flyattackProtectedUntil", Time.time - 1f);
            hero.rb.linearVelocity = Vector2.zero;
            Invoke(hero, "RecoverGroundedStompState");
            AssertSmoke(hero, 1);
            Object.DestroyImmediate(hero.gameObject);
            ClearSmoke();
        }
        yield break;
    }

    [UnityTest] public IEnumerator JumpAndAttackDuringLandStartANewStompWithItsOwnImpact()
    {
        foreach (int type in new[] { 1, 5 })
        {
            var hero = CreatePlayer(type, true);
            hero.landDuration = 0.1f;
            Set(hero, "isGrounded", false);
            Attack(hero);
            Land(hero);
            AssertSmoke(hero, 1);

            hero.jumpHeld = true;
            Invoke(hero, "HandleJump");
            Assert.IsFalse((bool)Get(hero, "isGrounded"));
            hero.transform.position = Vector3.up * 3f;
            Attack(hero);
            hero.jumpHeld = false;
            hero.rb.linearVelocity = Vector2.zero;
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue((bool)Get(hero, "isAttacking"), "The previous landing must not finish the new stomp.");
            Assert.IsFalse((bool)Get(hero, "isLanding"));
            Land(hero);
            AssertSmoke(hero, 2);
            // Processing the same grounded frame again must not create another impact.
            Invoke(hero, "HandleLandSequencing");
            AssertSmoke(hero, 2);
            Object.DestroyImmediate(hero.gameObject);
            ClearSmoke();
        }
    }

    [UnityTest] public IEnumerator ChildColliderOfFloorUsesRageSmokeAndPlatformUsesNormalSmoke()
    {
        foreach (int type in new[] { 1, 5 })
        foreach (bool pooled in new[] { false, true })
        foreach (string tag in new[] { "floor", "platform" })
        {
            var hero = CreatePlayer(type, pooled, tag, childCollider: true);
            Invoke(hero, "SpawnSmokeEffect");
            if (tag == "floor") AssertSmoke(hero, 1);
            else
            {
                Assert.AreEqual(1, Count("NormalStompSmoke(Clone)"));
                Assert.AreEqual(0, Count("RageStompSmoke(Clone)"));
            }
            Object.DestroyImmediate(hero.gameObject);
            ClearSmoke();
        }
        yield break;
    }

    [UnityTest] public IEnumerator GroundAttackRecoveryDoesNotDuplicateRageSmoke()
    {
        foreach (int type in new[] { 1, 5 })
        {
            var hero = CreatePlayer(type, true);
            Begin(hero, type == 1 ? "P1_GroundAttack" : "P5_GroundAttack");
            hero.StartCoroutine((IEnumerator)Invoke(hero, "ResolveGroundAttackEndTransition", type));
            yield return null;
            AssertSmoke(hero, 1);
            Object.DestroyImmediate(hero.gameObject);
            ClearSmoke();
        }
    }

    [UnityTest] public IEnumerator PhysicalFloorImpactEmitsOnceWithJumpAndAttackHeldOrReleased()
    {
        foreach (int type in new[] { 1, 5 })
        foreach (bool held in new[] { false, true })
        {
            var hero = CreatePlayer(type, true);
            hero.rb.bodyType = RigidbodyType2D.Dynamic;
            hero.rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            hero.rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            hero.transform.position = Vector3.up * 2f;
            Set(hero, "isGrounded", false);
            hero.jumpHeld = true;
            Invoke(hero, "HandleJump");
            Attack(hero); // Same-frame jump + attack must finish as a stomp.
            Assert.Less(hero.rb.linearVelocity.y, 0f);
            hero.jumpHeld = hero.attackHeld = held;
            float deadline = Time.time + 0.8f;
            while (Count("RageStompSmoke(Clone)") == 0 && Time.time < deadline)
            {
                yield return new WaitForFixedUpdate();
                Invoke(hero, "SyncGroundedState");
                Invoke(hero, "RecoverGroundedStompState");
                Invoke(hero, "HandleJump");
                Invoke(hero, "HandleAttackInput");
                Invoke(hero, "HandleLandSequencing");
            }
            AssertSmoke(hero, 1);
            Object.DestroyImmediate(hero.gameObject);
            ClearSmoke();
        }
    }

    [UnityTest] public IEnumerator RepeatedInterpolatedStompsKeepEverySmokeOnTheFloor()
    {
        foreach (int type in new[] { 1, 5 })
        foreach (bool pooled in new[] { false, true })
        {
            var hero = CreatePlayer(type, pooled);
            hero.rb.bodyType = RigidbodyType2D.Dynamic;
            hero.rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            hero.rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            hero.rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            hero.landDuration = 0.12f;
            for (int impact = 1; impact <= 6; impact++)
            {
                hero.jumpHeld = true;
                Invoke(hero, "HandleJump");
                yield return new WaitForSeconds(impact % 2 == 0 ? 0.02f : 0.06f);
                // Reproduce Update's ground sync and held-jump processing before
                // the new attack input, including collision callbacks during the wait.
                Invoke(hero, "SyncGroundedState");
                Invoke(hero, "RecoverGroundedStompState");
                Invoke(hero, "HandleJump");
                Assert.IsFalse((bool)Get(hero, "isGrounded"), $"P{type}, impact={impact}: attack starts in the air.");
                Attack(hero);
                hero.jumpHeld = false;
                float deadline = Time.time + 1f;
                while (Count("RageStompSmoke(Clone)") < impact && Time.time < deadline)
                {
                    yield return new WaitForFixedUpdate();
                    Invoke(hero, "SyncGroundedState");
                    Invoke(hero, "RecoverGroundedStompState");
                    Invoke(hero, "HandleJump");
                    Invoke(hero, "HandleLandSequencing");
                }
                AssertSmoke(hero, impact);
                foreach (Transform smoke in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (smoke.name.EndsWith("StompSmoke(Clone)"))
                        Assert.That(smoke.position.y, Is.EqualTo(0f).Within(0.0001f),
                            $"P{type}, pooled={pooled}, impact={impact}: smoke must stay at the floor art offset.");
            }
            Object.DestroyImmediate(hero.gameObject);
            ClearSmoke();
        }
    }

    private static StompSmokeInputPlayer CreatePlayer(int type, bool pooled, string tag = "floor", bool childCollider = false)
    {
        var floor = new GameObject("StompSurface"); floor.tag = tag;
        var surface = childCollider ? new GameObject("Untagged floor collider") : floor;
        if (childCollider) surface.transform.SetParent(floor.transform);
        surface.transform.position = Vector3.down * 0.5f;
        surface.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f);
        var go = new GameObject("Stomp input regression", typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(Animator), typeof(SpriteRenderer));
        go.transform.position = Vector3.up * 0.5f;
        go.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        go.GetComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/sprites/Arcade/HeroBase.controller");
        var hero = go.AddComponent<StompSmokeInputPlayer>();
        hero.enabled = false;
        hero.selectedType = type;
        hero.useEffectPool = pooled;
        hero.smokeEffectPrefab = hero.smokeEffectPrefabP5 = Template("NormalStompSmoke");
        hero.rage1SmokePrefab = hero.rageAttackSmokePrefab = Template("RageStompSmoke");
        hero.rageAttackSmokePrefabSecondary = Template("SecondaryStompSmoke");
        hero.smokePoolTag = hero.smokeP5PoolTag = "StompTestNormal";
        hero.rage1SmokePoolTag = hero.rageAttackSmokePoolTag = "StompTestRage";
        hero.rageAttackSmokePoolTagSecondary = "StompTestSecondary";
        Set(hero, "stats", StatsManager.GetPlayerStats(type, 1));
        Set(hero, "isRageMode", true);
        Set(hero, "currentAnim", "Walk");
        Set(hero, "lastGroundTag", tag);
        Physics2D.SyncTransforms();
        hero.anim.Rebind(); hero.anim.Update(0f);
        return hero;
    }

    private static GameObject Template(string name)
    {
        // Reuse the same source across pooled iterations.
        var existing = GameObject.Find(name);
        if (existing != null) return existing;
        var go = new GameObject(name); go.transform.position = Vector3.right * 1000f;
        return go;
    }

    private static void Attack(StompSmokeInputPlayer hero)
    {
        hero.attackPressed = true;
        Invoke(hero, "HandleAttackInput");
        hero.attackPressed = false;
    }

    private static void Land(StompSmokeInputPlayer hero)
    {
        hero.transform.position = Vector3.up * 0.5f;
        hero.rb.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        Invoke(hero, "SyncGroundedState");
        Set(hero, "flyattackProtectedUntil", Time.time - 1f);
        Invoke(hero, "HandleLandSequencing");
    }

    private static void AssertSmoke(StompSmokeInputPlayer hero, int impacts)
    {
        Assert.AreEqual(impacts, Count("RageStompSmoke(Clone)"), $"P{hero.selectedType} primary rage smoke");
        Assert.AreEqual(hero.selectedType == 5 ? impacts : 0, Count("SecondaryStompSmoke(Clone)"));
        Assert.AreEqual(0, Count("NormalStompSmoke(Clone)"), "A floor rage impact must not use normal smoke.");
    }

    private static int Count(string name)
    {
        int count = 0;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name == name) count++;
        return count;
    }

    private static void ClearSmoke()
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            if (t.name == "StompSurface") Object.DestroyImmediate(t.gameObject);
            else if (t.name.EndsWith("StompSmoke(Clone)"))
                if (!ObjectPool.Instance.TryReturnActive(t.gameObject)) Object.DestroyImmediate(t.gameObject);
        }
    }

    private static object Get(Player hero, string name) => typeof(Player).GetField(name, Private).GetValue(hero);
    private static void Set(Player hero, string name, object value) => typeof(Player).GetField(name, Private).SetValue(hero, value);
    private static object Invoke(Player hero, string name, params object[] args) => typeof(Player).GetMethod(name, Private).Invoke(hero, args);
    private static void Begin(Player hero, string name, params object[] args) => ((IEnumerator)Invoke(hero, name, args)).MoveNext();
}
#endif
