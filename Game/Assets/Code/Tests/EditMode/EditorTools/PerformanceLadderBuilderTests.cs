using System.IO;
using System.Linq;
using Northwatch.EditorTools;
using Northwatch.Presentation;
using Northwatch.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Northwatch.Tests.EditMode.EditorTools
{
    public sealed class PerformanceLadderBuilderTests
    {
        [Test]
        public void SceneUsesSandboxUnitsAndFramesWholePlaneWithoutUi()
        {
            var scene = EditorSceneManager.OpenScene(PerformanceLadderBuilder.ScenePath);
            GameObject[] roots = scene.GetRootGameObjects();
            Assert.That(roots.Length, Is.EqualTo(4));
            MatchDriver match = roots.Select(root => root.GetComponent<MatchDriver>()).Single(value => value != null);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(match), Is.True);
            Assert.That(match.GetComponent<PerformanceLadder>(), Is.Not.Null);
            Assert.That(roots.SelectMany(root => root.GetComponentsInChildren<Canvas>()).Any(), Is.False);
            Camera camera = roots.Select(root => root.GetComponent<Camera>()).Single(value => value != null);
            var scenario = AssetDatabase.LoadAssetAtPath<LadderScenario>(PerformanceLadderBuilder.ScenarioPath);
            Assert.That(scenario.Version, Is.EqualTo(1));
            Assert.That(scenario.WarmupSeconds, Is.EqualTo(10));
            Assert.That(scenario.WindowSeconds, Is.EqualTo(60));
            camera.aspect = 16f / 9f;
            foreach (float x in new[] { -128f, 128f })
                foreach (float z in new[] { -128f, 128f })
                {
                    Vector3 viewport = camera.WorldToViewportPoint(new Vector3(x, 1, z));
                    Assert.That(viewport.x, Is.InRange(0f, 1f));
                    Assert.That(viewport.y, Is.InRange(0f, 1f));
                    Assert.That(viewport.z, Is.InRange(camera.nearClipPlane, camera.farClipPlane));
                }
            Assert.That(PlayerSettings.enableFrameTimingStats, Is.True);
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(SandboxSceneBuilder.ScenePath));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void RepeatedBuildOnlySavesOwnedAssetsAndIsByteStable()
        {
            AssetDatabase.SaveAssets();
            string[] paths = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("ProjectSettings", "*", SearchOption.AllDirectories)).OrderBy(path => path).ToArray();
            byte[][] before = paths.Select(File.ReadAllBytes).ToArray();
            for (int repeat = 0; repeat < 2; repeat++)
            {
                PerformanceLadderBuilder.Build();
                AssetDatabase.SaveAssets();
                string[] after = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles("ProjectSettings", "*", SearchOption.AllDirectories)).OrderBy(path => path).ToArray();
                Assert.That(after, Is.EqualTo(paths));
                for (int i = 0; i < paths.Length; i++) Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(before[i]), paths[i]);
            }
        }
    }
}
