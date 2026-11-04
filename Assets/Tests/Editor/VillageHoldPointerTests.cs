#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class VillageHoldPointerTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type[] Types = { typeof(Building), typeof(SpecialBuilding), typeof(TurretImplementation), typeof(Oxygen) };

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        VillagePointerCapture.ReleaseAll();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        VillagePointerCapture.ReleaseAll();
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator LeavingBeforePickupCancelsEvenAtTheHoldThresholdAndReentryCannotResume()
    {
        foreach (Type type in Types)
        foreach (float elapsed in new[] { 0.2f, 1.39f, 2f })
        {
            MonoBehaviour target = Create(type);
            Press(target, elapsed);
            Call(target, "UpdatePointerHold", Vector3.right * 10f, true);
            Assert.IsFalse(Get<bool>(target, "pointerHeld"), type.Name);
            Assert.IsFalse(Get<bool>(target, "isDragging"), type.Name);
            Assert.IsFalse(VillagePointerCapture.HasActiveCapture, type.Name + " must release the camera input capture immediately.");
            Assert.AreEqual(-1f, Get<float>(target, "pointerDownStartedAt"));
            Call(target, "UpdatePointerHold", Vector3.zero, true);
            Assert.IsFalse(Get<bool>(target, "pointerHeld"), "Returning while still pressed cannot start a new hold.");
            if (type == typeof(Building)) Assert.IsFalse(Get<bool>(target, "dragPreparationActive"));
            if (type == typeof(TurretImplementation))
            {
                ((TurretImplementation)target).HandleColliderPointerUpAsButton();
                Assert.IsFalse(Get<bool>(target, "tapPending"));
                Assert.IsNull(TurretUI.Instance, "A cancelled forwarded tap must not open the turret UI.");
            }
            Object.DestroyImmediate(target.gameObject);
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator InsideAndChildColliderHoldsStayValidWhileDisabledColliderDoesNot()
    {
        foreach (Type type in Types)
        {
            MonoBehaviour target = Create(type);
            var child = new GameObject("Child hit area", typeof(BoxCollider2D));
            child.transform.SetParent(target.transform, false);
            child.transform.localPosition = Vector3.right * 3f;
            Physics2D.SyncTransforms();
            Press(target, 0.1f);
            Call(target, "UpdatePointerHold", Vector3.zero, true);
            Assert.IsTrue(Get<bool>(target, "pointerHeld"));
            Call(target, "UpdatePointerHold", Vector3.right * 3f, true);
            Assert.IsTrue(Get<bool>(target, "pointerHeld"), "Moving between colliders on the same object must keep the hold.");
            child.GetComponent<Collider2D>().enabled = false;
            Call(target, "UpdatePointerHold", Vector3.right * 3f, true);
            Assert.IsFalse(Get<bool>(target, "pointerHeld"));
            Assert.IsFalse(VillagePointerCapture.HasActiveCapture);
            Object.DestroyImmediate(target.gameObject);
        }
        yield break;
    }

    [UnityTest]
    public IEnumerator HoldFeedbackDoesNotCancelAStationaryEdgePressAndPickupCanStillMove()
    {
        foreach (Type type in Types)
        {
            MonoBehaviour target = Create(type);
            Press(target, 0.8f);
            object shake = Get<object>(target, "holdShake");
            shake.GetType().GetMethod("Begin").Invoke(shake, new object[] { target.transform });
            shake.GetType().GetMethod("Tick").Invoke(shake, new object[] { 0.83f, 1.4f });
            Set(target, "holdShake", shake);
            Physics2D.SyncTransforms();
            Call(target, "UpdatePointerHold", Vector3.right * 0.999f, true);
            Assert.IsTrue(Get<bool>(target, "pointerHeld"), type.Name + " shake must not move the hold boundary.");
            Set(target, "isDragging", true);
            Call(target, "UpdatePointerHold", Vector3.right * 10f, true);
            Assert.IsTrue(Get<bool>(target, "isDragging"), "After pickup the pointer must be free to move the object.");
            Object.DestroyImmediate(target.gameObject);
        }
        yield break;
    }

    private static MonoBehaviour Create(Type type)
    {
        var root = new GameObject(type.Name + " hold test", typeof(BoxCollider2D));
        root.GetComponent<BoxCollider2D>().size = Vector2.one * 2f;
        var target = (MonoBehaviour)root.AddComponent(type);
        Physics2D.SyncTransforms();
        return target;
    }

    private static void Press(MonoBehaviour target, float elapsed)
    {
        Set(target, "pointerHeld", true);
        Set(target, "pointerDownStartedAt", Time.unscaledTime - elapsed);
        if (target is Building) Set(target, "dragPreparationActive", true);
        if (target is TurretImplementation) Set(target, "tapPending", true);
        VillagePointerCapture.Acquire(target);
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
}
#endif
