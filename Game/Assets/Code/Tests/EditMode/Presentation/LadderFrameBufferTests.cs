using Northwatch.Presentation.Diagnostics;
using Northwatch.Presentation;
using Northwatch.Simulation;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class LadderFrameBufferTests
    {
        [TestCase(10)]
        [TestCase(2000)]
        public void SeededOrdersKeepEveryUnitActiveAcrossTheWholeWindow(int count)
        {
            var scenario = AssetDatabase.LoadAssetAtPath<LadderScenario>("Assets/Data/PerformanceLadder.asset");
            var settings = AssetDatabase.LoadAssetAtPath<MovementSettings>("Assets/Data/MovementSettings.asset");
            using var simulation = new UnitSimulation(settings.Snapshot());
            using var batched = new UnitSimulation(settings.Snapshot());
            Assert.That(simulation.QueueBlock(Side.First, scenario.FirstSize, scenario.FirstSpawn, count / 2), Is.True);
            Assert.That(simulation.QueueBlock(Side.Second, scenario.SecondSize, scenario.SecondSpawn, count / 2), Is.True);
            scenario.QueueOrders(simulation);
            batched.QueueBlock(Side.First, scenario.FirstSize, scenario.FirstSpawn, count / 2);
            batched.QueueBlock(Side.Second, scenario.SecondSize, scenario.SecondSpawn, count / 2);
            scenario.QueueOrders(batched);
            int warmup = (int)(scenario.WarmupSeconds / TickClock.TickDuration);
            int end = (int)((scenario.WarmupSeconds + scenario.WindowSeconds) / TickClock.TickDuration);
            int cycleTicks = (int)(scenario.OrderCycleSeconds / TickClock.TickDuration);
            bool firstCrossed = false, secondCrossed = false;
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
                    if (simulation.State.Sides[slot] == Side.First) firstCrossed |= simulation.State.Positions[slot].x > 0;
                    else secondCrossed |= simulation.State.Positions[slot].x < 0;
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
            TestContext.WriteLine("Minimum orders=" + minimumOrders + "; firstCrossed=" + firstCrossed + "; secondCrossed=" + secondCrossed);
            for (int rule = 1; rule <= 4; rule++)
                TestContext.WriteLine("Renewed: rule=" + rule + "; measured=" + (simulation.RenewedOrderCount(rule) - warmupRenewals[rule - 1]) +
                    "; warmup=" + warmupRenewals[rule - 1]);
            if (count == 2000) Assert.That(simulation.RenewedOrderCount(4), Is.GreaterThan(0), "Group stall must remain enabled.");
            Assert.That(allocated, Is.Zero, "Order cycles and renewals must allocate no managed bytes after warmup.");
            Assert.That(firstCrossed && secondCrossed, Is.True, "Both factions must cross the middle.");
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
