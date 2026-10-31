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
        [ReadOnly] internal NativeParallelHashMap<int, ulong> LatestProgress;
        [WriteOnly] internal NativeArray<MovementUnit> Planned;
        [ReadOnly] internal SpatialGrid Grid;
        internal FlowFieldReader Fields;
        [ReadOnly] internal NativeArray<int> UnitFields;
        internal bool HasFields;
        internal MovementParameters Settings;
        internal ulong Tick;

        public void Execute(int i)
        {
            if (Alive[i] == 0) return;
            MovementUnit unit = Units[i];
            if (!unit.HasMoveOrder) { Planned[i] = unit; return; }
            int field = HasFields ? UnitFields[i] : -1;
            FlowSampleStatus status = Fields.Sample(field, Positions[i].xz, out FlowSample sample);
            float2 target = status == FlowSampleStatus.Ready && sample.InSight
                ? math.clamp(unit.Goal, sample.Goal - .5f, sample.Goal + .5f) : unit.Goal;
            float2 delta = target - Positions[i].xz;
            float distance = math.length(delta);
            float remaining = status == FlowSampleStatus.Ready && !sample.InSight ? sample.Distance : distance;
            if (HasFields && field != unit.ProgressField || remaining <= unit.BestGoalDistance - .1f)
            {
                unit.ProgressField = field;
                unit.BestGoalDistance = remaining;
                unit.LastProgressTick = Tick;
            }
            byte arrivalRule = distance <= .05f && math.lengthsq(unit.Velocity) < .01f ? (byte)1 : (byte)0;
            // Without formation slots these are the ADR 0008 overflow arrivals:
            // stop on the route beside an already arrived member of this order.
            var query = new SpatialQuery(Grid, Positions[i].xz, Footprint.Radius(Sizes[i]) + Footprint.LargeRadius + .1f);
            while (arrivalRule == 0 && query.Next(out int other))
                if (other != i && Units[other].OrderVersion == unit.OrderVersion &&
                    Units[other].Mode == MovementMode.Idle &&
                    math.distance(Positions[i].xz, Positions[other].xz) <= Footprint.Radius(Sizes[i]) + Footprint.Radius(Sizes[other]) + .1f)
                    arrivalRule = 2;
            // Interim goal-disc radius until ADR 0008 Issue 4 (groups and arrival).
            // Further out, only the order-wide six-second stall can end the order.
            if (arrivalRule == 0 && distance <= Footprint.Radius(Sizes[i]) + .1f && Tick - unit.LastProgressTick >= 30)
                arrivalRule = 3;
            if (arrivalRule == 0 && LatestProgress.TryGetValue(unit.OrderVersion, out ulong latest) && Tick - latest >= 180)
                arrivalRule = 4;
            if (arrivalRule != 0)
            {
                unit.ArrivalRule = arrivalRule;
                unit.Mode = MovementMode.Idle; unit.HasMoveOrder = false; unit.Home = Positions[i].xz;
                unit.Velocity = unit.PreferredVelocity = unit.NewVelocity = float2.zero;
                Planned[i] = unit; return;
            }
            float2 direction = status == FlowSampleStatus.Ready && !sample.InSight ? sample.Direction : math.normalizesafe(delta);
            // Reynolds arrival, bounded by the remaining step so a direct seek cannot orbit its goal.
            float speed = math.min(Settings.Speed(Sizes[i]), math.sqrt(2 * Settings.Deceleration * distance));
            speed = math.min(speed, distance / MovementParameters.TickDuration);
            unit.PreferredVelocity = direction * speed;
            unit.NewVelocity = unit.PreferredVelocity;
            Planned[i] = unit;
        }
    }
}
