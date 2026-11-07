using System;
using Northwatch.Simulation.Movement;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation
{
    internal sealed class LadderCrossing : IDisposable
    {
        private NativeArray<byte> met;
        private NativeArray<float3> previous;
        private NativeArray<float4> snapshots;
        private NativeArray<LadderCycleCounts> counts;
        private readonly int cycleTicks, sampleTicks;
        private readonly float distance;

        internal LadderCrossing(int cycleTicks, int cycles, int sampleTicks, float distance)
        {
            this.cycleTicks = cycleTicks; this.sampleTicks = sampleTicks; this.distance = distance;
            met = new NativeArray<byte>(UnitSimulation.Capacity, Allocator.Persistent);
            previous = new NativeArray<float3>(UnitSimulation.Capacity, Allocator.Persistent);
            snapshots = new NativeArray<float4>(UnitSimulation.Capacity, Allocator.Persistent);
            counts = new NativeArray<LadderCycleCounts>(cycles * 2, Allocator.Persistent);
        }

        internal LadderCycleCounts Count(int cycle, Side side) => counts[cycle * 2 + (int)side];

        internal JobHandle Begin(UnitState state, ulong tick, JobHandle dependency)
        {
            if (tick != 1) return dependency;
            return new LadderPassJob { State = state, Previous = previous, Snapshots = snapshots, Met = met, Counts = counts,
                Cycle = -1 }.Schedule(dependency);
        }

        internal JobHandle End(UnitState state, MovementPipeline movement, ulong tick, JobHandle dependency)
        {
            int cycle = (int)((tick - 1) / (ulong)cycleTicks);
            if (cycle * 2 >= counts.Length) return dependency;
            if (tick % (ulong)sampleTicks == 0)
                dependency = new LadderMeetJob { Positions = state.Positions, Sides = state.Sides,
                    Alive = state.Alive, Met = met, Grid = movement.Grid, Distance = distance,
                    StepBound = movement.Settings.StepBound }.ScheduleParallel(UnitSimulation.Capacity, 64, dependency);
            if (tick % (ulong)cycleTicks == 0)
                dependency = new LadderPassJob { State = state, Previous = previous, Snapshots = snapshots, Met = met, Counts = counts,
                    Cycle = cycle, Seconds = cycleTicks * MovementParameters.TickDuration,
                    Settings = movement.Settings }.Schedule(dependency);
            return dependency;
        }

        public void Dispose() { met.Dispose(); previous.Dispose(); snapshots.Dispose(); counts.Dispose(); }
    }
}
