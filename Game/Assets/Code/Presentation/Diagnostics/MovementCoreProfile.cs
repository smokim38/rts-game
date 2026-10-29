using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Northwatch.Simulation.Movement;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Northwatch.Presentation.Diagnostics
{
    public sealed class MovementCoreProfile : MonoBehaviour
    {
        private const int FrameCapacity = 16384;
        private MovementMeasurement measurement;
        private TickClock clock;
        private ProfilerRecorder gc;
        private long[] schedule, complete, allocations;
        private int[] ticksPerFrame;
        private int frames, firstTick;
        private string output;
        private double began, measuredAt;
        private bool measuring, draining, finished;
        private long counterFrequency;
        private static readonly ProfilerMarker ScheduleMarker = new ProfilerMarker("Northwatch.Movement.Schedule");
        private static readonly ProfilerMarker CompleteMarker = new ProfilerMarker("Northwatch.Movement.Complete");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-movement-core-profile") < 0) return;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) behaviour.enabled = false;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            new GameObject("MovementCoreProfile").AddComponent<MovementCoreProfile>();
        }

        private void Start()
        {
            try { Initialize(); }
            catch (Exception error) { UnityEngine.Debug.LogException(error); finished = true; Application.Quit(1); }
        }

        [DllImport("kernel32.dll")] private static extern int QueryPerformanceFrequency(out long value);

        private void Initialize()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-movement-output"), sceneIndex = Array.IndexOf(args, "-movement-scene");
            if (!UnityEngine.Debug.isDebugBuild || index < 0 || index + 1 >= args.Length || sceneIndex < 0 || sceneIndex + 1 >= args.Length)
                throw new ArgumentException("Use a development Player with -movement-output PATH -movement-scene 0|1|2.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            output = Path.GetFullPath(args[index + 1]);
            if (!output.StartsWith(Path.Combine(project, "Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Movement output must be under this project's Logs folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var settings = ScriptableObject.CreateInstance<MovementSettings>();
            measurement = new MovementMeasurement(settings.Snapshot(), int.Parse(args[sceneIndex + 1], CultureInfo.InvariantCulture));
            Destroy(settings);
            schedule = new long[FrameCapacity]; complete = new long[FrameCapacity]; allocations = new long[FrameCapacity]; ticksPerFrame = new int[FrameCapacity];
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", FrameCapacity);
            if (!gc.Valid) throw new InvalidOperationException("Whole-frame GC recorder is unavailable.");
            if (QueryPerformanceFrequency(out counterFrequency) == 0) throw new InvalidOperationException("Performance counter unavailable.");
            clock = new TickClock();
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; Application.runInBackground = true;
            // The raw stream provides timeline evidence; CSV uses every tick's native
            // timestamps and every measured frame's GC sample, without loading captures.
            Profiler.logFile = output + ".raw"; Profiler.enableBinaryLog = true; Profiler.enabled = true;
            began = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            if (finished) return;
            if (measuring && frames > 0) allocations[frames - 1] = gc.LastValue;
            if (draining) { Finish(); return; }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!measuring && now - began >= 10)
            { measuring = true; measuredAt = now; firstTick = measurement.TickCount; }
            int ticks = clock.Advance(Time.unscaledDeltaTime);
            long before = Stopwatch.GetTimestamp(), afterSchedule;
            using (ScheduleMarker.Auto()) measurement.Schedule(ticks);
            afterSchedule = Stopwatch.GetTimestamp();
            using (CompleteMarker.Auto()) measurement.Complete();
            long afterComplete = Stopwatch.GetTimestamp();
            if (!measuring) return;
            if (frames == FrameCapacity) throw new InvalidOperationException("Movement frame capacity exhausted.");
            schedule[frames] = afterSchedule - before; complete[frames] = afterComplete - afterSchedule; ticksPerFrame[frames] = ticks;
            frames++;
            if (now - measuredAt >= 60) draining = true;
        }

        private void Finish()
        {
            finished = true; Profiler.enabled = false;
            using (var writer = new StreamWriter(output + "-ticks.csv"))
            {
                writer.WriteLine("tick,grid_ms,integration_ms,overlap_ms,first_check_ms,serial_ms,commit_ms,total_ms");
                for (int t = firstTick; t < measurement.TickCount; t++)
                {
                    writer.Write(t - firstTick);
                    for (int b = 0; b < 6; b++) writer.Write("," + CounterMilliseconds(measurement.Timestamp(t, b + 1) - measurement.Timestamp(t, b)));
                    writer.WriteLine("," + CounterMilliseconds(measurement.Timestamp(t, 6) - measurement.Timestamp(t, 0)));
                }
            }
            using (var writer = new StreamWriter(output + "-frames.csv"))
            {
                writer.WriteLine("frame,ticks,schedule_ms,complete_including_wait_ms,gc_bytes");
                for (int f = 0; f < frames; f++)
                    writer.WriteLine(f + "," + ticksPerFrame[f] + "," + Milliseconds(schedule[f]) + "," + Milliseconds(complete[f]) + "," + allocations[f]);
            }
            Application.Quit(0);
        }

        private static string Milliseconds(long value) => (value * 1000.0 / Stopwatch.Frequency).ToString("F6", CultureInfo.InvariantCulture);
        private string CounterMilliseconds(long value) => (value * 1000.0 / counterFrequency).ToString("F6", CultureInfo.InvariantCulture);

        private void OnDestroy() { gc.Dispose(); measurement?.Dispose(); }
    }
}
