#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SpawnIntroTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private float previousCaptureDeltaTime;

    [UnitySetUp] public IEnumerator Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        previousCaptureDeltaTime = Time.captureDeltaTime;
        Time.captureDeltaTime = 1f / 60f;
        new GameObject("Data").AddComponent<GameData>().enabled = false;
    }

    [UnityTearDown] public IEnumerator Cleanup()
    {
        Time.captureDeltaTime = previousCaptureDeltaTime;
        if (GameData.Instance != null) Object.Destroy(GameData.Instance.gameObject);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator IntroRejectsDistantFloorAndFindsNearbyFloorPastItsOwnCollider()
    {
        Player hero = PreparePlayer();
        var floor = new GameObject("Floor", typeof(BoxCollider2D)); floor.tag = "floor";
        floor.transform.position = Vector3.down * 0.5f;
        floor.GetComponent<BoxCollider2D>().size = new Vector2(20f, 1f);
        PlaceFeet(hero, 2f);
        Assert.IsFalse(Snap(hero, out _, out _), "Do not teleport from the shovel to a floor two units below.");
        PlaceFeet(hero, 0.1f);
        Assert.IsTrue(Snap(hero, out float y, out var support));
        Assert.AreSame(floor.GetComponent<Collider2D>(), support);
        Assert.That(y - hero.transform.position.y, Is.EqualTo(-0.1f + Physics2D.defaultContactOffset).Within(0.001f));
        yield break;
    }

    [UnityTest] public IEnumerator IntroUsesActualSlopedShovelSurfaceIncludingUntaggedChildColliders()
    {
        Player hero = PreparePlayer();
        var shovel = new GameObject("Shovel").AddComponent<GateShovel>();
        shovel.gameObject.tag = "Excavator";
        var child = new GameObject("Shovel polygon", typeof(PolygonCollider2D));
        child.transform.SetParent(shovel.transform);
        var polygon = child.GetComponent<PolygonCollider2D>();
        polygon.points = new[] { new Vector2(-4f, 0f), new Vector2(4f, -2f), new Vector2(4f, -3f), new Vector2(-4f, -3f) };
        PlaceFeet(hero, -0.9f);
        Assert.IsTrue(Snap(hero, out float y, out var support));
        Assert.AreSame(polygon, support);
        float resultingFoot = hero.GetComponent<Collider2D>().bounds.min.y + y - hero.transform.position.y;
        Assert.That(resultingFoot, Is.InRange(-1.05f, -0.9f), "Use the local slope hit, not the polygon's much higher bounding-box top.");
        yield break;
    }

    [UnityTest] public IEnumerator NativeTrainExitWalksOverShovelWithoutVerticalTeleportAndRestoresPhysics(
        [Values(30, 60, 120)] int frameRate)
    {
        Time.captureDeltaTime = 1f / frameRate;
        var train = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/TRAIN.prefab"));
        Train trainController = train.GetComponent<Train>(); trainController.enabled = false;
        train.transform.position = new Vector3(-3.81f, -0.98f);
        foreach (Animator animator in train.GetComponentsInChildren<Animator>()) animator.enabled = false;
        foreach (MechaLeg leg in train.GetComponentsInChildren<MechaLeg>()) leg.enabled = false;
        var floor = new GameObject("Floor", typeof(BoxCollider2D)); floor.tag = "floor";
        floor.transform.position = new Vector3(0f, -4.22f);
        floor.GetComponent<BoxCollider2D>().size = new Vector2(30f, 1f);
        Player hero = PreparePlayer();
        hero.introStartDelay = hero.introGateLeadTime = 0f;
        hero.rb.interpolation = RigidbodyInterpolation2D.None;
        Physics2D.SyncTransforms();
        yield return null;
        IEnumerator routine = (IEnumerator)typeof(Player).GetMethod("CoPlaySpawnIntro", Private).Invoke(hero, null);
        Assert.IsTrue(routine.MoveNext());
        Assert.That(hero.transform.position.x, Is.EqualTo(trainController.playerBoardingPoint.position.x).Within(0.3f), "Start at the actual train exit.");
        float previousY = hero.transform.position.y;
        bool touchedShovel = false;
        float largestDrop = 0f;
        int samples = 0;
        while (true)
        {
            yield return null;
            bool running = routine.MoveNext();
            float drop = previousY - hero.transform.position.y;
            largestDrop = Mathf.Max(largestDrop, drop);
            Assert.Less(drop, 0.5f, $"Exit must not snap several units down to floor in one frame: sample={samples}, dt={Time.deltaTime}, previousY={previousY}, position={hero.transform.position}.");
            previousY = hero.transform.position.y;
            if (Snap(hero, out _, out var support) && support.GetComponentInParent<GateShovel>() != null)
                touchedShovel = true;
            samples++;
            if (!running) break;
            Assert.Less(samples, 400, "The exit must finish.");
        }
        Debug.Log($"[TRAIN EXIT] fps={frameRate}, samples={samples}, largestDrop={largestDrop}, shovel={touchedShovel}, final={hero.transform.position}");
        Assert.IsTrue(touchedShovel, "The native exit route must pass over the shovel surface.");
        Assert.That(hero.transform.position.x, Is.EqualTo(hero.gameplayX).Within(0.001f));
        Assert.AreEqual(1.4f, hero.rb.gravityScale, 0.001f);
        Assert.IsTrue(hero.GetComponent<Collider2D>().enabled);
        Assert.IsFalse(hero.IsSpawnOrTransferActive);
        Assert.AreEqual(RigidbodyType2D.Dynamic, hero.rb.bodyType);
        float finishY = hero.rb.position.y;
        float contactDeadline = Time.time + 0.5f;
        Collider2D heroCollider = hero.GetComponent<Collider2D>();
        Collider2D floorCollider = floor.GetComponent<Collider2D>();
        while (!heroCollider.IsTouching(floorCollider) && Time.time < contactDeadline)
            yield return null;
        Assert.That(hero.rb.position.y, Is.EqualTo(finishY).Within(0.1f), "Restoring physics must keep the player on the floor.");
        Assert.IsTrue(heroCollider.IsTouching(floorCollider),
            "The intro must finish on the floor, rather than hand back control while still in the air.");
    }

    [UnityTest] public IEnumerator InterruptedIntroRestoresOriginalBodyType()
    {
        Player hero = PreparePlayer();
        hero.introStartDelay = hero.introGateLeadTime = 0f;
        foreach (bool disableObject in new[] { false, true })
        {
            hero.gameObject.SetActive(true);
            IEnumerator routine = (IEnumerator)typeof(Player).GetMethod("CoPlaySpawnIntro", Private).Invoke(hero, null);
            Assert.IsTrue(routine.MoveNext());
            Assert.AreEqual(RigidbodyType2D.Kinematic, hero.rb.bodyType);
            if (disableObject)
            {
                hero.enabled = true;
                hero.gameObject.SetActive(false);
            }
            else typeof(Player).GetMethod("StopSpawnIntroRoutine", Private).Invoke(hero, null);
            Assert.AreEqual(RigidbodyType2D.Dynamic, hero.rb.bodyType, "Cancellation and object reuse must restore normal physics.");
        }
        yield break;
    }

    private static Player PreparePlayer()
    {
        Player hero = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/player.prefab")).GetComponent<Player>();
        hero.StopAllCoroutines(); hero.enabled = false;
        hero.rb.gravityScale = 0f;
        return hero;
    }

    private static void PlaceFeet(Player hero, float y)
    {
        Physics2D.SyncTransforms();
        hero.transform.position += Vector3.up * (y - hero.GetComponent<Collider2D>().bounds.min.y);
        Physics2D.SyncTransforms();
    }

    private static bool Snap(Player hero, out float y, out Collider2D support)
    {
        object[] args = { 0f, null };
        bool result = (bool)typeof(Player).GetMethod("TryGetIntroGroundSnapY", Private).Invoke(hero, args);
        y = (float)args[0]; support = (Collider2D)args[1];
        return result;
    }
}
#endif
