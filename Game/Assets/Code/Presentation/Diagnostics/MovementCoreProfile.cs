#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Northwatch.Simulation.Movement;
using Unity.Profiling;
using UnityEngine;

namespace Northwatch.Presentation.Diagnostics
{
    [DefaultExecutionOrder(-20000)]
    public sealed class MovementCoreProfile : MonoBehaviour
    {
        private const int FrameCapacity = 1000000;
        private MatchDriver driver;
        private MovementMeasurement measurement;
        private ProfilerRecorder main, wait, gc, gcBytes;
        private long[] mainTimes, waitTimes, allocations, allocationCounts;
        private int[] scheduled, completed;
        private int frames, firstTick, invalidFrames;
        private string output;
        private double began, measuredAt;
        private bool measuring, finished;
        private long counterFrequency;
        private int targetFrameRate = -1;
        private static MovementCoreProfile allocationProfile;
        private ProfilerRecorder scopedGc;
        private long simulationAllocations, drawingAllocations;
        private bool scopeCapacityReached;

        internal readonly struct AllocationScope : IDisposable
        {
            private readonly MovementCoreProfile owner;
            private readonly bool drawing;
            internal AllocationScope(MovementCoreProfile owner, bool drawing)
            {
                this.owner = owner; this.drawing = drawing;
                owner.scopedGc.Reset(); owner.scopedGc.Start();
            }
            public void Dispose()
            {
                if (owner == null) return;
                owner.scopedGc.Stop();
                if (!owner.measuring) return;
                int count = owner.scopedGc.Count;
                if (count == 256) owner.scopeCapacityReached = true;
                long allocations = 0;
                for (int i = 0; i < count; i++) allocations += owner.scopedGc.GetSample(i).Count;
                if (drawing) owner.drawingAllocations += allocations;
                else owner.simulationAllocations += allocations;
            }
        }

        internal static AllocationScope TraceAllocations(bool drawing)
            => allocationProfile == null || allocationProfile.finished ? default : new AllocationScope(allocationProfile, drawing);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-movement-core-profile") < 0) return;
            PerformanceLadder ladder = FindFirstObjectByType<PerformanceLadder>();
            if (ladder != null) ladder.enabled = false;
            new GameObject("MovementCoreProfile").AddComponent<MovementCoreProfile>();
        }

        private void Start()
        {
            try { Initialize(); }
            catch (Exception error) { Debug.LogException(error); finished = true; Application.Quit(1); }
        }

        [DllImport("kernel32.dll")] private static extern int QueryPerformanceFrequency(out long value);

        private void Initialize()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-movement-output"), sceneIndex = Array.IndexOf(args, "-movement-scene");
            int countIndex = Array.IndexOf(args, "-movement-units");
            int fpsIndex = Array.IndexOf(args, "-movement-fps");
            if (fpsIndex >= 0 && (fpsIndex + 1 >= args.Length ||
                !int.TryParse(args[fpsIndex + 1], out targetFrameRate) || (targetFrameRate != -1 && targetFrameRate != 60)))
                throw new ArgumentException("Use -movement-fps -1|60 (default -1).");
            if (!Debug.isDebugBuild || index < 0 || index + 1 >= args.Length || sceneIndex < 0 || sceneIndex + 1 >= args.Length ||
                countIndex < 0 || countIndex + 1 >= args.Length)
                throw new ArgumentException("Use a development Player with -movement-output PATH -movement-scene 0|1|2|3 -movement-units N.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            output = Path.GetFullPath(args[index + 1]);
            if (!output.StartsWith(Path.Combine(project, "Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Movement output must be under this project's Logs folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            driver = FindFirstObjectByType<MatchDriver>();
            if (driver == null || driver.MovementSettings == null) throw new InvalidOperationException("The match has no driver or movement settings.");
            driver.Simulation.MeasureMovement(int.Parse(args[sceneIndex + 1], CultureInfo.InvariantCulture),
                int.Parse(args[countIndex + 1], CultureInfo.InvariantCulture));
            measurement = driver.Simulation.Measurement;
            mainTimes = new long[FrameCapacity]; waitTimes = new long[FrameCapacity];
            allocations = new long[FrameCapacity]; allocationCounts = new long[FrameCapacity];
            scheduled = new int[FrameCapacity]; completed = new int[FrameCapacity];
            const ProfilerRecorderOptions options = ProfilerRecorderOptions.StartImmediately |
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame;
            main = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Northwatch.Simulation", 1, options);
            wait = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Northwatch.Simulation.Wait", 1, options);
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 1, options);
            gcBytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1, options);
            if (!main.Valid || !wait.Valid || !gc.Valid || !gcBytes.Valid) throw new InvalidOperationException("Required driver/wait/GC recorders are unavailable.");
            if (Array.IndexOf(args, "-movement-gc-scopes") >= 0)
            {
                // Individual samples delimit the driver's scopes without a raw capture.
                scopedGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 256,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                if (!scopedGc.Valid) throw new InvalidOperationException("Scoped allocation recorder unavailable.");
                scopedGc.Reset(); scopedGc.Start();
                GC.KeepAlive(new byte[256]);
                scopedGc.Stop();
                if (scopedGc.Count == 0) throw new InvalidOperationException("Scoped recorder did not detect its setup allocation.");
                allocationProfile = this;
            }
            if (QueryPerformanceFrequency(out counterFrequency) == 0) throw new InvalidOperationException("Performance counter unavailable.");
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = targetFrameRate; Application.runInBackground = true;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            began = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            if (finished) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (measuring)
            {
                if (!Application.isFocused || Screen.width != 1920 || Screen.height != 1080) invalidFrames++;
                if (frames == FrameCapacity) throw new InvalidOperationException("Movement frame capacity exhausted.");
                // Before BeginFrame: record the preceding frame's summed scopes and
                // counters together, preserving the S, C and U frame sets (ADR 0012).
                mainTimes[frames] = main.LastValue; waitTimes[frames] = wait.LastValue;
                allocations[frames] = gcBytes.LastValue;
                allocationCounts[frames] = gc.Count == 0 ? 0 : gc.GetSample(0).Count;
                scheduled[frames] = driver.ScheduledTicks; completed[frames] = driver.CompletedTicks;
                frames++;
                if (now - measuredAt >= 60) { Finish(); return; }
            }
            else if (now - began >= 10)
            {
                driver.Simulation.CompleteFrame();
                firstTick = measurement.TickCount;
                measuring = true; measuredAt = now;
            }
        }

        private void Finish()
        {
            double windowSeconds = Time.realtimeSinceStartupAsDouble - measuredAt;
            finished = true;
            driver.Simulation.CompleteFrame();
            using (var writer = new StreamWriter(output + "-ticks.csv"))
            {
                writer.WriteLine("tick,grid_ms,integration_ms,overlap_ms,move_check_ms,commit_ms,total_ms,level_1,level_2,one_tick_stalls,still_over_5s,longest_still_ticks,alive,footprint_overlaps,static_regressions,gap_crossed,first_advance_cells,second_advance_cells");
                // Negative ticks retain warm-up validation, without including it in timings.
                for (int t = 0; t < measurement.TickCount; t++)
                {
                    writer.Write(t - firstTick);
                    for (int b = 0; b < 5; b++) writer.Write("," + CounterMilliseconds(measurement.Timestamp(t, b + 1) - measurement.Timestamp(t, b)));
                    writer.Write("," + CounterMilliseconds(measurement.Timestamp(t, 5) - measurement.Timestamp(t, 0)));
                    for (int m = 0; m < MovementMeasurement.MetricCount; m++) writer.Write("," + measurement.Metric(t, m));
                    var progress = measurement.Progress(t);
                    writer.Write("," + progress.x.ToString("R", CultureInfo.InvariantCulture));
                    writer.Write("," + progress.y.ToString("R", CultureInfo.InvariantCulture));
                    writer.WriteLine();
                }
            }
            using (var writer = new StreamWriter(output + "-frames.csv"))
            {
                writer.WriteLine("frame,scheduled_ticks,completed_ticks,driver_total_ms,driver_wait_ms,driver_work_ms,gc_alloc_count,gc_bytes");
                for (int f = 0; f < frames; f++) writer.WriteLine(f + "," + scheduled[f] + "," + completed[f] + "," +
                    (mainTimes[f] * .000001).ToString("F6", CultureInfo.InvariantCulture) + "," +
                    (waitTimes[f] * .000001).ToString("F6", CultureInfo.InvariantCulture) + "," +
                    ((mainTimes[f] - waitTimes[f]) * .000001).ToString("F6", CultureInfo.InvariantCulture) + "," +
                    allocationCounts[f] + "," + allocations[f]);
            }
            File.WriteAllText(output + "-info.txt", "scene=PerformanceLadder; drawing=on; vSync=0; targetFrameRate=" + targetFrameRate + "; warmup_s=10; window_s=60\n" +
                "passes=" + driver.MovementSettings.Snapshot().MoveCheckPasses + "; cpu=" + SystemInfo.processorType + "; gpu=" + SystemInfo.graphicsDeviceName + "\n" +
                "unfocused_or_wrong_resolution_frames=" + invalidFrames + "; measured_s=" +
                windowSeconds.ToString("F6", CultureInfo.InvariantCulture) + "\n" +
                "scoped_gc=" + scopedGc.Valid + "; simulation_alloc_count=" + simulationAllocations +
                "; drawing_alloc_count=" + drawingAllocations + "; scope_capacity_reached=" + scopeCapacityReached + "\n");
            Application.Quit(invalidFrames == 0 && !scopeCapacityReached ? 0 : 1);
        }

        private string CounterMilliseconds(long value) => (value * 1000.0 / counterFrequency).ToString("F6", CultureInfo.InvariantCulture);
        private void OnDestroy()
        {
            if (allocationProfile == this) allocationProfile = null;
            if (scopedGc.Valid) scopedGc.Dispose();
            main.Dispose(); wait.Dispose(); gc.Dispose(); gcBytes.Dispose();
        }
    }
}
#endif
