using System;
using System.IO;
using System.Text.RegularExpressions;
using Northwatch.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Northwatch.Tests.EditMode
{
    public sealed class ArchitectureTests
    {
        [Test]
        public void SimulationHasNoUnityObjectTypes()
        {
            foreach (Type type in typeof(UnitSimulation).Assembly.GetTypes())
                Assert.That(typeof(UnityEngine.Object).IsAssignableFrom(type), Is.False, type.FullName);
        }

        [Test]
        public void ScriptNamespacesMatchFolders()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Code" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string source = File.ReadAllText(path);
                if (Path.GetFileName(path) == "AssemblyInfo.cs")
                {
                    Assert.That(Regex.IsMatch(source, @"\b(class|struct|enum|interface)\s+"), Is.False, path);
                    continue;
                }
                string expected = "Northwatch." + Path.GetDirectoryName(path.Substring("Assets/Code/".Length)).Replace('\\', '.').Replace('/', '.');
                Assert.That(Regex.Match(source, @"\bnamespace\s+([\w.]+)").Groups[1].Value, Is.EqualTo(expected), path);
            }
        }
    }
}
