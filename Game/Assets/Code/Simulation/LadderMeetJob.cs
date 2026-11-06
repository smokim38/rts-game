using Northwatch.Simulation.Spatial;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation
{
    [BurstCompile]
    internal struct LadderMeetJob : IJobFor
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<Side> Sides;
        [ReadOnly] internal NativeArray<byte> Alive;
        [ReadOnly] internal SpatialGrid Grid;
        internal NativeArray<byte> Met;
        internal float Distance, StepBound;

        public void Execute(int i)
        {
            if (Alive[i] == 0 || Met[i] != 0) return;
            // The grid precedes this tick's movement. Expand the bucket query by
            // the movement envelope, then test exact post-step centre distances.
            var query = new SpatialQuery(Grid, Positions[i].xz, Distance + StepBound);
            while (query.Next(out int j))
                if (Alive[j] != 0 && Sides[j] != Sides[i] &&
                    math.distancesq(Positions[i].xz, Positions[j].xz) <= Distance * Distance)
                { Met[i] = 1; return; }
        }
    }
}
