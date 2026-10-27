using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Northwatch.EditorTools
{
    public static class UnitySettingsSettler
    {
        private static readonly string[] SettingsPaths =
        {
            "Game/Assets/Settings/Mobile_RPAsset.asset",
            "Game/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset",
            "Game/Assets/Settings/DefaultVolumeProfile.asset",
            "Game/Assets/Settings/PC_RPAsset.asset",
            "Game/ProjectSettings/ProjectSettings.asset"
        };

        public static void Settle()
        {
            WithOnlySettingsChanged(Path.GetFullPath(".."), () =>
            {
                AssetDatabase.ForceReserializeAssets(SettingsPaths.Take(3).Select(path => path.Substring(5)),
                    ForceReserializeAssetsOptions.ReserializeAssets);
                Directory.CreateDirectory("Builds/UnitySettingsSettler");
                BuildReport report = PlayerBuildHelper.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { SandboxSceneBuilder.ScenePath },
                    locationPathName = "Builds/UnitySettingsSettler/Sandbox.exe",
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
                }, keepBuildChanges: true);
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 || report.summary.totalWarnings != 0)
                    throw new InvalidOperationException("Settings settling build failed or produced warnings.");
                AssetDatabase.SaveAssets();
            });
        }

        private static void WithOnlySettingsChanged(string root, Action settle)
        {
            Dictionary<string, byte[]> before = SnapshotFiles(root);
            try { settle(); }
            finally
            {
                Dictionary<string, byte[]> after = SnapshotFiles(root);
                string[] changed = before.Keys.Union(after.Keys).Where(path => !SettingsPaths.Contains(path) &&
                    (!before.TryGetValue(path, out byte[] oldHash) || !after.TryGetValue(path, out byte[] newHash) ||
                     !oldHash.SequenceEqual(newHash))).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                if (changed.Length != 0)
                    throw new InvalidOperationException("Settle changed files outside its five settings paths: " + string.Join(", ", changed));
            }
        }

        private static Dictionary<string, byte[]> SnapshotFiles(string root)
        {
            var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using SHA256 hash = SHA256.Create();
            ReadDirectory(root, "");
            return snapshot;

            void ReadDirectory(string directory, string relative)
            {
                // Check a superset of tracked sources without starting Git from Editor code (ADR 0006).
                // Only Git/studio state and Unity's generated directories are outside this check.
                foreach (string path in Directory.GetFiles(directory))
                {
                    string name = relative + Path.GetFileName(path);
                    if (name == ".git") continue;
                    using FileStream stream = File.OpenRead(path);
                    snapshot.Add(name, hash.ComputeHash(stream));
                }
                foreach (string path in Directory.GetDirectories(directory))
                {
                    string name = relative + Path.GetFileName(path);
                    if (name == ".git" || name == ".studio") continue;
                    if (relative == "Game/" && new[] { "Library", "Temp", "Logs", "UserSettings", "obj", "Build", "Builds",
                        "MemoryCaptures", "Recordings", ".utmp" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)) continue;
                    ReadDirectory(path, name + "/");
                }
            }
        }
    }
}
