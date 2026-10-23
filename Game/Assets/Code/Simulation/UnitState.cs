using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Northwatch.Simulation
{
    internal struct UnitState : IDisposable
    {
        internal NativeArray<float3> Positions;
        internal NativeArray<Side> Sides;
        internal NativeArray<UnitSize> Sizes;
        internal NativeArray<byte> Alive;
        internal NativeArray<uint> Generations;
        internal NativeArray<int> FreeSlots;
        // Count, next unused slot, free-slot count, then the two draw counts.
        internal NativeArray<int> Summary;

        internal UnitState(int capacity)
        {
            Positions = new NativeArray<float3>(capacity, Allocator.Persistent);
            Sides = new NativeArray<Side>(capacity, Allocator.Persistent);
            Sizes = new NativeArray<UnitSize>(capacity, Allocator.Persistent);
            Alive = new NativeArray<byte>(capacity, Allocator.Persistent);
            Generations = new NativeArray<uint>(capacity, Allocator.Persistent);
            FreeSlots = new NativeArray<int>(capacity, Allocator.Persistent);
            Summary = new NativeArray<int>(5, Allocator.Persistent);
        }

        public void Dispose()
        {
            Positions.Dispose();
            Sides.Dispose();
            Sizes.Dispose();
            Alive.Dispose();
            Generations.Dispose();
            FreeSlots.Dispose();
            Summary.Dispose();
        }
    }
}
