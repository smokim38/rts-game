using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Simulation
{
    public sealed class UnitSimulation : IDisposable
    {
        public const int Capacity = 2000;
        internal UnitState State;
        private NativeArray<UnitCommand> commands;
        private NativeArray<Matrix4x4> matrices;
        private JobHandle pending;
        private int commandCount, queuedAdds;
        private ulong commandBatch;
        private bool scheduled, disposed;
        public ulong TickCount { get; private set; }

        public UnitSimulation()
        {
            State = new UnitState(Capacity);
            commands = new NativeArray<UnitCommand>(Capacity * 2, Allocator.Persistent);
            matrices = new NativeArray<Matrix4x4>(Capacity * 2, Allocator.Persistent);
        }

        public int Count { get { RequireComplete(); return State.Summary[0]; } }

        public bool QueueAdd(Side side, UnitSize size, Vector3 position)
            => QueueAdd(side, size, position, out _);

        // Resolve the request after a tick, before later commands overwrite its queue entry.
        public bool QueueAdd(Side side, UnitSize size, Vector3 position, out ulong requestId)
        {
            RequireComplete();
            requestId = 0;
            if ((byte)side > 1 || (byte)size > 1 || !math.all(math.isfinite((float3)position)) ||
                Count + queuedAdds >= Capacity || commandCount == commands.Length) return false;
            requestId = commandBatch * (ulong)commands.Length + (uint)commandCount + 1;
            commands[commandCount++] = new UnitCommand { Side = side, Size = size, Position = position, RequestId = requestId };
            queuedAdds++;
            return true;
        }

        public bool TryGetAddedHandle(ulong requestId, out UnitHandle handle)
        {
            RequireComplete();
            handle = default;
            if (requestId == 0) return false;
            UnitCommand command = commands[(int)((requestId - 1) % (ulong)commands.Length)];
            if (command.RequestId != requestId || !command.Applied || !IsAlive(command.Handle)) return false;
            handle = command.Handle;
            return true;
        }

        public bool IsAlive(UnitHandle handle)
        {
            RequireComplete();
            return (uint)handle.Slot < Capacity && State.Alive[handle.Slot] != 0 &&
                State.Generations[handle.Slot] == handle.Generation;
        }

        public bool QueueRemove(UnitHandle handle)
        {
            if (!IsAlive(handle) || commandCount == commands.Length) return false;
            commands[commandCount++] = new UnitCommand { Remove = true, Handle = handle };
            return true;
        }

        public void ScheduleFrame(int ticks)
        {
            RequireComplete();
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            for (int tick = 0; tick < ticks; tick++)
            {
                pending = new ApplyCommandsJob
                {
                    State = State, Commands = commands, CommandCount = tick == 0 ? commandCount : 0
                }.Schedule(pending);
                TickCount++;
            }
            if (ticks > 0) { commandCount = 0; queuedAdds = 0; commandBatch++; }
            pending = new FillDrawDataJob
            {
                Positions = State.Positions, Sides = State.Sides, Sizes = State.Sizes,
                Alive = State.Alive, Summary = State.Summary, Matrices = matrices
            }.Schedule(pending);
            scheduled = true;
        }

        public void CompleteFrame()
        {
            if (!scheduled) return;
            pending.Complete();
            scheduled = false;
        }

        public int DrawCount(Side side)
        {
            RequireComplete();
            if ((byte)side > 1) throw new ArgumentOutOfRangeException(nameof(side));
            return State.Summary[3 + (int)side];
        }

        // Draw-only output, valid until the next ScheduleFrame. Consumers must not modify it.
        public NativeArray<Matrix4x4> DrawMatrices
        {
            get { RequireComplete(); return matrices; }
        }

        private void RequireComplete()
        {
            if (disposed) throw new ObjectDisposedException(nameof(UnitSimulation));
            if (scheduled) throw new InvalidOperationException("Complete the frame before accessing simulation data or queuing commands.");
        }

        public void Dispose()
        {
            if (disposed) return;
            CompleteFrame();
            State.Dispose();
            commands.Dispose();
            matrices.Dispose();
            disposed = true;
        }
    }
}
