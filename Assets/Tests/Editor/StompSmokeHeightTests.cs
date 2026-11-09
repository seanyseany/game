#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class StompSmokeHeightTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string SmokeName = "StompHeightSmoke";

    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator FloorSmokeHeightIsIndependentOfPlayerHeightForEveryStompSmokePath()
    {
        yield return new EnterPlayMode();
        new GameObject("Data").AddComponent<GameData>().enabled = false;
        new GameObject("Pool").AddComponent<ObjectPool>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/player.prefab");
        Player hero = Object.Instantiate(prefab).GetComponent<Player>();
        hero.StopAllCoroutines(); hero.enabled = false;
        hero.rb.interpolation = RigidbodyInterpolation2D.None;
        hero.rb.gravityScale = 0f;
        hero.hitboxPrefabP1 = hero.hitboxPrefabP5 = null;
        var smoke = new GameObject(SmokeName);
        smoke.transform.position = Vector3.right * 1000f;
        hero.smokeEffectPrefab = hero.smokeEffectPrefabP5 = smoke;
        hero.rage1SmokePrefab = hero.rageAttackSmokePrefab = hero.rageAttackSmokePrefabSecondary = smoke;
        hero.smokePoolTag = "HeightNormal1"; hero.smokeP5PoolTag = "HeightNormal5";
        hero.rage1SmokePoolTag = "HeightRage1";
        hero.rageAttackSmokePoolTag = "HeightRage5A";
        hero.rageAttackSmokePoolTagSecondary = "HeightRage5B";
        var floor = new GameObject("Floor", typeof(BoxCollider2D)); floor.tag = "floor";
        var floorCol = floor.GetComponent<BoxCollider2D>();
        floorCol.size = new Vector2(20f, 1f);
        floor.transform.position = Vector3.down * 0.5f; // Surface Y = 0.
        Physics2D.SyncTransforms();
        var body = hero.GetComponent<Collider2D>();
        // Align the real body with the floor to record the existing art offset at rest.
        hero.transform.position += Vector3.up * (floorCol.bounds.max.y - body.bounds.min.y);
        Physics2D.SyncTransforms();
        float restingPlayerY = hero.transform.position.y;
        float restingSmokeY = restingPlayerY - 0.5f;

        foreach (int type in new[] { 1, 5 })
        foreach (bool rage in new[] { false, true })
        foreach (bool pooled in new[] { false, true })
        foreach (string route in new[] { "SpawnSmokeEffect", type == 1 ? "P1_Land" : "P5_Land" })
        foreach (float floorShift in new[] { 0f, 1.25f })
        foreach (float gap in new[] { 0f, 0.08f, 0.2f })
        {
            string context = $"P{type}, rage={rage}, pool={pooled}, {route}, floor={floorShift}, gap={gap}";
            GameData.Instance.selectedPlayerType = type;
            hero.useEffectPool = pooled;
            Set(hero, "stats", StatsManager.GetPlayerStats(type, 1));
            Set(hero, "isRageMode", rage); Set(hero, "isGrounded", true);
            Set(hero, "isTransformLock", false); Set(hero, "currentAnim", "Flyattack");
            Set(hero, "lastGroundTag", "floor"); Set(hero, "lastGroundCol", floorCol);
            Set(hero, "skipNextP1RageLandingSmoke", false); Set(hero, "skipNextP5RageLandingSmoke", false);
            floor.transform.position = new Vector3(0f, floorShift - 0.5f);
            hero.transform.position = new Vector3(0f, restingPlayerY + floorShift + gap, hero.transform.position.z);
            Physics2D.SyncTransforms();
            MethodInfo method = typeof(Player).GetMethod(route, Private);
            if (route == "SpawnSmokeEffect") method.Invoke(hero, null);
            else
            {
                object[] args = type == 1 ? new object[] { true } : new object[] { true, true };
                ((IEnumerator)method.Invoke(hero, args)).MoveNext();
            }

            int count = 0;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name != SmokeName + "(Clone)") continue;
                count++;
                Assert.That(t.position.y, Is.EqualTo(restingSmokeY + floorShift).Within(0.0001f), context);
                Assert.That(t.position.x, Is.EqualTo(hero.transform.position.x).Within(0.0001f), context);
                Assert.AreEqual(-0.5f, t.position.z, context);
                if (!ObjectPool.Instance.TryReturnActive(t.gameObject)) Object.DestroyImmediate(t.gameObject);
            }
            Assert.AreEqual(type == 5 && rage ? 2 : 1, count, context);
        }
    }

    private static void Set(Player hero, string name, object value) => typeof(Player).GetField(name, Private).SetValue(hero, value);
}
#endif
