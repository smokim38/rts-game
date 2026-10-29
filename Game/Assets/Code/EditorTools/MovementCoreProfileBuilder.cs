using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Northwatch.EditorTools
{
    public static class MovementCoreProfileBuilder
    {
        public static void BuildWindows()
        {
            Directory.CreateDirectory("Builds/MovementCore");
            BuildReport report = PlayerBuildHelper.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Sandbox.unity" },
                locationPathName = "Builds/MovementCore/MovementCore.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 || report.summary.totalWarnings != 0)
                throw new InvalidOperationException("Movement development Player failed or produced warnings.");
        }
    }
}
