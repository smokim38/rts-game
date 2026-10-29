using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Northwatch.Simulation.Spatial;

namespace Northwatch.Simulation.Movement
{
    [BurstCompile]
    internal struct SerialMoveCheckJob : IJob
    {
        internal MoveCheckData Data;
        internal NativeArray<byte> Levels, Choices;
        internal NativeArray<int> Affected, Changed, Marks, Metrics;
        internal int MaxSerialFallbacks;
        internal NativeArray<long> Timings;
        internal int TimingOffset;
        private int affectedCount, changedCount, epoch;

        public void Execute()
        {
            MovementTimestampJob.Write(Timings, TimingOffset + 7);
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
            MovementTimestampJob.Write(Timings, TimingOffset + 8);
            while (affectedCount > 0)
            {
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
                    if (Marks[other] == epoch) continue;
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
            MovementTimestampJob.Write(Timings, TimingOffset + 9);
            // Members return to Start; outsiders retain Poses. Add every outsider whose
            // current disc touches a restored member, then close over the new members.
            epoch++;
            for (int k = 0; k < affectedCount; k++) Marks[Affected[k]] = epoch;
            // Once every slot is in the set, no outside neighbour remains to discover.
            for (int k = 0; k < affectedCount && affectedCount < Data.Poses.Length; k++)
            {
                int i = Affected[k];
                // The member is restored, so only the candidate can move relative to
                // the old grid. One StepBound (rather than two) covers that search.
                var query = new SpatialQuery(Data.Grid, Data.Start[i].Position.xz,
                    Footprint.Radius(Data.Sizes[i]) + Footprint.LargeRadius + Data.StepBound);
                while (query.Next(out int other))
                {
                    if (Marks[other] == epoch) continue;
                    float reach = Footprint.Radius(Data.Sizes[i]) + Footprint.Radius(Data.Sizes[other]);
                    if (math.distancesq(Data.Start[i].Position.xz, Data.Poses[other].Position.xz) <= reach * reach) AddAffected(other);
                }
            }
            for (int k = 0; k < affectedCount; k++)
            {
                int i = Affected[k];
                Levels[i] = 2;
                Data.Apply(i, 2);
            }
            Metrics[3] = affectedCount;
            MovementTimestampJob.Write(Timings, TimingOffset + 10);
        }
    }
}
