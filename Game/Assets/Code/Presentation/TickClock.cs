using System;

namespace Northwatch.Presentation
{
    public sealed class TickClock
    {
        public const double TickDuration = 1.0 / 30.0;
        private double accumulator;

        public int Advance(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            accumulator += seconds;
            int ticks = (int)Math.Floor((accumulator + 1e-10) / TickDuration);
            accumulator -= ticks * TickDuration;
            return ticks;
        }
    }
}
