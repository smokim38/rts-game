using System;
using System.IO;
using System.Security.Cryptography;
using Northwatch.Presentation;
using Northwatch.Presentation.Diagnostics;
using Northwatch.Simulation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Northwatch.EditorTools
{
    public static class PerformanceLadderBuilder
    {
        public const string ScenePath = "Assets/Scenes/PerformanceLadder.unity";
        public const string ScenarioPath = "Assets/Data/PerformanceLadder.asset";

        public static void Build()
        {
            const string templates = "ProjectSettings/SceneTemplateSettings.json";
            byte[] before = File.Exists(templates) ? File.ReadAllBytes(templates) : null;
            try { BuildAssets(); }
            finally
            {
                if (before != null) File.WriteAllBytes(templates, before);
                else if (File.Exists(templates)) File.Delete(templates);
            }
        }

        private static void BuildAssets()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
            LadderScenario scenario = AssetDatabase.LoadAssetAtPath<LadderScenario>(ScenarioPath);
            if (scenario == null)
            {
                scenario = ScriptableObject.CreateInstance<LadderScenario>();
                AssetDatabase.CreateAsset(scenario, ScenarioPath);
            }
            // Scenario values are versioned together. Rebuilding resets every owned parameter.
            var defaults = ScriptableObject.CreateInstance<LadderScenario>();
            EditorUtility.CopySerialized(defaults, scenario);
            UnityEngine.Object.DestroyImmediate(defaults);
            scenario.name = "PerformanceLadder";
            AssetDatabase.SaveAssetIfDirty(scenario);
            Scene scene = File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath) :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject match = Root(scene, "MatchDriver");
            if (match == null)
                match = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MatchDriver.prefab"), scene);
            MatchDriver driver = match.GetComponent<MatchDriver>();
            GameObject view = Root(scene, "LadderCamera") ?? new GameObject("LadderCamera");
            Camera camera = ComponentOrAdd<Camera>(view);
            camera.orthographic = true; camera.orthographicSize = scenario.CameraSize;
            camera.nearClipPlane = 0.3f; camera.farClipPlane = 1000;
            camera.aspect = 16f / 9f;
            Quaternion angle = Quaternion.Euler(scenario.CameraAngles);
            view.transform.SetPositionAndRotation(-(angle * Vector3.forward) * scenario.CameraDistance, angle);
            ComponentOrAdd<PerformanceLadder>(match).Configure(scenario, driver);
            GameObject ground = Root(scene, "Ground");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Ground";
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
            }
            ground.transform.position = new Vector3(0, -0.05f, 0);
            ground.transform.localScale = new Vector3(scenario.PlaneSize, 0.1f, scenario.PlaneSize);
            ground.GetComponent<MeshRenderer>().sharedMaterial = GraphicsSettings.defaultRenderPipeline.defaultMaterial;
            GameObject sun = Root(scene, "Sun") ?? new GameObject("Sun");
            Light light = ComponentOrAdd<Light>(sun);
            light.type = LightType.Directional; light.color = Color.white; light.intensity = scenario.LightIntensity;
            sun.transform.rotation = Quaternion.Euler(scenario.LightAngles);
            RenderSettings.sun = light; RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = scenario.Ambient;
            SaveFilter.Active = true;
            try
            {
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Ladder scene save failed.");
            }
            finally { SaveFilter.Active = false; }
            PlayerSettings.enableFrameTimingStats = true;
        }

        public static void BuildWindows()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-ladder-commit");
            if (index < 0 || index + 1 >= arguments.Length ||
                !System.Text.RegularExpressions.Regex.IsMatch(arguments[index + 1], "^[0-9a-f]{40}$"))
                throw new ArgumentException("Pass -ladder-commit with the full measured commit SHA.");
            Build();
            WriteSourceHashes();
            BuildPlayer("PerformanceLadder", BuildOptions.None);
            BuildPlayer("PerformanceLadderDevelopment", BuildOptions.Development);
            File.WriteAllText("Builds/PerformanceLadder/commit.txt", arguments[index + 1]);
        }

        private static void WriteSourceHashes()
        {
            Directory.CreateDirectory("Builds/PerformanceLadder");
            using var writer = new StreamWriter("Builds/PerformanceLadder/sources.txt");
            using SHA256 hash = SHA256.Create();
            foreach (string folder in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                string[] paths = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
                Array.Sort(paths, StringComparer.Ordinal);
                foreach (string path in paths)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    writer.WriteLine(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() +
                        " " + path.Replace('\\', '/'));
                }
            }
        }

        private static void BuildPlayer(string folder, BuildOptions options)
        {
            if (PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone) != ScriptingImplementation.Mono2x)
                throw new InvalidOperationException("The ladder requires Mono.");
            Directory.CreateDirectory("Builds/" + folder);
            BuildReport report = PlayerBuildHelper.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = "Builds/" + folder + "/PerformanceLadder.exe",
                target = BuildTarget.StandaloneWindows64, options = options
            });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 || report.summary.totalWarnings != 0)
                throw new InvalidOperationException("Ladder Player build failed or produced warnings.");
        }

        private static GameObject Root(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) return root;
            return null;
        }
        private static T ComponentOrAdd<T>(GameObject root) where T : Component =>
            root.TryGetComponent(out T component) ? component : root.AddComponent<T>();

        private sealed class SaveFilter : AssetModificationProcessor
        {
            internal static bool Active;
            private static string[] OnWillSaveAssets(string[] paths) =>
                Active ? Array.FindAll(paths, path => path == ScenePath) : paths;
        }
    }
}
