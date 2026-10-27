using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;

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

        private static readonly string[] AssetsToReserialize =
        {
            "Assets/Settings/Mobile_RPAsset.asset",
            "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset",
            "Assets/Settings/DefaultVolumeProfile.asset"
        };

        public static void Settle()
        {
            WithOnlySettingsChanged(Path.GetFullPath(".."), () =>
            {
                ReserializeSettingsAssets();
                Directory.CreateDirectory("Builds/UnitySettingsSettler");
                BuildReport report = PlayerBuildHelper.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { SandboxSceneBuilder.ScenePath },
                    locationPathName = "Builds/UnitySettingsSettler/Sandbox.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                }, keepBuildChanges: true);
                if (report.summary.result != BuildResult.Succeeded ||
                    report.summary.totalErrors != 0 || report.summary.totalWarnings != 0)
                    throw new InvalidOperationException("Settings settling build failed or produced warnings.");
                AssetDatabase.SaveAssets();
            });
        }

        private static void ReserializeSettingsAssets()
        {
            AssetDatabase.ForceReserializeAssets(AssetsToReserialize, ForceReserializeAssetsOptions.ReserializeAssets);
            // URP finishes deserialization upgrades in delayCall. A synchronous -executeMethod/-quit
            // run must drain that pending work before saving, or it writes an unfinished upgrade.
            EditorApplication.CallbackFunction pendingUpgrades = EditorApplication.delayCall;
            EditorApplication.delayCall = null;
            pendingUpgrades?.Invoke();
            AssetDatabase.SaveAssets();
        }

        private static void WithOnlySettingsChanged(string root, Action settle)
        {
            Dictionary<string, byte[]> before = SnapshotFiles(root);
            Exception failure = null;
            try { settle(); }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                Dictionary<string, byte[]> after = SnapshotFiles(root);
                string[] changed = before.Keys.Union(after.Keys).Where(path => !SettingsPaths.Contains(path) &&
                    (!before.TryGetValue(path, out byte[] oldHash) || !after.TryGetValue(path, out byte[] newHash) ||
                     !oldHash.SequenceEqual(newHash))).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                if (changed.Length != 0)
                    throw new InvalidOperationException("Settle changed files outside its five settings paths: " +
                        string.Join(", ", changed), failure);
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
