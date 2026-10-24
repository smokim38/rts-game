using UnityEngine;

namespace Northwatch.Presentation.Diagnostics
{
    // Frame starts occur before script callbacks. A native start timestamp belongs to the
    // interval between the previous callback and this one, independently of delivery delay.
    public sealed class LadderFrameBuffer
    {
        private readonly long[] ends;
        private readonly FrameTiming[] samples;
        private readonly byte[] counts;
        private long previous;
        public int Count { get; private set; }
        public bool Invalid { get; private set; }
        public int Unmatched { get; private set; }

        public LadderFrameBuffer(int capacity, long previousCallback)
        {
            ends = new long[capacity];
            samples = new FrameTiming[capacity];
            counts = new byte[capacity];
            previous = previousCallback;
        }

        public void Begin(long previousCallback) => previous = previousCallback;

        public void Rendered(long callback)
        {
            if (Count == ends.Length || callback <= (Count == 0 ? previous : ends[Count - 1]))
            { Invalid = true; return; }
            ends[Count++] = callback;
        }

        public void Add(FrameTiming sample)
        {
            ulong timestamp = sample.frameStartTimestamp;
            if (Count == 0 || timestamp <= (ulong)previous) return;
            if (timestamp > (ulong)ends[Count - 1]) { Unmatched++; return; }
            int low = 0, high = Count - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if ((ulong)ends[middle] < timestamp) low = middle + 1;
                else high = middle;
            }
            if (counts[low] < byte.MaxValue) counts[low]++;
            samples[low] = sample;
            if (counts[low] != 1 || !(sample.cpuFrameTime > 0) || double.IsInfinity(sample.cpuFrameTime)) Invalid = true;
        }

        public FrameTiming Sample(int index) => samples[index];
        public int SampleCount(int index) => counts[index];
        public long Callback(int index) => ends[index];
    }
}
