using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct IntegrateMovementJob : IJobFor
    {
        [ReadOnly] internal NativeArray<float3> Positions;
        [ReadOnly] internal NativeArray<UnitSize> Sizes;
        [ReadOnly] internal NativeArray<byte> Alive, Walkable;
        [ReadOnly] internal NativeArray<MovementUnit> Units;
        [WriteOnly] internal NativeArray<MovementPose> Start, Integrated;
        [WriteOnly] internal NativeArray<float2> Velocities;
        internal MovementParameters Settings;

        public void Execute(int i)
        {
            if (Alive[i] == 0) return;
            MovementUnit unit = Units[i];
            MovementPose pose = new MovementPose { Position = Positions[i], Heading = unit.Heading };
            Start[i] = pose;
            float2 target = unit.Mode == MovementMode.Moving ? unit.NewVelocity : float2.zero;
            float speed = Settings.Speed(Sizes[i]);
            target *= math.min(1, speed / math.max(math.length(target), 1e-20f));
            float2 delta = target - unit.Velocity;
            float rate = math.lengthsq(target) < math.lengthsq(unit.Velocity) ? Settings.Deceleration : Settings.Acceleration;
            float2 velocity = unit.Mode == MovementMode.Hold ? float2.zero :
                unit.Velocity + delta * math.min(1, rate * MovementParameters.TickDuration / math.max(math.length(delta), 1e-20f));
            // Enforce the unit-type step bound even when a future stage supplies a larger input.
            velocity *= math.min(1, speed / math.max(math.length(velocity), 1e-20f));
            if (unit.Mode != MovementMode.Hold && math.any(velocity != 0))
            {
                float2 next = StaticClamp.Apply(pose.Position.xz + velocity * MovementParameters.TickDuration, Footprint.Radius(Sizes[i]), Walkable);
                if (math.distance(next, pose.Position.xz) <= Settings.StepBound)
                {
                    pose.Position.x = next.x; pose.Position.z = next.y;
                }
                if (unit.Mode != MovementMode.Idle)
                {
                    float angle = math.atan2(velocity.y, velocity.x);
                    float difference = math.atan2(math.sin(angle - pose.Heading), math.cos(angle - pose.Heading));
                    pose.Heading += math.clamp(difference, -Settings.TurnSpeed * MovementParameters.TickDuration, Settings.TurnSpeed * MovementParameters.TickDuration);
                }
            }
            Integrated[i] = pose;
            Velocities[i] = velocity;
        }
    }
}
