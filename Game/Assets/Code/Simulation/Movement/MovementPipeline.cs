using System;
using Northwatch.Simulation.Spatial;
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
        internal NativeArray<byte> Choices, Levels;
        internal NativeArray<int> Affected, Changed, Marks, Metrics;
        internal NativeArray<long> EmptyTimings;
        private NativeArray<byte> emptyMap;
        private NativeArray<byte> walkable;

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
            Choices = new NativeArray<byte>(capacity, Allocator.Persistent);
            Levels = new NativeArray<byte>(capacity, Allocator.Persistent);
            Affected = new NativeArray<int>(capacity, Allocator.Persistent);
            Changed = new NativeArray<int>(capacity, Allocator.Persistent);
            Marks = new NativeArray<int>(capacity, Allocator.Persistent);
            Metrics = new NativeArray<int>(4, Allocator.Persistent);
            EmptyTimings = new NativeArray<long>(0, Allocator.Persistent);
            emptyMap = new NativeArray<byte>(0, Allocator.Persistent);
            walkable = staticWalkable.IsCreated ? staticWalkable : emptyMap;
        }

        internal JobHandle Schedule(UnitState state, JobHandle dependency, int batchSize = 64,
            NativeArray<long> timestamps = default, int timestampOffset = 0)
        {
            var handle = MovementTimestampJob.Stamp(timestamps, timestampOffset, dependency);
            handle = new BucketUnitsJob { Positions = state.Positions, Alive = state.Alive, Buckets = Grid.Buckets }
                .ScheduleParallel(state.Alive.Length, batchSize, handle);
            handle = new BuildSpatialGridJob { Positions = state.Positions, Units = state.Movement, Alive = state.Alive, Grid = Grid }.Schedule(handle);
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 1, handle);
            handle = new IntegrateMovementJob
            {
                Positions = state.Positions, Sizes = state.Sizes, Alive = state.Alive, Walkable = walkable,
                Units = state.Movement, Start = Start, Integrated = Integrated, Velocities = Velocities, Settings = Settings
            }.ScheduleParallel(state.Alive.Length, batchSize, handle);
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 2, handle);
            handle = new OverlapMovementJob
            {
                Start = Start, Integrated = Integrated, Units = state.Movement, Sizes = state.Sizes, Sides = state.Sides,
                Alive = state.Alive, Walkable = walkable, Grid = Grid, Corrected = Corrected, Settings = Settings
            }.ScheduleParallel(state.Alive.Length, batchSize, handle);
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 3, handle);
            var check = new MoveCheckData
            {
                Start = Start, Poses = Corrected, Sizes = state.Sizes, Alive = state.Alive,
                Walkable = walkable, Grid = Grid, StepBound = Settings.StepBound
            };
            handle = new InitialMoveCheckJob { Data = check, Choices = Choices }.ScheduleParallel(state.Alive.Length, batchSize, handle);
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 4, handle);
            handle = new SerialMoveCheckJob
            {
                Data = check, Levels = Levels, Choices = Choices, Affected = Affected,
                Changed = Changed, Marks = Marks, Metrics = Metrics, MaxSerialFallbacks = Settings.MaxSerialFallbacks,
                Timings = timestamps.IsCreated ? timestamps : EmptyTimings, TimingOffset = timestampOffset
            }.Schedule(handle);
            handle = MovementTimestampJob.Stamp(timestamps, timestampOffset + 5, handle);
            handle = new CommitMovementJob
            {
                Start = Start, Poses = Corrected, Velocities = Velocities, Alive = state.Alive,
                Levels = Levels, Positions = state.Positions, Units = state.Movement
            }.ScheduleParallel(state.Alive.Length, batchSize, handle);
            return MovementTimestampJob.Stamp(timestamps, timestampOffset + 6, handle);
        }

        public void Dispose()
        {
            Grid.Dispose(); Start.Dispose(); Integrated.Dispose(); Corrected.Dispose(); Velocities.Dispose();
            Choices.Dispose(); Levels.Dispose(); Affected.Dispose(); Changed.Dispose(); Marks.Dispose(); Metrics.Dispose();
            emptyMap.Dispose(); EmptyTimings.Dispose();
        }
    }
}
