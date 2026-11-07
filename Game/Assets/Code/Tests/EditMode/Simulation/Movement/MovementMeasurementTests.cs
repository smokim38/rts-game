using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using Northwatch.Simulation.Spatial;
using NUnit.Framework;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation.Movement
{
    public sealed class MovementMeasurementTests
    {
        private static MovementParameters Settings()
        {
            var asset = ScriptableObject.CreateInstance<MovementSettings>();
            var result = asset.Snapshot(); Object.DestroyImmediate(asset); return result;
        }

        private static void Place(UnitState state, int slot, float2 position)
        {
            state.Alive[slot] = 1;
            state.Positions[slot] = new float3(position.x, 0, position.y);
            state.Movement[slot] = new MovementUnit { Previous = new MovementPose { Position = state.Positions[slot] } };
        }

        private static void Record(UnitState state, MovementPipeline pipeline, MovementMeasurement measurement)
        {
            var handle = new BucketUnitsJob { Positions = state.Positions, Alive = state.Alive, Buckets = pipeline.Grid.Buckets }
                .ScheduleParallel(state.Alive.Length, 1, default);
            handle = new BuildSpatialGridJob { Positions = state.Positions, Units = state.Movement, Alive = state.Alive, Grid = pipeline.Grid }.Schedule(handle);
            measurement.Record(state, pipeline, handle).Complete();
        }

        [TestCase(.00001f, 1)]
        [TestCase(.0001f, 0)]
        [TestCase(50f, 0)]
        public void ClearanceIncludesPairsOutsideTheContactQuery(float gap, int underFloor)
        {
            using var ownedState = new UnitState(2);
            UnitState state = ownedState;
            using var measurement = new MovementMeasurement(state, 4, 2);
            using var pipeline = new MovementPipeline(2, Settings(), measurement.Walkable);
            Place(state, 0, 0); Place(state, 1, new float2(2 * Footprint.Radius(UnitSize.Small) + gap, 0));
            Record(state, pipeline, measurement);
            Assert.That(measurement.Metric(0, 19), Is.EqualTo(underFloor));
            Assert.That(measurement.SafetyValue(0, 0), Is.EqualTo(gap).Within(.000005f));
            Assert.That(measurement.SafetyValue(0, 1), Is.EqualTo(gap).Within(.000005f));
        }

        [Test]
        public void DrawnGrazeIsMeasuredBetweenClearTickEndpoints()
        {
            using var ownedState = new UnitState(2);
            UnitState state = ownedState;
            using var measurement = new MovementMeasurement(state, 4, 2);
            using var pipeline = new MovementPipeline(2, Settings(), measurement.Walkable);
            float radius = Footprint.Radius(UnitSize.Small);
            Place(state, 0, new float2(-radius, 0)); Place(state, 1, new float2(-10, 0));
            state.Positions[0] = new float3(0, 0, -radius);
            measurement.Walkable[128 * 256 + 128] = 0;
            Record(state, pipeline, measurement);
            Assert.That(measurement.Metric(0, 7), Is.Zero);
            Assert.That(measurement.SafetyValue(0, 2), Is.EqualTo(radius * (1 - math.sqrt(.5f))).Within(.000001f));
            Assert.That(measurement.SafetyValue(0, 3), Is.Zero);
        }

        [Test]
        public void StateHashPreservesFloatBitsAndIncludesOrderAndProgressState()
        {
            using var ownedState = new UnitState(2);
            UnitState state = ownedState;
            using var measurement = new MovementMeasurement(state, 4, 2);
            using var pipeline = new MovementPipeline(2, Settings(), measurement.Walkable);
            Place(state, 0, 0); Place(state, 1, new float2(10, 0));
            Record(state, pipeline, measurement); Record(state, pipeline, measurement);
            Assert.That(measurement.StateHash(1), Is.EqualTo(measurement.StateHash(0)));
            var unit = state.Movement[0]; unit.SpotOffset = new float2(.1f, .2f); state.Movement[0] = unit;
            Record(state, pipeline, measurement);
            Assert.That(measurement.StateHash(2), Is.Not.EqualTo(measurement.StateHash(1)));
            unit.LastProgressTick = 1UL << 40; state.Movement[0] = unit;
            Record(state, pipeline, measurement);
            Assert.That(measurement.StateHash(3), Is.Not.EqualTo(measurement.StateHash(2)));
            state.Positions[0] = new float3(math.asfloat(0x80000000u), 0, 0);
            Record(state, pipeline, measurement);
            Assert.That(measurement.StateHash(4), Is.Not.EqualTo(measurement.StateHash(3)));
        }

        [Test]
        public void ResponseDistinguishesTurnsCreepAndHalfSpeedAndResetsForANewOrder()
        {
            using var ownedState = new UnitState(2);
            UnitState state = ownedState;
            using var measurement = new MovementMeasurement(state, 4, 2);
            var settings = Settings();
            using var pipeline = new MovementPipeline(2, settings, measurement.Walkable);
            Place(state, 0, 0); Place(state, 1, new float2(10, 0));
            var unit = state.Movement[0]; unit.OrderVersion = 1; unit.HasMoveOrder = true; unit.Heading = .1f;
            state.Movement[0] = unit; Record(state, pipeline, measurement);
            unit.Previous.Heading = .1f; state.Movement[0] = unit;
            state.Positions[0] = new float3(.001f, 0, 0); Record(state, pipeline, measurement);
            unit.Previous.Position = state.Positions[0]; state.Movement[0] = unit;
            state.Positions[0] += new float3(settings.SmallSpeed * MovementParameters.TickDuration, 0, 0);
            Record(state, pipeline, measurement);
            Assert.That(measurement.FirstResponse(0), Is.EqualTo(new int3(1, 2, 3)));
            unit.OrderVersion = 2; unit.Previous.Position = state.Positions[0]; state.Movement[0] = unit;
            Record(state, pipeline, measurement);
            Assert.That(measurement.FirstResponse(0), Is.EqualTo(int3.zero));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SharedDrawMatchesActualMatricesForStillAndMovingTicks(bool moved)
        {
            using var simulation = new UnitSimulation(Settings());
            Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero), Is.True);
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            var unit = simulation.State.Movement[0];
            unit.Previous = new MovementPose { Position = new float3(2, 0, 4), Heading = math.radians(156) };
            unit.Heading = math.radians(168); simulation.State.Movement[0] = unit;
            simulation.State.Positions[0] = unit.Previous.Position + (moved ? new float3(-.1f, 0, .02f) : float3.zero);
            foreach (float t in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                simulation.ScheduleFrame(0, t); simulation.CompleteFrame();
                var pose = DrawnPose.Sample(unit.Previous, simulation.State.Positions[0], unit.Heading, t);
                Matrix4x4 matrix = simulation.DrawMatrices[0];
                Assert.That(matrix.m03, Is.EqualTo(pose.Position.x));
                Assert.That(matrix.m23, Is.EqualTo(pose.Position.z));
                Assert.That(matrix.m00, Is.EqualTo(math.cos(pose.Heading) * .5f).Within(.000001f));
                Assert.That(matrix.m20, Is.EqualTo(math.sin(pose.Heading) * .5f).Within(.000001f));
                Assert.That(simulation.State.Movement[0], Is.EqualTo(unit));
            }
        }

        [TestCase(45)]
        [TestCase(180)]
        public void HundredVehicleDrawSamplesUseTheSharedPoseWithoutMutatingTheTick(float degrees)
        {
            using var simulation = new UnitSimulation(Settings());
            Assert.That(simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 100), Is.True);
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            Vector3 goal = new Vector3(math.cos(math.radians(degrees)), 0, math.sin(math.radians(degrees))) * 30;
            Assert.That(simulation.QueueSideMove(Side.First, goal, simulation.TickCount + 1), Is.True);
            float worst = 0;
            for (int tick = 0; tick < 20; tick++)
            {
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                foreach (float t in new[] { .25f, .5f, .75f, 1f })
                {
                    simulation.ScheduleFrame(0, t); simulation.CompleteFrame();
                    for (int i = 0; i < 100; i++)
                    {
                        MovementUnit unit = simulation.State.Movement[i];
                        var pose = DrawnPose.Sample(unit.Previous, simulation.State.Positions[i], unit.Heading, t);
                        Matrix4x4 matrix = simulation.DrawMatrices[i];
                        Assert.That(matrix.m03, Is.EqualTo(pose.Position.x));
                        Assert.That(matrix.m23, Is.EqualTo(pose.Position.z));
                        Assert.That(matrix.m00, Is.EqualTo(math.cos(pose.Heading) * .5f).Within(.000001f));
                        Assert.That(matrix.m20, Is.EqualTo(math.sin(pose.Heading) * .5f).Within(.000001f));
                        float2 step = simulation.State.Positions[i].xz - unit.Previous.Position.xz;
                        if (math.any(step != 0))
                        {
                            float2 heading = new float2(math.cos(pose.Heading), math.sin(pose.Heading));
                            worst = math.max(worst, math.degrees(math.atan2(math.abs(heading.x * step.y - heading.y * step.x), math.dot(heading, step))));
                        }
                        Assert.That(simulation.State.Movement[i], Is.EqualTo(unit));
                    }
                }
            }
            TestContext.WriteLine($"old_motor_direction_degrees={degrees}; maximum_drawn_slide_degrees={worst:R}; gate_13_bar_degrees=1; report_only=True");
        }

        [Test]
        public void MeasurementHashesMatchForTheThreeJobOrders()
        {
            int original = JobsUtility.JobWorkerCount;
            var expected = new ulong[8];
            try
            {
                for (int run = 0; run < 3; run++)
                {
                    JobsUtility.JobWorkerCount = run == 2 ? JobsUtility.JobWorkerMaximumCount : 1;
                    using var ownedState = new UnitState(16);
                    UnitState state = ownedState;
                    using var measurement = new MovementMeasurement(state, 0, 16);
                    using var pipeline = new MovementPipeline(16, Settings(), measurement.Walkable);
                    for (int tick = 0; tick < 8; tick++)
                    {
                        var handle = measurement.Input(state, default);
                        handle = pipeline.Schedule(state, handle, run == 1 ? 64 : 1);
                        measurement.Record(state, pipeline, handle).Complete();
                        if (run == 0) expected[tick] = measurement.StateHash(tick);
                        else Assert.That(measurement.StateHash(tick), Is.EqualTo(expected[tick]), $"run={run}; tick={tick}");
                    }
                }
            }
            finally { JobsUtility.JobWorkerCount = original; }
        }
    }
}
