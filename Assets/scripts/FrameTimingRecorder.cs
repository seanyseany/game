using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

// Attach to a dedicated root GameObject in the scene used to start the test.
// Measurements are buffered in memory; CSV output happens only at the end.
[DefaultExecutionOrder(10000)]
public sealed class FrameTimingRecorder : MonoBehaviour
{
    [Min(10)] public int recordingSeconds = 900;
    private const string OutputDirectory = "/tmp/ArcadePerformance";
    private const double MiB = 1024.0 * 1024.0;
    private static FrameTimingRecorder instance;
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private readonly Row[] rows = new Row[3600];
    private DateTimeOffset startedAt;
    private double startTime, windowStart;
    private ulong lastTimingTimestamp;
    private int rowCount, lastGcCount;
    private bool started, saved;
    private Row window;

    private struct Row
    {
        public double elapsed, duration, frameTotal, frameMax;
        public double cpuTotal, cpuMainTotal, presentWaitTotal, cpuRenderTotal, gpuTotal;
        public int frames, slowFrames, cpuSamples, gpuSamples, gcCollections;
        public long unityBytes, unityReservedBytes, managedBytes;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        startedAt = DateTimeOffset.Now;
        startTime = windowStart = Time.realtimeSinceStartupAsDouble;
        lastGcCount = GC.CollectionCount(0);
        started = true;
        if (!FrameTimingManager.IsFeatureEnabled())
            Debug.LogWarning("[FrameTimingRecorder] Enable Frame Timing Stats and rebuild. CPU/GPU columns may be empty.");
        Debug.Log("[FrameTimingRecorder] Recording. Quit normally to save CSV in " + OutputDirectory);
    }

    private void LateUpdate()
    {
        if (!started || saved) return;
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
        {
            FrameTiming timing = timings[0];
            if (timing.frameStartTimestamp != lastTimingTimestamp)
            {
                lastTimingTimestamp = timing.frameStartTimestamp;
                if (timing.cpuFrameTime > 0 && !double.IsNaN(timing.cpuFrameTime))
                {
                    window.cpuSamples++;
                    window.cpuTotal += timing.cpuFrameTime;
                    window.cpuMainTotal += timing.cpuMainThreadFrameTime;
                    window.presentWaitTotal += timing.cpuMainThreadPresentWaitTime;
                    window.cpuRenderTotal += timing.cpuRenderThreadFrameTime;
                }
                if (timing.gpuFrameTime > 0 && !double.IsNaN(timing.gpuFrameTime))
                {
                    window.gpuSamples++;
                    window.gpuTotal += timing.gpuFrameTime;
                }
            }
        }

        double frameMs = Time.unscaledDeltaTime * 1000.0;
        window.frames++;
        window.frameTotal += frameMs;
        window.frameMax = Math.Max(window.frameMax, frameMs);
        if (frameMs > 33.34) window.slowFrames++;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - windowStart >= 1.0) FinishWindow(now);
        if (now - startTime >= Math.Min(recordingSeconds, rows.Length - 1)) Save();
    }

    private void FinishWindow(double now)
    {
        if (window.frames == 0 || rowCount >= rows.Length) return;
        window.elapsed = now - startTime;
        window.duration = now - windowStart;
        window.unityBytes = Profiler.GetTotalAllocatedMemoryLong();
        window.unityReservedBytes = Profiler.GetTotalReservedMemoryLong();
        window.managedBytes = Profiler.GetMonoUsedSizeLong();
        int gcCount = GC.CollectionCount(0);
        window.gcCollections = gcCount - lastGcCount;
        lastGcCount = gcCount;
        rows[rowCount++] = window;
        window = default;
        windowStart = now;
    }

    private void OnApplicationQuit() { Save(); }
    private void OnDestroy() { if (instance == this) instance = null; }

    private void Save()
    {
        if (!started || saved) return;
        saved = true;
        FinishWindow(Time.realtimeSinceStartupAsDouble);
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            string path = System.IO.Path.Combine(OutputDirectory, "frames-" + startedAt.ToString("yyyyMMdd-HHmmss") + ".csv");
            using (var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8, 65536))
            {
                writer.WriteLine("wall_time,elapsed_s,fps,frame_avg_ms,frame_max_ms,frames_over_33ms,cpu_frame_ms,cpu_main_ms,cpu_present_wait_ms,cpu_render_ms,gpu_ms,cpu_samples,gpu_samples,unity_used_mib,unity_reserved_mib,managed_used_mib,gc_gen0_collections");
                for (int i = 0; i < rowCount; i++)
                {
                    Row r = rows[i];
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F3},{2:F2},{3:F3},{4:F3},{5},{6},{7},{8},{9},{10},{11},{12},{13:F2},{14:F2},{15:F2},{16}",
                        startedAt.AddSeconds(r.elapsed).ToString("o"), r.elapsed,
                        r.frames / r.duration, r.frameTotal / r.frames, r.frameMax, r.slowFrames,
                        Average(r.cpuTotal, r.cpuSamples), Average(r.cpuMainTotal, r.cpuSamples),
                        Average(r.presentWaitTotal, r.cpuSamples), Average(r.cpuRenderTotal, r.cpuSamples),
                        Average(r.gpuTotal, r.gpuSamples), r.cpuSamples, r.gpuSamples,
                        r.unityBytes / MiB, r.unityReservedBytes / MiB, r.managedBytes / MiB, r.gcCollections));
                }
            }
            File.WriteAllText(path + ".info.txt", "Unity: " + Application.unityVersion +
                "\nGraphics API: " + SystemInfo.graphicsDeviceType + "\nGPU: " + SystemInfo.graphicsDeviceName +
                "\nResolution: " + Screen.width + "x" + Screen.height +
                "\nFrame timing enabled: " + FrameTimingManager.IsFeatureEnabled() +
                "\nCPU/GPU timings arrive several frames after the displayed frame. Empty timing cells mean unavailable data.\n");
            Debug.Log("[FrameTimingRecorder] Saved: " + path);
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    private static string Average(double total, int count)
    {
        return count > 0 ? (total / count).ToString("F3", CultureInfo.InvariantCulture) : "";
    }
}
