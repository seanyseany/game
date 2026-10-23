#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ArcadeFrameTimeTests
{
    const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    private struct Sample
    {
        public int frame, count, action;
        public double dt, loop, create, activate, input, physics;
        public long gc;
        public float bodyY, visualY, velocityY;
    }
    [UnityTearDown] public IEnumerator Cleanup() { if (Application.isPlaying) yield return new ExitPlayMode(); }

    [UnityTest] public IEnumerator RecordActualArcadeProgressWithJumpAndAttack()
    {
        yield return new EnterPlayMode();
        yield return EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/arcade 1.unity", new LoadSceneParameters(LoadSceneMode.Single));
        float loadDeadline = Time.realtimeSinceStartup + 15f;
        while (SceneManager.GetActiveScene().name != "arcade 1")
        {
            Assert.Less(Time.realtimeSinceStartup, loadDeadline);
            yield return null;
        }
        int readyFrame = Time.frameCount + 3;
        while (Time.frameCount < readyFrame) yield return null;
        var player = Player.Instance;
        Assert.IsNotNull(player);
        player.lives = 10000;
        float preparationDeadline = Time.realtimeSinceStartup + 20f;
        while (typeof(StageManager).GetField("stageLoopRoutine", Fields).GetValue(StageManager.Instance) != null)
        {
            Assert.Less(Time.realtimeSinceStartup, preparationDeadline, "Initial phase preparation must finish");
            yield return null;
        }
        var created = (Dictionary<GameObject, int>)typeof(StageManager).GetField("createdCountByPrefab", Fields).GetValue(StageManager.Instance);
        int preparedPrefabCount = created.Count;
        using var loop = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop", 1);
        var options = ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
        using var create = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Arcade.CreatePhase", 1024, options);
        using var activate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Arcade.ActivatePhase", 1024, options);
        using var input = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Arcade.PlayerInput", 4096, options);
        using var physics = ProfilerRecorder.StartNew(ProfilerCategory.Physics2D, "Physics2D.Simulate", 4096, options);
        using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        var samples = new List<Sample>(2048);
        int actions = 0;
        float started = Time.unscaledTime;
        float nextAction = started + 3f;
        int recordedFrame = -1;
        while (Time.unscaledTime - started < 18f)
        {
            int action = 0;
            if (Time.unscaledTime >= nextAction)
            {
                nextAction += 0.7f;
                action = ++actions;
                if (actions % 2 == 1) player.ApplyJumpObstacleBoost(player.jumpSpeed, 0f);
                else
                {
                    bool grounded = (bool)typeof(Player).GetField("isGrounded", Fields).GetValue(player);
                    string attack = grounded ? "P1_GroundAttack" : "P1_AirStomp";
                    player.StartCoroutine((IEnumerator)typeof(Player).GetMethod(attack, Fields).Invoke(player, null));
                }
            }
            yield return null;
            if (Time.frameCount == recordedFrame) continue;
            recordedFrame = Time.frameCount;
            int count = 0; foreach (int n in created.Values) count += n;
            samples.Add(new Sample { frame = Time.frameCount, dt = Time.unscaledDeltaTime * 1000.0,
                loop = loop.LastValue / 1000000.0, create = create.LastValue / 1000000.0,
                activate = activate.LastValue / 1000000.0, input = input.LastValue / 1000000.0,
                physics = physics.LastValue / 1000000.0, gc = gc.LastValue, count = count, action = action,
                bodyY = player.rb.position.y, visualY = player.transform.position.y, velocityY = player.rb.linearVelocity.y });
        }
        string cpu = Describe("create", create) + "\n" + Describe("activate", activate) + "\n" + Describe("input", input)
            + "\n" + Describe("physics", physics);
        loop.Stop(); create.Stop(); activate.Stop(); input.Stop(); physics.Stop(); gc.Stop();
        var data = new StringBuilder("frame,dt_ms,loop_ms,create_ms,activate_ms,input_ms,physics_ms,gc_bytes,phases_created,action,body_y,visual_y,velocity_y\n");
        var durations = new List<double>();
        var creations = new List<double>();
        int moving = 0, stationary = 0;
        float previousY = samples[0].visualY;
        int index = 0;
        foreach (Sample sample in samples)
        {
            if (index++ > 0 && Mathf.Abs(sample.velocityY) > 0.3f)
            {
                moving++;
                if (Mathf.Abs(sample.visualY - previousY) < 0.00001f) stationary++;
            }
            previousY = sample.visualY;
            durations.Add(sample.dt);
            if (sample.create > 0) creations.Add(sample.create);
            data.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7},{8},{9},{10:F5},{11:F5},{12:F5}\n",
                sample.frame, sample.dt, sample.loop, sample.create, sample.activate, sample.input, sample.physics,
                sample.gc, sample.count, sample.action, sample.bodyY, sample.visualY, sample.velocityY);
        }
        durations.Sort(); creations.Sort();
        string suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string file = "/tmp/arcade-frames-" + suffix + ".csv";
        File.WriteAllText(file, data.ToString());
        File.WriteAllText(file + ".cpu.txt", cpu);
        UnityEngine.Debug.Log("[ARCADE CPU]\n" + cpu);
        UnityEngine.Debug.Log("[ARCADE MOTION] moving=" + moving + ", stationary_display_frames=" + stationary);
        UnityEngine.Debug.Log("[ARCADE FRAMES] file=" + file + ", frames=" + durations.Count + ", p95_ms=" + durations[(int)(durations.Count * 0.95)] + ", max_ms=" + durations[durations.Count - 1] + ", actions=" + actions + ", create_max_ms=" + (creations.Count == 0 ? 0 : creations[creations.Count - 1]));
        Assert.Greater(actions, 15);
        Assert.Greater(moving, 100);
        Assert.Less((double)stationary / moving, 0.08, "50 Hz physics must not repeatedly display stationary poses on the 60 fps frames");
        Assert.IsFalse(GameData.Instance.gameOver);
        Assert.AreEqual(preparedPrefabCount, created.Count, "Do not create a new phase type for the first time during gameplay");
    }
    private static string Describe(string name, ProfilerRecorder recorder)
    {
        recorder.Stop();
        var values = new List<double>();
        for (int i = 0; i < Math.Min(recorder.Count, recorder.Capacity); i++) values.Add(recorder.GetSample(i).Value / 1000000.0);
        values.Sort();
        return name + ": valid=" + recorder.Valid + ", samples=" + values.Count + (values.Count == 0 ? "" : ", median_ms=" + values[values.Count / 2] + ", p95_ms=" + values[(int)(values.Count * .95)] + ", max_ms=" + values[values.Count - 1]);
    }
}
#endif
