using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Northwatch.EditorTools
{
    public static class LadderProfileAnalysis
    {
        public static void Analyze()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-ladder-capture-path");
            if (index < 0)
            {
                AnalyzeCapture("Logs/unit-ladder/profile.raw", "Logs/unit-ladder/profile-summary.txt");
                return;
            }
            if (index + 1 >= args.Length) throw new ArgumentException("Pass -ladder-capture-path Logs/NAME.raw.");
            string path = Path.GetFullPath(args[index + 1]);
            if (!path.StartsWith(Path.GetFullPath("Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Capture must be inside this project's Logs folder.");
            AnalyzeCapture(path, path + "-summary.txt");
        }

        public static void AnalyzeCapture(string capturePath, string summaryPath)
        {
            using var capture = new ProfileCaptureSession(capturePath);
            var output = new StringBuilder();
            bool anyAllocation = false;
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
            }
            File.WriteAllText(summaryPath, output.ToString());
            if (anyAllocation) throw new InvalidOperationException("Ladder simulation/drawing allocated managed memory.");
        }
    }

    // A raw file is expanded by the native loader before its retained-frame limit
    // applies. Reject oversized captures before calling it; never sample fewer frames.
    internal sealed class ProfileCaptureSession : IDisposable
    {
        private const long MaximumCaptureBytes = 128 * 1024 * 1024;
        private const ulong MaximumPeakBytes = 16_000_000_000;
        private readonly string path;

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

        public void Dispose()
        {
            try { ReportMemory("after_analysis"); }
            finally { ProfilerDriver.ClearAllFrames(); }
        }

        private void ReportMemory(string stage)
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            if (!GetProcessMemoryInfo(process.Handle, out MemoryCounters counters, (uint)Marshal.SizeOf<MemoryCounters>()) ||
                counters.PeakWorkingSet.ToUInt64() == 0 || counters.PeakPagefile.ToUInt64() == 0)
                throw new InvalidOperationException("Native process peak memory counters unavailable.");
            string result = $"stage={stage}; capture_bytes={new FileInfo(path).Length}; peak_working_set_bytes={counters.PeakWorkingSet}; peak_committed_bytes={counters.PeakPagefile}";
            File.AppendAllText(path + "-memory.txt", result + Environment.NewLine);
            UnityEngine.Debug.Log("PROFILE_MEMORY " + result);
            if (counters.PeakWorkingSet.ToUInt64() >= MaximumPeakBytes || counters.PeakPagefile.ToUInt64() >= MaximumPeakBytes)
                throw new InvalidOperationException("Profiler analysis exceeded the 16 GB process memory budget.");
        }
    }
}
