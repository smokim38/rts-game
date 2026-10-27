using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Northwatch.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Northwatch.Tests.EditMode.EditorTools
{
    public sealed class PlayerBuildHelperTests
    {
        private const string ConnectPath = "ProjectSettings/UnityConnectSettings.asset";
        private static readonly Action<Action, bool> RunBuild = (Action<Action, bool>)Delegate.CreateDelegate(
            typeof(Action<Action, bool>), typeof(PlayerBuildHelper).GetMethod("WithSettingsRestored",
                BindingFlags.NonPublic | BindingFlags.Static));

        [Test]
        public void EveryUnityServiceIsDisabledInTrackedSettings()
        {
            MatchCollection flags = Regex.Matches(File.ReadAllText(ConnectPath), @"(?m)^\s*m_Enabled:\s*([^\r\n]+)");
            Assert.That(flags.Count, Is.GreaterThan(0), "The services guard must find the enabled flags.");
            foreach (Match flag in flags)
                Assert.That(flag.Groups[1].Value.Trim(), Is.EqualTo("0"), "Every m_Enabled must stay 0 (ADR 0010).");
        }

        [Test]
        public void SnapshotIncludesExactBytesOfEverySourceFile()
        {
            var snapshot = (Dictionary<string, byte[]>)typeof(PlayerBuildHelper).GetMethod("SnapshotFiles",
                BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            string[] paths = new[] { "Assets", "Packages", "ProjectSettings" }
                .SelectMany(folder => Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                .Select(path => path.Replace('\\', '/')).ToArray();
            Assert.That(snapshot.Keys, Is.EquivalentTo(paths));
            foreach (string path in paths) Assert.That(snapshot[path], Is.EqualTo(File.ReadAllBytes(path)), path);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RestoresBytesAndMemoryAfterBuildIncludingExceptions(bool keepBuildChanges, bool failBuild)
        {
            // A non-package fixture checks the Packages root without changing approved dependencies.
            const string packageFixture = "Packages/PlayerBuildHelperTests.snapshot";
            Assert.That(File.Exists(packageFixture), Is.False);
            string[] paths = { "Assets/Settings/PC_RPAsset.asset", packageFixture,
                "ProjectSettings/ProjectSettings.asset", ConnectPath };
            byte[] fixtureBytes = { 0, 255, 13, 10 };
            File.WriteAllBytes(packageFixture, fixtureBytes);
            byte[][] before = paths.Select(File.ReadAllBytes).ToArray();
            PlayerSettings playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>().Single();
            UnityEngine.Object connectSettings = Unsupported.GetSerializedAssetInterfaceSingleton("UnityConnectSettings");
            string playerBefore = EditorJsonUtility.ToJson(playerSettings);
            string connectBefore = EditorJsonUtility.ToJson(connectSettings);
            string companyBefore = PlayerSettings.companyName;
            const string buildCompany = "PlayerBuildHelperTests";
            try
            {
                Action build = () =>
                {
                    AssertServicesDisabled(connectSettings);
                    foreach (string path in paths) File.AppendAllText(path, "\r\nBuild rewrite\n");
                    File.Delete(packageFixture);
                    PlayerSettings.companyName = buildCompany;
                    // Model a build-time settings rewrite without enabling any service.
                    using var connect = new SerializedObject(connectSettings);
                    SerializedProperty testMode = connect.FindProperty("m_TestMode");
                    if (testMode.propertyType == SerializedPropertyType.Boolean) testMode.boolValue = !testMode.boolValue;
                    else testMode.intValue = testMode.intValue == 0 ? 1 : 0;
                    connect.ApplyModifiedPropertiesWithoutUndo();
                    if (failBuild) throw new InvalidOperationException("Simulated build failure");
                };
                if (failBuild)
                {
                    var error = Assert.Throws<InvalidOperationException>(() => RunBuild(build, keepBuildChanges));
                    Assert.That(error.Message, Is.EqualTo("Simulated build failure"));
                }
                else RunBuild(build, keepBuildChanges);

                for (int i = 0; i < paths.Length; i++)
                {
                    if (!keepBuildChanges || paths[i] == ConnectPath)
                        Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(before[i]), paths[i]);
                    else if (paths[i] == packageFixture) Assert.That(File.Exists(paths[i]), Is.False);
                    else Assert.That(File.ReadAllBytes(paths[i]), Is.Not.EqualTo(before[i]), paths[i]);
                }
                Assert.That(PlayerSettings.companyName, Is.EqualTo(keepBuildChanges ? buildCompany : companyBefore));
                if (!keepBuildChanges) Assert.That(EditorJsonUtility.ToJson(playerSettings), Is.EqualTo(playerBefore));
                Assert.That(EditorJsonUtility.ToJson(connectSettings), Is.EqualTo(connectBefore));
                AssertServicesDisabled(connectSettings);
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(playerBefore, playerSettings);
                EditorJsonUtility.FromJsonOverwrite(connectBefore, connectSettings);
                for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], before[i]);
                File.Delete(packageFixture);
            }
        }

        private static void AssertServicesDisabled(UnityEngine.Object settings)
        {
            using var serialized = new SerializedObject(settings);
            SerializedProperty property = serialized.GetIterator();
            int count = 0;
            while (property.Next(true))
            {
                if (property.name != "m_Enabled") continue;
                if (property.propertyType == SerializedPropertyType.Boolean) Assert.That(property.boolValue, Is.False, property.propertyPath);
                else Assert.That(property.intValue, Is.Zero, property.propertyPath);
                count++;
            }
            Assert.That(count, Is.GreaterThan(0));
        }
    }
}
