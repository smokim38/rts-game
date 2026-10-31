using System;
using Northwatch.Simulation.Movement;
using Northwatch.Simulation.FlowFields;
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
        internal StaticMaps StaticMaps { get; private set; }
        private StaticMaps openFieldMaps;
        public double StaticMapBuildMilliseconds { get; private set; }
        public long StaticMapBytes => StaticMaps?.MapBytes ?? 0;
        public long StaticMapScratchBytes => StaticMaps?.ScratchBytes ?? 0;
        private NativeArray<UnitCommand> commands;
        private NativeArray<int> commandCounts, queuedMoves;
        private NativeArray<Matrix4x4> matrices, nextMatrices;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public MovementMeasurement Measurement { get; private set; }
        public long GoalSearchCount { get { RequireComplete(); return movement?.Fields?.GoalMetrics[0] ?? 0; } }
        public long FieldRequestCount { get { RequireComplete(); return movement?.Fields?.GoalMetrics[2] ?? 0; } }

        public bool TrySelectedRoute(out int field, out byte size, out ulong published)
        {
            RequireComplete(); field = -1; size = 0; published = 0;
            if (movement?.Fields == null) return false;
            for (int i = 0; i < selected.Length; i++)
                if (State.Alive[i] != 0 && selected[i] == State.Generations[i])
                {
                    size = (byte)State.Sizes[i]; field = movement.Fields.UnitFields[i];
                    if (field >= 0 && movement.Fields.Entries[field].Valid != 0) published = movement.Fields.Entries[field].Published;
                    else field = -1;
                    return true;
                }
            return false;
        }

        public ushort RouteRegion(int cell, byte size)
        {
            RequireComplete();
            return (StaticMaps ?? openFieldMaps).Regions[size * 65536 + cell];
        }

        public bool RouteDirection(int field, Vector3 point, out Vector3 direction)
        {
            RequireComplete(); direction = default;
            if (movement?.Fields == null || movement.Fields.Reader.Sample(field, ((float3)point).xz, out var sample) != FlowSampleStatus.Ready)
                return false;
            direction = new Vector3(sample.Direction.x, 0, sample.Direction.y);
            return sample.Distance > 0;
        }

        public void MeasureMovement(int scene, int count)
        {
            RequireComplete();
            if (movement == null || Measurement != null || TickCount != 0)
                throw new InvalidOperationException("Enable movement measurement once, before the first frame.");
            Measurement = new MovementMeasurement(State, scene, count);
            if (scene >= 5)
            {
                if (StaticMaps == null) throw new InvalidOperationException("Obstacle measurement requires the sandbox layout.");
                Measurement.Walkable.CopyFrom(StaticMaps.Walkable);
            }
            MovementParameters settings = movement.Settings;
            movement.Dispose();
            movement = new MovementPipeline(Capacity, settings, Measurement.Walkable, StaticMaps ?? openFieldMaps);
            int smallCount = scene == 5 && count == 110 ? 100 : count;
            if (scene >= 4 && !QueueBlock(Side.First, UnitSize.Small, scene == 6 ? new Vector3(60, 0, 30) : Vector3.zero, smallCount))
                throw new InvalidOperationException("Could not queue the measured open-map block.");
            if (scene == 5 && count == 110 && !QueueBlock(Side.First, UnitSize.Large, new Vector3(-12, 0, -15), 10))
                throw new InvalidOperationException("Could not queue the measured large cubes.");
        }
#endif
        private NativeArray<uint> selected, groups;
        private NativeArray<byte> emptyWalkable;
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
            emptyWalkable = new NativeArray<byte>(0, Allocator.Persistent);
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
            try
            {
                settings.Validate(); settings.ValidateFields(256, 256);
                openFieldMaps = new StaticMaps(256, 256);
                using var boxes = new NativeArray<StaticBox>(0, Allocator.TempJob);
                openFieldMaps.ScheduleBuild(boxes).Complete();
                movement = new MovementPipeline(Capacity, settings, staticMaps: openFieldMaps);
            }
            catch { Dispose(); throw; }
        }

        public UnitSimulation(MovementParameters settings, NativeArray<StaticBox> boxes) : this()
        {
            try
            {
                settings.Validate();
                settings.ValidateFields(StaticClamp.MapWidth, StaticClamp.MapWidth);
                StaticMaps = new StaticMaps(StaticClamp.MapWidth, StaticClamp.MapWidth);
                long began = System.Diagnostics.Stopwatch.GetTimestamp();
                StaticMaps.ScheduleBuild(boxes).Complete();
                StaticMapBuildMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - began) *
                    1000.0 / System.Diagnostics.Stopwatch.Frequency;
                movement = new MovementPipeline(Capacity, settings, StaticMaps.Walkable, StaticMaps);
            }
            catch { Dispose(); throw; }
        }

        public int SelectedCount { get { RequireComplete(); return (int)selectionSummary[0].w; } }
        public Vector3 SelectedCentre { get { RequireComplete(); return selectionSummary[0].xyz; } }
        public int MoveMarkerVersion { get { RequireComplete(); return (int)selectionSummary[1].w; } }
        public int MoveMarkerCount { get { RequireComplete(); return (int)selectionSummary[2].y; } }
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
                Alive = State.Alive, Selected = selected, QueuedMoves = queuedMoves, Summary = State.Summary, Matrices = nextMatrices,
                Orders = selectionSummary, Seconds = seconds
            }.Schedule(pending);
            scheduled = true;
        }

        private JobHandle ApplyCommands(JobHandle dependency) => new ApplyCommandsJob
        {
            State = State, Commands = commands, CommandCount = commandCounts, QueuedMoves = queuedMoves, Tick = TickCount + 1,
            SlotSpacing = movement == null ? MovementParameters.DefaultSlotSpacing : movement.Settings.SlotSpacing,
            Walkable = StaticMaps == null ? emptyWalkable : StaticMaps.Walkable
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
            StaticMaps?.Dispose();
            openFieldMaps?.Dispose();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Measurement?.Dispose();
#endif
            commands.Dispose();
            emptyWalkable.Dispose();
            commandCounts.Dispose(); queuedMoves.Dispose();
            matrices.Dispose();
            nextMatrices.Dispose();
            selected.Dispose(); groups.Dispose(); selectionSummary.Dispose(); selectionRequests.Dispose();
            disposed = true;
        }
    }
}
