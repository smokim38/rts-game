using System;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation.FlowFields
{
    internal sealed class StaticMaps : IDisposable
    {
        internal readonly int Width, Height;
        internal readonly NativeArray<byte> Walkable, Passable;
        internal readonly NativeArray<ushort> Regions;
        private NativeArray<int> queue;

        // Passability and regions store class 0 first, followed by class 1.
        internal long MapBytes => Walkable.Length + Passable.Length + 2L * Regions.Length;
        internal long ScratchBytes => 4L * queue.Length;

        internal StaticMaps(int width, int height)
        {
            // With no corner cutting, even a checkerboard fits the ushort region labels.
            if (width < 1 || height < 1 || (long)width * height > 256 * 256)
                throw new ArgumentOutOfRangeException(nameof(width), "Static maps support up to 65,536 cells.");
            Width = width; Height = height;
            int cells = width * height;
            Walkable = new NativeArray<byte>(cells, Allocator.Persistent);
            Passable = new NativeArray<byte>(cells * 2, Allocator.Persistent);
            Regions = new NativeArray<ushort>(cells * 2, Allocator.Persistent);
            queue = new NativeArray<int>(cells, Allocator.Persistent);
        }

        internal JobHandle ScheduleBuild(NativeArray<StaticBox> boxes, JobHandle dependency = default)
            => new RebuildStaticMapsJob
            {
                Width = Width, Height = Height, Boxes = boxes, Walkable = Walkable,
                Passable = Passable, Regions = Regions, Queue = queue
            }.Schedule(dependency);

        public void Dispose()
        {
            Walkable.Dispose(); Passable.Dispose(); Regions.Dispose(); queue.Dispose();
        }
    }
}
