using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    internal static class Footprint
    {
        internal const float LargeRadius = .7071067811865476f;
        internal static float HalfWidth(UnitSize size) => size == UnitSize.Small ? .25f : .5f;
        internal static float Radius(UnitSize size) => HalfWidth(size) * 1.4142135623730951f;
        internal static float2 Axis(float heading) => new float2(math.cos(heading), math.sin(heading));
        internal static float2 Perpendicular(float2 axis) => new float2(-axis.y, axis.x);

        internal static float Overlap(MovementPose a, UnitSize aSize, MovementPose b, UnitSize bSize)
        {
            float2 ax = Axis(a.Heading), ay = Perpendicular(ax), bx = Axis(b.Heading), by = Perpendicular(bx);
            float2 delta = b.Position.xz - a.Position.xz;
            float ah = HalfWidth(aSize), bh = HalfWidth(bSize);
            float depth = math.min(Project(ax, ax, ay, ah, bx, by, bh, delta), Project(ay, ax, ay, ah, bx, by, bh, delta));
            depth = math.min(depth, math.min(Project(bx, ax, ay, ah, bx, by, bh, delta), Project(by, ax, ay, ah, bx, by, bh, delta)));
            return math.max(0, depth);
        }

        private static float Project(float2 axis, float2 ax, float2 ay, float ah, float2 bx, float2 by, float bh, float2 delta)
            => ah * (math.abs(math.dot(axis, ax)) + math.abs(math.dot(axis, ay))) +
               bh * (math.abs(math.dot(axis, bx)) + math.abs(math.dot(axis, by))) - math.abs(math.dot(axis, delta));
    }
}
