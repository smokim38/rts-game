using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Northwatch.EditorTools;
using NUnit.Framework;
using UnityEditor;

namespace Northwatch.Tests.EditMode.EditorTools
{
    public sealed class UnityOrganizationSettingsBuilderTests
    {
        private const string SettingsPath = "ProjectSettings/ProjectSettings.asset";

        [Test]
        public void TrackedPlayerSettingsHaveNoOrganizationId()
        {
            MatchCollection fields = Regex.Matches(File.ReadAllText(SettingsPath),
                @"(?m)^  organizationId:([^\r\n]*)\r?$");
            Assert.That(fields.Count, Is.EqualTo(1), "Expected one organizationId field.");
            // Assert a boolean so a failing test never prints the field's value.
            Assert.That(string.IsNullOrWhiteSpace(fields[0].Groups[1].Value), Is.True,
                "The tracked organizationId field must be empty.");
        }

        [Test]
        public void RepeatedBuilderLeavesClearedPlayerSettingsByteIdentical()
        {
            TrackedPlayerSettingsHaveNoOrganizationId();
            AssetDatabase.SaveAssets();
            byte[] before = File.ReadAllBytes(SettingsPath);
            for (int run = 0; run < 2; run++)
            {
                UnityOrganizationSettingsBuilder.Build();
                AssetDatabase.SaveAssets();
                Assert.That(File.ReadAllBytes(SettingsPath).SequenceEqual(before), Is.True,
                    "Rebuilding must preserve every byte of the cleared Player settings.");
            }
        }
    }
}
