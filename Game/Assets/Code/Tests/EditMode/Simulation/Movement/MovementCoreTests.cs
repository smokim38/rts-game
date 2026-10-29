using System;
using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation.Movement
{
    public sealed class MovementCoreTests
    {
        private static MovementParameters Settings(int cap = 32)
        {
            var asset = ScriptableObject.CreateInstance<MovementSettings>();
            MovementParameters result = asset.Snapshot();
            UnityEngine.Object.DestroyImmediate(asset);
            result.MaxSerialFallbacks = cap;
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

        [TestCase(0, 0)]
        [TestCase(0, 32)]
        [TestCase(1, 0)]
        [TestCase(1, 32)]
        [TestCase(2, 0)]
        [TestCase(2, 32)]
        public void SqueezedCrowdWallAndConcaveCornerStayClearEveryTick(int scene, int cap)
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
            using var pipeline = new MovementPipeline(64, Settings(cap), map);
            AssertClear(state, map);
            for (int tick = 0; tick < 180; tick++)
            {
                Seek(state, scene == 0 ? new float2(-4, -4) : new float2(2, scene == 2 ? 2 : -3));
                pipeline.Schedule(state, default).Complete();
                AssertClear(state, map);
            }
        }

        [Test]
        public void StableCountingSortExcludesDeadSlotsAndOrdersEachBucketByIndex()
        {
            using var ownedState = new UnitState(12);
            UnitState state = ownedState;
            using var pipeline = new MovementPipeline(12, Settings());
            for (int i = 0; i < 12; i++) Place(state, i, new float2((i % 2) * 4, 0));
            state.Alive[4] = 0;
            pipeline.Schedule(state, default, 1).Complete();
            int total = 0;
            for (int b = 0; b < pipeline.Grid.Counts.Length; b++)
            {
                int previous = -1;
                for (int k = 0; k < pipeline.Grid.Counts[b]; k++)
                {
                    int unit = pipeline.Grid.Indices[pipeline.Grid.Starts[b] + k];
                    Assert.That(unit, Is.GreaterThan(previous));
                    Assert.That(unit, Is.Not.EqualTo(4));
                    previous = unit; total++;
                }
            }
            Assert.That(total, Is.EqualTo(11));
        }

        [Test]
        public void CappedFallbackRestoresWholeConnectedQueueButLeavesDistantUnitMoving()
        {
            using var ownedState = new UnitState(32);
            UnitState state = ownedState;
            using var pipeline = new MovementPipeline(32, Settings(0));
            for (int i = 0; i < 31; i++)
            {
                Place(state, i, new float2(i * .5f, 0));
                var unit = state.Movement[i];
                unit.NewVelocity = unit.Velocity = new float2(-4, 0); state.Movement[i] = unit;
            }
            MovementUnit front = state.Movement[0]; front.Mode = MovementMode.Hold; front.Velocity = 0; state.Movement[0] = front;
            Place(state, 31, new float2(80, 0));
            var distant = state.Movement[31]; distant.NewVelocity = new float2(4, 0); state.Movement[31] = distant;
            pipeline.Schedule(state, default).Complete();
            Assert.That(pipeline.Metrics[3], Is.GreaterThan(0));
            Assert.That(pipeline.Metrics[1], Is.Zero);
            for (int i = 0; i < 31; i++) Assert.That(state.Positions[i], Is.EqualTo(state.Movement[i].Previous.Position));
            Assert.That(state.Positions[31].x, Is.GreaterThan(80));
            using var map = new NativeArray<byte>(0, Allocator.Persistent);
            AssertClear(state, map);
        }

        [Test]
        public void SerialRoundsPropagateFallbacksWithoutExceedingCap()
        {
            using var ownedState = new UnitState(24);
            UnitState state = ownedState;
            using var pipeline = new MovementPipeline(24, Settings(3));
            for (int i = 0; i < 24; i++)
            {
                Place(state, i, new float2(i * .5f, 0));
                var unit = state.Movement[i]; unit.NewVelocity = unit.Velocity = new float2(4, 0); state.Movement[i] = unit;
            }
            var front = state.Movement[23]; front.Mode = MovementMode.Hold; front.Velocity = 0; state.Movement[23] = front;
            pipeline.Schedule(state, default).Complete();
            Assert.That(pipeline.Metrics[1], Is.InRange(1, 3));
            Assert.That(pipeline.Metrics[2], Is.GreaterThan(0));
            Assert.That(pipeline.Metrics[3], Is.GreaterThan(0));
            using var map = new NativeArray<byte>(0, Allocator.Persistent);
            AssertClear(state, map);
        }

        [Test]
        public void ResultsAreBitIdenticalWithZeroOneAndAllWorkersAndDifferentBatchSizes()
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
                    using var pipeline = new MovementPipeline(64, Settings(8));
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
