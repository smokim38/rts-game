using System;
using Northwatch.Simulation.Movement;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.FlowFields
{
    internal sealed class FlowFieldPool : IDisposable
    {
        internal struct Entry
        {
            internal int Goal, Users;
            internal byte Class, Valid, Replacing;
            internal ulong LastUsed, Published;
        }
        internal struct Request
        {
            internal int Order, Goal, Users, Field;
            internal byte Class, NeedsField;
            internal ulong Queued;
        }
        internal struct Build
        {
            internal int Field, Goal;
            internal byte Class;
        }
        private sealed class Scratch : IDisposable
        {
            internal NativeArray<int> Costs, Heap, Positions;
            internal NativeArray<byte> Directions;
            internal NativeArray<ushort> Distances;
            internal NativeArray<Build> Assignment;
            internal NativeArray<long> Timestamps;
            internal NativeArray<int4> SightIntervalsA, SightIntervalsB;
            internal JobHandle Handle;
            internal ulong Due;
            internal Scratch(int cells, int extent)
            {
                Costs = new NativeArray<int>(cells, Allocator.Persistent);
                Heap = new NativeArray<int>(cells, Allocator.Persistent);
                Positions = new NativeArray<int>(cells, Allocator.Persistent);
                Directions = new NativeArray<byte>(cells, Allocator.Persistent);
                Distances = new NativeArray<ushort>(cells, Allocator.Persistent);
                Assignment = new NativeArray<Build>(1, Allocator.Persistent);
                Timestamps = new NativeArray<long>(3, Allocator.Persistent);
                SightIntervalsA = new NativeArray<int4>(extent + 2, Allocator.Persistent);
                SightIntervalsB = new NativeArray<int4>(extent + 2, Allocator.Persistent);
            }
            public void Dispose()
            {
                Handle.Complete(); Costs.Dispose(); Heap.Dispose(); Positions.Dispose();
                Directions.Dispose(); Distances.Dispose(); Assignment.Dispose(); Timestamps.Dispose();
                SightIntervalsA.Dispose(); SightIntervalsB.Dispose();
            }
        }

        internal readonly int Width, Height;
        internal readonly float2 Origin;
        internal readonly NativeArray<byte> Directions, Classes;
        internal readonly NativeArray<ushort> Distances;
        internal readonly NativeArray<int> UnitFields, Goals;
        internal readonly NativeArray<Entry> Entries;
        internal readonly NativeArray<Request> Requests;
        internal readonly NativeArray<Build> Builds;
        // Per slot: build start/end, publish start/end, publishes, missed deadlines, replacements, sight start.
        internal const int MetricStride = 8;
        internal readonly NativeArray<long> Metrics;
        private NativeParallelHashMap<long, int> orders;
        private readonly Scratch[] scratch;
        private readonly MovementParameters settings;
        internal long PoolBytes => 3L * Width * Height * Entries.Length;
        internal long ScratchBytes => (15L * Width * Height + 32L * (math.max(Width, Height) + 2) + 24 + 12) * scratch.Length;
        internal FlowFieldReader Reader => new FlowFieldReader { Width = Width, Height = Height, Origin = Origin,
            Directions = Directions.AsReadOnly(), Distances = Distances.AsReadOnly(), Classes = Classes.AsReadOnly(), Goals = Goals.AsReadOnly() };

        internal FlowFieldPool(int width, int height, float2 origin, int units, MovementParameters parameters)
        {
            Width = width; Height = height; Origin = origin; settings = parameters;
            int cells = checked(width * height), capacity = parameters.FieldCapacity;
            Directions = new NativeArray<byte>(checked(cells * capacity), Allocator.Persistent);
            Distances = new NativeArray<ushort>(cells * capacity, Allocator.Persistent);
            Classes = new NativeArray<byte>(capacity, Allocator.Persistent);
            Goals = new NativeArray<int>(capacity, Allocator.Persistent);
            Entries = new NativeArray<Entry>(capacity, Allocator.Persistent);
            Requests = new NativeArray<Request>(units, Allocator.Persistent);
            UnitFields = new NativeArray<int>(units, Allocator.Persistent);
            orders = new NativeParallelHashMap<long, int>(units, Allocator.Persistent);
            Builds = new NativeArray<Build>(parameters.BuildSlots, Allocator.Persistent);
            Metrics = new NativeArray<long>(parameters.BuildSlots * MetricStride, Allocator.Persistent);
            scratch = new Scratch[parameters.BuildSlots];
            for (int i = 0; i < scratch.Length; i++)
            {
                scratch[i] = new Scratch(cells, math.max(width, height)); Builds[i] = new Build { Field = -1 };
            }
        }

        internal JobHandle Publish(ulong tick, JobHandle dependency)
        {
            for (int i = 0; i < scratch.Length; i++)
            {
                Scratch s = scratch[i];
                if (s.Due == 0 || s.Due > tick) continue;
                dependency = new PublishFieldsJob { Slot = i, Cells = Width * Height, Tick = tick,
                    Builds = Builds, Entries = Entries, Directions = Directions, Distances = Distances, Classes = Classes, Goals = Goals,
                    BuiltDirections = s.Directions, BuiltDistances = s.Distances, BuildTimestamps = s.Timestamps,
                    Metrics = Metrics, DueTime = MovementTimestampJob.Now() }
                    .Schedule(JobHandle.CombineDependencies(dependency, s.Handle));
                s.Due = 0;
            }
            return dependency;
        }

        internal JobHandle RequestFields(UnitState state, NativeArray<byte> passable, ulong tick, JobHandle dependency)
        {
            int available = 0;
            for (int i = 0; i < scratch.Length; i++) if (scratch[i].Due == 0) available |= 1 << i;
            return new RequestFieldsJob { Positions = state.Positions, Alive = state.Alive, Sizes = state.Sizes,
                Units = state.Movement, Passable = passable, Width = Width, Height = Height, Origin = Origin,
                Tick = tick, Settings = settings, Entries = Entries, Requests = Requests, Orders = orders,
                UnitFields = UnitFields, Builds = Builds, Available = available }.Schedule(dependency);
        }

        internal JobHandle ScheduleBuilds(NativeArray<byte> passable, ulong tick, JobHandle dependency)
        {
            JobHandle assignments = dependency;
            for (int i = 0; i < scratch.Length; i++)
            {
                Scratch s = scratch[i];
                if (s.Due != 0) continue;
                var assigned = new AssignFieldBuildJob { Slot = i, Builds = Builds, Assignment = s.Assignment }.Schedule(dependency);
                assignments = JobHandle.CombineDependencies(assignments, assigned);
                s.Handle = new BuildFlowFieldJob { Width = Width, Height = Height,
                    Passable = passable, Builds = s.Assignment, Costs = s.Costs, Heap = s.Heap, HeapPositions = s.Positions,
                    Directions = s.Directions, Distances = s.Distances, Timestamps = s.Timestamps,
                    SightIntervalsA = s.SightIntervalsA, SightIntervalsB = s.SightIntervalsB }.Schedule(assigned);
                s.Due = tick + (ulong)settings.BuildLatencyTicks;
            }
            JobHandle.ScheduleBatchedJobs();
            return assignments;
        }

        public void Dispose()
        {
            foreach (Scratch s in scratch) s.Dispose();
            Directions.Dispose(); Distances.Dispose(); Classes.Dispose(); Entries.Dispose(); Requests.Dispose();
            Goals.Dispose();
            UnitFields.Dispose(); Builds.Dispose(); orders.Dispose(); Metrics.Dispose();
        }
    }
}
