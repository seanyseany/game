#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class MissileSmokeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PrefabPath = "Assets/Prefabs/Arcade/missile.prefab";

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PooledMissileKeepsSmokeAfterImpactOrExpiryAndRestartsOnReuse([Values(false, true)] bool impact)
    {
        yield return new EnterPlayMode();
        ObjectPool pool = PreparePool();
        var effectTemplate = new GameObject("ExplosionTemplate");
        effectTemplate.SetActive(false);
        pool.RegisterPool("Smoke", effectTemplate, 2);
        Missile missile = Spawn(pool);
        missile.explosionPrefabs = new GameObject[] { null };
        ParticleSystem particles = missile.GetComponentInChildren<ParticleSystem>();
        particles.Emit(5);
        Vector3 finalPosition = missile.transform.position;

        if (impact)
        {
            var data = new GameObject("MissileTestData").AddComponent<GameData>();
            data.enabled = false;
            data.selectedPlayerType = 3;
            // Only the collision callback's Player-3 branch is needed here; keep
            // the target inactive so unrelated player startup does not run.
            var target = new GameObject("ContactPlayer");
            target.SetActive(false);
            target.AddComponent<Player>();
            Collider2D contact = target.AddComponent<BoxCollider2D>();
            Invoke(missile, "OnTriggerEnter2D", contact);
            Invoke(missile, "OnTriggerEnter2D", contact);
        }
        else
        {
            typeof(Missile).GetField("spawnTime", Private).SetValue(missile, Time.time - 10f);
            Invoke(missile, "Update");
        }

        Assert.IsTrue(missile.gameObject.activeSelf, "Smoke must outlive the visible missile");
        Assert.IsFalse(missile.GetComponent<SpriteRenderer>().enabled);
        foreach (Collider2D collider in missile.GetComponentsInChildren<Collider2D>())
            Assert.IsFalse(collider.enabled, "An invisible missile must not keep hitting the player");
        var body = missile.GetComponent<Rigidbody2D>();
        Assert.IsFalse(body.simulated);
        Assert.AreEqual(Vector2.zero, body.linearVelocity);
        Assert.IsFalse(particles.isEmitting);
        Assert.Greater(particles.particleCount, 0, "Keep particles instead of clearing them at impact");
        Assert.IsTrue(particles.GetComponent<ParticleSystemRenderer>().enabled);
        Assert.AreEqual(ParticleSystemCullingMode.AlwaysSimulate, particles.main.cullingMode,
            "Smoke must finish and release its pool slot even outside the camera");
        Assert.IsNull(pool.SpawnFromPool("missile", Vector3.zero, Quaternion.identity));
        missile.ForceLaunch(100f);
        Assert.AreEqual(Vector2.zero, body.linearVelocity, "A spent missile cannot restart during smoke playback");
        var active = (Dictionary<string, HashSet<GameObject>>)typeof(ObjectPool).GetField("activeByTag", Private).GetValue(pool);
        Assert.AreEqual(impact ? 1 : 0, active["Smoke"].Count, "Duplicate contacts must not cause duplicate explosions");

        float deadline = Time.time + 2f;
        while (missile.gameObject.activeSelf && Time.time < deadline)
        {
            Assert.That(Vector3.Distance(finalPosition, missile.transform.position), Is.LessThan(0.0001f));
            yield return null;
        }
        Assert.IsFalse(missile.gameObject.activeSelf);
        Assert.AreEqual(0, particles.particleCount);

        Missile reused = Spawn(pool);
        Assert.AreSame(missile, reused);
        Assert.IsTrue(reused.GetComponent<SpriteRenderer>().enabled);
        Assert.IsTrue(reused.GetComponent<Collider2D>().enabled);
        Assert.IsTrue(body.simulated);
        Assert.AreEqual(Vector2.left * 15f, body.linearVelocity);
        Assert.IsTrue(particles.isPlaying);
        Assert.Less(particles.time, 0.01f);
        Assert.AreEqual(0, particles.particleCount);
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
            .GetComponentInChildren<ParticleSystem>().main.cullingMode, particles.main.cullingMode);
        pool.ReturnAllActive("missile", "Smoke");
    }

    [UnityTest]
    public IEnumerator ConfiguredExplosionPlaysAndDespawnsWithLegacySmokePoolPresent()
    {
        yield return new EnterPlayMode();
        ObjectPool pool = PreparePool();
        GameObject legacySmoke = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/missileSmoke.prefab");
        pool.RegisterPool("Smoke", legacySmoke, 1);
        Missile missile = Spawn(pool);
        Assert.AreEqual("BombDestroySmoke", missile.explosionPrefabs[0].name);

        Invoke(missile, "Explode");

        Assert.IsNull(GameObject.Find("missileSmoke(Clone)"));
        GameObject effect = GameObject.Find("BombDestroySmoke(Clone)");
        Assert.IsNotNull(effect);
        Assert.AreEqual(missile.transform.position, effect.transform.position);
        AnimationClip clip = effect.GetComponent<Animator>().runtimeAnimatorController.animationClips[0];
        Assert.IsFalse(clip.isLooping);
        var spriteBinding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
        Assert.IsNotEmpty(AnimationUtility.GetObjectReferenceCurve(clip, spriteBinding));

        float deadline = Time.time + clip.length + 1f;
        while (effect != null && Time.time < deadline) yield return null;
        Assert.IsTrue(effect == null, "An instantiated explosion must be destroyed after playback");
    }

    [UnityTest]
    public IEnumerator ExplosionWaitsForExtendedClipEvenWhenPlaybackPauses()
    {
        yield return new EnterPlayMode();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/BombDestroySmoke.prefab");
        GameObject effect = Object.Instantiate(prefab);
        Animator animator = effect.GetComponent<Animator>();
        var controller = new AnimatorOverrideController(animator.runtimeAnimatorController);
        var extendedClip = new AnimationClip();
        Sprite sprite = effect.GetComponent<SpriteRenderer>().sprite;
        var spriteBinding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(extendedClip, spriteBinding, new[]
        {
            new ObjectReferenceKeyframe { time = 0f, value = sprite },
            new ObjectReferenceKeyframe { time = 0.9f, value = sprite }
        });
        controller[controller.animationClips[0]] = extendedClip;
        animator.runtimeAnimatorController = controller;

        yield return new WaitForSeconds(0.6f);
        Assert.IsTrue(effect != null, "The extended clip must outlive the old fixed timer");
        animator.speed = 0f;
        yield return new WaitForSeconds(extendedClip.length);
        Assert.IsTrue(effect != null, "Pausing the Animator must also delay despawning");

        animator.speed = 1f;
        float deadline = Time.time + extendedClip.length + 1f;
        while (effect != null && Time.time < deadline) yield return null;
        Assert.IsTrue(effect == null, "The explosion must disappear after the extended clip ends");
        Object.Destroy(controller);
        Object.Destroy(extendedClip);
    }

    [UnityTest]
    public IEnumerator ForcedPoolResetClearsSmokeAndDoesNotReturnTheNextFlightEarly()
    {
        yield return new EnterPlayMode();
        ObjectPool pool = PreparePool();
        Missile missile = Spawn(pool);
        ParticleSystem particles = missile.GetComponentInChildren<ParticleSystem>();
        particles.Emit(5);
        Invoke(missile, "ReturnToPool");
        Assert.IsTrue(missile.gameObject.activeSelf);
        pool.ReturnAllActive("missile");
        Assert.IsFalse(missile.gameObject.activeSelf);
        Assert.AreEqual(0, particles.particleCount);
        Assert.AreSame(missile, Spawn(pool));
        yield return new WaitForSeconds(0.7f);
        Assert.IsTrue(missile.gameObject.activeSelf, "Old smoke completion must not return a reused missile");
        Assert.IsTrue(missile.GetComponent<Collider2D>().enabled);
        Assert.IsTrue(particles.isEmitting);
        pool.ReturnAllActive("missile");
    }

    [UnityTest]
    public IEnumerator UnpooledMissileIsDestroyedAfterItsRemainingSmokeFinishes()
    {
        yield return new EnterPlayMode();
        Missile missile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)).GetComponent<Missile>();
        ParticleSystem particles = missile.GetComponentInChildren<ParticleSystem>();
        particles.Emit(5);
        typeof(Missile).GetField("spawnTime", Private).SetValue(missile, Time.time - 10f);
        Invoke(missile, "Update");
        Assert.IsFalse(missile.GetComponent<SpriteRenderer>().enabled);
        Assert.Greater(particles.particleCount, 0);
        yield return null;
        Assert.IsTrue(missile != null, "Destroy must wait for the existing smoke");
        float deadline = Time.time + 2f;
        while (missile != null && Time.time < deadline) yield return null;
        Assert.IsTrue(missile == null);
    }

    [UnityTest]
    public IEnumerator MissileWithoutParticlesReturnsImmediately()
    {
        yield return new EnterPlayMode();
        var pool = new GameObject("MissileTestPool").AddComponent<ObjectPool>();
        var template = new GameObject("NoSmokeMissile");
        template.SetActive(false);
        template.AddComponent<Rigidbody2D>();
        template.AddComponent<BoxCollider2D>();
        template.AddComponent<Missile>();
        pool.RegisterPool("missile", template, 1);
        Missile missile = Spawn(pool);
        Invoke(missile, "ReturnToPool");
        Assert.IsFalse(missile.gameObject.activeSelf);
        Assert.AreSame(missile, Spawn(pool));
        pool.ReturnAllActive("missile");
    }

    private static ObjectPool PreparePool()
    {
        var pool = new GameObject("MissileTestPool").AddComponent<ObjectPool>();
        pool.RegisterPool("missile", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), 1);
        return pool;
    }

    private static Missile Spawn(ObjectPool pool)
    {
        Missile missile = pool.SpawnFromPool("missile", new Vector3(2f, 3f, -0.5f), Quaternion.identity).GetComponent<Missile>();
        missile.ForceLaunch(15f);
        return missile;
    }

    private static void Invoke(Missile missile, string method, params object[] args) =>
        typeof(Missile).GetMethod(method, Private).Invoke(missile, args);
}
#endif
