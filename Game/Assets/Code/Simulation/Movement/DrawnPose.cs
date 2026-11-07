using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    internal static class DrawnPose
    {
        internal static MovementPose Sample(MovementPose previous, float3 position, float heading, float interpolation)
            => new MovementPose
            {
                Position = math.lerp(previous.Position, position, interpolation),
                Heading = previous.Heading + math.atan2(math.sin(heading - previous.Heading),
                    math.cos(heading - previous.Heading)) * interpolation
            };
    }
}
