using UnityEngine;

namespace Northwatch.Presentation.Diagnostics
{
    public sealed class SandboxPlayerCheck : MonoBehaviour
    {
        private MatchDriver driver;
        private int frames, unitCount;
        private string output;
        private Camera captureCamera;
        private RenderTexture captureTarget;
        private UnityEngine.Rendering.RenderPipeline.StandardRequest renderRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartCheck()
        {
            string[] arguments = System.Environment.GetCommandLineArgs();
            int index = System.Array.IndexOf(arguments, "-sandbox-check");
            if (index < 0) return;
            if (index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out int count) || (count != 0 && count != 2000))
                throw new System.ArgumentException("Use -sandbox-check 0 or -sandbox-check 2000.");
            var check = new GameObject("SandboxPlayerCheck").AddComponent<SandboxPlayerCheck>();
            check.unitCount = count;
            check.driver = FindFirstObjectByType<MatchDriver>();
            if (check.driver == null || check.driver.Simulation.Count != 0)
                throw new System.InvalidOperationException("Sandbox must start with an empty match.");
            check.output = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../Logs/sandbox-" + count));
            check.captureCamera = Camera.main;
            check.captureTarget = new RenderTexture(1920, 1080, 24);
            if (!check.captureTarget.Create()) throw new System.InvalidOperationException("Could not create validation render target.");
            check.captureCamera.aspect = 16f / 9f;
            check.renderRequest = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = check.captureTarget };
            for (int i = 0; i < count; i++)
            {
                var side = i < count / 2 ? Simulation.Side.First : Simulation.Side.Second;
                var size = i % 2 == 0 ? Simulation.UnitSize.Small : Simulation.UnitSize.Large;
                if (!check.driver.Simulation.QueueAdd(side, size, new Vector3((i % 50 - 25) * 2, 0, (i / 50 - 20) * 2)))
                    throw new System.InvalidOperationException("Sandbox check could not queue all units.");
            }
        }

        private void LateUpdate()
        {
            // The unattended desktop can have no window back buffer. Render through Unity's SRP request API.
            if (!UnityEngine.Rendering.SplashScreen.isFinished || Time.realtimeSinceStartupAsDouble < 10.0) return;
            frames++;
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(captureCamera, renderRequest);
            if (frames == 60)
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = captureTarget;
                var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(output + ".png", image.EncodeToPNG());
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
            if (frames != 420) return;
#if DEVELOPMENT_BUILD
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.enableBinaryLog = false;
#endif
            int count = driver.Simulation.Count;
            Debug.Log("Sandbox check: scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                "; units=" + count + "; ticks=" + driver.Simulation.TickCount + "; rendered frames=" + frames);
            Application.Quit(count == unitCount ? 0 : 1);
        }

        private void OnDestroy()
        {
            if (captureTarget == null) return;
            captureTarget.Release();
            Destroy(captureTarget);
        }
    }
}
