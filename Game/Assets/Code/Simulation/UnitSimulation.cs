using System;
using Northwatch.Simulation.Movement;
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
        private MovementPipeline movement;
        private NativeArray<UnitCommand> commands;
        private NativeArray<int> commandCounts, queuedMoves;
        private NativeArray<Matrix4x4> matrices, nextMatrices;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public MovementMeasurement Measurement { get; private set; }

        public void MeasureMovement(int scene, int count)
        {
            RequireComplete();
            if (movement == null || Measurement != null || TickCount != 0)
                throw new InvalidOperationException("Enable movement measurement once, before the first frame.");
            Measurement = new MovementMeasurement(State, scene, count);
            MovementParameters settings = movement.Settings;
            movement.Dispose();
            movement = new MovementPipeline(Capacity, settings, Measurement.Walkable);
            if (scene == 4 && !QueueBlock(Side.First, UnitSize.Small, Vector3.zero, count))
                throw new InvalidOperationException("Could not queue the measured open-map block.");
        }
#endif
        private NativeArray<uint> selected, groups;
        private NativeArray<float4> selectionSummary;
        private NativeArray<SelectionRequest> selectionRequests;
        private int selectionRequestCount;
        private JobHandle pending;
        private int commandCount { get => commandCounts[0]; set => commandCounts[0] = value; }
        private int queuedAdds;
        private ulong commandBatch;
        private bool scheduled, disposed;
        public ulong TickCount { get; private set; }

        public UnitSimulation()
        {
            State = new UnitState(Capacity);
            commands = new NativeArray<UnitCommand>(Capacity * 4, Allocator.Persistent);
            commandCounts = new NativeArray<int>(1, Allocator.Persistent);
            queuedMoves = new NativeArray<int>(Capacity, Allocator.Persistent);
            matrices = new NativeArray<Matrix4x4>(Capacity * 4, Allocator.Persistent);
            nextMatrices = new NativeArray<Matrix4x4>(Capacity * 4, Allocator.Persistent);
            selected = new NativeArray<uint>(Capacity, Allocator.Persistent);
            groups = new NativeArray<uint>(Capacity * 9, Allocator.Persistent);
            selectionSummary = new NativeArray<float4>(3, Allocator.Persistent);
            selectionRequests = new NativeArray<SelectionRequest>(16, Allocator.Persistent);
        }

        public UnitSimulation(MovementParameters settings) : this()
        {
            try { movement = new MovementPipeline(Capacity, settings); }
            catch { Dispose(); throw; }
        }

        public int SelectedCount { get { RequireComplete(); return (int)selectionSummary[0].w; } }
        public Vector3 SelectedCentre { get { RequireComplete(); return selectionSummary[0].xyz; } }
        public int MoveMarkerVersion { get { RequireComplete(); return (int)selectionSummary[1].w; } }
        public Vector3 MoveMarkerPosition { get { RequireComplete(); return selectionSummary[1].xyz; } }
        public int AcknowledgementCount { get { RequireComplete(); return State.Summary[7]; } }

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
                math.abs(centre.z) > MapHalfWidth || commandCount >= Capacity * 2) return false;
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
                Count + queuedAdds >= Capacity || commandCount >= Capacity * 2) return false;
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
            if (!IsAlive(handle) || commandCount >= Capacity * 2) return false;
            commands[commandCount++] = new UnitCommand { Remove = true, Handle = handle };
            return true;
        }

        public void ScheduleFrame(int ticks, float interpolation = 1f, float seconds = 0)
        {
            RequireComplete();
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            if (!math.isfinite(interpolation) || interpolation < 0 || interpolation > 1)
                throw new ArgumentOutOfRangeException(nameof(interpolation));
            if (ticks > 0) pending = ApplyCommands(pending);
            pending = new SelectionJob
            {
                State = State, Requests = selectionRequests, RequestCount = selectionRequestCount,
                Selected = selected, Groups = groups, Summary = selectionSummary,
                Commands = commands, CommandCount = commandCounts, QueuedMoves = queuedMoves, Seconds = seconds
            }.Schedule(pending);
            selectionRequestCount = 0;
            for (int tick = 0; tick < ticks; tick++)
            {
                pending = ApplyCommands(pending);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (Measurement != null)
                {
                    pending = Measurement.Input(State, pending);
                    pending = movement.Schedule(State, pending, 64, Measurement.Timestamps, Measurement.TickCount * MovementMeasurement.TimestampStride, TickCount + 1);
                    pending = Measurement.Record(State, movement, pending);
                }
                else
#endif
                if (movement != null) pending = movement.Schedule(State, pending, tick: TickCount + 1);
                TickCount++;
            }
            if (ticks > 0) { queuedAdds = 0; commandBatch++; }
            pending = new FillDrawDataJob
            {
                Positions = State.Positions, Sides = State.Sides, Sizes = State.Sizes,
                Movement = State.Movement, Interpolation = interpolation,
                Alive = State.Alive, Selected = selected, Summary = State.Summary, Matrices = nextMatrices,
                Orders = selectionSummary, Seconds = seconds
            }.Schedule(pending);
            scheduled = true;
        }

        private JobHandle ApplyCommands(JobHandle dependency) => new ApplyCommandsJob
        {
            State = State, Commands = commands, CommandCount = commandCounts, QueuedMoves = queuedMoves, Tick = TickCount + 1,
            SlotSpacing = movement == null ? MovementParameters.DefaultSlotSpacing : movement.Settings.SlotSpacing
        }.Schedule(dependency);

        public void CompleteFrame()
        {
            if (!scheduled) return;
            pending.Complete();
            (matrices, nextMatrices) = (nextMatrices, matrices);
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
            movement?.Dispose();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Measurement?.Dispose();
#endif
            commands.Dispose();
            commandCounts.Dispose(); queuedMoves.Dispose();
            matrices.Dispose();
            nextMatrices.Dispose();
            selected.Dispose(); groups.Dispose(); selectionSummary.Dispose(); selectionRequests.Dispose();
            disposed = true;
        }
    }
}
