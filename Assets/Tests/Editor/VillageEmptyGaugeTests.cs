#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class VillageEmptyGaugeTests
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

    [UnityTest] public IEnumerator EmptyTurretUsesIdleSwingAndResumesTrackingAfterRefill()
    {
        var slot = new GameObject("TurretSlot", typeof(BoxCollider2D)).AddComponent<TurretImplementation>();
        slot.enabled = false;
        Turret turret = CreateTurret(slot.transform);
        Turret idleReference = CreateTurret(slot.transform);
        var enemy = new GameObject("Enemy", typeof(Rigidbody2D), typeof(CircleCollider2D)).AddComponent<Villan>();
        enemy.transform.position = new Vector3(4f, 2f, 0f);
        Call(turret, "Update");
        Assert.AreSame(enemy, Get<Villan>(turret, "currentTarget"));

        turret.ApplySavedState(1, 0, 10);
        // Compare the empty turret with an armed turret that has no enemy to detect.
        Set(idleReference, "currentRotationAngle", Get<float>(turret, "currentRotationAngle"));
        Quaternion firstRotation = turret.transform.localRotation;
        bool swung = false;
        for (int i = 0; i < 12; i++)
        {
            enemy.enabled = false;
            Call(idleReference, "Update");
            enemy.enabled = true;
            enemy.transform.position = new Vector3(4f, i % 2 == 0 ? -2f : 2f, 0f);
            Call(turret, "Update");
            Assert.IsNull(Get<Villan>(turret, "currentTarget"));
            Assert.That(Quaternion.Angle(turret.transform.localRotation, idleReference.transform.localRotation), Is.LessThan(0.001f));
            swung |= Quaternion.Angle(firstRotation, turret.transform.localRotation) > 0.01f;
            yield return null;
        }
        Assert.IsTrue(swung, "An empty turret must keep moving with the normal idle swing.");
        turret.ApplySavedState(1, 1, 10);
        Call(turret, "Update");
        Assert.AreSame(enemy, Get<Villan>(turret, "currentTarget"));
    }

    [UnityTest] public IEnumerator BuildingGaugeStaysVisibleWhenStockRunsOutAtBothLevels()
    {
        Building building = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/Bread.prefab")).GetComponent<Building>();
        Set(building, "level1StockConsumptionIntervalSeconds", 0f);
        Set(building, "level2StockConsumptionIntervalSeconds", 0f);
        Canvas gauge = Get<Canvas>(building, "boxStockGauge");
        Slider slider = gauge.GetComponentInChildren<Slider>(true);
        building.MarkPlaced(true);
        foreach (int level in new[] { 1, 2 })
        {
            building.SetLevel(level);
            building.SetSalary(1f, 10);
            Assert.IsTrue(building.IsWorking);
            Call(building, "ConsumeSalaryForCustomer");
            yield return null;
            Assert.AreEqual(0f, building.CurrentSalary);
            Assert.IsFalse(building.IsWorking);
            Assert.IsTrue(gauge.gameObject.activeInHierarchy, "The empty gauge must remain visible.");
            Assert.AreEqual(0f, slider.value);
            building.SetSalary(5f, 10);
            Assert.IsTrue(gauge.gameObject.activeInHierarchy);
            Assert.AreEqual(0.5f, slider.value);
            Assert.IsTrue(building.IsWorking);
        }
    }

    private static Turret CreateTurret(Transform slot)
    {
        Turret turret = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/TURRET1.prefab"), slot).GetComponent<Turret>();
        turret.enabled = false;
        turret.transform.localPosition = Vector3.zero;
        Set(turret, "bulletPrefab", null);
        turret.ConfigureTargetRange(slot, new Vector2(2f, -3f), new Vector2(10f, 3f));
        turret.ApplySavedState(1, 1, 10);
        return turret;
    }

    private static FieldInfo Field(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, Private);
            if (field != null) return field;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
}
#endif
