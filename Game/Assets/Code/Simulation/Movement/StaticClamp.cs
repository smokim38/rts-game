using Unity.Collections;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    internal static class StaticClamp
    {
        internal const int MapWidth = 256;
        private const float HalfMap = UnitSimulation.MapHalfWidth;

        internal static bool IsClear(float2 position, float radius, NativeArray<byte> walkable)
        {
            if (!walkable.IsCreated || walkable.Length == 0) return true;
            if (math.any(math.abs(position) > HalfMap - radius)) return false;
            int2 centre = (int2)math.floor(position + HalfMap);
            for (int y = centre.y - 1; y <= centre.y + 1; y++)
                for (int x = centre.x - 1; x <= centre.x + 1; x++)
                    if ((uint)x < MapWidth && (uint)y < MapWidth && walkable[y * MapWidth + x] == 0 &&
                        Penetration(position, radius, new int2(x, y)) > 0) return false;
            return true;
        }

        internal static float Penetration(float2 position, float radius, int2 cell)
        {
            float2 lo = (float2)cell - HalfMap, hi = lo + 1;
            float distance = math.length(position - math.clamp(position, lo, hi));
            if (distance > 0) return math.max(0, radius - distance);
            return radius + math.cmin(math.min(position - lo, hi - position));
        }

        internal static bool NoWorse(float2 next, float2 start, float radius, NativeArray<byte> walkable)
        {
            float limit = HalfMap - radius;
            if (math.any(math.max(0, math.abs(next) - limit) > math.max(0, math.abs(start) - limit))) return false;
            if (walkable.Length == 0) return true;
            int2 centre = (int2)math.floor(next + HalfMap);
            for (int y = centre.y - 1; y <= centre.y + 1; y++)
                for (int x = centre.x - 1; x <= centre.x + 1; x++)
                {
                    if ((uint)x >= MapWidth || (uint)y >= MapWidth || walkable[y * MapWidth + x] != 0) continue;
                    int2 cell = new int2(x, y);
                    if (Penetration(next, radius, cell) > Penetration(start, radius, cell)) return false;
                }
            return true;
        }

        internal static float2 Apply(float2 position, float radius, NativeArray<byte> walkable)
        {
            position = math.clamp(position, -HalfMap + radius, HalfMap - radius);
            if (walkable.Length == 0) return position;
            int2 centre = (int2)math.floor(position + HalfMap);
            for (int y = centre.y - 1; y <= centre.y + 1; y++)
                for (int x = centre.x - 1; x <= centre.x + 1; x++)
                {
                    if ((uint)x >= MapWidth || (uint)y >= MapWidth || walkable[y * MapWidth + x] != 0) continue;
                    float2 lo = new float2(x, y) - HalfMap, hi = lo + 1;
                    float2 delta = position - math.clamp(position, lo, hi);
                    float length = math.length(delta);
                    // Curved contacts need room for float rounding. Flat contacts
                    // stay stationary; the move check still permits no penetration.
                    float contactRadius = radius + (delta.x != 0 && delta.y != 0 ? .0001f : 0);
                    if (length >= contactRadius) continue;
                    if (length > 0) position += delta * ((contactRadius - length) / length);
                    else
                    {
                        float4 exits = new float4(position - lo, hi - position);
                        float nearest = math.cmin(exits);
                        if (exits.x == nearest) position.x = lo.x - radius;
                        else if (exits.y == nearest) position.y = lo.y - radius;
                        else if (exits.z == nearest) position.x = hi.x + radius;
                        else position.y = hi.y + radius;
                    }
                }
            return position;
        }
    }
}
