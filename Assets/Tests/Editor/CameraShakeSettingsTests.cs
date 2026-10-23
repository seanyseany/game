#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class ConfigurableShakeInputPlayer : Player
{
    public bool attackPressed;
    public int selectedType = 2;
    protected override bool UsesArcadeSystems => false;
    public override int CharacterType => selectedType;
    protected override bool ReadKeyDown(KeyCode key) => attackPressed && key == KeyCode.DownArrow;
    protected override void Start() { }
}

public sealed class CameraShakeSettingsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator EachEventUsesItsMultiplierAndCurrentDefaultsAndZeroDisablesIt()
    {
        yield return new EnterPlayMode();
        var manager = Camera();
        int index = 0;
        foreach (CameraShakeManager.ShakeEvent source in Enum.GetValues(typeof(CameraShakeManager.ShakeEvent)))
        {
            float multiplier = 0.5f + index * 0.1f;
            SetMultiplier(manager, source, multiplier);
            manager.defaultDuration = 0.2f + index * 0.01f;
            manager.defaultMagnitudeX = 0.3f + index * 0.02f;
            manager.defaultMagnitudeY = 0.4f + index * 0.03f;
            manager.defaultFrequency = 13f + index;
            manager.defaultDecay = ++index % 2 == 0;
            Requests(manager).Clear();
            CameraShakeManager.Shake(source);
            AssertRequest(manager, multiplier);
            Requests(manager).Clear();
            SetMultiplier(manager, source, 0f);
            CameraShakeManager.Shake(source);
            Assert.AreEqual(0, Requests(manager).Count, source.ToString());
        }
    }

    [UnityTest] public IEnumerator ChangingDefaultsUpdatesAllEventsAndInspectorMultipliersSurviveSerialization()
    {
        yield return new EnterPlayMode();
        var manager = Camera();
        manager.defaultDuration = 0.35f; manager.defaultMagnitudeX = 0.8f; manager.defaultMagnitudeY = 0.12f;
        foreach (CameraShakeManager.ShakeEvent source in Enum.GetValues(typeof(CameraShakeManager.ShakeEvent)))
        {
            float multiplier = manager.GetMultiplier(source);
            if ((int)source <= (int)CameraShakeManager.ShakeEvent.NormalPlayer5)
            {
                Assert.AreEqual(0.25f, multiplier);
            }
            if (source == CameraShakeManager.ShakeEvent.MissileHit) Assert.AreEqual(1f, multiplier);
            if (source == CameraShakeManager.ShakeEvent.GateHit) Assert.AreEqual(0.5f, multiplier);
        }
        manager.ragePlayer2Multiplier = 1.2f;
        CameraShakeManager.ShakePlayerAttack(2, true);
        AssertRequest(manager, 1.2f);
        Requests(manager).Clear();
        manager.defaultMagnitudeX = 1.6f;
        manager.defaultMagnitudeY = 0.24f;
        manager.defaultDuration = 0.6f;
        manager.defaultFrequency = 15f;
        CameraShakeManager.ShakePlayerAttack(2, true);
        AssertRequest(manager, 1.2f);
        Requests(manager).Clear();
        string serialized = EditorJsonUtility.ToJson(manager);
        manager.ragePlayer2Multiplier = 0f;
        EditorJsonUtility.FromJsonOverwrite(serialized, manager);
        CameraShakeManager.ShakePlayerAttack(2, true);
        AssertRequest(manager, 1.2f);
        Assert.AreEqual(1.2f, manager.ragePlayer2Multiplier);
    }

    [UnityTest] public IEnumerator RageAttackKeyUsesCharacterSettingsAndMovesTheCameraEvenDuringAnimationLock()
    {
        yield return new EnterPlayMode();
        int oldLimit = Application.targetFrameRate, oldVsync = QualitySettings.vSyncCount;
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
        try
        {
            var manager = Camera();
            var go = new GameObject("Input Player"); go.SetActive(false);
            go.AddComponent<Rigidbody2D>().simulated = false;
            var native = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arcade/player.prefab");
            go.AddComponent<Animator>().runtimeAnimatorController = native.GetComponent<Animator>().runtimeAnimatorController;
            go.AddComponent<SpriteRenderer>();
            var hero = go.AddComponent<ConfigurableShakeInputPlayer>(); hero.enabled = false;
            go.SetActive(true);
            typeof(Player).GetField("isRageMode", Private).SetValue(hero, true);
            typeof(Player).GetField("isTransformLock", Private).SetValue(hero, true);
            foreach (int type in new[] { 2, 3, 4 })
            {
                hero.selectedType = type;
                float multiplier = 0.5f + type * 0.2f;
                SetMultiplier(manager, (CameraShakeManager.ShakeEvent)((int)CameraShakeManager.ShakeEvent.RagePlayer1 + type - 1), multiplier);
                manager.defaultDuration = 0.4f;
                Requests(manager).Clear();
                hero.attackPressed = false;
                typeof(Player).GetMethod("HandleAttackInput", Private).Invoke(hero, null);
                Assert.AreEqual(0, Requests(manager).Count);
                hero.attackPressed = true;
                typeof(Player).GetMethod("HandleAttackInput", Private).Invoke(hero, null);
                AssertRequest(manager, multiplier);
                hero.attackPressed = false;
                Vector3 center = manager.transform.position;
                manager.enabled = true;
                bool moved = false;
                float until = Time.time + 0.15f;
                while (Time.time < until)
                {
                    yield return null;
                    moved |= (manager.transform.position - center).sqrMagnitude > 0.00000001f;
                }
                Assert.IsTrue(moved, "Configured Rage P" + type + " input must move the camera");
                manager.enabled = false;
                Assert.That(Vector3.Distance(center, manager.transform.position), Is.LessThan(0.000001f));
            }
        }
        finally { Application.targetFrameRate = oldLimit; QualitySettings.vSyncCount = oldVsync; }
    }

    private static CameraShakeManager Camera()
    {
        var go = new GameObject("Main Camera", typeof(Camera)); go.tag = "MainCamera";
        var manager = go.AddComponent<CameraShakeManager>(); manager.enabled = false;
        typeof(CameraShakeManager).GetField("allowHorizontalShake", Private).SetValue(manager, true);
        return manager;
    }

    private static IList Requests(CameraShakeManager manager) => (IList)typeof(CameraShakeManager).GetField("activeShakes", Private).GetValue(manager);
    private static void SetMultiplier(CameraShakeManager manager, CameraShakeManager.ShakeEvent source, float multiplier)
    {
        string name = source.ToString();
        // Check the actual Inspector property as well as event dispatch.
        var serialized = new SerializedObject(manager);
        var field = serialized.FindProperty(char.ToLowerInvariant(name[0]) + name.Substring(1) + "Multiplier");
        Assert.IsNotNull(field);
        field.floatValue = multiplier;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssertRequest(CameraShakeManager manager, float multiplier)
    {
        Assert.AreEqual(1, Requests(manager).Count);
        object request = Requests(manager)[0]; var type = request.GetType();
        Assert.AreEqual(manager.defaultDuration, type.GetField("duration").GetValue(request));
        Assert.AreEqual(manager.defaultMagnitudeX * multiplier, type.GetField("magnitudeX").GetValue(request));
        Assert.AreEqual(manager.defaultMagnitudeY * multiplier, type.GetField("magnitudeY").GetValue(request));
        Assert.AreEqual(manager.defaultFrequency, type.GetField("frequency").GetValue(request));
        Assert.AreEqual(manager.defaultDecay, type.GetField("decay").GetValue(request));
    }
}
#endif
