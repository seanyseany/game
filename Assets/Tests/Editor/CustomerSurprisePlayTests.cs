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

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
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
        Vector3 stoppedPosition = customer.transform.position;
        Assert.AreSame(surprise, renderer.sprite);
        yield return WaitForGameSeconds(0.08f);
        Assert.AreEqual(stoppedPosition, customer.transform.position, "Surprise must pause the existing movement.");
        Assert.IsTrue(root.GetComponent<Rigidbody2D>().simulated);
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
        Assert.AreEqual(stoppedPosition, customer.transform.position);
        yield return WaitForGameSeconds(0.22f);
        Assert.AreSame(carrying, renderer.sprite, "Expiry must restore the latest underlying state.");
        Assert.Greater(customer.transform.position.x, stoppedPosition.x, "Movement must resume after 0.3 seconds.");
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
        Object.Destroy(walking); Object.Destroy(receiving); Object.Destroy(carrying); Object.Destroy(surprise); Object.Destroy(texture);
        yield return null;
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

    private static IEnumerator WaitForGameSeconds(float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline) yield return null;
    }
}
#endif
