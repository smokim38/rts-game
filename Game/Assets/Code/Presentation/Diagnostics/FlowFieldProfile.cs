#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using Northwatch.Simulation.FlowFields;
using Northwatch.Simulation.Movement;
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
            began = Time.realtimeSinceStartupAsDouble; nextTick = began + 10;
            measurement = new FlowFieldMeasurement(settings, mode);
        }
        private void Update()
        {
            if (finished || measurement == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - began < 10) return;
            if (!Application.isFocused || Screen.width != 1920 || Screen.height != 1080 ||
                Screen.fullScreenMode != FullScreenMode.FullScreenWindow) invalidFrames++;
            if (now < nextTick) return;
            nextTick += 1.0 / 30;
            measurement.Step();
            if (!measurement.Finished) return;
            using (var writer = new StreamWriter(output + "-" + mode + ".txt"))
            { measurement.Write(writer); writer.WriteLine("invalid_frames=" + invalidFrames); }
            measurement.Dispose(); measurement = null;
            if (++mode == 5) { finished = true; Application.Quit(invalidFrames == 0 ? 0 : 1); return; }
            measurement = new FlowFieldMeasurement(settings, mode);
            nextTick = Time.realtimeSinceStartupAsDouble + 1.0 / 30;
        }
        private void OnDestroy() => measurement?.Dispose();
    }
}
#endif
