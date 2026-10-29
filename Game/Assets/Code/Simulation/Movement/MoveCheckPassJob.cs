using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct MoveCheckPassJob : IJobFor
    {
        [ReadOnly] internal MoveCheckData Data;
        [WriteOnly] internal NativeArray<byte> NextLevels;

        public void Execute(int i) { NextLevels[i] = Data.Alive[i] == 0 ? (byte)0 : Data.Choose(i, Data.Levels[i]); }
    }
}
