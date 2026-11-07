using Northwatch.Simulation.Movement;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation
{
    [BurstCompile]
    internal struct LadderPassJob : IJob
    {
        [ReadOnly] internal UnitState State;
        internal NativeArray<float3> Previous;
        internal NativeArray<float4> Snapshots;
        internal NativeArray<byte> Met;
        internal NativeArray<LadderCycleCounts> Counts;
        internal int Cycle;
        internal float Seconds;
        internal MovementParameters Settings;

        public void Execute()
        {
            float2 oldMeans = 0, means = 0;
            int2 sizes = 0;
            for (int i = 0; i < State.Alive.Length; i++)
            {
                Snapshots[i] = 0;
                if (State.Alive[i] != 0)
                {
                    int side = (int)State.Sides[i];
                    float3 position = State.Positions[i];
                    Snapshots[i] = new float4(position.xz, side, math.max(.000001f, Settings.Speed(State.Sizes[i])));
                    oldMeans[side] += Previous[i].x; means[side] += position.x; sizes[side]++;
                }
            }
            oldMeans /= math.max(sizes, 1); means /= math.max(sizes, 1);
            var first = new LadderCycleCounts { cycle = Cycle, units = sizes.x, minimumTravelShare = float.MaxValue };
            var second = new LadderCycleCounts { cycle = Cycle, side = 1, units = sizes.y, minimumTravelShare = float.MaxValue };
            for (int i = 0; i < Snapshots.Length; i++)
            {
                float4 snapshot = Snapshots[i];
                if (snapshot.w == 0) continue;
                int side = (int)snapshot.z;
                if (Cycle >= 0)
                {
                    var count = side == 0 ? first : second;
                    if ((Previous[i].x - oldMeans[1 - side]) * (snapshot.x - means[1 - side]) < 0) count.passed++;
                    count.met += Met[i];
                    float travel = math.distance(Previous[i].xz, snapshot.xy) / (snapshot.w * Seconds);
                    count.travelShare += travel / math.max(1, sizes[side]);
                    count.minimumTravelShare = math.min(count.minimumTravelShare, travel);
                    if (side == 0) first = count; else second = count;
                }
                Previous[i] = new float3(snapshot.x, 0, snapshot.y); Met[i] = 0;
            }
            if (Cycle >= 0) { Counts[Cycle * 2] = first; Counts[Cycle * 2 + 1] = second; }
        }
    }
}
