using System;
using System.IO;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Northwatch.EditorTools
{
    public static class LadderProfileAnalysis
    {
        public static void Analyze()
        {
            if (!ProfilerDriver.LoadProfile("Logs/unit-ladder/profile.raw", false))
                throw new InvalidOperationException("Ladder development capture is missing.");
            var output = new StringBuilder();
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
                if (samples < 200 || allocated != 0)
                    throw new InvalidOperationException("Ladder capture is too short or simulation/drawing allocated managed memory.");
                output.AppendLine(FormattableString.Invariant($"{scope}: samples={samples}; average_ms={milliseconds / samples:F6}; GC_alloc_bytes={allocated}"));
            }
            File.WriteAllText("Logs/unit-ladder/profile-summary.txt", output.ToString());
        }
    }
}
