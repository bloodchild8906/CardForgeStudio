using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    public static class CardForgeArtCatalog
    {
        public static VisualElement Create(string selectedPath, Action<string> onSelected)
        {
            var root = new VisualElement();
            root.AddToClassList("cf-art-catalog");
            var search = new TextField("Search art") { value = "" };
            root.Add(search);
            var resultCount = new Label();
            resultCount.style.marginTop = 5;
            resultCount.style.marginBottom = 5;
            resultCount.style.fontSize = 12;
            root.Add(resultCount);
            var scroller = new ScrollView(ScrollViewMode.Vertical);
            scroller.style.maxHeight = 330;
            scroller.verticalScrollerVisibility = ScrollerVisibility.Auto;
            root.Add(scroller);
            var grid = new VisualElement();
            grid.AddToClassList("cf-art-catalog__grid");
            scroller.Add(grid);

            void Rebuild(string query)
            {
                grid.Clear();
                var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(path => string.IsNullOrWhiteSpace(query) || path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Take(160)
                    .ToList();
                resultCount.text = paths.Count == 160
                    ? "Showing first 160 illustrations. Search to narrow the results."
                    : paths.Count + " illustrations found";
                if (paths.Count == 0)
                {
                    grid.Add(new Label("No texture assets match this search."));
                    return;
                }
                foreach (var path in paths)
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null) continue;
                    var localPath = path;
                    var tile = new Button(() => onSelected(localPath));
                    tile.AddToClassList("cf-art-catalog__tile");
                    if (string.Equals(localPath, selectedPath, StringComparison.OrdinalIgnoreCase)) tile.AddToClassList("is-selected");
                    tile.tooltip = localPath;
                    var preview = new Image { image = texture, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                    preview.AddToClassList("cf-art-catalog__preview");
                    tile.Add(preview);
                    var label = new Label(System.IO.Path.GetFileNameWithoutExtension(localPath)) { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("cf-art-catalog__label");
                    tile.Add(label);
                    grid.Add(tile);
                }
            }

            search.RegisterValueChangedCallback(change => Rebuild(change.newValue));
            Rebuild("");
            return root;
        }
    }
}
