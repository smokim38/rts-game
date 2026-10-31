using Northwatch.Simulation.Movement;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.FlowFields
{
    [BurstCompile]
    internal struct RequestFieldsJob : IJob
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<byte> Alive, Passable;
        [ReadOnly] internal NativeArray<UnitSize> Sizes;
        [ReadOnly] internal NativeArray<MovementUnit> Units;
        internal NativeArray<FlowFieldPool.Entry> Entries;
        internal NativeArray<FlowFieldPool.Request> Requests;
        internal NativeArray<FlowFieldPool.Build> Builds;
        internal NativeParallelHashMap<long, int> Orders;
        internal NativeArray<int> UnitFields;
        internal int Width, Height, Available;
        internal float2 Origin;
        internal ulong Tick;
        internal MovementParameters Settings;

        private static long Key(int order, byte size) => ((long)order << 1) | size;

        public void Execute()
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                var entry = Entries[i]; entry.Users = 0; Entries[i] = entry;
            }
            for (int i = 0; i < Requests.Length; i++)
            {
                var row = Requests[i]; row.Users = 0; Requests[i] = row;
            }
            // First retain existing orders, then release empty rows before admitting new ones.
            for (int i = 0; i < Alive.Length; i++)
                if (Alive[i] != 0 && Units[i].HasMoveOrder && Orders.TryGetValue(Key(Units[i].OrderVersion, (byte)Sizes[i]), out int row))
                {
                    var request = Requests[row]; request.Users++; Requests[row] = request;
                }
            for (int i = 0; i < Requests.Length; i++)
                if (Requests[i].Users == 0 && Orders.TryGetValue(Key(Requests[i].Order, Requests[i].Class), out int mapped) && mapped == i)
                    Orders.Remove(Key(Requests[i].Order, Requests[i].Class));
            int free = 0;
            for (int i = 0; i < Alive.Length; i++)
            {
                UnitFields[i] = FlowFieldReader.NoField;
                if (Alive[i] == 0 || !Units[i].HasMoveOrder) continue;
                byte size = (byte)Sizes[i];
                long key = Key(Units[i].OrderVersion, size);
                if (!Orders.TryGetValue(key, out int row))
                {
                    while (Requests[free].Users != 0) free++;
                    row = free++;
                    int2 goal = (int2)math.floor(Units[i].Goal - Origin - size * .5f);
                    goal = math.clamp(goal, 0, new int2(Width - 1, Height - 1));
                    Requests[row] = new FlowFieldPool.Request { Order = Units[i].OrderVersion, Goal = goal.y * Width + goal.x,
                        Class = size, Users = 1, Field = -1, Queued = Tick };
                    Orders.Add(key, row);
                }
                var request = Requests[row];
                if (!ClearLine(Positions[i].xz, Units[i].Goal, size)) request.NeedsField = 1;
                Requests[row] = request;
            }
            // The direct-route exemption belongs to the whole order, across classes.
            for (int i = 0; i < Requests.Length; i++)
                if (Requests[i].Users != 0 && Requests[i].NeedsField != 0 &&
                    Orders.TryGetValue(Key(Requests[i].Order, (byte)(1 - Requests[i].Class)), out int other))
                {
                    var row = Requests[other]; row.NeedsField = 1; Requests[other] = row;
                }
            for (int i = 0; i < Requests.Length; i++)
            {
                var row = Requests[i]; row.Users = 0; Requests[i] = row;
            }
            for (int i = 0; i < Alive.Length; i++)
                if (Alive[i] != 0 && Units[i].HasMoveOrder && Orders.TryGetValue(Key(Units[i].OrderVersion, (byte)Sizes[i]), out int row))
                {
                    var request = Requests[row]; request.Users++; Requests[row] = request;
                }
            for (int i = 0; i < Requests.Length; i++)
            {
                var request = Requests[i];
                if (request.Users == 0 || request.NeedsField == 0) continue;
                int field = request.Field;
                bool retained = field >= 0 && Entries[field].Valid != 0 && Entries[field].Goal == request.Goal && Entries[field].Class == request.Class;
                if (!retained)
                {
                    if (field >= 0) request.Queued = Tick;
                    field = -1;
                    for (int f = 0; f < Entries.Length; f++)
                        if (Entries[f].Valid != 0 && Entries[f].Replacing == 0 && Entries[f].Goal == request.Goal && Entries[f].Class == request.Class)
                        { field = f; break; }
                }
                request.Field = field; Requests[i] = request;
                if (field < 0) continue;
                var entry = Entries[field]; entry.Users += request.Users; entry.LastUsed = Tick; Entries[field] = entry;
            }
            for (int i = 0; i < Alive.Length; i++)
                if (Alive[i] != 0 && Units[i].HasMoveOrder && Orders.TryGetValue(Key(Units[i].OrderVersion, (byte)Sizes[i]), out int row))
                    UnitFields[i] = Requests[row].NeedsField != 0 ? Requests[row].Field : -1;
            for (int slot = 0; slot < Builds.Length; slot++)
            {
                if ((Available & (1 << slot)) == 0) continue;
                Builds[slot] = new FlowFieldPool.Build { Field = -1 };
                int head = -1;
                for (int i = 0; i < Requests.Length; i++)
                {
                    var row = Requests[i];
                    if (row.Users == 0 || row.NeedsField == 0 || row.Field >= 0 || Building(row.Goal, row.Class)) continue;
                    if (head < 0 || row.Queued < Requests[head].Queued || row.Queued == Requests[head].Queued &&
                        (row.Order < Requests[head].Order || row.Order == Requests[head].Order && row.Class < Requests[head].Class)) head = i;
                }
                if (head < 0) continue;
                int replacement = Replacement();
                if (replacement < 0) break;
                var entry = Entries[replacement]; entry.Replacing = 1; Entries[replacement] = entry;
                Builds[slot] = new FlowFieldPool.Build { Field = replacement, Goal = Requests[head].Goal, Class = Requests[head].Class };
            }
        }

        private bool Building(int goal, byte size)
        {
            for (int i = 0; i < Builds.Length; i++)
                if (Builds[i].Field >= 0 && Builds[i].Goal == goal && Builds[i].Class == size) return true;
            return false;
        }

        private int Replacement()
        {
            int best = -1;
            for (int i = 0; i < Entries.Length; i++)
            {
                var entry = Entries[i];
                if (entry.Replacing != 0) continue;
                if (entry.Valid == 0) return i;
                if (entry.Users > 0 && Tick - entry.Published < (ulong)math.ceil(Settings.MinLease * 30)) continue;
                if (best < 0) { best = i; continue; }
                var chosen = Entries[best];
                if (entry.Users == 0 ? chosen.Users > 0 || entry.LastUsed < chosen.LastUsed :
                    chosen.Users > 0 && (entry.Users < chosen.Users || entry.Users == chosen.Users && entry.Published < chosen.Published)) best = i;
            }
            return best;
        }

        internal bool ClearLine(float2 from, float2 to, byte size)
        {
            if (math.distancesq(from, to) > Settings.DirectSeekRange * Settings.DirectSeekRange) return false;
            float2 a = from - Origin - size * .5f, b = to - Origin - size * .5f;
            int2 cell = (int2)math.floor(a), end = (int2)math.floor(b);
            float2 delta = b - a;
            int2 step = (int2)math.sign(delta);
            float2 inverse = new float2(delta.x == 0 ? float.PositiveInfinity : math.abs(1 / delta.x),
                delta.y == 0 ? float.PositiveInfinity : math.abs(1 / delta.y));
            float2 edge = math.select(a - cell, cell + 1 - a, step > 0) * inverse;
            if (delta.x == 0) edge.x = float.PositiveInfinity;
            if (delta.y == 0) edge.y = float.PositiveInfinity;
            for (int n = 0; n <= Width + Height; n++)
            {
                if (!Pass(cell, size)) return false;
                if (math.all(cell == end)) return true;
                if (edge.x == edge.y)
                {
                    if (!Pass(cell + new int2(step.x, 0), size) || !Pass(cell + new int2(0, step.y), size)) return false;
                    cell += step; edge += inverse;
                }
                else if (edge.x < edge.y) { cell.x += step.x; edge.x += inverse.x; }
                else { cell.y += step.y; edge.y += inverse.y; }
            }
            return false;
        }
        private bool Pass(int2 cell, byte size) => math.all(cell >= 0) && cell.x < Width && cell.y < Height &&
            Passable[size * Width * Height + cell.y * Width + cell.x] != 0;
    }
}
