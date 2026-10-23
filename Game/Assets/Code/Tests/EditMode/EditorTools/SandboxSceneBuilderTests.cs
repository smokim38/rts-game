using System.IO;
using System.Linq;
using Northwatch.EditorTools;
using Northwatch.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Northwatch.Tests.EditMode.EditorTools
{
    public sealed class SandboxSceneBuilderTests
    {
        [Test]
        public void BuilderPreservesAssetBytesOnRepeatRun()
        {
            string[] paths = Directory.GetFiles("Assets/Scenes").Concat(Directory.GetFiles("Assets/Prefabs"))
                .Concat(Directory.GetFiles("Assets/Materials")).Concat(Directory.GetFiles("Assets/Settings"))
                .Concat(Directory.GetFiles("ProjectSettings")).ToArray();
            byte[][] before = paths.Select(File.ReadAllBytes).ToArray();
            SandboxSceneBuilder.Build();
            for (int i = 0; i < paths.Length; i++) Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(before[i]), paths[i]);
        }

        [Test]
        public void SceneHasEmptyMatchAndWholeMapInView()
        {
            var scene = EditorSceneManager.OpenScene(SandboxSceneBuilder.ScenePath);
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.That(roots.Length, Is.EqualTo(4));
            Camera camera = roots.Select(root => root.GetComponent<Camera>()).Single(value => value != null);
            MatchDriver driver = roots.Select(root => root.GetComponent<MatchDriver>()).Single(value => value != null);
            Assert.That(driver.Simulation, Is.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(driver), Is.True);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(camera), Is.True);
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
