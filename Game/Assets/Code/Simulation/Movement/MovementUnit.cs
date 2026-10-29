using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    internal struct MovementUnit
    {
        internal MovementPose Previous;
        internal float Heading;
        internal float2 Velocity, PreferredVelocity, NewVelocity, Home;
        internal float2 Goal;
        internal bool HasMoveOrder;
        internal int OrderVersion;
        internal MovementMode Mode;
        internal float StalledTime, HomeBlockedTime, ProgressTime;
    }
}
