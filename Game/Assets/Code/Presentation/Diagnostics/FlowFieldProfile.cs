#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using Northwatch.Simulation.FlowFields;
using Northwatch.Simulation.Movement;
using Unity.Profiling;
using UnityEngine;

namespace Northwatch.Presentation.Diagnostics
{
    public sealed class FlowFieldProfile : MonoBehaviour
    {
        private FlowFieldMeasurement measurement;
        private MovementParameters settings;
        private string output;
        private int mode, invalidFrames;
        private double nextTick, began;
        private bool finished;
        private bool requestStress;
        private ProfilerRecorder scopedGc;
        private long simulationBytes;
        private bool scopeCapacityReached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (Array.IndexOf(MovementCoreProfile.ProfileArguments(), "-flow-field-profile") < 0) return;
            var ladder = FindFirstObjectByType<PerformanceLadder>();
            if (ladder != null) ladder.enabled = false;
            new GameObject("FlowFieldProfile").AddComponent<FlowFieldProfile>();
        }
        private void Start()
        {
            string[] args = MovementCoreProfile.ProfileArguments(); int index = Array.IndexOf(args, "-flow-output");
            requestStress = Array.IndexOf(args, "-flow-request-stress") >= 0;
            if (requestStress) mode = 2;
            if (!Debug.isDebugBuild || index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Use a development Player with -flow-field-profile -flow-output PATH.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            output = Path.GetFullPath(args[index + 1]);
            if (!output.StartsWith(Path.Combine(project, "Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Flow results must be under the project's Logs folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            settings = FindFirstObjectByType<MatchDriver>().MovementSettings.Snapshot();
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; Application.runInBackground = true;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            began = Time.realtimeSinceStartupAsDouble; nextTick = began + (requestStress ? 0 : 10);
            measurement = new FlowFieldMeasurement(settings, mode, requestStress);
            if (requestStress)
            {
                scopedGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 256,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                if (!scopedGc.Valid) throw new InvalidOperationException("Scoped allocation recorder unavailable.");
                scopedGc.Reset(); scopedGc.Start(); GC.KeepAlive(new byte[256]); scopedGc.Stop();
                if (scopedGc.Count == 0) throw new InvalidOperationException("Scoped recorder did not detect its setup allocation.");
            }
        }
        private void Update()
        {
            if (finished || measurement == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            bool warming = now - began < 10;
            if (warming && !requestStress) return;
            if (!warming && (!Application.isFocused || Screen.width != 1920 || Screen.height != 1080 ||
                Screen.fullScreenMode != FullScreenMode.FullScreenWindow)) invalidFrames++;
            if (now < nextTick) return;
            nextTick += 1.0 / 30;
            if (requestStress && !warming) { scopedGc.Reset(); scopedGc.Start(); }
            measurement.Step();
            if (requestStress && !warming)
            {
                scopedGc.Stop();
                if (scopedGc.Count == 256) scopeCapacityReached = true;
                for (int i = 0; i < scopedGc.Count; i++) simulationBytes += scopedGc.GetSample(i).Value;
            }
            if (!measurement.Finished) return;
            using (var writer = new StreamWriter(output + "-" + mode + ".txt"))
            {
                measurement.Write(writer); writer.WriteLine("invalid_frames=" + invalidFrames);
                if (requestStress)
                {
                    writer.WriteLine("simulation_scope_bytes=" + simulationBytes);
                    writer.WriteLine("scope_capacity_reached=" + scopeCapacityReached);
                }
            }
            measurement.Dispose(); measurement = null;
            if (requestStress || ++mode == 9)
            {
                finished = true;
                Application.Quit(invalidFrames == 0 && simulationBytes == 0 && !scopeCapacityReached ? 0 : 1); return;
            }
            measurement = new FlowFieldMeasurement(settings, mode);
            nextTick = Time.realtimeSinceStartupAsDouble + 1.0 / 30;
        }
        private void OnDestroy() { measurement?.Dispose(); scopedGc.Dispose(); }
    }
}
#endif
