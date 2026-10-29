using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation.Movement
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [BurstCompile]
    internal struct MovementTimestampJob : IJob
    {
        internal NativeArray<long> Values;
        internal int Index;
        [DllImport("kernel32.dll")] private static extern int QueryPerformanceCounter(out long value);
        public void Execute() => Write(Values, Index);

        internal static void Write(NativeArray<long> values, int index)
        {
            if (!values.IsCreated || values.Length == 0) return;
            QueryPerformanceCounter(out long value); values[index] = value;
        }

        internal static JobHandle Stamp(NativeArray<long> values, int index, JobHandle dependency)
            => values.IsCreated ? new MovementTimestampJob { Values = values, Index = index }.Schedule(dependency) : dependency;
    }
#else
    internal static class MovementTimestampJob
    {
        internal static void Write(NativeArray<long> values, int index) { }
        internal static JobHandle Stamp(NativeArray<long> values, int index, JobHandle dependency) => dependency;
    }
#endif
}
