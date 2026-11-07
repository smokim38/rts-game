#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using Unity.Jobs.LowLevel.Unsafe;

namespace Northwatch.Presentation.Diagnostics
{
    [DefaultExecutionOrder(-20000)]
    public sealed class MovementCoreProfile : MonoBehaviour
    {
        private const int FrameCapacity = 1000000;
        private MatchDriver driver;
        private MovementMeasurement measurement;
        private ProfilerRecorder main, wait, gc, gcBytes, overlay;
        private long[] mainTimes, waitTimes, allocations, allocationCounts;
        private int[] scheduled, completed;
        private int frames, firstTick, invalidFrames;
        private string output;
        private double began, measuredAt;
        private bool measuring, finished, capture;
        private string[] capturePaths;
        private int capturePart;
        private long counterFrequency;
        private int targetFrameRate = -1;
        private static MovementCoreProfile allocationProfile;
        private ProfilerRecorder scopedGc;
        private long simulationAllocations, drawingAllocations;
        private long overlayCalls, overlayNanoseconds;
        private bool expectRouteOverlay, injectOverlayKey;
        private bool scopeCapacityReached;
        private int scene, unitCount, orderPhase, releaseFrame, acknowledgementFrame = -1, firstOrderTick = -1;
        private bool modern, selectionQueued, drawPending, allDrawPending;
        private Mouse mouse;
        private Keyboard keyboard;
        private SelectionController controls;
        private Camera orderCamera;
        private Vector2 orderPoint;
        private Vector4[] initialDraw;
        private double[] vehicleDrawTimes;
        private bool[] pendingDrawChanges;
        private int drawnVehicles, batchSize, workers, sizeSet;
        private float startHeading, orderDirection, orderDistance;
        private bool directedOrder;
        private bool ClickScene => scene >= 4 && scene <= 6;
        private double releaseTime, firstDrawTime = -1, allDrawTime = -1;
        private MeshRenderer markerDrawing;
        private bool checkMarker, expectedMarker;
        private int markerTravelFrames, markerSettledFrames, markerMismatches;
        private int lastOrderStoppedFrame = -1, markerHiddenFrame = -1;
        private long searchesAtRest = -1, requestsAtRest = -1;

        internal readonly struct AllocationScope : IDisposable
        {
            private readonly MovementCoreProfile owner;
            private readonly bool drawing;
            internal AllocationScope(MovementCoreProfile owner, bool drawing)
            {
                this.owner = owner; this.drawing = drawing;
                owner.scopedGc.Reset(); owner.scopedGc.Start();
            }
            public void Dispose()
            {
                if (owner == null) return;
                owner.scopedGc.Stop();
                if (!owner.measuring) return;
                int count = owner.scopedGc.Count;
                if (count == 256) owner.scopeCapacityReached = true;
                long allocations = 0;
                for (int i = 0; i < count; i++) allocations += owner.scopedGc.GetSample(i).Count;
                if (drawing) owner.drawingAllocations += allocations;
                else owner.simulationAllocations += allocations;
            }
        }

        internal static AllocationScope TraceAllocations(bool drawing)
            => allocationProfile == null || allocationProfile.finished ? default : new AllocationScope(allocationProfile, drawing);

        internal static string[] ProfileArguments()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-movement-core-profile") >= 0 || Array.IndexOf(args, "-flow-field-profile") >= 0) return args;
            // One argument per line lets desktop automation launch the normal Player.
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Logs/movement-profile-arguments.txt"));
            return File.Exists(path) ? File.ReadAllLines(path) : args;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (Array.IndexOf(ProfileArguments(), "-movement-core-profile") < 0) return;
            PerformanceLadder ladder = FindFirstObjectByType<PerformanceLadder>();
            if (ladder != null) ladder.enabled = false;
            new GameObject("MovementCoreProfile").AddComponent<MovementCoreProfile>();
        }

        private void Start()
        {
            try { Initialize(); }
            catch (Exception error) { Debug.LogException(error); finished = true; Application.Quit(1); }
        }

        [DllImport("kernel32.dll")] private static extern int QueryPerformanceFrequency(out long value);

        private void Initialize()
        {
            string[] args = ProfileArguments();
            expectRouteOverlay = Array.IndexOf(args, "-movement-route-overlay") >= 0;
            injectOverlayKey = Array.IndexOf(args, "-movement-inject-overlay-key") >= 0;
            capture = Array.IndexOf(args, "-movement-capture") >= 0;
            int index = Array.IndexOf(args, "-movement-output"), sceneIndex = Array.IndexOf(args, "-movement-scene");
            int countIndex = Array.IndexOf(args, "-movement-units");
            int fpsIndex = Array.IndexOf(args, "-movement-fps");
            if (fpsIndex >= 0 && (fpsIndex + 1 >= args.Length ||
                !int.TryParse(args[fpsIndex + 1], out targetFrameRate) || (targetFrameRate != -1 && targetFrameRate != 60)))
                throw new ArgumentException("Use -movement-fps -1|60 (default -1).");
            if (!Debug.isDebugBuild || index < 0 || index + 1 >= args.Length || sceneIndex < 0 || sceneIndex + 1 >= args.Length ||
                countIndex < 0 || countIndex + 1 >= args.Length)
                throw new ArgumentException("Use a development Player with -movement-output PATH -movement-scene 0..7 -movement-units N.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            output = Path.GetFullPath(args[index + 1]);
            if (!output.StartsWith(Path.Combine(project, "Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Movement output must be under this project's Logs folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if (capture)
                capturePaths = new[] { output + "-0.raw", output + "-1.raw", output + "-2.raw", output + "-3.raw" };
            driver = FindFirstObjectByType<MatchDriver>();
            if (driver == null || driver.MovementSettings == null) throw new InvalidOperationException("The match has no driver or movement settings.");
            scene = int.Parse(args[sceneIndex + 1], CultureInfo.InvariantCulture);
            if (injectOverlayKey && (!expectRouteOverlay || !ClickScene))
                throw new ArgumentException("Overlay key injection requires an order scene and -movement-route-overlay.");
            unitCount = int.Parse(args[countIndex + 1], CultureInfo.InvariantCulture);
            batchSize = IntegerOption(args, "-movement-batch", 64);
            string workerOption = Option(args, "-movement-workers", "all");
            workers = workerOption == "all" ? JobsUtility.JobWorkerMaximumCount : int.Parse(workerOption, CultureInfo.InvariantCulture);
            if (workers < 1 || workers > JobsUtility.JobWorkerMaximumCount || batchSize < 1)
                throw new ArgumentException("Use -movement-workers 1|all and -movement-batch 1|64.");
            JobsUtility.JobWorkerCount = workers;
            string sizes = Option(args, "-movement-size", "mixed");
            sizeSet = sizes == "mixed" ? 0 : sizes == "small" ? 1 : sizes == "large" ? 2 :
                throw new ArgumentException("Use -movement-size mixed|small|large.");
            startHeading = FloatOption(args, "-movement-heading", 0);
            orderDirection = FloatOption(args, "-movement-direction", 0);
            directedOrder = Array.IndexOf(args, "-movement-direction") >= 0;
            orderDistance = FloatOption(args, "-movement-distance", directedOrder ? 30 : unitCount == 5 ? 20 : unitCount == 100 ? 15 : 60);
            if (orderDistance <= 0 || directedOrder && scene != 4)
                throw new ArgumentException("Order distance must be positive; -movement-direction requires scene 4.");
            driver.Simulation.MeasureMovement(scene, unitCount, sizeSet, startHeading * Mathf.Deg2Rad, batchSize);
            measurement = driver.Simulation.Measurement;
            if (scene == 7)
            {
                var layout = ScriptableObject.CreateInstance<LadderScenario>();
                layout.QueueUnits(driver.Simulation, unitCount);
                layout.QueueOrders(driver.Simulation, unitCount);
                Destroy(layout);
            }
            if (ClickScene)
            {
                controls = FindFirstObjectByType<SelectionController>();
                orderCamera = Camera.main;
                if (controls == null || orderCamera == null) throw new InvalidOperationException("Open orders require the sandbox Player.");
                markerDrawing = driver.transform.Find("MoveMarker").GetComponent<MeshRenderer>();
                modern = Array.IndexOf(args, "-movement-modern") >= 0;
                mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
                gameObject.AddComponent<SelectionInputPump>();
                initialDraw = new Vector4[unitCount];
                vehicleDrawTimes = new double[unitCount]; Array.Fill(vehicleDrawTimes, -1);
                pendingDrawChanges = new bool[unitCount];
                orderCamera.GetComponent<Cameras.CameraController>().CentreOn(scene == 6 ? new Vector3(80, 0, 30) :
                    OrderDestination() * .5f);
                orderCamera.orthographicSize = unitCount <= 100 ? 25 : 70;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(960, 540) });
                RenderPipelineManager.endContextRendering += RecordDrawnResponse;
            }
            mainTimes = new long[FrameCapacity]; waitTimes = new long[FrameCapacity];
            allocations = new long[FrameCapacity]; allocationCounts = new long[FrameCapacity];
            scheduled = new int[FrameCapacity]; completed = new int[FrameCapacity];
            const ProfilerRecorderOptions options = ProfilerRecorderOptions.StartImmediately |
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame;
            main = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Northwatch.Simulation", 1, options);
            wait = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Northwatch.Simulation.Wait", 1, options);
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 1, options);
            gcBytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1, options);
            _ = RouteOverlay.Marker;
            overlay = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Northwatch.RouteOverlay", 1, options);
            if (!main.Valid || !wait.Valid || !gc.Valid || !gcBytes.Valid || !overlay.Valid)
                throw new InvalidOperationException("Required driver/wait/GC/overlay recorders are unavailable.");
            if (Array.IndexOf(args, "-movement-gc-scopes") >= 0)
            {
                // Individual samples delimit the driver's scopes without a raw capture.
                scopedGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 256,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                if (!scopedGc.Valid) throw new InvalidOperationException("Scoped allocation recorder unavailable.");
                scopedGc.Reset(); scopedGc.Start();
                GC.KeepAlive(new byte[256]);
                scopedGc.Stop();
                if (scopedGc.Count == 0) throw new InvalidOperationException("Scoped recorder did not detect its setup allocation.");
                allocationProfile = this;
            }
            if (QueryPerformanceFrequency(out counterFrequency) == 0) throw new InvalidOperationException("Performance counter unavailable.");
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = targetFrameRate; Application.runInBackground = true;
            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            began = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            if (finished) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (ClickScene)
            {
                if (orderPhase == 0 && now - began >= 1)
                {
                    if (modern) InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F2));
                    if (injectOverlayKey) InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F8));
                    orderPhase = 1;
                }
                else if (orderPhase == 1)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); orderPhase = 2;
                }
                // Overlay checks exercise field drawing during the normal warm-up too.
                else if ((measuring || expectRouteOverlay) && orderPhase == 2)
                {
                    orderPoint = orderCamera.WorldToScreenPoint(OrderDestination());
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = orderPoint, buttons = modern ? (ushort)2 : (ushort)1 });
                    orderPhase = 3;
                }
                else if (orderPhase == 3)
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = orderPoint });
                    releaseTime = now; releaseFrame = Time.frameCount; orderPhase = 4;
                }
            }
            if (measuring)
            {
                if (!Application.isFocused || Screen.width != 1920 || Screen.height != 1080 ||
                    Screen.fullScreenMode != FullScreenMode.FullScreenWindow) invalidFrames++;
                if (frames == FrameCapacity) throw new InvalidOperationException("Movement frame capacity exhausted.");
                // Before BeginFrame: record the preceding frame's summed scopes and
                // counters together, preserving the S, C and U frame sets (ADR 0012).
                mainTimes[frames] = main.LastValue; waitTimes[frames] = wait.LastValue;
                allocations[frames] = gcBytes.LastValue;
                allocationCounts[frames] = gc.Count == 0 ? 0 : gc.GetSample(0).Count;
                overlayCalls += overlay.Count == 0 ? 0 : overlay.GetSample(0).Count;
                overlayNanoseconds += overlay.LastValue;
                scheduled[frames] = driver.ScheduledTicks; completed[frames] = driver.CompletedTicks;
                frames++;
                // Diagnostic captures use 60 fps and short chunks so the Editor's
                // 2,000-frame loader can inspect allocation sites across the window.
                if (capture && now - measuredAt >= (capturePart + 1) * 20 && capturePart < capturePaths.Length - 1)
                {
                    UnityEngine.Profiling.Profiler.enabled = false;
                    UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                    capturePart++;
                    BeginCapture();
                }
                if (scene == 7 ? measurement.TickCount >= 1800 : now - measuredAt >= 60) { Finish(); return; }
            }
            else if (scene == 7 || now - began >= 10)
            {
                driver.Simulation.CompleteFrame();
                firstTick = measurement.TickCount;
                if (capture)
                {
                    BeginCapture();
                }
                measuring = true; measuredAt = now;
            }
        }

        private void BeginCapture()
        {
            UnityEngine.Profiling.Profiler.logFile = capturePaths[capturePart];
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
        }

        private void LateUpdate()
        {
            if (finished || !ClickScene) return;
            // After the normal sync point, before MatchDriver draws; never add a sync here.
            if (!selectionQueued && Time.realtimeSinceStartupAsDouble - began >= 1)
            {
                driver.Simulation.QueueSelection(SelectionRequest.Box(new Matrix4x4 { m00 = .001f, m12 = .001f, m33 = 1 }, new Rect(0, 0, 1, 1)));
                selectionQueued = true;
            }
            if (orderPhase < 4)
            {
                if (driver.Simulation.SelectedCount != unitCount) return;
                var matrices = driver.Simulation.DrawMatrices;
                for (int i = 0; i < unitCount; i++)
                {
                    Matrix4x4 pose = matrices[UnitSimulation.Capacity * 2 + i];
                    initialDraw[i] = new Vector4(pose.m03, pose.m23, pose.m00, pose.m20);
                }
                return;
            }
            if (Time.frameCount == releaseFrame + 1 && driver.Simulation.AcknowledgementCount == unitCount)
                acknowledgementFrame = 1;
            int tick = measurement.TickCount - 1;
            if (firstOrderTick < 0 && tick >= 0 && measurement.Metric(tick, 11) != 0) firstOrderTick = tick;
            if (firstOrderTick >= 0)
            {
                // Cache the completed job's independent order count before the driver
                // schedules again; inspect the renderer after SelectionController updates it.
                checkMarker = true;
                expectedMarker = measurement.Metric(tick, 11) > 0;
                if (!expectedMarker && lastOrderStoppedFrame < 0)
                {
                    lastOrderStoppedFrame = Time.frameCount;
                    searchesAtRest = driver.Simulation.GoalSearchCount;
                    requestsAtRest = driver.Simulation.FieldRequestCount;
                }
            }
            if (allDrawTime >= 0) return;
            var drawing = driver.Simulation.DrawMatrices;
            for (int i = 0; i < unitCount; i++)
            {
                Matrix4x4 matrix = drawing[UnitSimulation.Capacity * 2 + i];
                if (matrix.m03 != initialDraw[i].x || matrix.m23 != initialDraw[i].y ||
                    matrix.m00 != initialDraw[i].z || matrix.m20 != initialDraw[i].w) pendingDrawChanges[i] = true;
            }
        }

        private void RecordDrawnResponse(ScriptableRenderContext context, List<Camera> cameras)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (orderPhase >= 4 && !finished)
                for (int i = 0; i < unitCount; i++)
                    if (pendingDrawChanges[i] && vehicleDrawTimes[i] < 0)
                    {
                        vehicleDrawTimes[i] = now - releaseTime; drawnVehicles++;
                    }
            drawPending |= drawnVehicles > 0;
            allDrawPending |= drawnVehicles == unitCount;
            if (drawPending && firstDrawTime < 0) firstDrawTime = Time.realtimeSinceStartupAsDouble;
            if (allDrawPending && allDrawTime < 0) allDrawTime = Time.realtimeSinceStartupAsDouble;
            if (!checkMarker || finished) return;
            if (markerDrawing.enabled != expectedMarker) markerMismatches++;
            if (expectedMarker) markerTravelFrames++;
            else
            {
                markerSettledFrames++;
                if (!markerDrawing.enabled && markerHiddenFrame < 0) markerHiddenFrame = Time.frameCount;
            }
        }

        private void Finish()
        {
            double windowSeconds = Time.realtimeSinceStartupAsDouble - measuredAt;
            finished = true;
            if (capture)
            {
                UnityEngine.Profiling.Profiler.enabled = false;
                UnityEngine.Profiling.Profiler.enableBinaryLog = false;
            }
            driver.Simulation.CompleteFrame();
            WriteMovementJobLabels();
            using (var writer = new StreamWriter(output + "-ticks.csv"))
            {
                writer.WriteLine("tick,grid_ms,order_progress_ms,preferred_ms,integration_ms,overlap_ms,move_check_ms,commit_ms,total_ms,level_1,level_2,one_tick_stalls,still_over_5s,longest_still_ticks,alive,footprint_overlaps,static_regressions,gap_crossed,moving,preferred_nonzero,orders,exact,contact,congestion,rest_with_order,under_one_cell_progress,group_stall,pose_changes,pairs_under_clearance_floor,drawn_footprint_overlaps,drawn_pairs_inside_discs,only_creep_by_third_tick,lowest_clearance,lowest_drawn_clearance,deepest_small_graze,deepest_large_graze,drawn_slide_degrees,state_hash,first_advance_cells,second_advance_cells");
                // Negative ticks retain warm-up validation, without including it in timings.
                for (int t = 0; t < measurement.TickCount; t++)
                {
                    writer.Write(t - firstTick);
                    for (int b = 0; b < MovementMeasurement.TimestampStride - 1; b++) writer.Write("," + CounterMilliseconds(measurement.Timestamp(t, b + 1) - measurement.Timestamp(t, b)));
                    writer.Write("," + CounterMilliseconds(measurement.Timestamp(t, MovementMeasurement.TimestampStride - 1) - measurement.Timestamp(t, 0)));
                    for (int m = 0; m < MovementMeasurement.MetricCount; m++) writer.Write("," + measurement.Metric(t, m));
                    for (int m = 0; m < MovementMeasurement.FloatMetricCount; m++) writer.Write("," + measurement.SafetyValue(t, m).ToString("R", CultureInfo.InvariantCulture));
                    writer.Write("," + measurement.StateHash(t).ToString("X16", CultureInfo.InvariantCulture));
                    var progress = measurement.Progress(t);
                    writer.Write("," + progress.x.ToString("R", CultureInfo.InvariantCulture));
                    writer.Write("," + progress.y.ToString("R", CultureInfo.InvariantCulture));
                    writer.WriteLine();
                }
            }
            using (var writer = new StreamWriter(output + "-frames.csv"))
            {
                writer.WriteLine("frame,scheduled_ticks,completed_ticks,driver_total_ms,driver_wait_ms,driver_work_ms,gc_alloc_count,gc_bytes");
                for (int f = 0; f < frames; f++) writer.WriteLine(f + "," + scheduled[f] + "," + completed[f] + "," +
                    (mainTimes[f] * .000001).ToString("F6", CultureInfo.InvariantCulture) + "," +
                    (waitTimes[f] * .000001).ToString("F6", CultureInfo.InvariantCulture) + "," +
                    ((mainTimes[f] - waitTimes[f]) * .000001).ToString("F6", CultureInfo.InvariantCulture) + "," +
                    allocationCounts[f] + "," + allocations[f]);
            }
            File.WriteAllText(output + "-info.txt", "scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "; movement_scene=" + scene + "; drawing=on; vSync=0; targetFrameRate=" + targetFrameRate + "; warmup_s=10; window_s=60\n" +
                "passes=" + driver.MovementSettings.Snapshot().MoveCheckPasses + "; cpu=" + SystemInfo.processorType + "; gpu=" + SystemInfo.graphicsDeviceName + "\n" +
                "unfocused_or_wrong_resolution_frames=" + invalidFrames + "; measured_s=" +
                windowSeconds.ToString("F6", CultureInfo.InvariantCulture) + "\n" +
                "scoped_gc=" + scopedGc.Valid + "; simulation_alloc_count=" + simulationAllocations +
                "; drawing_alloc_count=" + drawingAllocations + "; scope_capacity_reached=" + scopeCapacityReached + "\n");
            File.AppendAllText(output + "-info.txt", "overlay_calls=" + overlayCalls +
                "; overlay_total_ms=" + (overlayNanoseconds * .000001).ToString("F6", CultureInfo.InvariantCulture) +
                "; overlay_expected=" + expectRouteOverlay + "; overlay_key_injected=" + injectOverlayKey + "\n");
            File.AppendAllText(output + "-info.txt", $"workers={workers}; batch={batchSize}; size_set={sizeSet}; heading_degrees={startHeading.ToString(CultureInfo.InvariantCulture)}; direction_degrees={orderDirection.ToString(CultureInfo.InvariantCulture)}; inner_margin_rule=absent_on_baseline_motor; drawn_samples=0.25,0.5,0.75,1\n");
            using (var writer = new StreamWriter(output + "-response.csv")) measurement.WriteResponse(writer);
            bool orderPassed = true;
            if (ClickScene)
            {
                using (var writer = new StreamWriter(output + "-orders.csv")) measurement.WriteOrderState(writer);
                int tick = measurement.TickCount - 1;
                int lastChangingTick = firstOrderTick;
                for (int t = Math.Max(0, firstOrderTick); t <= tick; t++)
                {
                    orderPassed &= measurement.Metric(t, 6) == 0 && measurement.Metric(t, 7) == 0;
                    if (measurement.Metric(t, 9) != 0 || measurement.Metric(t, 18) != 0) lastChangingTick = t;
                }
                int stableTick = lastChangingTick - firstOrderTick + 2;
                orderPassed = controls.Modern == modern && acknowledgementFrame == 1 && firstOrderTick >= 0 &&
                    measurement.Metric(firstOrderTick, 10) == unitCount && measurement.Metric(firstOrderTick, 11) == unitCount && orderPassed;
                orderPassed &= markerTravelFrames > 0 && markerMismatches == 0;
                if (unitCount <= 100 && scene == 4)
                {
                    orderPassed &= markerSettledFrames > 0 && markerHiddenFrame == lastOrderStoppedFrame;
                    orderPassed &= measurement.Metric(tick, 16) == 0 && stableTick <= 1140;
                    for (int t = tick - 59; t <= tick; t++)
                        orderPassed &= measurement.Metric(t, 9) == 0 && measurement.Metric(t, 11) == 0 &&
                            measurement.Metric(t, 18) == 0 && measurement.Metric(t, 2) == unitCount;
                }
                File.WriteAllText(output + "-order.txt", "option=" + (modern ? "Modern" : "Classic") + "; units=" + unitCount +
                    "; response_ms=" + ((firstDrawTime - releaseTime) * 1000).ToString("F6", CultureInfo.InvariantCulture) +
                    "; all_drawn_ms=" + ((allDrawTime - releaseTime) * 1000).ToString("F6", CultureInfo.InvariantCulture) +
                    "; acknowledgement_frames=" + acknowledgementFrame + "; first_order_tick=" + firstOrderTick +
                    "; first_tick_moving=" + (firstOrderTick < 0 ? 0 : measurement.Metric(firstOrderTick, 9)) +
                    "; T=" + stableTick + "; exact/contact/congestion/group_stall=" + measurement.Metric(tick, 12) + "/" +
                    measurement.Metric(tick, 13) + "/" + measurement.Metric(tick, 14) + "/" + measurement.Metric(tick, 17) +
                    "; at_rest_with_order=" + measurement.Metric(tick, 15) +
                    "; marker_version=" + driver.Simulation.MoveMarkerVersion + "; marker=" + driver.Simulation.MoveMarkerPosition +
                    "; marker_travel_frames=" + markerTravelFrames + "; marker_settled_frames=" + markerSettledFrames +
                    "; marker_mismatches=" + markerMismatches + "; last_order_stopped_frame=" + lastOrderStoppedFrame +
                    "; marker_hidden_frame=" + markerHiddenFrame +
                    "; pass=" + orderPassed + "\n");
                using var responseWriter = new StreamWriter(output + "-drawn-response.csv");
                responseWriter.WriteLine("unit,release_to_first_drawn_pose_seconds");
                for (int i = 0; i < unitCount; i++) responseWriter.WriteLine(i + "," + vehicleDrawTimes[i].ToString("R", CultureInfo.InvariantCulture));
            }
            if (scene == 5)
            {
                using var writer = new StreamWriter(output + "-static-maps.txt");
                writer.WriteLine("sandbox=256x256; map_ms=" + driver.Simulation.StaticMapBuildMilliseconds.ToString("R", CultureInfo.InvariantCulture) +
                    "; map_MiB=" + (driver.Simulation.StaticMapBytes / 1048576.0).ToString("R", CultureInfo.InvariantCulture) +
                    "; scratch_MiB=" + (driver.Simulation.StaticMapScratchBytes / 1048576.0).ToString("R", CultureInfo.InvariantCulture));
                int last = measurement.TickCount - 1;
                bool stopped = true;
                for (int t = last - 59; t <= last; t++)
                    stopped &= measurement.Metric(t, 9) == 0 && measurement.Metric(t, 18) == 0;
                measurement.CountPastSandboxWall(out int smallPast, out int largePast);
                orderPassed &= smallPast > 0 && largePast > 0;
                writer.WriteLine("stopped=" + stopped + "; small_past_wall=" + smallPast + "; large_past_wall=" + largePast +
                    "; all_past_wall=" + (smallPast + largePast == unitCount) + "; pass=" + orderPassed);
            }
            if (scene == 6)
            {
                int last = measurement.TickCount - 1;
                bool stopped = lastOrderStoppedFrame >= 0;
                for (int t = last - 59; t <= last; t++)
                    stopped &= measurement.Metric(t, 9) == 0 && measurement.Metric(t, 11) == 0 && measurement.Metric(t, 18) == 0;
                bool noRetries = searchesAtRest == 1 && requestsAtRest <= 1 &&
                    driver.Simulation.GoalSearchCount == searchesAtRest && driver.Simulation.FieldRequestCount == requestsAtRest;
                // ADR 0015 reports crowd stalls here; Issue 5 gates arrival without them.
                orderPassed &= stopped && noRetries;
                File.WriteAllText(output + "-unreachable.txt", "stopped=" + stopped + "; searches=" + driver.Simulation.GoalSearchCount +
                    "; requests=" + driver.Simulation.FieldRequestCount + "; no_retries=" + noRetries +
                    "; exact/contact/congestion/group_stall=" + measurement.Metric(last, 12) + "/" +
                    measurement.Metric(last, 13) + "/" + measurement.Metric(last, 14) + "/" + measurement.Metric(last, 17) +
                    "; positions_file=" + Path.GetFileName(output) + "-orders.csv; pass=" + orderPassed + "\n");
            }
            bool scopesPassed = !scopedGc.Valid || simulationAllocations == 0 && drawingAllocations == 0;
            bool overlayPassed = expectRouteOverlay ? overlayCalls > 0 : overlayCalls == 0;
            Application.Quit(invalidFrames == 0 && !scopeCapacityReached && scopesPassed && overlayPassed && orderPassed ? 0 : 1);
        }

        private Vector3 OrderDestination() => scene == 6 ? new Vector3(88, 0, 30) : scene == 5 ? new Vector3(40, 0, 0) :
            new Vector3(Mathf.Cos(orderDirection * Mathf.Deg2Rad), 0, Mathf.Sin(orderDirection * Mathf.Deg2Rad)) * orderDistance;

        private static string Option(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return fallback;
            if (index + 1 == args.Length) throw new ArgumentException("Missing value for " + name);
            return args[index + 1];
        }

        private static int IntegerOption(string[] args, string name, int fallback)
            => int.Parse(Option(args, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

        private static float FloatOption(string[] args, string name, float fallback)
        {
            float value = float.Parse(Option(args, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
            if (!float.IsFinite(value)) throw new ArgumentException("Invalid value for " + name);
            return value;
        }

        private void WriteMovementJobLabels()
        {
            // Enumerate only after the window: this evidence must not allocate in measured frames.
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            using var writer = new StreamWriter(output + "-job-labels.txt");
            foreach (ProfilerRecorderHandle handle in handles)
            {
                string name = ProfilerRecorderHandle.GetDescription(handle).Name;
                if (name.Contains("Northwatch.Simulation") || name.Contains("MovementJob") ||
                    name.Contains("OrderProgressJob") ||
                    name.Contains("PreferredVelocityJob") || name.Contains("MoveCheckPassJob") ||
                    name.Contains("SpatialGridJob") || name.Contains("BucketUnitsJob"))
                    writer.WriteLine(name);
            }
        }

        private string CounterMilliseconds(long value) => (value * 1000.0 / counterFrequency).ToString("F6", CultureInfo.InvariantCulture);
        private void OnDestroy()
        {
            RenderPipelineManager.endContextRendering -= RecordDrawnResponse;
            if (allocationProfile == this) allocationProfile = null;
            if (scopedGc.Valid) scopedGc.Dispose();
            main.Dispose(); wait.Dispose(); gc.Dispose(); gcBytes.Dispose(); overlay.Dispose();
        }
    }
}
#endif
