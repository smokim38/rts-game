using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Northwatch.Simulation;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Northwatch.Presentation.Diagnostics
{
    [DefaultExecutionOrder(1000)]
    public sealed class PerformanceLadder : MonoBehaviour
    {
        [SerializeField] private LadderScenario scenario;
        [SerializeField] private MatchDriver driver;
        private readonly FrameTiming[] latest = new FrameTiming[64];
        private LadderFrameBuffer buffer;
        private string output;
        private int unitCount, renderedStart, renderedEnd;
        private double started, windowStart, windowEnd;
        private long previousCallback;
        private ulong lastDelivered;
        private bool measuring, draining, finished, focusLost, paused, featureEnabled, profile, waitMode;
        private bool activeUnitsValid = true;
        private readonly int[] warmupRenewals = new int[4], measuredRenewals = new int[4];
        private double profileSeconds = 60;
        private ProfilerRecorder waitRecorder;
        private long[] waitTimes;
        private int waitFrames, previousCompletedTicks;
        private ulong windowCloseTick;
        private string commit;

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceCounter(out long timestamp);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceFrequency(out long frequency);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref int length);

        private static int ReadPhysicalCoreCount()
        {
            const int processorCore = 0, insufficientBuffer = 122;
            int length = 0;
            if (GetLogicalProcessorInformationEx(processorCore, IntPtr.Zero, ref length) ||
                Marshal.GetLastWin32Error() != insufficientBuffer || length <= 0)
                throw new InvalidOperationException("Windows CPU topology is unavailable.");
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetLogicalProcessorInformationEx(processorCore, buffer, ref length))
                    throw new InvalidOperationException("Windows CPU topology could not be read.");
                int cores = 0, offset = 0;
                while (offset < length)
                {
                    if (length - offset < 8 || Marshal.ReadInt32(buffer, offset) != processorCore)
                        throw new InvalidOperationException("Invalid Windows CPU topology record.");
                    int size = Marshal.ReadInt32(buffer, offset + 4);
                    if (size < 8 || size > length - offset)
                        throw new InvalidOperationException("Invalid Windows CPU topology size.");
                    cores++;
                    offset += size;
                }
                if (cores == 0) throw new InvalidOperationException("Windows reported no CPU cores.");
                return cores;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Configure(LadderScenario settings, MatchDriver match) { scenario = settings; driver = match; }

        private void Start()
        {
            try { Initialize(); }
            catch (Exception error) { Fail(error.Message); }
        }

        private void Fail(string reason)
        {
            finished = true;
            enabled = false;
            Debug.LogError("Performance ladder failed: " + reason);
            Application.Quit(1);
        }

        private void Initialize()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int countIndex = Array.IndexOf(arguments, "-ladder-units");
            int outputIndex = Array.IndexOf(arguments, "-ladder-output");
            if (countIndex < 0 || countIndex + 1 >= arguments.Length || !int.TryParse(arguments[countIndex + 1], out unitCount) ||
                unitCount < 10 || unitCount > UnitSimulation.Capacity || unitCount % 2 != 0 ||
                outputIndex < 0 || outputIndex + 1 >= arguments.Length)
                throw new ArgumentException("Use -ladder-units N -ladder-output PATH.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string logs = Path.Combine(project, "Logs") + Path.DirectorySeparatorChar;
            output = Path.GetFullPath(arguments[outputIndex + 1]);
            if (!File.Exists(Path.Combine(project, "ProjectSettings/ProjectVersion.txt")) ||
                !output.StartsWith(logs, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Ladder output must be under this project's Logs folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            commit = File.ReadAllText(Path.Combine(project, "Builds/PerformanceLadder/commit.txt")).Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(commit, "^[0-9a-f]{40}$"))
                throw new InvalidOperationException("Missing measured build commit.");
            profile = Array.IndexOf(arguments, "-ladder-profile") >= 0;
            waitMode = Array.IndexOf(arguments, "-ladder-wait") >= 0;
            if ((profile || waitMode) != Debug.isDebugBuild || (profile && waitMode))
                throw new InvalidOperationException("Use release for measurements, development with either -ladder-profile or -ladder-wait for cost runs.");
            int secondsIndex = Array.IndexOf(arguments, "-ladder-profile-seconds");
            if (secondsIndex >= 0 && (!profile || secondsIndex + 1 >= arguments.Length ||
                !double.TryParse(arguments[secondsIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out profileSeconds) ||
                profileSeconds < 10 || profileSeconds > 60 || double.IsNaN(profileSeconds)))
                throw new ArgumentException("Use -ladder-profile-seconds 10..60 only for diagnostic captures.");
            QualitySettings.vSyncCount = 0;
            // A 60-second unbounded-FPS raw capture can expand to over 100 GB in
            // the Editor loader. 25 fps keeps the full tank capture below the
            // loader's byte budget and its 2,000-frame retention limit.
            Application.targetFrameRate = profile ? 25 : -1;
            Application.runInBackground = true;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            // Unity's native frame timestamps use QPC. Mono's Stopwatch can have a
            // different origin even when its frequency is the same.
            if (!QueryPerformanceFrequency(out long frequency) || !QueryPerformanceCounter(out previousCallback))
                throw new InvalidOperationException("Windows performance counter is unavailable.");
            featureEnabled = FrameTimingManager.IsFeatureEnabled() &&
                FrameTimingManager.GetCpuTimerFrequency() == (ulong)frequency;
            buffer = new LadderFrameBuffer(scenario.FrameCapacity, 0);
            if (waitMode)
            {
                waitTimes = new long[scenario.FrameCapacity];
                waitRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Northwatch.Simulation.Wait", 1,
                    ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                    ProfilerRecorderOptions.SumAllSamplesInFrame);
                if (!waitRecorder.Valid) throw new InvalidOperationException("Simulation wait recorder unavailable.");
            }
            scenario.QueueUnits(driver.Simulation, unitCount);
            scenario.QueueOrders(driver.Simulation, unitCount);
            started = Time.realtimeSinceStartupAsDouble;
        }

        private void LateUpdate()
        {
            if (finished) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!QueryPerformanceCounter(out long callback))
            {
                Fail("Windows performance counter is unavailable.");
                return;
            }
            if (!measuring && !draining && now - started >= scenario.WarmupSeconds)
            {
                measuring = true; windowStart = now;
                buffer.Begin(previousCallback);
                renderedStart = Time.renderedFrameCount;
                for (int rule = 1; rule <= 4; rule++) warmupRenewals[rule - 1] = driver.Simulation.RenewedOrderCount(rule);
#if DEVELOPMENT_BUILD
                if (profile)
                {
                    UnityEngine.Profiling.Profiler.logFile = output + ".raw";
                    UnityEngine.Profiling.Profiler.enableBinaryLog = true;
                    UnityEngine.Profiling.Profiler.enabled = true;
                }
#endif
            }
            if (measuring)
            {
                // LastValue is the previous rendered frame; keep the completed
                // tick count from that frame beside it (ADR 0012 set C).
                if (waitMode && previousCompletedTicks > 0)
                {
                    if (waitFrames == waitTimes.Length) { Fail("Wait buffer overflow."); return; }
                    waitTimes[waitFrames++] = waitRecorder.LastValue;
                }
                activeUnitsValid &= driver.Simulation.Count == unitCount && driver.Simulation.MovingOrdersValid &&
                    driver.Simulation.DrawCount(Side.First) == unitCount / 2 && driver.Simulation.DrawCount(Side.Second) == unitCount / 2;
#if DEVELOPMENT_BUILD
                if (profile && now - windowStart >= profileSeconds)
                {
                    UnityEngine.Profiling.Profiler.enabled = false;
                    UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                }
#endif
                if (now - windowStart >= scenario.WindowSeconds)
                {
                    measuring = false; draining = true; windowEnd = now;
                    renderedEnd = Time.renderedFrameCount;
                    buffer.End();
                    windowCloseTick = driver.Simulation.TickCount;
                    for (int rule = 1; rule <= 4; rule++)
                        measuredRenewals[rule - 1] = driver.Simulation.RenewedOrderCount(rule) - warmupRenewals[rule - 1];
                }
                else
                {
                    buffer.Rendered(callback);
                    focusLost |= !Application.isFocused;
                    featureEnabled &= FrameTimingManager.IsFeatureEnabled();
                }
            }
            uint count = FrameTimingManager.GetLatestTimings((uint)latest.Length, latest);
            // GetLatestTimings overlaps earlier polls. Consume each native timestamp once;
            // distinct samples landing on one rendered frame are still rejected by the buffer.
            ulong newest = lastDelivered;
            for (int i = 0; i < count; i++)
                if (latest[i].frameStartTimestamp > lastDelivered)
                {
                    if (measuring || draining) buffer.Add(latest[i]);
                    newest = Math.Max(newest, latest[i].frameStartTimestamp);
                }
            lastDelivered = newest;
            FrameTimingManager.CaptureFrameTimings();
            previousCallback = callback;
            previousCompletedTicks = driver.CompletedTicks;
            if (draining && now - windowEnd >= scenario.DrainSeconds) Finish();
        }

        private void OnApplicationFocus(bool focused) { if (measuring && !focused) focusLost = true; }
        private void OnApplicationPause(bool value) { if (measuring && value) paused = true; }

        private void Finish()
        {
            finished = true;
#if DEVELOPMENT_BUILD
            if (profile)
            {
                UnityEngine.Profiling.Profiler.enabled = false;
                UnityEngine.Profiling.Profiler.enableBinaryLog = false;
            }
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var crossing = new LadderCycleCounts[4];
            var cycleZeroCrossing = new LadderCycleCounts[2];
            for (int side = 0; side < 2; side++)
                cycleZeroCrossing[side] = driver.Simulation.LadderCrossingCount(0, (Side)side);
            for (int cycle = 1; cycle <= 2; cycle++)
                for (int side = 0; side < 2; side++)
                    crossing[(cycle - 1) * 2 + side] = driver.Simulation.LadderCrossingCount(cycle, (Side)side);
            if (waitMode)
            {
                using var writer = new StreamWriter(output + "-wait.csv");
                writer.WriteLine("waitMs");
                for (int i = 0; i < waitFrames; i++)
                    writer.WriteLine((waitTimes[i] * .000001).ToString("R", CultureInfo.InvariantCulture));
                waitRecorder.Dispose();
            }
            using (var writer = new StreamWriter(output + ".csv"))
            {
                writer.WriteLine("callback,timestamp,samples,frameMs,mainMs,gpuMs");
                for (int i = 0; i < buffer.Count; i++)
                {
                    FrameTiming timing = buffer.Sample(i);
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:R},{4:R},{5:R}",
                        buffer.Callback(i), timing.frameStartTimestamp, buffer.SampleCount(i),
                        timing.cpuFrameTime, timing.cpuMainThreadFrameTime, timing.gpuFrameTime));
                }
            }
            // Only allow-listed hardware/build fields are collected; raw output stays ignored.
            File.WriteAllText(output + ".json", JsonUtility.ToJson(new RunMetadata
            {
                units = unitCount, scenarioVersion = scenario.Version, seed = scenario.Seed, commit = commit,
                featureEnabled = featureEnabled, focusLost = focusLost, paused = paused, activeUnitsValid = activeUnitsValid,
                renewedOrdersByArrivalRule = measuredRenewals, warmupRenewedOrdersByArrivalRule = warmupRenewals,
                windowCloseTick = windowCloseTick, crossing = crossing, cycleZeroCrossing = cycleZeroCrossing,
                meetDistance = scenario.MeetDistance,
                laneGap = scenario.LaneGap, spawnDistance = scenario.SecondSpawn.x - scenario.FirstSpawn.x,
                profile = profile, waitMode = waitMode, profileSeconds = profileSeconds,
                bufferInvalid = buffer.Invalid, renderedFrames = renderedEnd - renderedStart,
                matchedSamples = buffer.Matched, unmatchedSamples = buffer.Unmatched,
                recordedFrames = buffer.Count, seconds = windowEnd - windowStart,
                warmupSeconds = windowStart - started, width = Screen.width, height = Screen.height,
                borderless = Screen.fullScreenMode == FullScreenMode.FullScreenWindow,
                development = Debug.isDebugBuild, unity = Application.unityVersion,
                api = SystemInfo.graphicsDeviceType.ToString(), quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                cpu = SystemInfo.processorType, cores = ReadPhysicalCoreCount(), threads = SystemInfo.processorCount,
                gpu = SystemInfo.graphicsDeviceName, vramMb = SystemInfo.graphicsMemorySize,
                ramMb = SystemInfo.systemMemorySize, graphicsVersion = SystemInfo.graphicsDeviceVersion,
                vsync = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate
            }, true));
            Application.Quit(0);
        }

        [Serializable]
        private sealed class RunMetadata
        {
            public int units, scenarioVersion, seed, renderedFrames, recordedFrames, matchedSamples, unmatchedSamples, width, height, cores, threads, vramMb, ramMb, vsync, targetFrameRate;
            public bool featureEnabled, focusLost, paused, bufferInvalid, borderless, development, activeUnitsValid;
            public int[] renewedOrdersByArrivalRule, warmupRenewedOrdersByArrivalRule;
            public ulong windowCloseTick;
            public LadderCycleCounts[] crossing, cycleZeroCrossing;
            public float meetDistance, laneGap, spawnDistance;
            public bool profile, waitMode;
            public double profileSeconds;
            public double seconds, warmupSeconds;
            public string unity, api, quality, cpu, gpu, graphicsVersion, commit;
        }
    }
}
