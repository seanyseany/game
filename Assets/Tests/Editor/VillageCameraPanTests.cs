#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class VillageCameraPanTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Camera camera;
    private VillageCameraZoom zoom;
    private VillageCameraScroller scroller;

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
        camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
        camera.aspect = 1f;
        camera.transform.position = new Vector3(3f, 0f, -10f);
        zoom = camera.gameObject.AddComponent<VillageCameraZoom>();
        scroller = camera.gameObject.AddComponent<VillageCameraScroller>();
        Transform top = new GameObject("Top").transform;
        Transform bottom = new GameObject("Bottom").transform;
        top.position = Vector3.up * 100f;
        bottom.position = Vector3.down * 100f;
        Set("topLimit", top);
        Set("bottomLimit", bottom);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        VillagePointerCapture.ReleaseAll();
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator ZoomedViewDragsHorizontallyVerticallyAndDiagonally()
    {
        ZoomTo(2f);
        foreach (Vector2 delta in new[] { Vector2.right * 100f, Vector2.left * 100f,
            Vector2.up * 100f, Vector2.down * 100f, new Vector2(100f, 100f) })
        {
            scroller.ResetToDefaultPosition();
            Vector2 start = camera.pixelRect.center;
            Vector3 worldDelta = camera.ScreenToWorldPoint(new Vector3(start.x + delta.x, start.y + delta.y, 10f))
                - camera.ScreenToWorldPoint(new Vector3(start.x, start.y, 10f));
            Call("BeginPointer", start);
            Call("UpdatePointer", start + delta);
            Assert.IsTrue(Get<bool>("draggingCamera"));
            Assert.That(camera.transform.position.x, Is.EqualTo(3f - worldDelta.x).Within(0.001f));
            Assert.That(camera.transform.position.y, Is.EqualTo(-worldDelta.y).Within(0.001f));
            Assert.AreEqual(-10f, camera.transform.position.z);
            scroller.CancelPointerMotion();
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator MinZoomKeepsHorizontalCenterAndExistingVerticalDrag()
    {
        Call("BeginPointer", new Vector2(500f, 500f));
        Call("UpdatePointer", new Vector2(800f, 500f));
        Assert.AreEqual(3f, camera.transform.position.x);
        Assert.IsFalse(Get<bool>("draggingCamera"), "A sideways move at Min must retain the original behavior.");
        scroller.CancelPointerMotion();
        Vector2 start = camera.pixelRect.center;
        float deltaY = camera.ScreenToWorldPoint(new Vector3(start.x, start.y + 100f, 10f)).y
            - camera.ScreenToWorldPoint(new Vector3(start.x, start.y, 10f)).y;
        Call("BeginPointer", start);
        Call("UpdatePointer", start + Vector2.up * 100f);
        Assert.That(camera.transform.position.y, Is.EqualTo(-deltaY).Within(0.001f));
        Assert.AreEqual(3f, camera.transform.position.x);
        yield break;
    }

    [UnityTest]
    public IEnumerator HorizontalLimitsFollowAspectRatioAndPreventLeavingTheMinZoomView()
    {
        ZoomTo(2f);
        foreach (float aspect in new[] { 0.5f, 1f, 2f })
        {
            camera.aspect = aspect;
            Call("MoveCameraXBy", 1000f);
            Assert.That(camera.transform.position.x, Is.EqualTo(3f + 2.5f * aspect).Within(0.001f));
            Assert.That(camera.transform.position.x + camera.orthographicSize * aspect,
                Is.EqualTo(3f + 5f * aspect).Within(0.001f));
            Call("MoveCameraXBy", -1000f);
            Assert.That(camera.transform.position.x, Is.EqualTo(3f - 2.5f * aspect).Within(0.001f));
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator ZoomingBackToMinRecentersHorizontalPositionAndClearsSidewaysInertia()
    {
        ZoomTo(2f);
        Call("MoveCameraXBy", 1000f);
        Assert.AreEqual(5.5f, camera.transform.position.x);
        Set("inertialVelocityX", 8f);
        ZoomTo(1f);
        Assert.That(camera.transform.position.x, Is.EqualTo(3f).Within(0.001f));
        Assert.AreEqual(0f, Get<float>("inertialVelocityX"));
        Assert.AreEqual(5f, camera.orthographicSize);
        yield break;
    }

    [UnityTest]
    public IEnumerator SidewaysReleaseHasInertiaAndZoomCancelsBothAxes()
    {
        ZoomTo(2f);
        Vector2 start = camera.WorldToScreenPoint(new Vector3(3f, 0f, 0f));
        Vector2 end = camera.WorldToScreenPoint(new Vector3(4.5f, 0f, 0f));
        Call("BeginPointer", start);
        Call("UpdatePointer", end);
        Call("EndPointer");
        Assert.Less(Get<float>("inertialVelocityX"), 0f);
        float before = camera.transform.position.x;
        Call("AdvanceHorizontalInertia", 1f / 60f);
        Assert.Less(camera.transform.position.x, before);
        Assert.AreEqual(0f, camera.transform.position.y);
        Set("inertialVelocityX", 8f);
        Set("inertialVelocityY", 8f);
        CallZoom("ProcessScroll", 1f, Time.unscaledTime);
        Assert.AreEqual(0f, Get<float>("inertialVelocityX"));
        Assert.AreEqual(0f, Get<float>("inertialVelocityY"));
        yield break;
    }

    [UnityTest]
    public IEnumerator DraggedObjectCaptureStillBlocksMovementOnBothAxes()
    {
        ZoomTo(2f);
        GameObject owner = new GameObject("Dragged building");
        VillagePointerCapture.Acquire(owner);
        Call("BeginPointer", new Vector2(500f, 500f));
        Call("UpdatePointer", new Vector2(700f, 700f));
        Assert.AreEqual(new Vector3(3f, 0f, -10f), camera.transform.position);
        Assert.AreEqual(0f, Get<float>("inertialVelocityX"));
        Assert.AreEqual(0f, Get<float>("inertialVelocityY"));
        VillagePointerCapture.Release(owner);
        yield break;
    }

    private void ZoomTo(float value)
    {
        CallZoom("ProcessPinch", 1, 2, 100f);
        CallZoom("ProcessPinch", 1, 2, 100f * value / zoom.CurrentZoom);
        for (int i = 0; i < 120; i++) CallZoom("AdvanceZoom", 1f / 60f);
        CallZoom("EndGesture");
        Assert.That(zoom.CurrentZoom, Is.EqualTo(value).Within(0.0001f));
    }

    private void Call(string method, params object[] args) => typeof(VillageCameraScroller).GetMethod(method, Private).Invoke(scroller, args);
    private void CallZoom(string method, params object[] args) => typeof(VillageCameraZoom).GetMethod(method, Private).Invoke(zoom, args);
    private void Set(string name, object value) => typeof(VillageCameraScroller).GetField(name, Private).SetValue(scroller, value);
    private T Get<T>(string name) => (T)typeof(VillageCameraScroller).GetField(name, Private).GetValue(scroller);
}
#endif
