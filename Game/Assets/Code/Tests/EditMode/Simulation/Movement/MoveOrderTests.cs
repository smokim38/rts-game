using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation.Movement
{
    public sealed class MoveOrderTests
    {
        private static MovementParameters Settings()
        {
            var asset = ScriptableObject.CreateInstance<MovementSettings>();
            var settings = asset.Snapshot(); Object.DestroyImmediate(asset); return settings;
        }
        private static void Frame(UnitSimulation simulation, int ticks = 0, float seconds = 0)
        { simulation.ScheduleFrame(ticks, 1, seconds); simulation.CompleteFrame(); }
        private static void SelectAll(UnitSimulation simulation)
        {
            simulation.QueueSelection(SelectionRequest.Box(new Matrix4x4 { m00 = .001f, m12 = .001f, m33 = 1 },
                new Rect(0, 0, 1, 1)));
            Frame(simulation);
        }
        private static void Order(UnitSimulation simulation, Vector3 point, bool classic = true)
        {
            simulation.QueueSelection(SelectionRequest.PointerClick(new Ray(point + Vector3.up * 10, Vector3.down),
                Matrix4x4.identity, classic, true, false, false, false));
            Frame(simulation);
        }

        private static void Spawn(UnitSimulation simulation, bool lattice, bool touching = false)
        {
            if (lattice || touching)
                for (int i = 0; i < 100; i++)
                    Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small,
                        new Vector3((i % 10 - 4.5f) * (touching ? .5f : .625f), 0,
                            (i / 10 - 4.5f) * (touching ? .5f : .625f))), Is.True);
            else Assert.That(simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 100), Is.True);
            Frame(simulation, 1);
        }

        [TestCase(true, true, false)] [TestCase(false, true, false)]
        [TestCase(true, false, false)] [TestCase(false, false, false)]
        [TestCase(true, false, true)] [TestCase(false, false, true)]
        public void ClickQueuesSnapshotAndNextTickPlansEverySelectedUnit(bool classic, bool lattice, bool touching)
        {
            using var simulation = new UnitSimulation(Settings());
            Spawn(simulation, lattice, touching);
            SelectAll(simulation); Order(simulation, new Vector3(20, 0, 0), classic);
            Assert.That(simulation.AcknowledgementCount, Is.EqualTo(100));
            Assert.That(simulation.MoveMarkerVersion, Is.EqualTo(1));
            for (int i = 0; i < 100; i++) Assert.That(simulation.State.Movement[i].HasMoveOrder, Is.False);
            simulation.QueueSelection(SelectionRequest.Clear()); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.Zero);
            Frame(simulation, 1);
            int started = 0;
            for (int i = 0; i < 100; i++)
            {
                Assert.That(simulation.State.Movement[i].HasMoveOrder, Is.True);
                Assert.That(math.lengthsq(simulation.State.Movement[i].PreferredVelocity), Is.GreaterThan(0));
                if (math.lengthsq(simulation.State.Movement[i].Velocity) > 0) started++;
            }
            TestContext.WriteLine("First tick moving=" + started + "/100; lattice=" + lattice + "; touching=" + touching + "; classic=" + classic);
            if (!touching) Assert.That(started, Is.EqualTo(100), "Units with velocity on the first normally spaced tick");
            Frame(simulation, 0, 1);
            Assert.That(simulation.AcknowledgementCount, Is.Zero);
        }

        [Test]
        public void DirectSeekReachesExactGoalWithoutOvershootAndMarkerStays()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 1); Frame(simulation, 1);
            SelectAll(simulation); var goal = new Vector3(10, 0, 3); Order(simulation, goal);
            for (int t = 0; t < 180; t++) Frame(simulation, 1);
            Assert.That(math.distance(simulation.State.Positions[0], (float3)goal), Is.LessThan(1e-5));
            Assert.That(math.lengthsq(simulation.State.Movement[0].Velocity), Is.LessThan(1e-10));
            for (int t = 0; t < 300; t++) Frame(simulation, 1);
            Assert.That(simulation.MoveMarkerVersion, Is.EqualTo(1));
            Assert.That(simulation.MoveMarkerPosition, Is.EqualTo(goal));
        }

        [Test]
        public void NewestOrderReplacesMarkerAndPendingTargetWithoutQueueOverflow()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 2000); Frame(simulation, 1);
            SelectAll(simulation);
            for (int i = 0; i < 20; i++) Order(simulation, new Vector3(60 + i, 0, 0));
            Assert.That(simulation.MoveMarkerVersion, Is.EqualTo(20));
            Assert.That(simulation.MoveMarkerPosition.x, Is.EqualTo(79));
            Frame(simulation, 2);
            for (int i = 0; i < 2000; i++) Assert.That(simulation.State.Movement[i].Goal.x, Is.EqualTo(79));
        }

        [Test]
        public void EmptySelectionDoesNotMakeAnOrderOrAcknowledgement()
        {
            using var simulation = new UnitSimulation(Settings());
            Order(simulation, new Vector3(10, 0, 0)); Frame(simulation, 1);
            Assert.That(simulation.MoveMarkerVersion, Is.Zero);
            Assert.That(simulation.AcknowledgementCount, Is.Zero);
        }

        [TestCase(false, false, 15f, 0f)] [TestCase(true, false, 15f, 0f)] [TestCase(false, true, 15f, 0f)]
        [TestCase(false, false, 14.9999542f, -.0000152587891f)]
        public void HundredUnitsComeToRestWithoutAnyTickAddingFootprintOverlap(bool lattice, bool touching, float goalX, float goalZ)
        {
            using var simulation = new UnitSimulation(Settings());
            Spawn(simulation, lattice, touching);
            var goal = new Vector3(goalX, 0, goalZ);
            var initialDistance = new float[100]; var moved = new bool[100];
            for (int i = 0; i < 100; i++) initialDistance[i] = math.distance(simulation.State.Positions[i], (float3)goal);
            SelectAll(simulation); Order(simulation, goal);
            int lastChange = 0, allMovedTick = 0, firstTickMoving = 0;
            for (int tick = 0; tick < 1200; tick++)
            {
                Frame(simulation, 1);
                int movedCount = 0;
                for (int i = 0; i < 100; i++)
                {
                    var unit = simulation.State.Movement[i];
                    float displacement = math.distance(simulation.State.Positions[i], unit.Previous.Position);
                    moved[i] |= displacement > 1e-5f;
                    if (moved[i]) movedCount++;
                    if (tick == 0 && math.any(unit.Velocity != 0)) firstTickMoving++;
                    if (displacement > 1e-5f || math.abs(unit.Heading - unit.Previous.Heading) > 1e-5f ||
                        math.any(unit.Velocity != 0)) lastChange = tick + 1;
                    for (int j = i + 1; j < 100; j++)
                        if (Footprint.Overlap(new MovementPose { Position = simulation.State.Positions[i], Heading = simulation.State.Movement[i].Heading },
                            UnitSize.Small, new MovementPose { Position = simulation.State.Positions[j], Heading = simulation.State.Movement[j].Heading },
                            UnitSize.Small) > 0) Assert.Fail("tick/pair " + tick + "/" + i + "/" + j);
                }
                if (movedCount == 100 && allMovedTick == 0) allMovedTick = tick + 1;
            }
            int[] stopped = new int[4]; int orders = 0, insufficientProgress = 0, moving = 0, restWithOrder = 0;
            float leastProgress = float.PositiveInfinity;
            float2 min = new float2(float.PositiveInfinity), max = new float2(float.NegativeInfinity);
            for (int i = 0; i < 100; i++)
            {
                var unit = simulation.State.Movement[i];
                if (math.any(unit.Velocity != 0)) moving++;
                float progress = initialDistance[i] - math.distance(simulation.State.Positions[i], (float3)goal);
                leastProgress = math.min(leastProgress, progress);
                if (progress < 1) insufficientProgress++;
                stopped[unit.ArrivalRule]++;
                if (unit.HasMoveOrder) orders++;
                if (unit.HasMoveOrder && math.all(unit.Velocity == 0)) restWithOrder++;
                float extent = .25f * (math.abs(math.cos(unit.Heading)) + math.abs(math.sin(unit.Heading)));
                min = math.min(min, simulation.State.Positions[i].xz - extent);
                max = math.max(max, simulation.State.Positions[i].xz + extent);
            }
            TestContext.WriteLine("lattice=" + lattice + "; touching=" + touching + "; goal=" + goalX + "," + goalZ + "; T=" + (lastChange + 1) +
                "; first_tick_moving=" + firstTickMoving + "; all_moved_tick=" + allMovedTick +
                "; exact/contact/congestion=" + stopped[1] + "/" + stopped[2] + "/" + stopped[3] +
                "; moving=" + moving + "; orders=" + orders + "; at_rest_with_order=" + restWithOrder +
                "; S2.2_area_ratio=" + ((max.x - min.x) * (max.y - min.y) / 25) +
                "; under_one_cell_progress=" + insufficientProgress + "; least_progress=" + leastProgress);
            if (touching) return; // Owner ruling: touching starts are reported until movement steps 4 and 5.
            Assert.That(firstTickMoving, Is.EqualTo(100));
            Assert.That(moving, Is.Zero, "All units must be at rest at tick 1200");
            Assert.That(lastChange + 1, Is.LessThanOrEqualTo(1140));
            Assert.That(allMovedTick, Is.GreaterThan(0));
            Assert.That(insufficientProgress, Is.Zero, "Every unit must end at least one cell closer");
            Assert.That(simulation.MoveMarkerPosition, Is.EqualTo(goal));
        }

        [Test]
        public void UnitHeldAwayFromGoalKeepsOrderAfterTwoSecondsWithoutProgress()
        {
            using var ownedState = new UnitState(1);
            var state = ownedState;
            using var ownedMap = new NativeArray<byte>(256 * 256, Allocator.Persistent);
            var map = ownedMap;
            for (int i = 0; i < map.Length; i++) map[i] = (byte)(i % 256 == 128 ? 0 : 1);
            var position = new float3(-Footprint.Radius(UnitSize.Small), 0, 0);
            state.Positions[0] = position; state.Alive[0] = 1;
            state.Movement[0] = new MovementUnit { HasMoveOrder = true, OrderVersion = 1,
                Goal = new float2(15, 0), BestGoalDistance = 15 - position.x, LastProgressTick = 1,
                Mode = MovementMode.Moving, Previous = new MovementPose { Position = position } };
            using var pipeline = new MovementPipeline(1, Settings(), map);
            for (ulong tick = 1; tick <= 90; tick++)
            {
                pipeline.Schedule(state, default, tick: tick).Complete();
            }
            Assert.That(math.distance(state.Positions[0], position), Is.LessThan(1e-5));
            Assert.That(state.Movement[0].HasMoveOrder, Is.True);
            Assert.That(state.Movement[0].ArrivalRule, Is.Zero);
            Assert.That(state.Movement[0].PreferredVelocity.x, Is.GreaterThan(0));
            Assert.That(state.Movement[0].LastProgressTick, Is.EqualTo(1));
        }

        [Test]
        public void NewOrderResetsProgressAndArrivalStateOnItsTick()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero); Frame(simulation, 1);
            SelectAll(simulation); Order(simulation, new Vector3(1, 0, 0));
            for (int tick = 0; tick < 90; tick++) Frame(simulation, 1);
            Assert.That(simulation.State.Movement[0].ArrivalRule, Is.EqualTo(1));
            Order(simulation, new Vector3(20, 0, 0));
            float distance = 20 - simulation.State.Positions[0].x;
            Frame(simulation, 1);
            Assert.That(simulation.State.Movement[0].BestGoalDistance, Is.EqualTo(distance));
            Assert.That(simulation.State.Movement[0].LastProgressTick, Is.EqualTo(simulation.TickCount));
            Assert.That(simulation.State.Movement[0].ArrivalRule, Is.Zero);
            Assert.That(simulation.State.Movement[0].HasMoveOrder, Is.True);
        }
    }
}
