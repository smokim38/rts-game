using System;
using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.FlowFields;
using Northwatch.Simulation.Movement;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation.FlowFields
{
    public sealed class FlowFieldPoolTests
    {
        private static MovementParameters Settings(int capacity = 256)
        {
            var asset = ScriptableObject.CreateInstance<MovementSettings>();
            var settings = asset.Snapshot(); UnityEngine.Object.DestroyImmediate(asset);
            settings.FieldCapacity = capacity; return settings;
        }
        private static void Order(UnitState state, int unit, int order, float2 goal, float2 position, byte size = 0)
        {
            state.Alive[unit] = 1; state.Positions[unit] = new float3(position.x, 0, position.y);
            state.Sizes[unit] = (UnitSize)size;
            state.Movement[unit] = new MovementUnit { HasMoveOrder = true, Mode = MovementMode.Moving,
                ProgressField = -1, OrderVersion = order, Goal = goal };
        }
        private static JobHandle Tick(FlowFieldPool pool, StaticMaps maps, UnitState state, ulong tick, JobHandle dependency = default)
        {
            var handle = pool.Publish(tick, dependency);
            handle = pool.RequestFields(state, maps.Passable, tick, handle);
            return pool.ScheduleBuilds(maps.Passable, tick, handle);
        }

        [TestCase(0)] [TestCase(1)]
        public void OpenGroundHasExactOctileDistancesAndClassAnchors(byte size)
        {
            using var maps = new StaticMaps(20, 20);
            using var boxes = new NativeArray<StaticBox>(0, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            using var state = new UnitState(1);
            using var pool = new FlowFieldPool(20, 20, 0, 1, Settings(2));
            Order(state, 0, 1, new float2(15.5f + size * .5f), new float2(1.5f + size * .5f), size);
            Tick(pool, maps, state, 0).Complete();
            Assert.That(pool.UnitFields[0], Is.EqualTo(-1));
            Tick(pool, maps, state, 1).Complete();
            int field = pool.UnitFields[0]; Assert.That(field, Is.GreaterThanOrEqualTo(0));
            for (int y = 0; y < 19; y++)
                for (int x = 0; x < 19; x++)
                {
                    int dx = math.abs(15 - x), dy = math.abs(15 - y);
                    Assert.That(pool.Distances[field * 400 + y * 20 + x], Is.EqualTo(10 * math.max(dx, dy) + 4 * math.min(dx, dy)));
                }
            Assert.That(pool.Reader.Sample(field, new float2(15.5f + size * .5f), out var sample), Is.EqualTo(FlowSampleStatus.Ready));
            Assert.That(sample.Distance, Is.Zero); Assert.That(sample.Direction, Is.EqualTo(float2.zero));
        }

        [Test]
        public void BoxDetourUsesLegalStepsAndStaysWithinShortestRouteBound()
        {
            using var maps = new StaticMaps(24, 24);
            using var boxes = new NativeArray<StaticBox>(new[] { new StaticBox { Min = new int2(11, 4), MaxExclusive = new int2(12, 20) } }, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            using var state = new UnitState(1);
            using var pool = new FlowFieldPool(24, 24, 0, 1, Settings(2));
            Order(state, 0, 1, new float2(18.5f, 12.5f), new float2(5.5f, 12.5f));
            Tick(pool, maps, state, 0).Complete(); Tick(pool, maps, state, 1).Complete();
            int field = pool.UnitFields[0], cell = 12 * 24 + 5, goal = 12 * 24 + 18;
            float length = 0;
            for (int n = 0; cell != goal && n < 576; n++)
            {
                int2 p = new int2(cell % 24, cell / 24), step = BuildFlowFieldJob.Step(pool.Directions[field * 576 + cell]);
                int2 next = p + step;
                Assert.That(maps.Passable[next.y * 24 + next.x], Is.EqualTo(1));
                if (step.x != 0 && step.y != 0)
                {
                    Assert.That(maps.Passable[p.y * 24 + next.x], Is.EqualTo(1));
                    Assert.That(maps.Passable[next.y * 24 + p.x], Is.EqualTo(1));
                }
                length += math.length((float2)step); cell = next.y * 24 + next.x;
            }
            Assert.That(cell, Is.EqualTo(goal));
            // Euclidean shortest route round the lower two corners is a lower bound on any legal route.
            float shortest = math.length(new float2(6, 8)) + 1 + math.length(new float2(6, 8));
            Assert.That(length, Is.LessThanOrEqualTo(1.25f * shortest));
        }

        [Test]
        public void SaturationRotatesOnlyAfterLeaseAndEveryOrderReceivesAField()
        {
            var settings = Settings(3); settings.MinLease = .2f;
            using var maps = new StaticMaps(32, 32);
            using var boxes = new NativeArray<StaticBox>(0, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            using var state = new UnitState(4);
            using var pool = new FlowFieldPool(32, 32, 0, 4, settings);
            for (int i = 0; i < 4; i++) Order(state, i, i + 1, new float2(28.5f, i + .5f), new float2(.5f, i + .5f));
            var last = new int[4]; var seen = new bool[4];
            for (int tick = 0; tick < 80; tick++)
            {
                Tick(pool, maps, state, (ulong)tick).Complete();
                for (int i = 0; i < 4; i++)
                {
                    if (pool.UnitFields[i] >= 0) { last[i] = tick; seen[i] = true; }
                    Assert.That(tick - last[i], Is.LessThanOrEqualTo(12), "lease wait bound, order " + i);
                }
            }
            Assert.That(seen, Is.All.True);
        }

        [Test]
        public void ClearShortOrderRequestsNothingAndAbandonedRequestsAreDropped()
        {
            using var maps = new StaticMaps(32, 32);
            using var boxes = new NativeArray<StaticBox>(0, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            using var state = new UnitState(4);
            using var pool = new FlowFieldPool(32, 32, 0, 4, Settings(2));
            for (int i = 0; i < 4; i++) Order(state, i, i + 1, new float2(29, i + 1), new float2(1, i + 1));
            Tick(pool, maps, state, 0).Complete();
            Order(state, 2, 5, new float2(4, 3), new float2(1, 3)); var alive = state.Alive; alive[3] = 0;
            Tick(pool, maps, state, 1).Complete(); Tick(pool, maps, state, 2).Complete();
            Assert.That(pool.UnitFields[2], Is.EqualTo(-1));
            foreach (var entry in pool.Entries)
                Assert.That(entry.Valid == 0 || entry.Goal % 32 == 29 && entry.Goal / 32 <= 2, Is.True);
        }

        [TestCase(295, 256, true)] [TestCase(296, 256, false)] [TestCase(320, 200, false)]
        [TestCase(256, 199, false)] [TestCase(256, 342, false)]
        public void MatchStartEnforcesIndependentMapAndMemoryLimits(int width, int capacity, bool allowed)
        {
            var settings = Settings(capacity);
            if (allowed) Assert.DoesNotThrow(() => settings.ValidateFields(width, width));
            else Assert.Throws<ArgumentException>(() => settings.ValidateFields(width, width));
        }

        [Test]
        public void OneLongRouteRequestsBothClassesOfTheOrder()
        {
            using var maps = new StaticMaps(32, 32);
            using var boxes = new NativeArray<StaticBox>(0, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            using var state = new UnitState(2);
            using var pool = new FlowFieldPool(32, 32, 0, 2, Settings(2));
            Order(state, 0, 1, new float2(2), new float2(1));
            Order(state, 1, 1, new float2(2), new float2(28), 1);
            Tick(pool, maps, state, 0).Complete(); Tick(pool, maps, state, 1).Complete();
            Assert.That(pool.UnitFields[0], Is.GreaterThanOrEqualTo(0));
            Assert.That(pool.UnitFields[1], Is.GreaterThanOrEqualTo(0).And.Not.EqualTo(pool.UnitFields[0]));
        }

        [Test]
        public void DeterministicAdr0011StreamStaysBelowFivePercentStopgap()
        {
            using var maps = new StaticMaps(256, 256);
            using var boxes = new NativeArray<StaticBox>(0, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            using var state = new UnitState(200);
            using var pool = new FlowFieldPool(256, 256, new float2(-128), 200, Settings());
            var random = new Unity.Mathematics.Random(160);
            var goals = new bool[65536]; var positions = new bool[65536];
            for (int i = 0; i < 200; i++)
            {
                int cell; do { cell = random.NextInt(65536); } while (positions[cell]); positions[cell] = true;
                Order(state, i, i + 1, NextGoal(ref random, goals, cell % 256 < 128),
                    new float2(cell % 256 - 127.5f, cell / 256 - 127.5f));
            }
            int stopgap = 0;
            for (int tick = 0; tick < 1830; tick++)
            {
                if (tick > 0 && tick <= 1800 && tick % 3 == 0)
                {
                    int i = (tick / 3 - 1) % 200;
                    Order(state, i, 200 + tick / 3, NextGoal(ref random, goals, state.Positions[i].x < 0), state.Positions[i].xz);
                }
                Tick(pool, maps, state, (ulong)tick).Complete();
                for (int i = 0; i < 200; i++) if (pool.UnitFields[i] < 0) stopgap++;
            }
            TestContext.WriteLine("Stopgap " + stopgap + "/366000 = " + 100.0 * stopgap / 366000 + "%");
            Assert.That(stopgap, Is.LessThanOrEqualTo(18300));
            Assert.That(pool.PoolBytes, Is.EqualTo(48 * 1024 * 1024));
        }
        private static float2 NextGoal(ref Unity.Mathematics.Random random, bool[] used, bool right)
        {
            int cell;
            do { cell = random.NextInt(256) * 256 + (right ? random.NextInt(144, 256) : random.NextInt(112)); } while (used[cell]);
            used[cell] = true;
            return new float2(cell % 256 - 127.5f, cell / 256 - 127.5f);
        }

        [TestCase(1, 1)] [TestCase(3, 4)]
        public void DependencyLoadNeverChangesThePublishTick(int latency, int slots)
        {
            var settings = Settings(8); settings.BuildLatencyTicks = latency; settings.BuildSlots = slots;
            using var maps = new StaticMaps(32, 32);
            using var boxes = new NativeArray<StaticBox>(0, Allocator.TempJob);
            maps.ScheduleBuild(boxes).Complete();
            using var state = new UnitState(1);
            using var pool = new FlowFieldPool(32, 32, 0, 1, settings);
            using var load = new NativeArray<uint>(1, Allocator.Persistent);
            Order(state, 0, 1, new float2(30.5f), new float2(.5f));
            JobHandle chain = Tick(pool, maps, state, 0, new WorkerLoadJob { Result = load }.Schedule());
            for (int tick = 1; tick <= latency; tick++)
            {
                chain = Tick(pool, maps, state, (ulong)tick, chain);
                chain.Complete();
                Assert.That(pool.UnitFields[0] >= 0, Is.EqualTo(tick == latency));
            }
            Assert.That(pool.Entries[pool.UnitFields[0]].Published, Is.EqualTo((ulong)latency));
        }
        private struct WorkerLoadJob : IJob
        {
            internal NativeArray<uint> Result;
            public void Execute()
            {
                uint value = 160;
                for (int i = 0; i < 1000000; i++) value = value * 1664525 + 1013904223;
                Result[0] = value;
            }
        }

        [TestCase(UnitSize.Small)] [TestCase(UnitSize.Large)]
        public void MovingUnitRoundsSandboxWallWithoutStaticPenetration(UnitSize size)
        {
            using var boxes = new NativeArray<StaticBox>(new[] { new StaticBox { Min = new int2(148, 88), MaxExclusive = new int2(149, 168) } }, Allocator.TempJob);
            using var simulation = new UnitSimulation(Settings(), boxes);
            simulation.QueueAdd(Side.First, size, new Vector3(10.5f, 0, 0.5f));
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            UnitState state = simulation.State;
            Order(state, 0, 1, new float2(30.5f, .5f), new float2(10.5f, .5f), (byte)size);
            float route = 0;
            for (int tick = 0; tick < 1800 && state.Movement[0].HasMoveOrder; tick++)
            {
                float3 previous = state.Positions[0];
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                route += math.distance(previous, state.Positions[0]);
                Assert.That(StaticClamp.IsClear(state.Positions[0].xz, Footprint.Radius(size), simulation.StaticMaps.Walkable), Is.True);
            }
            Assert.That(math.distance(state.Positions[0].xz, new float2(30.5f, .5f)), Is.LessThan(.1f),
                "pose=" + state.Positions[0] + "; unit=" + state.Movement[0].Mode + "; arrival=" + state.Movement[0].ArrivalRule +
                "; best=" + state.Movement[0].BestGoalDistance + "; progress=" + state.Movement[0].LastProgressTick);
            float shortest = math.length(new float2(9.5f, 40.5f)) + 1 + math.length(new float2(9.5f, 40.5f));
            Assert.That(route, Is.LessThanOrEqualTo(shortest * 1.25f));
        }
    }
}
