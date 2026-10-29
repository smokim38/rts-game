using Unity.Mathematics;

namespace Northwatch.Simulation.Spatial
{
    internal struct SpatialQuery
    {
        private SpatialGrid grid;
        private int2 first, last;
        private int x, y, offset, end;

        internal SpatialQuery(SpatialGrid grid, float2 position, float radius)
        {
            this.grid = grid;
            first = SpatialGrid.Cell(position - radius); last = SpatialGrid.Cell(position + radius);
            x = first.x - 1; y = first.y; offset = end = 0;
        }

        internal bool Next(out int unit)
        {
            while (offset == end)
            {
                if (++x > last.x) { x = first.x; y++; }
                if (y > last.y) { unit = -1; return false; }
                int bucket = y * SpatialGrid.Width + x;
                offset = grid.Starts[bucket]; end = offset + grid.Counts[bucket];
            }
            unit = grid.Indices[offset++];
            return true;
        }
    }
}
