using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Northwatch.Simulation.Spatial
{
    internal struct SpatialGrid : IDisposable
    {
        internal const int Width = 128;
        internal const float BucketSize = 2f;
        internal NativeArray<int> Starts, Counts, Indices, Cursors;

        internal SpatialGrid(int capacity)
        {
            Starts = new NativeArray<int>(Width * Width, Allocator.Persistent);
            Counts = new NativeArray<int>(Width * Width, Allocator.Persistent);
            Cursors = new NativeArray<int>(Width * Width, Allocator.Persistent);
            Indices = new NativeArray<int>(capacity, Allocator.Persistent);
        }

        internal static int2 Cell(float2 position) => math.clamp((int2)math.floor((position + UnitSimulation.MapHalfWidth) / BucketSize), 0, Width - 1);
        internal static int Bucket(float2 position) { int2 cell = Cell(position); return cell.y * Width + cell.x; }

        public void Dispose() { Starts.Dispose(); Counts.Dispose(); Cursors.Dispose(); Indices.Dispose(); }
    }
}
