using System;
using Northwatch.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation
{
    public sealed class SelectionTests
    {
        // Project X/Z onto the viewport, independent of cube height.
        private static readonly Matrix4x4 Projection = new Matrix4x4 { m00 = 1, m12 = 1, m33 = 1 };
        private static SelectionRequest Box(bool shift = false) =>
            SelectionRequest.Box(Projection, Rect.MinMaxRect(0.25f, 0.25f, 0.75f, 0.75f), shift);
        private static SelectionRequest Click(float x, bool shift = false) =>
            SelectionRequest.Click(new Ray(new Vector3(x, 10, 0), Vector3.down), shift);
        private static void Frame(UnitSimulation simulation, int ticks = 0)
        { simulation.ScheduleFrame(ticks); simulation.CompleteFrame(); }
        private static UnitHandle Add(UnitSimulation simulation, Vector3 position, Side side = Side.First)
        {
            Assert.That(simulation.QueueAdd(side, UnitSize.Small, position, out ulong request), Is.True);
            Frame(simulation, 1);
            Assert.That(simulation.TryGetAddedHandle(request, out UnitHandle handle), Is.True);
            return handle;
        }

        [Test]
        public void BoxIncludesCentresOnEveryEdgeAndExcludesOutsideAndEnemy()
        {
            using var simulation = new UnitSimulation();
            Vector3[] positions = { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f),
                new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f), Vector3.zero,
                new Vector3(0.5001f, 0, 0), new Vector3(0, 0, -0.5001f) };
            var handles = new UnitHandle[positions.Length];
            for (int i = 0; i < positions.Length; i++) handles[i] = Add(simulation, positions[i]);
            UnitHandle enemy = Add(simulation, Vector3.zero, Side.Second);
            simulation.QueueSelection(Box()); Frame(simulation);
            for (int i = 0; i < handles.Length; i++) Assert.That(simulation.IsSelected(handles[i]), Is.EqualTo(i < 5));
            Assert.That(simulation.IsSelected(enemy), Is.False);
            Assert.That(simulation.SelectedCount, Is.EqualTo(5));
            Assert.That(simulation.DrawCount(Side.First), Is.EqualTo(7));
            Assert.That(simulation.DrawMatrices[UnitSimulation.Capacity * 2].m03, Is.EqualTo(-0.5f));
            // Zero-area boxes retain a centre exactly on their boundary.
            simulation.QueueSelection(SelectionRequest.Box(Projection, new Rect(0.5f, 0.5f, 0, 0))); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(1));
            Assert.That(simulation.IsSelected(handles[4]), Is.True);
        }

        [Test]
        public void ClickReplacesShiftClickTogglesAndShiftBoxOnlyAdds()
        {
            using var simulation = new UnitSimulation();
            UnitHandle first = Add(simulation, Vector3.zero);
            UnitHandle second = Add(simulation, new Vector3(3, 0, 0));
            Add(simulation, new Vector3(6, 0, 0), Side.Second);
            simulation.QueueSelection(Click(0)); Frame(simulation);
            Assert.That(simulation.IsSelected(first), Is.True);
            simulation.QueueSelection(Click(3, true)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(2));
            simulation.QueueSelection(Click(0, true)); Frame(simulation);
            Assert.That(simulation.IsSelected(first), Is.False);
            Assert.That(simulation.IsSelected(second), Is.True);
            simulation.QueueSelection(Box(true)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(2));
            simulation.QueueSelection(Box(true)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(2));
            simulation.QueueSelection(Click(6, true)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(2));
            simulation.QueueSelection(Click(3)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(1));
            Assert.That(simulation.IsSelected(second), Is.True);
            simulation.QueueSelection(Click(6)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.Zero);
            simulation.QueueSelection(Click(0)); Frame(simulation);
            simulation.QueueSelection(Click(10)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.Zero);
        }

        [Test]
        public void PickingUsesCubeExtentsAndNearestHitIncludingEnemyOcclusion()
        {
            using var simulation = new UnitSimulation();
            UnitHandle own = Add(simulation, Vector3.zero);
            simulation.QueueSelection(Click(0.24f)); Frame(simulation);
            Assert.That(simulation.IsSelected(own), Is.True);
            simulation.QueueSelection(Click(0.26f)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.Zero);
            Add(simulation, new Vector3(0, 2, 0), Side.Second);
            simulation.QueueSelection(Click(0)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.Zero);
        }

        [Test]
        public void AllNineGroupsSaveRecallOverwriteAndPruneRemovedGenerations()
        {
            using var simulation = new UnitSimulation();
            UnitHandle first = Add(simulation, Vector3.zero);
            UnitHandle second = Add(simulation, new Vector3(3, 0, 0));
            simulation.QueueSelection(Click(0)); simulation.QueueSelection(Click(3, true)); Frame(simulation);
            for (int group = 1; group <= 9; group++) simulation.QueueSelection(SelectionRequest.Group(group, true));
            Frame(simulation);
            simulation.QueueRemove(first); Frame(simulation, 1);
            Assert.That(simulation.SelectedCount, Is.EqualTo(1));
            UnitHandle replacement = Add(simulation, Vector3.zero);
            Assert.That(replacement.Slot, Is.EqualTo(first.Slot));
            for (int group = 1; group <= 9; group++)
            {
                simulation.QueueSelection(Click(10)); simulation.QueueSelection(SelectionRequest.Group(group, false)); Frame(simulation);
                Assert.That(simulation.SelectedCount, Is.EqualTo(1));
                Assert.That(simulation.IsSelected(second), Is.True);
                Assert.That(simulation.IsSelected(replacement), Is.False);
                Assert.That(simulation.SelectedCentre, Is.EqualTo(new Vector3(3, 0, 0)));
            }
            simulation.QueueSelection(Click(10)); simulation.QueueSelection(SelectionRequest.Group(1, true));
            simulation.QueueSelection(Click(0)); simulation.QueueSelection(SelectionRequest.Group(1, false)); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.Zero, "Saving empty selection replaces a group.");
        }

        [Test]
        public void TwoThousandSelectedUnitsHaveDrawDataInSameFrameAndAllocateZeroAfterWarmup()
        {
            using var simulation = new UnitSimulation();
            simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 2000); Frame(simulation, 1);
            var all = SelectionRequest.Box(Projection, new Rect(-100, -100, 200, 200));
            for (int i = 0; i < 20; i++) { simulation.QueueSelection(all); Frame(simulation); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                simulation.QueueSelection(all);
                simulation.QueueSelection(SelectionRequest.Group(9, true));
                simulation.QueueSelection(SelectionRequest.Group(9, false));
                Frame(simulation);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            Assert.That(simulation.SelectedCount, Is.EqualTo(2000));
            Assert.That(simulation.DrawMatrices[UnitSimulation.Capacity * 3 - 1].m33, Is.EqualTo(1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => SelectionRequest.Group(0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => SelectionRequest.Group(10, true));
            Assert.That(simulation.QueueSelection(default), Is.False);
        }

        [Test]
        public void SameTypeClickFiltersByOwnershipSizeAndVisibleCentreAndShiftAdds()
        {
            using var simulation = new UnitSimulation();
            UnitHandle first = Add(simulation, Vector3.zero);
            UnitHandle second = Add(simulation, new Vector3(0.8f, 0, 0));
            UnitHandle offscreen = Add(simulation, new Vector3(1.1f, 0, 0));
            UnitHandle enemy = Add(simulation, new Vector3(-0.5f, 0, 0), Side.Second);
            simulation.QueueAdd(Side.First, UnitSize.Large, new Vector3(-0.8f, 0, 0), out ulong request);
            Frame(simulation, 1);
            simulation.TryGetAddedHandle(request, out UnitHandle large);
            void DoubleClick(bool shift) => simulation.QueueSelection(SelectionRequest.PointerClick(
                new Ray(new Vector3(0, 10, 0), Vector3.down), Projection, true, true, false, shift, true));
            simulation.QueueSelection(Click(-0.8f)); Frame(simulation);
            DoubleClick(true); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(3));
            Assert.That(simulation.IsSelected(large), Is.True);
            DoubleClick(false); Frame(simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(2));
            Assert.That(simulation.IsSelected(first) && simulation.IsSelected(second), Is.True);
            Assert.That(simulation.IsSelected(offscreen) || simulation.IsSelected(enemy) || simulation.IsSelected(large), Is.False);
            for (int i = 0; i < 20; i++) { DoubleClick(true); Frame(simulation); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { DoubleClick(true); Frame(simulation); }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }
    }
}
