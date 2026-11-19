#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class VillageCameraZoomTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Camera camera;
    private VillageCameraZoom zoom;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        VillagePointerCapture.ReleaseAll();
        camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.transform.position = Vector3.back * 10f;
        zoom = camera.gameObject.AddComponent<VillageCameraZoom>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        VillagePointerCapture.ReleaseAll();
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator WheelReachesBothPreLimitsAndReturnsInExactlyTheConfiguredDuration()
    {
        Set("returnDuration", 0.4f);
        foreach (float scroll in new[] { 100f, -100f })
        {
            Call("ProcessScroll", scroll, Time.unscaledTime);
            float pre = scroll > 0f ? 2.3f : 0.85f;
            float normal = scroll > 0f ? 2f : 1f;
            ReachPreLimit();
            Assert.That(zoom.CurrentZoom, Is.EqualTo(pre).Within(0.0001f));
            Assert.That(camera.orthographicSize, Is.EqualTo(5f / pre).Within(0.001f));
            Assert.IsTrue(Get<bool>("returning"), "Reaching Pre must start returning without waiting for release.");
            Call("AdvanceZoom", 0.2f);
            Assert.That(zoom.CurrentZoom, Is.EqualTo((pre + normal) * 0.5f).Within(0.0001f));
            Call("EndGesture"); // Repeated cancellation must not restart the return timer.
            Call("AdvanceZoom", 0.2f);
            Assert.That(zoom.CurrentZoom, Is.EqualTo(normal).Within(0.0001f));
            Assert.IsFalse(Get<bool>("returning"));
        }
        Set("returnDuration", 0f);
        Call("ProcessScroll", 100f, Time.unscaledTime);
        ReachPreLimit();
        Call("AdvanceZoom", 1f / 60f);
        Assert.AreEqual(2f, zoom.CurrentZoom);
        yield break;
    }

    [UnityTest]
    public IEnumerator ContinuedOutwardInputCannotDelayOrRestartTheAutomaticReturn()
    {
        Set("returnDuration", 0.4f);
        Call("ProcessScroll", 100f, Time.unscaledTime);
        ReachPreLimit();
        float previous = zoom.CurrentZoom;
        for (int frame = 0; frame < 25; frame++)
        {
            Call("ProcessScroll", 10f, Time.unscaledTime);
            Call("AdvanceZoom", 1f / 60f);
            Assert.LessOrEqual(zoom.CurrentZoom, previous + 0.00001f);
            previous = zoom.CurrentZoom;
        }
        Assert.That(zoom.CurrentZoom, Is.EqualTo(2f).Within(0.0001f));
        Assert.IsFalse(Get<bool>("returning"));
        Call("ProcessScroll", -1f, Time.unscaledTime);
        Call("AdvanceZoom", 1f / 60f);
        Assert.Less(zoom.CurrentZoom, 2f, "Reversing the gesture must remain responsive.");
        yield break;
    }

    [UnityTest]
    public IEnumerator PinchUsesFingerDistanceAndKeepsZoomInsideTheNormalRangeAfterRelease()
    {
        Call("ProcessPinch", 3, 8, 100f);
        Assert.AreEqual(1f, zoom.CurrentZoom);
        Call("ProcessPinch", 3, 8, 150f);
        AdvanceFrames();
        Assert.That(zoom.CurrentZoom, Is.EqualTo(1.5f).Within(0.0001f));
        Call("EndGesture");
        AdvanceFrames();
        Assert.That(zoom.CurrentZoom, Is.EqualTo(1.5f).Within(0.0001f));
        Assert.IsFalse(Get<bool>("returning"), "A zoom within Min/Max must stay at the user's chosen value.");
        yield break;
    }

    [UnityTest]
    public IEnumerator ReleasingInsideThePreRangeReturnsWithoutWaitingToReachTheHardLimit()
    {
        Set("returnDuration", 0.4f);
        Call("ProcessPinch", 3, 8, 100f);
        Call("ProcessPinch", 3, 8, 220f);
        AdvanceFrames();
        Assert.That(zoom.CurrentZoom, Is.EqualTo(2.2f).Within(0.0001f));
        Assert.IsFalse(Get<bool>("returning"));
        Call("EndGesture");
        Call("AdvanceZoom", 0.4f);
        Assert.AreEqual(2f, zoom.CurrentZoom);
        yield break;
    }

    [UnityTest]
    public IEnumerator ChangingFingersDoesNotJumpAndNewInputCanInterruptTheReturn()
    {
        Call("ProcessPinch", 3, 8, 100f);
        Call("ProcessPinch", 3, 8, 400f);
        ReachPreLimit();
        Assert.That(zoom.CurrentZoom, Is.EqualTo(2.3f).Within(0.0001f));
        Call("AdvanceZoom", 0.1f);
        float before = zoom.CurrentZoom;
        Call("ProcessPinch", 3, 9, 600f);
        Assert.IsTrue(Get<bool>("returning"), "Replacing a finger must not delay an automatic return.");
        Assert.AreEqual(before, zoom.CurrentZoom);
        Call("ProcessPinch", 3, 9, 300f);
        Assert.IsFalse(Get<bool>("returning"), "Reversing the pinch may interrupt the return.");
        Call("ProcessPinch", 3, 10, 0f);
        Call("AdvanceZoom", 1f / 60f);
        Assert.IsFalse(float.IsNaN(zoom.CurrentZoom));
        Assert.That(zoom.CurrentZoom, Is.EqualTo(before).Within(0.0001f));
        yield break;
    }

    [UnityTest]
    public IEnumerator ReturnUsesRealTimeWhileTheGameIsPaused()
    {
        float previous = Time.timeScale;
        try
        {
            Set("returnDuration", 0.2f);
            Call("ProcessScroll", 100f, Time.unscaledTime);
            ReachPreLimit();
            Time.timeScale = 0f;
            float end = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < end) yield return null;
            Assert.That(zoom.CurrentZoom, Is.EqualTo(2f).Within(0.0001f));
        }
        finally { Time.timeScale = previous; }
    }

    [UnityTest]
    public IEnumerator ObjectCaptureAndStorePopupBlockCameraZoomInput()
    {
        var method = typeof(VillageCameraZoom).GetMethod("InputBlocked", BindingFlags.Static | BindingFlags.NonPublic);
        var owner = new GameObject("Dragged building");
        Assert.IsFalse((bool)method.Invoke(null, new object[] { Vector2.zero }));
        VillagePointerCapture.Acquire(owner);
        Assert.IsTrue((bool)method.Invoke(null, new object[] { Vector2.zero }));
        VillagePointerCapture.Release(owner);
        var popup = new GameObject("Store popup");
        Tycoon.DeliveryModal.Track(popup);
        Assert.IsTrue((bool)method.Invoke(null, new object[] { Vector2.zero }));
        popup.SetActive(false);
        Assert.IsFalse((bool)method.Invoke(null, new object[] { Vector2.zero }));
        yield break;
    }

    [UnityTest]
    public IEnumerator ZoomStopsPanInertiaAndKeepsTheCameraInsideVerticalBounds()
    {
        VillageCameraScroller scroller = camera.gameObject.AddComponent<VillageCameraScroller>();
        Transform top = new GameObject("Top").transform;
        Transform bottom = new GameObject("Bottom").transform;
        top.position = Vector3.up * 20f;
        bottom.position = Vector3.down * 20f;
        typeof(VillageCameraScroller).GetField("topLimit", Private).SetValue(scroller, top);
        typeof(VillageCameraScroller).GetField("bottomLimit", Private).SetValue(scroller, bottom);
        typeof(VillageCameraScroller).GetField("inertialVelocityY", Private).SetValue(scroller, 10f);
        typeof(VillageCameraScroller).GetField("pointerActive", Private).SetValue(scroller, true);
        camera.transform.position = new Vector3(0f, 15f, -10f);
        Call("ProcessScroll", -100f, Time.unscaledTime);
        Assert.AreEqual(0f, typeof(VillageCameraScroller).GetField("inertialVelocityY", Private).GetValue(scroller));
        Assert.AreEqual(false, typeof(VillageCameraScroller).GetField("pointerActive", Private).GetValue(scroller));
        AdvanceFrames();
        Assert.LessOrEqual(camera.transform.position.y + camera.orthographicSize, 20.001f);
        Assert.Less(camera.transform.position.y, 15f);
        Assert.AreEqual(0f, camera.transform.position.x);
        Assert.AreEqual(-10f, camera.transform.position.z);
        yield break;
    }

    [UnityTest]
    public IEnumerator InvalidLimitOrderIsCorrectedAndDisablingCannotLeaveAnOvershoot()
    {
        Call("ProcessScroll", 100f, Time.unscaledTime);
        ReachPreLimit();
        zoom.enabled = false;
        Assert.AreEqual(2f, zoom.CurrentZoom);
        Assert.AreEqual(2.5f, camera.orthographicSize);
        Set("minZoom", 2f);
        Set("maxZoom", 1f);
        Set("preMinZoom", 4f);
        Set("preMaxZoom", 1f);
        Call("OnValidate");
        Assert.AreEqual(2f, Get<float>("maxZoom"));
        Assert.AreEqual(2f, Get<float>("preMinZoom"));
        Assert.AreEqual(2f, Get<float>("preMaxZoom"));
        yield break;
    }

    private void AdvanceFrames() { for (int i = 0; i < 120; i++) Call("AdvanceZoom", 1f / 60f); }
    private void ReachPreLimit()
    {
        for (int i = 0; i < 120 && !Get<bool>("returning"); i++) Call("AdvanceZoom", 1f / 60f);
        Assert.IsTrue(Get<bool>("returning"), "Pre must immediately begin the return while the gesture is still held.");
    }
    private void Call(string method, params object[] args) => typeof(VillageCameraZoom).GetMethod(method, Private).Invoke(zoom, args);
    private void Set(string name, object value) => typeof(VillageCameraZoom).GetField(name, Private).SetValue(zoom, value);
    private T Get<T>(string name) => (T)typeof(VillageCameraZoom).GetField(name, Private).GetValue(zoom);
}

public sealed class VillageCameraZoomSceneTests
{
    [Test]
    public void VillageMainCameraHasEditableZoomLimitsAndReturnDuration()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/Village.unity");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/Village.unity", OpenSceneMode.Additive);
        try
        {
            Camera camera = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
                    if (candidate.CompareTag("MainCamera")) camera = candidate;
            Assert.IsNotNull(camera);
            VillageCameraZoom zoom = camera.GetComponent<VillageCameraZoom>();
            Assert.IsNotNull(zoom);
            var serialized = new SerializedObject(zoom);
            foreach (string field in new[] { "minZoom", "maxZoom", "preMinZoom", "preMaxZoom", "returnDuration" })
                Assert.IsNotNull(serialized.FindProperty(field), field + " must be editable in the Inspector.");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }
}
#endif
