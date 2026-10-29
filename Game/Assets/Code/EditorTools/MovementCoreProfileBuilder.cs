using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Northwatch.Presentation;

namespace Northwatch.EditorTools
{
    public static class MovementCoreProfileBuilder
    {
        public static void TuneCap()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-movement-cap");
            if (index < 0 || index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out int cap) || cap < 0)
                throw new ArgumentException("Pass -movement-cap N.");
            MovementSettings settings = AssetDatabase.LoadAssetAtPath<MovementSettings>(SandboxSceneBuilder.MovementSettingsPath);
            if (settings == null) throw new InvalidOperationException("Movement settings asset is missing.");
            using var serialized = new SerializedObject(settings);
            serialized.FindProperty("maxSerialFallbacks").intValue = cap;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            settings.Snapshot();
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        public static void BuildWindows()
        {
            Directory.CreateDirectory("Builds/MovementCore");
            BuildReport report = PlayerBuildHelper.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PerformanceLadderBuilder.ScenePath },
                locationPathName = "Builds/MovementCore/MovementCore.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 || report.summary.totalWarnings != 0)
                throw new InvalidOperationException("Movement development Player failed or produced warnings.");
        }
    }
}
