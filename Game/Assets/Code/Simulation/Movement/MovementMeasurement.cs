#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using Northwatch.Simulation.Spatial;
using Northwatch.Simulation.FlowFields;
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
        public const int MetricCount = 23;
        public const int FloatMetricCount = 5;
        public const float ClearanceFloor = .000025f;
        public const int TimestampStride = 8;
        internal NativeArray<byte> Walkable;
        internal NativeArray<long> Timestamps;
        private NativeArray<int> samples, stillTicks;
        private NativeArray<int2> violations;
        private NativeArray<float2> progress;
        private NativeArray<float3> initialPositions;
        private NativeArray<float> floatSamples;
        private NativeArray<float4> safety;
        private NativeArray<int2> drawnViolations;
        private NativeArray<float4> drawnSafety;
        private NativeArray<ulong> hashes;
        private NativeArray<int4> firstResponse;
        private NativeArray<int> orderVersions, orderAges;
        private NativeArray<float4> sightTransitions;
        private NativeArray<int2> sightState;
        private readonly int scene;
        private readonly UnitState state;
        private readonly float startHeading;
        public int BatchSize { get; }
        internal int RemainingTicks => (scene == 7 ? 1800 : SampleCapacity) - TickCount;
        public int TickCount { get; private set; }

        internal MovementMeasurement(UnitState state, int scene, int count, float startHeading = 0, int batchSize = 64)
        {
            if (scene < 0 || scene > 7 || count < 2 || count > state.Alive.Length)
                throw new ArgumentOutOfRangeException(nameof(scene));
            this.scene = scene;
            this.state = state;
            this.startHeading = startHeading;
            BatchSize = batchSize;
            Walkable = new NativeArray<byte>(256 * 256, Allocator.Persistent);
            Timestamps = new NativeArray<long>(SampleCapacity * TimestampStride, Allocator.Persistent);
            samples = new NativeArray<int>(SampleCapacity * MetricCount, Allocator.Persistent);
            stillTicks = new NativeArray<int>(state.Alive.Length, Allocator.Persistent);
            violations = new NativeArray<int2>(state.Alive.Length, Allocator.Persistent);
            progress = new NativeArray<float2>(SampleCapacity, Allocator.Persistent);
            initialPositions = new NativeArray<float3>(state.Alive.Length, Allocator.Persistent);
            floatSamples = new NativeArray<float>(SampleCapacity * FloatMetricCount, Allocator.Persistent);
            safety = new NativeArray<float4>(state.Alive.Length, Allocator.Persistent);
            drawnSafety = new NativeArray<float4>(state.Alive.Length, Allocator.Persistent);
            drawnViolations = new NativeArray<int2>(state.Alive.Length, Allocator.Persistent);
            hashes = new NativeArray<ulong>(SampleCapacity, Allocator.Persistent);
            firstResponse = new NativeArray<int4>(state.Alive.Length, Allocator.Persistent);
            orderVersions = new NativeArray<int>(state.Alive.Length, Allocator.Persistent);
            orderAges = new NativeArray<int>(state.Alive.Length, Allocator.Persistent);
            sightTransitions = new NativeArray<float4>(state.Alive.Length, Allocator.Persistent);
            sightState = new NativeArray<int2>(state.Alive.Length, Allocator.Persistent);
            new SetupJob { State = state, Map = Walkable, Scene = scene, Count = count }.Schedule().Complete();
        }

        internal JobHandle Input(UnitState state, JobHandle dependency)
        {
            if (TickCount == SampleCapacity) throw new InvalidOperationException("Movement sample capacity exhausted.");
            if (TickCount == 0)
                dependency = new InitialHeadingJob { State = state, Heading = startHeading }.Schedule(dependency);
            if (scene >= 4) return dependency; // Real commands supply the sandbox order.
            return new InputJob { Positions = state.Positions, Sizes = state.Sizes, Alive = state.Alive,
                Units = state.Movement, Scene = scene }.Schedule(state.Alive.Length, 64, dependency);
        }

        internal JobHandle Record(UnitState state, MovementPipeline pipeline, JobHandle dependency)
        {
            // Validation follows the final chain timestamp, so it is outside stage timings.
            if (pipeline.Fields != null)
                dependency = new SightTransitionJob { State = state, Fields = pipeline.Fields.Reader,
                    UnitFields = pipeline.Fields.UnitFields, SightState = sightState, Facts = sightTransitions,
                    Tick = TickCount + 1 }.Schedule(state.Alive.Length, 64, dependency);
            var handle = new ValidateJob { State = state, Map = Walkable, Grid = pipeline.Grid,
                StepBound = pipeline.Settings.StepBound, Violations = violations, Safety = safety,
                DrawnSafety = drawnSafety, DrawnViolations = drawnViolations }
                .Schedule(state.Alive.Length, 64, dependency);
            return new RecordJob { State = state, Levels = pipeline.Levels, Samples = samples, StillTicks = stillTicks,
                Violations = violations, Progress = progress, InitialPositions = initialPositions,
                FloatSamples = floatSamples, Safety = safety, DrawnSafety = drawnSafety, DrawnViolations = drawnViolations,
                Hashes = hashes, FirstResponse = firstResponse, OrderVersions = orderVersions, OrderAges = orderAges,
                Settings = pipeline.Settings, Scene = scene, Tick = TickCount++ }.Schedule(handle);
        }

        public long Timestamp(int tick, int boundary) => Timestamps[tick * TimestampStride + boundary];
        public int Metric(int tick, int metric) => samples[tick * MetricCount + metric];
        public float2 Progress(int tick) => progress[tick];
        // minimum tick clearance, minimum drawn clearance, deepest small/large blocked-cell graze,
        // maximum drawn angle against the tick's whole displacement (degrees).
        public float SafetyValue(int tick, int metric) => floatSamples[tick * FloatMetricCount + metric];
        public ulong StateHash(int tick) => hashes[tick];
        public int3 FirstResponse(int unit) => firstResponse[unit].xyz;

        public void WriteResponse(TextWriter writer)
        {
            writer.WriteLine("unit,order_version,first_pose_tick,first_step_tick,first_half_speed_tick,only_creep_by_third_tick");
            for (int i = 0; i < state.Alive.Length; i++)
                if (state.Alive[i] != 0)
                    writer.WriteLine($"{i},{orderVersions[i]},{firstResponse[i].x},{firstResponse[i].y},{firstResponse[i].z},{firstResponse[i].w == 1}");
        }

        public void CountPastSandboxWall(out int small, out int large)
        {
            small = large = 0;
            for (int i = 0; i < state.Alive.Length; i++)
            {
                if (state.Alive[i] == 0 || state.Positions[i].x <= 21 + Footprint.Radius(state.Sizes[i])) continue;
                if (state.Sizes[i] == UnitSize.Small) small++;
                else large++;
            }
        }

        public void WriteOrderState(TextWriter writer)
        {
            // Called only after the driver's final completion, outside the measured window.
            writer.WriteLine("unit,x,z,heading,goal_x,goal_z,velocity_x,velocity_z,has_order,arrival_rule,best_distance,last_progress_tick,size,last_sight_entry_tick,straight_minus_best_at_sight_entry,rule4_tick,rule4_while_moving_after_sight_switch");
            for (int i = 0; i < state.Alive.Length; i++)
            {
                if (state.Alive[i] == 0) continue;
                MovementUnit unit = state.Movement[i];
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8},{9},{10:R},{11},{12},{13:R},{14:R},{15:R},{16:R}",
                    i, state.Positions[i].x, state.Positions[i].z, unit.Heading, unit.Goal.x, unit.Goal.y,
                    unit.Velocity.x, unit.Velocity.y, unit.HasMoveOrder, unit.ArrivalRule, unit.BestGoalDistance, unit.LastProgressTick, state.Sizes[i],
                    sightTransitions[i].x, sightTransitions[i].y, sightTransitions[i].z, sightTransitions[i].w));
            }
        }

        public void Dispose()
        {
            Walkable.Dispose(); Timestamps.Dispose(); samples.Dispose(); stillTicks.Dispose(); violations.Dispose();
            progress.Dispose(); initialPositions.Dispose(); floatSamples.Dispose(); safety.Dispose();
            drawnSafety.Dispose(); drawnViolations.Dispose(); hashes.Dispose(); firstResponse.Dispose();
            orderVersions.Dispose(); orderAges.Dispose();
            sightTransitions.Dispose(); sightState.Dispose();
        }

        [BurstCompile]
        private struct SightTransitionJob : IJobParallelFor
        {
            [ReadOnly] internal UnitState State;
            [ReadOnly] internal NativeArray<int> UnitFields;
            internal FlowFieldReader Fields;
            internal NativeArray<int2> SightState;
            internal NativeArray<float4> Facts;
            internal int Tick;
            public void Execute(int i)
            {
                if (State.Alive[i] == 0) return;
                MovementUnit unit = State.Movement[i];
                int2 old = SightState[i]; float4 facts = Facts[i];
                if (old.y != unit.OrderVersion) { old = new int2(0, unit.OrderVersion); facts = 0; }
                bool inSight = Fields.Sample(UnitFields[i], State.Positions[i].xz, out var sample) == FlowSampleStatus.Ready && sample.InSight;
                float2 target = unit.HasOwnSpot ? unit.Spot : unit.Goal;
                if (inSight && (old.x & 1) == 0 && unit.HasMoveOrder)
                {
                    facts.x = Tick;
                    facts.y = math.distance(target, State.Positions[i].xz) - unit.BestGoalDistance;
                }
                if (unit.ArrivalRule == 4 && facts.z == 0)
                {
                    facts.z = Tick;
                    // A diagnostic candidate, not a new arrival rule: the previous tick
                    // advanced while straight distance still exceeded the saved field best.
                    bool moving = (old.x & 2) != 0;
                    facts.w = facts.x > 0 && facts.y > .1f && moving &&
                        math.distance(target, State.Positions[i].xz) > unit.BestGoalDistance ? 1 : 0;
                }
                SightState[i] = new int2((inSight ? 1 : 0) | (math.any(unit.Previous.Position != State.Positions[i]) ? 2 : 0), unit.OrderVersion);
                Facts[i] = facts;
            }
        }

        [BurstCompile]
        private struct InitialHeadingJob : IJob
        {
            internal UnitState State;
            internal float Heading;
            public void Execute()
            {
                for (int i = 0; i < State.Alive.Length; i++)
                {
                    if (State.Alive[i] == 0) continue;
                    MovementUnit unit = State.Movement[i];
                    unit.Heading = Heading; unit.Previous.Heading = Heading;
                    State.Movement[i] = unit;
                }
            }
        }

        [BurstCompile]
        private struct SetupJob : IJob
        {
            internal UnitState State;
            internal NativeArray<byte> Map;
            internal int Scene, Count;
            public void Execute()
            {
                for (int i = 0; i < Map.Length; i++) Map[i] = 1;
                if (Scene >= 4) return;
                if (Scene == 1 || Scene == 3)
                    for (int y = 0; y < 256; y++)
                        if (Scene == 3 || y < 126 || y > 128) Map[y * 256 + 128] = 0;
                State.Summary[0] = State.Summary[1] = Count;
                // ADR 0019 requires disc-clear entry in the synthetic scene too.
                var first = new BlockPlacement(Count / 2, UnitSize.Small, new float3(-80, 0, 0));
                var second = new BlockPlacement(Count - Count / 2, UnitSize.Large, new float3(80, 0, 0));
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
            internal NativeArray<float4> Safety, DrawnSafety;
            internal NativeArray<int2> DrawnViolations;
            public void Execute(int i)
            {
                int2 errors = 0;
                float4 facts = new float4(float.PositiveInfinity, 0, 0, 0);
                float4 drawn = new float4(float.PositiveInfinity, 0, 0, 0);
                int2 drawnErrors = 0;
                if (State.Alive[i] != 0)
                {
                    var pose = new MovementPose { Position = State.Positions[i], Heading = State.Movement[i].Heading };
                    float radius = Footprint.Radius(State.Sizes[i]);
                    if (!StaticClamp.IsClear(pose.Position.xz, radius, Map)) errors.y++;
                    // Seed an upper bound from a live pair, then query every pair that could
                    // improve it. The grid holds start poses, so include the other unit's step.
                    // This also finds the true minimum in sparse scenes, not just contacts.
                    float bound = float.PositiveInfinity;
                    for (int j = i + 1; j < State.Alive.Length; j++)
                        if (State.Alive[j] != 0)
                        {
                            bound = math.distance(pose.Position.xz, State.Positions[j].xz) - radius - Footprint.Radius(State.Sizes[j]);
                            break;
                        }
                    var query = new SpatialQuery(Grid, pose.Position.xz,
                        math.isfinite(bound) ? radius + Footprint.LargeRadius + math.max(0, bound) + StepBound : 0);
                    while (query.Next(out int other))
                    {
                        if (other <= i) continue;
                        var neighbour = new MovementPose { Position = State.Positions[other], Heading = State.Movement[other].Heading };
                        if (Footprint.Overlap(pose, State.Sizes[i], neighbour, State.Sizes[other]) > 0) errors.x++;
                        float clearance = math.distance(pose.Position.xz, neighbour.Position.xz) - radius - Footprint.Radius(State.Sizes[other]);
                        facts.x = math.min(facts.x, clearance);
                        if (clearance < ClearanceFloor) facts.y++;
                    }
                    // Sample against the whole displacement, never between tiny drawn steps.
                    for (int sample = 1; sample <= 4; sample++)
                    {
                        MovementUnit unit = State.Movement[i];
                        MovementPose frame = DrawnPose.Sample(unit.Previous, pose.Position, pose.Heading, sample * .25f);
                        float2 step = pose.Position.xz - unit.Previous.Position.xz;
                        if (math.any(step != 0))
                        {
                            float2 heading = new float2(math.cos(frame.Heading), math.sin(frame.Heading));
                            drawn.y = math.max(drawn.y, math.degrees(math.atan2(math.abs(heading.x * step.y - heading.y * step.x), math.dot(heading, step))));
                        }
                        float penetration = 0;
                        int2 centre = (int2)math.floor(frame.Position.xz + UnitSimulation.MapHalfWidth);
                        for (int y = centre.y - 1; y <= centre.y + 1; y++)
                            for (int x = centre.x - 1; x <= centre.x + 1; x++)
                                if ((uint)x < 256 && (uint)y < 256 && Map[y * 256 + x] == 0)
                                    penetration = math.max(penetration, StaticClamp.Penetration(frame.Position.xz, radius, new int2(x, y)));
                        if (State.Sizes[i] == UnitSize.Small) drawn.z = math.max(drawn.z, penetration);
                        else drawn.w = math.max(drawn.w, penetration);
                        // Endpoint bound plus two step lengths covers every interpolated pair.
                        var frames = new SpatialQuery(Grid, frame.Position.xz,
                            math.isfinite(bound) ? radius + Footprint.LargeRadius + math.max(0, bound) + 3 * StepBound : 0);
                        while (frames.Next(out int other))
                        {
                            if (other <= i) continue;
                            MovementUnit neighbour = State.Movement[other];
                            MovementPose otherFrame = DrawnPose.Sample(neighbour.Previous, State.Positions[other], neighbour.Heading, sample * .25f);
                            float clearance = math.distance(frame.Position.xz, otherFrame.Position.xz) - radius - Footprint.Radius(State.Sizes[other]);
                            drawn.x = math.min(drawn.x, clearance);
                            if (clearance < 0) drawnErrors.y++;
                            if (Footprint.Overlap(frame, State.Sizes[i], otherFrame, State.Sizes[other]) > 0) drawnErrors.x++;
                        }
                    }
                }
                Violations[i] = errors;
                Safety[i] = facts; DrawnSafety[i] = drawn; DrawnViolations[i] = drawnErrors;
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
            [ReadOnly] internal NativeArray<float4> Safety, DrawnSafety;
            [ReadOnly] internal NativeArray<int2> DrawnViolations;
            internal NativeArray<float> FloatSamples;
            internal NativeArray<ulong> Hashes;
            internal NativeArray<int4> FirstResponse;
            internal NativeArray<int> OrderVersions, OrderAges;
            internal MovementParameters Settings;
            internal int Tick, Scene;
            public void Execute()
            {
                int offset = Tick * MetricCount;
                int stopped = 0, overFiveSeconds = 0, longest = 0, alive = 0, levelOne = 0, levelTwo = 0, crossed = 0;
                float2 advance = 0;
                int2 sides = 0;
                int2 errors = 0;
                int moving = 0, preferred = 0, orders = 0, exact = 0, contact = 0, congestion = 0, restWithOrder = 0, underOneCell = 0;
                int groupStall = 0, poseChanges = 0;
                float minimum = float.PositiveInfinity, drawnMinimum = float.PositiveInfinity;
                float3 drawnMaxima = 0;
                int underFloor = 0, creepOnly = 0;
                int2 drawnErrors = 0;
                var hash = new PhysicalHash();
                for (int i = 0; i < State.Alive.Length; i++)
                {
                    hash.Add(State.Alive[i]); hash.Add(State.Generations[i]);
                    if (State.Alive[i] == 0) continue;
                    alive++;
                    MovementUnit unit = State.Movement[i];
                    hash.Add(State.Positions[i]); hash.Add((uint)State.Sizes[i]); hash.Add((uint)State.Sides[i]); hash.Add(unit);
                    minimum = math.min(minimum, Safety[i].x);
                    underFloor += (int)Safety[i].y;
                    drawnMinimum = math.min(drawnMinimum, DrawnSafety[i].x);
                    drawnMaxima = math.max(drawnMaxima, DrawnSafety[i].yzw);
                    drawnErrors += DrawnViolations[i];
                    if (unit.OrderVersion != OrderVersions[i])
                    {
                        OrderVersions[i] = unit.OrderVersion; OrderAges[i] = 0; FirstResponse[i] = 0;
                    }
                    if (unit.OrderVersion != 0)
                    {
                        int age = OrderAges[i] + 1; OrderAges[i] = age;
                        int4 response = FirstResponse[i];
                        float step = math.distance(State.Positions[i].xz, unit.Previous.Position.xz);
                        bool turned = unit.Heading != unit.Previous.Heading;
                        if (response.x == 0 && (step > 0 || turned)) response.x = age;
                        if (response.y == 0 && step > 0) response.y = age;
                        if (response.z == 0 && step >= Settings.Speed(State.Sizes[i]) * MovementParameters.TickDuration * .5f) response.z = age;
                        if (age <= 3 && (turned || response.z != 0)) response.w = -1;
                        if (age == 3 && response.w == 0 && response.y > 0) response.w = 1;
                        FirstResponse[i] = response;
                        if (response.w == 1) creepOnly++;
                    }
                    if (Tick == 0) InitialPositions[i] = unit.Previous.Position;
                    bool hasVelocity = math.any(unit.Velocity != 0);
                    if (hasVelocity) moving++;
                    if (math.any(unit.PreferredVelocity != 0)) preferred++;
                    if (unit.HasMoveOrder) { orders++; if (!hasVelocity) restWithOrder++; }
                    if (unit.ArrivalRule == 1) exact++;
                    if (unit.ArrivalRule == 2) contact++;
                    if (unit.ArrivalRule == 3) congestion++;
                    if (unit.ArrivalRule == 4) groupStall++;
                    if (math.distance(State.Positions[i], unit.Previous.Position) > 1e-5f ||
                        math.abs(unit.Heading - unit.Previous.Heading) > 1e-5f) poseChanges++;
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
                Samples[offset + 17] = groupStall; Samples[offset + 18] = poseChanges;
                Samples[offset + 19] = underFloor; Samples[offset + 20] = drawnErrors.x;
                Samples[offset + 21] = drawnErrors.y; Samples[offset + 22] = creepOnly;
                int floatOffset = Tick * FloatMetricCount;
                FloatSamples[floatOffset] = minimum; FloatSamples[floatOffset + 1] = drawnMinimum;
                FloatSamples[floatOffset + 2] = drawnMaxima.y; FloatSamples[floatOffset + 3] = drawnMaxima.z;
                FloatSamples[floatOffset + 4] = drawnMaxima.x;
                Hashes[Tick] = hash.Value;
                Progress[Tick] = advance / math.max(sides, 1);
            }
        }

        // Hash named fields, never native struct padding. Float bits and unit order are retained.
        private struct PhysicalHash
        {
            internal ulong Value;
            internal void Add(uint value) { Value = unchecked((Value ^ value) * 1099511628211UL); }
            internal void Add(float value) => Add(math.asuint(value));
            internal void Add(float2 value) { Add(value.x); Add(value.y); }
            internal void Add(float3 value) { Add(value.x); Add(value.y); Add(value.z); }
            internal void Add(MovementUnit unit)
            {
                Add(unit.Previous.Position); Add(unit.Previous.Heading); Add(unit.Heading);
                Add(unit.Velocity); Add(unit.PreferredVelocity); Add(unit.NewVelocity); Add(unit.Home);
                Add(unit.Goal); Add(unit.Spot); Add(unit.SpotOffset); Add(unit.HasOwnSpot ? 1u : 0u);
                Add(unit.HasMoveOrder ? 1u : 0u); Add((uint)unit.OrderVersion); Add(unit.GoalRegion);
                Add(unit.GoalResolved ? 1u : 0u); Add((uint)unit.ProgressField); Add(unit.BestGoalDistance);
                Add((uint)unit.LastProgressTick); Add((uint)(unit.LastProgressTick >> 32));
                Add(unit.ArrivalRule); Add((uint)unit.Mode); Add(unit.StalledTime); Add(unit.HomeBlockedTime); Add(unit.ProgressTime);
            }
        }
    }
}
#endif
