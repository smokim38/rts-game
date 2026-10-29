using System;
using Northwatch.Simulation.Spatial;
using Northwatch.Simulation.FlowFields;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    internal sealed class MovementPipeline : IDisposable
    {
        internal readonly MovementParameters Settings;
        internal SpatialGrid Grid;
        internal NativeArray<MovementPose> Start, Integrated, Corrected;
        internal NativeArray<float2> Velocities;
        private NativeArray<MovementUnit> planned;
        internal NativeArray<MovementUnit> Planned => planned;
        internal NativeArray<byte> LevelsA, LevelsB;
        internal NativeArray<byte> Levels => Settings.MoveCheckPasses % 2 == 0 ? LevelsA : LevelsB;
        private NativeArray<byte> emptyMap;
        private NativeArray<byte> walkable;
        private NativeArray<ushort> emptyDistances;

        // A caller may lend the static walkability map; field storage is outside this core.
        internal MovementPipeline(int capacity, MovementParameters settings, NativeArray<byte> staticWalkable = default)
        {
            settings.Validate();
            if (staticWalkable.IsCreated && staticWalkable.Length != StaticClamp.MapWidth * StaticClamp.MapWidth)
                throw new ArgumentException("Static walkability must contain 256 by 256 cells.");
            Settings = settings;
            Grid = new SpatialGrid(capacity);
            Start = new NativeArray<MovementPose>(capacity, Allocator.Persistent);
            Integrated = new NativeArray<MovementPose>(capacity, Allocator.Persistent);
            Corrected = new NativeArray<MovementPose>(capacity, Allocator.Persistent);
            Velocities = new NativeArray<float2>(capacity, Allocator.Persistent);
            planned = new NativeArray<MovementUnit>(capacity, Allocator.Persistent);
            LevelsA = new NativeArray<byte>(capacity, Allocator.Persistent);
            LevelsB = new NativeArray<byte>(capacity, Allocator.Persistent);
            emptyMap = new NativeArray<byte>(0, Allocator.Persistent);
            emptyDistances = new NativeArray<ushort>(0, Allocator.Persistent);
            walkable = staticWalkable.IsCreated ? staticWalkable : emptyMap;
        }

        internal JobHandle Schedule(UnitState state, JobHandle dependency, int batchSize = 64,
            NativeArray<long> timestamps = default, int timestampOffset = 0, ulong tick = 0)
        {
            var handle = MovementTimestampJob.Stamp(timestamps, timestampOffset, dependency);
            handle = new BucketUnitsJob { Positions = state.Positions, Alive = state.Alive, Buckets = Grid.Buckets }
                .ScheduleParallel(state.Alive.Length, batchSize, handle);
            handle = new BuildSpatialGridJob { Positions = state.Positions, Units = state.Movement, Alive = state.Alive, Grid = Grid }.Schedule(handle);
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 1, handle);
            handle = new PreferredVelocityJob
            {
                Positions = state.Positions, Sizes = state.Sizes, Alive = state.Alive, Units = state.Movement,
                Planned = planned, Grid = Grid,
                Settings = Settings, Tick = tick, Fields = new FlowFieldReader { Directions = emptyMap.AsReadOnly(),
                    Classes = emptyMap.AsReadOnly(), Distances = emptyDistances.AsReadOnly() }
            }.ScheduleParallel(state.Alive.Length, batchSize, handle);
            handle = new IntegrateMovementJob
            {
                Positions = state.Positions, Sizes = state.Sizes, Alive = state.Alive, Walkable = walkable,
                Units = planned, Start = Start, Integrated = Integrated, Velocities = Velocities,
                Levels = LevelsA, Settings = Settings
            }.ScheduleParallel(state.Alive.Length, batchSize, handle);
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 2, handle);
            handle = new OverlapMovementJob
            {
                Start = Start, Integrated = Integrated, Units = planned, Sizes = state.Sizes, Sides = state.Sides,
                Alive = state.Alive, Walkable = walkable, Grid = Grid, Corrected = Corrected, Settings = Settings
            }.ScheduleParallel(state.Alive.Length, batchSize, handle);
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 3, handle);
            var check = new MoveCheckData
            {
                Start = Start, Poses = Corrected, Sizes = state.Sizes, Alive = state.Alive,
                Walkable = walkable, Grid = Grid, StepBound = Settings.StepBound
            };
            for (int pass = 0; pass < Settings.MoveCheckPasses; pass++)
            {
                check.Levels = pass % 2 == 0 ? LevelsA : LevelsB;
                handle = new MoveCheckPassJob { Data = check, NextLevels = pass % 2 == 0 ? LevelsB : LevelsA }
                    .ScheduleParallel(state.Alive.Length, batchSize, handle);
            }
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 4, handle);
            handle = new CommitMovementJob
            {
                Start = Start, Poses = Corrected, Velocities = Velocities, Alive = state.Alive,
                Levels = Levels, Positions = state.Positions, Units = state.Movement, Planned = planned
            }.ScheduleParallel(state.Alive.Length, batchSize, handle);
            return MovementTimestampJob.Stamp(timestamps, timestampOffset + 5, handle);
        }

        public void Dispose()
        {
            Grid.Dispose(); Start.Dispose(); Integrated.Dispose(); Corrected.Dispose(); Velocities.Dispose();
            LevelsA.Dispose(); LevelsB.Dispose(); emptyMap.Dispose(); emptyDistances.Dispose(); planned.Dispose();
        }
    }
}
