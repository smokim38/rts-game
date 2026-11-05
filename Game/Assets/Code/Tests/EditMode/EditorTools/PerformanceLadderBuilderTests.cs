using System.IO;
using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Linq;
using Northwatch.EditorTools;
using Northwatch.Presentation;
using Northwatch.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditorInternal;

namespace Northwatch.Tests.EditMode.EditorTools
{
    public sealed class PerformanceLadderBuilderTests
    {
        [Test]
        public void OversizedCaptureIsRejectedBeforeLoadingOrReportingSuccess()
        {
            Directory.CreateDirectory("Logs");
            const string path = "Logs/230-oversized-test.raw";
            try
            {
                using (var file = File.Create(path)) file.SetLength(192L * 1024 * 1024 + 1);
                var error = Assert.Throws<InvalidOperationException>(() => MovementCoreProfileBuilder.AnalyzeCaptureFile(path));
                StringAssert.Contains("no frames were analyzed", error.Message);
                Assert.That(File.Exists(path + "-allocations.txt"), Is.False);
            }
            finally { File.Delete(path); }
        }

        [UnityTest]
        public IEnumerator CaptureChecksDetectRepeatedAndSingleFrameAllocations()
        {
            Directory.CreateDirectory("Logs");
            bool wasEnabled = UnityEngine.Profiling.Profiler.enabled;
            bool wasBinary = UnityEngine.Profiling.Profiler.enableBinaryLog;
            bool wasEditor = ProfilerDriver.profileEditor;
            bool wasDriverEnabled = ProfilerDriver.enabled;
            string previousLog = UnityEngine.Profiling.Profiler.logFile;
            try
            {
                // Real raw data exercises loading, GC metadata and frame boundaries.
                // The one-frame case catches accidental striding or early termination.
                for (int mode = 0; mode < 3; mode++)
                {
                    string path = "Logs/230-allocation-test-" + mode + ".raw";
                    UnityEngine.Profiling.Profiler.enabled = false;
                    ProfilerDriver.ClearAllFrames();
                    ProfilerDriver.profileEditor = true;
                    UnityEngine.Profiling.Profiler.logFile = path;
                    UnityEngine.Profiling.Profiler.enableBinaryLog = true;
                    UnityEngine.Profiling.Profiler.enabled = true;
                    for (int frame = 0; frame < 240; frame++)
                    {
                        UnityEngine.Profiling.Profiler.BeginSample("PlayerLoop");
                        UnityEngine.Profiling.Profiler.BeginSample("Northwatch.Simulation");
                        if (mode == 1 || (mode == 2 && frame == 120)) GC.KeepAlive(new byte[256]);
                        UnityEngine.Profiling.Profiler.EndSample();
                        foreach (string scope in new[] { "Northwatch.Drawing", "Northwatch.SelectionInput", "Northwatch.SandboxOverlay", "Northwatch.MoveMarker" })
                        {
                            UnityEngine.Profiling.Profiler.BeginSample(scope);
                            UnityEngine.Profiling.Profiler.EndSample();
                        }
                        UnityEngine.Profiling.Profiler.EndSample();
                        yield return null;
                    }
                    UnityEngine.Profiling.Profiler.enabled = false;
                    UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                    string ladderSummary = path + "-ladder.txt", sandboxSummary = path + "-sandbox.txt";
                    Action ladder = () => LadderProfileAnalysis.AnalyzeCapture(path, ladderSummary);
                    Action movement = () => MovementCoreProfileBuilder.AnalyzeCaptureFile(path);
                    if (mode == 0) { ladder(); movement(); }
                    else
                    {
                        Assert.Throws<InvalidOperationException>(() => ladder());
                        Assert.Throws<InvalidOperationException>(() => movement());
                    }
                    Assert.That(ProfilerDriver.lastFrameIndex, Is.LessThan(0), "Loaded data must be released even after a failed check.");
                    long ladderBytes = long.Parse(Regex.Match(File.ReadAllText(ladderSummary), @"GC_alloc_bytes=(\d+)").Groups[1].Value);
                    string movementOutput = File.ReadAllText(path + "-allocations.txt");
                    StringAssert.Contains("simulation_bytes=" + ladderBytes + ";", movementOutput);
                    Assert.That(ladderBytes, mode == 0 ? Is.Zero : Is.GreaterThan(0));
                    if (mode == 1)
                    {
                        int samples = int.Parse(Regex.Match(File.ReadAllText(ladderSummary), @"samples=(\d+)").Groups[1].Value);
                        Assert.That(ladderBytes, Is.EqualTo(samples * 288L), "Every interior frame must be inspected.");
                    }
                    if (mode == 2) Assert.That(ladderBytes, Is.InRange(256, 512), "Exactly one allocation must survive the full scan.");
                    var sandbox = typeof(SandboxProfileAnalysis).GetMethod("AnalyzeCaptures", BindingFlags.Static | BindingFlags.NonPublic);
                    Action selection = () => sandbox.Invoke(null, new object[] { new[] { path }, sandboxSummary, path });
                    // Editor PlayerLoop includes test-runner allocations, so sandbox rejects
                    // those too. Its simulation bytes must still match the other two jobs.
                    var failure = Assert.Throws<TargetInvocationException>(() => selection());
                    Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
                    StringAssert.Contains("scope=Northwatch.Simulation;", File.ReadAllText(sandboxSummary));
                    StringAssert.Contains("GC_alloc_bytes=" + ladderBytes, File.ReadAllText(sandboxSummary).Split('\n')[0]);
                    var variants = typeof(SandboxProfileAnalysis).GetMethod("AnalyzeVariants", BindingFlags.Static | BindingFlags.NonPublic);
                    failure = Assert.Throws<TargetInvocationException>(() => variants.Invoke(null,
                        new object[] { new[] { mode.ToString() }, "Logs/230-allocation-test-", sandboxSummary }));
                    Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
                    StringAssert.Contains("GC_alloc_bytes=" + ladderBytes, File.ReadAllText(sandboxSummary).Split('\n')[0]);
                    if (mode == 0)
                    {
                        var session = typeof(LadderProfileAnalysis).Assembly.GetType("Northwatch.EditorTools.ProfileCaptureSession");
                        var original = Assert.Throws<InvalidOperationException>(() =>
                        {
                            using var capture = (IDisposable)Activator.CreateInstance(session,
                                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { path }, null);
                            File.Delete(path + "-memory.txt");
                            Directory.CreateDirectory(path + "-memory.txt");
                            throw new InvalidOperationException("Original analysis failure");
                        });
                        Assert.That(original.Message, Is.EqualTo("Original analysis failure"));
                        Assert.That(ProfilerDriver.lastFrameIndex, Is.LessThan(0));
                    }
                }
            }
            finally
            {
                UnityEngine.Profiling.Profiler.enabled = false;
                UnityEngine.Profiling.Profiler.enableBinaryLog = wasBinary;
                UnityEngine.Profiling.Profiler.logFile = previousLog;
                ProfilerDriver.profileEditor = wasEditor;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = wasDriverEnabled;
                UnityEngine.Profiling.Profiler.enabled = wasEnabled;
                for (int mode = 0; mode < 3; mode++)
                {
                    string path = "Logs/230-allocation-test-" + mode + ".raw";
                    if (Directory.Exists(path + "-memory.txt")) Directory.Delete(path + "-memory.txt");
                    foreach (string file in Directory.GetFiles("Logs", Path.GetFileName(path) + "*")) File.Delete(file);
                }
            }
        }

        [Test]
        public void NativeCpuTopologyReportsPhysicalCoresWithoutCim()
        {
            var read = typeof(PerformanceLadder).GetMethod("ReadPhysicalCoreCount",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(read, Is.Not.Null);
            Assert.That((int)read.Invoke(null, null), Is.InRange(1, SystemInfo.processorCount));
        }

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
