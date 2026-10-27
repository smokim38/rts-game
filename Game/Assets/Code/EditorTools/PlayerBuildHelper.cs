using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Northwatch.EditorTools
{
    public static class PlayerBuildHelper
    {
        private const string ConnectSettingsPath = "ProjectSettings/UnityConnectSettings.asset";

        // Keeping build changes is reserved for the Settle builder (ADR 0010).
        public static BuildReport BuildPlayer(BuildPlayerOptions options, bool keepBuildChanges = false)
        {
            BuildReport report = null;
            WithSettingsRestored(() => report = BuildPipeline.BuildPlayer(options), keepBuildChanges);
            return report;
        }

        private static Dictionary<string, byte[]> SnapshotFiles()
        {
            var sources = new Dictionary<string, byte[]>();
            // Include untracked source assets too, without starting Git from Editor code (ADR 0006).
            foreach (string folder in new[] { "Assets", "Packages", "ProjectSettings" })
                foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    sources.Add(path.Replace('\\', '/'), File.ReadAllBytes(path));
            return sources;
        }

        private static void WithSettingsRestored(Action build, bool keepBuildChanges)
        {
            Dictionary<string, byte[]> sources = SnapshotFiles();
            PlayerSettings playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>().Single();
            UnityEngine.Object connectSettings = Unsupported.GetSerializedAssetInterfaceSingleton("UnityConnectSettings");
            if (connectSettings == null) throw new InvalidOperationException("UnityConnectSettings could not be loaded.");
            string playerBefore = EditorJsonUtility.ToJson(playerSettings);
            string connectBefore = EditorJsonUtility.ToJson(connectSettings);
            try
            {
                DisableServices(connectSettings);
                build();
            }
            finally
            {
                try
                {
                    if (!keepBuildChanges) EditorJsonUtility.FromJsonOverwrite(playerBefore, playerSettings);
                    EditorJsonUtility.FromJsonOverwrite(connectBefore, connectSettings);
                    DisableServices(connectSettings);
                }
                finally
                {
                    // Reset memory first: the Editor also saves settings at shutdown.
                    foreach (var source in sources)
                    {
                        if (keepBuildChanges && source.Key != ConnectSettingsPath) continue;
                        if (!File.Exists(source.Key) || !File.ReadAllBytes(source.Key).SequenceEqual(source.Value))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(source.Key));
                            File.WriteAllBytes(source.Key, source.Value);
                        }
                    }
                }
            }
        }

        private static void DisableServices(UnityEngine.Object settings)
        {
            using var serialized = new SerializedObject(settings);
            SerializedProperty property = serialized.GetIterator();
            int count = 0;
            while (property.Next(true))
            {
                if (property.name != "m_Enabled") continue;
                if (property.propertyType == SerializedPropertyType.Boolean) property.boolValue = false;
                else property.intValue = 0;
                count++;
            }
            if (count == 0) throw new InvalidOperationException("UnityConnectSettings has no m_Enabled fields.");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
