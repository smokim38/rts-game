using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Northwatch.Presentation.Diagnostics
{
    [DefaultExecutionOrder(3000)]
    public sealed class SandboxPlayerCheck : MonoBehaviour
    {
        private MatchDriver driver;
        private int frames, unitCount;
        private string output;
        private Camera captureCamera;
        private Cameras.CameraController cameraController;
        private RenderTexture captureTarget;
        private UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest renderRequest;
        private DebugOverlay overlay;
        private double measuredSeconds;
        private int measuredFrames;
        private bool selectionCheck;
        private int selectionSamples, selectionResponseFrame;
        private double selectionTotalMs, selectionMaxMs;
        private Mouse selectionMouse;
        private bool doubleClickCheck;
        private Vector2 doubleClickPoint;
        private bool moveMarkerCheck;
        private Keyboard checkKeyboard;
        private Vector2 moveClick;
        private int imageFirst, imageSecond, imageSelected, savedImages, confirmedSelection;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartCheck()
        {
            string[] arguments = System.Environment.GetCommandLineArgs();
            int index = System.Array.IndexOf(arguments, "-sandbox-check");
            if (index < 0) return;
            if (index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out int count) || (count != 0 && count != 2000))
                throw new System.ArgumentException("Use -sandbox-check 0 or -sandbox-check 2000.");
            string output = OutputPrefix(Application.dataPath, count);
            var check = new GameObject("SandboxPlayerCheck").AddComponent<SandboxPlayerCheck>();
            check.unitCount = count;
            check.driver = FindFirstObjectByType<MatchDriver>();
            if (check.driver == null || check.driver.Simulation.Count != 0)
                throw new System.InvalidOperationException("Sandbox must start with an empty match.");
            check.output = output;
            check.selectionCheck = System.Array.IndexOf(arguments, "-sandbox-selection") >= 0;
            check.doubleClickCheck = System.Array.IndexOf(arguments, "-sandbox-double-click") >= 0;
            check.moveMarkerCheck = System.Array.IndexOf(arguments, "-sandbox-move-marker") >= 0;
            check.selectionCheck |= check.doubleClickCheck || check.moveMarkerCheck;
            if (check.selectionCheck)
            {
                check.output += "-selection";
                if (check.doubleClickCheck) check.output += "-double";
                // A batch Player has no native pointer. The virtual device feeds the same action bindings.
                check.selectionMouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                if (check.moveMarkerCheck) check.checkKeyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
                check.gameObject.AddComponent<SelectionInputPump>();
                // Keep edge panning idle throughout warm-up, before the first diagnostic drag.
                InputSystem.QueueStateEvent(check.selectionMouse, new MouseState
                    { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) });
            }
            check.overlay = FindFirstObjectByType<DebugOverlay>();
            if (System.Array.IndexOf(arguments, "-sandbox-no-overlay") >= 0)
            {
                check.overlay.gameObject.SetActive(false);
                check.output += "-baseline";
            }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            check.captureCamera = Camera.main;
            check.cameraController = check.captureCamera.GetComponent<Cameras.CameraController>();
            check.captureTarget = new RenderTexture(1920, 1080, 24);
            if (!check.captureTarget.Create()) throw new System.InvalidOperationException("Could not create validation render target.");
            check.captureCamera.aspect = 16f / 9f;
            check.renderRequest = CreateRenderRequest(check.captureTarget);
            QueueUnits(check.driver.Simulation, count, check.selectionCheck);
        }

        private static void QueueUnits(Simulation.UnitSimulation simulation, int count, bool selection)
        {
            if (count == 2000 && selection)
            {
                if (!simulation.QueueBlock(Simulation.Side.First, Simulation.UnitSize.Small, Vector3.zero, count))
                    throw new System.InvalidOperationException("Selection check could not queue 2000 own units.");
            }
            else if (count == 2000 && (!simulation.QueueBlock(Simulation.Side.First, Simulation.UnitSize.Small,
                    new Vector3(-40, 0, 0), 1000) || !simulation.QueueBlock(Simulation.Side.Second,
                    Simulation.UnitSize.Large, new Vector3(-40, 0, 40), 1000)))
                throw new System.InvalidOperationException("Sandbox check could not queue two blocks of 1000.");
        }

        private static string OutputPrefix(string dataPath, int count)
        {
            string project = System.IO.Path.GetFullPath(System.IO.Path.Combine(dataPath, "../../.."));
            if (!System.IO.File.Exists(System.IO.Path.Combine(project, "ProjectSettings", "ProjectVersion.txt")))
                throw new System.InvalidOperationException("Sandbox checks must run from a build inside the Unity project.");
            return System.IO.Path.Combine(project, "Logs", "sandbox-" + count);
        }

        private void Update()
        {
            if ((!Application.isBatchMode && !UnityEngine.Rendering.SplashScreen.isFinished) ||
                Time.realtimeSinceStartupAsDouble < 10.0) return;
            frames++;
            if (moveMarkerCheck && frames <= 180) CheckMoveMarkerFrame();
            // After the camera reset, resume on phase 0 so the first box starts with its press.
            else if (selectionCheck && (!moveMarkerCheck || frames >= 190)) CheckSelectionFrame();
            if (!enabled) return;
            imageFirst = driver.Simulation.DrawCount(Simulation.Side.First) - driver.Simulation.SelectedCount;
            imageSecond = driver.Simulation.DrawCount(Simulation.Side.Second);
            imageSelected = driver.Simulation.SelectedCount;
            if (frames > 120) { measuredFrames++; measuredSeconds += Time.unscaledDeltaTime; }
            if (frames == (moveMarkerCheck ? 180 : 120))
            {
#if DEVELOPMENT_BUILD
                UnityEngine.Profiling.Profiler.logFile = output + ".raw";
                UnityEngine.Profiling.Profiler.enableBinaryLog = true;
                UnityEngine.Profiling.Profiler.enabled = true;
#endif
            }
            if (frames < 420 || measuredSeconds < 3.0) return;
#if DEVELOPMENT_BUILD
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.enableBinaryLog = false;
#endif
            int count = driver.Simulation.Count;
            Debug.Log("Sandbox check: scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                "; units=" + count + "; ticks=" + driver.Simulation.TickCount + "; rendered frames=" + frames);
            Debug.Log("Sandbox counter: A=" + driver.Simulation.DrawCount(Simulation.Side.First) +
                "; B=" + driver.Simulation.DrawCount(Simulation.Side.Second) + "; total=" + count +
                "; counter_fps=" + overlay.DisplayedFramesPerSecond +
                "; fps=" + overlay.FramesPerSecond.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "; frame_ms=" + overlay.FrameMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; measured_fps=" + (measuredFrames / measuredSeconds).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "; gpu=" + SystemInfo.graphicsDeviceName + "; cpu=" + SystemInfo.processorType);
            if (selectionCheck)
                Debug.Log("Selection check: own_units=" + confirmedSelection + "; selected=" + confirmedSelection + "; samples=" + selectionSamples +
                    "; response_frames=1; selected_frame_average_ms=" + (selectionTotalMs / selectionSamples).ToString("F6", System.Globalization.CultureInfo.InvariantCulture) +
                    "; selected_frame_max_ms=" + selectionMaxMs.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) +
                    "; resolution=" + Screen.width + "x" + Screen.height + "; focused=" + Application.isFocused);
            Application.Quit(CountersValid(unitCount, selectionCheck, driver.Simulation.DrawCount(Simulation.Side.First),
                driver.Simulation.DrawCount(Simulation.Side.Second), count) && savedImages > 0 &&
                (!selectionCheck || (confirmedSelection == 2000 && selectionSamples > 0)) ? 0 : 1);
        }

        private static bool CountersValid(int expected, bool selection, int first, int second, int total)
            => total == expected && first == (expected == 0 ? 0 : selection ? 2000 : 1000) &&
                second == (expected == 0 || selection ? 0 : 1000);

        private void CheckSelectionFrame()
        {
            if (frames < 50) return;
            // Keep the move ring visible throughout selection timing, including the plain selection run.
            if (frames == 54)
                driver.Simulation.QueueSelection(Simulation.SelectionRequest.PointerClick(
                    captureCamera.ScreenPointToRay(captureCamera.WorldToScreenPoint(new Vector3(30, 0, 0))),
                    captureCamera.projectionMatrix * captureCamera.worldToCameraMatrix, false, true, false, false, false));
            if (Time.frameCount == selectionResponseFrame)
            {
                if (driver.Simulation.SelectedCount != 2000 ||
                    driver.Simulation.DrawMatrices[Simulation.UnitSimulation.Capacity * 3 - 1].m33 != 1f)
                {
                    FailSelection("2000 selected units must have draw data on the next frame; selected=" +
                        driver.Simulation.SelectedCount + "; frame=" + frames + "; focused=" + Application.isFocused);
                    return;
                }
                confirmedSelection = driver.Simulation.SelectedCount;
            }
            if (Time.frameCount == selectionResponseFrame + 1 && frames > 120)
            {
                // Delta at the next frame's start measures the completed selection frame, including waits.
                double milliseconds = Time.unscaledDeltaTime * 1000.0;
                selectionSamples++; selectionTotalMs += milliseconds;
                selectionMaxMs = System.Math.Max(selectionMaxMs, milliseconds);
                if (milliseconds > 33.0) { FailSelection("Selection frame exceeded 33 ms."); return; }
            }
            int phase = frames % 10;
            if (doubleClickCheck)
            {
                if (frames == 50) doubleClickPoint = captureCamera.WorldToScreenPoint(driver.Simulation.DrawMatrices[0].GetColumn(3));
                InputSystem.QueueStateEvent(selectionMouse, new MouseState
                {
                    position = doubleClickPoint,
                    buttons = phase == 0 || phase == 2 ? (ushort)1 : phase == 5 ? (ushort)2 : (ushort)0
                });
                // Queued events reach input next frame; check draw data one frame after that.
                if (phase == 3) selectionResponseFrame = Time.frameCount + 2;
                return;
            }
            if (phase == 3 || phase == 4 || phase == 8 || phase == 9) return;
            Vector2 viewport = phase == 0 ? new Vector2(0.4f, 0.4f) : phase <= 2 ? new Vector2(0.6f, 0.6f) :
                phase == 5 ? new Vector2(0.9f, 0.1f) : new Vector2(0.92f, 0.12f);
            InputSystem.QueueStateEvent(selectionMouse, new MouseState
            {
                position = captureCamera.ViewportToScreenPoint(viewport),
                buttons = phase == 2 || phase == 7 ? (ushort)0 : (ushort)1
            });
            if (phase == 2) selectionResponseFrame = Time.frameCount + 2;
        }

        private void CheckMoveMarkerFrame()
        {
            if (frames < 50) return;
            if (frames == 180)
            {
                // The following selection measurement must fit all 2,000 vehicles in its box again.
                captureCamera.orthographicSize = 200f;
                cameraController.CentreOn(Vector3.zero);
            }
            if (frames < 54)
            {
                // Use the real box gesture before giving move orders to all 2,000 vehicles.
                Vector2 p = frames == 50 ? new Vector2(0.4f, 0.4f) : new Vector2(0.6f, 0.6f);
                InputSystem.QueueStateEvent(selectionMouse, new MouseState
                    { position = captureCamera.ViewportToScreenPoint(p), buttons = frames < 52 ? (ushort)1 : (ushort)0 });
                return;
            }
            if (driver.Simulation.SelectedCount != 2000) { FailSelection("Move marker check lost its selection."); return; }
            // Far, near, then a drag pan, in Classic followed by Modern. Zoom through the wheel binding.
            int phase = frames < 105 ? frames : frames - 60;
            Vector2 position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            ushort buttons = 0;
            if (phase == 55 || phase == 75 || phase == 95)
            {
                moveClick = captureCamera.WorldToScreenPoint(new Vector3(30, 0, 0));
                position = moveClick; buttons = frames < 105 ? (ushort)1 : (ushort)2;
            }
            else if (phase == 56 || phase == 76 || phase == 96) position = moveClick;
            else if (phase == 90 || phase == 91)
            {
                position += Vector2.right * (phase == 90 ? 0 : 100);
                buttons = frames < 105 ? (ushort)2 : (ushort)4;
            }
            else if (phase == 92) position += Vector2.right * 100;
            InputSystem.QueueStateEvent(selectionMouse, new MouseState
            {
                position = position, buttons = buttons,
                scroll = new Vector2(0, phase == 70 ? 12000 : frames == 110 ? -12000 : 0)
            });
            InputSystem.QueueStateEvent(checkKeyboard, frames == 105 ? new KeyboardState(Key.F2) : new KeyboardState());
        }

        private void CaptureMoveMarker()
        {
            if (Screen.width != 1920 || Screen.height != 1080)
            {
                FailSelection("Move marker screenshots require a 1920x1080 Player.");
                return;
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = captureTarget;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            int pixels = 0;
            int left = 1920, right = -1, bottom = 1080, top = -1;
            Vector2 sum = Vector2.zero;
            // Green is absent from the grey ground and the blue, red and yellow units.
            for (int y = Mathf.Max(0, (int)moveClick.y - 40); y < Mathf.Min(1080, moveClick.y + 40); y++)
                for (int x = Mathf.Max(0, (int)moveClick.x - 40); x < Mathf.Min(1920, moveClick.x + 40); x++)
                {
                    Color colour = image.GetPixel(x, y);
                    if (colour.g < 0.6f || colour.r > 0.4f || colour.b > 0.4f) continue;
                    pixels++; sum += new Vector2(x + 0.5f, y + 0.5f);
                    left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                    bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
                }
            string option = frames <= 100 ? "classic" : "modern";
            string zoom = frames == 60 || frames == 120 ? "far" : frames == 80 || frames == 140 ? "near" : "pan";
            try { SaveImage(image, "-" + option + "-" + zoom); }
            finally { RenderTexture.active = previous; Destroy(image); }
            Debug.Log("Move marker: " + option + "; " + zoom + "; click=" + moveClick + "; pixels=" + pixels +
                "; centre_error_px=" + (pixels == 0 ? -1f : Vector2.Distance(sum / pixels, moveClick)) +
                "; diameter_px=" + (right - left + 1) + "; height_px=" + (top - bottom + 1) +
                "; zoom=" + captureCamera.orthographicSize);
            if (pixels == 0 || Vector2.Distance(sum / pixels, moveClick) > 8f)
                FailSelection("Move marker pixels must be within 8 pixels of the click.");
            else if (zoom == "far" && (right - left + 1 < 22 || right - left + 1 > 30))
                FailSelection("The far-zoom move ring must stay about 24 pixels across throughout its pulse.");
        }

        private void FailSelection(string message)
        {
            enabled = false;
            Debug.LogError(message);
            Application.Quit(1);
        }

        private void OnDestroy()
        {
            if (captureTarget == null) return;
            captureTarget.Release();
            Destroy(captureTarget);
        }

        private void LateUpdate()
        {
            if (frames == 0) return;
            // Batch Players need an explicit pass. MatchDriver has now submitted this frame's vehicles.
            try
            {
                RenderCamera(captureCamera, renderRequest);
                CaptureFrame();
            }
            catch (System.Exception error) { FailSelection(error.Message); }
        }

        private void CaptureFrame()
        {
            if (!enabled) return;
            bool marker = moveMarkerCheck && (frames == 60 || frames == 80 || frames == 100 || frames == 120 || frames == 140 || frames == 160);
            bool main = frames == (selectionCheck ? doubleClickCheck ? 65 : 64 : 60) || (selectionCheck && frames == 62);
            if (!marker && !main) return;
            if (marker) CaptureMoveMarker();
            if (!main) return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = captureTarget;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            try { SaveImage(image, selectionCheck && frames == 62 ? "-drag" : ""); }
            finally { RenderTexture.active = previous; Destroy(image); }
        }

        private void SaveImage(Texture2D image, string suffix)
        {
            string path = output + suffix + ".png";
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            if (!ImageValid(image, imageFirst, imageSecond, imageSelected))
                throw new System.InvalidOperationException("Sandbox image has no vehicles in the expected colours: " + path);
            if (unitCount > 0) CheckRaisedVehiclePixels(image, path);
            savedImages++;
        }

        private void CheckRaisedVehiclePixels(Texture2D image, string path)
        {
            try
            {
                // Acknowledgement pads reach Y=.0225. Clip below Y=.05 so their yellow
                // pixels cannot substitute for the vehicle bodies in this same frame.
                captureCamera.projectionMatrix = VehicleProjection(captureCamera);
                RenderCamera(captureCamera, renderRequest);
                RenderTexture.active = captureTarget;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(path + "-vehicles.png", image.EncodeToPNG());
                if (!VehicleColoursValid(image, imageFirst, imageSecond, imageSelected))
                    throw new System.InvalidOperationException("Sandbox image has no raised vehicles in the expected colours: " + path);
            }
            // Assigning the old matrix would leave automatic zoom updates disabled.
            finally { captureCamera.ResetProjectionMatrix(); }
        }

        private static Matrix4x4 VehicleProjection(Camera camera)
        {
            Matrix4x4 view = camera.worldToCameraMatrix;
            Vector3 point = view.MultiplyPoint(new Vector3(0, .05f, 0));
            Vector3 normal = view.MultiplyVector(Vector3.up).normalized;
            Vector4 plane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(point, normal));
            Matrix4x4 projection = camera.projectionMatrix;
            // Map world heights .05..1000 to clip depth -1..1. Unlike the usual
            // oblique far-corner formula, this keeps the upper half-space even
            // when every far-plane corner of the downward-looking camera is underground.
            projection.SetRow(2, plane * (2f / 999.95f) - projection.GetRow(3));
            return projection;
        }

        private static UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest CreateRenderRequest(RenderTexture target)
            => new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target };

        private static void RenderCamera(Camera camera, UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest request)
            => UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);

        private static bool ImageValid(Texture2D image, int first, int second, int selected)
        {
            if (image.GetPixel(image.width / 2, image.height / 2).grayscale < .2f) return false;
            return VehicleColoursValid(image, first, second, selected);
        }

        private static bool VehicleColoursValid(Texture2D image, int first, int second, int selected)
        {
            int blue = 0, red = 0, yellow = 0;
            Color32[] pixels = image.GetPixels32();
            foreach (Color32 colour in pixels)
            {
                if (colour.b > 100 && colour.b > colour.r * 1.4f && colour.b > colour.g * 1.2f) blue++;
                if (colour.r > 100 && colour.r > colour.g * 1.4f && colour.r > colour.b * 1.4f) red++;
                if (colour.r > 128 && colour.g > 100 && Mathf.Min(colour.r, colour.g) > colour.b * 1.5f) yellow++;
            }
            // A grey map, white overlay or green move ring cannot stand in for vehicle pixels.
            return blue >= Mathf.Min(first, 100) && red >= Mathf.Min(second, 100) && yellow >= Mathf.Min(selected, 100);
        }
    }
}
