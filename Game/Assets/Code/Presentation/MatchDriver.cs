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
        [SerializeField] private Material selectedSide;
        private UnitSimulation simulation;
        private TickClock clock;
        private RenderParams firstParameters, secondParameters, selectedParameters;
        private static readonly ProfilerMarker SimulationMarker = new ProfilerMarker("Northwatch.Simulation");
        private static readonly ProfilerMarker DrawingMarker = new ProfilerMarker("Northwatch.Drawing");
        public UnitSimulation Simulation => simulation;

        public void Configure(Mesh mesh, Material first, Material second)
        {
            cube = mesh;
            firstSide = first;
            secondSide = second;
        }

        public void ConfigureSelection(Material selected) => selectedSide = selected;

        private void OnEnable()
        {
            simulation = new UnitSimulation();
            clock = new TickClock();
            firstParameters = new RenderParams(firstSide);
            secondParameters = new RenderParams(secondSide);
            selectedParameters = new RenderParams(selectedSide);
        }

        private void LateUpdate()
        {
            using (SimulationMarker.Auto())
            {
                // Chain at most two ticks, dropping excess time, then complete once per frame.
                simulation.ScheduleFrame(clock.Advance(Time.unscaledDeltaTime));
                simulation.CompleteFrame();
            }
            using (DrawingMarker.Auto())
            {
                Draw(simulation.DrawCount(Side.First) - simulation.SelectedCount, 0, ref firstParameters);
                Draw(simulation.DrawCount(Side.Second), UnitSimulation.Capacity, ref secondParameters);
                Draw(simulation.SelectedCount, UnitSimulation.Capacity * 2, ref selectedParameters);
            }
        }

        private void Draw(int count, int offset, ref RenderParams parameters)
        {
            var matrices = simulation.DrawMatrices;
            for (int start = 0; start < count; start += InstancesPerDraw)
                Graphics.RenderMeshInstanced(parameters, cube, 0, matrices,
                    Mathf.Min(InstancesPerDraw, count - start), offset + start);
        }

        private void OnDisable()
        {
            simulation?.Dispose();
            simulation = null;
        }
    }
}
