#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using Northwatch.Simulation.Movement;
using Unity.Collections;
using Unity.Mathematics;

namespace Northwatch.Simulation.FlowFields
{
    // Drives the production pool without movement, as the ADR 0011 stream specifies.
    public sealed class FlowFieldMeasurement : IDisposable
    {
        private readonly StaticMaps maps;
        private readonly FlowFieldPool pool;
        private UnitState state;
        private readonly int mode;
        private readonly int width, cells;
        private Unity.Mathematics.Random random;
        private readonly bool[] usedGoals, usedStarts;
        private readonly bool[] seenOrders = new bool[801];
        private readonly int[] orderTicks = new int[801];
        private readonly double[] builds = new double[4096], sights = new double[4096], publishes = new double[4096], waits = new double[801];
        private readonly long[] seenBuilds;
        private int buildCount, waitCount, tick, stopgap;
        public bool Finished => tick == (mode == 2 ? 1830 : 202);

        public FlowFieldMeasurement(MovementParameters settings, int mode)
        {
            this.mode = mode; random = new Unity.Mathematics.Random(160);
            width = mode == 4 ? 295 : 256; cells = width * width;
            usedGoals = new bool[cells]; usedStarts = new bool[cells];
            settings.ValidateFields(width, width);
            maps = new StaticMaps(width, width);
            using var ownedBoxes = new NativeArray<StaticBox>(mode == 1 ? cells / 5 : mode == 3 ? cells / 50 : 0, Allocator.TempJob);
            var boxes = ownedBoxes;
            var blocked = new bool[cells];
            for (int i = 0; i < boxes.Length; i++)
            {
                int cell; do { cell = random.NextInt(cells); } while (blocked[cell]); blocked[cell] = true;
                boxes[i] = new StaticBox { Min = new int2(cell % width, cell / width), MaxExclusive = new int2(cell % width + 1, cell / width + 1) };
            }
            maps.ScheduleBuild(boxes).Complete();
            state = new UnitState(mode == 2 ? 200 : 1);
            pool = new FlowFieldPool(width, width, new float2(-128), state.Alive.Length, settings);
            seenBuilds = new long[settings.BuildSlots];
            for (int i = 0; i < state.Alive.Length; i++)
            {
                int cell;
                do { cell = random.NextInt(cells); } while (usedStarts[cell] || maps.Passable[cell] == 0);
                usedStarts[cell] = true;
                state.Alive[i] = 1; state.Positions[i] = new float3(cell % width - 127.5f, 0, cell / width - 127.5f);
                if (mode == 2) Order(i, i + 1);
            }
        }

        private void Order(int i, int order)
        {
            bool right = state.Positions[i].x < 0;
            int cell;
            do { cell = random.NextInt(width) * width + (right ? random.NextInt(144, width) : random.NextInt(112)); }
            while (usedGoals[cell] || maps.Passable[cell] == 0);
            usedGoals[cell] = true; orderTicks[order] = tick;
            state.Movement[i] = new MovementUnit { HasMoveOrder = true, Mode = MovementMode.Moving, OrderVersion = order,
                Goal = new float2(cell % width - 127.5f, cell / width - 127.5f) };
        }

        public void Step()
        {
            if (Finished) return;
            if (mode == 2)
            {
                if (tick > 0 && tick <= 1800 && tick % 3 == 0) Order((tick / 3 - 1) % 200, 200 + tick / 3);
            }
            else if (tick < 200) Order(0, tick + 1);
            var chain = pool.Publish((ulong)tick, default);
            chain = pool.RequestFields(state, maps.Passable, (ulong)tick, chain);
            chain = pool.ScheduleBuilds(maps.Passable, (ulong)tick, chain);
            chain.Complete();
            for (int i = 0; i < state.Alive.Length; i++)
            {
                if (pool.UnitFields[i] < 0) { if (mode == 2) stopgap++; continue; }
                int order = state.Movement[i].OrderVersion;
                if (seenOrders[order]) continue;
                seenOrders[order] = true; waits[waitCount++] = (tick - orderTicks[order]) * 1000.0 / 30;
            }
            for (int slot = 0; slot < seenBuilds.Length; slot++)
            {
                int metric = slot * FlowFieldPool.MetricStride;
                if (pool.Metrics[metric + 4] == seenBuilds[slot]) continue;
                seenBuilds[slot] = pool.Metrics[metric + 4];
                double scale = 1000.0 / MovementTimestampJob.Frequency;
                builds[buildCount] = (pool.Metrics[metric + 1] - pool.Metrics[metric]) * scale;
                sights[buildCount] = (pool.Metrics[metric + 1] - pool.Metrics[metric + 7]) * scale;
                publishes[buildCount++] = (pool.Metrics[metric + 3] - pool.Metrics[metric + 2]) * scale;
            }
            tick++;
        }

        public void Write(TextWriter writer)
        {
            Array.Sort(builds, 0, buildCount); Array.Sort(sights, 0, buildCount); Array.Sort(publishes, 0, buildCount); Array.Sort(waits, 0, waitCount);
            long missed = 0, replacements = 0;
            for (int slot = 0; slot < seenBuilds.Length; slot++)
            { missed += pool.Metrics[slot * FlowFieldPool.MetricStride + 5]; replacements += pool.Metrics[slot * FlowFieldPool.MetricStride + 6]; }
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "mode={0}; builds={1}; build_median_ms={2:F6}; build_p95_ms={3:F6}; publish_median_ms={4:F6}; publish_p95_ms={5:F6}; missed_due={6}; replacements_per_s={7:F6}; stopgap_percent={8:F6}; pool_MiB={9:F6}; scratch_MiB={10:F6}",
                mode, buildCount, Percentile(builds, buildCount, .5), Percentile(builds, buildCount, .95),
                Percentile(publishes, buildCount, .5), Percentile(publishes, buildCount, .95), missed,
                replacements * 30.0 / tick, 100.0 * stopgap / (200 * 1830), pool.PoolBytes / 1048576.0, pool.ScratchBytes / 1048576.0));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "waits={0}; wait_median_ms={1:F3}; wait_p95_ms={2:F3}; wait_max_ms={3:F3}",
                waitCount, Percentile(waits, waitCount, .5), Percentile(waits, waitCount, .95), Percentile(waits, waitCount, 1)));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "sight_median_ms={0:F6}; sight_p95_ms={1:F6}",
                Percentile(sights, buildCount, .5), Percentile(sights, buildCount, .95)));
        }
        private static double Percentile(double[] values, int count, double p) => count == 0 ? 0 : values[(int)Math.Ceiling(count * p) - 1];
        public void Dispose() { pool.Dispose(); maps.Dispose(); state.Dispose(); }
    }
}
#endif
