using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Spatial
{
    [BurstCompile]
    internal struct BuildSpatialGridJob : IJob
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<byte> Alive;
        internal SpatialGrid Grid;

        public void Execute()
        {
            for (int b = 0; b < Grid.Counts.Length; b++) Grid.Counts[b] = 0;
            for (int i = 0; i < Alive.Length; i++)
                if (Alive[i] != 0) Grid.Counts[SpatialGrid.Bucket(Positions[i].xz)]++;
            int sum = 0;
            for (int b = 0; b < Grid.Counts.Length; b++)
            {
                Grid.Starts[b] = Grid.Cursors[b] = sum;
                sum += Grid.Counts[b];
            }
            // Stable counting sort: each bucket visits units in slot order on every worker count.
            for (int i = 0; i < Alive.Length; i++)
            {
                if (Alive[i] == 0) continue;
                int bucket = SpatialGrid.Bucket(Positions[i].xz);
                Grid.Indices[Grid.Cursors[bucket]++] = i;
            }
        }
    }
}
