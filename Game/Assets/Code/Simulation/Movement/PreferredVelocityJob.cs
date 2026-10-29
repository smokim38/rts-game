using Northwatch.Simulation.FlowFields;
using Northwatch.Simulation.Spatial;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct PreferredVelocityJob : IJobFor
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<UnitSize> Sizes;
        [ReadOnly] internal NativeArray<byte> Alive;
        [ReadOnly] internal NativeArray<MovementUnit> Units;
        [WriteOnly] internal NativeArray<MovementUnit> Planned;
        [ReadOnly] internal SpatialGrid Grid;
        internal FlowFieldReader Fields;
        internal MovementParameters Settings;

        public void Execute(int i)
        {
            if (Alive[i] == 0) return;
            MovementUnit unit = Units[i];
            if (!unit.HasMoveOrder) { Planned[i] = unit; return; }
            float2 delta = unit.Goal - Positions[i].xz;
            float distance = math.length(delta);
            bool arrived = distance <= .05f && math.lengthsq(unit.Velocity) < .01f;
            // Without formation slots these are the ADR 0008 overflow arrivals:
            // stop on the route beside an already arrived member of this order.
            var query = new SpatialQuery(Grid, Positions[i].xz, Footprint.Radius(Sizes[i]) + Footprint.LargeRadius + .1f);
            while (!arrived && query.Next(out int other))
                if (other != i && Units[other].OrderVersion == unit.OrderVersion &&
                    Units[other].Mode == MovementMode.Idle &&
                    math.distance(Positions[i].xz, Positions[other].xz) <= Footprint.Radius(Sizes[i]) + Footprint.Radius(Sizes[other]) + .1f)
                    arrived = true;
            if (arrived)
            {
                unit.Mode = MovementMode.Idle; unit.HasMoveOrder = false; unit.Home = Positions[i].xz;
                unit.Velocity = unit.PreferredVelocity = unit.NewVelocity = float2.zero;
                Planned[i] = unit; return;
            }
            FlowSampleStatus status = Fields.Sample(FlowFieldReader.NoField, Positions[i].xz, out FlowSample sample);
            float2 direction = status == FlowSampleStatus.Ready ? sample.Direction : math.normalizesafe(delta);
            // Reynolds arrival, bounded by the remaining step so a direct seek cannot orbit its goal.
            float speed = math.min(Settings.Speed(Sizes[i]), math.sqrt(2 * Settings.Deceleration * distance));
            speed = math.min(speed, distance / MovementParameters.TickDuration);
            unit.PreferredVelocity = direction * speed;
            unit.NewVelocity = unit.PreferredVelocity;
            Planned[i] = unit;
        }
    }
}
