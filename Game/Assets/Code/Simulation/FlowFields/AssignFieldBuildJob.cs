using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation.FlowFields
{
    [BurstCompile]
    internal struct AssignFieldBuildJob : IJob
    {
        internal int Slot;
        [ReadOnly] internal NativeArray<FlowFieldPool.Build> Builds;
        internal NativeArray<FlowFieldPool.Build> Assignment;
        public void Execute() => Assignment[0] = Builds[Slot];
    }
}
