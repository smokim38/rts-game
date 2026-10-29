#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using Northwatch.Simulation.Spatial;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    // Instrument the real driver's chain. Expose samples, never unit arrays (ADR 0005).
    public sealed class MovementMeasurement : IDisposable
    {
        public const int SampleCapacity = 8192;
        public const int MetricCount = 17;
        public const int TimestampStride = 6;
        internal NativeArray<byte> Walkable;
        internal NativeArray<long> Timestamps;
        private NativeArray<int> samples, stillTicks;
        private NativeArray<int2> violations;
        private NativeArray<float2> progress;
        private NativeArray<float3> initialPositions;
        private readonly int scene;
        private readonly UnitState state;
        public int TickCount { get; private set; }

        internal MovementMeasurement(UnitState state, int scene, int count)
        {
            if (scene < 0 || scene > 4 || count < 2 || count > state.Alive.Length)
                throw new ArgumentOutOfRangeException(nameof(scene));
            this.scene = scene;
            this.state = state;
            Walkable = new NativeArray<byte>(256 * 256, Allocator.Persistent);
            Timestamps = new NativeArray<long>(SampleCapacity * TimestampStride, Allocator.Persistent);
            samples = new NativeArray<int>(SampleCapacity * MetricCount, Allocator.Persistent);
            stillTicks = new NativeArray<int>(state.Alive.Length, Allocator.Persistent);
            violations = new NativeArray<int2>(state.Alive.Length, Allocator.Persistent);
            progress = new NativeArray<float2>(SampleCapacity, Allocator.Persistent);
            initialPositions = new NativeArray<float3>(state.Alive.Length, Allocator.Persistent);
            new SetupJob { State = state, Map = Walkable, Scene = scene, Count = count }.Schedule().Complete();
        }

        internal JobHandle Input(UnitState state, JobHandle dependency)
        {
            if (TickCount == SampleCapacity) throw new InvalidOperationException("Movement sample capacity exhausted.");
            if (scene == 4) return dependency; // Real commands supply the open-map order.
            return new InputJob { Positions = state.Positions, Sizes = state.Sizes, Alive = state.Alive,
                Units = state.Movement, Scene = scene }.Schedule(state.Alive.Length, 64, dependency);
        }

        internal JobHandle Record(UnitState state, MovementPipeline pipeline, JobHandle dependency)
        {
            // Validation follows the final chain timestamp, so it is outside stage timings.
            var handle = new ValidateJob { State = state, Map = Walkable, Grid = pipeline.Grid,
                StepBound = pipeline.Settings.StepBound, Violations = violations }
                .Schedule(state.Alive.Length, 64, dependency);
            return new RecordJob { State = state, Levels = pipeline.Levels, Samples = samples, StillTicks = stillTicks,
                Violations = violations, Progress = progress, InitialPositions = initialPositions,
                Scene = scene, Tick = TickCount++ }.Schedule(handle);
        }

        public long Timestamp(int tick, int boundary) => Timestamps[tick * TimestampStride + boundary];
        public int Metric(int tick, int metric) => samples[tick * MetricCount + metric];
        public float2 Progress(int tick) => progress[tick];

        public void WriteOrderState(TextWriter writer)
        {
            // Called only after the driver's final completion, outside the measured window.
            writer.WriteLine("unit,x,z,heading,goal_x,goal_z,velocity_x,velocity_z,has_order,arrival_rule,best_distance,last_progress_tick");
            for (int i = 0; i < state.Alive.Length; i++)
            {
                if (state.Alive[i] == 0) continue;
                MovementUnit unit = state.Movement[i];
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8},{9},{10:R},{11}",
                    i, state.Positions[i].x, state.Positions[i].z, unit.Heading, unit.Goal.x, unit.Goal.y,
                    unit.Velocity.x, unit.Velocity.y, unit.HasMoveOrder, unit.ArrivalRule, unit.BestGoalDistance, unit.LastProgressTick));
            }
        }

        public void Dispose() { Walkable.Dispose(); Timestamps.Dispose(); samples.Dispose(); stillTicks.Dispose(); violations.Dispose(); progress.Dispose(); initialPositions.Dispose(); }

        [BurstCompile]
        private struct SetupJob : IJob
        {
            internal UnitState State;
            internal NativeArray<byte> Map;
            internal int Scene, Count;
            public void Execute()
            {
                for (int i = 0; i < Map.Length; i++) Map[i] = 1;
                if (Scene == 4) return;
                if (Scene == 1 || Scene == 3)
                    for (int y = 0; y < 256; y++)
                        if (Scene == 3 || y < 126 || y > 128) Map[y * 256 + 128] = 0;
                State.Summary[0] = State.Summary[1] = Count;
                // Preserve the ADR 0012 synthetic scene for comparison with #153.
                var first = new BlockPlacement(Count / 2, UnitSize.Small, new float3(-80, 0, 0), 1);
                var second = new BlockPlacement(Count - Count / 2, UnitSize.Large, new float3(80, 0, 0), 1);
                for (int i = 0; i < Count; i++)
                {
                    bool firstSide = i < Count / 2;
                    float3 position = Scene == 0 ? (firstSide ? first.Position(i) : second.Position(i - Count / 2)) :
                        new float3(Scene == 2 ? (i % 50 - 25) * .72f : -.72f - (i % 50) * .72f, 0, (i / 50 - 20) * .72f);
                    State.Positions[i] = position;
                    State.Alive[i] = 1; State.Generations[i] = 1;
                    State.Sides[i] = firstSide ? Side.First : Side.Second;
                    State.Sizes[i] = Scene == 0 && !firstSide ? UnitSize.Large : UnitSize.Small;
                    State.Movement[i] = new MovementUnit { Mode = MovementMode.Moving, Home = position.xz,
                        Previous = new MovementPose { Position = position } };
                }
            }
        }

        [BurstCompile]
        private struct InputJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<float3> Positions;
            [ReadOnly] internal NativeArray<UnitSize> Sizes;
            [ReadOnly] internal NativeArray<byte> Alive;
            internal NativeArray<MovementUnit> Units;
            internal int Scene;
            public void Execute(int i)
            {
                if (Alive[i] == 0) return;
                MovementUnit unit = Units[i];
                float2 goal = Scene == 0 ? new float2(unit.Home.x < 0 ? 80 : -80, unit.Home.y) :
                    Scene == 2 ? float2.zero : new float2(20, Scene == 1 ? -.5f : Positions[i].z);
                unit.PreferredVelocity = math.normalizesafe(goal - Positions[i].xz) * (Sizes[i] == UnitSize.Small ? 4 : 2.5f);
                unit.NewVelocity = unit.PreferredVelocity;
                Units[i] = unit;
            }
        }

        [BurstCompile]
        private struct ValidateJob : IJobParallelFor
        {
            [ReadOnly] internal UnitState State;
            [ReadOnly] internal NativeArray<byte> Map;
            [ReadOnly] internal SpatialGrid Grid;
            internal float StepBound;
            internal NativeArray<int2> Violations;
            public void Execute(int i)
            {
                int2 errors = 0;
                if (State.Alive[i] != 0)
                {
                    var pose = new MovementPose { Position = State.Positions[i], Heading = State.Movement[i].Heading };
                    float radius = Footprint.Radius(State.Sizes[i]);
                    if (!StaticClamp.NoWorse(pose.Position.xz, State.Movement[i].Previous.Position.xz, radius, Map)) errors.y++;
                    // The grid contains start-of-tick poses. The other unit may have
                    // moved by StepBound since then; do not miss a pair across a bucket.
                    var query = new SpatialQuery(Grid, pose.Position.xz, radius + Footprint.LargeRadius + StepBound);
                    while (query.Next(out int other))
                    {
                        if (other <= i) continue;
                        var neighbour = new MovementPose { Position = State.Positions[other], Heading = State.Movement[other].Heading };
                        if (Footprint.Overlap(pose, State.Sizes[i], neighbour, State.Sizes[other]) > 0) errors.x++;
                    }
                }
                Violations[i] = errors;
            }
        }

        [BurstCompile]
        private struct RecordJob : IJob
        {
            [ReadOnly] internal UnitState State;
            [ReadOnly] internal NativeArray<byte> Levels;
            [ReadOnly] internal NativeArray<int2> Violations;
            internal NativeArray<int> Samples, StillTicks;
            internal NativeArray<float2> Progress;
            internal NativeArray<float3> InitialPositions;
            internal int Tick, Scene;
            public void Execute()
            {
                int offset = Tick * MetricCount;
                int stopped = 0, overFiveSeconds = 0, longest = 0, alive = 0, levelOne = 0, levelTwo = 0, crossed = 0;
                float2 advance = 0;
                int2 sides = 0;
                int2 errors = 0;
                int moving = 0, preferred = 0, orders = 0, exact = 0, contact = 0, congestion = 0, restWithOrder = 0, underOneCell = 0;
                for (int i = 0; i < State.Alive.Length; i++)
                {
                    if (State.Alive[i] == 0) continue;
                    alive++;
                    MovementUnit unit = State.Movement[i];
                    if (Tick == 0) InitialPositions[i] = unit.Previous.Position;
                    bool hasVelocity = math.any(unit.Velocity != 0);
                    if (hasVelocity) moving++;
                    if (math.any(unit.PreferredVelocity != 0)) preferred++;
                    if (unit.HasMoveOrder) { orders++; if (!hasVelocity) restWithOrder++; }
                    if (unit.ArrivalRule == 1) exact++;
                    if (unit.ArrivalRule == 2) contact++;
                    if (unit.ArrivalRule == 3) congestion++;
                    if (Scene == 4 && math.distance(InitialPositions[i].xz, unit.Goal) -
                        math.distance(State.Positions[i].xz, unit.Goal) < 1) underOneCell++;
                    if (Levels[i] == 1) levelOne++;
                    if (Levels[i] == 2) levelTwo++;
                    bool first = State.Sides[i] == Side.First;
                    int side = first ? 0 : 1;
                    advance[side] += (State.Positions[i].x - State.Movement[i].Home.x) * (first ? 1 : -1);
                    sides[side]++;
                    if (Scene == 1 && State.Positions[i].x - Footprint.Radius(State.Sizes[i]) >= 1) crossed++;
                    errors += Violations[i];
                    bool still = math.distancesq(State.Positions[i], State.Movement[i].Previous.Position) < 1e-12f;
                    StillTicks[i] = still ? StillTicks[i] + 1 : 0;
                    if (still) stopped++;
                    if (StillTicks[i] > 150) overFiveSeconds++;
                    longest = math.max(longest, StillTicks[i]);
                }
                Samples[offset] = levelOne; Samples[offset + 1] = levelTwo;
                Samples[offset + 2] = stopped; Samples[offset + 3] = overFiveSeconds;
                Samples[offset + 4] = longest; Samples[offset + 5] = alive;
                Samples[offset + 6] = errors.x; Samples[offset + 7] = errors.y;
                Samples[offset + 8] = crossed;
                Samples[offset + 9] = moving; Samples[offset + 10] = preferred; Samples[offset + 11] = orders;
                Samples[offset + 12] = exact; Samples[offset + 13] = contact; Samples[offset + 14] = congestion;
                Samples[offset + 15] = restWithOrder; Samples[offset + 16] = underOneCell;
                Progress[Tick] = advance / math.max(sides, 1);
            }
        }
    }
}
#endif
