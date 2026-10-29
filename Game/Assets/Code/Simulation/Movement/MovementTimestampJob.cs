using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct MovementTimestampJob : IJob
    {
        internal NativeArray<long> Values;
        internal int Index;
        [DllImport("kernel32.dll")] private static extern int QueryPerformanceCounter(out long value);
        public void Execute() { QueryPerformanceCounter(out long value); Values[Index] = value; }

        internal static JobHandle Stamp(NativeArray<long> values, int index, JobHandle dependency)
            => values.IsCreated ? new MovementTimestampJob { Values = values, Index = index }.Schedule(dependency) : dependency;
    }
}
