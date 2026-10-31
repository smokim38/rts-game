using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.FlowFields
{
    [BurstCompile]
    internal struct RebuildStaticMapsJob : IJob
    {
        internal int Width, Height;
        [ReadOnly] internal NativeArray<StaticBox> Boxes;
        internal NativeArray<byte> Walkable, Passable;
        internal NativeArray<ushort> Regions;
        internal NativeArray<int> Queue;

        public void Execute()
        {
            int cells = Width * Height;
            for (int i = 0; i < cells; i++) Walkable[i] = 1;
            for (int i = 0; i < Boxes.Length; i++)
            {
                int2 min = math.max(0, Boxes[i].Min);
                int2 max = math.min(new int2(Width, Height), Boxes[i].MaxExclusive);
                for (int y = min.y; y < max.y; y++)
                    for (int x = min.x; x < max.x; x++) Walkable[y * Width + x] = 0;
            }
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int cell = y * Width + x;
                    Passable[cell] = Walkable[cell];
                    // ADR 0011: (x,y) anchors the lowest cell of the free 2 x 2 square.
                    Passable[cells + cell] = (byte)(x + 1 < Width && y + 1 < Height &&
                        Walkable[cell] != 0 && Walkable[cell + 1] != 0 &&
                        Walkable[cell + Width] != 0 && Walkable[cell + Width + 1] != 0 ? 1 : 0);
                }
            for (int i = 0; i < Regions.Length; i++) Regions[i] = 0;
            FloodFill(0, cells);
            FloodFill(cells, cells);
        }

        private void FloodFill(int offset, int cells)
        {
            ushort label = 0;
            for (int seed = 0; seed < cells; seed++)
            {
                if (Passable[offset + seed] == 0 || Regions[offset + seed] != 0) continue;
                label++;
                int head = 0, tail = 1;
                Queue[0] = seed;
                Regions[offset + seed] = label;
                while (head < tail)
                {
                    int cell = Queue[head++], x = cell % Width, y = cell / Width;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if ((uint)nx >= Width || (uint)ny >= Height) continue;
                            int next = offset + ny * Width + nx;
                            if (Passable[next] == 0 || Regions[next] != 0) continue;
                            if (dx != 0 && dy != 0 && (Passable[offset + y * Width + nx] == 0 ||
                                Passable[offset + ny * Width + x] == 0)) continue;
                            Regions[next] = label;
                            Queue[tail++] = next - offset;
                        }
                }
            }
        }
    }
}
