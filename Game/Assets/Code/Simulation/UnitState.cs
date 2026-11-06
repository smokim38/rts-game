using System;
using Northwatch.Simulation.Movement;
using Unity.Collections;
using Unity.Mathematics;

namespace Northwatch.Simulation
{
    internal struct UnitState : IDisposable
    {
        internal NativeArray<float3> Positions;
        internal NativeArray<MovementUnit> Movement;
        internal NativeArray<Side> Sides;
        internal NativeArray<UnitSize> Sizes;
        internal NativeArray<byte> Alive;
        internal NativeArray<uint> Generations;
        internal NativeArray<int> FreeSlots;
        // Count, next slot, free count, draw counts, placement result/version, acknowledgements,
        // move orders, renewals by arrival rule (1..4), latched missing-order failure.
        internal NativeArray<int> Summary;

        internal UnitState(int capacity)
        {
            Positions = new NativeArray<float3>(capacity, Allocator.Persistent);
            Movement = new NativeArray<MovementUnit>(capacity, Allocator.Persistent);
            Sides = new NativeArray<Side>(capacity, Allocator.Persistent);
            Sizes = new NativeArray<UnitSize>(capacity, Allocator.Persistent);
            Alive = new NativeArray<byte>(capacity, Allocator.Persistent);
            Generations = new NativeArray<uint>(capacity, Allocator.Persistent);
            FreeSlots = new NativeArray<int>(capacity, Allocator.Persistent);
            Summary = new NativeArray<int>(14, Allocator.Persistent);
        }

        public void Dispose()
        {
            Positions.Dispose();
            Movement.Dispose();
            Sides.Dispose();
            Sizes.Dispose();
            Alive.Dispose();
            Generations.Dispose();
            FreeSlots.Dispose();
            Summary.Dispose();
        }
    }
}
