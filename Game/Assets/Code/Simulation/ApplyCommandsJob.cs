using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Northwatch.Simulation
{
    [BurstCompile]
    internal struct ApplyCommandsJob : IJob
    {
        internal NativeArray<UnitCommand> Commands;
        internal int CommandCount;
        internal UnitState State;

        public void Execute()
        {
            for (int i = 0; i < CommandCount; i++)
            {
                UnitCommand command = Commands[i];
                if (command.BlockCount > 0)
                {
                    PlaceBlock(command);
                    continue;
                }
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
                    AddUnit(ref command);
                    Commands[i] = command;
                }
            }
        }

        private void PlaceBlock(UnitCommand command)
        {
            State.Summary[6]++;
            State.Summary[5] = -1;
            if (command.BlockCount > State.Alive.Length - State.Summary[0]) return;
            var block = new BlockPlacement(command.BlockCount, command.Size, command.Position);
            // One area query per spawn order, like selection; no per-unit neighbour searches.
            for (int slot = 0; slot < State.Alive.Length; slot++)
                if (State.Alive[slot] != 0 && block.Overlaps(State.Positions[slot], State.Sizes[slot])) return;
            for (int index = 0; index < command.BlockCount; index++)
            {
                command.Position = block.Position(index);
                AddUnit(ref command);
            }
            State.Summary[5] = command.BlockCount;
        }

        private void AddUnit(ref UnitCommand command)
        {
            int slot = State.Summary[2] > 0 ? State.FreeSlots[--State.Summary[2]] : State.Summary[1]++;
            if (State.Generations[slot] == 0) State.Generations[slot] = 1;
            State.Positions[slot] = command.Position;
            State.Sides[slot] = command.Side;
            State.Sizes[slot] = command.Size;
            State.Alive[slot] = 1;
            State.Summary[0]++;
            command.Handle = new UnitHandle(slot, State.Generations[slot]);
            command.Applied = true;
        }
    }
}
