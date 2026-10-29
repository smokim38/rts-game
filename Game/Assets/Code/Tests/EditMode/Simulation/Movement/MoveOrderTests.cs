using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using NUnit.Framework;
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

        [TestCase(true)] [TestCase(false)]
        public void ClickQueuesSnapshotAndNextTickStartsEverySelectedUnit(bool classic)
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 100); Frame(simulation, 1);
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
                if (math.lengthsq(simulation.State.Movement[i].Velocity) > 0) started++;
            }
            Assert.That(started, Is.EqualTo(100), "Units with velocity on the first tick");
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

        [Test]
        public void HundredUnitsComeToRestWithoutAnyTickAddingFootprintOverlap()
        {
            using var simulation = new UnitSimulation(Settings());
            simulation.QueueBlock(Side.First, UnitSize.Small, Vector3.zero, 100); Frame(simulation, 1);
            SelectAll(simulation); Order(simulation, new Vector3(15, 0, 0));
            for (int tick = 0; tick < 1200; tick++)
            {
                Frame(simulation, 1);
                for (int i = 0; i < 100; i++)
                    for (int j = i + 1; j < 100; j++)
                        if (Footprint.Overlap(new MovementPose { Position = simulation.State.Positions[i], Heading = simulation.State.Movement[i].Heading },
                            UnitSize.Small, new MovementPose { Position = simulation.State.Positions[j], Heading = simulation.State.Movement[j].Heading },
                            UnitSize.Small) > 0) Assert.Fail("tick/pair " + tick + "/" + i + "/" + j);
            }
            for (int i = 0; i < 100; i++) Assert.That(math.lengthsq(simulation.State.Movement[i].Velocity), Is.LessThan(1e-8), "unit " + i);
            Assert.That(simulation.MoveMarkerPosition.x, Is.EqualTo(15));
        }
    }
}
