using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Spatial
{
    [BurstCompile]
    internal struct BucketUnitsJob : IJobFor
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<byte> Alive;
        [WriteOnly] internal NativeArray<int> Buckets;

        public void Execute(int i) { Buckets[i] = Alive[i] == 0 ? -1 : SpatialGrid.Bucket(Positions[i].xz); }
    }
}
