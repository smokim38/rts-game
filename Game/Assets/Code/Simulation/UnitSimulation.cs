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
        public const float MapHalfWidth = 128f;
        internal UnitState State;
        private NativeArray<UnitCommand> commands;
        private NativeArray<Matrix4x4> matrices;
        private NativeArray<uint> selected, groups;
        private NativeArray<float4> selectionSummary;
        private NativeArray<SelectionRequest> selectionRequests;
        private int selectionRequestCount;
        private JobHandle pending;
        private int commandCount, queuedAdds;
        private ulong commandBatch;
        private bool scheduled, disposed;
        public ulong TickCount { get; private set; }

        public UnitSimulation()
        {
            State = new UnitState(Capacity);
            commands = new NativeArray<UnitCommand>(Capacity * 2, Allocator.Persistent);
            matrices = new NativeArray<Matrix4x4>(Capacity * 3, Allocator.Persistent);
            selected = new NativeArray<uint>(Capacity, Allocator.Persistent);
            groups = new NativeArray<uint>(Capacity * 9, Allocator.Persistent);
            selectionSummary = new NativeArray<float4>(2, Allocator.Persistent);
            selectionRequests = new NativeArray<SelectionRequest>(16, Allocator.Persistent);
        }

        public int SelectedCount { get { RequireComplete(); return (int)selectionSummary[0].w; } }
        public Vector3 SelectedCentre { get { RequireComplete(); return selectionSummary[0].xyz; } }
        public int MoveMarkerVersion { get { RequireComplete(); return (int)selectionSummary[1].w; } }
        public Vector3 MoveMarkerPosition { get { RequireComplete(); return selectionSummary[1].xyz; } }

        public bool IsSelected(UnitHandle handle) => IsAlive(handle) && selected[handle.Slot] == handle.Generation;

        public bool QueueSelection(SelectionRequest request)
        {
            RequireComplete();
            if (request.Kind < 1 || request.Kind > 6 || selectionRequestCount == selectionRequests.Length) return false;
            selectionRequests[selectionRequestCount++] = request;
            return true;
        }

        public int Count { get { RequireComplete(); return State.Summary[0]; } }
        public int RemainingCapacity { get { RequireComplete(); return Capacity - Count - queuedAdds; } }
        public int PlacementResult { get { RequireComplete(); return State.Summary[5]; } }
        public int PlacementVersion { get { RequireComplete(); return State.Summary[6]; } }

        public bool QueueBlock(Side side, UnitSize size, Vector3 centre, int count)
        {
            RequireComplete();
            if ((byte)side > 1 || (byte)size > 1 || count < 1 || count > RemainingCapacity ||
                !math.all(math.isfinite((float3)centre)) || math.abs(centre.x) > MapHalfWidth ||
                math.abs(centre.z) > MapHalfWidth || commandCount == commands.Length) return false;
            commands[commandCount++] = new UnitCommand
                { Side = side, Size = size, Position = centre, BlockCount = count };
            queuedAdds += count;
            return true;
        }

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
            pending = new SelectionJob
            {
                State = State, Requests = selectionRequests, RequestCount = selectionRequestCount,
                Selected = selected, Groups = groups, Summary = selectionSummary
            }.Schedule(pending);
            selectionRequestCount = 0;
            pending = new FillDrawDataJob
            {
                Positions = State.Positions, Sides = State.Sides, Sizes = State.Sizes,
                Alive = State.Alive, Selected = selected, Summary = State.Summary, Matrices = matrices
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
            selected.Dispose(); groups.Dispose(); selectionSummary.Dispose(); selectionRequests.Dispose();
            disposed = true;
        }
    }
}
