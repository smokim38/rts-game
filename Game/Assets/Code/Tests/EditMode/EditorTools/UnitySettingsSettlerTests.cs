using System;
using System.IO;
using System.Reflection;
using Northwatch.EditorTools;
using NUnit.Framework;

namespace Northwatch.Tests.EditMode.EditorTools
{
    public sealed class UnitySettingsSettlerTests
    {
        private static readonly Action<string, Action> RunChecked = (Action<string, Action>)Delegate.CreateDelegate(
            typeof(Action<string, Action>), typeof(UnitySettingsSettler).GetMethod("WithOnlySettingsChanged",
                BindingFlags.NonPublic | BindingFlags.Static));
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.GetFullPath("Builds/UnitySettingsSettlerTests/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(root, true);

        [Test]
        public void AllowsOnlyTheFiveSettingsAndIgnoresGeneratedBuildFiles()
        {
            string[] allowed =
            {
                "Game/Assets/Settings/Mobile_RPAsset.asset",
                "Game/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset",
                "Game/Assets/Settings/DefaultVolumeProfile.asset",
                "Game/Assets/Settings/PC_RPAsset.asset",
                "Game/ProjectSettings/ProjectSettings.asset"
            };
            foreach (string path in allowed) Write(path, "before");
            Write("docs/unchanged.md", "unchanged");
            Assert.DoesNotThrow(() => RunChecked(root, () =>
            {
                foreach (string path in allowed) Write(path, "after");
                Write("Game/Builds/UnitySettingsSettler/Sandbox.exe", "build output");
                Write("Game/Library/import", "cache");
                Write("Game/Logs/settle.log", "log");
            }));
        }

        [TestCase("Game/Assets/Settings/PC_RPAsset.asset.meta")]
        [TestCase("Game/Assets/Scenes/Sandbox.unity")]
        [TestCase("Game/Packages/manifest.json")]
        [TestCase("Game/ProjectSettings/UnityConnectSettings.asset")]
        [TestCase("Game/ProjectSettings/QualitySettings.asset")]
        [TestCase("docs/unrelated.md")]
        public void RejectsUnexpectedByteChangesIncludingMetasAndFilesOutsideGame(string path)
        {
            Write(path, "before\r\n");
            var error = Assert.Throws<InvalidOperationException>(() => RunChecked(root, () => Write(path, "before\n")));
            Assert.That(error.Message, Does.Contain(path));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RejectsUnexpectedAdditionsAndDeletions(bool add)
        {
            const string path = "Game/Assets/Settings/unexpected.asset";
            if (!add) Write(path, "before");
            var error = Assert.Throws<InvalidOperationException>(() => RunChecked(root, () =>
            {
                if (add) Write(path, "after");
                else File.Delete(Path.Combine(root, path));
            }));
            Assert.That(error.Message, Does.Contain(path));
        }

        [Test]
        public void ChecksUnexpectedChangesEvenWhenSettlingThrows()
        {
            const string path = "Game/Assets/unexpected.asset.meta";
            var failure = new IOException("Build failed");
            Write(path, "before");
            var error = Assert.Throws<InvalidOperationException>(() => RunChecked(root, () =>
            {
                Write(path, "after");
                throw failure;
            }));
            Assert.That(error.Message, Does.Contain(path));
            Assert.That(error.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void PreservesBuildFailureWhenNoUnexpectedFileChanged()
        {
            var failure = new IOException("Build failed");
            Assert.That(Assert.Throws<IOException>(() => RunChecked(root, () => throw failure)), Is.SameAs(failure));
        }

        private void Write(string relative, string contents)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents);
        }
    }
}
