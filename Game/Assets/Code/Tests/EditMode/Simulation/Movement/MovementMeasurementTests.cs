using System;
using System.IO;
using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using Northwatch.Simulation.FlowFields;
using Northwatch.Simulation.Spatial;
using NUnit.Framework;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

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

        [TestCase(0, 12, 6, 0)]
        [TestCase(0, 12, -5, 5)]
        [TestCase(170, -170, 180, 0)]
        [TestCase(0, 0, 180, 0)]
        [TestCase(0, 0, 90, 90)]
        public void SlideUsesTheWholeHeadingArcAndItsReverse(float start, float end, float direction, float expected)
        {
            float radians = math.radians(direction);
            Assert.That(MovementMeasurement.SlidingAngle(new float2(math.cos(radians), math.sin(radians)),
                math.radians(start), math.radians(end)), Is.EqualTo(expected).Within(.00003f));
        }

        [Test]
        public void ShortLateralAccumulatesPerVehicleAndReverseIsSeparateFromSlide()
        {
            using var ownedState = new UnitState(2); UnitState state = ownedState;
            using var measurement = new MovementMeasurement(state, 4, 2);
            using var pipeline = new MovementPipeline(2, Settings(), measurement.Walkable);
            Place(state, 0, 0); Place(state, 1, new float2(10, 0));
            for (int tick = 0; tick < 3; tick++)
            {
                var unit = state.Movement[0]; unit.Previous.Position = state.Positions[0]; state.Movement[0] = unit;
                state.Positions[0] += new float3(0, 0, .004f); Record(state, pipeline, measurement);
            }
            Assert.That(measurement.SafetyValue(2, 6), Is.EqualTo(.012f).Within(.000001f));
            Assert.That(measurement.SafetyValue(2, 5), Is.Zero, "Short steps do not enter #220's angle maximum.");
            var reverse = state.Movement[1]; reverse.Previous.Position = state.Positions[1]; state.Movement[1] = reverse;
            state.Positions[1] -= new float3(.02f, 0, 0); Record(state, pipeline, measurement);
            Assert.That(measurement.Metric(3, 23), Is.EqualTo(1));
        }

        internal static StaticBox[] SandboxBoxes() => new[]
        {
            new StaticBox { Min = new int2(148, 88), MaxExclusive = new int2(149, 168) },
            new StaticBox { Min = new int2(178, 88), MaxExclusive = new int2(179, 127) },
            new StaticBox { Min = new int2(178, 130), MaxExclusive = new int2(179, 168) },
            new StaticBox { Min = new int2(203, 148), MaxExclusive = new int2(223, 149) },
            new StaticBox { Min = new int2(203, 167), MaxExclusive = new int2(223, 168) },
            new StaticBox { Min = new int2(203, 149), MaxExclusive = new int2(204, 167) },
            new StaticBox { Min = new int2(222, 149), MaxExclusive = new int2(223, 167) }
        };

        [TestCase(5)] [TestCase(6)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
        public void AllFifteenFixturesStartAtDiscClearSpacingAndRetainTheirSizesAndHeading(int scene)
        {
            using var boxes = new NativeArray<StaticBox>(SandboxBoxes(), Allocator.TempJob);
            for (int sizeSet = 0; sizeSet < 3; sizeSet++)
            {
                int count = scene == 6 ? 100 : scene == 8 ? 60 : 110;
                float heading = scene == 9 ? math.PI : scene == 10 ? math.PI / 2 : 0;
                using var simulation = new UnitSimulation(Settings(), boxes);
                simulation.MeasureMovement(scene, count, sizeSet, heading);
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                Assert.That(simulation.Count, Is.EqualTo(count));
                for (int i = 0; i < count; i++)
                {
                    Assert.That(simulation.State.Movement[i].Heading, Is.EqualTo(heading));
                    Assert.That(StaticClamp.IsClear(simulation.State.Positions[i].xz, Footprint.Radius(simulation.State.Sizes[i]), simulation.Measurement.Walkable), Is.True);
                    UnitSize size = sizeSet == 2 || sizeSet == 0 && scene != 6 && i >= count - 10 ? UnitSize.Large : UnitSize.Small;
                    Assert.That(simulation.State.Sizes[i], Is.EqualTo(size));
                    for (int j = 0; j < i; j++) Assert.That(Footprint.DiscsTooClose(simulation.State.Positions[i].xz,
                        size, simulation.State.Positions[j].xz, simulation.State.Sizes[j]), Is.False, $"scene={scene}; sizes={sizeSet}; pair={i}/{j}");
                }
                using var writer = new StringWriter(); simulation.Measurement.WriteFixtureSummary(writer);
                StringAssert.Contains("220_R+F", writer.ToString());
            }
        }

        [Test]
        public void ResponseSceneClearsSandboxObstaclesForBothCommandsAndFields()
        {
            using var boxes = new NativeArray<StaticBox>(SandboxBoxes(), Allocator.TempJob);
            using var simulation = new UnitSimulation(Settings(), boxes);
            simulation.MeasureMovement(4, 2);
            Assert.That(simulation.StaticMaps.Walkable[128 * 256 + 148], Is.EqualTo(1));
            Assert.That(simulation.Measurement.Walkable[128 * 256 + 148], Is.EqualTo(1));
        }

        // Run on the clean source commit for ADR 0019 row 14; these are reports,
        // not pass/fail interpretations of S2.1. Normal tests exercise the recorder above.
        [TestCase(0)] [TestCase(30)] [TestCase(45)] [TestCase(90)] [TestCase(135)] [TestCase(180)]
        [Explicit("ADR 0019 row 14 baseline: run after the Producer commits the measurement sources.")]
        public void ResponseFactsForTwoThousandVehicles(float degrees)
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.MeasureMovement(4, 2000, 1);
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            Assert.That(simulation.QueueSelection(SelectionRequest.Box(new Matrix4x4 { m00 = .001f, m12 = .001f, m33 = 1 }, new Rect(0, 0, 1, 1))), Is.True);
            simulation.ScheduleFrame(0); simulation.CompleteFrame();
            var goal = simulation.SelectedCentre + new Vector3(math.cos(math.radians(degrees)), 0, math.sin(math.radians(degrees))) * 30;
            Assert.That(simulation.QueueSideMove(Side.First, goal, simulation.TickCount + 1), Is.True);
            for (int tick = 0; tick < 180; tick++) { simulation.ScheduleFrame(1); simulation.CompleteFrame(); }
            Directory.CreateDirectory("Logs/259-response");
            string prefix = "Logs/259-response/direction-" + degrees;
            using (var writer = new StreamWriter(prefix + "-units.csv")) simulation.Measurement.WriteResponse(writer);
            using var summary = new StringWriter(); simulation.Measurement.WriteResponseSummary(summary);
            File.WriteAllText(prefix + "-summary.txt", summary.ToString()); TestContext.WriteLine(summary.ToString());
        }

        [Test]
        [Explicit("ADR 0019 row 14 compressed column baseline: run after the Producer commits the measurement sources.")]
        public void ResponseFactsForACompressedFortyFiveRankColumn()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.MeasureMovement(4, 46, 1);
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            for (int i = 0; i < 46; i++)
            {
                float3 position = new float3(-i * .71f, 0, 0);
                simulation.State.Positions[i] = position;
                simulation.State.Sides[i] = i == 0 ? Side.Second : Side.First;
                simulation.State.Movement[i] = new MovementUnit { Home = position.xz,
                    Mode = i == 0 ? MovementMode.Hold : MovementMode.Idle,
                    Previous = new MovementPose { Position = position } };
            }
            int compressionTicks = 0; bool compressed = false; float maxGap = 0;
            for (; compressionTicks < 1800; compressionTicks++)
            {
                if (compressionTicks % 150 == 0) Assert.That(simulation.QueueSideMove(Side.First, new Vector3(30, 0, 0), simulation.TickCount + 1), Is.True);
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                maxGap = float.NegativeInfinity; bool stopped = true;
                for (int i = 1; i < 46; i++)
                {
                    maxGap = math.max(maxGap, math.distance(simulation.State.Positions[i], simulation.State.Positions[i - 1]) - 2 * Footprint.Radius(UnitSize.Small));
                    stopped &= math.all(simulation.State.Positions[i] == simulation.State.Movement[i].Previous.Position);
                }
                if (stopped && maxGap < .001f) { compressed = true; break; }
            }
            Directory.CreateDirectory("Logs/259-response");
            using var writer = new StreamWriter("Logs/259-response/column.csv");
            writer.WriteLine(FormattableString.Invariant($"compressed={compressed}; compression_ticks={compressionTicks}; max_disc_gap={maxGap:R}; report_only=True"));
            if (compressed)
            {
                Assert.That(simulation.QueueRemove(new UnitHandle(0, simulation.State.Generations[0])), Is.True);
                Assert.That(simulation.QueueSideMove(Side.First, new Vector3(30, 0, 0), simulation.TickCount + 1), Is.True);
                for (int tick = 0; tick < 180; tick++) { simulation.ScheduleFrame(1); simulation.CompleteFrame(); }
                writer.WriteLine("rank,first_nonzero_step_tick");
                for (int i = 1; i < 46; i++) writer.WriteLine($"{i},{simulation.Measurement.FirstResponse(i).y}");
            }
            writer.Flush(); TestContext.WriteLine($"compressed={compressed}; compression_ticks={compressionTicks}; rank output: Logs/259-response/column.csv; report_only=True");
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
            simulation.State.Positions[0] = unit.Previous.Position + (moved ? new float3(math.cos(unit.Heading) * .1f, 0, math.sin(unit.Heading) * .1f) : float3.zero);
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
                if (moved && t > 0)
                {
                    float angle = math.abs(math.degrees(math.atan2(math.sin(pose.Heading - unit.Heading), math.cos(pose.Heading - unit.Heading))));
                    TestContext.WriteLine($"156_to_168_tick; t={t}; drawn_slide_degrees={angle:R}; gate_13_bar=1; report_only=True");
                }
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
            float worst = 0, minimum = float.PositiveInfinity; int overlaps = 0, insideDiscs = 0;
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
                        for (int j = i + 1; j < 100; j++)
                        {
                            Matrix4x4 other = simulation.DrawMatrices[j];
                            var otherPose = new MovementPose { Position = new float3(other.m03, 0, other.m23), Heading = math.atan2(other.m20, other.m00) };
                            float clearance = math.distance(pose.Position.xz, otherPose.Position.xz) - 2 * Footprint.Radius(UnitSize.Small);
                            minimum = math.min(minimum, clearance);
                            if (clearance < 0) insideDiscs++;
                            if (Footprint.Overlap(pose, UnitSize.Small, otherPose, UnitSize.Small) > 0) overlaps++;
                        }
                        if (t == 1) Assert.That(pose.Position, Is.EqualTo(simulation.State.Positions[i]));
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
            TestContext.WriteLine($"old_motor_direction_degrees={degrees}; maximum_drawn_slide_degrees={worst:R}; drawn_footprint_overlaps={overlaps}; pairs_inside_bare_discs={insideDiscs}; lowest_drawn_clearance={minimum:R}; gate_13_bar_degrees=1; report_only=True");
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
