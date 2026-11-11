using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardForge.DedicatedServer.Editor
{
    /// <summary>
    /// Builds the native Unity host. Authoritative rule code comes from the
    /// CardForge.Simulation asmdef; no ASP.NET DLL is bundled in the player.
    /// </summary>
    public static class DedicatedServerBuilder
    {
        public const string Scene = "Assets/CardForgeStudio/CardForgeDedicatedServer/Scenes/CardForgeServer.unity";
        public const string Output = "Builds/DedicatedServer/CardForgeServer.exe";

        [MenuItem("CardForge/Server/Create Native Simulation Scene")]
        public static void CreateScene()
        {
            if (File.Exists(Scene)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var host = new GameObject("CardForge Unity Dedicated Simulation");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<DedicatedServerHost>();
            EditorSceneManager.SaveScene(scene, Scene);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
        }

        [MenuItem("CardForge/Server/Build Native Windows Dedicated Server")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before building the dedicated server.");
            CreateScene();
            Directory.CreateDirectory(Path.GetDirectoryName(Output) ?? "Builds");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.StrictMode
            };
            var previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            var previousOptimizations = PlayerSettings.dedicatedServerOptimizations;
            try
            {
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException(
                        "Unity server build failed: " + report.summary.result +
                        ". Install Windows Dedicated Server build support if Unity reports a missing module.");
                Debug.Log("Native CardForge Unity Dedicated Server: " + Path.GetFullPath(Output));
            }
            finally
            {
                // Restore project build preferences even if BuildPlayer throws.
                PlayerSettings.dedicatedServerOptimizations = previousOptimizations;
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
            }
        }
    }
}

