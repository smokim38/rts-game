using Northwatch.Presentation.Diagnostics;
using Northwatch.Presentation;
using Northwatch.Simulation;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class LadderFrameBufferTests
    {
        [TestCase(10)] [TestCase(50)] [TestCase(2000)]
        public void SpawnFacingRowsStayOnTheirOwnGoalLines(int count)
        {
            var scenario = ScriptableObject.CreateInstance<LadderScenario>();
            try
            {
                using var simulation = new UnitSimulation();
                scenario.QueueUnits(simulation, count);
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                float firstEdge = float.MaxValue, secondEdge = float.MinValue;
                for (int i = 0; i < count; i++)
                    if (simulation.State.Sides[i] == Side.First) firstEdge = Mathf.Min(firstEdge, simulation.State.Positions[i].z);
                    else secondEdge = Mathf.Max(secondEdge, simulation.State.Positions[i].z);
                Assert.That(firstEdge, Is.EqualTo(scenario.LaneGap / 2).Within(.00001));
                Assert.That(secondEdge, Is.EqualTo(-scenario.LaneGap / 2).Within(.00001));
            }
            finally { Object.DestroyImmediate(scenario); }
        }

        [Test]
        public void CrossingCountsWholeArmiesAndClearsMeetFlagsAtEachBoundary()
        {
            using var stateStorage = new UnitState(10);
            var state = stateStorage;
            using var previous = new NativeArray<float3>(10, Allocator.TempJob);
            using var snapshots = new NativeArray<float4>(10, Allocator.TempJob);
            using var met = new NativeArray<byte>(10, Allocator.TempJob);
            using var counts = new NativeArray<LadderCycleCounts>(6, Allocator.TempJob);
            for (int i = 0; i < 10; i++)
            {
                state.Alive[i] = 1; state.Sides[i] = i < 5 ? Side.First : Side.Second;
                state.Positions[i] = new float3(i < 5 ? -10 : 10, 0, 0);
            }
            var job = new LadderPassJob { State = state, Previous = previous, Snapshots = snapshots, Met = met,
                Counts = counts, Cycle = -1, Seconds = 20,
                Settings = new Northwatch.Simulation.Movement.MovementParameters { SmallSpeed = 4, LargeSpeed = 2.5f } };
            job.Run();
            state.Positions[0] = new float3(20, 0, 0);
            var meetFlags = met;
            meetFlags[0] = 1;
            job.Cycle = 1; job.Run();
            Assert.That(counts[2].passed, Is.EqualTo(1), "One crossing unit must not stand in for its army.");
            Assert.That(counts[3].passed, Is.Zero);
            Assert.That(counts[2].met, Is.EqualTo(1));
            Assert.That(met[0], Is.Zero, "Meet flags belong only to the completed cycle.");
            for (int i = 0; i < 10; i++) state.Positions[i] = new float3(i < 5 ? 30 : -30, 0, 0);
            job.Cycle = 2; job.Run();
            Assert.That(counts[4].passed, Is.EqualTo(4));
            Assert.That(counts[5].passed, Is.EqualTo(5));
            Assert.That(counts[4].met, Is.Zero);
        }

        [TestCase(1.09f, 65)] [TestCase(2.01f, 65)] [TestCase(1.2f, 49)] [TestCase(1.2f, 81)]
        [TestCase(1.6f, 65)] [TestCase(2f, 65)] [TestCase(1.2f, 50)] [TestCase(1.2f, 80)]
        public void LayoutRejectsAnyOtherValueIncludingFormerTrials(float gap, float distance)
        {
            var scenario = ScriptableObject.CreateInstance<LadderScenario>();
            try
            {
                scenario.LaneGap = gap; scenario.FirstSpawn.x = -distance * 8 / 13; scenario.SecondSpawn.x = distance * 5 / 13;
                Assert.Throws<System.ArgumentException>(() => scenario.Validate());
            }
            finally { Object.DestroyImmediate(scenario); }
        }

        [TestCase(10)]
        [TestCase(2000)]
        public void SeededOrdersKeepEveryUnitActiveAcrossTheWholeWindow(int count)
        {
            var scenario = AssetDatabase.LoadAssetAtPath<LadderScenario>("Assets/Data/PerformanceLadder.asset");
            var settings = AssetDatabase.LoadAssetAtPath<MovementSettings>("Assets/Data/MovementSettings.asset");
            using var simulation = new UnitSimulation(settings.Snapshot());
            using var batched = new UnitSimulation(settings.Snapshot());
            scenario.QueueUnits(simulation, count);
            scenario.QueueOrders(simulation);
            scenario.QueueUnits(batched, count);
            scenario.QueueOrders(batched);
            int warmup = (int)(scenario.WarmupSeconds / TickClock.TickDuration);
            int end = (int)((scenario.WarmupSeconds + scenario.WindowSeconds) / TickClock.TickDuration);
            int cycleTicks = (int)(scenario.OrderCycleSeconds / TickClock.TickDuration);
            int minimumOrders = count;
            var lastOrderTick = new int[count];
            var lastOrderVersion = new int[count];
            var warmupRenewals = new int[4];
            long allocated = 0;
            for (int tick = 1; tick <= end; tick++)
            {
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                if (tick > warmup) allocated += System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(simulation.Count, Is.EqualTo(count));
                Assert.That(simulation.MovingOrdersValid, Is.True, "Order was not renewed before the next movement step.");
                if (tick == warmup)
                    for (int rule = 1; rule <= 4; rule++) warmupRenewals[rule - 1] = simulation.RenewedOrderCount(rule);
                if (tick >= warmup)
                {
                    minimumOrders = System.Math.Min(minimumOrders, simulation.MovingOrderCount);
                }
                if ((tick - 1) % cycleTicks == 0)
                    for (int slot = 0; slot < count; slot++)
                    {
                        Side side = simulation.State.Sides[slot];
                        Vector3 destination = scenario.OrderDestination((tick - 1) / cycleTicks, side);
                        Assert.That(simulation.State.Movement[slot].Goal.x, Is.EqualTo(destination.x));
                        Assert.That(simulation.State.Movement[slot].Goal.y, Is.EqualTo(destination.z));
                    }
                for (int slot = 0; slot < count; slot++)
                {
                    var unit = simulation.State.Movement[slot];
                    if (unit.HasMoveOrder || unit.OrderVersion != lastOrderVersion[slot]) lastOrderTick[slot] = tick;
                    lastOrderVersion[slot] = unit.OrderVersion;
                    if (tick >= warmup && tick - lastOrderTick[slot] > 1)
                        Assert.Fail("Order gap longer than one tick: tick=" + tick + "; slot=" + slot);
                }
                if (tick % 2 == 0)
                {
                    batched.ScheduleFrame(2); batched.CompleteFrame();
                    Assert.That(batched.MovingOrdersValid, Is.True);
                    for (int rule = 1; rule <= 4; rule++)
                        Assert.That(batched.RenewedOrderCount(rule), Is.EqualTo(simulation.RenewedOrderCount(rule)));
                    if (tick % cycleTicks == 0 || tick == end)
                        for (int slot = 0; slot < count; slot++)
                        {
                            Assert.That(batched.State.Positions[slot], Is.EqualTo(simulation.State.Positions[slot]));
                            Assert.That(batched.State.Movement[slot], Is.EqualTo(simulation.State.Movement[slot]));
                        }
                }
                if (tick % cycleTicks == 0) TestContext.WriteLine("Tick=" + tick + "; orders=" + simulation.MovingOrderCount +
                    "; firstX=" + simulation.State.Positions[0].x + "; secondX=" + simulation.State.Positions[count / 2].x);
            }
            TestContext.WriteLine("Minimum orders=" + minimumOrders);
            for (int rule = 1; rule <= 4; rule++)
                TestContext.WriteLine("Renewed: rule=" + rule + "; measured=" + (simulation.RenewedOrderCount(rule) - warmupRenewals[rule - 1]) +
                    "; warmup=" + warmupRenewals[rule - 1]);
            Assert.That(allocated, Is.Zero, "Order cycles and renewals must allocate no managed bytes after warmup.");
            for (int cycle = 0; cycle <= 2; cycle++)
                for (int side = 0; side < 2; side++)
                {
                    LadderCycleCounts crossing = simulation.LadderCrossingCount(cycle, (Side)side);
                    TestContext.WriteLine("Crossing: " + JsonUtility.ToJson(crossing));
                    Assert.That(crossing.units, Is.EqualTo(count / 2));
                    Assert.That(crossing.passed, Is.InRange(0, crossing.units));
                    Assert.That(crossing.met, Is.InRange(0, crossing.units));
                    Assert.That(batched.LadderCrossingCount(cycle, (Side)side), Is.EqualTo(crossing));
                }
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void RenewalWaitsUntilOrderEndsAndUsesCurrentCycleDestination(int rule)
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            simulation.QueueSideMove(Side.First, Vector3.right * 20, 1, renewEnded: true);
            simulation.QueueSideMove(Side.First, Vector3.left * 20, 3, renewEnded: true);
            simulation.ScheduleFrame(2); simulation.CompleteFrame();
            Assert.That(simulation.State.Movement[0].OrderVersion, Is.EqualTo(2), "A live order must not be renewed early.");
            Assert.That(simulation.RenewedOrderCount(rule), Is.Zero);
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            var unit = simulation.State.Movement[0];
            unit.HasMoveOrder = false; unit.ArrivalRule = (byte)rule;
            simulation.State.Movement[0] = unit;
            simulation.ScheduleFrame(0); simulation.CompleteFrame();
            Assert.That(simulation.MovingOrderCount, Is.Zero, "No renewal between simulation ticks.");
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            Assert.That(simulation.MovingOrderCount, Is.EqualTo(1));
            Assert.That(simulation.MovingOrdersValid, Is.True);
            Assert.That(simulation.State.Movement[0].OrderVersion, Is.EqualTo(8));
            Assert.That(simulation.State.Movement[0].Goal.x, Is.EqualTo(-20));
            Assert.That(simulation.State.Movement[0].ArrivalRule, Is.Zero);
            Assert.That(simulation.RenewedOrderCount(rule), Is.EqualTo(1));
        }

        [Test]
        public void ActiveOrderCheckLatchesAnUnrenewedGapInsideABatchedFrame()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MovementSettings>("Assets/Data/MovementSettings.asset");
            using var simulation = new UnitSimulation(settings.Snapshot());
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            simulation.QueueSideMove(Side.First, Vector3.zero, 1, renewEnded: true);
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            Assert.That(simulation.MovingOrderCount, Is.Zero);
            Assert.That(simulation.MovingOrdersValid, Is.True, "An order may end on this tick.");
            simulation.QueueSideMove(Side.First, Vector3.zero, 2);
            simulation.QueueSideMove(Side.First, Vector3.left * 20, 4, renewEnded: true);
            simulation.ScheduleFrame(3); simulation.CompleteFrame();
            Assert.That(simulation.MovingOrderCount, Is.EqualTo(1), "The later cycle hides the gap from a render-only check.");
            Assert.That(simulation.MovingOrdersValid, Is.False);
        }

        [Test]
        public void SeedAndScenarioGenerateRepeatableOrdersIndependentOfFrameBatching()
        {
            var scenario = ScriptableObject.CreateInstance<LadderScenario>();
            try
            {
                using var first = new UnitSimulation();
                using var second = new UnitSimulation();
                foreach (var simulation in new[] { first, second })
                {
                    simulation.QueueBlock(Side.First, UnitSize.Small, scenario.FirstSpawn, 1);
                    simulation.QueueBlock(Side.Second, UnitSize.Large, scenario.SecondSpawn, 1);
                    scenario.QueueOrders(simulation);
                }
                for (int frame = 0; frame < 1250; frame++)
                {
                    first.ScheduleFrame(1); first.CompleteFrame();
                    first.ScheduleFrame(1); first.CompleteFrame();
                    second.ScheduleFrame(2); second.CompleteFrame();
                    for (int slot = 0; slot < 2; slot++)
                    {
                        Assert.That(first.State.Movement[slot].Goal, Is.EqualTo(second.State.Movement[slot].Goal));
                        Assert.That(first.State.Movement[slot].OrderVersion, Is.EqualTo(second.State.Movement[slot].OrderVersion));
                    }
                }
                Vector3 previous = scenario.OrderDestination(0, Side.First);
                scenario.Seed++;
                Assert.That(scenario.OrderDestination(0, Side.First), Is.Not.EqualTo(previous));
            }
            finally { Object.DestroyImmediate(scenario); }
        }

        [Test]
        public void FutureSideOrderPreservesAddResponseAndAppliesOnlyOnItsTick()
        {
            using var simulation = new UnitSimulation();
            Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero, out ulong request), Is.True);
            Assert.That(simulation.QueueSideMove(Side.First, Vector3.right * 20, 3), Is.True);
            for (int tick = 1; tick <= 3; tick++)
            {
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                Assert.That(simulation.TryGetAddedHandle(request, out _), Is.True);
                Assert.That(simulation.MovingOrderCount, Is.EqualTo(tick == 3 ? 1 : 0));
            }
        }

        [Test]
        public void DelayedOutOfOrderSamplesMatchTheRenderedFrame()
        {
            var buffer = new LadderFrameBuffer(5, 100);
            buffer.Rendered(200); buffer.Rendered(300); buffer.Rendered(400);
            buffer.Add(new FrameTiming { frameStartTimestamp = 350, cpuFrameTime = 3 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 1 });
            Assert.That(buffer.SampleCount(0), Is.EqualTo(1));
            Assert.That(buffer.SampleCount(1), Is.Zero, "A missing frame must remain missing.");
            Assert.That(buffer.Sample(2).cpuFrameTime, Is.EqualTo(3));
            Assert.That(buffer.Matched, Is.EqualTo(2));
            Assert.That(buffer.Unmatched, Is.Zero);
        }

        [Test]
        public void DifferentClockOriginsProduceUnmatchedSamples()
        {
            var buffer = new LadderFrameBuffer(2, 100);
            buffer.Rendered(200); buffer.Rendered(300);
            buffer.Add(new FrameTiming { frameStartTimestamp = 10150, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 10250, cpuFrameTime = 1 });
            Assert.That(buffer.Matched, Is.Zero);
            Assert.That(buffer.Unmatched, Is.EqualTo(2));
            Assert.That(buffer.SampleCount(0), Is.Zero);
            Assert.That(buffer.SampleCount(1), Is.Zero);
        }

        [Test]
        public void DrainMatchesDelayedWindowSamplesAndIgnoresFramesOutsideWindow()
        {
            var buffer = new LadderFrameBuffer(2, 100);
            buffer.Rendered(200); buffer.Rendered(300);
            buffer.Add(new FrameTiming { frameStartTimestamp = 350, cpuFrameTime = 1 });
            buffer.End();
            buffer.Add(new FrameTiming { frameStartTimestamp = 100, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 300, cpuFrameTime = 2 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 450, cpuFrameTime = 1 });
            Assert.That(buffer.Matched, Is.EqualTo(2));
            Assert.That(buffer.Unmatched, Is.EqualTo(1), "Closing the window must not hide earlier unmatched samples.");
            Assert.That(buffer.SampleCount(0), Is.EqualTo(1));
            Assert.That(buffer.SampleCount(1), Is.EqualTo(1));
            Assert.That(buffer.Invalid, Is.False);
        }

        [Test]
        public void DuplicateNonPositiveAndOverflowAreRejected()
        {
            var buffer = new LadderFrameBuffer(1, 100);
            buffer.Rendered(200);
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 1 });
            buffer.Add(new FrameTiming { frameStartTimestamp = 160, cpuFrameTime = 1 });
            Assert.That(buffer.SampleCount(0), Is.EqualTo(2));
            Assert.That(buffer.Matched, Is.EqualTo(2));
            Assert.That(buffer.Invalid, Is.True);
            buffer = new LadderFrameBuffer(1, 100);
            buffer.Rendered(200);
            buffer.Add(new FrameTiming { frameStartTimestamp = 150, cpuFrameTime = 0 });
            Assert.That(buffer.Invalid, Is.True);
            buffer = new LadderFrameBuffer(1, 100);
            buffer.Rendered(200); buffer.Rendered(300);
            Assert.That(buffer.Invalid, Is.True);
        }
    }
}
