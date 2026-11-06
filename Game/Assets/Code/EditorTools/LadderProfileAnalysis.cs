using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Northwatch.EditorTools
{
    public static class LadderProfileAnalysis
    {
        public static void Analyze()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-ladder-capture-path");
            if (index >= 0 && index + 1 >= args.Length) throw new ArgumentException("Pass -ladder-capture-path Logs/NAME.raw.");
            string path = Path.GetFullPath(index < 0 ? "Logs/unit-ladder/profile.raw" : args[index + 1]);
            if (!path.StartsWith(Path.GetFullPath("Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Capture must be inside this project's Logs folder.");
            string summaryPath = path + "-summary.txt";
            AnalyzeCapture(path, summaryPath);
            var metadata = JsonUtility.FromJson<CostMetadata>(File.ReadAllText(Path.ChangeExtension(path, ".json")));
            if (!metadata.development || !metadata.profile || metadata.units != 2000 || metadata.targetFrameRate != 25)
                throw new InvalidOperationException("Worker capture requires the 2,000-unit capped development ladder.");
            var result = JsonUtility.FromJson<WorkerCost>(File.ReadAllText(summaryPath + ".json"));
            result.commit = metadata.commit; result.scenarioVersion = metadata.scenarioVersion;
            result.profileSeconds = metadata.profileSeconds;
            result.crossingChecked = metadata.windowCloseTick >= 1800;
            result.crossingValid = !result.crossingChecked || (metadata.crossing != null && metadata.crossing.Length == 4);
            if (result.crossingChecked && result.crossingValid)
                for (int i = 0; i < 4; i++)
                {
                    var count = metadata.crossing[i];
                    result.crossingValid &= count.cycle == 1 + i / 2 && count.side == i % 2 &&
                        count.units == 1000 && count.passed >= 900 && count.passed <= 1000 && count.met >= 32 && count.met <= 1000;
                }
            File.WriteAllText(summaryPath + ".json", JsonUtility.ToJson(result, true));
            File.AppendAllText(summaryPath, "Wait: use the separate uncapped -ladder-wait run; capped waits are not comparable.\n");
            if (!result.crossingValid || metadata.focusLost || metadata.paused || !metadata.activeUnitsValid)
                throw new InvalidOperationException("Development capture missed the crossing or active-unit rule, or lost focus.");
        }

        [Serializable] private sealed class CostMetadata
        {
            public string commit = null;
            public int scenarioVersion = 0, units = 0, targetFrameRate = 0;
            public bool development = false, profile = false;
            public ulong windowCloseTick = 0;
            public double profileSeconds = 0;
            public bool focusLost = false, paused = false, activeUnitsValid = false;
            public Northwatch.Simulation.LadderCycleCounts[] crossing = null;
        }

        [Serializable] private sealed class WorkerCost
        {
            public string commit;
            public int scenarioVersion, ticks, oneTickFrames, frameRate = 25;
            public double profileSeconds, meanMs, p99Ms;
            public long captureBytes, gcAllocBytes;
            public ulong peakWorkingSetBytes, peakCommittedBytes;
            public bool crossingChecked, crossingValid;
            public JobCost[] jobs;
        }

        [Serializable] private sealed class JobCost
        {
            public string marker;
            public int samples;
            public double totalMs, meanMsPerTick;
            public bool includedInTotal;
        }

        public static void AnalyzeCapture(string capturePath, string summaryPath)
        {
            using var capture = new ProfileCaptureSession(capturePath);
            var output = new StringBuilder();
            bool anyAllocation = false;
            long allocationBytes = 0;
            foreach (string scope in new[] { "Northwatch.Simulation", "Northwatch.Drawing" })
            {
                int samples = 0;
                long allocated = 0;
                double milliseconds = 0;
                for (int frame = ProfilerDriver.firstFrameIndex + 1; frame < ProfilerDriver.lastFrameIndex; frame++)
                {
                    using RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                    if (!data.valid) continue;
                    int marker = data.GetMarkerId(scope), gc = data.GetMarkerId("GC.Alloc");
                    for (int sample = 0; sample < data.sampleCount; sample++)
                    {
                        if (data.GetSampleMarkerId(sample) != marker) continue;
                        samples++; milliseconds += data.GetSampleTimeMs(sample);
                        int end = sample + data.GetSampleChildrenCountRecursive(sample) + 1;
                        for (int child = sample + 1; child < end; child++)
                            if (data.GetSampleMarkerId(child) == gc) allocated += data.GetSampleMetadataAsLong(child, 0);
                    }
                }
                if (samples < 200)
                    throw new InvalidOperationException("Ladder capture is too short.");
                output.AppendLine(FormattableString.Invariant($"{scope}: samples={samples}; average_ms={milliseconds / samples:F6}; GC_alloc_bytes={allocated}"));
                anyAllocation |= allocated != 0;
                allocationBytes += allocated;
            }
            File.WriteAllText(summaryPath, output.ToString());
            string[] markers = { "ApplyCommandsJob", "SelectionJob", "PublishFieldsJob", "RequestFieldsJob",
                "AssignFieldBuildJob", "BuildFlowFieldJob", "BucketUnitsJob", "BuildSpatialGridJob", "OrderProgressJob",
                "PreferredVelocityJob", "IntegrateMovementJob", "OverlapMovementJob", "MoveCheckPassJob", "CommitMovementJob",
                "FillDrawDataJob", "LadderMeetJob", "LadderPassJob" };
            var costs = new JobCost[markers.Length];
            for (int i = 0; i < costs.Length; i++) costs[i] = new JobCost
                { marker = markers[i], includedInTotal = i < 14 && i != 1 };
            int ticks = 0;
            double total = 0;
            var oneTickTimes = new List<double>();
            for (int frame = ProfilerDriver.firstFrameIndex + 1; frame < ProfilerDriver.lastFrameIndex; frame++)
            {
                int frameTicks = 0;
                double frameTime = 0;
                for (int thread = 0; ; thread++)
                {
                    using RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!data.valid) break;
                    for (int sample = 0; sample < data.sampleCount; sample++)
                    {
                        string name = data.GetSampleName(sample);
                        if (string.IsNullOrEmpty(name) || name.Contains("Schedule")) continue;
                        for (int i = 0; i < markers.Length; i++)
                            if (name.Contains(markers[i]))
                            {
                                double ms = data.GetSampleTimeMs(sample);
                                costs[i].samples++; costs[i].totalMs += ms;
                                if (costs[i].includedInTotal) frameTime += ms;
                                if (markers[i] == "BuildSpatialGridJob") frameTicks++;
                                sample += data.GetSampleChildrenCountRecursive(sample);
                                break;
                            }
                    }
                }
                ticks += frameTicks; total += frameTime;
                if (frameTicks == 1) oneTickTimes.Add(frameTime);
            }
            oneTickTimes.Sort();
            foreach (JobCost cost in costs) cost.meanMsPerTick = ticks == 0 ? 0 : cost.totalMs / ticks;
            capture.Complete();
            var result = new WorkerCost { ticks = ticks, oneTickFrames = oneTickTimes.Count,
                meanMs = ticks == 0 ? 0 : total / ticks,
                p99Ms = oneTickTimes.Count == 0 ? 0 : oneTickTimes[(int)Math.Ceiling(.99 * oneTickTimes.Count) - 1],
                jobs = costs, captureBytes = new FileInfo(capturePath).Length, gcAllocBytes = allocationBytes,
                peakWorkingSetBytes = capture.PeakWorkingSetBytes, peakCommittedBytes = capture.PeakCommittedBytes };
            File.WriteAllText(summaryPath + ".json", JsonUtility.ToJson(result, true));
            File.AppendAllText(summaryPath, FormattableString.Invariant($"Worker jobs: ticks={ticks}; mean_ms_per_tick={result.meanMs:F6}; p99_one_tick_ms={result.p99Ms:F6}\n"));
            if (anyAllocation) throw new InvalidOperationException("Ladder simulation/drawing allocated managed memory.");
        }
    }

    // A raw file is expanded by the native loader before its retained-frame limit
    // applies. Reject oversized captures before calling it; never sample fewer frames.
    internal sealed class ProfileCaptureSession : IDisposable
    {
        // Uncapped three-second sandbox captures reach 172 MB on the reference PC.
        // Keep those intact while refusing multi-GB logs before native expansion.
        private const long MaximumCaptureBytes = 192 * 1024 * 1024;
        private const ulong MaximumPeakBytes = 16_000_000_000;
        private readonly string path;
        private bool completed;
        internal ulong PeakWorkingSetBytes { get; private set; }
        internal ulong PeakCommittedBytes { get; private set; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryCounters
        {
            public uint Size, PageFaults;
            public UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonpagedPool, NonpagedPool, Pagefile, PeakPagefile;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);

        internal ProfileCaptureSession(string path)
        {
            this.path = path;
            long bytes = new FileInfo(path).Length;
            if (bytes > MaximumCaptureBytes)
                throw new InvalidOperationException($"Capture is {bytes} bytes; maximum is {MaximumCaptureBytes}. Record a shorter capture or cap diagnostic capture FPS; no frames were analyzed.");
            ReportMemory("before_load");
            ProfilerDriver.enabled = false;
            ProfilerDriver.ClearAllFrames();
            try
            {
                if (!ProfilerDriver.LoadProfile(path, false)) throw new InvalidOperationException("Could not load " + path);
            }
            catch { ProfilerDriver.ClearAllFrames(); throw; }
        }

        internal void Complete()
        {
            MemoryCounters counters = ReportMemory("after_analysis");
            PeakWorkingSetBytes = counters.PeakWorkingSet.ToUInt64();
            PeakCommittedBytes = counters.PeakPagefile.ToUInt64();
            completed = true;
            if (counters.PeakWorkingSet.ToUInt64() >= MaximumPeakBytes || counters.PeakPagefile.ToUInt64() >= MaximumPeakBytes)
                throw new InvalidOperationException("Profiler analysis exceeded the 16 GB process memory budget.");
        }

        public void Dispose()
        {
            // Cleanup must preserve the analysis error, even if its memory report fails.
            try { if (!completed) ReportMemory("analysis_incomplete"); }
            catch (Exception error) { UnityEngine.Debug.Log("PROFILE_MEMORY report failed: " + error.Message); }
            finally { ProfilerDriver.ClearAllFrames(); }
        }

        private MemoryCounters ReportMemory(string stage)
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            if (!GetProcessMemoryInfo(process.Handle, out MemoryCounters counters, (uint)Marshal.SizeOf<MemoryCounters>()) ||
                counters.PeakWorkingSet.ToUInt64() == 0 || counters.PeakPagefile.ToUInt64() == 0)
                throw new InvalidOperationException("Native process peak memory counters unavailable.");
            string result = $"stage={stage}; capture_bytes={new FileInfo(path).Length}; peak_working_set_bytes={counters.PeakWorkingSet}; peak_committed_bytes={counters.PeakPagefile}";
            File.AppendAllText(path + "-memory.txt", result + Environment.NewLine);
            UnityEngine.Debug.Log("PROFILE_MEMORY " + result);
            // These are process-lifetime high-water marks, including earlier captures.
            // Only Complete enforces them; before_load is context, not analysis work.
            return counters;
        }
    }
}
