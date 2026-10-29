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
            Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero, out ulong request), Is.True);
            Assert.That(simulation.TryGetAddedHandle(request, out _), Is.False);
            Tick(simulation, 0);
            Assert.That(simulation.TryGetAddedHandle(request, out _), Is.False);
            Tick(simulation);
            Assert.That(simulation.TryGetAddedHandle(request, out UnitHandle oldHandle), Is.True);
            Assert.That(simulation.IsAlive(oldHandle), Is.True);
            Assert.That(simulation.QueueRemove(oldHandle), Is.True);
            Assert.That(simulation.IsAlive(oldHandle), Is.True);
            Tick(simulation);
            Assert.That(simulation.Count, Is.Zero);
            Assert.That(simulation.IsAlive(oldHandle), Is.False);
            Assert.That(simulation.QueueAdd(Side.Second, UnitSize.Large, Vector3.one, out ulong replacement), Is.True);
            Tick(simulation);
            Assert.That(simulation.IsAlive(oldHandle), Is.False);
            Assert.That(simulation.QueueRemove(oldHandle), Is.False);
            Assert.That(simulation.TryGetAddedHandle(replacement, out UnitHandle current), Is.True);
            Assert.That(current.Slot, Is.EqualTo(oldHandle.Slot));
            Assert.That(current.Generation, Is.Not.EqualTo(oldHandle.Generation));
            Assert.That(simulation.IsAlive(current), Is.True);
            Assert.That(simulation.TryGetAddedHandle(request, out _), Is.False);
            Assert.That(simulation.IsAlive(default), Is.False);
            Assert.That(simulation.IsAlive(new UnitHandle(-1, 1)), Is.False);
        }

        [Test]
        public void DrawDataGroupsSidesAndUsesSizeAndGroundPosition()
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.Second, UnitSize.Large, new Vector3(4, 0, 6), out ulong request);
            simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(2, 1, 3));
            Tick(simulation);
            Assert.That(simulation.DrawCount(Side.First), Is.EqualTo(1));
            Assert.That(simulation.DrawCount(Side.Second), Is.EqualTo(1));
            var matrices = simulation.DrawMatrices;
            Assert.That(matrices[0], Is.EqualTo(Matrix4x4.TRS(new Vector3(2, 1.25f, 3), Quaternion.identity, Vector3.one * 0.5f)));
            Assert.That(matrices[UnitSimulation.Capacity], Is.EqualTo(Matrix4x4.TRS(new Vector3(4, 0.5f, 6), Quaternion.identity, Vector3.one)));
            Assert.That(simulation.TryGetAddedHandle(request, out UnitHandle handle), Is.True);
            simulation.QueueRemove(handle);
            Tick(simulation);
            Assert.That(simulation.DrawCount(Side.Second), Is.Zero);
        }

        [Test]
        public void StaleQueuedRemovalCannotRemoveReusedSlotAndQueueIsBounded()
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero, out ulong request);
            Tick(simulation);
            Assert.That(simulation.TryGetAddedHandle(request, out UnitHandle oldHandle), Is.True);
            simulation.QueueRemove(oldHandle);
            simulation.QueueAdd(Side.Second, UnitSize.Large, Vector3.one, out ulong replacement);
            simulation.QueueRemove(oldHandle);
            Tick(simulation, 3);
            Assert.That(simulation.Count, Is.EqualTo(1));
            Assert.That(simulation.IsAlive(oldHandle), Is.False);
            Assert.That(simulation.TryGetAddedHandle(replacement, out UnitHandle current), Is.True);
            for (int i = 0; i < UnitSimulation.Capacity * 2; i++) Assert.That(simulation.QueueRemove(current), Is.True);
            Assert.That(simulation.QueueRemove(current), Is.False);
            Tick(simulation);
            Assert.That(simulation.Count, Is.Zero);
        }

        [Test]
        public void AddedHandlesMatchRequestsAndOverwrittenRequestsAreRejected()
        {
            using var simulation = new UnitSimulation();
            Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero, out ulong first), Is.True);
            Assert.That(simulation.QueueAdd(Side.Second, UnitSize.Large, Vector3.one, out ulong second), Is.True);
            Assert.That(simulation.TryGetAddedHandle(0, out _), Is.False);
            Tick(simulation);
            Assert.That(simulation.TryGetAddedHandle(first, out UnitHandle firstHandle), Is.True);
            Assert.That(simulation.TryGetAddedHandle(second, out UnitHandle secondHandle), Is.True);
            Assert.That(firstHandle.Slot, Is.Not.EqualTo(secondHandle.Slot));
            Assert.That(simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero, out ulong third), Is.True);
            Assert.That(simulation.TryGetAddedHandle(first, out _), Is.False);
            Assert.That(simulation.TryGetAddedHandle(third, out _), Is.False);
            Tick(simulation);
            Assert.That(simulation.TryGetAddedHandle(first, out _), Is.False);
            Assert.That(simulation.IsAlive(firstHandle), Is.True);
            Assert.That(simulation.TryGetAddedHandle(third, out UnitHandle thirdHandle), Is.True);
            Assert.That(thirdHandle.Slot, Is.Not.EqualTo(firstHandle.Slot));
            Assert.That(simulation.QueueAdd((Side)2, UnitSize.Small, Vector3.zero, out ulong rejected), Is.False);
            Assert.That(rejected, Is.Zero);
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
        public void PendingFrameWritesTheOtherDrawBufferAndPublishesItAtCompletion()
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            Tick(simulation);
            var previousDraw = simulation.DrawMatrices;
            Matrix4x4 previous = previousDraw[0];
            simulation.State.Positions[0] = new Unity.Mathematics.float3(9, 0, 0);
            var unit = simulation.State.Movement[0];
            unit.Previous.Position = simulation.State.Positions[0];
            simulation.State.Movement[0] = unit;
            simulation.ScheduleFrame(0);
            Assert.That(previousDraw[0], Is.EqualTo(previous));
            simulation.CompleteFrame();
            Assert.That(simulation.DrawMatrices.Equals(previousDraw), Is.False);
            Assert.That(simulation.DrawMatrices[0].m03, Is.EqualTo(9));
            Assert.That(previousDraw[0], Is.EqualTo(previous));
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
