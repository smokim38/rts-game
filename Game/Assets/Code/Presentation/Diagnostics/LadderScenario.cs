using Northwatch.Simulation;
using UnityEngine;

namespace Northwatch.Presentation.Diagnostics
{
    public sealed class LadderScenario : ScriptableObject
    {
        public int Version = 4;
        public int Seed = 103;
        public float PlaneSize = UnitSimulation.MapHalfWidth * 2;
        public Vector3 FirstSpawn = new Vector3(-40, 0, 0);
        public Vector3 SecondSpawn = new Vector3(25, 0, 0);
        public UnitSize FirstSize = UnitSize.Small;
        public UnitSize SecondSize = UnitSize.Large;
        public float OrderCycleSeconds = 20;
        public Vector3 FirstOrderEnd = new Vector3(-120, 0, 0);
        public Vector3 SecondOrderEnd = new Vector3(120, 0, 0);
        public float OrderSpread = 7;
        public float LaneGap = 1.2f;
        public float MeetDistance = 2;
        public int MeetSampleTicks = 15;
        public Vector3 CameraAngles = new Vector3(35.26439f, 45, 0);
        public float CameraDistance = 400;
        public float CameraSize = 200;
        public Vector3 LightAngles = new Vector3(50, -30, 0);
        public Color Ambient = new Color(0.5f, 0.5f, 0.5f);
        public float LightIntensity = 1;
        public double WarmupSeconds = 10;
        public double WindowSeconds = 60;
        public int FrameCapacity = 1000000;
        public double DrainSeconds = 2;

        public Vector3 OrderDestination(int cycle, Side side, float centreZ)
        {
            var random = Unity.Mathematics.Random.CreateFromIndex(unchecked((uint)Seed + (uint)cycle * 2 + (uint)side));
            bool firstEnd = ((cycle + (int)side) & 1) != 0;
            Vector3 destination = firstEnd ? FirstOrderEnd : SecondOrderEnd;
            destination.x += random.NextFloat(-OrderSpread, OrderSpread);
            destination.z = centreZ;
            return destination;
        }

        public void QueueUnits(UnitSimulation simulation, int count)
        {
            Validate();
            if (!simulation.QueueLaneBlock(Side.First, FirstSize, FirstSpawn, count / 2, LaneGap) ||
                !simulation.QueueLaneBlock(Side.Second, SecondSize, SecondSpawn, count / 2, LaneGap))
                throw new System.InvalidOperationException("Ladder blocks could not be queued.");
        }

        public void Validate()
        {
            if (Version != 4 || OrderCycleSeconds != 20 || OrderSpread != 7 ||
                LaneGap != 1.2f || !FirstSpawn.Equals(new Vector3(-40, 0, 0)) || !SecondSpawn.Equals(new Vector3(25, 0, 0)) ||
                !FirstOrderEnd.Equals(new Vector3(-120, 0, 0)) || !SecondOrderEnd.Equals(new Vector3(120, 0, 0)) ||
                FirstSize != UnitSize.Small || SecondSize != UnitSize.Large || MeetDistance != 2 || MeetSampleTicks != 15)
                throw new System.ArgumentException("Ladder layout violates ADR 0019.");
        }

        public void QueueOrders(UnitSimulation simulation, int count)
        {
            Validate();
            double cycleTicks = OrderCycleSeconds / TickClock.TickDuration;
            double endTicks = (WarmupSeconds + WindowSeconds + DrainSeconds) / TickClock.TickDuration;
            if (!double.IsFinite(cycleTicks) || cycleTicks < 1 || cycleTicks != System.Math.Round(cycleTicks) ||
                !double.IsFinite(endTicks) || endTicks < 1 || endTicks / cycleTicks >= UnitSimulation.Capacity ||
                !float.IsFinite(OrderSpread) || OrderSpread < 0)
                throw new System.ArgumentException("Ladder order cycle must use whole simulation ticks and fit the command queue.");
            simulation.MeasureLadderCrossing((int)cycleTicks, (int)(endTicks / cycleTicks) + 1,
                MeetSampleTicks, MeetDistance);
            float firstZ = simulation.LaneOrderZ(Side.First, FirstSize, count / 2, LaneGap);
            float secondZ = simulation.LaneOrderZ(Side.Second, SecondSize, count / 2, LaneGap);
            for (int cycle = 0; cycle * cycleTicks <= endTicks; cycle++)
                for (int side = 0; side < 2; side++)
                    if (!simulation.QueueSideMove((Side)side, OrderDestination(cycle, (Side)side, side == 0 ? firstZ : secondZ),
                        1 + (ulong)(cycle * cycleTicks), renewEnded: true))
                        throw new System.InvalidOperationException("Ladder move orders could not be queued.");
        }
    }
}
