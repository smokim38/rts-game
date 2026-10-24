using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Northwatch.EditorTools;
using Northwatch.Presentation;
using Northwatch.Presentation.Diagnostics;
using Northwatch.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Northwatch.Tests.EditMode.EditorTools
{
    public sealed class SandboxSceneBuilderTests
    {
        [Test]
        public void PlayerCheckOutputStaysInProjectAndRejectsRelocatedBuild()
        {
            MethodInfo outputPrefix = typeof(SandboxPlayerCheck).GetMethod("OutputPrefix", BindingFlags.NonPublic | BindingFlags.Static);
            string dataPath = Path.GetFullPath("Builds/Sandbox/Sandbox_Data");
            Assert.That(outputPrefix.Invoke(null, new object[] { dataPath, 0 }),
                Is.EqualTo(Path.GetFullPath("Logs/sandbox-0")));
            Assert.That(outputPrefix.Invoke(null, new object[] { dataPath, 2000 }),
                Is.EqualTo(Path.GetFullPath("Logs/sandbox-2000")));
            // An extra export directory no longer places the project marker beside Logs.
            string relocated = Path.GetFullPath("Builds/Export/Sandbox/Sandbox_Data");
            var error = Assert.Throws<TargetInvocationException>(() => outputPrefix.Invoke(null, new object[] { relocated, 0 }));
            Assert.That(error.InnerException, Is.TypeOf<System.InvalidOperationException>());
        }

        [TestCase("\n", false, TestName = "BuilderPreservesLfCheckoutOnRepeatRun")]
        [TestCase("\r\n", false, TestName = "BuilderPreservesCrLfCheckoutOnRepeatRun")]
        [TestCase("\n", true, TestName = "BuilderPreservesExistingSceneTemplateSettings")]
        public void BuilderPreservesAssetBytesOnRepeatRun(string lineEnding, bool existingSettings)
        {
            AssertPreservesAssetBytesOnRepeatRun(lineEnding, existingSettings, SandboxSceneBuilder.Build);
        }

        [Test]
        public void BuilderDoesNotSavePendingChangesToUnrelatedAsset()
        {
            WithUnrelatedMaterial((material, path) =>
            {
                byte[] before = File.ReadAllBytes(path);
                material.name = "PendingImportChange";
                EditorUtility.SetDirty(material);
                SandboxSceneBuilder.Build();
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), path);
                Assert.That(EditorUtility.IsDirty(material), Is.True, "The pending change must remain unsaved.");
                AssetDatabase.SaveAssets();
                Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(path)), Does.Contain("m_Name: PendingImportChange"),
                    "Normal saves must resume after the builder finishes.");
            });
        }

        [Test]
        public void RepeatRunBaselineIncludesPendingImportChanges()
        {
            WithUnrelatedMaterial((material, path) =>
            {
                // Model an importer that dirties an asset before its deferred save reaches disk.
                material.name = "PendingImportChange";
                EditorUtility.SetDirty(material);
                AssertPreservesAssetBytesOnRepeatRun("\n", false, () =>
                {
                    Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(path)), Does.Contain("m_Name: PendingImportChange"),
                        "Import changes must be saved before the builder and its byte snapshot.");
                    SandboxSceneBuilder.Build();
                });
            });
        }

        [Test]
        public void RepeatRunCheckRejectsUnsavedBuilderChangesToUnrelatedAsset()
        {
            WithUnrelatedMaterial((material, path) =>
            {
                var error = Assert.Throws<AssertionException>(() =>
                    AssertPreservesAssetBytesOnRepeatRun("\n", false, () =>
                    {
                        SandboxSceneBuilder.Build();
                        material.name = "PendingBuilderChange";
                        EditorUtility.SetDirty(material);
                    }));
                Assert.That(error.Message, Does.Contain(path));
            });
        }

        [TestCase("Assets")]
        [TestCase("ProjectSettings/SceneTemplateSettings.json")]
        public void RepeatRunCheckRejectsBuilderWritesToUnrelatedFiles(string path)
        {
            WithUnrelatedMaterial((material, assetPath) =>
            {
                string changedPath = path == "Assets" ? assetPath : path;
                var error = Assert.Throws<AssertionException>(() =>
                    AssertPreservesAssetBytesOnRepeatRun("\n", true, () =>
                    {
                        SandboxSceneBuilder.Build();
                        File.AppendAllText(changedPath, "\n");
                    }));
                Assert.That(error.Message, Does.Contain(changedPath));
            });
        }

        private static void AssertPreservesAssetBytesOnRepeatRun(string lineEnding, bool existingSettings, TestDelegate build)
        {
            // First-import upgrades can leave unrelated assets dirty. Flush Unity's work before
            // taking either snapshot so every subsequent change is still attributed to the builder.
            AssetDatabase.SaveAssets();
            string[] paths = AssetAndSettingsPaths();
            byte[][] before = paths.Select(File.ReadAllBytes).ToArray();
            const string templateSettings = "ProjectSettings/SceneTemplateSettings.json";
            try
            {
                if (existingSettings) File.WriteAllText(templateSettings, "{}");
                else if (File.Exists(templateSettings)) File.Delete(templateSettings);
                // Model both checkout conventions without changing any serialized properties.
                for (int i = 0; i < paths.Length; i++)
                    if (paths[i].EndsWith(".unity") || paths[i].EndsWith(".prefab") || paths[i].EndsWith(".mat") ||
                        paths[i] == "ProjectSettings/EditorBuildSettings.asset")
                        File.WriteAllBytes(paths[i], Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(before[i])
                            .Replace("\r\n", "\n").Replace("\n", lineEnding)));
                string[] expectedPaths = AssetAndSettingsPaths();
                byte[][] expected = expectedPaths.Select(File.ReadAllBytes).ToArray();
                for (int run = 0; run < 2; run++)
                {
                    build();
                    // Include changes left dirty by this build before another baseline can absorb them.
                    AssetDatabase.SaveAssets();
                    Assert.That(AssetAndSettingsPaths(), Is.EqualTo(expectedPaths), "The builder must not add assets or settings.");
                    for (int i = 0; i < expectedPaths.Length; i++)
                        Assert.That(File.ReadAllBytes(expectedPaths[i]), Is.EqualTo(expected[i]), expectedPaths[i]);
                }
            }
            finally
            {
                for (int i = 0; i < paths.Length; i++)
                    if (!File.Exists(paths[i]) || !File.ReadAllBytes(paths[i]).SequenceEqual(before[i]))
                        File.WriteAllBytes(paths[i], before[i]);
                if (!paths.Contains(templateSettings) && File.Exists(templateSettings)) File.Delete(templateSettings);
            }
        }

        private static void WithUnrelatedMaterial(System.Action<Material, string> check)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Code/Tests/EditMode/EditorTools/UnrelatedBuilderTest.mat");
            var material = new Material(Shader.Find("Unlit/Color"));
            try
            {
                AssetDatabase.CreateAsset(material, path);
                AssetDatabase.SaveAssetIfDirty(material);
                check(material, path);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static string[] AssetAndSettingsPaths() => Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("ProjectSettings", "*", SearchOption.AllDirectories)).Select(path => path.Replace('\\', '/'))
            .OrderBy(path => path).ToArray();

        [Test]
        public void SceneHasEmptyMatchAndWholeMapInView()
        {
            var scene = EditorSceneManager.OpenScene(SandboxSceneBuilder.ScenePath);
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.That(roots.Length, Is.EqualTo(5));
            Camera camera = roots.Select(root => root.GetComponent<Camera>()).Single(value => value != null);
            MatchDriver driver = roots.Select(root => root.GetComponent<MatchDriver>()).Single(value => value != null);
            Assert.That(driver.Simulation, Is.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(driver), Is.True);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(camera), Is.True);
            DebugOverlay overlay = roots.Select(root => root.GetComponent<DebugOverlay>()).Single(value => value != null);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(overlay), Is.True);
            var overlayData = new SerializedObject(overlay);
            Assert.That(overlayData.FindProperty("driver").objectReferenceValue, Is.EqualTo(driver));
            Assert.That(overlayData.FindProperty("view").objectReferenceValue, Is.EqualTo(camera));
            Assert.That(overlay.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(overlay.GetComponentsInChildren<Text>().Length, Is.GreaterThan(11));
            var input = (InputActionAsset)overlayData.FindProperty("actions").objectReferenceValue;
            Assert.That(input.FindAction("Sandbox/ToggleSpawn", true).bindings[0].path, Is.EqualTo("<Keyboard>/f1"));
            Assert.That(input.FindAction("Sandbox/Place", true).bindings[0].path, Is.EqualTo("<Mouse>/leftButton"));
            var serialized = new SerializedObject(driver);
            Assert.That(serialized.FindProperty("cube").objectReferenceValue, Is.Not.Null);
            foreach (string property in new[] { "firstSide", "secondSide" })
            {
                var material = (Material)serialized.FindProperty(property).objectReferenceValue;
                Assert.That(material.enableInstancing, Is.True);
                Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            }
            GameObject ground = roots.Single(root => root.name == "Ground");
            Assert.That(ground.transform.localScale, Is.EqualTo(new Vector3(256f, 0.1f, 256f)));
            Assert.That(ground.transform.localScale.x, Is.EqualTo(UnitSimulation.MapHalfWidth * 2));
            Assert.That(ground.transform.localScale.z, Is.EqualTo(UnitSimulation.MapHalfWidth * 2));
            Assert.That(roots.SelectMany(root => root.GetComponentsInChildren<Light>()).Count(), Is.EqualTo(1));
            camera.aspect = 16f / 9f;
            foreach (float x in new[] { -128f, 128f })
                foreach (float z in new[] { -128f, 128f })
                {
                    Vector3 viewport = camera.WorldToViewportPoint(new Vector3(x, 0, z));
                    Assert.That(viewport.x, Is.InRange(0f, 1f));
                    Assert.That(viewport.y, Is.InRange(0f, 1f));
                    Assert.That(viewport.z, Is.InRange(camera.nearClipPlane, camera.farClipPlane));
                }
            Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(SandboxSceneBuilder.ScenePath));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(File.Exists("Assets/Scenes/SampleScene.unity"), Is.False);
            // Do not save the temporary aspect override from this validation.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
