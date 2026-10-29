using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct InitialMoveCheckJob : IJobParallelFor
    {
        [ReadOnly] internal MoveCheckData Data;
        [WriteOnly] internal NativeArray<byte> Choices;

        public void Execute(int i) { Choices[i] = Data.Alive[i] == 0 ? (byte)0 : Data.Choose(i, 0); }
    }
}
