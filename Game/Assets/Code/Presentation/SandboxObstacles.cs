using Northwatch.Simulation;
using Northwatch.Simulation.FlowFields;
using Northwatch.Simulation.Movement;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Presentation
{
    public sealed class SandboxObstacles : ScriptableObject
    {
        [SerializeField] private RectInt[] boxes;
        public int Count => boxes.Length;
        public RectInt Box(int index) => boxes[index];

        public void Configure(RectInt[] layout) => boxes = layout;

        public UnitSimulation CreateSimulation(MovementParameters settings)
        {
            using var ownedSnapshot = new NativeArray<StaticBox>(Count, Allocator.TempJob);
            NativeArray<StaticBox> snapshot = ownedSnapshot;
            for (int i = 0; i < Count; i++)
            {
                RectInt box = boxes[i];
                snapshot[i] = new StaticBox { Min = new int2(box.xMin, box.yMin),
                    MaxExclusive = new int2(box.xMax, box.yMax) };
            }
            return new UnitSimulation(settings, snapshot);
        }
    }
}
