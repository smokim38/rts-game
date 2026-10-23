using Northwatch.Simulation;
using Unity.Profiling;
using UnityEngine;

namespace Northwatch.Presentation
{
    public sealed class MatchDriver : MonoBehaviour
    {
        // Unity 6.3's default object-to-world and world-to-object data allows 511 instances.
        public const int InstancesPerDraw = 511;
        [SerializeField] private Mesh cube;
        [SerializeField] private Material firstSide;
        [SerializeField] private Material secondSide;
        private UnitSimulation simulation;
        private TickClock clock;
        private RenderParams firstParameters, secondParameters;
        private static readonly ProfilerMarker SimulationMarker = new ProfilerMarker("Northwatch.Simulation");
        private static readonly ProfilerMarker DrawingMarker = new ProfilerMarker("Northwatch.Drawing");
        public UnitSimulation Simulation => simulation;

        public void Configure(Mesh mesh, Material first, Material second)
        {
            cube = mesh;
            firstSide = first;
            secondSide = second;
        }

        private void OnEnable()
        {
            simulation = new UnitSimulation();
            clock = new TickClock();
            firstParameters = new RenderParams(firstSide);
            secondParameters = new RenderParams(secondSide);
        }

        private void Update()
        {
            using (SimulationMarker.Auto())
            {
                // Chain at most two ticks, dropping excess time, then complete once per frame.
                simulation.ScheduleFrame(clock.Advance(Time.unscaledDeltaTime));
                simulation.CompleteFrame();
            }
            using (DrawingMarker.Auto())
            {
                Draw(Side.First, ref firstParameters);
                Draw(Side.Second, ref secondParameters);
            }
        }

        private void Draw(Side side, ref RenderParams parameters)
        {
            int count = simulation.DrawCount(side);
            var matrices = simulation.DrawMatrices;
            for (int start = 0; start < count; start += InstancesPerDraw)
                Graphics.RenderMeshInstanced(parameters, cube, 0, matrices,
                    Mathf.Min(InstancesPerDraw, count - start), (int)side * UnitSimulation.Capacity + start);
        }

        private void OnDisable()
        {
            simulation?.Dispose();
            simulation = null;
        }
    }
}
