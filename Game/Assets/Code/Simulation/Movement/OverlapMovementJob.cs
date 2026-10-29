using Northwatch.Simulation.Spatial;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct OverlapMovementJob : IJobFor
    {
        [ReadOnly] internal NativeArray<MovementPose> Start, Integrated;
        [ReadOnly] internal NativeArray<MovementUnit> Units;
        [ReadOnly] internal NativeArray<UnitSize> Sizes;
        [ReadOnly] internal NativeArray<Side> Sides;
        [ReadOnly] internal NativeArray<byte> Alive, Walkable;
        [ReadOnly] internal SpatialGrid Grid;
        [WriteOnly] internal NativeArray<MovementPose> Corrected;
        internal MovementParameters Settings;

        public void Execute(int i)
        {
            if (Alive[i] == 0) return;
            MovementPose pose = Integrated[i];
            float radius = Footprint.Radius(Sizes[i]);
            float2 correction = 0;
            bool moverNearby = Units[i].Mode == MovementMode.Moving;
            var query = new SpatialQuery(Grid, Start[i].Position.xz, radius + Footprint.LargeRadius + 2 * Settings.StepBound);
            while (query.Next(out int j))
            {
                if (i == j) continue;
                moverNearby |= Units[j].Mode == MovementMode.Moving;
                float2 delta = pose.Position.xz - Integrated[j].Position.xz;
                float distance = math.length(delta), depth = radius + Footprint.Radius(Sizes[j]) - distance;
                if (depth <= 0) continue;
                float share = Share(i, j);
                // A fixed axis breaks coincident-centre ties reciprocally without randomness.
                float2 axis = distance > 0 ? delta / distance : new float2(i < j ? -1 : 1, 0);
                correction += axis * (share * depth);
            }
            // ADR 0008 permits skipping idle units with no mover nearby. In particular,
            // freshly spawned, tightly packed squares remain at their legal spawn poses.
            if (moverNearby && Units[i].Mode != MovementMode.Hold && math.any(correction != 0))
            {
                correction *= math.min(1, .5f * radius / math.max(math.length(correction), 1e-20f));
                float2 next = pose.Position.xz + correction;
                if (Units[i].Mode == MovementMode.Idle && math.distance(next, Units[i].Home) > 2) next = pose.Position.xz;
                next = StaticClamp.Apply(next, radius, Walkable);
                if (math.distance(next, Start[i].Position.xz) <= Settings.StepBound &&
                    (Units[i].Mode != MovementMode.Idle || math.distance(next, Units[i].Home) <= 2))
                { pose.Position.x = next.x; pose.Position.z = next.y; }
            }
            Corrected[i] = pose;
        }

        private float Share(int a, int b)
        {
            MovementUnit first = Units[a], second = Units[b];
            if (first.Mode == MovementMode.Hold) return 0;
            if (second.Mode == MovementMode.Hold) return 1;
            bool idleA = first.Mode == MovementMode.Idle, idleB = second.Mode == MovementMode.Idle;
            if (idleA == idleB) return .5f;
            if (idleA) return Sides[a] != Sides[b] || math.distance(Start[a].Position.xz, first.Home) >= 1.75f ? 0 : 1;
            return Sides[a] != Sides[b] || math.distance(Start[b].Position.xz, second.Home) >= 1.75f ? 1 : 0;
        }
    }
}
