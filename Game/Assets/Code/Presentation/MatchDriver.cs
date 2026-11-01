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
        [SerializeField] private SandboxObstacles obstacles;
        private UnitSimulation simulation;
        private TickClock clock;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private RouteOverlay routeOverlay;
#endif
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
        public void ConfigureObstacles(SandboxObstacles layout) => obstacles = layout;

        private void OnEnable()
        {
            try { simulation = movementSettings == null ? new UnitSimulation() : obstacles == null ?
                new UnitSimulation(movementSettings.Snapshot()) : obstacles.CreateSimulation(movementSettings.Snapshot()); }
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using var allocations = Diagnostics.MovementCoreProfile.TraceAllocations(false);
#endif
            using (SimulationMarker.Auto())
            {
                using (SimulationWaitMarker.Auto()) simulation.CompleteFrame();
                CompletedTicks = ScheduledTicks;
            }
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (UnityEngine.InputSystem.Keyboard.current?.f8Key.wasPressedThisFrame == true)
            {
                if (routeOverlay == null && movementSettings != null) routeOverlay = new RouteOverlay(cube, firstSide);
                else { routeOverlay?.Dispose(); routeOverlay = null; }
            }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (Diagnostics.MovementCoreProfile.TraceAllocations(true))
#endif
            using (DrawingMarker.Auto())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                routeOverlay?.Draw(simulation);
#endif
                Draw(simulation.DrawCount(Side.First) - simulation.SelectedCount, 0, ref firstParameters);
                Draw(simulation.DrawCount(Side.Second), UnitSimulation.Capacity, ref secondParameters);
                Draw(simulation.SelectedCount, UnitSimulation.Capacity * 2, ref selectedParameters);
                Draw(simulation.AcknowledgementCount, UnitSimulation.Capacity * 3, ref selectedParameters);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (Diagnostics.MovementCoreProfile.TraceAllocations(false))
#endif
            using (SimulationMarker.Auto())
            {
                // Completing next frame adds one frame to S2.1's bound: about 83 ms
                // at 60 fps. Selection feedback can still show on the next frame.
                ScheduledTicks = clock.Advance(Time.unscaledDeltaTime);
                simulation.ScheduleFrame(ScheduledTicks, clock.Interpolation, Time.unscaledTime);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            routeOverlay?.Dispose(); routeOverlay = null;
#endif
            simulation?.Dispose();
            simulation = null;
        }
    }
}
