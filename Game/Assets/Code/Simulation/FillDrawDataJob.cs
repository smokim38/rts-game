using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Northwatch.Simulation.Movement;

namespace Northwatch.Simulation
{
    [BurstCompile]
    internal struct FillDrawDataJob : IJob
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<MovementUnit> Movement;
        internal float Interpolation;
        [ReadOnly] internal NativeArray<Side> Sides;
        [ReadOnly] internal NativeArray<UnitSize> Sizes;
        [ReadOnly] internal NativeArray<byte> Alive;
        [ReadOnly] internal NativeArray<uint> Selected;
        [ReadOnly] internal NativeArray<int> QueuedMoves;
        internal NativeArray<float4> Orders;
        internal float Seconds;
        internal NativeArray<int> Summary;
        [WriteOnly] internal NativeArray<Matrix4x4> Matrices;

        public void Execute()
        {
            int first = 0, second = 0, selected = 0, acknowledgements = 0, markerCount = 0;
            float elapsed = Seconds - Orders[2].x;
            bool acknowledge = Orders[1].w > 0 && elapsed >= 0 && elapsed < .3f;
            float pulse = 1.2f + .1f * math.sin(elapsed * (math.PI * 2 / .3f));
            float3 selectedCentre = 0;
            for (int slot = 0; slot < Alive.Length; slot++)
            {
                if (Alive[slot] == 0) continue;
                float width = Sizes[slot] == UnitSize.Small ? 0.5f : 1f;
                MovementUnit unit = Movement[slot];
                // A click gets feedback before its queued move is applied on a tick.
                if (Orders[1].w > 0 && unit.OrderVersion == (int)Orders[1].w &&
                    (unit.HasMoveOrder || QueuedMoves[slot] != 0)) markerCount++;
                if (Selected[slot] != 0) selectedCentre += Positions[slot];
                float3 position = math.lerp(unit.Previous.Position, Positions[slot], Interpolation);
                float angle = unit.Previous.Heading + math.atan2(math.sin(unit.Heading - unit.Previous.Heading),
                    math.cos(unit.Heading - unit.Previous.Heading)) * Interpolation;
                float cosine = math.cos(angle) * width, sine = math.sin(angle) * width;
                int index = Selected[slot] != 0 ? Alive.Length * 2 + selected++ :
                    Sides[slot] == Side.First ? first++ : Alive.Length + second++;
                Matrices[index] = new Matrix4x4
                {
                    m00 = cosine, m02 = -sine, m20 = sine, m11 = width, m22 = cosine, m33 = 1f,
                    m03 = position.x, m13 = position.y + width * 0.5f, m23 = position.z
                };
                if (acknowledge && unit.OrderVersion == (int)Orders[1].w)
                    Matrices[Alive.Length * 3 + acknowledgements++] = new Matrix4x4
                    {
                        m00 = width * pulse, m11 = .015f, m22 = width * pulse, m33 = 1,
                        m03 = position.x, m13 = position.y + .015f, m23 = position.z
                    };
            }
            Summary[3] = first + selected;
            Summary[4] = second;
            Summary[7] = acknowledgements;
            Orders[0] = new float4(selected == 0 ? float3.zero : selectedCentre / selected, selected);
            float4 feedback = Orders[2]; feedback.y = markerCount; Orders[2] = feedback;
        }
    }
}
