using Northwatch.Simulation;
using UnityEngine;

namespace Northwatch.Presentation.Diagnostics
{
    public sealed class LadderScenario : ScriptableObject
    {
        public int Version = 2;
        public int Seed = 103;
        public float PlaneSize = UnitSimulation.MapHalfWidth * 2;
        public Vector3 FirstSpawn = new Vector3(-30, 0, 0);
        public Vector3 SecondSpawn = new Vector3(30, 0, 0);
        public UnitSize FirstSize = UnitSize.Small;
        public UnitSize SecondSize = UnitSize.Large;
        public float OrderCycleSeconds = 20;
        public Vector3 FirstOrderEnd = new Vector3(-120, 0, 0);
        public Vector3 SecondOrderEnd = new Vector3(120, 0, 0);
        public float OrderSpread = 8;
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

        public Vector3 OrderDestination(int cycle, Side side)
        {
            var random = Unity.Mathematics.Random.CreateFromIndex(unchecked((uint)Seed + (uint)cycle * 2 + (uint)side));
            bool firstEnd = ((cycle + (int)side) & 1) != 0;
            Vector3 destination = firstEnd ? FirstOrderEnd : SecondOrderEnd;
            destination.z += random.NextFloat(-OrderSpread, OrderSpread);
            return destination;
        }

        public void QueueOrders(UnitSimulation simulation)
        {
            double cycleTicks = OrderCycleSeconds / TickClock.TickDuration;
            double endTicks = (WarmupSeconds + WindowSeconds + DrainSeconds) / TickClock.TickDuration;
            if (!double.IsFinite(cycleTicks) || cycleTicks < 1 || cycleTicks != System.Math.Round(cycleTicks) ||
                !double.IsFinite(endTicks) || endTicks < 1 || endTicks / cycleTicks >= UnitSimulation.Capacity ||
                !float.IsFinite(OrderSpread) || OrderSpread < 0)
                throw new System.ArgumentException("Ladder order cycle must use whole simulation ticks and fit the command queue.");
            for (int cycle = 0; cycle * cycleTicks <= endTicks; cycle++)
                for (int side = 0; side < 2; side++)
                    if (!simulation.QueueSideMove((Side)side, OrderDestination(cycle, (Side)side),
                        1 + (ulong)(cycle * cycleTicks), renewEnded: true))
                        throw new System.InvalidOperationException("Ladder move orders could not be queued.");
        }
    }
}
