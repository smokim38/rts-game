using System;
using System.IO;
using System.Text;
using Northwatch.Presentation;
using Northwatch.Presentation.Cameras;
using Northwatch.Simulation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Northwatch.EditorTools
{
    public static class SandboxSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Sandbox.unity";
        private const string DriverPath = "Assets/Prefabs/MatchDriver.prefab";
        private const string CameraPath = "Assets/Prefabs/CameraRig.prefab";
        private const string OverlayPath = "Assets/Prefabs/DebugOverlay.prefab";
        private const string InputPath = "Assets/Settings/InputSystem_Actions.inputactions";
        public const string MovementSettingsPath = "Assets/Data/MovementSettings.asset";

        public static void BuildMovement()
        {
            EnsureFolder("Data");
            MovementSettings settings = AssetDatabase.LoadAssetAtPath<MovementSettings>(MovementSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<MovementSettings>();
                AssetDatabase.CreateAsset(settings, MovementSettingsPath);
            }
            settings.name = "MovementSettings";
            settings.Snapshot();
            AssetDatabase.SaveAssetIfDirty(settings);
            GameObject driver = PrefabRoot(DriverPath, "MatchDriver");
            ComponentOrAdd<MatchDriver>(driver).ConfigureMovement(settings);
            SavePrefab(driver, DriverPath);
        }

        [MenuItem("Northwatch/Build sandbox assets")]
        public static void Build()
        {
            string[] paths = { ScenePath, DriverPath, CameraPath, "Assets/Materials/FirstSide.mat",
                "Assets/Materials/SecondSide.mat", "Assets/Materials/SelectedSide.mat",
                "ProjectSettings/EditorBuildSettings.asset", OverlayPath, InputPath };
            var before = new byte[paths.Length][];
            for (int i = 0; i < paths.Length; i++)
                if (File.Exists(paths[i])) before[i] = File.ReadAllBytes(paths[i]);
            const string templateSettings = "ProjectSettings/SceneTemplateSettings.json";
            byte[] templateSettingsBefore = File.Exists(templateSettings) ? File.ReadAllBytes(templateSettings) : null;
            try
            {
                BuildAssets();
            }
            finally
            {
                // Unity serializes with LF even when Git checked out identical assets with CRLF.
                // Restore the checkout bytes only when no serialized property has changed.
                bool restoredAssets = false;
                for (int i = 0; i < paths.Length; i++)
                {
                    if (before[i] == null || !File.Exists(paths[i])) continue;
                    string original = Encoding.UTF8.GetString(before[i]);
                    string saved = Encoding.UTF8.GetString(File.ReadAllBytes(paths[i]));
                    if (original != saved && original.Replace("\r\n", "\n") == saved.Replace("\r\n", "\n"))
                    {
                        File.WriteAllBytes(paths[i], before[i]);
                        restoredAssets |= paths[i].StartsWith("Assets/", StringComparison.Ordinal);
                    }
                }
                // Loading prefab contents initializes unrelated scene-template defaults on a fresh checkout.
                // Leave these settings as they were, including preserving a pre-existing file.
                if (templateSettingsBefore != null) File.WriteAllBytes(templateSettings, templateSettingsBefore);
                else if (File.Exists(templateSettings)) File.Delete(templateSettings);
                // Raw restores change source timestamps after Unity has imported the saved assets.
                if (restoredAssets) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static void BuildAssets()
        {
            BuildMovement();
            EnsureFolder("Prefabs");
            EnsureFolder("Materials");
            Material first = SideMaterial("FirstSide", new Color(0.1f, 0.35f, 0.95f));
            Material second = SideMaterial("SecondSide", new Color(0.95f, 0.2f, 0.1f));
            Material selected = SideMaterial("SelectedSide", new Color(1f, 0.85f, 0.05f));
            GameObject driver = PrefabRoot(DriverPath, "MatchDriver");
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh cube = primitive.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(primitive);
            ComponentOrAdd<MatchDriver>(driver).Configure(cube, first, second);
            driver.GetComponent<MatchDriver>().ConfigureSelection(selected);
            SavePrefab(driver, DriverPath);

            BuildCamera();

            BuildOverlay(BuildSpawnActions());

            Scene scene = File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath) :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureInstance(scene, "MatchDriver", DriverPath);
            EnsureInstance(scene, "CameraRig", CameraPath);
            EnsureInstance(scene, "DebugOverlay", OverlayPath);
            Root(scene, "DebugOverlay").GetComponent<DebugOverlay>().Bind(
                Root(scene, "MatchDriver").GetComponent<MatchDriver>(), Root(scene, "CameraRig").GetComponent<Camera>());
            Root(scene, "DebugOverlay").GetComponent<SelectionController>().Bind(
                Root(scene, "MatchDriver").GetComponent<MatchDriver>(), Root(scene, "CameraRig").GetComponent<Camera>(),
                Root(scene, "CameraRig").GetComponent<CameraController>());
            PrefabUtility.RecordPrefabInstancePropertyModifications(Root(scene, "DebugOverlay").GetComponent<SelectionController>());
            Canvas overlayCanvas = Root(scene, "DebugOverlay").GetComponent<Canvas>();
            overlayCanvas.worldCamera = Root(scene, "CameraRig").GetComponent<Camera>();
            PrefabUtility.RecordPrefabInstancePropertyModifications(overlayCanvas);
            GameObject ground = Root(scene, "Ground");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Ground";
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
            }
            ground.transform.position = new Vector3(0f, -0.05f, 0f);
            ground.transform.localScale = new Vector3(UnitSimulation.MapHalfWidth * 2, 0.1f, UnitSimulation.MapHalfWidth * 2);
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
            // Saving a scene also flushes unrelated dirty assets, including pending import upgrades.
            // Limit that implicit save; the two owned materials are saved explicitly below.
            SceneSaveFilter.Active = true;
            try
            {
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Sandbox scene could not be saved.");
            }
            finally
            {
                SceneSaveFilter.Active = false;
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (File.Exists("Assets/Scenes/SampleScene.unity") && !AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity"))
                throw new InvalidOperationException("SampleScene could not be removed.");
            AssetDatabase.SaveAssetIfDirty(first);
            AssetDatabase.SaveAssetIfDirty(second);
            AssetDatabase.SaveAssetIfDirty(selected);
        }

        public static void BuildCamera()
        {
            byte[] before = File.Exists(CameraPath) ? File.ReadAllBytes(CameraPath) : null;
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            InputActionMap map = actions.FindActionMap("GameplayCamera") ?? actions.AddActionMap("GameplayCamera");
            if (map.FindAction("Pan") == null)
                map.AddAction("Pan", InputActionType.Value, expectedControlLayout: "Vector2")
                    .AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/upArrow", "Keyboard&Mouse").With("Down", "<Keyboard>/downArrow", "Keyboard&Mouse")
                    .With("Left", "<Keyboard>/leftArrow", "Keyboard&Mouse").With("Right", "<Keyboard>/rightArrow", "Keyboard&Mouse");
            SpawnAction(map, "Point", "<Mouse>/position", InputActionType.Value);
            InputAction zoom = map.FindAction("Zoom") ?? map.AddAction("Zoom", InputActionType.Value, expectedControlLayout: "Axis");
            bool hasScroll = false;
            foreach (InputBinding binding in zoom.bindings) hasScroll |= binding.path == "<Mouse>/scroll/y";
            if (!hasScroll) zoom.AddBinding("<Mouse>/scroll/y", groups: "Keyboard&Mouse");
            File.WriteAllText(InputPath, actions.ToJson());
            AssetDatabase.ImportAsset(InputPath);
            actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);

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
            // Input System normalizes scroll to one unit per wheel notch.
            ComponentOrAdd<CameraController>(rig).Configure(camera, actions, 0.5f, 16f, 20f, 200f, 0.18f);
            SavePrefab(rig, CameraPath);
            if (before == null) return;
            string original = Encoding.UTF8.GetString(before);
            string saved = File.ReadAllText(CameraPath);
            if (original != saved && original.Replace("\r\n", "\n") == saved.Replace("\r\n", "\n"))
            {
                File.WriteAllBytes(CameraPath, before);
                AssetDatabase.ImportAsset(CameraPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static InputActionAsset BuildSpawnActions()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            InputActionMap map = asset.FindActionMap("Sandbox") ?? asset.AddActionMap("Sandbox");
            SpawnAction(map, "ToggleSpawn", "<Keyboard>/f1");
            SpawnAction(map, "SideA", "<Keyboard>/a");
            SpawnAction(map, "SideB", "<Keyboard>/b");
            SpawnAction(map, "Small", "<Keyboard>/s");
            SpawnAction(map, "Large", "<Keyboard>/l");
            SpawnAction(map, "Place", "<Mouse>/leftButton");
            SpawnAction(map, "Point", "<Mouse>/position", InputActionType.Value);
            SpawnAction(map, "Backspace", "<Keyboard>/backspace");
            for (int i = 0; i < 10; i++)
            {
                SpawnAction(map, "Digit" + i, "<Keyboard>/" + i);
                SpawnAction(map, "Digit" + i, "<Keyboard>/numpad" + i);
            }
            InputActionMap selection = asset.FindActionMap("Selection") ?? asset.AddActionMap("Selection");
            SpawnAction(selection, "Point", "<Mouse>/position", InputActionType.Value);
            SpawnAction(selection, "Select", "<Mouse>/leftButton");
            SpawnAction(selection, "Shift", "<Keyboard>/leftShift");
            SpawnAction(selection, "Shift", "<Keyboard>/rightShift");
            SpawnAction(selection, "Control", "<Keyboard>/leftCtrl");
            SpawnAction(selection, "Control", "<Keyboard>/rightCtrl");
            for (int i = 1; i <= 9; i++)
            {
                SpawnAction(selection, "Group" + i, "<Keyboard>/" + i);
                SpawnAction(selection, "Group" + i, "<Keyboard>/numpad" + i);
            }
            File.WriteAllText(InputPath, asset.ToJson());
            AssetDatabase.ImportAsset(InputPath);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
        }

        private static void SpawnAction(InputActionMap map, string name, string binding, InputActionType type = InputActionType.Button)
        {
            InputAction action = map.FindAction(name) ?? map.AddAction(name, type, expectedControlLayout: type == InputActionType.Value ? "Vector2" : "Button");
            foreach (InputBinding existing in action.bindings) if (existing.path == binding) return;
            action.AddBinding(binding, groups: "Keyboard&Mouse");
        }

        private static void BuildOverlay(InputActionAsset actions)
        {
            GameObject root = File.Exists(OverlayPath) ? PrefabUtility.LoadPrefabContents(OverlayPath) :
                new GameObject("DebugOverlay", typeof(RectTransform));
            Canvas canvas = ComponentOrAdd<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.planeDistance = 1f;
            CanvasScaler scaler = ComponentOrAdd<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            GameObject panel = Child(root, "Panel");
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(12, -12);
            rect.sizeDelta = new Vector2(700, 250);
            ComponentOrAdd<Image>(panel).color = new Color(0.02f, 0.03f, 0.04f, 0.94f);
            var fields = new Text[11];
            string[] labels = { "Units A:", "B:", "Total:", "Average fps:", "Frame ms:", "F1 spawn:", "Side:", "Size:", "Count:", "", "Still fit:" };
            int[] x = { 100, 290, 520, 160, 520, 160, 100, 290, 520, 12, 520 };
            int[] y = { 8, 8, 8, 38, 38, 68, 98, 98, 98, 218, 188 };
            for (int i = 0; i < fields.Length; i++)
            {
                Label(panel, "Label" + i, labels[i], x[i] - (i == 3 || i == 5 ? 148 : 88), y[i], 148);
                fields[i] = Label(panel, "Value" + i, i == 9 ? "" : "0000", x[i], y[i], i == 9 ? 676 : 150);
            }
            Label(panel, "Help", "A/B side  |  S/L size  |  Type count 1-2000, Backspace\nClick clear ground to place; typing replaces the last count.", 12, 128, 676, 56);
            DebugOverlay debugOverlay = ComponentOrAdd<DebugOverlay>(root);
            debugOverlay.Configure(actions, canvas, rect, fields);
            GameObject selectionBox = Child(root, "SelectionBox");
            RectTransform selectionRect = (RectTransform)selectionBox.transform;
            selectionRect.anchorMin = selectionRect.anchorMax = selectionRect.pivot = Vector2.zero;
            ComponentOrAdd<Image>(selectionBox).color = new Color(1f, 0.85f, 0.05f, 0.18f);
            if (selectionBox.TryGetComponent(out Outline outline)) UnityEngine.Object.DestroyImmediate(outline);
            BoxEdge(selectionBox, "Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(1, 0));
            BoxEdge(selectionBox, "Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(1, 0));
            BoxEdge(selectionBox, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 1));
            BoxEdge(selectionBox, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 1));
            selectionBox.GetComponent<Image>().raycastTarget = false;
            ComponentOrAdd<SelectionController>(root).Configure(actions, debugOverlay, selectionRect);
            selectionBox.SetActive(false);
            Label(panel, "SelectionHelp", SelectionController.HelpText(false), 12, 250, 676, 56);
            rect.sizeDelta = new Vector2(700, 310);
            SavePrefab(root, OverlayPath);
        }

        private static void BoxEdge(GameObject box, string name, Vector2 minimum, Vector2 maximum, Vector2 pivot, Vector2 size)
        {
            GameObject edge = Child(box, name);
            var rect = (RectTransform)edge.transform;
            rect.anchorMin = minimum; rect.anchorMax = maximum; rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
            Image image = ComponentOrAdd<Image>(edge);
            image.color = new Color(1f, 0.85f, 0.05f, 1f);
            image.raycastTarget = false;
        }

        private static GameObject Child(GameObject parent, string name)
        {
            Transform existing = parent.transform.Find(name);
            if (existing != null) return existing.gameObject;
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static Text Label(GameObject parent, string name, string content, int x, int y, int width, int height = 28)
        {
            Text text = ComponentOrAdd<Text>(Child(parent, name));
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18; text.color = Color.white; text.supportRichText = false;
            text.raycastTarget = false; text.text = content;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            return text;
        }
        private sealed class SceneSaveFilter : AssetModificationProcessor
        {
            internal static bool Active;

            private static string[] OnWillSaveAssets(string[] paths) =>
                Active ? Array.FindAll(paths, path => path == ScenePath) : paths;
        }

        public static void BuildWindows()
        {
            BuildPlayer("Sandbox", BuildOptions.None);
        }

        public static void BuildWindowsDevelopment()
        {
            BuildPlayer("SandboxDevelopment", BuildOptions.Development);
        }

        public static void BuildSelectionPlayers()
        {
            BuildWindowsDevelopment();
            BuildWindows();
        }

        private static void BuildPlayer(string folder, BuildOptions options)
        {
            Directory.CreateDirectory("Builds/" + folder);
            BuildReport report = PlayerBuildHelper.BuildPlayer(new BuildPlayerOptions
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
