using Northwatch.Simulation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation
{
    public sealed class BlockPlacementTests
    {
        private static void Tick(UnitSimulation simulation)
        {
            simulation.ScheduleFrame(1);
            simulation.CompleteFrame();
        }

        [TestCase(UnitSize.Small, 1, 0, 0)]
        [TestCase(UnitSize.Small, 7, 0, 0)]
        [TestCase(UnitSize.Large, 37, 127.9f, -127.9f)]
        [TestCase(UnitSize.Large, 37, -127.9f, -127.9f)]
        [TestCase(UnitSize.Small, 2000, -128, 128)]
        [TestCase(UnitSize.Large, 2000, 128, 128)]
        public void PlacesExactCountWithoutOverlapInsideMap(UnitSize size, int count, float x, float z)
        {
            using var simulation = new UnitSimulation();
            Assert.That(simulation.QueueBlock(Side.Second, size, new Vector3(x, 5, z), count), Is.True);
            Assert.That(simulation.Count, Is.Zero, "Orders apply only at a tick.");
            Tick(simulation);
            Assert.That(simulation.Count, Is.EqualTo(count));
            Assert.That(simulation.PlacementResult, Is.EqualTo(count));
            Assert.That(simulation.DrawCount(Side.Second), Is.EqualTo(count));
            AssertPlacement(simulation);
            float half = (size == UnitSize.Small ? 0.5f : 1f) * 0.5f;
            float2 minimum = new float2(float.MaxValue), maximum = new float2(float.MinValue);
            for (int i = 0; i < count; i++)
            {
                float2 position = simulation.State.Positions[i].xz;
                minimum = math.min(minimum, position - half);
                maximum = math.max(maximum, position + half);
            }
            if (x == 0 && z == 0) Assert.That(math.length((minimum + maximum) * 0.5f), Is.LessThan(0.0001f));
            else
            {
                Assert.That(x > 0 ? maximum.x : minimum.x, Is.EqualTo(x > 0 ? 128f : -128f));
                Assert.That(z > 0 ? maximum.y : minimum.y, Is.EqualTo(z > 0 ? 128f : -128f));
            }
        }

        [TestCase(0, 2001)]
        [TestCase(1, 2000)]
        [TestCase(2000, 1)]
        public void CommandJobRefusesWholeBlockBeyondStateCapacity(int existingCount, int blockCount)
        {
            using var simulation = new UnitSimulation();
            if (existingCount > 0)
                Assert.That(simulation.QueueBlock(Side.First, UnitSize.Small, new Vector3(-40, 0, 0), existingCount), Is.True);
            Tick(simulation);
            int version = simulation.PlacementVersion;
            using var commands = new NativeArray<UnitCommand>(new[] { new UnitCommand
                { Side = Side.Second, Size = UnitSize.Large, Position = new float3(40, 0, 0), BlockCount = blockCount } }, Allocator.Persistent);
            new ApplyCommandsJob { State = simulation.State, Commands = commands, CommandCount = 1 }.Schedule().Complete();
            Assert.That(simulation.Count, Is.EqualTo(existingCount));
            Assert.That(simulation.PlacementVersion, Is.EqualTo(version + 1));
            Assert.That(simulation.PlacementResult, Is.EqualTo(-1));
            Assert.That(simulation.State.Summary[1], Is.EqualTo(existingCount), "Refusal must not consume any slots.");
            if (existingCount < UnitSimulation.Capacity) Assert.That(simulation.State.Alive[existingCount], Is.Zero);
        }

        [Test]
        public void TwoPendingThousandsFillEitherOneSideOrBothAndCapRefusalIsAtomic()
        {
            foreach (Side second in new[] { Side.First, Side.Second })
            {
                using var simulation = new UnitSimulation();
                Assert.That(simulation.QueueBlock(Side.First, UnitSize.Small, new Vector3(-40, 0, 0), 1000), Is.True);
                Assert.That(simulation.RemainingCapacity, Is.EqualTo(1000));
                Assert.That(simulation.QueueBlock(second, UnitSize.Large, new Vector3(40, 0, 0), 1001), Is.False);
                Assert.That(simulation.RemainingCapacity, Is.EqualTo(1000));
                Assert.That(simulation.QueueBlock(second, UnitSize.Large, new Vector3(40, 0, 0), 1000), Is.True);
                Assert.That(simulation.RemainingCapacity, Is.Zero);
                Assert.That(simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 1), Is.False);
                Tick(simulation);
                Assert.That(simulation.Count, Is.EqualTo(2000));
                Assert.That(simulation.DrawCount(Side.First), Is.EqualTo(second == Side.First ? 2000 : 1000));
                Assert.That(simulation.QueueBlock(Side.Second, UnitSize.Small, Vector3.zero, 1), Is.False);
                AssertPlacement(simulation);
            }
        }

        [Test]
        public void RefusesOccupiedBlockIncludingMixedWidthsAndPartialLastRow()
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.First, UnitSize.Large, new Vector3(1.1f, 0, 0.3f));
            simulation.QueueBlock(Side.Second, UnitSize.Small, Vector3.zero, 7);
            Tick(simulation);
            Assert.That(simulation.Count, Is.EqualTo(1));
            Assert.That(simulation.PlacementResult, Is.EqualTo(-1));
            Assert.That(simulation.RemainingCapacity, Is.EqualTo(1999));
            simulation.QueueBlock(Side.Second, UnitSize.Large, new Vector3(20, 0, 20), 7);
            simulation.QueueBlock(Side.First, UnitSize.Small, new Vector3(20, 0, 20), 1);
            Tick(simulation);
            Assert.That(simulation.Count, Is.EqualTo(8));
            Assert.That(simulation.PlacementResult, Is.EqualTo(-1));
            AssertPlacement(simulation);
        }

        [Test]
        public void RejectsInvalidCountsPositionsSidesAndSizesWithoutQueuing()
        {
            using var simulation = new UnitSimulation();
            foreach (int count in new[] { -1, 0, 2001 })
                Assert.That(simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, count), Is.False);
            foreach (float x in new[] { -128.1f, 128.1f, float.NaN, float.PositiveInfinity })
                Assert.That(simulation.QueueBlock(Side.First, UnitSize.Small, new Vector3(x, 0, 0), 1), Is.False);
            Assert.That(simulation.QueueBlock((Side)2, UnitSize.Small, Vector3.zero, 1), Is.False);
            Assert.That(simulation.QueueBlock(Side.First, (UnitSize)2, Vector3.zero, 1), Is.False);
            Tick(simulation);
            Assert.That(simulation.Count, Is.Zero);
        }

        private static void AssertPlacement(UnitSimulation simulation)
        {
            for (int a = 0; a < simulation.Count; a++)
            {
                float3 position = simulation.State.Positions[a];
                float half = simulation.State.Sizes[a] == UnitSize.Small ? 0.25f : 0.5f;
                Assert.That(position.y, Is.Zero);
                Assert.That(math.cmax(math.abs(position.xz)) + half, Is.LessThanOrEqualTo(128));
                for (int b = 0; b < a; b++)
                {
                    float clearance = half + (simulation.State.Sizes[b] == UnitSize.Small ? 0.25f : 0.5f);
                    float2 distance = math.abs(position.xz - simulation.State.Positions[b].xz);
                    if (math.all(distance < clearance)) Assert.Fail("Cubes overlap.");
                }
            }
        }
    }
}
