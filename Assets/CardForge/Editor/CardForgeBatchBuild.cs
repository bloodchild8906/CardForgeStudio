using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CardForge.Editor
{
    /// <summary>
    /// CI integration, without creating additional Unity menu items.
    /// unity -batchmode -quit -projectPath ... -buildTarget StandaloneWindows64
    ///       -executeMethod CardForge.Editor.CardForgeBatchBuild.BuildAllActive
    /// </summary>
    public static class CardForgeBatchBuild
    {
        public static void BuildAllActive()
        {
            var store = new CardForgeStore();
            store.Reload();
            var target = EditorUserBuildSettings.activeBuildTarget;
            int complete = 0;
            foreach (var block in store.Blocks.Values)
            {
                try
                {
                    string destination = store.ExportBlock(block, target);
                    Debug.Log("CardForge exported " + block.code + " -> " + destination);
                    complete++;
                }
                catch (Exception ex)
                {
                    Debug.LogError("CardForge failed exporting " + block.code + ": " + ex);
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                    throw;
                }
            }
            Debug.Log("CardForge complete: " + complete + " block bundles for " + target);
        }

        /// <summary>
        /// CI entry point for portable authoritative releases.
        /// unity -batchmode -quit -projectPath ...
        ///       -executeMethod CardForge.Editor.CardForgeBatchBuild.BuildAllGamePackages
        /// </summary>
        public static void BuildAllGamePackages()
        {
            var store = new CardForgeStore();
            store.Reload();
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var destination = Environment.GetEnvironmentVariable("CARDFORGE_PACKAGE_OUTPUT");
            if (string.IsNullOrWhiteSpace(destination)) destination = Path.GetFullPath(Path.Combine(projectRoot, "..", "GamePackages"));
            Directory.CreateDirectory(destination);
            var complete = 0;
            foreach (var block in store.Blocks.Values)
            {
                try
                {
                    var game = store.GetGameDefinition(block);
                    var file = Path.Combine(destination, game.packageId + "-" + game.packageVersion + ".cardforge");
                    CardForgeGamePackageExporter.Export(store, block, game, file);
                    Debug.Log("CardForge package exported " + block.code + " -> " + file);
                    complete++;
                }
                catch (Exception exception)
                {
                    Debug.LogError("CardForge package export failed for " + block.code + ": " + exception);
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                    throw;
                }
            }
            Debug.Log("CardForge package export complete: " + complete + " package(s).");
        }
    }
}
