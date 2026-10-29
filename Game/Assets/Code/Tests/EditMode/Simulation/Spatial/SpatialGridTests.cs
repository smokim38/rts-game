using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Simulation.Spatial
{
    public sealed class SpatialGridTests
    {
        private static MovementParameters Settings()
        {
            var asset = ScriptableObject.CreateInstance<MovementSettings>();
            var result = asset.Snapshot();
            Object.DestroyImmediate(asset);
            return result;
        }

        private static void Place(UnitState state, int i, float2 position)
        {
            state.Alive[i] = 1;
            state.Positions[i] = new float3(position.x, 0, position.y);
        }
        [Test]
        public void StableCountingSortExcludesDeadSlotsAndOrdersEachBucketByIndex()
        {
            using var ownedState = new UnitState(12);
            UnitState state = ownedState;
            using var pipeline = new MovementPipeline(12, Settings());
            for (int i = 0; i < 12; i++) Place(state, i, new float2((i % 2) * 4, 0));
            state.Alive[4] = 0;
            pipeline.Schedule(state, default, 1).Complete();
            int total = 0;
            for (int b = 0; b < pipeline.Grid.Counts.Length; b++)
            {
                int previous = -1;
                for (int k = 0; k < pipeline.Grid.Counts[b]; k++)
                {
                    int unit = pipeline.Grid.Indices[pipeline.Grid.Starts[b] + k];
                    Assert.That(unit, Is.GreaterThan(previous));
                    Assert.That(unit, Is.Not.EqualTo(4));
                    Assert.That(pipeline.Grid.Poses[pipeline.Grid.Starts[b] + k].Position,
                        Is.EqualTo(state.Movement[unit].Previous.Position));
                    previous = unit; total++;
                }
            }
            Assert.That(total, Is.EqualTo(11));
        }

    }
}
