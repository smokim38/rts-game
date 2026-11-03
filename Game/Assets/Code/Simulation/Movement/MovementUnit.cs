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
        internal ushort GoalRegion;
        internal bool GoalResolved;
        internal int ProgressField;
        internal float BestGoalDistance;
        internal ulong LastProgressTick;
        // 1: exact arrival, 2: idle order contact, 3: goal congestion, 4: group stall.
        internal byte ArrivalRule;
        internal MovementMode Mode;
        internal float StalledTime, HomeBlockedTime, ProgressTime;
    }
}
