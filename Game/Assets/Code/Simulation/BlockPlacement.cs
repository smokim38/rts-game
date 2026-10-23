using Unity.Mathematics;

namespace Northwatch.Simulation
{
    internal struct BlockPlacement
    {
        private int count, columns, rows;
        private float width;
        private float2 centre;

        internal BlockPlacement(int count, UnitSize size, float3 click)
        {
            this.count = count;
            columns = (int)math.ceil(math.sqrt(count));
            rows = (count + columns - 1) / columns;
            width = size == UnitSize.Small ? 0.5f : 1f;
            float2 halfExtent = new float2(columns, rows) * width * 0.5f;
            centre = math.clamp(click.xz, -UnitSimulation.MapHalfWidth + halfExtent, UnitSimulation.MapHalfWidth - halfExtent);
        }

        internal float3 Position(int index)
        {
            int row = index / columns;
            int rowCount = math.min(columns, count - row * columns);
            return new float3(centre.x + (index % columns - (rowCount - 1) * 0.5f) * width, 0f,
                centre.y + (row - (rows - 1) * 0.5f) * width);
        }

        internal bool Overlaps(float3 position, UnitSize size)
        {
            float clearance = (width + (size == UnitSize.Small ? 0.5f : 1f)) * 0.5f;
            float rowPosition = (position.z - centre.y) / width + (rows - 1) * 0.5f;
            int first = math.max(0, (int)math.floor(rowPosition - clearance / width));
            int last = math.min(rows - 1, (int)math.ceil(rowPosition + clearance / width));
            for (int row = first; row <= last; row++)
            {
                int rowCount = math.min(columns, count - row * columns);
                int column = math.clamp((int)math.round((position.x - centre.x) / width + (rowCount - 1) * 0.5f), 0, rowCount - 1);
                float3 nearest = Position(row * columns + column);
                if (math.abs(position.x - nearest.x) < clearance && math.abs(position.z - nearest.z) < clearance) return true;
            }
            return false;
        }
    }
}
