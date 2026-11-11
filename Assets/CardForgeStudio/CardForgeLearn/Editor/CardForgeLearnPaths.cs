using System;
using System.IO;
using UnityEngine;

namespace CardForge.Learn
{
    /// <summary>
    /// Locates the other halves of the CardForgeStudio repository relative to this Unity project.
    /// The Unity project may be the repository root or nested under it; the server is in the repository's Server folder.
    /// When the project is opened outside a CardForgeStudio checkout, the helpers report that instead of guessing.
    /// </summary>
    public static class CardForgeLearnPaths
    {
        /// <summary>The Unity project folder (the one that contains Assets).</summary>
        public static string ProjectRoot { get { return Directory.GetParent(Application.dataPath).FullName; } }

        /// <summary>The repository root, or null when the project is not inside a CardForgeStudio checkout.</summary>
        public static string RepoRoot
        {
            get
            {
                var projectRoot = ProjectRoot;
                if (HasRepositoryStructure(projectRoot)) return projectRoot;
                var parent = Directory.GetParent(projectRoot);
                return parent != null && HasRepositoryStructure(parent.FullName) ? parent.FullName : null;
            }
        }

        static bool HasRepositoryStructure(string path)
        {
            return Directory.Exists(Path.Combine(path, "Assets", "CardForgeDedicatedServer")) &&
                   Directory.Exists(Path.Combine(path, "Shared"));
        }

        public static string GamePackagesFolder { get { return RepoRoot == null ? null : Path.Combine(RepoRoot, "GamePackages"); } }
        public static string ServerFolder { get { return RepoRoot == null ? null : Path.Combine(RepoRoot, "Assets", "CardForgeDedicatedServer"); } }
        public static string ServerLauncher { get { return RepoRoot == null ? null : Path.Combine(RepoRoot, "Demo", "Start-CardForgeDedicatedServer.ps1"); } }
        public static string DocumentationFolder { get { return RepoRoot == null ? null : Path.Combine(RepoRoot, "Documentation"); } }
        public static string CardForgeDataFolder { get { return Path.Combine(Application.dataPath, "CardForgeData"); } }

        /// <summary>Counts files below a folder whose path contains the given segment and whose name matches the pattern.</summary>
        public static int CountFiles(string folder, string pathSegment, string pattern)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
                var count = 0;
                foreach (var file in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
                    if (string.IsNullOrEmpty(pathSegment) || file.Replace('\\', '/').IndexOf(pathSegment, StringComparison.OrdinalIgnoreCase) >= 0) count++;
                return count;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
