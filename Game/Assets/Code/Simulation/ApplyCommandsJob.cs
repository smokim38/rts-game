using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Northwatch.Simulation.Movement;
using Unity.Mathematics;

namespace Northwatch.Simulation
{
    [BurstCompile]
    internal struct ApplyCommandsJob : IJob
    {
        internal NativeArray<UnitCommand> Commands;
        internal NativeArray<UnitCommand> SideOrders;
        internal bool RenewEndedOrders;
        internal NativeArray<int> CommandCount, QueuedMoves;
        internal UnitState State;
        internal ulong Tick;
        internal float SlotSpacing;
        [ReadOnly] internal NativeArray<byte> Walkable;

        public void Execute()
        {
            bool deferred = false;
            for (int i = 0; i < CommandCount[0]; i++)
            {
                UnitCommand command = Commands[i];
                if (command.Consumed) continue;
                if (command.ExecuteTick > Tick)
                {
                    deferred = true;
                    continue;
                }
                // Keep response slots stable while a future order is pending.
                command.Consumed = true;
                Commands[i] = command;
                if (command.BlockCount > 0)
                {
                    PlaceBlock(command);
                    continue;
                }
                int slot = command.Handle.Slot;
                if (command.Move)
                {
                    if (command.AllOnSide)
                    {
                        SideOrders[(int)command.Side] = command;
                        float2 centre = 0;
                        int members = 0;
                        for (int member = 0; member < State.Alive.Length; member++)
                            if (State.Alive[member] != 0 && State.Sides[member] == command.Side)
                            { centre += State.Positions[member].xz; members++; }
                        centre /= math.max(1, members);
                        for (int member = 0; member < State.Alive.Length; member++)
                            if (State.Alive[member] != 0 && State.Sides[member] == command.Side)
                            {
                                var unit = State.Movement[member];
                                unit.OrderVersion = (int)(Tick * 2 + (uint)command.Side);
                                unit.SpotOffset = State.Positions[member].xz - centre;
                                State.Movement[member] = unit;
                                Move(member, command.Position.xz);
                            }
                        continue;
                    }
                    QueuedMoves[slot] = 0;
                    if (State.Alive[slot] == 0 || State.Generations[slot] != command.Handle.Generation) continue;
                    var selected = State.Movement[slot];
                    selected.SpotOffset = command.SpotOffset;
                    State.Movement[slot] = selected;
                    Move(slot, command.Position.xz);
                    continue;
                }
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
                    if (!EntryClear(command)) continue;
                    AddUnit(ref command);
                    Commands[i] = command;
                }
            }
            if (!deferred) CommandCount[0] = 0;
            if (RenewEndedOrders) RenewOrders();
        }

        private void RenewOrders()
        {
            // This runs before movement on every tick, including batched ticks. An
            // order ended by the previous movement step must be replaced now.
            for (int slot = 0; slot < State.Alive.Length; slot++)
            {
                if (State.Alive[slot] == 0) continue;
                MovementUnit unit = State.Movement[slot];
                if (unit.HasMoveOrder) continue;
                UnitCommand order = SideOrders[(int)State.Sides[slot]];
                if (!order.RenewEnded || unit.ArrivalRule < 1 || unit.ArrivalRule > 4)
                {
                    State.Summary[13] = 1;
                    continue;
                }
                State.Summary[8 + unit.ArrivalRule]++;
                unit.OrderVersion = (int)(Tick * 2 + (uint)order.Side);
                State.Movement[slot] = unit;
                Move(slot, order.Position.xz);
            }
        }

        private void Move(int slot, float2 goal)
        {
            MovementUnit unit = State.Movement[slot];
            unit.Goal = goal;
            float radius = Footprint.Radius(State.Sizes[slot]);
            unit.Spot = math.clamp(goal + unit.SpotOffset, -UnitSimulation.MapHalfWidth + radius, UnitSimulation.MapHalfWidth - radius);
            unit.HasOwnSpot = true;
            unit.GoalResolved = false;
            unit.GoalRegion = 0;
            unit.HasMoveOrder = true;
            unit.Mode = MovementMode.Moving;
            unit.BestGoalDistance = math.distance(unit.Spot, State.Positions[slot].xz);
            unit.LastProgressTick = Tick;
            unit.ProgressField = -1;
            unit.ArrivalRule = 0;
            State.Movement[slot] = unit;
        }

        private void PlaceBlock(UnitCommand command)
        {
            State.Summary[6]++;
            State.Summary[5] = -1;
            if (command.BlockCount > State.Alive.Length - State.Summary[0]) return;
            var block = new BlockPlacement(command.BlockCount, command.Size, command.Position,
                SlotSpacing > 0 ? SlotSpacing : MovementParameters.DefaultSlotSpacing, command.ReverseRows);
            // One area query per spawn order, like selection; no per-unit neighbour searches.
            for (int slot = 0; slot < State.Alive.Length; slot++)
                if (State.Alive[slot] != 0 && block.Overlaps(State.Positions[slot], State.Sizes[slot])) return;
            for (int index = 0; index < command.BlockCount; index++)
                if (!StaticClamp.IsClear(block.Position(index).xz, Footprint.Radius(command.Size), Walkable)) return;
            for (int index = 0; index < command.BlockCount; index++)
            {
                command.Position = block.Position(index);
                AddUnit(ref command);
            }
            State.Summary[5] = command.BlockCount;
        }

        private bool EntryClear(UnitCommand command)
        {
            for (int slot = 0; slot < State.Alive.Length; slot++)
            {
                if (State.Alive[slot] == 0) continue;
                if (Footprint.DiscsTooClose(command.Position.xz, command.Size, State.Positions[slot].xz, State.Sizes[slot])) return false;
            }
            return true;
        }

        private void AddUnit(ref UnitCommand command)
        {
            if (!StaticClamp.IsClear(command.Position.xz, Footprint.Radius(command.Size), Walkable)) return;
            int slot = State.Summary[2] > 0 ? State.FreeSlots[--State.Summary[2]] : State.Summary[1]++;
            if (State.Generations[slot] == 0) State.Generations[slot] = 1;
            State.Positions[slot] = command.Position;
            State.Movement[slot] = new MovementUnit
            {
                Previous = new MovementPose { Position = command.Position }, Home = command.Position.xz
            };
            State.Sides[slot] = command.Side;
            State.Sizes[slot] = command.Size;
            State.Alive[slot] = 1;
            State.Summary[0]++;
            command.Handle = new UnitHandle(slot, State.Generations[slot]);
            command.Applied = true;
        }
    }
}
