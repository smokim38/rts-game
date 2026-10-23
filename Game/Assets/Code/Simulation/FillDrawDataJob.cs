using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Simulation
{
    [BurstCompile]
    internal struct FillDrawDataJob : IJob
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<Side> Sides;
        [ReadOnly] internal NativeArray<UnitSize> Sizes;
        [ReadOnly] internal NativeArray<byte> Alive;
        internal NativeArray<int> Summary;
        [WriteOnly] internal NativeArray<Matrix4x4> Matrices;

        public void Execute()
        {
            int first = 0, second = 0;
            for (int slot = 0; slot < Alive.Length; slot++)
            {
                if (Alive[slot] == 0) continue;
                float width = Sizes[slot] == UnitSize.Small ? 0.5f : 1f;
                float3 position = Positions[slot];
                int index = Sides[slot] == Side.First ? first++ : Alive.Length + second++;
                Matrices[index] = new Matrix4x4
                {
                    m00 = width, m11 = width, m22 = width, m33 = 1f,
                    m03 = position.x, m13 = position.y + width * 0.5f, m23 = position.z
                };
            }
            Summary[3] = first;
            Summary[4] = second;
        }
    }
}
