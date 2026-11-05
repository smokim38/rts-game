using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Northwatch.EditorTools;
using Northwatch.Presentation;
using Northwatch.Simulation;
using Northwatch.Simulation.Movement;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Northwatch.Tests.EditMode.EditorTools
{
    public sealed class TankMeshBuilderTests
    {
        [Test]
        public void BuilderRepeatsExactMeshAndPrefabBytes()
        {
            SandboxSceneBuilder.BuildTankPresentation();
            string[] paths = { TankMeshBuilder.MeshPath, TankMeshBuilder.MeshPath + ".meta",
                "Assets/Prefabs/MatchDriver.prefab", "Assets/Prefabs/MatchDriver.prefab.meta" };
            byte[][] before = Array.ConvertAll(paths, File.ReadAllBytes);
            SandboxSceneBuilder.BuildTankPresentation();
            for (int i = 0; i < paths.Length; i++) Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(before[i]), paths[i]);
        }

        [Test]
        public void MeshHasOneSubmeshAndOutwardFacesWithinTheFootprint()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(TankMeshBuilder.MeshPath);
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(36).And.LessThanOrEqualTo(100));
            Assert.That(mesh.bounds.min, Is.EqualTo(new Vector3(-.5f, -.5f, -.5f)));
            Assert.That(mesh.bounds.size.x, Is.EqualTo(1));
            Assert.That(mesh.bounds.size.z, Is.EqualTo(1));
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            int[] triangles = mesh.triangles;
            foreach (Vector3 vertex in vertices)
            {
                Assert.That(vertex.x, Is.InRange(-.5f, .5f));
                Assert.That(vertex.z, Is.InRange(-.5f, .5f));
                Assert.That(vertex.y, Is.InRange(-.5f, .5f));
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Assert.That(Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized,
                    normals[a]), Is.GreaterThan(.999f));
            }
        }

        [TestCase(UnitSize.Small, 0f, 1.2f, .25f)]
        [TestCase(UnitSize.Large, 3.1f, -3.1f, .75f)]
        public void HullAndBarrelFollowSimulatedSizeAndInterpolatedHeading(UnitSize size, float previous, float heading, float alpha)
        {
            using var simulation = new UnitSimulation();
            simulation.QueueAdd(Side.First, size, new Vector3(4, 0, 6));
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            var unit = simulation.State.Movement[0];
            unit.Previous = new MovementPose { Position = new float3(3, 0, 5), Heading = previous };
            unit.Heading = heading;
            simulation.State.Movement[0] = unit;
            simulation.ScheduleFrame(0, alpha); simulation.CompleteFrame();
            Matrix4x4 matrix = simulation.DrawMatrices[0];
            float angle = previous + math.atan2(math.sin(heading - previous), math.cos(heading - previous)) * alpha;
            Vector3 forward = new Vector3(math.cos(angle), 0, math.sin(angle));
            Vector3 sideways = new Vector3(-forward.z, 0, forward.x);
            Vector3 centre = Vector3.Lerp(new Vector3(3, 0, 5), new Vector3(4, 0, 6), alpha);
            float half = Footprint.HalfWidth(size);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(TankMeshBuilder.MeshPath);
            Vector3[] vertices = mesh.vertices;
            foreach (Vector3 vertex in vertices)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertex) - centre;
                Assert.That(Mathf.Abs(Vector3.Dot(world, forward)), Is.LessThanOrEqualTo(half + 1e-6f));
                Assert.That(Mathf.Abs(Vector3.Dot(world, sideways)), Is.LessThanOrEqualTo(half + 1e-6f));
                Assert.That(world.y, Is.GreaterThanOrEqualTo(-1e-6f));
            }
            Vector3 barrel = matrix.MultiplyVector(new Vector3(.5f - .12f, 0, 0)).normalized;
            Assert.That(Vector3.Distance(barrel, forward), Is.LessThan(1e-6f));
            Assert.That(matrix.MultiplyVector(Vector3.right).magnitude, Is.EqualTo(half * 2).Within(1e-6f));
        }

        [TestCase(SandboxSceneBuilder.ScenePath)]
        [TestCase(PerformanceLadderBuilder.ScenePath)]
        public void ScenesUseTankForUnitsAndKeepCubeForMarkers(string path)
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent(out MatchDriver driver)) continue;
                using var data = new SerializedObject(driver);
                var tank = (Mesh)data.FindProperty("vehicle").objectReferenceValue;
                Assert.That(AssetDatabase.GetAssetPath(tank), Is.EqualTo(TankMeshBuilder.MeshPath));
                Assert.That(data.FindProperty("cube").objectReferenceValue, Is.Not.Null.And.Not.EqualTo(tank));
                foreach (string field in new[] { "firstSide", "secondSide", "selectedSide" })
                    Assert.That(((Material)data.FindProperty(field).objectReferenceValue).enableInstancing, Is.True);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return;
            }
            Assert.Fail("MatchDriver missing");
        }

        [Test]
        public void FixedMovementScenarioPreservesSimulationBytes()
        {
            var asset = ScriptableObject.CreateInstance<MovementSettings>();
            using var simulation = new UnitSimulation(asset.Snapshot());
            UnityEngine.Object.DestroyImmediate(asset);
            simulation.QueueBlock(Side.First, UnitSize.Small, new Vector3(-8, 0, -4), 50);
            simulation.QueueBlock(Side.Second, UnitSize.Large, new Vector3(8, 0, 4), 50);
            simulation.ScheduleFrame(1); simulation.CompleteFrame();
            Vector3 initial = simulation.State.Positions[0];
            simulation.QueueSelection(SelectionRequest.Box(new Matrix4x4 { m00 = .02f, m12 = .02f, m33 = 1 },
                new Rect(0, 0, 1, 1)));
            simulation.ScheduleFrame(0); simulation.CompleteFrame();
            simulation.QueueSelection(SelectionRequest.PointerClick(new Ray(new Vector3(12, 10, 12), Vector3.down),
                Matrix4x4.identity, false, true, false, false, false));
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            for (int frame = 0; frame < 120; frame++)
            {
                simulation.ScheduleFrame(1, .5f, frame / 30f); simulation.CompleteFrame();
                Write(writer, simulation.TickCount);
                WriteArray(writer, simulation.State.Positions); WriteArray(writer, simulation.State.Movement);
                WriteArray(writer, simulation.State.Sides); WriteArray(writer, simulation.State.Sizes);
                WriteArray(writer, simulation.State.Alive); WriteArray(writer, simulation.State.Generations);
                WriteArray(writer, simulation.State.FreeSlots); WriteArray(writer, simulation.State.Summary);
                WriteArray(writer, simulation.DrawMatrices);
            }
            byte[] bytes = stream.ToArray();
            Assert.That((Vector3)simulation.State.Positions[0], Is.Not.EqualTo(initial), "Scenario exercises movement");
            using var hash = SHA256.Create();
            // Captured with the cube renderer at c7cd0824cd520a80a5c9bb2726d6a0fb45b6e286.
            Assert.That(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""),
                Is.EqualTo("FD42D6BE3F76ECB56AFE7A21DB980E33DA825C065A45547AB8D1744583E4AD8E"));
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-tank-state-after") >= 0)
            {
                Assert.That(bytes, Is.EqualTo(File.ReadAllBytes("Logs/issue-224-simulation-before.bin")));
                File.WriteAllBytes("Logs/issue-224-simulation-after.bin", bytes);
            }
        }

        private static void WriteArray<T>(BinaryWriter writer, NativeArray<T> values) where T : struct
        {
            writer.Write(values.Length);
            foreach (T value in values) Write(writer, value);
        }

        // Field values exclude native struct padding, whose bytes are not simulation state.
        private static void Write(BinaryWriter writer, object value)
        {
            switch (value)
            {
                case float number: writer.Write(number); return;
                case int number: writer.Write(number); return;
                case uint number: writer.Write(number); return;
                case ulong number: writer.Write(number); return;
                case ushort number: writer.Write(number); return;
                case byte number: writer.Write(number); return;
                case bool flag: writer.Write(flag); return;
                case Enum number: writer.Write(Convert.ToInt32(number)); return;
            }
            FieldInfo[] fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (left, right) => string.CompareOrdinal(left.Name, right.Name));
            foreach (FieldInfo field in fields) Write(writer, field.GetValue(value));
        }
    }
}
