using System;
using System.Collections.Generic;
using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.FlowFields;
using Northwatch.Simulation.Movement;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation.FlowFields
{
    public sealed class FlowFieldSightTests
    {
        private sealed class Field : IDisposable
        {
            internal NativeArray<byte> Directions, Classes;
            internal NativeArray<ushort> Distances;
            internal NativeArray<int> Goals, Costs, Heap, Positions;
            internal NativeArray<int4> A, B;
            internal NativeArray<FlowFieldPool.Build> Build;
            internal NativeArray<long> Times;
            internal FlowFieldReader Reader;
            internal Field(StaticMaps maps, int goal, byte size)
            {
                int cells = maps.Width * maps.Height;
                Directions = new NativeArray<byte>(cells, Allocator.Persistent);
                Distances = new NativeArray<ushort>(cells, Allocator.Persistent);
                Costs = new NativeArray<int>(cells, Allocator.Persistent);
                Heap = new NativeArray<int>(cells, Allocator.Persistent);
                Positions = new NativeArray<int>(cells, Allocator.Persistent);
                Classes = new NativeArray<byte>(1, Allocator.Persistent); Classes[0] = size;
                Goals = new NativeArray<int>(1, Allocator.Persistent); Goals[0] = goal;
                Build = new NativeArray<FlowFieldPool.Build>(1, Allocator.Persistent);
                Build[0] = new FlowFieldPool.Build { Field = 0, Goal = goal, Class = size };
                Times = new NativeArray<long>(3, Allocator.Persistent);
                A = new NativeArray<int4>(math.max(maps.Width, maps.Height) + 2, Allocator.Persistent);
                B = new NativeArray<int4>(A.Length, Allocator.Persistent);
                new BuildFlowFieldJob { Width = maps.Width, Height = maps.Height, Passable = maps.Passable,
                    Builds = Build, Directions = Directions, Distances = Distances, Costs = Costs, Heap = Heap,
                    HeapPositions = Positions, Timestamps = Times, SightIntervalsA = A, SightIntervalsB = B }.Schedule().Complete();
                Reader = new FlowFieldReader { Width = maps.Width, Height = maps.Height,
                    Directions = Directions.AsReadOnly(), Distances = Distances.AsReadOnly(),
                    Classes = Classes.AsReadOnly(), Goals = Goals.AsReadOnly() };
            }
            public void Dispose()
            {
                Directions.Dispose(); Distances.Dispose(); Costs.Dispose(); Heap.Dispose(); Positions.Dispose();
                Classes.Dispose(); Goals.Dispose(); Build.Dispose(); Times.Dispose(); A.Dispose(); B.Dispose();
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void SightAndDirectionMatchExactIntegerOracle(int layout)
        {
            int width = layout == 6 ? 256 : 64;
            var boxes = new List<StaticBox>();
            if (layout == 1) boxes.Add(new StaticBox { Min = new int2(25, 20), MaxExclusive = new int2(28, 44) });
            if (layout >= 2 && layout <= 5)
            {
                var random = new Unity.Mathematics.Random((uint)(160 + layout));
                var used = new bool[width * width];
                int count = width * width * (layout == 5 ? 2 : 20) / 100;
                for (int i = 0; i < count; i++)
                {
                    int cell; do { cell = random.NextInt(used.Length); } while (used[cell]); used[cell] = true;
                    int2 p = new int2(cell % width, cell / width);
                    boxes.Add(new StaticBox { Min = p, MaxExclusive = p + 1 });
                }
            }
            if (layout == 6)
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<SandboxObstacles>("Assets/Data/SandboxObstacles.asset");
                for (int i = 0; i < asset.Count; i++)
                {
                    RectInt box = asset.Box(i);
                    boxes.Add(new StaticBox { Min = new int2(box.xMin, box.yMin), MaxExclusive = new int2(box.xMax, box.yMax) });
                }
            }
            using var nativeBoxes = new NativeArray<StaticBox>(boxes.ToArray(), Allocator.TempJob);
            using var maps = new StaticMaps(width, width);
            maps.ScheduleBuild(nativeBoxes).Complete();
            int[] targets = layout == 6 ? new[] { 128 * width + 158, 128 * width + 128 } :
                new[] { 32 * width + 32, 32 * width, 0, 32 * width + 29 };
            for (byte size = 0; size < 2; size++)
            {
                using var blocked = new NativeList<int2>(Allocator.TempJob);
                for (int i = 0; i < width * width; i++)
                    if (maps.Passable[size * width * width + i] == 0) blocked.Add(new int2(i % width, i / width));
                foreach (int target in targets)
                {
                    int goal = target;
                    while (maps.Passable[size * width * width + goal] == 0) goal = (goal + 1) % (width * width);
                    using var field = new Field(maps, goal, size);
                    using var errors = new NativeArray<int>(width * width, Allocator.TempJob);
                    new OracleJob { Width = width, Goal = goal, Class = size, Passable = maps.Passable,
                        Directions = field.Directions, Distances = field.Distances, Blocked = blocked.AsArray(), Errors = errors }
                        .Schedule(width * width, 64).Complete();
                    int sightErrors = 0, directionErrors = 0, first = -1;
                    for (int i = 0; i < errors.Length; i++)
                    {
                        if (errors[i] != 0 && first < 0) first = i;
                        if ((errors[i] & 1) != 0) sightErrors++;
                        if ((errors[i] & 2) != 0) directionErrors++;
                    }
                    Assert.That(sightErrors, Is.Zero, $"layout={layout}; class={size}; goal={goal}; first={first}; sight");
                    Assert.That(directionErrors, Is.Zero, $"layout={layout}; class={size}; goal={goal}; first={first}; direction");
                    // Half-cell sample grid, target corners/edge midpoints/centre and seeded interior points.
                    new ClearTargetsJob { Width = width, Class = size, Blocked = blocked.AsArray(),
                        Reader = field.Reader, Errors = errors }.Schedule(errors.Length, 64).Complete();
                    for (int i = 0; i < errors.Length; i++)
                        Assert.That(errors[i], Is.Zero, $"unsafe in-sight target; layout={layout}; class={size}; goal={goal}; cell={i}");
                }
            }
        }

        [TestCase(0)] [TestCase(1)]
        public void CorrectedRingGoalPublishesTheUnitsGoalAndMatchesSightOracle(byte size)
        {
            const int width = 32, cells = width * width;
            using var boxes = new NativeArray<StaticBox>(new[] {
                new StaticBox { Min = new int2(8, 8), MaxExclusive = new int2(16, 9) },
                new StaticBox { Min = new int2(8, 15), MaxExclusive = new int2(16, 16) },
                new StaticBox { Min = new int2(8, 9), MaxExclusive = new int2(9, 15) },
                new StaticBox { Min = new int2(15, 9), MaxExclusive = new int2(16, 15) } }, Allocator.TempJob);
            using var maps = new StaticMaps(width, width);
            maps.ScheduleBuild(boxes).Complete();
            var asset = ScriptableObject.CreateInstance<MovementSettings>();
            var settings = asset.Snapshot(); UnityEngine.Object.DestroyImmediate(asset);
            using var state = new UnitState(1);
            using var pool = new FlowFieldPool(width, width, 0, 1, settings);
            float anchor = (size + 1) * .5f;
            var alive = state.Alive; alive[0] = 1;
            var sizes = state.Sizes; sizes[0] = (UnitSize)size;
            var positions = state.Positions; positions[0] = new float3(2 + anchor, 0, 2 + anchor);
            var units = state.Movement;
            units[0] = new MovementUnit { HasMoveOrder = true, OrderVersion = 1, Goal = new float2(12 + anchor) };
            var handle = pool.RequestFields(state, maps, 0, default);
            pool.ScheduleBuilds(maps.Passable, 0, handle).Complete();
            handle = pool.Publish((ulong)settings.BuildLatencyTicks, default);
            pool.RequestFields(state, maps, (ulong)settings.BuildLatencyTicks, handle).Complete();
            int field = pool.UnitFields[0], goal = 12 * width + 16;
            Assert.That(field, Is.GreaterThanOrEqualTo(0));
            Assert.That(pool.Goals[field], Is.EqualTo(goal));
            float2 corrected = new float2(16, 12) + anchor;
            Assert.That(state.Movement[0].Goal, Is.EqualTo(corrected));
            Assert.That(pool.Reader.Sample(field, corrected, out var sample), Is.EqualTo(FlowSampleStatus.Ready));
            Assert.That(sample.Goal, Is.EqualTo(corrected));
            Assert.That(sample.InSight, Is.True);
            Assert.That(sample.Direction, Is.EqualTo(float2.zero));
            using var blocked = new NativeList<int2>(Allocator.TempJob);
            for (int cell = 0; cell < cells; cell++)
                if (maps.Passable[size * cells + cell] == 0) blocked.Add(new int2(cell % width, cell / width));
            using var errors = new NativeArray<int>(cells, Allocator.TempJob);
            new OracleJob { Width = width, Goal = goal, Class = size, FieldOffset = field * cells,
                Passable = maps.Passable, Directions = pool.Directions, Distances = pool.Distances,
                Blocked = blocked.AsArray(), Errors = errors }.Schedule(cells, 64).Complete();
            for (int cell = 0; cell < cells; cell++) Assert.That(errors[cell], Is.Zero, "Corrected field cell " + cell);
            new ClearTargetsJob { Width = width, Class = size, FieldIndex = field, Reader = pool.Reader,
                Blocked = blocked.AsArray(), Errors = errors }.Schedule(cells, 64).Complete();
            for (int cell = 0; cell < cells; cell++) Assert.That(errors[cell], Is.Zero, "Corrected target cell " + cell);
        }

        // Independent segment/open-square intersection using rational clipping.
        private static bool Hits(int2 start, int2 end, int2 centre, int half)
        {
            int2 low = new int2(0, 1), high = new int2(1, 1);
            for (int axis = 0; axis < 2; axis++)
            {
                int delta = end[axis] - start[axis];
                int relative = centre[axis] - start[axis];
                if (delta == 0) { if (math.abs(relative) >= half) return false; continue; }
                int2 a = new int2(relative - half, delta), b = new int2(relative + half, delta);
                if (delta < 0) { int2 swap = -a; a = -b; b = swap; }
                if ((long)a.x * low.y > (long)low.x * a.y) low = a;
                if ((long)b.x * high.y < (long)high.x * b.y) high = b;
                if ((long)low.x * high.y >= (long)high.x * low.y) return false;
            }
            return true;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct OracleJob : IJobParallelFor
        {
            internal int Width, Goal, Class, FieldOffset;
            [ReadOnly] internal NativeArray<byte> Passable, Directions;
            [ReadOnly] internal NativeArray<ushort> Distances;
            [ReadOnly] internal NativeArray<int2> Blocked;
            internal NativeArray<int> Errors;
            public void Execute(int cell)
            {
                int cells = Width * Width;
                int2 p = new int2(cell % Width, cell / Width), goal = new int2(Goal % Width, Goal / Width);
                int index = FieldOffset + cell;
                bool visible = Distances[index] != ushort.MaxValue;
                for (int i = 0; visible && i < Blocked.Length; i++)
                    if (Hits(p, goal, Blocked[i], 1)) visible = false;
                int error = visible != ((Directions[index] & 8) != 0) ? 1 : 0;
                if ((Directions[index] & 240) != 0) error |= 2;
                if (Distances[index] != ushort.MaxValue && cell != Goal)
                {
                    int best = Distances[index], chosen = int.MaxValue;
                    for (int d = 0; d < 8; d++)
                    {
                        int2 step = BuildFlowFieldJob.Step(d), next = p + step;
                        if (math.any(next < 0) || math.any(next >= Width) || Passable[Class * cells + next.y * Width + next.x] == 0) continue;
                        if (step.x != 0 && step.y != 0 && (Passable[Class * cells + p.y * Width + next.x] == 0 ||
                            Passable[Class * cells + next.y * Width + p.x] == 0)) continue;
                        int distance = Distances[FieldOffset + next.y * Width + next.x];
                        best = math.min(best, distance);
                        if (d == (Directions[index] & 7)) chosen = distance;
                    }
                    if (chosen != best) error |= 2;
                }
                Errors[cell] = error;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ClearTargetsJob : IJobParallelFor
        {
            internal int Width, Class, FieldIndex;
            internal FlowFieldReader Reader;
            [ReadOnly] internal NativeArray<int2> Blocked;
            internal NativeArray<int> Errors;
            public void Execute(int cell)
            {
                int failures = 0;
                int2 lattice = new int2(cell % Width, cell / Width) * 64;
                for (int sub = 0; sub < 4; sub++)
                {
                    int2 p = lattice + new int2(sub & 1, sub >> 1) * 32;
                    float2 world = (float2)p / 64 + (Class + 1) * .5f;
                    if (Reader.Sample(FieldIndex, world, out var sample) != FlowSampleStatus.Ready || !sample.InSight) continue;
                    int2 goal = (int2)math.round((sample.Goal - (Class + 1) * .5f) * 64);
                    var random = new Unity.Mathematics.Random((uint)(cell + 1));
                    for (int target = 0; target < 13; target++)
                    {
                        int2 t = goal + (target < 9 ? new int2(target % 3 - 1, target / 3 - 1) * 32 : random.NextInt2(-32, 33));
                        for (int i = 0; i < Blocked.Length; i++)
                            if (Hits(p, t, Blocked[i] * 64, 32))
                            { failures++; break; }
                    }
                }
                Errors[cell] = failures;
            }
        }
    }
}
