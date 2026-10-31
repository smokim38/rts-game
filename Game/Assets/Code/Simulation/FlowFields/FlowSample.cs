using Unity.Mathematics;

namespace Northwatch.Simulation.FlowFields
{
    internal struct FlowSample
    {
        internal float2 Direction;
        internal float Distance;
        internal bool InSight;
        internal float2 Goal;
    }
}
