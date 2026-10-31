using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Northwatch.Presentation;

namespace Northwatch.EditorTools
{
    public static class MovementCoreProfileBuilder
    {
        public static void TunePasses()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-movement-passes");
            if (index < 0 || index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out int passes) || passes < 1 || passes > 4)
                throw new ArgumentException("Pass -movement-passes 1|2|3|4.");
            MovementSettings settings = AssetDatabase.LoadAssetAtPath<MovementSettings>(SandboxSceneBuilder.MovementSettingsPath);
            if (settings == null) throw new InvalidOperationException("Movement settings asset is missing.");
            using var serialized = new SerializedObject(settings);
            serialized.FindProperty("moveCheckPasses").intValue = passes;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            settings.Snapshot();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        public static void BuildWindows()
            => Build(PerformanceLadderBuilder.ScenePath, "MovementCore");

        public static void BuildOrderCheckWindows()
            => Build("Assets/Scenes/Sandbox.unity", "MovementOrders");

        public static void BuildStaticMapChecks()
        {
            BuildOrderCheckWindows();
            BuildWindows();
        }

        public static void AnalyzeCapture()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-movement-capture-path");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Pass -movement-capture-path Logs/NAME.raw.");
            string path = Path.GetFullPath(args[index + 1]);
            if (!path.StartsWith(Path.GetFullPath("Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Capture must be inside this project's Logs folder.");
            if (!ProfilerDriver.LoadProfile(path, false)) throw new InvalidOperationException("Could not load movement capture.");
            var output = new StringBuilder();
            var sites = new Dictionary<string, long>();
            long simulationBytes = 0, drawingBytes = 0;
            int frames = 0;
            for (int frame = ProfilerDriver.firstFrameIndex + 1; frame < ProfilerDriver.lastFrameIndex; frame++)
            {
                frames++;
                for (int thread = 0; ; thread++)
                {
                    using RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!data.valid) break;
                    int gc = data.GetMarkerId("GC.Alloc");
                    for (int sample = 0; sample < data.sampleCount; sample++)
                    {
                        if (data.GetSampleMarkerId(sample) != gc) continue;
                        long bytes = data.GetSampleMetadataAsLong(sample, 0);
                        var site = new StringBuilder("GC.Alloc");
                        for (int parent = sample - 1; parent >= 0; parent--)
                        {
                            if (parent + data.GetSampleChildrenCountRecursive(parent) < sample) continue;
                            string name = data.GetSampleName(parent);
                            if (name == "Northwatch.Simulation") simulationBytes += bytes;
                            if (name == "Northwatch.Drawing") drawingBytes += bytes;
                            site.Insert(0, name + "/");
                        }
                        string key = site.ToString();
                        sites.TryGetValue(key, out long previous); sites[key] = previous + bytes;
                    }
                }
            }
            output.AppendLine($"loaded_frames={frames}; simulation_bytes={simulationBytes}; drawing_bytes={drawingBytes}");
            foreach (var site in sites) output.AppendLine($"bytes={site.Value}; site={site.Key}");
            File.WriteAllText(path + "-allocations.txt", output.ToString());
            if (frames < 200 || simulationBytes != 0 || drawingBytes != 0)
                throw new InvalidOperationException("Movement capture has insufficient frames or hot-path allocation.");
        }

        private static void Build(string scene, string folder)
        {
            Directory.CreateDirectory("Builds/" + folder);
            BuildReport report = PlayerBuildHelper.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = "Builds/" + folder + "/" + folder + ".exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 || report.summary.totalWarnings != 0)
                throw new InvalidOperationException("Movement development Player failed or produced warnings.");
        }
    }
}
