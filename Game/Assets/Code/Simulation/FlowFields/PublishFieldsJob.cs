using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation.FlowFields
{
    [BurstCompile]
    internal struct PublishFieldsJob : IJob
    {
        internal int Slot, Cells;
        internal ulong Tick;
        internal NativeArray<FlowFieldPool.Build> Builds;
        internal NativeArray<FlowFieldPool.Entry> Entries;
        internal NativeArray<byte> Directions, Classes;
        internal NativeArray<ushort> Distances;
        internal NativeArray<int> Goals;
        [ReadOnly] internal NativeArray<byte> BuiltDirections;
        [ReadOnly] internal NativeArray<ushort> BuiltDistances;
        [ReadOnly] internal NativeArray<long> BuildTimestamps;
        internal NativeArray<long> Metrics;
        internal long DueTime;
        public void Execute()
        {
            FlowFieldPool.Build build = Builds[Slot];
            if (build.Field < 0) return;
            int metric = Slot * FlowFieldPool.MetricStride;
            Movement.MovementTimestampJob.Write(Metrics, metric + 2);
            Metrics[metric] = BuildTimestamps[0]; Metrics[metric + 1] = BuildTimestamps[1];
            Metrics[metric + 7] = BuildTimestamps[2];
            Metrics[metric + 4]++;
            if (BuildTimestamps[1] > DueTime) Metrics[metric + 5]++;
            if (Entries[build.Field].Valid != 0) Metrics[metric + 6]++;
            NativeArray<byte>.Copy(BuiltDirections, 0, Directions, build.Field * Cells, Cells);
            NativeArray<ushort>.Copy(BuiltDistances, 0, Distances, build.Field * Cells, Cells);
            Classes[build.Field] = build.Class;
            Goals[build.Field] = build.Goal;
            Entries[build.Field] = new FlowFieldPool.Entry { Valid = 1, Goal = build.Goal, Class = build.Class,
                LastUsed = Tick, Published = Tick };
            Builds[Slot] = new FlowFieldPool.Build { Field = -1 };
            Movement.MovementTimestampJob.Write(Metrics, metric + 3);
        }
    }
}
