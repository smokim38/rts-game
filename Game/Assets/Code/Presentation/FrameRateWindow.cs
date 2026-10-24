namespace Northwatch.Presentation
{
    public sealed class FrameRateWindow
    {
        private readonly float[] durations = new float[16384];
        private int first, count;
        private double elapsed;
        public float FramesPerSecond => elapsed > 0 ? (float)(count / elapsed) : 0f;
        public float Milliseconds => count > 0 ? (float)(elapsed * 1000 / count) : 0f;

        public void Advance(float seconds)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            if (count == durations.Length) RemoveFirst();
            durations[(first + count) % durations.Length] = seconds;
            count++;
            elapsed += seconds;
            while (count > 1 && elapsed - durations[first] >= 0.5) RemoveFirst();
        }

        private void RemoveFirst()
        {
            elapsed -= durations[first];
            first = (first + 1) % durations.Length;
            count--;
        }
    }
}
