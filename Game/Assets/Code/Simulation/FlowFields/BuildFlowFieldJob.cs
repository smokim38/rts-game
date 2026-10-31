using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.FlowFields
{
    [BurstCompile]
    internal struct BuildFlowFieldJob : IJob
    {
        internal int Width, Height, BuildSlot;
        [ReadOnly] internal NativeArray<byte> Passable;
        [ReadOnly] internal NativeArray<FlowFieldPool.Build> Builds;
        internal NativeArray<int> Costs, Heap, HeapPositions;
        internal NativeArray<byte> Directions;
        internal NativeArray<ushort> Distances;
        internal NativeArray<long> Timestamps;
        internal NativeArray<int4> SightIntervalsA, SightIntervalsB;

        // Indexed binary heap: at most one entry per cell, including decrease-key.
        public void Execute()
        {
            FlowFieldPool.Build build = Builds[BuildSlot];
            if (build.Field < 0) return;
            Movement.MovementTimestampJob.Write(Timestamps, 0);
            int cells = Width * Height, offset = build.Class * cells, count = 0;
            for (int i = 0; i < cells; i++)
            {
                Costs[i] = int.MaxValue; HeapPositions[i] = -1;
                Distances[i] = ushort.MaxValue; Directions[i] = 0;
            }
            if ((uint)build.Goal >= cells || Passable[offset + build.Goal] == 0)
            {
                Movement.MovementTimestampJob.Write(Timestamps, 2);
                Movement.MovementTimestampJob.Write(Timestamps, 1); return;
            }
            Costs[build.Goal] = 0;
            Push(build.Goal, ref count);
            while (count > 0)
            {
                int cell = Heap[0];
                HeapPositions[cell] = -1;
                count--;
                if (count > 0)
                {
                    Heap[0] = Heap[count]; HeapPositions[Heap[0]] = 0;
                    int at = 0;
                    while (at * 2 + 1 < count)
                    {
                        int child = at * 2 + 1;
                        if (child + 1 < count && Less(Heap[child + 1], Heap[child])) child++;
                        if (!Less(Heap[child], Heap[at])) break;
                        Swap(at, child); at = child;
                    }
                }
                Distances[cell] = (ushort)math.min(Costs[cell], 65534);
                int2 p = new int2(cell % Width, cell / Width);
                for (int d = 0; d < 8; d++)
                {
                    int2 step = Step(d), next = p + step;
                    if (math.any(next < 0) || next.x >= Width || next.y >= Height) continue;
                    int neighbour = next.y * Width + next.x;
                    if (Passable[offset + neighbour] == 0) continue;
                    if (step.x != 0 && step.y != 0 && (Passable[offset + p.y * Width + next.x] == 0 ||
                        Passable[offset + next.y * Width + p.x] == 0)) continue;
                    int cost = Costs[cell] + (step.x == 0 || step.y == 0 ? 10 : 14);
                    if (cost >= Costs[neighbour]) continue;
                    Costs[neighbour] = cost; Directions[neighbour] = (byte)((d + 4) & 7);
                    Push(neighbour, ref count);
                }
            }
            // The integration search chooses shortest costs. The stored direction
            // chooses the legal neighbour with the lowest cost (ADR 0008).
            for (int cell = 0; cell < cells; cell++)
            {
                if (Costs[cell] == int.MaxValue || cell == build.Goal) continue;
                int2 p = new int2(cell % Width, cell / Width);
                int best = Costs[cell];
                for (int d = 0; d < 8; d++)
                {
                    int2 step = Step(d), next = p + step;
                    if (math.any(next < 0) || next.x >= Width || next.y >= Height) continue;
                    if (step.x != 0 && step.y != 0 && (Passable[offset + p.y * Width + next.x] == 0 ||
                        Passable[offset + next.y * Width + p.x] == 0)) continue;
                    int cost = Costs[next.y * Width + next.x];
                    if (cost >= best) continue;
                    best = cost; Directions[cell] = (byte)d;
                }
            }
            Movement.MovementTimestampJob.Write(Timestamps, 2);
            BuildSight(build.Goal, offset);
            Movement.MovementTimestampJob.Write(Timestamps, 1);
        }

        private bool IsPassable(int2 p, int offset) => math.any(p < 0) || p.x >= Width || p.y >= Height ||
            Passable[offset + p.y * Width + p.x] != 0;

        // A rational endpoint is (numerator, denominator); shadow ends are open.
        private static int Compare(int2 a, int2 b) => a.x * b.y - b.x * a.y;

        private void BuildSight(int goal, int offset)
        {
            int2 origin = new int2(goal % Width, goal / Width);
            Directions[goal] |= 8;
            for (int octant = 0; octant < 8; octant++)
            {
                int2 axis = Step(((octant + 1) / 2 * 2) & 7);
                int2 side = Step((octant / 2 * 2 + (octant % 2 == 0 ? 2 : 0)) & 7);
                int depthLimit = Extent(origin, axis), sideLimit = Extent(origin, side);
                var lit = SightIntervalsA; var output = SightIntervalsB;
                lit[0] = new int4(0, 1, 1, 1);
                int count = 1;
                bool axisVisible = true, diagonalVisible = true;
                for (int depth = 0; depth <= depthLimit; depth++)
                {
                    if (depth > 0)
                    {
                        axisVisible &= IsPassable(origin + axis * depth, offset);
                        diagonalVisible &= IsPassable(origin + (axis + side) * depth, offset) &&
                            IsPassable(origin + axis * depth + side * (depth - 1), offset) &&
                            IsPassable(origin + axis * (depth - 1) + side * depth, offset);
                    }
                    // Classify the row against the previous lit list while merging
                    // its ordered open shadows into the next list. Dark blockers
                    // still cast shadows; the diagonal band is read once per row.
                    int interval = 0, cursor = 0, written = 0;
                    int4 current = count == 0 ? default : lit[0];
                    for (int column = 0; column <= math.min(depth + 1, sideLimit); column++)
                    {
                        int2 p = origin + axis * depth + side * column;
                        if (depth > 0 && column <= depth)
                        {
                            int cell = p.y * Width + p.x;
                            int2 slope = new int2(column, depth);
                            while (interval < count && Compare(lit[interval].zw, slope) < 0) interval++;
                            bool visible = column == 0 ? axisVisible : column == depth ? diagonalVisible :
                                interval < count && Compare(lit[interval].xy, slope) <= 0 &&
                                IsPassable(p - side, offset);
                            if (visible && Distances[cell] != ushort.MaxValue) Directions[cell] |= 8;
                        }
                        if (cursor == count || depth == 0 && column == 0 || IsPassable(p, offset)) continue;
                        int2 low = new int2(column - 1, depth + 1);
                        int2 high = depth <= 1 ? new int2(1, 0) : new int2(column + 1, depth - 1);
                        while (cursor < count)
                        {
                            if (Compare(current.zw, low) <= 0)
                            {
                                output[written++] = current;
                                if (++cursor < count) current = lit[cursor];
                                continue;
                            }
                            if (Compare(current.xy, high) >= 0) break;
                            if (Compare(current.xy, low) <= 0)
                                output[written++] = new int4(current.xy, low);
                            if (Compare(high, current.zw) <= 0)
                            { current = new int4(high, current.zw); break; }
                            if (++cursor < count) current = lit[cursor];
                        }
                    }
                    if (cursor < count) output[written++] = current;
                    while (++cursor < count) output[written++] = lit[cursor];
                    count = written;
                    var swap = lit; lit = output; output = swap;
                }
            }
        }

        private int Extent(int2 origin, int2 axis) => axis.x > 0 ? Width - 1 - origin.x :
            axis.x < 0 ? origin.x : axis.y > 0 ? Height - 1 - origin.y : origin.y;

        internal static int2 Step(int direction)
        {
            switch (direction & 7)
            {
                case 0: return new int2(1, 0);
                case 1: return new int2(1, 1);
                case 2: return new int2(0, 1);
                case 3: return new int2(-1, 1);
                case 4: return new int2(-1, 0);
                case 5: return new int2(-1, -1);
                case 6: return new int2(0, -1);
                default: return new int2(1, -1);
            }
        }

        private bool Less(int a, int b) => Costs[a] < Costs[b] || Costs[a] == Costs[b] && a < b;
        private void Swap(int a, int b)
        {
            int value = Heap[a]; Heap[a] = Heap[b]; Heap[b] = value;
            HeapPositions[Heap[a]] = a; HeapPositions[Heap[b]] = b;
        }
        private void Push(int cell, ref int count)
        {
            int at = HeapPositions[cell];
            if (at < 0) { at = count++; Heap[at] = cell; HeapPositions[cell] = at; }
            while (at > 0)
            {
                int parent = (at - 1) / 2;
                if (!Less(cell, Heap[parent])) break;
                Swap(at, parent); at = parent;
            }
        }
    }
}
