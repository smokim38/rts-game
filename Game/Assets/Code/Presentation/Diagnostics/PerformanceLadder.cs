using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Northwatch.Simulation;
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
        private bool measuring, draining, finished, focusLost, paused, featureEnabled, profile;
        private bool activeUnitsValid = true;

        public void Configure(LadderScenario settings, MatchDriver match) { scenario = settings; driver = match; }

        private void Start()
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
            profile = Array.IndexOf(arguments, "-ladder-profile") >= 0;
            if (profile != Debug.isDebugBuild) throw new InvalidOperationException("Use the release Player for measurements and development Player for captures.");
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            featureEnabled = FrameTimingManager.IsFeatureEnabled() &&
                FrameTimingManager.GetCpuTimerFrequency() == (ulong)Stopwatch.Frequency;
            buffer = new LadderFrameBuffer(scenario.FrameCapacity, 0);
            if (!driver.Simulation.QueueBlock(Side.First, scenario.FirstSize, scenario.FirstSpawn, unitCount / 2) ||
                !driver.Simulation.QueueBlock(Side.Second, scenario.SecondSize, scenario.SecondSpawn, unitCount / 2))
                throw new InvalidOperationException("Ladder blocks could not be queued.");
            started = Time.realtimeSinceStartupAsDouble;
            previousCallback = Stopwatch.GetTimestamp();
        }

        private void LateUpdate()
        {
            if (finished) return;
            double now = Time.realtimeSinceStartupAsDouble;
            long callback = Stopwatch.GetTimestamp();
            if (!measuring && !draining && now - started >= scenario.WarmupSeconds)
            {
                measuring = true; windowStart = now;
                buffer.Begin(previousCallback);
                renderedStart = Time.renderedFrameCount;
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
                if (now - windowStart >= scenario.WindowSeconds)
                {
                    measuring = false; draining = true; windowEnd = now;
                    renderedEnd = Time.renderedFrameCount;
                }
                else
                {
                    buffer.Rendered(callback);
                    focusLost |= !Application.isFocused;
                    featureEnabled &= FrameTimingManager.IsFeatureEnabled();
                    activeUnitsValid &= driver.Simulation.Count == unitCount &&
                        driver.Simulation.DrawCount(Side.First) == unitCount / 2 && driver.Simulation.DrawCount(Side.Second) == unitCount / 2;
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
                units = unitCount, scenarioVersion = scenario.Version, seed = scenario.Seed,
                featureEnabled = featureEnabled, focusLost = focusLost, paused = paused, activeUnitsValid = activeUnitsValid,
                bufferInvalid = buffer.Invalid, renderedFrames = renderedEnd - renderedStart,
                recordedFrames = buffer.Count, seconds = windowEnd - windowStart,
                warmupSeconds = windowStart - started, width = Screen.width, height = Screen.height,
                borderless = Screen.fullScreenMode == FullScreenMode.FullScreenWindow,
                development = Debug.isDebugBuild, unity = Application.unityVersion,
                api = SystemInfo.graphicsDeviceType.ToString(), quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                cpu = SystemInfo.processorType, threads = SystemInfo.processorCount,
                gpu = SystemInfo.graphicsDeviceName, vramMb = SystemInfo.graphicsMemorySize,
                ramMb = SystemInfo.systemMemorySize, graphicsVersion = SystemInfo.graphicsDeviceVersion,
                vsync = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate
            }, true));
            Application.Quit(0);
        }

        [Serializable]
        private sealed class RunMetadata
        {
            public int units, scenarioVersion, seed, renderedFrames, recordedFrames, width, height, threads, vramMb, ramMb, vsync, targetFrameRate;
            public bool featureEnabled, focusLost, paused, bufferInvalid, borderless, development, activeUnitsValid;
            public double seconds, warmupSeconds;
            public string unity, api, quality, cpu, gpu, graphicsVersion;
        }
    }
}
