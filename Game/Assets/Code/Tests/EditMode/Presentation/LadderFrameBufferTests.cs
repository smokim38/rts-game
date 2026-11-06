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
            Assert.That(simulation.QueueBlock(Side.First, scenario.FirstSize, scenario.FirstSpawn, count / 2), Is.True);
            Assert.That(simulation.QueueBlock(Side.Second, scenario.SecondSize, scenario.SecondSpawn, count / 2), Is.True);
            scenario.QueueOrders(simulation);
            int warmup = (int)(scenario.WarmupSeconds / TickClock.TickDuration);
            int end = (int)((scenario.WarmupSeconds + scenario.WindowSeconds) / TickClock.TickDuration);
            int cycleTicks = (int)(scenario.OrderCycleSeconds / TickClock.TickDuration);
            bool firstCrossed = false, secondCrossed = false;
            int firstMissingOrder = 0, minimumOrders = count;
            for (int tick = 1; tick <= end; tick++)
            {
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                Assert.That(simulation.Count, Is.EqualTo(count));
                if (tick >= warmup)
                {
                    minimumOrders = System.Math.Min(minimumOrders, simulation.MovingOrderCount);
                    if (simulation.MovingOrderCount != count && firstMissingOrder == 0)
                    {
                        firstMissingOrder = tick;
                        for (int slot = 0; slot < count; slot++)
                            if (!simulation.State.Movement[slot].HasMoveOrder)
                            {
                                TestContext.WriteLine("First missing order: tick=" + tick + "; side=" + simulation.State.Sides[slot] +
                                    "; arrivalRule=" + simulation.State.Movement[slot].ArrivalRule);
                                break;
                            }
                    }
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
                    if (simulation.State.Sides[slot] == Side.First) firstCrossed |= simulation.State.Positions[slot].x > 0;
                    else secondCrossed |= simulation.State.Positions[slot].x < 0;
                }
                if (tick % cycleTicks == 0) TestContext.WriteLine("Tick=" + tick + "; orders=" + simulation.MovingOrderCount +
                    "; firstX=" + simulation.State.Positions[0].x + "; secondX=" + simulation.State.Positions[count / 2].x);
            }
            TestContext.WriteLine("Minimum orders=" + minimumOrders + "; firstCrossed=" + firstCrossed + "; secondCrossed=" + secondCrossed);
            Assert.That(firstMissingOrder, Is.Zero, "Every unit must retain an order throughout measurement.");
            Assert.That(firstCrossed && secondCrossed, Is.True, "Both factions must cross the middle.");
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
