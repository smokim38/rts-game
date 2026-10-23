using Unity.Mathematics;

namespace Northwatch.Simulation
{
    internal struct UnitCommand
    {
        internal bool Remove;
        internal UnitHandle Handle;
        internal Side Side;
        internal UnitSize Size;
        internal float3 Position;
    }
}
