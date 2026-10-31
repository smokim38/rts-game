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
            { Movement.MovementTimestampJob.Write(Timestamps, 1); return; }
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
            Movement.MovementTimestampJob.Write(Timestamps, 1);
        }

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
