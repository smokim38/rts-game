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
        public static void TunePasses()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-movement-passes");
            if (index < 0 || index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out int passes) || passes < 1 || passes > 4)
                throw new ArgumentException("Pass -movement-passes 1|2|3|4.");
            MovementSettings settings = AssetDatabase.LoadAssetAtPath<MovementSettings>(SandboxSceneBuilder.MovementSettingsPath);
            if (settings == null) throw new InvalidOperationException("Movement settings asset is missing.");
            using var serialized = new SerializedObject(settings);
            serialized.FindProperty("moveCheckPasses").intValue = passes;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            settings.Snapshot();
            EditorUtility.SetDirty(settings);
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
