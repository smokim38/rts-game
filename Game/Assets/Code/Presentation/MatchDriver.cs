using Northwatch.Simulation;
using Unity.Profiling;
using Unity.Jobs;
using UnityEngine;

namespace Northwatch.Presentation
{
    [DefaultExecutionOrder(2000)]
    public sealed class MatchDriver : MonoBehaviour
    {
        // Unity 6.3's default object-to-world and world-to-object data allows 511 instances.
        public const int InstancesPerDraw = 511;
        [SerializeField] private Mesh cube;
        [SerializeField] private Material firstSide;
        [SerializeField] private Material secondSide;
        [SerializeField] private Material selectedSide;
        [SerializeField] private MovementSettings movementSettings;
        private UnitSimulation simulation;
        private TickClock clock;
        private RenderParams firstParameters, secondParameters, selectedParameters;
        private static readonly ProfilerMarker SimulationMarker = new ProfilerMarker("Northwatch.Simulation");
        private static readonly ProfilerMarker SimulationWaitMarker = new ProfilerMarker("Northwatch.Simulation.Wait");
        private static readonly ProfilerMarker DrawingMarker = new ProfilerMarker("Northwatch.Drawing");
        public UnitSimulation Simulation => simulation;
        public Material SelectionMaterial => selectedSide;
        public MovementSettings MovementSettings => movementSettings;
        public int ScheduledTicks { get; private set; }
        public int CompletedTicks { get; private set; }

        public void Configure(Mesh mesh, Material first, Material second)
        {
            cube = mesh;
            firstSide = first;
            secondSide = second;
        }

        public void ConfigureSelection(Material selected) => selectedSide = selected;
        public void ConfigureMovement(MovementSettings settings) => movementSettings = settings;

        private void OnEnable()
        {
            try { simulation = movementSettings == null ? new UnitSimulation() : new UnitSimulation(movementSettings.Snapshot()); }
            catch (System.ArgumentException error)
            {
                Debug.LogError(error.Message); enabled = false; return;
            }
            clock = new TickClock();
            firstParameters = new RenderParams(firstSide);
            secondParameters = new RenderParams(secondSide);
            selectedParameters = new RenderParams(selectedSide);
            if (!TryGetComponent(out MatchFrameStart start)) start = gameObject.AddComponent<MatchFrameStart>();
            start.Configure(this);
        }

        internal void BeginFrame()
        {
            using (SimulationMarker.Auto())
            {
                using (SimulationWaitMarker.Auto()) simulation.CompleteFrame();
                CompletedTicks = ScheduledTicks;
            }
        }

        private void LateUpdate()
        {
            using (DrawingMarker.Auto())
            {
                Draw(simulation.DrawCount(Side.First) - simulation.SelectedCount, 0, ref firstParameters);
                Draw(simulation.DrawCount(Side.Second), UnitSimulation.Capacity, ref secondParameters);
                Draw(simulation.SelectedCount, UnitSimulation.Capacity * 2, ref selectedParameters);
            }
            using (SimulationMarker.Auto())
            {
                // Completing next frame adds one frame to S2.1's bound: about 83 ms
                // at 60 fps. Selection feedback can still show on the next frame.
                ScheduledTicks = clock.Advance(Time.unscaledDeltaTime);
                simulation.ScheduleFrame(ScheduledTicks, clock.Interpolation);
                JobHandle.ScheduleBatchedJobs();
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
