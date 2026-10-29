using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Northwatch.Simulation.Movement;

namespace Northwatch.Simulation.Spatial
{
    [BurstCompile]
    internal struct BuildSpatialGridJob : IJob
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<byte> Alive;
        [ReadOnly] internal NativeArray<MovementUnit> Units;
        internal SpatialGrid Grid;

        public void Execute()
        {
            for (int b = 0; b < Grid.Counts.Length; b++) Grid.Counts[b] = 0;
            for (int i = 0; i < Alive.Length; i++)
                if (Alive[i] != 0) Grid.Counts[Grid.Buckets[i]]++;
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
                int sorted = Grid.Cursors[Grid.Buckets[i]]++;
                Grid.Indices[sorted] = i;
                Grid.Poses[sorted] = new MovementPose { Position = Positions[i], Heading = Units[i].Heading };
                Grid.Velocities[sorted] = Units[i].Velocity;
            }
        }
    }
}
