using Unity.Mathematics;

namespace Northwatch.Simulation
{
    internal struct UnitCommand
    {
        internal bool Remove;
        internal bool Move;
        internal bool AllOnSide;
        internal bool RenewEnded;
        internal ulong ExecuteTick;
        internal bool Applied;
        internal bool Consumed;
        internal ulong RequestId;
        internal UnitHandle Handle;
        internal Side Side;
        internal UnitSize Size;
        internal float3 Position;
        internal int BlockCount;
    }
}
