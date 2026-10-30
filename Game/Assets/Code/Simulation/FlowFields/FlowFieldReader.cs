using Unity.Collections;
using Unity.Mathematics;

namespace Northwatch.Simulation.FlowFields
{
    internal struct FlowFieldReader
    {
        internal const int NoField = -1;
        internal NativeArray<byte>.ReadOnly Directions, Classes;
        internal NativeArray<ushort>.ReadOnly Distances;
        internal int Width, Height;
        internal float2 Origin;

        internal FlowSampleStatus Sample(int field, float2 position, out FlowSample sample)
        {
            sample = default;
            if (field < 0 || field >= Classes.Length || Width <= 0 || Height <= 0)
                return FlowSampleStatus.Unavailable;
            float2 p = position - Origin - (Classes[field] + 1) * .5f;
            int2 cell = (int2)math.floor(p);
            float2 t = p - cell;
            float weight = 0, directionWeight = 0, heaviest = -1;
            float2 sum = 0, fallback = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                int2 offset = new int2(corner & 1, corner >> 1);
                int2 at = cell + offset;
                float2 blend = math.select(1 - t, t, offset != 0);
                float w = blend.x * blend.y;
                if (w <= 0 || math.any(at < 0) || at.x >= Width || at.y >= Height) continue;
                int index = field * Width * Height + at.y * Width + at.x;
                ushort distance = Distances[index];
                if (distance == ushort.MaxValue) continue;
                weight += w;
                sample.Distance += w * distance * .1f;
                if (distance == 0) continue;
                float angle = Directions[index] * (math.PI / 4);
                float2 direction = new float2(math.cos(angle), math.sin(angle));
                sum += w * direction;
                directionWeight += w;
                if (w > heaviest) { heaviest = w; fallback = direction; }
            }
            if (weight == 0) return FlowSampleStatus.Unreachable;
            sample.Distance /= weight;
            sample.Direction = directionWeight == 0 ? float2.zero :
                math.lengthsq(sum) < 1e-6f * directionWeight * directionWeight ? fallback : math.normalizesafe(sum);
            return FlowSampleStatus.Ready;
        }
    }
}
