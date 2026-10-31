using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Northwatch.Simulation.FlowFields;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct OrderProgressJob : IJob
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<byte> Alive;
        [ReadOnly] internal NativeArray<MovementUnit> Units;
        internal NativeParallelHashMap<int, ulong> LatestProgress;
        internal ulong Tick;
        internal FlowFieldReader Fields;
        [ReadOnly] internal NativeArray<int> UnitFields;
        internal bool HasFields;

        public void Execute()
        {
            // Only current holders count. Rebuilding also releases orders with no
            // members, so the persistent table never needs more than unit capacity.
            LatestProgress.Clear();
            for (int i = 0; i < Alive.Length; i++)
            {
                MovementUnit unit = Units[i];
                if (Alive[i] == 0 || !unit.HasMoveOrder) continue;
                int field = HasFields ? UnitFields[i] : -1;
                float distance = Fields.Sample(field, Positions[i].xz, out var sample) == FlowSampleStatus.Ready
                    ? sample.Distance : math.distance(unit.Goal, Positions[i].xz);
                ulong progress = HasFields && field != unit.ProgressField || distance <= unit.BestGoalDistance - .1f
                    ? Tick : unit.LastProgressTick;
                if (LatestProgress.TryGetValue(unit.OrderVersion, out ulong latest))
                    LatestProgress[unit.OrderVersion] = progress > latest ? progress : latest;
                else LatestProgress.Add(unit.OrderVersion, progress);
            }
        }
    }
}
