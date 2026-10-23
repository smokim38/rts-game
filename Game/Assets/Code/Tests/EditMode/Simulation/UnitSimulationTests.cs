using System;
using Northwatch.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation
{
    public sealed class UnitSimulationTests
    {
        private static void Tick(UnitSimulation simulation, int ticks = 1)
        {
            simulation.ScheduleFrame(ticks);
            simulation.CompleteFrame();
        }

        [Test]
        public void AddsOnlyAtTickStartAndRefusesBeyondCapacity()
        {
            using var simulation = new UnitSimulation();
            for (int i = 0; i < UnitSimulation.Capacity; i++)
                Assert.That(simulation.QueueAdd((Side)(i % 2), UnitSize.Small, new Vector3(i % 100, 0, i / 100)), Is.True);
            Assert.That(simulation.QueueAdd(Side.First, UnitSize.Large, Vector3.zero), Is.False);
            Tick(simulation, 0);
            Assert.That(simulation.Count, Is.Zero);
            Tick(simulation);
            Assert.That(simulation.Count, Is.EqualTo(UnitSimulation.Capacity));
            Assert.That(simulation.DrawCount(Side.First), Is.EqualTo(1000));
            Assert.That(simulation.DrawCount(Side.Second), Is.EqualTo(1000));
            Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero), Is.False);
        }

        [Test]
        public void RemovalInvalidatesHandleAndReusesSlotWithNewGeneration()
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            Tick(simulation);
            var oldHandle = new UnitHandle(0, simulation.State.Generations[0]);
            Assert.That(simulation.IsAlive(oldHandle), Is.True);
            Assert.That(simulation.QueueRemove(oldHandle), Is.True);
            Assert.That(simulation.IsAlive(oldHandle), Is.True);
            Tick(simulation);
            Assert.That(simulation.Count, Is.Zero);
            Assert.That(simulation.IsAlive(oldHandle), Is.False);
            simulation.QueueAdd(Side.Second, UnitSize.Large, Vector3.one);
            Tick(simulation);
            Assert.That(simulation.IsAlive(oldHandle), Is.False);
            Assert.That(simulation.QueueRemove(oldHandle), Is.False);
            Assert.That(simulation.IsAlive(new UnitHandle(0, simulation.State.Generations[0])), Is.True);
            Assert.That(simulation.IsAlive(default), Is.False);
            Assert.That(simulation.IsAlive(new UnitHandle(-1, 1)), Is.False);
        }

        [Test]
        public void DrawDataGroupsSidesAndUsesSizeAndGroundPosition()
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.Second, UnitSize.Large, new Vector3(4, 0, 6));
            simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(2, 1, 3));
            Tick(simulation);
            Assert.That(simulation.DrawCount(Side.First), Is.EqualTo(1));
            Assert.That(simulation.DrawCount(Side.Second), Is.EqualTo(1));
            var matrices = simulation.DrawMatrices;
            Assert.That(matrices[0], Is.EqualTo(Matrix4x4.TRS(new Vector3(2, 1.25f, 3), Quaternion.identity, Vector3.one * 0.5f)));
            Assert.That(matrices[UnitSimulation.Capacity], Is.EqualTo(Matrix4x4.TRS(new Vector3(4, 0.5f, 6), Quaternion.identity, Vector3.one)));
            simulation.QueueRemove(new UnitHandle(0, simulation.State.Generations[0]));
            Tick(simulation);
            Assert.That(simulation.DrawCount(Side.Second), Is.Zero);
        }

        [Test]
        public void StaleQueuedRemovalCannotRemoveReusedSlotAndQueueIsBounded()
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            Tick(simulation);
            var oldHandle = new UnitHandle(0, simulation.State.Generations[0]);
            simulation.QueueRemove(oldHandle);
            simulation.QueueAdd(Side.Second, UnitSize.Large, Vector3.one);
            simulation.QueueRemove(oldHandle);
            Tick(simulation, 3);
            Assert.That(simulation.Count, Is.EqualTo(1));
            Assert.That(simulation.IsAlive(oldHandle), Is.False);
            var current = new UnitHandle(0, simulation.State.Generations[0]);
            for (int i = 0; i < UnitSimulation.Capacity * 2; i++) Assert.That(simulation.QueueRemove(current), Is.True);
            Assert.That(simulation.QueueRemove(current), Is.False);
            Tick(simulation);
            Assert.That(simulation.Count, Is.Zero);
        }

        [Test]
        public void RejectsInvalidCommandsAndAccessWhileJobsAreScheduled()
        {
            using var simulation = new UnitSimulation();
            Assert.That(simulation.QueueAdd((Side)2, UnitSize.Small, Vector3.zero), Is.False);
            Assert.That(simulation.QueueAdd(Side.First, (UnitSize)2, Vector3.zero), Is.False);
            Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(float.NaN, 0, 0)), Is.False);
            simulation.ScheduleFrame(1);
            Assert.Throws<InvalidOperationException>(() => simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero));
            Assert.Throws<InvalidOperationException>(() => { _ = simulation.DrawMatrices; });
            simulation.CompleteFrame();
            Assert.That(simulation.Count, Is.Zero);
        }

        [Test]
        public void TwoThousandUnitTicksAllocateNoManagedBytesAfterWarmup()
        {
            using var simulation = new UnitSimulation();
            for (int i = 0; i < UnitSimulation.Capacity; i++) simulation.QueueAdd((Side)(i % 2), UnitSize.Small, Vector3.zero);
            Tick(simulation);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++) Tick(simulation);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.That(allocated, Is.Zero);
            Assert.That(simulation.Count, Is.EqualTo(2000));
        }
    }
}
