using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Northwatch.EditorTools
{
    public static class SandboxProfileAnalysis
    {
        public static void AnalyzeCheck()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-sandbox-capture");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Pass -sandbox-capture Logs/NAME.raw.");
            string path = CheckCapturePath(args[index + 1]);
            AnalyzeCaptures(new[] { path }, path + "-summary.txt");
        }

        private static string CheckCapturePath(string capture)
        {
            string path = Path.GetFullPath(capture);
            if (!path.StartsWith(Path.GetFullPath("Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Capture must be inside this project's Logs folder.");
            return path;
        }

        public static void AnalyzeSelection()
            => AnalyzeCaptures(new[] { "Logs/selection-before.raw", "Logs/sandbox-2000.raw", "Logs/sandbox-2000-selection.raw" },
                "Logs/102-profile-summary.txt");

        public static void AnalyzeControls()
            => AnalyzeCaptures(new[] { "Logs/135-before.raw", "Logs/135-after-box.raw", "Logs/135-after-double.raw" },
                "Logs/135-profile-summary.txt");

        public static void AnalyzeMoveMarker()
        {
            const string after = "Logs/sandbox-2000-selection.raw";
            AnalyzeCaptures(new[] { "Logs/137-before.raw", after }, "Logs/137-profile-summary.txt", after);
        }

        private static void AnalyzeCaptures(string[] captures, string summaryPath, string moveMarkerCapture = null)
        {
            var output = new StringBuilder();
            bool allocated = false;
            foreach (string path in captures)
            {
                using var capture = new ProfileCaptureSession(path);
                foreach (string scope in new[] { "Northwatch.Simulation", "Northwatch.Drawing", "Northwatch.SelectionInput", "Northwatch.SandboxOverlay", "PlayerLoop", "Northwatch.MoveMarker" })
                {
                    if (scope == "Northwatch.MoveMarker" && path != moveMarkerCapture) continue;
                    if (path == "Logs/selection-before.raw" && scope == "Northwatch.SelectionInput") continue;
                    int samples = 0;
                    long bytes = 0;
                    double total = 0, maximum = 0;
                    for (int frame = ProfilerDriver.firstFrameIndex + 1; frame < ProfilerDriver.lastFrameIndex; frame++)
                    {
                        using RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                        if (!data.valid) continue;
                        int marker = data.GetMarkerId(scope), gc = data.GetMarkerId("GC.Alloc");
                        for (int sample = 0; sample < data.sampleCount; sample++)
                        {
                            if (data.GetSampleMarkerId(sample) != marker) continue;
                            samples++;
                            double ms = data.GetSampleTimeMs(sample);
                            total += ms; maximum = Math.Max(maximum, ms);
                            int end = sample + data.GetSampleChildrenCountRecursive(sample) + 1;
                            for (int child = sample + 1; child < end; child++)
                                if (data.GetSampleMarkerId(child) == gc) bytes += data.GetSampleMetadataAsLong(child, 0);
                        }
                    }
                    if (samples < 200) throw new InvalidOperationException("Not enough samples for " + scope);
                    output.AppendLine($"capture={path}; scope={scope}; samples={samples}; average_ms={total / samples:F6}; max_ms={maximum:F6}; GC_alloc_bytes={bytes}");
                    allocated |= bytes != 0;
                }
                capture.Complete();
            }
            File.WriteAllText(summaryPath, output.ToString());
            Debug.Log(output.ToString());
            if (allocated) throw new InvalidOperationException("Selection capture contains managed allocation.");
        }

        public static void Analyze()
            => AnalyzeVariants(new[] { "0", "2000", "2000-baseline" }, "Logs/sandbox-", "Logs/profile-summary.txt");

        private static void AnalyzeVariants(string[] variants, string capturePrefix, string summaryPath)
        {
            var output = new StringBuilder();
            bool anyAllocation = false;
            foreach (string variant in variants)
            {
                using var capture = new ProfileCaptureSession(capturePrefix + variant + ".raw");
                foreach (string scope in new[] { "Northwatch.Simulation", "Northwatch.Drawing", "Northwatch.SandboxOverlay", "PlayerLoop" })
                {
                    if (variant.EndsWith("-baseline") && scope == "Northwatch.SandboxOverlay") continue;
                    int samples = 0;
                    long allocated = 0;
                    double milliseconds = 0, maximum = 0;
                    var sites = new Dictionary<string, long>();
                    for (int frame = ProfilerDriver.firstFrameIndex + 1; frame < ProfilerDriver.lastFrameIndex; frame++)
                    {
                        using RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                        if (!data.valid) continue;
                        int marker = data.GetMarkerId(scope);
                        int gc = data.GetMarkerId("GC.Alloc");
                        for (int sample = 0; sample < data.sampleCount; sample++)
                        {
                            if (data.GetSampleMarkerId(sample) != marker) continue;
                            samples++;
                            double duration = data.GetSampleTimeMs(sample);
                            milliseconds += duration;
                            maximum = Math.Max(maximum, duration);
                            int end = sample + data.GetSampleChildrenCountRecursive(sample) + 1;
                            for (int child = sample + 1; child < end; child++)
                                if (data.GetSampleMarkerId(child) == gc)
                                {
                                    long bytes = data.GetSampleMetadataAsLong(child, 0);
                                    allocated += bytes;
                                    if (scope != "PlayerLoop") continue;
                                    int parent = child - 1;
                                    var path = new StringBuilder();
                                    for (int depth = 0; depth < 5 && parent > sample; depth++)
                                    {
                                        while (parent > sample && parent + data.GetSampleChildrenCountRecursive(parent) < child) parent--;
                                        path.Insert(0, data.GetSampleName(parent) + "/");
                                        parent--;
                                    }
                                    string site = path.ToString();
                                    sites.TryGetValue(site, out long previous);
                                    sites[site] = previous + bytes;
                                }
                        }
                    }
                    if (samples < 200) throw new InvalidOperationException("Not enough sandbox Profiler samples.");
                    output.AppendLine($"variant={variant}; scope={scope}; samples={samples}; average_ms={milliseconds / samples:F6}; max_ms={maximum:F6}; GC_alloc_bytes={allocated}");
                    foreach (var site in sites) output.AppendLine($"  GC_site={site.Key}; bytes={site.Value}");
                    anyAllocation |= allocated != 0;
                }
                capture.Complete();
            }
            File.WriteAllText(summaryPath, output.ToString());
            Debug.Log(output.ToString());
            if (anyAllocation) throw new InvalidOperationException("Sandbox hot path allocated managed memory; see Logs/profile-summary.txt.");
        }
    }
}
