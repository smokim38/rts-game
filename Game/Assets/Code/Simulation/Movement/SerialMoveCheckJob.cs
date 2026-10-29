using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct SerialMoveCheckJob : IJob
    {
        internal MoveCheckData Data;
        internal NativeArray<byte> Levels, Choices;
        internal NativeArray<int> Affected, Changed, Marks, Metrics;
        internal int MaxSerialFallbacks;
        private int affectedCount, changedCount, epoch;

        public void Execute()
        {
            // Metrics: first-round fallbacks, serial fallbacks, serial rounds, cap rollbacks.
            for (int i = 0; i < Metrics.Length; i++) Metrics[i] = 0;
            for (int i = 0; i < Levels.Length; i++)
            {
                Marks[i] = 0;
                Levels[i] = Choices[i];
                if (Choices[i] == 0) continue;
                Data.Apply(i, Choices[i]);
                Changed[changedCount++] = i;
                Metrics[0]++;
            }
            CollectAffected();
            while (affectedCount > 0)
            {
                if (Metrics[1] >= MaxSerialFallbacks) { RollBackConnectedSet(); return; }
                changedCount = 0;
                // Decisions all read the round's starting poses. Only this small frontier
                // is revisited; commits happen after every decision has been made.
                for (int k = 0; k < affectedCount; k++)
                {
                    int i = Affected[k];
                    byte choice = Data.Choose(i, Levels[i]);
                    Choices[i] = choice;
                    if (choice != Levels[i])
                    {
                        Changed[changedCount++] = i;
                        // Stop selecting as soon as the round cannot fit. Nothing in this
                        // round has been applied, and the entire frontier seeds rollback.
                        if (Metrics[1] + changedCount > MaxSerialFallbacks) { RollBackConnectedSet(); return; }
                    }
                }
                if (changedCount == 0) return;
                for (int k = 0; k < changedCount; k++)
                {
                    int i = Changed[k];
                    Levels[i] = Choices[i];
                    Data.Apply(i, Choices[i]);
                }
                Metrics[1] += changedCount;
                Metrics[2]++;
                CollectAffected();
            }
        }

        private void CollectAffected()
        {
            affectedCount = 0;
            epoch++;
            for (int k = 0; k < changedCount; k++)
            {
                int i = Changed[k];
                AddAffected(i);
                var query = Data.Neighbours(i);
                while (query.Next(out int other))
                {
                    float reach = Footprint.Radius(Data.Sizes[i]) + Footprint.Radius(Data.Sizes[other]);
                    if (math.distancesq(Data.Poses[i].Position.xz, Data.Poses[other].Position.xz) <= reach * reach)
                        AddAffected(other);
                }
            }
            Affected.GetSubArray(0, affectedCount).Sort();
        }

        private void AddAffected(int unit)
        {
            if (Marks[unit] == epoch) return;
            Marks[unit] = epoch;
            Affected[affectedCount++] = unit;
        }

        private void RollBackConnectedSet()
        {
            // Swept disc envelopes conservatively connect all pairs that could touch at
            // either endpoint. No restored footprint can reach a unit outside this closure.
            epoch++;
            for (int k = 0; k < affectedCount; k++) Marks[Affected[k]] = epoch;
            for (int k = 0; k < affectedCount; k++)
            {
                int i = Affected[k];
                var query = Data.Neighbours(i);
                while (query.Next(out int other))
                {
                    float reach = Footprint.Radius(Data.Sizes[i]) + Footprint.Radius(Data.Sizes[other]) + 2 * Data.StepBound;
                    if (math.distancesq(Data.Start[i].Position.xz, Data.Start[other].Position.xz) <= reach * reach) AddAffected(other);
                }
            }
            for (int k = 0; k < affectedCount; k++)
            {
                int i = Affected[k];
                Levels[i] = 2;
                Data.Apply(i, 2);
            }
            Metrics[3] = affectedCount;
        }
    }
}
