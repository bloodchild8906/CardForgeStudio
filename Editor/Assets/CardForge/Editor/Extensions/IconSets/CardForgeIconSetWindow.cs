using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardForge.Editor;
using CardForge.DesignLogic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    /// <summary>Slice icon atlases or import PNG folders as named, reusable digital TCG symbols.</summary>
    public sealed class CardForgeIconSetWindow : EditorWindow
    {
        [Serializable] public class IconSetEntry
        {
            public string name = "Icon";
            public string assetPath = "";
        }
        [Serializable] public class IconSetManifest
        {
            public string id = CardForgeData.NewId();
            public string name = "Icon set";
            public List<IconSetEntry> entries = new List<IconSetEntry>();
        }
        sealed class DraftIcon
        {
            public string name;
            public bool enabled = true;
        }

        CardForgeStore store;
        Action onSaved;
        Texture2D atlas;
        string setName = "Symbols", prefix = "symbol";
        int cols = 6, rows = 4, inset;
        bool trim = true;
        List<DraftIcon> draft = new List<DraftIcon>();
        VisualElement grid;
        Label status;
        int selectedIndex;

        [MenuItem("CardForge/Icon Set Studio")]
        public static void OpenFromMenu() => Open();

        public static void Open(CardForgeStore source = null, Action saved = null)
        {
            var window = GetWindow<CardForgeIconSetWindow>("Icon Set Studio");
            window.minSize = new Vector2(1000, 640);
            window.store = source ?? new CardForgeStore();
            if (source == null) window.store.Reload();
            window.onSaved = saved;
            window.Show();
            window.Render();
        }
        public void CreateGUI() => Render();

        void Render()
        {
            rootVisualElement.Clear();
            if (store == null) { store = new CardForgeStore(); store.Reload(); }
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/CardForge/Editor/UI/USS/CardForge.uss");
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            var app = new VisualElement(); app.AddToClassList("cf-app"); app.AddToClassList("cf-tool-window");
            app.style.flexGrow = 1; rootVisualElement.Add(app); new ThemeController(app);
            var header = new VisualElement(); header.AddToClassList("cf-tool-header"); app.Add(header);
            var kicker = new Label("CARDFORGE / SYMBOL LIBRARY"); kicker.AddToClassList("cf-tool-kicker"); header.Add(kicker);
            var heading = new Label("Icon Set Studio"); heading.AddToClassList("cf-tool-title"); header.Add(heading);
            header.Add(new Label("Import an icon atlas or a folder of illustrations. Slice and name reusable PNGs, then bind them to cards."));
            var row = new VisualElement(); row.AddToClassList("cf-tool-controls"); header.Add(row);
            var field = new ObjectField("Sprite atlas") { objectType = typeof(Texture2D),
                allowSceneObjects = false, value = atlas };
            field.AddToClassList("cf-tool-asset");
            field.RegisterValueChangedCallback(e => { atlas = e.newValue as Texture2D; RefreshDraft(); Render(); });
            row.Add(field);
            row.Add(Button("Import image…", ImportAtlasFile));
            row.Add(Button("Import PNG folder…", ImportFolder));

            var body = new VisualElement(); body.AddToClassList("cf-tool-body"); app.Add(body);
            var preview = new VisualElement(); preview.AddToClassList("cf-tool-preview-pane"); body.Add(preview);
            status = new Label(atlas == null ? "Choose an atlas to preview its slices." :
                cols * rows + " cells  ·  " + atlas.width + " × " + atlas.height + " pixels");
            status.AddToClassList("cf-tool-preview-bar"); preview.Add(status);
            var view = new ScrollView(); view.AddToClassList("cf-tool-icon-scroll"); preview.Add(view);
            grid = new VisualElement(); grid.AddToClassList("cf-tool-icon-grid"); view.Add(grid);
            var options = new ScrollView(); options.AddToClassList("cf-tool-inspector"); body.Add(options);
            options.Add(Title("Icon set"));
            var set = new TextField("Set name") { value = setName, isDelayed = true };
            set.RegisterValueChangedCallback(e => setName = e.newValue); options.Add(set);
            var itemPrefix = new TextField("Default prefix") { value = prefix, isDelayed = true };
            itemPrefix.RegisterValueChangedCallback(e => prefix = e.newValue); options.Add(itemPrefix);
            var columns = new IntegerField("Columns") { value = cols, isDelayed = true };
            columns.RegisterValueChangedCallback(e => {
                cols = Mathf.Clamp(e.newValue, 1, 32); RefreshDraft(); Render();
            }); options.Add(columns);
            var rowCount = new IntegerField("Rows") { value = rows, isDelayed = true };
            rowCount.RegisterValueChangedCallback(e => {
                rows = Mathf.Clamp(e.newValue, 1, 32); RefreshDraft(); Render();
            }); options.Add(rowCount);
            var padding = new IntegerField("Cell inset (px)") { value = inset, isDelayed = true };
            padding.RegisterValueChangedCallback(e => { inset = Mathf.Clamp(e.newValue, 0, 128); RenderGrid(); });
            options.Add(padding);
            var alpha = new Toggle("Trim transparent pixels") { value = trim };
            alpha.RegisterValueChangedCallback(e => trim = e.newValue); options.Add(alpha);
            options.Add(new HelpBox(
                "Each cell is exported as an independent PNG. Uncheck blank cells, adjust names, and use the shared icon library in any layout or card. Transparent pixels can be trimmed automatically.",
                HelpBoxMessageType.Info));
            var save = Button("Import selected PNG icons", SaveAtlas);
            save.AddToClassList("cf-tool-primary");
            save.SetEnabled(atlas != null && draft.Any(d => d.enabled));
            options.Add(save);
            options.Add(Title("Selected icon"));
            var selected = selectedIndex < draft.Count ? draft[selectedIndex] : null;
            if (selected != null)
            {
                var name = new TextField("Name") { value = selected.name, isDelayed = true };
                name.RegisterValueChangedCallback(e => { selected.name = e.newValue; RenderGrid(); });
                options.Add(name);
                var include = new Toggle("Include in set") { value = selected.enabled };
                include.RegisterValueChangedCallback(e => { selected.enabled = e.newValue; RenderGrid(); });
                options.Add(include);
            }
            RenderGrid();
        }

        void RefreshDraft()
        {
            var previous = draft;
            draft = Enumerable.Range(0, Mathf.Clamp(cols * rows, 1, 1024)).Select(i =>
                i < previous.Count ? previous[i] : new DraftIcon
                {
                    name = CardForgeImageAssets.SafeName(prefix) + "-" + (i + 1).ToString("000")
                }).ToList();
            selectedIndex = Mathf.Clamp(selectedIndex, 0, draft.Count - 1);
        }

        void RenderGrid()
        {
            if (grid == null) return;
            grid.Clear();
            if (atlas == null) return;
            if (draft.Count != cols * rows) RefreshDraft();
            int width = atlas.width, height = atlas.height;
            for (int i = 0; i < draft.Count; i++)
            {
                var item = draft[i]; int index = i, row = i / cols, col = i % cols;
                var tile = Button("", () => { selectedIndex = index; Render(); });
                tile.AddToClassList("cf-tool-icon-tile");
                if (index == selectedIndex) tile.AddToClassList("cf-tool-icon-tile--selected");
                if (!item.enabled) tile.AddToClassList("cf-tool-icon-tile--disabled");
                var picture = new Image { image = atlas, pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.ScaleToFit,
                    uv = new Rect(col / (float)cols, 1f - (row + 1) / (float)rows,
                        1f / cols, 1f / rows) };
                picture.AddToClassList("cf-tool-icon-thumb"); tile.Add(picture);
                var label = new Label(item.name);
                label.AddToClassList("cf-tool-icon-label"); label.pickingMode = PickingMode.Ignore; tile.Add(label);
                grid.Add(tile);
            }
        }

        void ImportAtlasFile()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("Choose icon atlas", "",
                new[] { "Images", "png,jpg,jpeg", "All files", "*" });
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 48 * 1024 * 1024) throw new FormatException("Atlas exceeds 48MB.");
                var imported = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!imported.LoadImage(bytes)) throw new FormatException("Unsupported image.");
                string target = "Assets/CardForgeData/icon-sets/imports/" + CardForgeData.NewId() + ".png";
                CardForgeImageAssets.SavePng(imported, target);
                DestroyImmediate(imported);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
                setName = Path.GetFileNameWithoutExtension(path);
                prefix = CardForgeImageAssets.SafeName(setName);
                draft.Clear(); RefreshDraft(); Render();
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Atlas import", e.Message, "OK"); }
        }

        void ImportFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("Choose folder containing icons", "", "");
            if (string.IsNullOrEmpty(folder)) return;
            string[] paths = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length == 0) { EditorUtility.DisplayDialog("Icon Set Studio", "No PNG or JPEG files found.", "OK"); return; }
            if (paths.Length > 1024) { EditorUtility.DisplayDialog("Icon Set Studio", "Import up to 1024 icons at a time.", "OK"); return; }
            var set = new IconSetManifest { name = Path.GetFileName(folder) };
            string target = "Assets/CardForgeData/icon-sets/" + CardForgeImageAssets.SafeName(set.name) + "-" +
                set.id.Substring(0, 8);
            int imported = 0;
            try
            {
                foreach (string path in paths)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.Length > 24 * 1024 * 1024) throw new FormatException("Image exceeds 24MB: " + Path.GetFileName(path));
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    try
                    {
                        if (!texture.LoadImage(bytes)) throw new FormatException("Invalid image " + Path.GetFileName(path));
                        string name = Path.GetFileNameWithoutExtension(path);
                        string asset = target + "/" + CardForgeImageAssets.SafeName(name) + "-" + (imported + 1).ToString("000") + ".png";
                        string png = CardForgeImageAssets.SavePng(texture, asset);
                        set.entries.Add(new IconSetEntry { name = name, assetPath = png });
                    }
                    finally { DestroyImmediate(texture); }
                    imported++;
                }
                Commit(set, target);
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Icon import failed", e.Message, "OK"); }
        }

        void SaveAtlas()
        {
            if (atlas == null) return;
            if (draft.Count != cols * rows) RefreshDraft();
            var set = new IconSetManifest { name = string.IsNullOrWhiteSpace(setName) ? "Symbols" : setName.Trim() };
            string target = "Assets/CardForgeData/icon-sets/" + CardForgeImageAssets.SafeName(set.name) + "-" + set.id.Substring(0, 8);
            int cellWidth = atlas.width / cols, cellHeight = atlas.height / rows;
            if (cellWidth < 2 || cellHeight < 2) { EditorUtility.DisplayDialog("Icon Set Studio", "Atlas has too many rows or columns.", "OK"); return; }
            Texture2D readable = null;
            try
            {
                readable = CardForgeImageAssets.Readable(atlas);
                for (int i = 0; i < draft.Count; i++)
                {
                    if (!draft[i].enabled) continue;
                    int col = i % cols, row = i / cols;
                    int margin = Mathf.Clamp(inset, 0, (Mathf.Min(cellWidth, cellHeight) - 1) / 2);
                    var area = AtlasGeometry.Cell(atlas.width, atlas.height, cols, rows, i, margin);
                    string name = string.IsNullOrWhiteSpace(draft[i].name) ?
                        prefix + "-" + (i + 1).ToString("000") : draft[i].name.Trim();
                    string asset = target + "/" + CardForgeImageAssets.SafeName(name) + "-" + (i + 1).ToString("000") + ".png";
                    string png = CardForgeImageAssets.ImportIconFromReadable(readable, area, asset, trim);
                    set.entries.Add(new IconSetEntry { name = name, assetPath = png });
                }
                Commit(set, target);
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Icon set extraction", e.Message, "OK"); }
            finally { if (readable != null) DestroyImmediate(readable); }
        }

        void Commit(IconSetManifest set, string folder)
        {
            if (set.entries.Count == 0) throw new InvalidOperationException("Select at least one icon.");
            Directory.CreateDirectory(Path.GetFullPath(folder));
            File.WriteAllText(Path.GetFullPath(folder + "/manifest.json"), JsonUtility.ToJson(set, true));
            foreach (var entry in set.entries)
            {
                var icon = new IconDocument
                {
                    name = entry.name,
                    assetPath = entry.assetPath,
                    layers = new List<IconLayer>()
                };
                store.SaveIcon(icon);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            onSaved?.Invoke();
            EditorUtility.DisplayDialog("Icon set imported",
                set.entries.Count + " independent PNG icons and reusable icon records created under:\n" +
                folder + "\n\nThey are now available in CardForge's icon fields.", "OK");
        }

        static Button Button(string text, Action callback) => new Button(callback) { text = text };
        static Label Title(string text)
        {
            var label = new Label(text); label.AddToClassList("cf-tool-heading"); return label;
        }
    }
}
