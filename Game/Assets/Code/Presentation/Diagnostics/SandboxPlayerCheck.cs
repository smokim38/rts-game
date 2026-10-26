using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Northwatch.Presentation.Diagnostics
{
    [DefaultExecutionOrder(300)]
    public sealed class SandboxPlayerCheck : MonoBehaviour
    {
        private MatchDriver driver;
        private int frames, unitCount;
        private string output;
        private Camera captureCamera;
        private RenderTexture captureTarget;
        private UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest renderRequest;
        private DebugOverlay overlay;
        private double measuredSeconds;
        private int measuredFrames;
        private bool selectionCheck;
        private int selectionSamples, selectionResponseFrame;
        private double selectionTotalMs, selectionMaxMs;
        private Mouse selectionMouse;

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
            if (check.selectionCheck)
            {
                check.output += "-selection";
                // A batch Player has no native pointer. The virtual device feeds the same action bindings.
                check.selectionMouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
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
            check.captureTarget = new RenderTexture(1920, 1080, 24);
            if (!check.captureTarget.Create()) throw new System.InvalidOperationException("Could not create validation render target.");
            check.captureCamera.aspect = 16f / 9f;
            check.renderRequest = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest
                { destination = check.captureTarget };
            if (count == 2000 && check.selectionCheck)
            {
                if (!check.driver.Simulation.QueueBlock(Simulation.Side.First, Simulation.UnitSize.Small, Vector3.zero, count))
                    throw new System.InvalidOperationException("Selection check could not queue 2000 own units.");
            }
            else if (count == 2000 && (!check.driver.Simulation.QueueBlock(Simulation.Side.First, Simulation.UnitSize.Small,
                    new Vector3(-40, 0, 0), 1000) || !check.driver.Simulation.QueueBlock(Simulation.Side.Second,
                    Simulation.UnitSize.Large, new Vector3(40, 0, 0), 1000)))
                throw new System.InvalidOperationException("Sandbox check could not queue two blocks of 1000.");
        }

        private static string OutputPrefix(string dataPath, int count)
        {
            string project = System.IO.Path.GetFullPath(System.IO.Path.Combine(dataPath, "../../.."));
            if (!System.IO.File.Exists(System.IO.Path.Combine(project, "ProjectSettings", "ProjectVersion.txt")))
                throw new System.InvalidOperationException("Sandbox checks must run from a build inside the Unity project.");
            return System.IO.Path.Combine(project, "Logs", "sandbox-" + count);
        }

        private void LateUpdate()
        {
            // The unattended desktop has no back buffer. The single-camera request reuses URP's camera list.
            if ((!Application.isBatchMode && !UnityEngine.Rendering.SplashScreen.isFinished) ||
                Time.realtimeSinceStartupAsDouble < 10.0) return;
            frames++;
            if (selectionCheck) CheckSelectionFrame();
            if (!enabled) return;
            if (frames > 120) { measuredFrames++; measuredSeconds += Time.unscaledDeltaTime; }
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(captureCamera, renderRequest);
            if (frames == (selectionCheck ? 63 : 60) || (selectionCheck && frames == 62))
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = captureTarget;
                var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                if (image.GetPixel(960, 540).grayscale < 0.2f)
                    throw new System.InvalidOperationException("Sandbox validation target was not rendered.");
                System.IO.File.WriteAllBytes(output + (selectionCheck && frames == 62 ? "-drag.png" : ".png"), image.EncodeToPNG());
                RenderTexture.active = previous;
                Destroy(image);
            }
            if (frames == 120)
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
                Debug.Log("Selection check: own_units=2000; samples=" + selectionSamples +
                    "; response_frames=1; selected_frame_average_ms=" + (selectionTotalMs / selectionSamples).ToString("F6", System.Globalization.CultureInfo.InvariantCulture) +
                    "; selected_frame_max_ms=" + selectionMaxMs.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) +
                    "; resolution=" + Screen.width + "x" + Screen.height + "; focused=" + Application.isFocused);
            Application.Quit(count == unitCount ? 0 : 1);
        }

        private void CheckSelectionFrame()
        {
            if (frames < 50) return;
            if (Time.frameCount == selectionResponseFrame)
            {
                if (driver.Simulation.SelectedCount != 2000 ||
                    driver.Simulation.DrawMatrices[Simulation.UnitSimulation.Capacity * 3 - 1].m33 != 1f)
                {
                    FailSelection("2000 selected units must have draw data on the next frame; selected=" +
                        driver.Simulation.SelectedCount + "; focused=" + Application.isFocused);
                    return;
                }
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
            if (phase == 3 || phase == 4 || phase == 8 || phase == 9) return;
            Vector2 viewport = phase == 0 ? new Vector2(0.4f, 0.4f) : phase <= 2 ? new Vector2(0.6f, 0.6f) :
                phase == 5 ? new Vector2(0.9f, 0.1f) : new Vector2(0.92f, 0.12f);
            InputSystem.QueueStateEvent(selectionMouse, new MouseState
            {
                position = captureCamera.ViewportToScreenPoint(viewport),
                buttons = phase == 2 || phase == 7 ? (ushort)0 : (ushort)1
            });
            if (phase == 2) selectionResponseFrame = Time.frameCount + 1;
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
    }
}
