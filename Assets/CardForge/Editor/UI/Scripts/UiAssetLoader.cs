using UnityEditor;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    /// <summary>Loads UI assets from the single CardForge asset root.</summary>
    public static class UiAssetLoader
    {
        public const string UiRootPath = "Assets/CardForge/Editor/UI";

        /// <summary>Loads a visual tree asset by its path relative to the UI root.</summary>
        public static VisualTreeAsset LoadTree(string relativePath)
        {
            var assetPath = $"{UiRootPath}/{relativePath}";
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
            if (tree != null) return tree;
            foreach (var guid in AssetDatabase.FindAssets(System.IO.Path.GetFileNameWithoutExtension(relativePath) + " t:VisualTreeAsset")) {
                var candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (candidate.EndsWith("/" + relativePath, System.StringComparison.Ordinal))
                    return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(candidate);
            }
            throw new System.IO.FileNotFoundException($"CardForge UI asset not found: {assetPath}");
        }

        /// <summary>Instantiates a template and returns the named root element detached from its wrapper.</summary>
        public static VisualElement InstantiateRoot(string relativePath, string rootName)
        {
            return LoadTree(relativePath).Instantiate().Q(rootName);
        }
    }
}
