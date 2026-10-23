using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation
{
    [BurstCompile]
    internal struct ApplyCommandsJob : IJob
    {
        [ReadOnly] internal NativeArray<UnitCommand> Commands;
        internal int CommandCount;
        internal UnitState State;

        public void Execute()
        {
            for (int i = 0; i < CommandCount; i++)
            {
                UnitCommand command = Commands[i];
                int slot = command.Handle.Slot;
                if (command.Remove)
                {
                    if (State.Alive[slot] == 0 || State.Generations[slot] != command.Handle.Generation) continue;
                    State.Alive[slot] = 0;
                    State.Generations[slot]++;
                    State.FreeSlots[State.Summary[2]++] = slot;
                    State.Summary[0]--;
                }
                else if (State.Summary[0] < State.Alive.Length)
                {
                    slot = State.Summary[2] > 0 ? State.FreeSlots[--State.Summary[2]] : State.Summary[1]++;
                    if (State.Generations[slot] == 0) State.Generations[slot] = 1;
                    State.Positions[slot] = command.Position;
                    State.Sides[slot] = command.Side;
                    State.Sizes[slot] = command.Size;
                    State.Alive[slot] = 1;
                    State.Summary[0]++;
                }
            }
        }
    }
}
