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
        internal NativeArray<MovementPose> Poses;
        internal float StepBound;

        internal SpatialQuery Neighbours(int unit) => new SpatialQuery(Grid, Start[unit].Position.xz,
            Footprint.Radius(Sizes[unit]) + Footprint.LargeRadius + 2 * StepBound);

        internal byte Choose(int unit, byte level)
        {
            if (level == 2 || Accept(unit, Poses[unit])) return level;
            if (level == 0)
            {
                MovementPose oldHeading = Poses[unit]; oldHeading.Heading = Start[unit].Heading;
                if (Accept(unit, oldHeading)) return 1;
            }
            return 2;
        }

        private bool Accept(int unit, MovementPose pose)
        {
            if (!StaticClamp.NoWorse(pose.Position.xz, Start[unit].Position.xz, Footprint.Radius(Sizes[unit]), Walkable)) return false;
            var query = Neighbours(unit);
            while (query.Next(out int other))
            {
                if (other == unit) continue;
                float reach = Footprint.Radius(Sizes[unit]) + Footprint.Radius(Sizes[other]);
                if (math.distancesq(pose.Position.xz, Poses[other].Position.xz) >= reach * reach) continue;
                if (Footprint.Overlap(pose, Sizes[unit], Poses[other], Sizes[other]) >
                    Footprint.Overlap(Start[unit], Sizes[unit], Start[other], Sizes[other])) return false;
            }
            return true;
        }

        internal void Apply(int unit, byte level)
        {
            MovementPose pose = Poses[unit];
            if (level == 2) pose = Start[unit];
            else if (level == 1) pose.Heading = Start[unit].Heading;
            Poses[unit] = pose;
        }
    }
}
