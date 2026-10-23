using System;
using System.IO;
using Northwatch.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Northwatch.EditorTools
{
    public static class SandboxSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Sandbox.unity";
        private const string DriverPath = "Assets/Prefabs/MatchDriver.prefab";
        private const string CameraPath = "Assets/Prefabs/CameraRig.prefab";

        [MenuItem("Northwatch/Build sandbox assets")]
        public static void Build()
        {
            EnsureFolder("Prefabs");
            EnsureFolder("Materials");
            Material first = SideMaterial("FirstSide", new Color(0.1f, 0.35f, 0.95f));
            Material second = SideMaterial("SecondSide", new Color(0.95f, 0.2f, 0.1f));
            GameObject driver = PrefabRoot(DriverPath, "MatchDriver");
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh cube = primitive.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(primitive);
            ComponentOrAdd<MatchDriver>(driver).Configure(cube, first, second);
            SavePrefab(driver, DriverPath);

            GameObject rig = PrefabRoot(CameraPath, "CameraRig");
            Camera camera = ComponentOrAdd<Camera>(rig);
            rig.tag = "MainCamera";
            rig.transform.rotation = Quaternion.Euler(35.26439f, 45f, 0f);
            rig.transform.position = -rig.transform.forward * 400f;
            camera.orthographic = true;
            camera.orthographicSize = 200f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
            SavePrefab(rig, CameraPath);

            Scene scene = File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath) :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureInstance(scene, "MatchDriver", DriverPath);
            EnsureInstance(scene, "CameraRig", CameraPath);
            GameObject ground = Root(scene, "Ground");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Ground";
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
            }
            ground.transform.position = new Vector3(0f, -0.05f, 0f);
            ground.transform.localScale = new Vector3(256f, 0.1f, 256f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = GraphicsSettings.defaultRenderPipeline.defaultMaterial;
            GameObject sun = Root(scene, "Sun") ?? new GameObject("Sun");
            Light light = ComponentOrAdd<Light>(sun);
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = Color.white;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Sandbox scene could not be saved.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (File.Exists("Assets/Scenes/SampleScene.unity") && !AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity"))
                throw new InvalidOperationException("SampleScene could not be removed.");
            AssetDatabase.SaveAssetIfDirty(first);
            AssetDatabase.SaveAssetIfDirty(second);
        }

        public static void BuildWindows()
        {
            BuildPlayer("Sandbox", BuildOptions.None);
        }

        public static void BuildWindowsDevelopment()
        {
            BuildPlayer("SandboxDevelopment", BuildOptions.Development);
        }

        private static void BuildPlayer(string folder, BuildOptions options)
        {
            Directory.CreateDirectory("Builds/" + folder);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = "Builds/" + folder + "/Sandbox.exe",
                target = BuildTarget.StandaloneWindows64, options = options
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Sandbox Player build failed: " + report.summary.result);
        }

        private static void EnsureFolder(string name)
        {
            if (!AssetDatabase.IsValidFolder("Assets/" + name)) AssetDatabase.CreateFolder("Assets", name);
        }

        private static Material SideMaterial(string name, Color colour)
        {
            string path = "Assets/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0f);
            material.enableInstancing = true;
            // Apply URP's derived keywords and legacy properties before saving the first output.
            var editor = (MaterialEditor)Editor.CreateEditor(material);
            editor.customShaderGUI?.ValidateMaterial(material);
            UnityEngine.Object.DestroyImmediate(editor);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject PrefabRoot(string path, string name) =>
            File.Exists(path) ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name);

        private static void SavePrefab(GameObject root, string path)
        {
            bool loaded = File.Exists(path);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success) throw new InvalidOperationException("Could not save " + path);
            if (loaded) PrefabUtility.UnloadPrefabContents(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }

        private static T ComponentOrAdd<T>(GameObject root) where T : Component =>
            root.TryGetComponent(out T component) ? component : root.AddComponent<T>();

        private static GameObject Root(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) return root;
            return null;
        }

        private static void EnsureInstance(Scene scene, string name, string path)
        {
            if (Root(scene, name) == null) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
        }
    }
}
