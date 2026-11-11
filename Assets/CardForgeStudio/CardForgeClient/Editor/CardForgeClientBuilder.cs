using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TcgServer.Client.Editor
{
    public static class CardForgeClientBuilder
    {
        public const string Output = "Builds/Windows/CardForgeStudio.exe";

        [MenuItem("CardForge/Demo/Build Windows Client")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before building the demo client.");
            if (!File.Exists(TcgTemplateProjectSetup.ScenePath))
                TcgTemplateProjectSetup.CreateTemplateScene();
            TcgTemplateProjectSetup.EnsurePanelSettings();
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            PlayerSettings.dedicatedServerOptimizations = false;

            var outputDirectory = Path.GetDirectoryName(Output) ?? "Builds";
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
            Directory.CreateDirectory(outputDirectory);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { TcgTemplateProjectSetup.ScenePath },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.StrictMode
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("CardForge Windows client build failed: " +
                    report.summary.result);

            Debug.Log("CARDFORGE_CLIENT_BUILD_READY " + Path.GetFullPath(Output));
        }
    }
}




