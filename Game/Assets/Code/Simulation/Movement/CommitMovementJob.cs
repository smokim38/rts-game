using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct CommitMovementJob : IJobFor
    {
        [ReadOnly] internal NativeArray<MovementPose> Start, Poses;
        [ReadOnly] internal NativeArray<float2> Velocities;
        [ReadOnly] internal NativeArray<byte> Alive, Levels;
        internal NativeArray<float3> Positions;
        internal NativeArray<MovementUnit> Units;

        public void Execute(int i)
        {
            if (Alive[i] == 0) return;
            MovementUnit unit = Units[i];
            unit.Previous = Start[i];
            unit.Heading = Poses[i].Heading;
            unit.Velocity = Levels[i] == 2 ? float2.zero : Velocities[i];
            Units[i] = unit;
            Positions[i] = Poses[i].Position;
        }
    }
}
