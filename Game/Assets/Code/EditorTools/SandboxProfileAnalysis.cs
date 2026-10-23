using System;
using System.IO;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Northwatch.EditorTools
{
    public static class SandboxProfileAnalysis
    {
        public static void Analyze()
        {
            var output = new StringBuilder();
            foreach (int units in new[] { 0, 2000 })
            {
                if (!ProfilerDriver.LoadProfile("Logs/sandbox-" + units + ".raw", false))
                    throw new InvalidOperationException("Could not load sandbox Player capture.");
                foreach (string scope in new[] { "Northwatch.Simulation", "Northwatch.Drawing" })
                {
                    int samples = 0;
                    long allocated = 0;
                    double milliseconds = 0, maximum = 0;
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
                                if (data.GetSampleMarkerId(child) == gc) allocated += data.GetSampleMetadataAsLong(child, 0);
                        }
                    }
                    if (samples < 200) throw new InvalidOperationException("Not enough sandbox Profiler samples.");
                    output.AppendLine($"units={units}; scope={scope}; samples={samples}; average_ms={milliseconds / samples:F6}; max_ms={maximum:F6}; GC_alloc_bytes={allocated}");
                    if (allocated != 0) throw new InvalidOperationException("Sandbox hot path allocated managed memory.");
                }
            }
            File.WriteAllText("Logs/profile-summary.txt", output.ToString());
            Debug.Log(output.ToString());
        }
    }
}
