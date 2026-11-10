#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class RangedJumpInputPlayer : Player
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

public sealed class RangedJumpAnimationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnitySetUp] public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
    }

    [UnityTearDown] public IEnumerator TearDown()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator JumpOnlyLeavesAnEarlierAirAttackInNormalAndRageModes()
    {
        foreach (int type in new[] { 2, 4 })
        foreach (bool rage in new[] { false, true })
        {
            var hero = CreatePlayer(type, rage);
            StartAirAttack(hero);
            hero.attackPressed = hero.attackHeld = false;
            hero.jumpHeld = true;

            Invoke(hero, "HandleJump");
            hero.anim.Update(0f);

            AssertJump(hero);
            Assert.IsNull(Get(hero, "rangedFlyattackLatchRoutine"));
            // The previous attack's recovery must not restore Flyattack on a later frame.
            yield return new WaitForSeconds(0.6f);
            Invoke(hero, "HandleJump");
            hero.anim.Update(0f);
            AssertJump(hero);
            Object.DestroyImmediate(hero.gameObject);
        }
    }

    [UnityTest] public IEnumerator HoldingAttackKeepsAirAttackUntilOnlyJumpIsHeld()
    {
        foreach (int type in new[] { 2, 4 })
        foreach (bool rage in new[] { false, true })
        {
            var hero = CreatePlayer(type, rage);
            StartAirAttack(hero);
            hero.attackPressed = false;
            hero.jumpHeld = true;
            Invoke(hero, "HandleJump");
            hero.anim.Update(0f);
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Flyattack"));
            Assert.IsTrue((bool)Get(hero, "isAttacking"));
            Assert.AreEqual(hero.jumpSpeed, hero.rb.linearVelocity.y);

            hero.attackHeld = false;
            Invoke(hero, "HandleJump");
            hero.anim.Update(0f);
            AssertJump(hero);
            Object.DestroyImmediate(hero.gameObject);
        }
        yield break;
    }

    [UnityTest] public IEnumerator ANewAttackStillTakesOverFromJumpOnTheSameFrame()
    {
        foreach (int type in new[] { 2, 4 })
        foreach (bool rage in new[] { false, true })
        {
            var hero = CreatePlayer(type, rage);
            hero.jumpHeld = true;
            hero.attackHeld = hero.attackPressed = true;
            Invoke(hero, "HandleJump");
            Invoke(hero, "HandleAttackInput");
            hero.anim.Update(0f);
            Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Flyattack"));
            Assert.IsTrue((bool)Get(hero, "isAttacking"));
            Assert.AreEqual(hero.jumpSpeed, hero.rb.linearVelocity.y);
            Object.DestroyImmediate(hero.gameObject);
        }
        yield break;
    }

    private static RangedJumpInputPlayer CreatePlayer(int type, bool rage)
    {
        var go = new GameObject("Ranged jump regression", typeof(Rigidbody2D), typeof(Animator), typeof(SpriteRenderer));
        var animator = go.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/sprites/Arcade/HeroBase.controller");
        var hero = go.AddComponent<RangedJumpInputPlayer>();
        hero.enabled = false;
        hero.rb.simulated = false;
        hero.selectedType = type;
        Set(hero, "stats", StatsManager.GetPlayerStats(type, 1));
        Set(hero, "isRageMode", rage);
        Set(hero, "isGrounded", false);
        animator.Rebind();
        animator.Update(0f);
        return hero;
    }

    private static void StartAirAttack(RangedJumpInputPlayer hero)
    {
        hero.attackHeld = hero.attackPressed = true;
        Invoke(hero, "HandleAttackInput");
        hero.anim.Update(0f);
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Flyattack"));
    }

    private static void AssertJump(RangedJumpInputPlayer hero)
    {
        Assert.AreEqual("Jump", Get(hero, "currentAnim"));
        Assert.IsTrue(hero.anim.GetCurrentAnimatorStateInfo(0).IsName("Base Jump"));
        Assert.IsFalse((bool)Get(hero, "isAttacking"));
        Assert.AreEqual(hero.jumpSpeed, hero.rb.linearVelocity.y);
    }

    private static object Get(Player hero, string field) => typeof(Player).GetField(field, Private).GetValue(hero);
    private static void Set(Player hero, string field, object value) => typeof(Player).GetField(field, Private).SetValue(hero, value);
    private static void Invoke(Player hero, string method) => typeof(Player).GetMethod(method, Private).Invoke(hero, null);
}
#endif
