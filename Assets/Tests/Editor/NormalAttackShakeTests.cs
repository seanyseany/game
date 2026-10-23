#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class NormalAttackShakeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator NormalStompGroundAttacksShakeOnceIncludingTheirLandTransition()
    {
        yield return new EnterPlayMode();
        Player hero = Prepare(out var camera);
        var floor = new GameObject("Floor", typeof(BoxCollider2D)); floor.tag = "floor";
        floor.transform.position = hero.GetComponent<Collider2D>().bounds.min - Vector3.up * 0.5f;
        floor.GetComponent<BoxCollider2D>().size = new Vector2(20f, 1f);
        Physics2D.SyncTransforms();
        foreach (int type in new[] { 1, 5 })
        {
            SetType(hero, type); Requests(camera).Clear();
            Set(hero, "lastGroundTag", "floor"); Set(hero, "isGrounded", true);
            Begin(hero, type == 1 ? "P1_GroundAttack" : "P5_GroundAttack");
            AssertNormalSettings(camera, 1);
            Begin(hero, "ResolveGroundAttackEndTransition", type);
            AssertNormalSettings(camera, 1); // Ground Attack -> Land must not shake twice.
            hero.StopAllCoroutines();
        }
    }

    [UnityTest] public IEnumerator NormalStompLandingsShakeOnFloorButNotPlatform()
    {
        yield return new EnterPlayMode();
        Player hero = Prepare(out var camera);
        foreach (int type in new[] { 1, 5 })
        foreach (string surface in new[] { "floor", "platform" })
        {
            SetType(hero, type); Requests(camera).Clear();
            Set(hero, "lastGroundTag", surface); Set(hero, "isGrounded", true);
            Begin(hero, type == 1 ? "P1_Land" : "P5_Land", type == 1 ? new object[] { true } : new object[] { true, true });
            AssertNormalSettings(camera, surface == "floor" ? 1 : 0);
            hero.StopAllCoroutines();
        }
    }

    [UnityTest] public IEnumerator NormalStompGroundAttacksDoNotShakeOnPlatform()
    {
        yield return new EnterPlayMode();
        Player hero = Prepare(out var camera);
        foreach (int type in new[] { 1, 5 })
        {
            SetType(hero, type); Requests(camera).Clear();
            Set(hero, "lastGroundTag", "platform"); Set(hero, "isGrounded", true);
            Begin(hero, type == 1 ? "P1_GroundAttack" : "P5_GroundAttack");
            AssertNormalSettings(camera, 0);
            hero.StopAllCoroutines();
        }
    }

    [UnityTest] public IEnumerator NormalPlayerThreeShakesAtAttackStartOnGroundAndInAir()
    {
        yield return new EnterPlayMode();
        Player hero = Prepare(out var camera);
        SetType(hero, 3);
        foreach (bool grounded in new[] { false, true })
        {
            Requests(camera).Clear(); Set(hero, "isAttacking", false); Set(hero, "isGrounded", grounded);
            Begin(hero, "P3_AirStomp", false);
            AssertNormalSettings(camera, 1);
            Begin(hero, "P3_AirStomp", false); // Rejected repeated input adds no shake.
            AssertNormalSettings(camera, 1);
            hero.StopAllCoroutines();
        }
    }

    [UnityTest] public IEnumerator NormalBallAndEmptyBallShakeAtFloorImpactOnlyOnce()
    {
        yield return new EnterPlayMode();
        Player hero = Prepare(out var camera);
        foreach (int type in new[] { 2, 4 })
        foreach (string surfaceTag in new[] { "floor", "platform" })
        {
            SetType(hero, type); Requests(camera).Clear(); Set(hero, "isGrounded", false);
            Begin(hero, type == 2 ? "P2_FireRanged" : "P4_FireLightning");
            AssertNormalSettings(camera, 0); // Neither ranged character shakes on input.
            var ball = ActiveBall(type == 2 ? hero.projectilePoolTag : hero.dummyProjectilePoolTag);
            Assert.IsNotNull(ball); Assert.AreSame(hero, ball.Owner);
            var surface = new GameObject(surfaceTag, typeof(BoxCollider2D)); surface.tag = surfaceTag;
            var collider = surface.GetComponent<Collider2D>();
            for (int i = 0; i < 3; i++) Invoke(ball, "HandleTriggerContact", collider);
            Invoke(ball, "FixedUpdate");
            AssertNormalSettings(camera, surfaceTag == "floor" ? 1 : 0);
            Assert.IsFalse(ball.gameObject.activeInHierarchy, "Impact returns the native pooled projectile");
            Invoke(ball, "FixedUpdate");
            AssertNormalSettings(camera, surfaceTag == "floor" ? 1 : 0);
            Object.DestroyImmediate(surface);
            hero.StopAllCoroutines();
        }
    }

    [UnityTest] public IEnumerator ReusedProjectileClearsFloorShakeAndRageOwnerSuppressesNormalImpactShake()
    {
        yield return new EnterPlayMode();
        Player hero = Prepare(out var camera); SetType(hero, 2);
        Set(hero, "isGrounded", false); Begin(hero, "P2_FireRanged");
        var ball = ActiveBall(hero.projectilePoolTag);
        var floor = new GameObject("Floor", typeof(BoxCollider2D)); floor.tag = "floor";
        ball.gameObject.SetActive(false); ball.gameObject.SetActive(true);
        Invoke(ball, "HandleTriggerContact", floor.GetComponent<Collider2D>());
        Invoke(ball, "FixedUpdate");
        AssertNormalSettings(camera, 0); // A pooled activation must explicitly opt in again.

        Begin(hero, "P2_FireRanged"); ball = ActiveBall(hero.projectilePoolTag);
        Set(hero, "isRageMode", true);
        Invoke(ball, "HandleTriggerContact", floor.GetComponent<Collider2D>());
        Invoke(ball, "FixedUpdate");
        AssertNormalSettings(camera, 0);
    }

    private static Player Prepare(out CameraShakeManager camera)
    {
        new GameObject("Data").AddComponent<GameData>().enabled = false;
        new GameObject("Pool").AddComponent<ObjectPool>();
        var view = new GameObject("ShakeCamera", typeof(Camera)); view.tag = "MainCamera";
        camera = view.AddComponent<CameraShakeManager>(); camera.enabled = false;
        camera.normalPlayer1Multiplier = 0.4f;
        camera.normalPlayer2Multiplier = 0.7f;
        camera.normalPlayer3Multiplier = 0.9f;
        camera.normalPlayer4Multiplier = 1.2f;
        camera.normalPlayer5Multiplier = 1.4f;
        camera.defaultDuration = 0.23f;
        camera.defaultFrequency = 17f;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/player.prefab");
        var hero = Object.Instantiate(prefab).GetComponent<Player>();
        hero.StopAllCoroutines(); hero.enabled = false; hero.rb.simulated = false;
        // Shake must not depend on smoke/hitbox art being assigned.
        hero.hitboxPrefabP1 = hero.hitboxPrefabP3 = hero.hitboxPrefabP5 = null;
        hero.smokeEffectPrefab = hero.smokeEffectPrefabP3 = hero.smokeEffectPrefabP5 = null;
        SetType(hero, 1);
        return hero;
    }

    private static void SetType(Player hero, int type)
    {
        GameData.Instance.selectedPlayerType = type;
        Set(hero, "stats", StatsManager.GetPlayerStats(type, 1));
        Set(hero, "isAttacking", false); Set(hero, "isLanding", false); Set(hero, "isTransformLock", false);
        Set(hero, "currentAnim", "Walk");
    }

    private static ProjectileBall ActiveBall(string tag)
    {
        var active = (Dictionary<string, HashSet<GameObject>>)typeof(ObjectPool).GetField("activeByTag", Private).GetValue(ObjectPool.Instance);
        foreach (var obj in active[tag]) if (obj.activeInHierarchy) return obj.GetComponent<ProjectileBall>();
        return null;
    }

    private static IList Requests(CameraShakeManager camera) => (IList)typeof(CameraShakeManager).GetField("activeShakes", Private).GetValue(camera);
    private static void AssertNormalSettings(CameraShakeManager camera, int count)
    {
        IList requests = Requests(camera); Assert.AreEqual(count, requests.Count);
        float multiplier = camera.GetMultiplier((CameraShakeManager.ShakeEvent)(GameData.Instance.selectedPlayerType - 1));
        foreach (object request in requests)
        {
            var type = request.GetType();
            Assert.That((float)type.GetField("magnitudeX").GetValue(request), Is.EqualTo(camera.defaultMagnitudeX * multiplier).Within(0.000001f));
            Assert.That((float)type.GetField("magnitudeY").GetValue(request), Is.EqualTo(camera.defaultMagnitudeY * multiplier).Within(0.000001f));
            Assert.AreEqual(camera.defaultDuration, type.GetField("duration").GetValue(request));
            Assert.AreEqual(camera.defaultFrequency, type.GetField("frequency").GetValue(request));
        }
    }
    private static void Set(Player hero, string field, object value) => typeof(Player).GetField(field, Private).SetValue(hero, value);
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Begin(Player hero, string method, params object[] args) => ((IEnumerator)Invoke(hero, method, args)).MoveNext();
}
#endif
