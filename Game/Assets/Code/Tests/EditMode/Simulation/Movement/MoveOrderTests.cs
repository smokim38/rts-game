using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using Northwatch.Simulation.FlowFields;
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

        private static void Spawn(UnitSimulation simulation, bool lattice)
        {
            if (lattice)
                for (int i = 0; i < 100; i++)
                    Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small,
                        new Vector3((i % 10 - 4.5f) * .71f, 0,
                            (i / 10 - 4.5f) * .71f)), Is.True);
            else Assert.That(simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 100), Is.True);
            Frame(simulation, 1);
        }

        [TestCase(1, UnitSize.Small)] [TestCase(1, UnitSize.Large)] [TestCase(100, UnitSize.Small)]
        public void ClosedRingOrderStopsWithoutOverlapsOrFurtherRequests(int count, UnitSize size)
        {
            using var boxes = new NativeArray<StaticBox>(new[] {
                new StaticBox { Min = new int2(203, 148), MaxExclusive = new int2(223, 149) },
                new StaticBox { Min = new int2(203, 167), MaxExclusive = new int2(223, 168) },
                new StaticBox { Min = new int2(203, 149), MaxExclusive = new int2(204, 167) },
                new StaticBox { Min = new int2(222, 149), MaxExclusive = new int2(223, 167) } }, Allocator.TempJob);
            using var simulation = new UnitSimulation(Settings(), boxes);
            Assert.That(simulation.QueueBlock(Side.First, size, new Vector3(60, 0, 30), count), Is.True);
            Frame(simulation, 1); SelectAll(simulation); Order(simulation, new Vector3(88, 0, 30));
            Frame(simulation, 1);
            Assert.That(simulation.GoalSearchCount, Is.EqualTo(1));
            Assert.That(simulation.FieldRequestCount, Is.LessThanOrEqualTo(1));
            float2 correctedGoal = size == UnitSize.Small ? new float2(95.5f, 30.5f) : new float2(96, 30);
            long searches = simulation.GoalSearchCount, requests = simulation.FieldRequestCount;
            for (int tick = 0; tick < 1320; tick++)
            {
                Frame(simulation, 1);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(simulation.State.Movement[i].Goal, Is.EqualTo(correctedGoal));
                    Assert.That(StaticClamp.IsClear(simulation.State.Positions[i].xz, Footprint.Radius(size),
                        simulation.StaticMaps.Walkable), Is.True, "Disc in a blocked cell on tick " + tick);
                    if (tick >= 1200)
                    {
                        Assert.That(simulation.State.Movement[i].HasMoveOrder, Is.False);
                        Assert.That(simulation.State.Movement[i].Velocity, Is.EqualTo(float2.zero));
                        Assert.That(simulation.State.Positions[i], Is.EqualTo(simulation.State.Movement[i].Previous.Position));
                        Assert.That(simulation.State.Movement[i].Heading, Is.EqualTo(simulation.State.Movement[i].Previous.Heading));
                    }
                    for (int j = i + 1; j < count; j++)
                    {
                        if (math.distancesq(simulation.State.Positions[i], simulation.State.Positions[j]) > 1) continue;
                        var a = new MovementPose { Position = simulation.State.Positions[i], Heading = simulation.State.Movement[i].Heading };
                        var b = new MovementPose { Position = simulation.State.Positions[j], Heading = simulation.State.Movement[j].Heading };
                        if (Footprint.Overlap(a, size, b, size) > 0) Assert.Fail("Footprint overlap on tick " + tick);
                    }
                }
                Assert.That(simulation.GoalSearchCount, Is.EqualTo(searches));
                Assert.That(simulation.FieldRequestCount, Is.EqualTo(requests));
            }
            int groupStalls = 0, exact = 0, contact = 0, congestion = 0;
            for (int i = 0; i < count; i++)
            {
                Assert.That(simulation.State.Movement[i].HasMoveOrder, Is.False);
                switch (simulation.State.Movement[i].ArrivalRule)
                {
                    case 1: exact++; break;
                    case 2: contact++; break;
                    case 3: congestion++; break;
                    case 4:
                        groupStalls++;
                        TestContext.WriteLine($"group_stall_unit={i}; position={simulation.State.Positions[i].xz}");
                        break;
                }
            }
            if (count == 1)
            {
                Assert.That(math.distance(simulation.State.Positions[0].xz, correctedGoal), Is.LessThan(.05f));
                Assert.That(groupStalls, Is.Zero);
            }
            // ADR 0015 gates the 100-cube stop and safety; Issue 5 gates arrival without stalls.
            TestContext.WriteLine($"ring_units={count}; size={size}; exact={exact}; contact={contact}; congestion={congestion}; group_stalls={groupStalls}; searches={searches}; requests={requests}");
        }

        [TestCase(true, true)] [TestCase(false, true)]
        [TestCase(true, false)] [TestCase(false, false)]
        public void ClickQueuesSnapshotAndNextTickPlansEverySelectedUnit(bool classic, bool lattice)
        {
            using var simulation = new UnitSimulation(Settings());
            Spawn(simulation, lattice);
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
            TestContext.WriteLine("First tick moving=" + started + "/100; lattice=" + lattice + "; classic=" + classic);
            Assert.That(started, Is.EqualTo(100), "Units with velocity on the first normally spaced tick");
            Frame(simulation, 0, 1);
            Assert.That(simulation.AcknowledgementCount, Is.Zero);
        }

        [Test]
        public void OwnSpotsCaptureTheClickSnapshotAndNewOrdersCaptureTheCurrentShape()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(-3, 0, -1));
            simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(3, 0, 1));
            Frame(simulation, 1); SelectAll(simulation); Order(simulation, new Vector3(20, 0, 5));
            simulation.State.Positions[0] = new float3(-4, 0, -2);
            simulation.State.Positions[1] = new float3(4, 0, 2);
            Frame(simulation, 1);
            for (int i = 0; i < 2; i++)
            {
                var unit = simulation.State.Movement[i];
                Assert.That(unit.SpotOffset, Is.EqualTo(new float2(i == 0 ? -3 : 3, i == 0 ? -1 : 1)));
                Assert.That(unit.HasOwnSpot, Is.True);
                Assert.That(unit.Spot, Is.EqualTo(unit.Goal + unit.SpotOffset));
                Assert.That(unit.PreferredVelocity.y, Is.GreaterThan(0), "An order inside the old z span must still centre on the click.");
            }
            float2 centre = (simulation.State.Positions[0].xz + simulation.State.Positions[1].xz) * .5f;
            float2 offset = simulation.State.Positions[0].xz - centre;
            Order(simulation, new Vector3(30, 0, 0)); Frame(simulation, 1);
            Assert.That(simulation.State.Movement[0].SpotOffset, Is.EqualTo(offset));
        }

        [Test]
        public void MembersReachOwnSpotsPastTheSharedGoal()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(-10, 0, 0));
            Frame(simulation, 1); SelectAll(simulation); Order(simulation, Vector3.right);
            for (int tick = 0; tick < 180; tick++) Frame(simulation, 1);
            for (int i = 0; i < 2; i++)
            {
                var unit = simulation.State.Movement[i];
                Assert.That(unit.Spot, Is.EqualTo(new float2(i == 0 ? 6 : -4, 0)));
                Assert.That(math.distance(simulation.State.Positions[i].xz, unit.Spot), Is.LessThan(.05f));
                Assert.That(unit.HasMoveOrder, Is.False);
                Assert.That(unit.ArrivalRule, Is.EqualTo(1));
            }
        }

        [Test]
        public void OwnSpotsKeepRuleThreeAtTheSharedGoal()
        {
            using var ownedState = new UnitState(1);
            var state = ownedState;
            using var pipeline = new MovementPipeline(1, Settings());
            state.Alive[0] = 1;
            state.Movement[0] = new MovementUnit { HasMoveOrder = true, Mode = MovementMode.Moving,
                Goal = float2.zero, Spot = new float2(3, 0), HasOwnSpot = true,
                OrderVersion = 1, BestGoalDistance = 0, LastProgressTick = 1 };
            pipeline.Schedule(state, default, tick: 31).Complete();
            Assert.That(state.Movement[0].ArrivalRule, Is.EqualTo(3));
        }

        [TestCase(true)] [TestCase(false)]
        public void ContactArrivalAppliesOnlyWhenTheOwnSpotFellBack(bool validSpot)
        {
            using var ownedState = new UnitState(2);
            var state = ownedState;
            using var pipeline = new MovementPipeline(2, Settings());
            state.Alive[0] = state.Alive[1] = 1;
            state.Positions[1] = new float3(.71f, 0, 0);
            state.Movement[0] = new MovementUnit { HasMoveOrder = true, Mode = MovementMode.Moving,
                Goal = new float2(3, 0), Spot = new float2(3, 0), HasOwnSpot = validSpot,
                OrderVersion = 1, BestGoalDistance = 3, LastProgressTick = 1 };
            state.Movement[1] = new MovementUnit { Mode = MovementMode.Idle, OrderVersion = 1 };
            pipeline.Schedule(state, default, tick: 1).Complete();
            Assert.That(state.Movement[0].ArrivalRule, Is.EqualTo(validSpot ? 0 : 2));
            Assert.That(state.Movement[0].HasMoveOrder, Is.EqualTo(validSpot));
        }

        [Test]
        public void DirectSeekReachesExactGoalWithoutOvershootAndMarkerEnds()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 1); Frame(simulation, 1);
            SelectAll(simulation); var goal = new Vector3(10, 0, 3); Order(simulation, goal);
            for (int t = 0; t < 180; t++) Frame(simulation, 1);
            Assert.That(math.distance(simulation.State.Positions[0], (float3)goal), Is.LessThan(1e-5));
            Assert.That(math.lengthsq(simulation.State.Movement[0].Velocity), Is.LessThan(1e-10));
            Assert.That(simulation.MoveMarkerCount, Is.Zero);
            for (int t = 0; t < 300; t++) Frame(simulation, 1);
            Assert.That(simulation.MoveMarkerVersion, Is.EqualTo(1));
            Assert.That(simulation.MoveMarkerCount, Is.Zero);
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
            Assert.That(simulation.MoveMarkerCount, Is.Zero);
            Assert.That(simulation.AcknowledgementCount, Is.Zero);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void LastHolderEndingByAnyArrivalRuleClearsCountOnNextDraw(byte rule)
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(-5, 0, 0)); Frame(simulation, 1);
            SelectAll(simulation); Order(simulation, new Vector3(20, 0, 0));
            Assert.That(simulation.MoveMarkerCount, Is.EqualTo(2), "Pending recipients count before a tick.");
            Frame(simulation, 1);
            for (int i = 0; i < 2; i++)
            {
                var unit = simulation.State.Movement[i];
                unit.HasMoveOrder = false; unit.Mode = MovementMode.Idle; unit.ArrivalRule = rule;
                simulation.State.Movement[i] = unit;
                Frame(simulation);
                Assert.That(simulation.MoveMarkerCount, Is.EqualTo(1 - i));
            }
        }

        [Test]
        public void MarkerCountsUntilLastActualArrivalAndDoesNotReappear()
        {
            using var simulation = new UnitSimulation(Settings());
            // Different speeds keep arrivals staggered when own spots give equal travel distances.
            simulation.QueueAdd(Side.First, UnitSize.Large, Vector3.zero);
            simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(-5, 0, 0)); Frame(simulation, 1);
            SelectAll(simulation); Order(simulation, new Vector3(10, 0, 0));
            bool sawOneHolder = false;
            for (int tick = 0; tick < 400; tick++)
            {
                Frame(simulation, 1);
                int holders = (simulation.State.Movement[0].HasMoveOrder ? 1 : 0) +
                    (simulation.State.Movement[1].HasMoveOrder ? 1 : 0);
                Assert.That(simulation.MoveMarkerCount, Is.EqualTo(holders), "Same completed frame as movement.");
                sawOneHolder |= holders == 1;
            }
            Assert.That(sawOneHolder, Is.True);
            Assert.That(simulation.MoveMarkerCount, Is.Zero);
        }

        [Test]
        public void OlderOrderArrivalCannotEndNewerMarkerOrReuseItsVersion()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(-10, 0, 0)); Frame(simulation, 1);
            SelectAll(simulation); Order(simulation, new Vector3(1, 0, 0)); Frame(simulation, 1);
            simulation.QueueSelection(SelectionRequest.PointerClick(new Ray(new Vector3(-10, 10, 0), Vector3.down),
                Matrix4x4.identity, true, false, false, false, false)); Frame(simulation);
            Order(simulation, new Vector3(-30, 0, 0));
            Assert.That(simulation.MoveMarkerVersion, Is.EqualTo(2));
            Assert.That(simulation.MoveMarkerCount, Is.EqualTo(1));
            for (int tick = 0; tick < 60; tick++) Frame(simulation, 1);
            Assert.That(simulation.State.Movement[0].HasMoveOrder, Is.False);
            Assert.That(simulation.MoveMarkerCount, Is.EqualTo(1));
            Assert.That(simulation.MoveMarkerPosition, Is.EqualTo(new Vector3(-30, 0, 0)));
            for (int tick = 0; tick < 300; tick++) Frame(simulation, 1);
            Assert.That(simulation.MoveMarkerCount, Is.Zero);
            Order(simulation, new Vector3(-50, 0, 0));
            Assert.That(simulation.MoveMarkerVersion, Is.EqualTo(3));
            Assert.That(simulation.MoveMarkerCount, Is.EqualTo(1));
        }

        [TestCase(false, 15f, 0f)] [TestCase(true, 15f, 0f)]
        [TestCase(false, 14.9999542f, -.0000152587891f)]
        [TestCase(false, 15.04f, 0f)] [TestCase(true, 15.04f, 0f)]
        [TestCase(false, 14.96f, 0f)] [TestCase(true, 14.96f, 0f)]
        [TestCase(false, 15f, .04f)] [TestCase(true, 15f, .04f)]
        [TestCase(false, 15f, -.04f)] [TestCase(true, 15f, -.04f)]
        [TestCase(false, 15.03f, .03f)] [TestCase(true, 15.03f, .03f)]
        [TestCase(false, 14.97f, .03f)] [TestCase(true, 14.97f, .03f)]
        [TestCase(false, 15.03f, -.03f)] [TestCase(true, 15.03f, -.03f)]
        [TestCase(false, 14.97f, -.03f)] [TestCase(true, 14.97f, -.03f)]
        public void HundredUnitsComeToRestWithoutAnyTickAddingFootprintOverlap(bool lattice, float goalX, float goalZ)
        {
            using var simulation = new UnitSimulation(Settings());
            Spawn(simulation, lattice);
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
            int[] stopped = new int[5]; int orders = 0, insufficientProgress = 0, moving = 0, restWithOrder = 0;
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
            TestContext.WriteLine("lattice=" + lattice + "; goal=" + goalX + "," + goalZ + "; T=" + (lastChange + 1) +
                "; first_tick_moving=" + firstTickMoving + "; all_moved_tick=" + allMovedTick +
                "; exact/contact/congestion/group_stall=" + stopped[1] + "/" + stopped[2] + "/" + stopped[3] + "/" + stopped[4] +
                "; moving=" + moving + "; orders=" + orders + "; at_rest_with_order=" + restWithOrder +
                "; S2.2_area_ratio=" + ((max.x - min.x) * (max.y - min.y) / 25) +
                "; under_one_cell_progress=" + insufficientProgress + "; least_progress=" + leastProgress);
            Assert.That(firstTickMoving, Is.EqualTo(100));
            Assert.That(moving, Is.Zero, "All units must be at rest at tick 1200");
            Assert.That(orders, Is.Zero);
            Assert.That(lastChange + 1, Is.LessThanOrEqualTo(1140));
            Assert.That(allMovedTick, Is.GreaterThan(0));
            Assert.That(insufficientProgress, Is.Zero, "Every unit must end at least one cell closer");
            Assert.That(simulation.MoveMarkerPosition, Is.EqualTo(goal));
        }

        [Test]
        public void BlockedKnotSettlesBy181TicksAndRemainsStill()
        {
            using var ownedState = new UnitState(5);
            var state = ownedState;
            using var pipeline = new MovementPipeline(5, Settings());
            for (int i = 0; i < 5; i++)
            {
                float3 position = i == 0 ? float3.zero :
                    new float3(i < 3 ? (i == 1 ? -.5f : .5f) : 0, 0, i >= 3 ? (i == 3 ? -.5f : .5f) : 0);
                state.Positions[i] = position; state.Alive[i] = 1;
                state.Movement[i] = new MovementUnit { Mode = i == 0 ? MovementMode.Moving : MovementMode.Hold,
                    HasMoveOrder = i == 0, OrderVersion = i == 0 ? 1 : 0,
                    Goal = new float2(15, 0), BestGoalDistance = 15, LastProgressTick = 1,
                    Previous = new MovementPose { Position = position } };
            }
            for (ulong tick = 1; tick <= 241; tick++)
            {
                pipeline.Schedule(state, default, tick: tick).Complete();
                if (tick < 181) Assert.That(state.Movement[0].HasMoveOrder, Is.True);
                else
                {
                    Assert.That(state.Movement[0].ArrivalRule, Is.EqualTo(4));
                    Assert.That(state.Movement[0].HasMoveOrder, Is.False);
                    Assert.That(math.lengthsq(state.Movement[0].Velocity), Is.Zero);
                    Assert.That(math.lengthsq(state.Positions[0]), Is.LessThan(1e-10f));
                    Assert.That(state.Movement[0].Heading, Is.Zero);
                }
            }
        }

        [Test]
        public void ProgressOnThisTickKeepsOnlyItsOwnOrderAlive()
        {
            using var ownedState = new UnitState(4);
            var state = ownedState;
            using var pipeline = new MovementPipeline(4, Settings());
            for (int i = 0; i < 4; i++)
            {
                state.Positions[i] = new float3(0, 0, i * 10); state.Alive[i] = 1;
                state.Movement[i] = new MovementUnit { Mode = MovementMode.Moving, HasMoveOrder = true,
                    OrderVersion = i < 2 ? 1 : 2, Goal = new float2(15, i * 10),
                    BestGoalDistance = i == 0 || i == 3 ? 15.2f : 15, LastProgressTick = 1 };
            }
            state.Alive[3] = 0; // A dead member cannot reset the other order's timer.
            pipeline.Schedule(state, default, tick: 181).Complete();
            Assert.That(state.Movement[0].HasMoveOrder, Is.True);
            Assert.That(state.Movement[0].LastProgressTick, Is.EqualTo(181));
            Assert.That(state.Movement[1].HasMoveOrder, Is.True);
            Assert.That(math.lengthsq(state.Movement[1].PreferredVelocity), Is.GreaterThan(0));
            Assert.That(state.Movement[2].HasMoveOrder, Is.False);
            Assert.That(state.Movement[2].ArrivalRule, Is.EqualTo(4));
            // Replacing every order must reclaim the full table without stale progress.
            for (int i = 0; i < 4; i++)
            {
                state.Alive[i] = 1;
                var unit = state.Movement[i]; unit.OrderVersion = 100 + i;
                unit.HasMoveOrder = true; unit.Mode = MovementMode.Moving;
                unit.BestGoalDistance = 15; unit.LastProgressTick = 182; unit.ArrivalRule = 0;
                state.Movement[i] = unit;
            }
            pipeline.Schedule(state, default, tick: 182).Complete();
            for (int i = 0; i < 4; i++) Assert.That(state.Movement[i].HasMoveOrder, Is.True);
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
