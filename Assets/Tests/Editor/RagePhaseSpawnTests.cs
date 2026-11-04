#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class RagePhaseSpawnTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private StageManager manager;
    private Collider2D detector;
    private float previousTimeScale;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        previousTimeScale = Time.timeScale;
        manager = new GameObject("Rage spawn regression").AddComponent<StageManager>();
        manager.initialPoolSizePerPrefab = 0;
        manager.phaseBaseSpeed = 0f;
        manager.ragePhaseDuration = 1000f;
        var phase = new GameObject("Rage phase template");
        phase.SetActive(false);
        var end = new GameObject("End", typeof(BoxCollider2D), typeof(PhaseEndTrigger));
        end.transform.SetParent(phase.transform, false);
        manager.rageStagePrefabs = new[] { phase };
        manager.stage1PhasePrefabs = new[] { phase };
        Call("InitPools");
        manager.ResetState();
        var sensor = new GameObject("Detector", typeof(BoxCollider2D), typeof(Rigidbody2D), typeof(PhaseDetector));
        sensor.transform.position = Vector3.left * 1000f;
        sensor.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        detector = sensor.GetComponent<Collider2D>();
        Call("StartRagePhaseSpawn");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Time.timeScale = previousTimeScale;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator OldRageTriggerCannotSpawnASecondPhaseWhenItReactivatesAfterTenSeconds()
    {
        Time.timeScale = 20f;
        PhaseEndTrigger first = Latest;
        Contact(first);
        float deadline = Time.time + 11f;
        while (Time.time < deadline) yield return null;
        yield return null;
        int before = Active.Count;
        Contact(Latest);
        Contact(first);
        Assert.AreEqual(before + 1, Active.Count,
            "An old rage phase and the current phase must not both spawn a successor in the same frame.");
        Assert.IsFalse(first.GetComponent<Collider2D>().enabled, "A phase end must stay consumed until pool reactivation.");
    }

    [UnityTest]
    public IEnumerator DuplicatePassesAndResumePathsNeverAddTwoPendingRagePhases()
    {
        for (int i = 0; i < 12; i++)
        {
            PhaseEndTrigger current = Latest;
            int before = Active.Count;
            manager.OnPhasePassed(current);
            manager.OnPhasePassed(current);
            Call("EnsureRagePhasePresence");
            manager.SetGameplayPause(false);
            Assert.AreEqual(before + 1, Active.Count, "Each rage phase may spawn exactly one successor.");
        }
        int count = Active.Count;
        Call("StartRagePhaseSpawn");
        Assert.AreEqual(count, Active.Count, "Retriggering rage must reuse the phase already waiting for its detector.");
        Call("HandleMachineGunSequenceStart");
        manager.OnPhasePassed(Latest);
        Assert.AreEqual(count, Active.Count);
        Call("HandleMachineGunSequenceEnd");
        Assert.AreEqual(count + 1, Active.Count, "Machinegun resume spawns one successor after a recorded pass.");
        Call("EnsureRagePhasePresence");
        manager.SetGameplayPause(false);
        Assert.AreEqual(count + 1, Active.Count);
        yield break;
    }

    [UnityTest]
    public IEnumerator ReturnedRagePhaseEndWorksAgainWhenItsPoolObjectIsReused()
    {
        PhaseEndTrigger first = Latest;
        GameObject firstRoot = first.transform.parent.gameObject;
        Contact(first);
        firstRoot.transform.position = Vector3.left * 100f;
        typeof(StageManager).GetField("despawnCheckTimer", Private).SetValue(manager, 1f);
        Call("Update");
        Assert.IsFalse(firstRoot.activeSelf);
        Contact(Latest);
        Assert.AreSame(firstRoot, Latest.transform.parent.gameObject);
        Assert.IsTrue(Latest.GetComponent<Collider2D>().enabled, "Pool activation must reset the consumed detector.");
        int before = Active.Count;
        Contact(Latest);
        Assert.AreEqual(before + 1, Active.Count);
        yield break;
    }

    private void Contact(PhaseEndTrigger trigger) => trigger.SendMessage("OnTriggerEnter2D", detector);
    private IList Active => (IList)typeof(StageManager).GetField("activePhases", Private).GetValue(manager);
    private PhaseEndTrigger Latest => ((GameObject)Active[Active.Count - 1].GetType().GetField("obj").GetValue(Active[Active.Count - 1])).GetComponentInChildren<PhaseEndTrigger>();
    private object Call(string method) => typeof(StageManager).GetMethod(method, Private).Invoke(manager, null);
}
#endif
