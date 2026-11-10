using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardForge.Editor;
using CardForge.DesignLogic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    /// <summary>AI-assisted extraction of editable digital-card layouts and their PNG layers.</summary>
    public sealed class CardForgeFrameComposerWindow : EditorWindow
    {
        CardForgeStore store;
        Action onSaved;
        Texture2D source;
        LayoutDocument draft;
        string blockId, selectedZoneId;
        bool removeContent = true, refineEdges = true, busy;
        VisualElement workspace, inspector, surface;
        CardForgeCanvas canvas;
        Label state;
        TextField layoutName;
        DropdownField blockSelect;
        readonly List<BlockDocument> blockOptions = new List<BlockDocument>();
        bool isBack;

        [MenuItem("CardForge/Frame Composer")]
        public static void OpenFromMenu() => Open();

        public static void Open(CardForgeStore data = null, string activeBlockId = null, Action saved = null)
        {
            var window = GetWindow<CardForgeFrameComposerWindow>("Frame Composer");
            window.minSize = new Vector2(1020, 670);
            window.store = data ?? new CardForgeStore();
            if (data == null) window.store.Reload();
            window.onSaved = saved;
            window.blockId = activeBlockId;
            window.Show();
            window.Render();
        }

        public void CreateGUI() => Render();

        void Render()
        {
            if (store == null) { store = new CardForgeStore(); store.Reload(); }
            if (draft == null) draft = new LayoutDocument { name = "Extracted frame" };
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1;
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/CardForge/Editor/UI/USS/CardForge.uss");
            if (style != null) rootVisualElement.styleSheets.Add(style);
            var app = new VisualElement { name = "frame-composer" };
            app.AddToClassList("cf-app"); app.AddToClassList("cf-tool-window");
            app.style.flexGrow = 1;
            rootVisualElement.Add(app);
            new ThemeController(app);
            var head = new VisualElement(); head.AddToClassList("cf-tool-header"); app.Add(head);
            var kicker = new Label("CARDFORGE / DESIGN TOOLS"); kicker.AddToClassList("cf-tool-kicker"); head.Add(kicker);
            var title = new Label("Card Frame Composer"); title.AddToClassList("cf-tool-title"); head.Add(title);
            head.Add(new Label("Analyze a reference frame, adjust its editable regions, then extract reusable PNGs and save a layout."));
            var controls = new VisualElement(); controls.AddToClassList("cf-tool-controls"); head.Add(controls);
            var asset = new ObjectField("Reference image") { objectType = typeof(Texture2D), allowSceneObjects = false, value = source };
            asset.AddToClassList("cf-tool-asset"); controls.Add(asset);
            asset.RegisterValueChangedCallback(e => { source = e.newValue as Texture2D; ResetSource(); Render(); });
            controls.Add(Btn("Import image…", ImportFile));
            controls.Add(Btn("Ollama settings", CardForgeAiSettings.Open));
            var local = Btn("Detect regions locally", DetectLocal); local.SetEnabled(source != null && !busy); controls.Add(local);
            var ai = Btn(busy ? "Analyzing…" : "Analyze with Ollama Cloud", async () => await DetectAi());
            ai.AddToClassList("cf-tool-primary"); ai.SetEnabled(source != null && !busy); controls.Add(ai);
            var body = new VisualElement(); body.AddToClassList("cf-tool-body"); app.Add(body);
            surface = new VisualElement(); surface.AddToClassList("cf-tool-preview-pane"); body.Add(surface);
            var previewBar = new VisualElement(); previewBar.AddToClassList("cf-tool-preview-bar"); surface.Add(previewBar);
            state = new Label(busy ? "Analyzing image…" :
                source == null ? "Select a reference PNG or JPEG to begin." :
                draft.zones.Count + " proposed zones · " + source.width + " × " + source.height + " px");
            state.AddToClassList("cf-tool-state"); previewBar.Add(state);
            var zoom = new Button(() => { if (source == null) return; canvas?.Draw(draft, null, store.Schema,
                store.Icons, PreviewZoom(), true, selectedZoneId, SelectZone, MoveZone); }) { text = "Fit preview" };
            previewBar.Add(zoom);
            canvas = new CardForgeCanvas { Snap = false, ShowGrid = false };
            canvas.AddToClassList("cf-frame-canvas"); surface.Add(canvas);
            DrawPreview();

            inspector = new ScrollView(); inspector.AddToClassList("cf-tool-inspector"); body.Add(inspector);
            inspector.Add(Heading("Output layout"));
            layoutName = new TextField("Layout name") { value = draft.name, isDelayed = true };
            layoutName.RegisterValueChangedCallback(e => draft.name = e.newValue);
            inspector.Add(layoutName);
            blockOptions.Clear(); blockOptions.AddRange(store.Blocks.Values.OrderBy(b => b.name));
            var names = blockOptions.Select(b => b.code + " · " + b.name).ToList();
            if (names.Count == 0) names.Add("Create a block first");
            int index = Mathf.Max(0, blockOptions.FindIndex(b => b.id == blockId));
            blockSelect = new DropdownField("Save to edition", names, index);
            blockSelect.SetEnabled(blockOptions.Count > 0);
            blockSelect.RegisterValueChangedCallback(e => { int i = names.IndexOf(e.newValue); if (i >= 0 && i < blockOptions.Count) blockId = blockOptions[i].id; });
            inspector.Add(blockSelect);
            if (blockOptions.Count > 0 && string.IsNullOrEmpty(blockId)) blockId = blockOptions[0].id;
            var back = new Toggle("Card back layout") { value = isBack }; back.RegisterValueChangedCallback(e => isBack = e.newValue); inspector.Add(back);
            var snap = new Toggle("Snap AI regions to visible edges") { value = refineEdges };
            snap.tooltip = "Search nearby image boundaries to refine AI coordinates before manual editing.";
            snap.RegisterValueChangedCallback(e => refineEdges = e.newValue); inspector.Add(snap);
            var erase = new Toggle("Clear regions from frame PNG") { value = removeContent };
            erase.RegisterValueChangedCallback(e => removeContent = e.newValue); inspector.Add(erase);
            inspector.Add(new HelpBox("Every extracted region is saved separately as PNG. The composited frame overlay can be made transparent inside detected content slots while preserving a small border. Review detected boundaries before committing.", HelpBoxMessageType.Info));
            var save = Btn("Save extracted layout + PNG assets", Save);
            save.AddToClassList("cf-tool-primary"); save.SetEnabled(source != null && blockOptions.Count > 0 && !busy);
            inspector.Add(save);
            inspector.Add(Heading("Zones"));
            var add = Btn("+ Add region", () => {
                draft.zones.Add(new ZoneDefinition { name = "Content region", kind = ZoneKind.Image,
                    x = draft.width * .15f, y = draft.height * .15f, width = draft.width * .7f,
                    height = draft.height * .3f, order = draft.zones.Count });
                selectedZoneId = draft.zones.Last().id; Render();
            });
            add.SetEnabled(source != null); inspector.Add(add);
            foreach (var z in draft.zones.ToList())
            {
                var zone = z;
                var button = Btn((z.id == selectedZoneId ? "● " : "○ ") + z.name + "  [" + z.kind + "]",
                    () => { selectedZoneId = zone.id; RenderZoneEditor(); DrawPreview(); });
                button.AddToClassList("cf-zone-entry");
                inspector.Add(button);
            }
            RenderZoneEditor();
        }

        void ImportFile()
        {
            string file = EditorUtility.OpenFilePanelWithFilters("Import digital card frame", "",
                new[] { "Images", "png,jpg,jpeg", "All files", "*" });
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                var bytes = System.IO.File.ReadAllBytes(file);
                if (bytes.Length > 48 * 1024 * 1024) throw new InvalidOperationException("Image exceeds the 48MB import limit.");
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes)) throw new FormatException("Not a supported PNG or JPEG image.");
                string asset = "Assets/CardForgeData/frame-imports/" + CardForgeImageAssets.SafeName(
                    System.IO.Path.GetFileNameWithoutExtension(file)) + "-" + CardForgeData.NewId().Substring(0, 8) + ".png";
                CardForgeImageAssets.SavePng(texture, asset);
                DestroyImmediate(texture);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                source = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                ResetSource(); Render();
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Frame import", e.Message, "OK"); }
        }

        void ResetSource()
        {
            if (source == null) { draft = new LayoutDocument { name = "Extracted frame" }; selectedZoneId = null; return; }
            draft = new LayoutDocument
            {
                name = CardForgeImageAssets.SafeName(source.name) + " layout",
                width = source.width, height = source.height,
                backgroundAssetPath = AssetDatabase.GetAssetPath(source),
                backgroundColor = "#151e2b"
            };
            selectedZoneId = null;
        }

        void DetectLocal()
        {
            if (source == null) return;
            draft.zones.Clear();
            // Non-AI geometric starting points: conservative regions, tuned to common card frames.
            // The user can adjust any field or resize the rectangles before extraction.
            AddProposal("Card name", ZoneKind.Text, "title", .07f, .04f, .74f, .09f);
            AddProposal("Cost", ZoneKind.Number, "cost", .83f, .04f, .11f, .10f);
            AddProposal("Artwork", ZoneKind.Image, "art", .07f, .16f, .86f, .38f);
            AddProposal("Card type", ZoneKind.Text, "type", .07f, .56f, .86f, .07f);
            AddProposal("Rules", ZoneKind.Text, "rules", .07f, .65f, .86f, .23f);
            AddProposal("Power", ZoneKind.Number, "power", .79f, .90f, .15f, .07f);
            selectedZoneId = draft.zones[0].id; Render();
        }

        void AddProposal(string name, ZoneKind kind, string fieldKey, float x, float y, float w, float h)
        {
            var field = store.Schema.fields.FirstOrDefault(f =>
                string.Equals(f.key, fieldKey, StringComparison.OrdinalIgnoreCase));
            draft.zones.Add(new ZoneDefinition { name = name, kind = kind,
                fieldId = field?.id ?? "", x = draft.width * x, y = draft.height * y,
                width = draft.width * w, height = draft.height * h, order = draft.zones.Count,
                backgroundColor = "#00000000" });
        }

        async Task DetectAi()
        {
            if (busy || source == null) return;
            busy = true; if (state != null) state.text = "Analyzing with " + CardForgeOllama.Model + "…";
            try
            {
                var result = await CardForgeOllama.AnalyzeAsync(source);
                draft.zones.Clear();
                var proposals = new List<CardForgeOllama.ZoneProposal>();
                var bounds = new List<Rect>();
                foreach (var proposal in result.zones)
                {
                    if (!FrameGeometry.TryNormalize(proposal.x, proposal.y, proposal.w, proposal.h,
                        draft.width, draft.height, out var region)) continue;
                    proposals.Add(proposal);
                    bounds.Add(region);
                }
                if (refineEdges && bounds.Count > 0)
                    bounds = CardForgeFrameEdgeRefiner.Refine(source, bounds);
                for (int index = 0; index < proposals.Count; index++)
                {
                    var proposal = proposals[index];
                    var region = bounds[index];
                    if (!Enum.TryParse(proposal.kind, true, out ZoneKind kind)) kind = ZoneKind.Text;
                    var key = CardForgeImageAssets.SafeName(proposal.field).Replace('-', '_');
                    var match = store.Schema.fields.FirstOrDefault(f =>
                        string.Equals(f.key, key, StringComparison.OrdinalIgnoreCase));
                    draft.zones.Add(new ZoneDefinition {
                        name = proposal.name, kind = kind, fieldId = match?.id ?? "",
                        x = region.x, y = region.y,
                        width = region.width, height = region.height,
                        order = draft.zones.Count, backgroundColor = "#00000000"
                    });
                }
                selectedZoneId = draft.zones.FirstOrDefault()?.id;
                Render();
            }
            catch (Exception error)
            {
                EditorUtility.DisplayDialog("Ollama frame analysis", error.Message, "OK");
                if (state != null) state.text = "AI analysis failed. Local region suggestions are available.";
            }
            finally { busy = false; if (aiSafe) Render(); }
        }
        bool aiSafe => rootVisualElement != null && store != null;

        float PreviewZoom()
        {
            if (draft == null) return .5f;
            float width = surface == null ? 650f : Mathf.Max(450f, surface.resolvedStyle.width);
            float height = surface == null ? 640f : Mathf.Max(450f, surface.resolvedStyle.height);
            return Mathf.Clamp(Mathf.Min((width - 100) / draft.width, (height - 145) / draft.height), .2f, 1.25f);
        }
        void DrawPreview()
        {
            if (canvas == null || store == null) return;
            canvas.Draw(source == null ? null : draft, null, store.Schema,
                store.Icons, PreviewZoom(), true, selectedZoneId, SelectZone, MoveZone);
        }
        void SelectZone(ZoneDefinition zone)
        {
            selectedZoneId = zone.id;
            RenderZoneEditor();
        }
        void MoveZone(ZoneDefinition zone, float x, float y, float width, float height)
        {
            zone.x = Mathf.Clamp(x - zone.offsetX, 0, draft.width - 10);
            zone.y = Mathf.Clamp(y - zone.offsetY, 0, draft.height - 10);
            zone.width = Mathf.Clamp(width, 10, draft.width - zone.x);
            zone.height = Mathf.Clamp(height, 10, draft.height - zone.y);
        }
        VisualElement zoneEditor;
        void RenderZoneEditor()
        {
            if (inspector == null) return;
            if (zoneEditor != null) inspector.Remove(zoneEditor);
            zoneEditor = new VisualElement(); zoneEditor.AddToClassList("cf-tool-zone-editor");
            inspector.Add(zoneEditor);
            var z = draft?.zones.FirstOrDefault(item => item.id == selectedZoneId);
            if (z == null) { zoneEditor.Add(new Label("Select a zone to edit its bounds and field binding.")); return; }
            zoneEditor.Add(Heading("Selected: " + z.name));
            var name = new TextField("Name") { value = z.name, isDelayed = true };
            name.RegisterValueChangedCallback(e => { z.name = e.newValue; });
            zoneEditor.Add(name);
            var kind = new EnumField("Zone type", z.kind);
            kind.RegisterValueChangedCallback(e => { z.kind = (ZoneKind)e.newValue; DrawPreview(); });
            zoneEditor.Add(kind);
            var options = new List<string> { "Unbound" };
            options.AddRange(store.Schema.fields.Select(f => f.key + " · " + f.label));
            int current = store.Schema.fields.FindIndex(f => f.id == z.fieldId) + 1;
            var field = new DropdownField("Data field", options, Mathf.Clamp(current, 0, options.Count - 1));
            field.RegisterValueChangedCallback(e => {
                int idx = options.IndexOf(e.newValue) - 1;
                z.fieldId = idx >= 0 ? store.Schema.fields[idx].id : "";
                DrawPreview();
            }); zoneEditor.Add(field);
            NumberField("X", z.x, v => z.x = Mathf.Clamp(v, 0, draft.width - z.width));
            NumberField("Y", z.y, v => z.y = Mathf.Clamp(v, 0, draft.height - z.height));
            NumberField("Width", z.width, v => z.width = Mathf.Clamp(v, 10, draft.width - z.x));
            NumberField("Height", z.height, v => z.height = Mathf.Clamp(v, 10, draft.height - z.y));
            zoneEditor.Add(Btn("Remove region", () => {
                draft.zones.Remove(z); selectedZoneId = draft.zones.FirstOrDefault()?.id; Render();
            }));
        }
        void NumberField(string label, float value, Action<float> onChanged)
        {
            var field = new FloatField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(e => { onChanged(e.newValue); DrawPreview(); });
            zoneEditor.Add(field);
        }
        void Save()
        {
            if (source == null || draft == null) return;
            var block = blockOptions.FirstOrDefault(b => b.id == blockId);
            if (block == null) { EditorUtility.DisplayDialog("Frame Composer", "Select an edition.", "OK"); return; }
            if (string.IsNullOrWhiteSpace(draft.name)) { EditorUtility.DisplayDialog("Frame Composer", "Give your layout a name.", "OK"); return; }
            try
            {
                draft.isBack = isBack;
                var assets = CardForgeImageAssets.SaveFrame(source, draft, removeContent);
                draft.backgroundAssetPath = removeContent ? assets.OverlayPath : assets.ReferencePath;
                foreach (var region in assets.Regions)
                {
                    var zone = draft.zones.FirstOrDefault(z => z.id == region.Key);
                    if (zone == null) continue;
                    if (!draft.templates.Any(t => t.backgroundAssetPath == region.Value))
                        draft.templates.Add(new ZoneTemplateDefinition {
                            name = zone.name + " · extracted PNG",
                            backgroundAssetPath = region.Value,
                            imageScaleMode = ZoneImageScaleMode.Stretch
                        });
                }
                // Reuse generated layout IDs and zone IDs so asset references remain stable.
                if (!block.layoutIds.Contains(draft.id)) block.layoutIds.Add(draft.id);
                store.SaveLayout(block.id, draft);
                if (isBack && string.IsNullOrEmpty(block.defaultBackLayoutId)) block.defaultBackLayoutId = draft.id;
                store.SaveBlock(block);
                onSaved?.Invoke();
                EditorUtility.DisplayDialog("Frame extracted",
                    "Created layout ‘" + draft.name + "’ with " + draft.zones.Count + " editable regions.\n" +
                    assets.Regions.Count + " region PNGs, one source PNG and one overlay PNG were written to CardForgeData/frames.\n" +
                    "You can refine it further in Layout Designer.", "OK");
                Close();
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Frame extraction failed", e.Message, "OK"); }
        }
        static Button Btn(string title, Action action) => new Button(action) { text = title };
        static Label Heading(string text) { var label = new Label(text); label.AddToClassList("cf-tool-heading"); return label; }
    }
}
