using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

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

        public void Execute()
        {
            // Only current holders count. Rebuilding also releases orders with no
            // members, so the persistent table never needs more than unit capacity.
            LatestProgress.Clear();
            for (int i = 0; i < Alive.Length; i++)
            {
                MovementUnit unit = Units[i];
                if (Alive[i] == 0 || !unit.HasMoveOrder) continue;
                ulong progress = math.distance(unit.Goal, Positions[i].xz) <= unit.BestGoalDistance - .1f
                    ? Tick : unit.LastProgressTick;
                if (LatestProgress.TryGetValue(unit.OrderVersion, out ulong latest))
                    LatestProgress[unit.OrderVersion] = progress > latest ? progress : latest;
                else LatestProgress.Add(unit.OrderVersion, progress);
            }
        }
    }
}
