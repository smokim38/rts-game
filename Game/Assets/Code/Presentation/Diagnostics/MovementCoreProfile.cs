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

namespace Northwatch.Presentation.Diagnostics
{
    [DefaultExecutionOrder(-20000)]
    public sealed class MovementCoreProfile : MonoBehaviour
    {
        private const int FrameCapacity = 1000000;
        private MatchDriver driver;
        private MovementMeasurement measurement;
        private ProfilerRecorder main, wait, gc, gcBytes;
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
        private bool scopeCapacityReached;
        private int scene, unitCount, orderPhase, releaseFrame, acknowledgementFrame = -1, firstOrderTick = -1;
        private bool modern, selectionQueued, drawPending, allDrawPending;
        private Mouse mouse;
        private Keyboard keyboard;
        private SelectionController controls;
        private Camera orderCamera;
        private Vector2 orderPoint;
        private Vector2[] initialDraw;
        private double releaseTime, firstDrawTime = -1, allDrawTime = -1;
        private MeshRenderer markerDrawing;
        private bool checkMarker, expectedMarker;
        private int markerTravelFrames, markerSettledFrames, markerMismatches;
        private int lastOrderStoppedFrame = -1, markerHiddenFrame = -1;
        private MovementCheckRenderer batchRenderer;

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-movement-core-profile") < 0) return;
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
            string[] args = Environment.GetCommandLineArgs();
            capture = Array.IndexOf(args, "-movement-capture") >= 0;
            int index = Array.IndexOf(args, "-movement-output"), sceneIndex = Array.IndexOf(args, "-movement-scene");
            int countIndex = Array.IndexOf(args, "-movement-units");
            int fpsIndex = Array.IndexOf(args, "-movement-fps");
            if (fpsIndex >= 0 && (fpsIndex + 1 >= args.Length ||
                !int.TryParse(args[fpsIndex + 1], out targetFrameRate) || (targetFrameRate != -1 && targetFrameRate != 60)))
                throw new ArgumentException("Use -movement-fps -1|60 (default -1).");
            if (!Debug.isDebugBuild || index < 0 || index + 1 >= args.Length || sceneIndex < 0 || sceneIndex + 1 >= args.Length ||
                countIndex < 0 || countIndex + 1 >= args.Length)
                throw new ArgumentException("Use a development Player with -movement-output PATH -movement-scene 0|1|2|3 -movement-units N.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            output = Path.GetFullPath(args[index + 1]);
            if (!output.StartsWith(Path.Combine(project, "Logs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Movement output must be under this project's Logs folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if (capture)
                capturePaths = new[] { output + "-0.raw", output + "-1.raw", output + "-2.raw", output + "-3.raw" };
            driver = FindFirstObjectByType<MatchDriver>();
            if (driver == null || driver.MovementSettings == null) throw new InvalidOperationException("The match has no driver or movement settings.");
            if (Application.isBatchMode)
            {
                batchRenderer = gameObject.AddComponent<MovementCheckRenderer>();
                batchRenderer.Initialize(FindFirstObjectByType<Camera>());
            }
            scene = int.Parse(args[sceneIndex + 1], CultureInfo.InvariantCulture);
            unitCount = int.Parse(args[countIndex + 1], CultureInfo.InvariantCulture);
            driver.Simulation.MeasureMovement(scene, unitCount);
            measurement = driver.Simulation.Measurement;
            if (scene == 4)
            {
                controls = FindFirstObjectByType<SelectionController>();
                orderCamera = Camera.main;
                if (controls == null || orderCamera == null) throw new InvalidOperationException("Open orders require the sandbox Player.");
                markerDrawing = driver.transform.Find("MoveMarker").GetComponent<MeshRenderer>();
                modern = Array.IndexOf(args, "-movement-modern") >= 0;
                mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
                gameObject.AddComponent<SelectionInputPump>();
                initialDraw = new Vector2[unitCount];
                orderCamera.GetComponent<Cameras.CameraController>().CentreOn(new Vector3(unitCount <= 100 ? 7.5f : 30, 0, 0));
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
            if (!main.Valid || !wait.Valid || !gc.Valid || !gcBytes.Valid) throw new InvalidOperationException("Required driver/wait/GC recorders are unavailable.");
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
            if (scene == 4)
            {
                if (orderPhase == 0 && now - began >= 1)
                {
                    if (modern) InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F2));
                    orderPhase = 1;
                }
                else if (orderPhase == 1)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); orderPhase = 2;
                }
                else if (measuring && orderPhase == 2)
                {
                    orderPoint = orderCamera.WorldToScreenPoint(new Vector3(unitCount == 5 ? 20 : unitCount == 100 ? 15 : 60, 0, 0));
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
                if (!Application.isFocused || Screen.width != 1920 || Screen.height != 1080) invalidFrames++;
                if (frames == FrameCapacity) throw new InvalidOperationException("Movement frame capacity exhausted.");
                // Before BeginFrame: record the preceding frame's summed scopes and
                // counters together, preserving the S, C and U frame sets (ADR 0012).
                mainTimes[frames] = main.LastValue; waitTimes[frames] = wait.LastValue;
                allocations[frames] = gcBytes.LastValue;
                allocationCounts[frames] = gc.Count == 0 ? 0 : gc.GetSample(0).Count;
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
                if (now - measuredAt >= 60) { Finish(); return; }
            }
            else if (now - began >= 10)
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
            if (finished || scene != 4) return;
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
                for (int i = 0; i < unitCount; i++) initialDraw[i] = new Vector2(matrices[UnitSimulation.Capacity * 2 + i].m03, matrices[UnitSimulation.Capacity * 2 + i].m23);
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
                if (!expectedMarker && lastOrderStoppedFrame < 0) lastOrderStoppedFrame = Time.frameCount;
            }
            if (allDrawTime >= 0) return;
            int moved = 0;
            var drawing = driver.Simulation.DrawMatrices;
            for (int i = 0; i < unitCount; i++)
            {
                Matrix4x4 matrix = drawing[UnitSimulation.Capacity * 2 + i];
                if (Vector2.SqrMagnitude(new Vector2(matrix.m03, matrix.m23) - initialDraw[i]) > 1e-10f) moved++;
            }
            drawPending |= moved > 0;
            allDrawPending |= moved == unitCount;
        }

        private void RecordDrawnResponse(ScriptableRenderContext context, List<Camera> cameras)
        {
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
                writer.WriteLine("tick,grid_ms,order_progress_ms,preferred_ms,integration_ms,overlap_ms,move_check_ms,commit_ms,total_ms,level_1,level_2,one_tick_stalls,still_over_5s,longest_still_ticks,alive,footprint_overlaps,static_regressions,gap_crossed,moving,preferred_nonzero,orders,exact,contact,congestion,rest_with_order,under_one_cell_progress,group_stall,pose_changes,first_advance_cells,second_advance_cells");
                // Negative ticks retain warm-up validation, without including it in timings.
                for (int t = 0; t < measurement.TickCount; t++)
                {
                    writer.Write(t - firstTick);
                    for (int b = 0; b < MovementMeasurement.TimestampStride - 1; b++) writer.Write("," + CounterMilliseconds(measurement.Timestamp(t, b + 1) - measurement.Timestamp(t, b)));
                    writer.Write("," + CounterMilliseconds(measurement.Timestamp(t, MovementMeasurement.TimestampStride - 1) - measurement.Timestamp(t, 0)));
                    for (int m = 0; m < MovementMeasurement.MetricCount; m++) writer.Write("," + measurement.Metric(t, m));
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
                "; drawing_alloc_count=" + drawingAllocations + "; scope_capacity_reached=" + scopeCapacityReached + "\n" +
                "batch_rendered_frames=" + (batchRenderer == null ? -1 : batchRenderer.RenderedFrames) + "\n");
            bool orderPassed = true;
            if (scene == 4)
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
                    firstDrawTime >= releaseTime && firstDrawTime - releaseTime <= .1 &&
                    measurement.Metric(firstOrderTick, 10) == unitCount && measurement.Metric(firstOrderTick, 11) == unitCount && orderPassed;
                orderPassed &= markerTravelFrames > 0 && markerMismatches == 0;
                if (unitCount <= 100)
                {
                    orderPassed &= markerSettledFrames > 0 && markerHiddenFrame == lastOrderStoppedFrame;
                    orderPassed &= allDrawTime >= releaseTime && allDrawTime - releaseTime <= .1 &&
                        measurement.Metric(firstOrderTick, 9) == unitCount && measurement.Metric(tick, 16) == 0 && stableTick <= 1140;
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
            }
            Application.Quit(invalidFrames == 0 && !scopeCapacityReached && orderPassed &&
                (batchRenderer == null || batchRenderer.RenderedFrames >= 200) ? 0 : 1);
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
            main.Dispose(); wait.Dispose(); gc.Dispose(); gcBytes.Dispose();
        }
    }
}
#endif
