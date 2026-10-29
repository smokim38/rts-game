using Northwatch.Simulation.Spatial;
using Unity.Collections;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    internal struct MoveCheckData
    {
        [ReadOnly] internal NativeArray<MovementPose> Start;
        [ReadOnly] internal NativeArray<UnitSize> Sizes;
        [ReadOnly] internal NativeArray<byte> Alive, Walkable;
        [ReadOnly] internal SpatialGrid Grid;
        [ReadOnly] internal NativeArray<MovementPose> Poses;
        [ReadOnly] internal NativeArray<byte> Levels;
        internal float StepBound;

        internal SpatialQuery Neighbours(int unit) => new SpatialQuery(Grid, Start[unit].Position.xz,
            Footprint.Radius(Sizes[unit]) + Footprint.LargeRadius + 2 * StepBound);

        internal byte Choose(int unit, byte level)
        {
            for (byte candidate = 0; candidate < level; candidate++)
                if (Accept(unit, Pose(unit, candidate))) return candidate;
            return level;
        }

        private bool Accept(int unit, MovementPose pose)
        {
            if (!StaticClamp.NoWorse(pose.Position.xz, Start[unit].Position.xz, Footprint.Radius(Sizes[unit]), Walkable)) return false;
            var query = Neighbours(unit);
            while (query.Next(out int other))
            {
                if (other == unit) continue;
                float reach = Footprint.Radius(Sizes[unit]) + Footprint.Radius(Sizes[other]);
                if (math.distancesq(pose.Position.xz, Poses[other].Position.xz) >= reach * reach &&
                    (Levels[other] < 2 || math.distancesq(pose.Position.xz, Start[other].Position.xz) >= reach * reach)) continue;
                float limit = Footprint.Overlap(Start[unit], Sizes[unit], Start[other], Sizes[other]);
                for (byte level = 0; level <= Levels[other]; level++)
                {
                    MovementPose neighbour = Pose(other, level);
                    if (math.distancesq(pose.Position.xz, neighbour.Position.xz) >= reach * reach) continue;
                    if (Footprint.Overlap(pose, Sizes[unit], neighbour, Sizes[other]) > limit) return false;
                }
            }
            return true;
        }

        private MovementPose Pose(int unit, byte level)
        {
            MovementPose pose = Poses[unit];
            if (level == 2) pose = Start[unit];
            else if (level == 1) pose.Heading = Start[unit].Heading;
            return pose;
        }
    }
}
