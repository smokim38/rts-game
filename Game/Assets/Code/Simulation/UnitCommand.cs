using Unity.Mathematics;

namespace Northwatch.Simulation
{
    internal struct UnitCommand
    {
        internal bool Remove;
        internal bool Move;
        internal bool Applied;
        internal ulong RequestId;
        internal UnitHandle Handle;
        internal Side Side;
        internal UnitSize Size;
        internal float3 Position;
        internal int BlockCount;
    }
}
