using System;
using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using Northwatch.Simulation.Spatial;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation.Movement
{
    public sealed class MovementCoreTests
    {
        private static MovementParameters Settings(int passes = 1)
        {
            var asset = ScriptableObject.CreateInstance<MovementSettings>();
            MovementParameters result = asset.Snapshot();
            UnityEngine.Object.DestroyImmediate(asset);
            result.MoveCheckPasses = passes;
            return result;
        }

        private static void Place(UnitState state, int i, float2 position, UnitSize size = UnitSize.Small, float heading = 0)
        {
            state.Alive[i] = 1; state.Sizes[i] = size;
            state.Positions[i] = new float3(position.x, 0, position.y);
            state.Movement[i] = new MovementUnit
            {
                Mode = MovementMode.Moving, Home = position, Heading = heading,
                Previous = new MovementPose { Position = state.Positions[i], Heading = heading }
            };
        }

        private static void Seek(UnitState state, float2 goal)
        {
            for (int i = 0; i < state.Alive.Length; i++)
            {
                MovementUnit unit = state.Movement[i];
                unit.PreferredVelocity = math.normalizesafe(goal - state.Positions[i].xz) * (state.Sizes[i] == UnitSize.Small ? 4 : 2.5f);
                unit.NewVelocity = unit.PreferredVelocity;
                state.Movement[i] = unit;
            }
        }

        private static MovementPose Pose(UnitState state, int i) => new MovementPose
            { Position = state.Positions[i], Heading = state.Movement[i].Heading };

        private static void AssertClear(UnitState state, NativeArray<byte> map)
        {
            for (int i = 0; i < state.Alive.Length; i++)
            {
                Assert.That(StaticClamp.NoWorse(state.Positions[i].xz, state.Movement[i].Previous.Position.xz,
                    Footprint.Radius(state.Sizes[i]), map), Is.True, "static penetration at " + i);
                for (int j = i + 1; j < state.Alive.Length; j++)
                    Assert.That(Footprint.Overlap(Pose(state, i), state.Sizes[i], Pose(state, j), state.Sizes[j]),
                        Is.Zero, "footprints " + i + ", " + j);
            }
        }

        [TestCase(UnitSize.Small)]
        [TestCase(UnitSize.Large)]
        public void RotatedSquaresInsideTouchingDiscsDoNotOverlap(UnitSize size)
        {
            var a = new MovementPose { Heading = math.PI / 4 };
            var b = new MovementPose { Position = new float3(2 * Footprint.Radius(size), 0, 0), Heading = -math.PI / 4 };
            Assert.That(Footprint.Overlap(a, size, b, size), Is.Zero);
            b.Position.x -= .01f;
            Assert.That(Footprint.Overlap(a, size, b, size), Is.GreaterThan(0));
        }

        [TestCase(0, 1)]
        [TestCase(0, 2)]
        [TestCase(1, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        [TestCase(2, 2)]
        public void SqueezedCrowdWallAndConcaveCornerStayClearEveryTick(int scene, int passes)
        {
            using var ownedState = new UnitState(64);
            UnitState state = ownedState;
            using var ownedMap = new NativeArray<byte>(256 * 256, Allocator.Persistent);
            NativeArray<byte> map = ownedMap;
            for (int i = 0; i < map.Length; i++) map[i] = 1;
            if (scene > 0)
                for (int y = 0; y < 256; y++) map[y * 256 + 128] = 0;
            if (scene == 2)
                for (int x = 0; x < 128; x++) map[128 * 256 + x] = 0;
            for (int i = 0; i < state.Alive.Length; i++)
                Place(state, i, new float2(-.8f - (i % 8) * 1.05f, -.8f - (i / 8) * 1.05f),
                    i % 7 == 0 ? UnitSize.Large : UnitSize.Small);
            using var pipeline = new MovementPipeline(64, Settings(passes), map);
            AssertClear(state, map);
            for (int tick = 0; tick < 180; tick++)
            {
                Seek(state, scene == 0 ? new float2(-4, -4) : new float2(2, scene == 2 ? 2 : -3));
                pipeline.Schedule(state, default).Complete();
                AssertClear(state, map);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void FollowerWaitsForMovingLeaderUntilSecondPass(int passes)
        {
            using var state = new UnitState(2);
            using var pipeline = new MovementPipeline(2, Settings(passes));
            Place(state, 0, 0);
            Place(state, 1, new float2(.5f, 0));
            for (int i = 0; i < 2; i++)
            {
                pipeline.Start[i] = Pose(state, i);
                MovementPose next = pipeline.Start[i]; next.Position.x += .125f;
                pipeline.Corrected[i] = next;
                pipeline.Velocities[i] = new float2(3.75f, 0);
            }
            CheckCandidates(state, pipeline);
            Assert.That(pipeline.Levels[0], Is.EqualTo(passes == 1 ? 2 : 0));
            Assert.That(state.Positions[0].x, Is.EqualTo(passes == 1 ? 0 : .125f));
            Assert.That(state.Movement[0].Velocity, Is.EqualTo(passes == 1 ? float2.zero : new float2(3.75f, 0)));
            Assert.That(state.Positions[1].x, Is.EqualTo(.625f));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void PipelineFollowerWaitsForMovingLeaderUntilSecondPass(int passes)
        {
            using var ownedState = new UnitState(2);
            UnitState state = ownedState;
            using var pipeline = new MovementPipeline(2, Settings(passes));
            using var map = new NativeArray<byte>(0, Allocator.Persistent);
            Place(state, 0, 0);
            Place(state, 1, new float2(.5f, 0));
            for (int i = 0; i < 2; i++)
            {
                MovementUnit unit = state.Movement[i];
                unit.Velocity = unit.NewVelocity = new float2(3.75f, 0);
                state.Movement[i] = unit;
            }

            pipeline.Schedule(state, default).Complete();

            Assert.That(pipeline.Levels[1], Is.Zero);
            Assert.That(state.Positions[1].x, Is.GreaterThan(.5f));
            Assert.That(pipeline.Corrected[0].Position.x, Is.GreaterThan(0));
            Assert.That(pipeline.Levels[0], Is.EqualTo(passes == 1 ? 2 : 0));
            Assert.That(state.Positions[0], Is.EqualTo(passes == 1 ? pipeline.Start[0].Position : pipeline.Corrected[0].Position));
            Assert.That(state.Movement[0].Velocity, Is.EqualTo(passes == 1 ? float2.zero : new float2(3.75f, 0)));
            AssertClear(state, map);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ConflictOnlyWithNeighbourOldHeadingClearsOnSecondPass(int passes)
        {
            using var state = new UnitState(2);
            using var pipeline = new MovementPipeline(2, Settings(passes));
            Place(state, 0, new float2(-.2f, 0));
            Place(state, 1, new float2(.7f, 0), heading: math.PI / 4);
            pipeline.Start[0] = Pose(state, 0); pipeline.Start[1] = Pose(state, 1);
            var candidate = pipeline.Start[0]; candidate.Position.x = 0;
            var neighbour = pipeline.Start[1]; neighbour.Position.x = .55f; neighbour.Heading = 0;
            var oldHeading = neighbour; oldHeading.Heading = pipeline.Start[1].Heading;
            pipeline.Corrected[0] = candidate; pipeline.Corrected[1] = neighbour;
            Assert.That(Footprint.Overlap(candidate, UnitSize.Small, neighbour, UnitSize.Small), Is.Zero);
            Assert.That(Footprint.Overlap(candidate, UnitSize.Small, pipeline.Start[1], UnitSize.Small), Is.Zero);
            Assert.That(Footprint.Overlap(candidate, UnitSize.Small, oldHeading, UnitSize.Small), Is.GreaterThan(0));
            CheckCandidates(state, pipeline);
            Assert.That(pipeline.Levels[1], Is.Zero);
            Assert.That(pipeline.Levels[0], Is.EqualTo(passes == 1 ? 2 : 0));
            Assert.That(state.Positions[0].x, Is.EqualTo(passes == 1 ? -.2f : 0));
            Assert.That(pipeline.Corrected[0], Is.EqualTo(candidate));
            Assert.That(pipeline.Corrected[1], Is.EqualTo(neighbour));
        }

        private static void CheckCandidates(UnitState state, MovementPipeline pipeline)
        {
            using var map = new NativeArray<byte>(0, Allocator.Persistent);
            var planned = pipeline.Planned;
            for (int i = 0; i < state.Alive.Length; i++)
            { pipeline.LevelsA[i] = 2; planned[i] = state.Movement[i]; }
            var handle = new BucketUnitsJob { Positions = state.Positions, Alive = state.Alive, Buckets = pipeline.Grid.Buckets }
                .ScheduleParallel(state.Alive.Length, 1, default);
            handle = new BuildSpatialGridJob { Positions = state.Positions, Units = state.Movement, Alive = state.Alive, Grid = pipeline.Grid }.Schedule(handle);
            var data = new MoveCheckData { Start = pipeline.Start, Poses = pipeline.Corrected, Sizes = state.Sizes,
                Alive = state.Alive, Walkable = map, Grid = pipeline.Grid, StepBound = pipeline.Settings.StepBound };
            for (int pass = 0; pass < pipeline.Settings.MoveCheckPasses; pass++)
            {
                data.Levels = pass % 2 == 0 ? pipeline.LevelsA : pipeline.LevelsB;
                handle = new MoveCheckPassJob { Data = data, NextLevels = pass % 2 == 0 ? pipeline.LevelsB : pipeline.LevelsA }
                    .ScheduleParallel(state.Alive.Length, 1, handle);
            }
            new CommitMovementJob { Start = pipeline.Start, Poses = pipeline.Corrected, Velocities = pipeline.Velocities,
                Alive = state.Alive, Levels = pipeline.Levels, Positions = state.Positions, Units = state.Movement, Planned = pipeline.Planned }
                .ScheduleParallel(state.Alive.Length, 1, handle).Complete();
            AssertClear(state, map);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void LevelOneCommitsCorrectedPositionWithStartHeading(int passes)
        {
            using var state = new UnitState(2);
            using var pipeline = new MovementPipeline(2, Settings(passes));
            Place(state, 0, 0);
            Place(state, 1, new float2(.7f, 0));
            pipeline.Start[0] = Pose(state, 0); pipeline.Start[1] = Pose(state, 1);
            pipeline.Corrected[0] = new MovementPose { Position = new float3(.125f, 0, 0), Heading = math.PI / 4 };
            pipeline.Corrected[1] = pipeline.Start[1];
            pipeline.Velocities[0] = new float2(3.75f, 0);
            CheckCandidates(state, pipeline);
            Assert.That(pipeline.Levels[0], Is.EqualTo(1));
            Assert.That(state.Positions[0].x, Is.EqualTo(.125f));
            Assert.That(state.Movement[0].Heading, Is.Zero);
            Assert.That(state.Movement[0].Velocity, Is.EqualTo(new float2(3.75f, 0)));
        }

        [TestCase(0)]
        [TestCase(5)]
        public void InvalidPassCountCannotStartMatch(int passes)
        {
            Assert.Throws<ArgumentException>(() => new UnitSimulation(Settings(passes)));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ResultsAreBitIdenticalWithZeroOneAndAllWorkersAndDifferentBatchSizes(int passes)
        {
            int original = JobsUtility.JobWorkerCount;
            float3[] expected = null;
            MovementUnit[] expectedUnits = null;
            try
            {
                foreach (int workers in new[] { 0, 1, original })
                {
                    JobsUtility.JobWorkerCount = workers;
                    using var ownedState = new UnitState(64);
                    UnitState state = ownedState;
                    using var pipeline = new MovementPipeline(64, Settings(passes));
                    for (int i = 0; i < 64; i++) Place(state, i, new float2((i % 8) * .51f, (i / 8) * .51f));
                    for (int tick = 0; tick < 90; tick++)
                    {
                        Seek(state, new float2(2, 2));
                        pipeline.Schedule(state, default, workers == 1 ? 1 : 17).Complete();
                    }
                    if (expected == null) { expected = state.Positions.ToArray(); expectedUnits = state.Movement.ToArray(); }
                    else
                        for (int i = 0; i < 64; i++)
                        {
                            Assert.That(math.asuint(state.Positions[i]), Is.EqualTo(math.asuint(expected[i])));
                            Assert.That(state.Movement[i], Is.EqualTo(expectedUnits[i]));
                        }
                }
            }
            finally { JobsUtility.JobWorkerCount = original; }
        }

        [Test]
        public void DrawInterpolatesWrappedHeadingAndPositionWithoutChangingSimulation()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            var unit = simulation.State.Movement[0];
            unit.Previous = new MovementPose { Position = new float3(2, 0, 4), Heading = math.radians(179) };
            unit.Heading = math.radians(-179); simulation.State.Movement[0] = unit;
            simulation.State.Positions[0] = new float3(4, 0, 8);
            simulation.ScheduleFrame(0, .5f); simulation.CompleteFrame();
            Matrix4x4 drawn = simulation.DrawMatrices[0];
            Assert.That(drawn.m03, Is.EqualTo(3)); Assert.That(drawn.m23, Is.EqualTo(6));
            Assert.That(drawn.m00, Is.EqualTo(-.5f).Within(1e-6));
            Assert.That(simulation.State.Movement[0], Is.EqualTo(unit));
            Assert.That(simulation.State.Positions[0], Is.EqualTo(new float3(4, 0, 8)));
        }

        [Test]
        public void InvalidSpeedDecelerationAndSpacingCannotStartMovement()
        {
            var settings = Settings(); settings.SmallSpeed = 7.51f;
            Assert.Throws<ArgumentException>(() => new UnitSimulation(settings));
            settings = Settings(); settings.Deceleration = 7.99f;
            Assert.Throws<ArgumentException>(() => new UnitSimulation(settings));
            settings = Settings(); settings.SlotSpacing = 1.31f;
            Assert.Throws<ArgumentException>(() => new UnitSimulation(settings));
        }

        [Test]
        public void MeasurementDetectsFootprintOverlapsAndStaticRegressions()
        {
            using var ownedState = new UnitState(2);
            UnitState state = ownedState;
            using var measurement = new MovementMeasurement(state, 2, 2);
            using var pipeline = new MovementPipeline(2, Settings(), measurement.Walkable);
            Place(state, 0, new float2(-2, -2));
            Place(state, 1, new float2(-3, -2));
            state.Positions[0] = new float3(.1f, 0, .1f);
            state.Positions[1] = new float3(.2f, 0, .1f);
            measurement.Walkable[128 * 256 + 128] = 0;
            var handle = new BucketUnitsJob { Positions = state.Positions, Alive = state.Alive, Buckets = pipeline.Grid.Buckets }
                .ScheduleParallel(2, 1, default);
            handle = new BuildSpatialGridJob { Positions = state.Positions, Units = state.Movement, Alive = state.Alive, Grid = pipeline.Grid }.Schedule(handle);
            measurement.Record(state, pipeline, handle).Complete();
            Assert.That(measurement.Metric(0, 6), Is.EqualTo(1));
            Assert.That(measurement.Metric(0, 7), Is.EqualTo(2));
        }

        [Test]
        public void FullTwoThousandUnitMovingChainAllocatesNoManagedBytesAfterWarmup()
        {
            using var ownedState = new UnitState(2000);
            UnitState state = ownedState;
            using var pipeline = new MovementPipeline(2000, Settings());
            for (int i = 0; i < 2000; i++) Place(state, i, new float2((i % 50) * .8f - 20, (i / 50) * .8f - 16));
            for (int tick = 0; tick < 8; tick++) { Seek(state, 0); pipeline.Schedule(state, default).Complete(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 60; tick++) { Seek(state, 0); pipeline.Schedule(state, default).Complete(); }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }
    }
}
