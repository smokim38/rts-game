#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using Northwatch.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Northwatch.Presentation.Diagnostics
{
    [DefaultExecutionOrder(3000)]
    public sealed class TankPreview : MonoBehaviour
    {
        private MatchDriver driver;
        private Camera view;
        private string output;
        private bool selected, captured;
        private int count, selectedCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartPreview()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-tank-preview") < 0) return;
            var preview = new GameObject("TankPreview").AddComponent<TankPreview>();
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            if (!File.Exists(Path.Combine(project, "ProjectSettings/ProjectVersion.txt")))
                throw new InvalidOperationException("Tank preview requires a Player built inside Game/Builds.");
            preview.output = Path.Combine(project, "Logs/issue-224-tanks.png");
            preview.driver = FindFirstObjectByType<MatchDriver>();
            preview.view = Camera.main;
            var camera = preview.view.GetComponent<Cameras.CameraController>();
            camera.CentreOn(Vector3.zero); camera.enabled = false;
            preview.view.orthographicSize = 20;
            preview.view.aspect = 16f / 9f;
            if (!preview.driver.Simulation.QueueBlock(Side.First, UnitSize.Large, new Vector3(-10, 0, 0), 100) ||
                !preview.driver.Simulation.QueueBlock(Side.Second, UnitSize.Large, new Vector3(10, 0, 0), 100))
                throw new InvalidOperationException("Tank preview could not queue both sides.");
        }

        private void Update()
        {
            count = driver.Simulation.Count;
            selectedCount = driver.Simulation.SelectedCount;
            if (selected || count != 200) return;
            selected = true;
            driver.Simulation.QueueSelection(SelectionRequest.Box(new Matrix4x4 { m00 = .05f, m12 = .05f, m33 = 1 },
                Rect.MinMaxRect(.1f, .35f, .2f, .45f)));
        }

        private void LateUpdate()
        {
            if (captured || Time.realtimeSinceStartupAsDouble < 10) return;
            captured = true;
            if (count != 200 || selectedCount == 0) throw new InvalidOperationException("Tank preview population or selection failed.");
            var target = new RenderTexture(1920, 1080, 24);
            if (!target.Create()) throw new InvalidOperationException("Tank preview render target failed.");
            // MatchDriver queues instanced draws at order 2000; capture after those submissions.
            RenderPipeline.SubmitRenderRequest(view, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            int blue = 0, red = 0, yellow = 0;
            foreach (Color32 pixel in image.GetPixels32())
            {
                if (pixel.b > 100 && pixel.r < 100) blue++;
                if (pixel.r > 100 && pixel.g < 100 && pixel.b < 100) red++;
                if (pixel.r > 100 && pixel.g > 100 && pixel.b < 70) yellow++;
            }
            if (blue < 100 || red < 100 || yellow < 100)
                throw new InvalidOperationException("Tank preview did not render all side and selection colours.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, image.EncodeToPNG());
            RenderTexture.active = previous;
            Destroy(image); target.Release(); Destroy(target);
            Debug.Log("Tank preview: units=" + count + "; selected=" + selectedCount + "; blue/red/yellow_pixels=" + blue + "/" + red + "/" + yellow);
            Application.Quit(0);
        }
    }
}
#endif
