#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class CustomerSurprisePlayTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private int previousCaptureFramerate;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        previousCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = 60;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (Application.isPlaying)
        {
            Time.captureFramerate = previousCaptureFramerate;
            yield return new ExitPlayMode();
        }
    }

    [UnityTest]
    public IEnumerator ClickPausesForPointThreeSecondsThenResumesMovementAndRestoresCurrentSprite()
    {
        var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
        camera.transform.position = Vector3.back * 10f;
        var root = new GameObject("Customer", typeof(SpriteRenderer), typeof(BoxCollider2D));
        var customer = root.AddComponent<CustomerBlood>();
        SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
        var texture = new Texture2D(2, 2);
        Sprite walking = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Sprite carrying = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Sprite surprise = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Set(customer, "walkingSprite", walking);
        Set(customer, "carryingWalkSprite", carrying);
        Set(customer, "surpriseSprite", surprise);
        renderer.sprite = walking;
        root.GetComponent<BoxCollider2D>().size = Vector2.one;
        yield return WaitForGameSeconds(0.05f);
        Physics2D.SyncTransforms();
        Assert.AreSame(camera, Camera.main, "The click test requires an active main camera.");
        Assert.IsFalse(Tycoon.DeliveryRuntime.BlocksWorldInput);
        Assert.IsTrue(root.GetComponent<Collider2D>().OverlapPoint(customer.transform.position),
            "The test collider must be initialized before simulating a click.");

        Click(customer, camera, Vector3.right * 10f);
        Assert.AreSame(walking, renderer.sprite);
        customer.StartCoroutine(customer.MoveDirectToPointRoutine(Vector3.right * 3f));
        yield return WaitForGameSeconds(0.1f);
        Assert.Greater(customer.transform.position.x, 0f);
        Click(customer, camera, customer.transform.position);
        Rigidbody2D body = root.GetComponent<Rigidbody2D>();
        Vector2 stoppedPosition = body.position;
        Assert.AreSame(surprise, renderer.sprite);
        yield return WaitForGameSeconds(0.08f);
        Assert.That(Vector2.Distance(stoppedPosition, body.position), Is.LessThan(0.001f),
            "Surprise must pause the existing movement.");
        Assert.IsTrue(body.simulated);
        Assert.IsTrue(root.GetComponent<Collider2D>().enabled);
        Assert.AreSame(surprise, renderer.sprite);

        yield return WaitForGameSeconds(0.1f);
        Set(customer, "purchaseFinished", true);
        Type visualState = typeof(CustomerBlood).GetNestedType("VisualState", BindingFlags.NonPublic);
        typeof(CustomerBlood).GetMethod("ApplyVisualState", Private)
            .Invoke(customer, new[] { Enum.Parse(visualState, "CarryingItem") });
        Assert.AreSame(surprise, renderer.sprite);
        Click(customer, camera, customer.transform.position);
        yield return WaitForGameSeconds(0.18f);
        Assert.AreSame(surprise, renderer.sprite, "A second click must restart the 0.3-second duration.");
        Assert.That(Vector2.Distance(stoppedPosition, body.position), Is.LessThan(0.001f));
        yield return WaitForGameSeconds(0.22f);
        Assert.AreSame(carrying, renderer.sprite, "Expiry must restore the latest underlying state.");
        Assert.Greater(customer.transform.position.x, stoppedPosition.x, "Movement must resume after 0.3 seconds.");
        Assert.Greater(Get<float>(customer, "surpriseWalkPauseTime"), 0.3f,
            "Restarting surprise must also account for the total walking time that was paused.");
        yield return WaitForGameSeconds(1.6f);
        Assert.That(customer.transform.position.x, Is.EqualTo(3f).Within(0.03f));

        Click(customer, camera, customer.transform.position);
        Assert.AreSame(surprise, renderer.sprite);
        root.SetActive(false);
        Assert.AreSame(carrying, renderer.sprite);
        root.SetActive(true);
        Assert.AreSame(carrying, renderer.sprite, "Pooled customers must not retain the surprise state.");
        Object.Destroy(root);
        Object.Destroy(walking); Object.Destroy(carrying); Object.Destroy(surprise); Object.Destroy(texture);
        yield return null;
    }

    [UnityTest]
    public IEnumerator TradingBlocksSurpriseAndReceivingCancelsAnExistingPause()
    {
        var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
        camera.transform.position = Vector3.back * 10f;
        var root = new GameObject("Customer", typeof(SpriteRenderer), typeof(BoxCollider2D));
        var customer = root.AddComponent<CustomerBlood>();
        SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
        var texture = new Texture2D(2, 2);
        Sprite walking = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Sprite receiving = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Sprite carrying = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Sprite surprise = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Set(customer, "walkingSprite", walking);
        Set(customer, "receiveItemSprite", receiving);
        Set(customer, "carryingWalkSprite", carrying);
        Set(customer, "surpriseSprite", surprise);
        renderer.sprite = walking;
        root.GetComponent<BoxCollider2D>().size = Vector2.one;
        yield return WaitForGameSeconds(0.05f);

        foreach (string flag in new[] { "purchaseSequenceRunning", "waitingAtCounter" })
        {
            Set(customer, flag, true);
            Click(customer, camera, customer.transform.position);
            Assert.AreSame(walking, renderer.sprite, flag + " must block surprise.");
            Set(customer, flag, false);
        }

        var building = new GameObject("Shop", typeof(BoxCollider2D)).AddComponent<Building>();
        building.GetComponent<BoxCollider2D>().size = Vector2.one * 4f;
        Set(building, "isPlaced", true);
        Set(customer, "pendingPurchaseBuilding", building);
        Click(customer, camera, customer.transform.position);
        Assert.AreSame(walking, renderer.sprite, "Approaching the shop must keep the purchase moving.");
        Set(customer, "waitingAtCounter", true);
        Vector2 customerScreenPoint = camera.WorldToScreenPoint(customer.transform.position);
        MethodInfo tryHold = typeof(Building).GetMethod("TryBeginPointerHold", Private);
        Assert.IsFalse((bool)tryHold.Invoke(building, new object[] { customerScreenPoint, -1 }),
            "A press over the customer must not start a shop hold that could cancel the trade.");
        Assert.IsTrue((bool)tryHold.Invoke(building, new object[] { (Vector2)camera.WorldToScreenPoint(Vector3.right), -1 }),
            "The shop must still accept a press away from the customer.");
        typeof(Building).GetMethod("ReleasePointerHold", Private).Invoke(building, null);
        Set(customer, "waitingAtCounter", false);
        Set(customer, "pendingPurchaseBuilding", null);

        Click(customer, camera, customer.transform.position);
        Assert.AreSame(surprise, renderer.sprite);
        Set(customer, "purchaseSequenceRunning", true);
        customer.StartCoroutine(customer.ReceivePurchasedItemRoutine(null));
        Assert.AreSame(receiving, renderer.sprite, "Receiving an item must cancel an existing surprise.");
        Click(customer, camera, customer.transform.position);
        Assert.AreSame(receiving, renderer.sprite, "Touching during the trade must not replace the receiving sprite.");
        yield return WaitForGameSeconds(0.6f);
        Assert.AreSame(carrying, renderer.sprite);
        Click(customer, camera, customer.transform.position);
        Assert.AreSame(surprise, renderer.sprite, "Touching must work again after the trade finishes.");
        Object.Destroy(root);
        Object.Destroy(building.gameObject);
        Object.Destroy(walking); Object.Destroy(receiving); Object.Destroy(carrying); Object.Destroy(surprise); Object.Destroy(texture);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SurprisePausesBothApproachSegmentsAndStopsWhenCustomerBoardsLift()
    {
        var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
        camera.transform.position = Vector3.back * 10f;
        var root = new GameObject("Customer", typeof(SpriteRenderer), typeof(BoxCollider2D));
        var customer = root.AddComponent<CustomerBlood>();
        root.GetComponent<BoxCollider2D>().size = Vector2.one;
        var body = root.GetComponent<Rigidbody2D>();
        var renderer = root.GetComponent<SpriteRenderer>();
        var texture = new Texture2D(2, 2);
        Sprite walking = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Sprite surprise = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Set(customer, "walkingSprite", walking);
        Set(customer, "surpriseSprite", surprise);
        renderer.sprite = walking;

        var departureWay = new GameObject("Departure Way").AddComponent<Way>();
        var arrivalWay = new GameObject("Arrival Way").AddComponent<Way>();
        Set(customer, "currentWay", departureWay);
        Set(customer, "spawnedAtTime", Time.time - 6f);
        Set(VillageManagement.EnsureInstance(), "autoSaveOnChange", false);

        var liftRoot = new GameObject("Lift");
        liftRoot.SetActive(false);
        var lift = liftRoot.AddComponent<Lift>();
        var pickup = new GameObject("Pickup").transform;
        pickup.position = new Vector3(1f, 0.5f, 0f);
        var dropoff = new GameObject("Drop-off").transform;
        dropoff.position = new Vector3(1f, 2f, 0f);
        Type routeType = typeof(Lift).GetNestedType("PlatformRoute", BindingFlags.NonPublic);
        Array routes = Array.CreateInstance(routeType, 1);
        object route = Activator.CreateInstance(routeType, true);
        routeType.GetField("dropPoints").SetValue(route, new[] { pickup, dropoff });
        routes.SetValue(route, 0);
        Set(lift, "connectedWays", new[] { departureWay, arrivalWay });
        Set(lift, "platformRoutes", routes);
        Set(lift, "platformPrefab", new GameObject("Platform Prefab"));
        Set(lift, "customerRideChance", 1f);
        liftRoot.SetActive(true);

        var segment = (IEnumerator)typeof(CustomerBlood).GetMethod("TravelWaySegmentRoutine", Private)
            .Invoke(customer, new object[] { Vector3.right * 2f });
        customer.StartCoroutine(segment);
        yield return WaitForGameSeconds(0.1f);
        Assert.That(body.position.x, Is.InRange(0.01f, 0.99f));
        Assert.AreEqual(0f, body.position.y);
        yield return ClickAndCheckPause(customer, camera, renderer, body, surprise);

        float deadline = Time.time + 2f;
        while (body.position.y < 0.1f && Time.time < deadline)
            yield return null;
        Assert.That(body.position.y, Is.InRange(0.1f, 0.49f), "The customer must continue walking toward the platform.");
        yield return ClickAndCheckPause(customer, camera, renderer, body, surprise);

        deadline = Time.time + 2f;
        while (customer.transform.parent == null && Time.time < deadline)
            yield return null;
        Assert.IsNotNull(customer.transform.parent, "The customer must board the selected lift after the pause.");
        Click(customer, camera, customer.transform.position);
        Assert.AreSame(walking, renderer.sprite, "The boarding passenger must use the normal sprite.");

        deadline = Time.time + 2f;
        while (customer.transform.parent != null && Time.time < deadline)
            yield return null;
        Assert.IsNull(customer.transform.parent);
        Assert.AreEqual(dropoff.position, customer.transform.position);
        Object.Destroy(walking);
        Object.Destroy(surprise);
        Object.Destroy(texture);
    }

    [UnityTest]
    public IEnumerator RepeatedClickFeedbackDoesNotDelayOrRedirectDespawn()
    {
        var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
        camera.transform.position = Vector3.back * 10f;
        var root = new GameObject("Customer", typeof(SpriteRenderer), typeof(BoxCollider2D));
        var customer = root.AddComponent<CustomerBlood>();
        root.GetComponent<BoxCollider2D>().size = Vector2.one;
        var texture = new Texture2D(2, 2);
        Sprite surprise = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Set(customer, "surpriseSprite", surprise);

        var entrance = new GameObject("Entrance").AddComponent<Entrance>();
        entrance.transform.position = Vector3.left;
        var entranceManagement = new GameObject("Entrance Manager").AddComponent<EntranceManagement>();
        var way = new GameObject("Route").AddComponent<Way>();
        var route = new Way.RouteSequence();
        foreach (float x in new[] { -0.5f, 0f, 1f })
        {
            var node = new GameObject("Route Point").transform;
            node.SetParent(way.transform);
            node.position = Vector3.right * x;
            route.nodes.Add(node);
        }
        Get<System.Collections.Generic.List<Way.RouteSequence>>(way, "routeSequences").Add(route);
        Set(customer, "currentWay", way);
        Set(customer, "routeSequenceIndex", 0);
        Set(customer, "currentRouteNodeIndex", 1);
        Set(customer, "sourceEntrance", entrance);
        Set(customer, "ownerEntranceManagement", entranceManagement);
        Set(customer, "sourcePrefab", customer);
        yield return new WaitForFixedUpdate();

        var returnRoutine = (IEnumerator)typeof(CustomerBlood).GetMethod("ReturnToEntranceAndDespawn", Private)
            .Invoke(customer, null);
        Click(customer, camera, customer.transform.position);
        Assert.AreSame(surprise, root.GetComponent<SpriteRenderer>().sprite);
        customer.StartCoroutine(returnRoutine);
        float deadline = Time.time + 1f;
        while (root.activeSelf && Time.time < deadline)
        {
            Click(customer, camera, customer.transform.position);
            Assert.AreSame(surprise, root.GetComponent<SpriteRenderer>().sprite);
            Assert.LessOrEqual(customer.transform.position.x, 0f, "The return route must head back toward the entrance.");
            yield return WaitForGameSeconds(0.06f);
        }

        Assert.IsFalse(root.activeSelf, "Repeated clicks must let the customer finish the exit and be recycled.");
        Assert.AreEqual(entrance.DespawnWorldPosition, customer.transform.position);
        Assert.AreEqual(0f, Get<float>(customer, "surpriseWalkPauseTime"), "Exit feedback must not pause movement.");
        Object.Destroy(surprise);
        Object.Destroy(texture);
    }

    [UnityTest]
    public IEnumerator ClickUsesOwnPrefabAndLifetimeWithoutReplacingTownHallSatisfaction()
    {
        var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
        camera.transform.position = Vector3.back * 10f;
        VillageManagement village = VillageManagement.EnsureInstance();
        Set(village, "autoSaveOnChange", false);
        long scoreBefore = village.VillageScore;
        var hall = new GameObject("Hall").AddComponent<VillageTownHall>();
        hall.GetComponent<BoxCollider2D>().enabled = false;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Feedback/BloodCustomer.prefab");
        Assert.IsNotNull(prefab);
        Set(hall, "customerEmojiPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/Feedback/Emoji Happy.prefab"));
        var root = new GameObject("Customer", typeof(SpriteRenderer), typeof(BoxCollider2D));
        var customer = root.AddComponent<CustomerBlood>();
        var texture = new Texture2D(2, 2);
        Sprite walking = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        Set(customer, "walkingSprite", walking);
        root.GetComponent<SpriteRenderer>().sprite = walking;
        root.GetComponent<BoxCollider2D>().size = Vector2.one;
        yield return WaitForGameSeconds(0.05f);
        hall.ShowCustomerSatisfaction(customer);
        GameObject satisfaction = Get<GameObject>(customer, "satisfactionEmojiInstance");
        Click(customer, camera, customer.transform.position);
        Assert.IsNull(Get<GameObject>(customer, "clickEmojiInstance"), "The town hall prefab must not be used for clicking.");
        Assert.IsNull(Get<Coroutine>(customer, "surpriseRoutine"));
        Set(customer, "clickEmojiPrefab", prefab);

        Click(customer, camera, Vector3.right * 10f);
        Assert.IsNull(Get<GameObject>(customer, "clickEmojiInstance"));
        Set(customer, "purchaseSequenceRunning", true);
        Click(customer, camera, customer.transform.position);
        Assert.IsNull(Get<GameObject>(customer, "clickEmojiInstance"), "Trading must also block click emojis.");
        Set(customer, "purchaseSequenceRunning", false);
        Click(customer, camera, customer.transform.position);
        GameObject first = Get<GameObject>(customer, "clickEmojiInstance");
        Assert.IsNotNull(first, "The registered emoji must be instantiated during the click itself.");
        Assert.AreEqual(customer.transform.position, first.transform.position);
        Assert.AreSame(walking, root.GetComponent<SpriteRenderer>().sprite, "An unassigned surprise sprite must not hide the customer.");
        ArcadePopup popup = first.GetComponent<ArcadePopup>();
        Assert.AreEqual(0.1f, Get<float>(popup, "growDuration"));
        Assert.AreEqual(0.1f, Get<float>(popup, "holdDuration"));
        Assert.AreEqual(0.1f, Get<float>(popup, "disappearDuration"));
        yield return WaitForGameSeconds(0.05f);
        Assert.Greater(first.transform.localScale.sqrMagnitude, 0f, "The grow animation must begin on the first rendered frames.");
        Click(customer, camera, customer.transform.position);
        GameObject second = Get<GameObject>(customer, "clickEmojiInstance");
        Assert.AreNotSame(first, second);
        Assert.IsFalse(first.activeSelf);
        yield return WaitForGameSeconds(0.15f);
        Assert.IsTrue(second != null);
        yield return WaitForGameSeconds(0.2f);
        Assert.IsTrue(second == null, "The emoji must disappear after the authored total duration of 0.3 seconds.");
        Assert.IsNull(Get<GameObject>(customer, "clickEmojiInstance"));
        Assert.AreSame(satisfaction, Get<GameObject>(customer, "satisfactionEmojiInstance"), "Clicking must not replace or shorten the satisfaction emoji.");
        Assert.IsTrue(satisfaction != null);
        Assert.AreEqual(1f, satisfaction.GetComponentInChildren<SpriteRenderer>().color.a);
        Assert.AreEqual(scoreBefore, village.VillageScore, "Click feedback must not award village score.");
        Click(customer, camera, customer.transform.position);
        GameObject pooledClick = Get<GameObject>(customer, "clickEmojiInstance");
        root.SetActive(false);
        Assert.IsFalse(pooledClick.activeSelf);
        Assert.IsFalse(satisfaction.activeSelf);
        root.SetActive(true);
        Assert.IsNull(Get<GameObject>(customer, "clickEmojiInstance"));
        Assert.IsNull(Get<GameObject>(customer, "satisfactionEmojiInstance"));
        Object.Destroy(root); Object.Destroy(walking); Object.Destroy(texture);
        yield return null;
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);

    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);

    private static void Click(CustomerBlood customer, Camera camera, Vector3 worldPoint)
    {
        Physics2D.SyncTransforms();
        typeof(CustomerBlood).GetMethod("TryShowSurpriseAtScreenPoint", Private)
            .Invoke(customer, new object[] { (Vector2)camera.WorldToScreenPoint(worldPoint) });
    }

    private static IEnumerator ClickAndCheckPause(CustomerBlood customer, Camera camera, SpriteRenderer renderer,
        Rigidbody2D body, Sprite surprise)
    {
        Click(customer, camera, customer.transform.position);
        Assert.AreSame(surprise, renderer.sprite);
        Vector2 pausedPosition = body.position;
        yield return WaitForGameSeconds(0.08f);
        Assert.That(Vector2.Distance(pausedPosition, body.position), Is.LessThan(0.001f));
    }

    private static IEnumerator WaitForGameSeconds(float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline) yield return null;
    }
}
#endif
