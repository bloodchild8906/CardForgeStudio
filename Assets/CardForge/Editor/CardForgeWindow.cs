using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    /// <summary>
    /// One entry point, three workspaces. VisualElements only: no IMGUI and no menu sprawl.
    /// </summary>
    public sealed partial class CardForgeDesignerView : VisualElement, IDisposable
    {
        enum Workspace { Layouts, Database, Blocks, TurnFlow, Triggers, Actions, Abilities }
        CardForgeStore _store;
        Workspace _tab = Workspace.Layouts;
        bool _layoutDesignerOpen, _databaseEditorOpen, _blockEditorOpen, _triggerEditorOpen, _keywordEditorOpen;
        string _blockId, _layoutId, _cardId, _zoneId, _iconId, _triggerId, _keywordId, _cardArtFieldId;
        bool _iconStudio, _showSchema, _liveCardEdit, _cloneCards = true, _previewBack;
        float _zoom = .8f;
        VisualElement _main, _inspector;
        CardForgeCanvas _canvas;
        LayoutDocument _drawLayout;
        CardDocument _drawCard;
        string _notification = "Ready";
        Texture2D _iconPreview;

        readonly VisualElement rootVisualElement;
        readonly bool embedded;
        public event System.Action Closed;
        public event System.Action ContextChanged;
        public string DocumentScreen => _tab == Workspace.Layouts ? (_iconStudio ? "icons" : "layouts") :
            _tab == Workspace.Database ? (_showSchema ? "schema" : "cards") : _tab == Workspace.Blocks ? "blocks" :
            _tab == Workspace.TurnFlow ? "turnflow" : _tab == Workspace.Triggers ? "triggers" : _tab == Workspace.Actions ? "actions" : _keywordEditorOpen ? "keywords" : "abilities";
        public string DocumentId => DocumentScreen == "schema" ? null : _iconStudio ? _iconId :
            _tab == Workspace.Layouts ? _layoutId : _tab == Workspace.Database ? _cardId : _tab == Workspace.Blocks ? _blockId :
            _tab == Workspace.TurnFlow ? _flowId : _tab == Workspace.Triggers ? _triggerId : _tab == Workspace.Actions ? _actionId : _keywordEditorOpen ? _keywordId : _abilityId;
        public string DocumentTitle => DocumentScreen == "schema" ? "Data fields" : _iconStudio ? Icon?.name ?? "Icon Workshop" :
            _tab == Workspace.Layouts ? Layout?.name ?? "Layout" : _tab == Workspace.Database ? Card?.name ?? "Card" :
            _tab == Workspace.Blocks ? Block?.name ?? "Block" : _tab == Workspace.TurnFlow ? Flow?.name ?? "Turn flow" : _tab == Workspace.Triggers ? Trigger?.name ?? "Trigger" :
            _tab == Workspace.Actions ? Action?.name ?? "Action" : _keywordEditorOpen ? Keyword?.name ?? "Keyword" : Ability?.name ?? "Ability";
        public CardForgeDesignerView(CardForgeStore store = null, string screen = null, string id = null, string blockId = null)
        {
            rootVisualElement = this;
            style.flexGrow = 1; style.minHeight = 0;
            _store = store ?? new CardForgeStore();
            if (store == null) _store.Reload();
            embedded = screen != null;
            _blockId = blockId ?? _store.Blocks.Values.OrderBy(b => b.name).Select(b => b.id).FirstOrDefault();
            switch (screen)
            {
                case "layouts": _layoutId = id; _layoutDesignerOpen = true; break;
                case "icons": _iconId = id; _iconStudio = true; _layoutDesignerOpen = true; break;
                case "cards": _tab = Workspace.Database; _cardId = id; _databaseEditorOpen = true; break;
                case "schema": _tab = Workspace.Database; _showSchema = true; break;
                case "blocks": _tab = Workspace.Blocks; _blockId = id ?? _blockId; _blockEditorOpen = true; break;
                case "turnflow": _tab = Workspace.TurnFlow; _flowId = id; break;
                case "triggers": _tab = Workspace.Triggers; _triggerId = id; _triggerEditorOpen = true; break;
                case "actions": _tab = Workspace.Actions; _actionId = id; _actionEditorOpen = true; break;
                case "abilities": _tab = Workspace.Abilities; _abilityId = id; _abilityEditorOpen = true; break;
                case "keywords": _tab = Workspace.Abilities; _keywordId = id; _keywordEditorOpen = true; break;
            }
            var sheetPath = "Assets/CardForge/Editor/UI/USS/CardForgeWindow.uss";
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(sheetPath);
            if (sheet != null) styleSheets.Add(sheet);
            focusable = true;
            RegisterCallback<KeyDownEvent>(OnDesignerKey, TrickleDown.TrickleDown);
            Render();
        }
        public void Dispose() { if (_iconPreview != null) UnityEngine.Object.DestroyImmediate(_iconPreview); }
        BlockDocument Block => _blockId != null && _store.Blocks.TryGetValue(_blockId, out var b) ? b : null;
        LayoutDocument Layout => _layoutId != null && _store.Layouts.TryGetValue(_layoutId, out var l) ? l : null;
        CardDocument Card => _cardId != null && _store.Cards.TryGetValue(_cardId, out var c) ? c : null;
        IconDocument Icon => _iconId != null && _store.Icons.TryGetValue(_iconId, out var i) ? i : null;
        void EnsureSelection()
        {
            if (_store.Blocks.Count == 0) { _blockId = null; _layoutId = null; _cardId = null; }
            if (_triggerId == null || !_store.Triggers.ContainsKey(_triggerId)) _triggerId = _store.Triggers.Values.OrderBy(x => x.name).Select(x => x.id).FirstOrDefault();
            if (_flowId == null || !_store.TurnFlows.ContainsKey(_flowId)) _flowId = _store.TurnFlows.Values.OrderBy(x => x.name).Select(x => x.id).FirstOrDefault();
            if (_actionId == null || !_store.Actions.ContainsKey(_actionId)) _actionId = _store.Actions.Values.OrderBy(x => x.name).Select(x => x.id).FirstOrDefault();
            if (_abilityId == null || !_store.Abilities.ContainsKey(_abilityId)) _abilityId = _store.Abilities.Values.OrderBy(x => x.name).Select(x => x.id).FirstOrDefault();
            if (_keywordId == null || !_store.Keywords.ContainsKey(_keywordId)) _keywordId = _store.Keywords.Values.OrderBy(x => x.name).Select(x => x.id).FirstOrDefault();
            if (Block == null && _store.Blocks.Count > 0) _blockId = _store.Blocks.Values.OrderBy(x => x.name).First().id;
            if (Block != null)
            {
                if (!Block.layoutIds.Contains(_layoutId)) _layoutId = Block.layoutIds.FirstOrDefault();
                if (!Block.cardIds.Contains(_cardId)) _cardId = Block.cardIds.FirstOrDefault();
            }
            if (_layoutId != null && Layout != null && !Layout.zones.Any(z => z.id == _zoneId)) _zoneId = null;
            if (_store.Icons.Count > 0 && Icon == null) _iconId = _store.Icons.Values.First().id;
        }
        void Render()
        {
            if (_store == null) return;
            _canvas = null; _drawLayout = null; _drawCard = null;
            EnsureSelection();
            rootVisualElement.Clear(); rootVisualElement.AddToClassList("cf-root");
            if (embedded && ((_tab == Workspace.Layouts && !_layoutDesignerOpen) ||
                (_tab == Workspace.Database && !_databaseEditorOpen && !_showSchema) ||
                (_tab == Workspace.Blocks && !_blockEditorOpen) ||
                (_tab == Workspace.Triggers && !_triggerEditorOpen) ||
                (_tab == Workspace.Actions && !_actionEditorOpen) || (_tab == Workspace.Abilities && !_abilityEditorOpen && !_keywordEditorOpen)))
            { Closed?.Invoke(); return; }
            SyncHistory();
            CardForgeArtwork.AddWorkspaceBackdrop(rootVisualElement, _iconStudio ? "Icons" : _tab.ToString());
            if (!embedded) {
            var header = new Toolbar(); header.AddToClassList("app-header"); rootVisualElement.Add(header);
            var brand = Col("brand"); header.Add(brand);
            brand.Add(CardForgeArtwork.BrandMark());
            brand.Add(new Label("CARDFORGE") { name = "brand-text" });
            brand.Add(new Label("CARD CONTENT STUDIO") { name = "brand-sub" });
            var tabs = Row("main-tabs"); header.Add(tabs);
            AddTab(tabs, "01   Layout Designer", Workspace.Layouts);
            AddTab(tabs, "02   Card Database", Workspace.Database);
            AddTab(tabs, "03   Block Builder", Workspace.Blocks);
            AddTab(tabs, "04   Turn Flow", Workspace.TurnFlow);
            AddTab(tabs, "05   Triggers", Workspace.Triggers);
            AddTab(tabs, "06   Actions", Workspace.Actions);
            AddTab(tabs, "07   Abilities", Workspace.Abilities);
            header.Add(new ToolbarSpacer { flex = true });
            header.Add(new ToolbarButton(() =>
            {
                _store.Reload(); _notification = "JSON reloaded"; Render();
            }) { text = "Reload JSON" });
            header.Add(new Label(_notification) { name = "status-text" });
            }
            AddDesignToolbar();
            _main = Row("workspace"); rootVisualElement.Add(_main);
            switch (_tab)
            {
                case Workspace.Layouts: RenderLayouts(); break;
                case Workspace.Database: RenderDatabase(); break;
                case Workspace.Blocks: RenderBlocks(); break;
                case Workspace.TurnFlow: RenderTurnFlow(); break;
                case Workspace.Triggers: RenderTriggers(); break;
                case Workspace.Actions: RenderActions(); break;
                case Workspace.Abilities: RenderAbilities(); break;
            }
            ContextChanged?.Invoke();
            if (embedded) return;
            var footer = Row("footer");
            footer.Add(new Label("JSON SOURCE  ·  UI TOOLKIT  ·  NON-DESTRUCTIVE REPRINTS"));
            footer.Add(Flex());
            footer.Add(new Label("Assets/CardForgeData"));
            rootVisualElement.Add(footer);
            rootVisualElement.Q<Label>("status-text")?.SetEnabled(true);
        }
        void AddTab(VisualElement parent, string text, Workspace tab)
        {
            var b = new ToolbarButton(() =>
            {
                _tab = tab; _notification = "Ready";
                if (tab == Workspace.Layouts) _layoutDesignerOpen = false;
                if (tab == Workspace.Database) { _databaseEditorOpen = false; _showSchema = false; }
                if (tab == Workspace.Blocks) _blockEditorOpen = false;
                if (tab == Workspace.Triggers) _triggerEditorOpen = false;
                if (tab == Workspace.Actions) _actionEditorOpen = false;
                if (tab == Workspace.Abilities) _abilityEditorOpen = false;
                if (tab == Workspace.Abilities) _keywordEditorOpen = false;
                Render();
            }) { text = text };
            b.AddToClassList("tab");
            if (_tab == tab) b.AddToClassList("tab-active"); parent.Add(b);
        }
        static VisualElement Row(string cls = "")
        {
            var e = new VisualElement(); e.style.flexDirection = FlexDirection.Row;
            if (!string.IsNullOrEmpty(cls)) e.AddToClassList(cls); return e;
        }
        static VisualElement Col(string cls = "")
        {
            var e = new VisualElement(); e.style.flexDirection = FlexDirection.Column;
            if (!string.IsNullOrEmpty(cls)) e.AddToClassList(cls); return e;
        }
        static VisualElement Flex() { var v = new VisualElement(); v.style.flexGrow = 1; return v; }
        static Button Btn(string label, Action action, string cls = "button")
        { var b = new Button(action) { text = label }; b.AddToClassList(cls); return b; }
        static Label Title(string text) { var l = new Label(text); l.AddToClassList("section-title"); return l; }
        static Label Kicker(string text) { var l = new Label(text); l.AddToClassList("section-kicker"); return l; }
        static Label Note(string text) { var l = new Label(text); l.AddToClassList("muted"); return l; }
        static VisualElement Pane(string cls, float width = 0)
        {
            var pane = Col("pane"); pane.AddToClassList(cls);
            if (width > 0) { pane.style.width = width; pane.style.flexShrink = 0; }
            else pane.style.flexGrow = 1;
            return pane;
        }
        static ScrollView Scroll(VisualElement parent)
        {
            var s = new ScrollView(); s.style.flexGrow = 1; s.AddToClassList("panel-scroll"); parent.Add(s); return s;
        }
        static VisualElement Group(string label)
        {
            var g = Col("form-group"); g.Add(Kicker(label)); return g;
        }
        static TextField TextBox(VisualElement parent, string label, string value, Action<string> action, bool multiline = false)
        {
            var f = new TextField(label) { value = value ?? "", multiline = multiline, isDelayed = true };
            f.AddToClassList("field-input");
            if (multiline) f.style.minHeight = 70;
            f.RegisterValueChangedCallback(ev => action(ev.newValue)); parent.Add(f); return f;
        }
        static IntegerField IntBox(VisualElement parent, string label, int value, Action<int> action)
        {
            var f = new IntegerField(label) { value = value, isDelayed = true };
            f.RegisterValueChangedCallback(ev => action(ev.newValue)); parent.Add(f); return f;
        }
        static FloatField FloatBox(VisualElement parent, string label, float value, Action<float> action)
        {
            var f = new FloatField(label) { value = value, isDelayed = true };
            f.RegisterValueChangedCallback(ev => action(ev.newValue)); parent.Add(f); return f;
        }
        static Toggle ToggleBox(VisualElement parent, string label, bool value, Action<bool> action)
        {
            var t = new Toggle(label) { value = value };
            t.RegisterValueChangedCallback(ev => action(ev.newValue)); parent.Add(t); return t;
        }
        static void ColorBox(VisualElement parent, string label, string value, Action<string> action)
        {
            var f = new ColorField(label) { value = CardForgeCanvas.Parse(value, Color.white), showAlpha = true };
            f.RegisterValueChangedCallback(ev => action("#" + ColorUtility.ToHtmlStringRGBA(ev.newValue))); parent.Add(f);
        }
        static void TextureBox(VisualElement parent, string label, string assetPath, Action<string> action)
        {
            var f = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false };
            f.value = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            f.RegisterValueChangedCallback(ev => action(ev.newValue == null ? "" : AssetDatabase.GetAssetPath(ev.newValue)));
            parent.Add(f);
        }
        static void Choice(VisualElement parent, string label, IEnumerable<KeyValuePair<string,string>> data, string chosen, Action<string> onSelected)
        {
            var entries = data.ToList();
            var strings = entries.Select(x => x.Value).ToList();
            if (strings.Count == 0) { parent.Add(Note(label + ": none available")); return; }
            int selected = Mathf.Max(0, entries.FindIndex(x => x.Key == chosen));
            var f = new DropdownField(label, strings, selected);
            f.RegisterValueChangedCallback(evt =>
            {
                int index = strings.IndexOf(evt.newValue);
                if (index >= 0) onSelected(entries[index].Key);
            });
            parent.Add(f);
        }
        static IEnumerable<KeyValuePair<string,string>> Options<T>(IEnumerable<T> list, Func<T,string> id, Func<T,string> label)
        { return list.Select(x => new KeyValuePair<string,string>(id(x), label(x))); }
        void ChooseBlock(VisualElement parent)
        {
            Choice(parent, "Active block", Options(_store.Blocks.Values.OrderBy(x => x.name), x => x.id,
                x => x.code + " · " + x.name), _blockId,
                id => { _blockId = id; _layoutId = null; _cardId = null; _zoneId = null; Render(); });
        }
        void SaveLayout() { if (Block != null && Layout != null) _store.SaveLayout(Block.id, Layout); TrackHistory(); _notification = "Layout saved"; }
        void SaveCard() { if (Block != null && Card != null) _store.SaveCard(Block.id, Card); TrackHistory(); _notification = "Card saved"; }
        void SaveBlock() { if (Block != null) _store.SaveBlock(Block); TrackHistory(); _notification = "Block saved"; }
        void SaveSchema() { _store.SaveSchema(); TrackHistory(); _notification = "Schema saved"; }
        void Error(string title, Exception error) { EditorUtility.DisplayDialog(title, error.Message, "OK"); }
        void RefreshCanvas()
        {
            if (_canvas == null || _drawLayout == null) return;
            bool cardEdit = _tab == Workspace.Database && _liveCardEdit;
            _canvas.GestureCompleted = () => { TrackHistory(); FillInspector(_inspector); RefreshCanvas(); };
            _canvas.Snap = _snap; _canvas.GridSize = _gridSize; _canvas.ShowGrid = _showGrid;
            _canvas.Draw(_drawLayout, _drawCard, _store.Schema, _store.Icons, _zoom,
                (_tab == Workspace.Layouts && !_previewVariants) || cardEdit, _zoneId, zone =>
                {
                    _zoneId = zone.id; _canvas.SelectZone(zone.id);
                    if (_inspector != null) FillInspector(_inspector);
                }, (zone, x, y, w, h) =>
                {
                    if (_tab == Workspace.Database && Card != null)
                    {
                        var o = CardForgeData.EnsureOverride(Card, zone.id);
                        o.deltaX = x - zone.x - zone.offsetX;
                        o.deltaY = y - zone.y - zone.offsetY;
                        o.deltaWidth = w - zone.width;
                        o.deltaHeight = h - zone.height;
                        SaveCard();
                    }
                    else if (_tab == Workspace.Layouts)
                    {
                        zone.x = x - zone.offsetX; zone.y = y - zone.offsetY;
                        zone.width = w; zone.height = h; SaveLayout();
                    }
                    if (!_canvas.ApplyingTransforms) { FillInspector(_inspector); RefreshCanvas(); }
                });
        }
        void SetupPreview(VisualElement destination, LayoutDocument l, CardDocument card)
        {
            _drawLayout = l; _drawCard = card;
            _canvas = new CardForgeCanvas(); destination.Add(_canvas); RefreshCanvas();
        }
        static void AddListButton(VisualElement parent, string name, bool active, Action clicked, string secondary = null)
        {
            var button = Btn(name, clicked, "list-entry");
            if (active) button.AddToClassList("list-entry-selected");
            parent.Add(button);
            if (!string.IsNullOrEmpty(secondary)) { var s = Note(secondary); s.AddToClassList("list-secondary"); parent.Add(s); }
        }
        void RenderLayouts()
        {
            if (!_layoutDesignerOpen) { RenderLayoutIndex(); return; }
            if (!embedded) {
            var left = Pane("sidebar", 252); _main.Add(left);
            var leftBody = Scroll(left);
            leftBody.Add(Kicker("PROJECT")); ChooseBlock(leftBody);
            leftBody.Add(Title("Layout library"));
            var create = Row("button-row"); leftBody.Add(create);
            create.Add(Btn("+ Front", () => NewLayout(false), "primary-button"));
            create.Add(Btn("+ Back", () => NewLayout(true), "ghost-button"));
            leftBody.Add(Kicker("FRONTS"));
            foreach (var l in (Block?.layoutIds ?? new List<string>()).Select(id => _store.Layouts.TryGetValue(id, out var item) ? item : null)
                .Where(x => x != null && !x.isBack))
                AddListButton(leftBody, l.name, _layoutId == l.id && !_iconStudio,
                    () => { _iconStudio = false; _layoutId = l.id; _zoneId = null; Render(); });
            leftBody.Add(Kicker("CARD BACKS"));
            foreach (var l in (Block?.layoutIds ?? new List<string>()).Select(id => _store.Layouts.TryGetValue(id, out var item) ? item : null)
                .Where(x => x != null && x.isBack))
                AddListButton(leftBody, l.name, _layoutId == l.id && !_iconStudio,
                    () => { _iconStudio = false; _layoutId = l.id; _zoneId = null; Render(); });
            leftBody.Add(Title("Design tools"));
            leftBody.Add(Btn("✦ Icon Workshop", () => { _iconStudio = true; _layoutDesignerOpen = true; Render(); }, "primary-button"));
            }
            var center = Pane("center-pane"); _main.Add(center);
            if (!embedded) {
            var top = Row("content-bar"); center.Add(top);
            top.Add(Title(_iconStudio ? "Icon Workshop" : Layout == null ? "No layout" : Layout.name));
            top.Add(CardForgeUi.Back("Layouts", () => { _layoutDesignerOpen = false; _iconStudio = false; Render(); }));
            top.Add(Flex());
            if (!_iconStudio && Layout != null)
            {
                top.Add(Btn("+ Text", () => NewZone(ZoneKind.Text), "small-button"));
                top.Add(Btn("+ Number", () => NewZone(ZoneKind.Number), "small-button"));
                top.Add(Btn("+ Icon", () => NewZone(ZoneKind.Icon), "small-button"));
                top.Add(Btn("+ Image", () => NewZone(ZoneKind.Image), "small-button"));
                top.Add(Btn("+ Label", () => NewZone(ZoneKind.StaticText), "small-button"));
            }
            }
            if (_iconStudio) RenderIconCenter(center);
            else
            {
                var previewBar = Row("preview-bar"); center.Add(previewBar);
                if (!embedded) previewBar.Add(Note("Select zones  ·  Shift+click for multiple  ·  drag handles to resize"));
                previewBar.Add(Flex());
                var zoom = new Slider("Zoom", .2f, 2.5f) { value = _zoom };
                zoom.style.width = 185;
                var zoomValue=new Label(Mathf.RoundToInt(_zoom*100)+"%");zoomValue.AddToClassList("preview-zoom-value");
                zoom.RegisterValueChangedCallback(ev => { _zoom = ev.newValue;zoomValue.text=Mathf.RoundToInt(_zoom*100)+"%"; RefreshCanvas(); });
                previewBar.Add(zoom);
                previewBar.Add(zoomValue);
                var examples = Block == null ? new List<CardDocument>() : Block.cardIds.Where(_store.Cards.ContainsKey).Select(id=>_store.Cards[id]).Where(c=>Layout!=null&&(Layout.isBack?c.backLayoutId==Layout.id:c.layoutId==Layout.id)).ToList();
                Choice(previewBar,"Preview data",new[]{new KeyValuePair<string,string>("","Schema defaults")}.Concat(Options(examples,c=>c.id,c=>c.name)),_previewCardId,
                    id=>{_previewCardId=id;Render();});
                CardDocument example=examples.FirstOrDefault(c=>c.id==_previewCardId);
                SetupPreview(center, Layout, example);
            }
            _inspector = Pane("inspector", 330); _main.Add(_inspector); FillInspector(_inspector);
        }
        void NewLayout(bool back)
        {
            if (Block == null) { EditorUtility.DisplayDialog("No active block", "Create or activate a block first.", "OK"); return; }
            var l = _store.CreateLayout(Block, back ? "New Card Back" : "New Front", back);
            if (back && string.IsNullOrEmpty(Block.defaultBackLayoutId)) { Block.defaultBackLayoutId = l.id; SaveBlock(); }
            _layoutId = l.id; _zoneId = null; _iconStudio = false; _layoutDesignerOpen = true; Render();
        }
        void NewZone(ZoneKind kind)
        {
            if (Layout == null) return;
            var z = new ZoneDefinition { name = kind + " Zone", kind = kind, order = Layout.zones.Count,
                x = 32 + (Layout.zones.Count % 6) * 8, y = 36 + (Layout.zones.Count % 6) * 8,
                width = kind == ZoneKind.Image ? 180 : 130, height = kind == ZoneKind.Image ? 150 : 42 };
            FieldKind wanted = kind == ZoneKind.Number ? FieldKind.Number : kind == ZoneKind.Icon ? FieldKind.Icon :
                kind == ZoneKind.Image ? FieldKind.Image : FieldKind.Text;
            var definition = _store.Schema.fields.FirstOrDefault(f => f.kind == wanted);
            if (kind != ZoneKind.StaticText && definition != null) z.fieldId = definition.id;
            if (kind == ZoneKind.StaticText) z.staticText = "STATIC LABEL";
            Layout.zones.Add(z); _zoneId = z.id; SaveLayout(); Render();
        }
        void FillInspector(VisualElement pane)
        {
            if (pane == null) return;
            UpdateDesignToolState();
            pane.Clear();
            var scroll = Scroll(pane);
            if (_tab == Workspace.Layouts)
            {
                if (_iconStudio) { RenderIconInspector(scroll); return; }
                RenderLayoutInspector(scroll); return;
            }
            if (_tab == Workspace.Database && !_showSchema) RenderCardOverrideInspector(scroll);
        }
        void RenderLayoutInspector(VisualElement scroll)
        {
            if (Layout == null) { scroll.Add(Note("Create a layout to configure card zones.")); return; }
            var settings = new Foldout { text = "Layout settings", value = string.IsNullOrEmpty(_zoneId) };
            settings.AddToClassList("designer-disclosure"); scroll.Add(settings);
            TextBox(settings, "Name", Layout.name, v => { Layout.name = v; SaveLayout(); });
            TextBox(settings, "Layout type", Layout.layoutType, v => { Layout.layoutType = v; SaveLayout(); });
            ToggleBox(settings, "Card back", Layout.isBack, v => { Layout.isBack = v; SaveLayout(); });
            IntBox(settings, "Width", Layout.width, v => { Layout.width = Mathf.Clamp(v, 100, 2048); SaveLayout(); RefreshCanvas(); }).tooltip="Width in design units";
            IntBox(settings, "Height", Layout.height, v => { Layout.height = Mathf.Clamp(v, 100, 2048); SaveLayout(); RefreshCanvas(); }).tooltip="Height in design units";
            ColorBox(settings, "Base color", Layout.backgroundColor, v => { Layout.backgroundColor = v; SaveLayout(); RefreshCanvas(); });
            TextureBox(settings, "Background artwork", Layout.backgroundAssetPath,
                v => { Layout.backgroundAssetPath = v; SaveLayout(); RefreshCanvas(); });
            if (Layout.templates == null) Layout.templates = new List<ZoneTemplateDefinition>();
            var templates = new Foldout { text = "Zone templates", value = false }; settings.Add(templates);
            templates.Add(Note("Reusable zone skins can use JPEGs, PNGs, or sliced backgrounds."));
            templates.Add(Btn("+ Add template", () => { var t = new ZoneTemplateDefinition { name = "Template " + (Layout.templates.Count + 1) }; Layout.templates.Add(t); SaveLayout(); Render(); }, "small-button"));
            foreach (var template in Layout.templates.ToList())
            {
                var local = template; var editor = new Foldout { text = local.name, value = false }; templates.Add(editor);
                TextBox(editor, "Name", local.name, v => { local.name = v; SaveLayout(); Render(); });
                TextureBox(editor, "Background image", local.backgroundAssetPath, v => { local.backgroundAssetPath = v; SaveLayout(); RefreshCanvas(); });
                ColorBox(editor, "Background color", local.backgroundColor, v => { local.backgroundColor = v; SaveLayout(); RefreshCanvas(); });
                Choice(editor, "Image mode", Enum.GetNames(typeof(ZoneImageScaleMode)).Select(x => new KeyValuePair<string,string>(x, x)), local.imageScaleMode.ToString(), v => { Enum.TryParse(v, out local.imageScaleMode); SaveLayout(); RefreshCanvas(); });
                IntBox(editor, "Slice left", local.sliceLeft, v => { local.sliceLeft = Mathf.Max(0, v); SaveLayout(); RefreshCanvas(); });
                IntBox(editor, "Slice right", local.sliceRight, v => { local.sliceRight = Mathf.Max(0, v); SaveLayout(); RefreshCanvas(); });
                IntBox(editor, "Slice top", local.sliceTop, v => { local.sliceTop = Mathf.Max(0, v); SaveLayout(); RefreshCanvas(); });
                IntBox(editor, "Slice bottom", local.sliceBottom, v => { local.sliceBottom = Mathf.Max(0, v); SaveLayout(); RefreshCanvas(); });
                editor.Add(Btn("Remove template", () => { Layout.templates.Remove(local); SaveLayout(); Render(); }, "small-danger"));
            }
            var requirements = new Foldout { text = "Requirements", value = false }; settings.Add(requirements);
            RenderRequirements(requirements, "Layout requirements", Layout.requirements);
            if (!embedded) {
            scroll.Add(Btn("Duplicate this layout", () => { var l = _store.ImportLayout(Block, Layout); _layoutId = l.id; _zoneId = null; Render(); }, "ghost-button"));
            scroll.Add(Btn("Delete layout", () =>
            {
                if (!EditorUtility.DisplayDialog("Delete layout?", "This removes the layout JSON. Assigned cards must be reassigned first.", "Delete", "Cancel")) return;
                try { _store.DeleteLayout(Block, Layout); _layoutId = null; Render(); } catch (Exception e) { Error("Cannot delete layout", e); }
            }, "danger-button"));
            }
            scroll.Add(Title("Zone layers"));
            foreach (var z in Layout.zones.OrderByDescending(z => z.order)) {
                var layer=Btn("",()=> { _zoneId=z.id;FillInspector(_inspector);RefreshCanvas(); },"list-entry");
                layer.AddToClassList("zone-layer-row");layer.EnableInClassList("list-entry-selected",_zoneId==z.id);
                layer.EnableInClassList("zone-layer-row--hidden",!z.visible);
                layer.tooltip=z.name+(z.locked?" (locked)":"")+(!z.visible?" (hidden)":"");
                var name=new Label(z.name) { pickingMode=PickingMode.Ignore };name.AddToClassList("zone-layer-row__name");layer.Add(name);
                var kind=new Label(z.kind==ZoneKind.StaticText?"Label":z.kind.ToString()) { pickingMode=PickingMode.Ignore };kind.AddToClassList("zone-layer-row__kind");layer.Add(kind);
                scroll.Add(layer);
            }
            var zone = Layout.zones.FirstOrDefault(z => z.id == _zoneId);
            if (zone == null) return;
            scroll.Add(new CardForgeZoneEditor(Layout, zone, _store.Schema, _store.Icons, SaveLayout, RefreshCanvas));
            scroll.Add(Title("Selected zone"));
            TextBox(scroll, "Zone name", zone.name, v => { zone.name = v; SaveLayout(); });
            var type = new EnumField("Content type", zone.kind);
            type.RegisterValueChangedCallback(ev => { zone.kind = (ZoneKind)ev.newValue; SaveLayout(); Render(); }); scroll.Add(type);
            if (Layout.templates.Count > 0)
                Choice(scroll, "Zone template", new[] { new KeyValuePair<string,string>("", "No template") }.Concat(Options(Layout.templates, x => x.id, x => x.name)), zone.templateId, v => { zone.templateId = v; SaveLayout(); RefreshCanvas(); });
            if (zone.kind != ZoneKind.StaticText)
            {
                var choices = new List<KeyValuePair<string,string>> { new KeyValuePair<string,string>("", "Unbound / static") };
                choices.AddRange(Options(_store.Schema.fields, f => f.id, f => f.label + " · " + f.kind));
                Choice(scroll, "Data binding", choices, zone.fieldId, v => { zone.fieldId = v; SaveLayout(); RefreshCanvas(); });
            }
            if (zone.kind == ZoneKind.StaticText || string.IsNullOrEmpty(zone.fieldId))
            {
                if (zone.kind == ZoneKind.Icon || zone.kind == ZoneKind.Image)
                {
                    TextureBox(scroll, "Static artwork", zone.staticAssetPath, v => { zone.staticAssetPath = v; SaveLayout(); RefreshCanvas(); });
                    if (zone.kind == ZoneKind.Icon)
                        Choice(scroll, "Designed icon", Options(_store.Icons.Values, i => i.assetPath, i => i.name), zone.staticAssetPath,
                            v => { zone.staticAssetPath = v; SaveLayout(); RefreshCanvas(); });
                }
                else TextBox(scroll, "Static text", zone.staticText, v => { zone.staticText = v; SaveLayout(); RefreshCanvas(); }, true);
            }
            ToggleBox(scroll, "Locked", zone.locked, v => { zone.locked = v; SaveLayout(); RefreshCanvas(); });
            var geometry = Group("GEOMETRY & OFFSETS"); scroll.Add(geometry);
            FloatBox(geometry, "X", zone.x, v => { zone.x = v; SaveLayout(); RefreshCanvas(); });
            FloatBox(geometry, "Y", zone.y, v => { zone.y = v; SaveLayout(); RefreshCanvas(); });
            FloatBox(geometry, "Width", zone.width, v => { zone.width = Mathf.Max(10, v); SaveLayout(); RefreshCanvas(); });
            FloatBox(geometry, "Height", zone.height, v => { zone.height = Mathf.Max(10, v); SaveLayout(); RefreshCanvas(); });
            FloatBox(geometry, "Offset X", zone.offsetX, v => { zone.offsetX = v; SaveLayout(); RefreshCanvas(); });
            FloatBox(geometry, "Offset Y", zone.offsetY, v => { zone.offsetY = v; SaveLayout(); RefreshCanvas(); });
            var typography = Group("STYLE & STACKING"); scroll.Add(typography);
            FloatBox(typography, "Font size", zone.fontSize, v => { zone.fontSize = Mathf.Max(1, v); SaveLayout(); RefreshCanvas(); });
            var currentFont = string.IsNullOrEmpty(zone.fontAssetPath) ? null :
                AssetDatabase.LoadAssetAtPath<Font>(zone.fontAssetPath);
            var fontPicker = new ObjectField("Type family") {
                objectType = typeof(Font), allowSceneObjects = false, value = currentFont
            };
            fontPicker.tooltip = "Choose a font imported with Google Fonts or another Unity TTF/OTF asset";
            fontPicker.RegisterValueChangedCallback(e => {
                zone.fontAssetPath = e.newValue == null ? "" : AssetDatabase.GetAssetPath(e.newValue);
                SaveLayout(); RefreshCanvas();
            });
            typography.Add(fontPicker);
            ToggleBox(typography, "Bold", zone.bold, v => { zone.bold = v; SaveLayout(); RefreshCanvas(); });
            ColorBox(typography, "Foreground", zone.color, v => { zone.color = v; SaveLayout(); RefreshCanvas(); });
            ColorBox(typography, "Background", zone.backgroundColor, v => { zone.backgroundColor = v; SaveLayout(); RefreshCanvas(); });
            Choice(typography, "Alignment", Options(Enum.GetNames(typeof(TextAnchor)), s => s, s => s), zone.alignment,
                v => { zone.alignment = v; SaveLayout(); RefreshCanvas(); });
            ToggleBox(typography, "Visible", zone.visible, v => { zone.visible = v; SaveLayout(); RefreshCanvas(); });
            if (!embedded) {
            var layers = Row("button-row"); typography.Add(layers);
            layers.Add(Btn("Move up", () => ChangeOrder(zone, 1), "ghost-button"));
            layers.Add(Btn("Move down", () => ChangeOrder(zone, -1), "ghost-button"));
            scroll.Add(Btn("Duplicate zone", () =>
            {
                var copy = CardForgeData.Copy(zone); RenewZoneIds(copy); copy.name += " copy";
                copy.x += 12; copy.y += 12; copy.order = Layout.zones.Max(z => z.order) + 1;
                Layout.zones.Add(copy); _zoneId = copy.id; SaveLayout(); Render();
            }, "ghost-button"));
            scroll.Add(Btn("Remove selected zone", () => { Layout.zones.Remove(zone); _zoneId = null; SaveLayout(); Render(); }, "danger-button"));
            }
        }
        void ChangeOrder(ZoneDefinition zone, int direction)
        {
            var sequence = Layout.zones.OrderBy(z => z.order).ToList();
            int at = sequence.IndexOf(zone); int next = at + direction;
            if (next < 0 || next >= sequence.Count) return;
            var other = sequence[next]; int temp = zone.order; zone.order = other.order; other.order = temp;
            SaveLayout(); Render();
        }
        Image _iconImage;
        void RenderIconCenter(VisualElement center)
        {
            var content = Row("icon-center"); center.Add(content);
            if (!embedded) {
            var shelf = Pane("icon-list", 240); content.Add(shelf);
            var scroll = Scroll(shelf);
            scroll.Add(Title("Designed icons"));
            scroll.Add(Btn("Create icon", CreateIcon, "primary-button"));
            foreach (var icon in _store.Icons.Values.OrderBy(x => x.name))
                AddListButton(scroll, icon.name, _iconId == icon.id, () => { _iconId = icon.id; Render(); });
            }
            var view = Col("icon-preview-wrap"); content.Add(view);
            view.Add(Title(Icon == null ? "Create a reusable icon" : Icon.name));
            if (Icon == null && embedded) view.Add(Btn("Create icon", CreateIcon, "primary-button"));
            _iconImage = new Image(); _iconImage.AddToClassList("icon-preview"); view.Add(_iconImage);
            UpdateIconPreview();
        }
        void CreateIcon()
        {
                var icon = new IconDocument { name = "New Icon" };
                icon.layers.Add(new IconLayer { shape = "Circle", color = "#5FD6C1", size = .78f });
                icon.layers.Add(new IconLayer { shape = "Star", color = "#FFFFFF", size = .38f });
                _store.SaveIcon(icon); CardForgeIconPainter.SavePng(icon); _store.SaveIcon(icon);
                _iconId = icon.id; Render();
        }

        void UpdateIconPreview()
        {
            if (_iconPreview != null) UnityEngine.Object.DestroyImmediate(_iconPreview);
            _iconPreview = Icon == null || (Icon.layers.Count == 0 && !string.IsNullOrEmpty(Icon.assetPath))
                ? null : CardForgeIconPainter.Render(Icon, 256);
            if (_iconImage != null) _iconImage.image = _iconPreview != null ? _iconPreview :
                (Icon == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(Icon.assetPath));
        }
        void IconChanged(bool refreshUi = false)
        {
            if (Icon == null) return;
            try
            {
                CardForgeIconPainter.SavePng(Icon); _store.SaveIcon(Icon); TrackHistory();
                UpdateIconPreview(); _notification = "Icon JSON and PNG saved";
                if (refreshUi) Render();
            }
            catch (Exception e) { Error("Icon update failed", e); }
        }
        void RenderIconInspector(VisualElement panel)
        {
            panel.Add(Title("Icon Workshop"));
            if (Icon == null) { panel.Add(Note("Create an icon from the library to start designing.")); return; }
            TextBox(panel, "Icon name", Icon.name, v => { Icon.name = v; IconChanged(); });

            panel.Add(Btn("+ Add shape layer", () =>
            {
                Icon.layers.Add(new IconLayer { shape = "Circle", color = "#E7CFA0", size = .4f });
                IconChanged(true);
            }, "primary-button"));
            for (int i = 0; i < Icon.layers.Count; ++i)
            {
                var layer = Icon.layers[i];
                var g = new Foldout { text = "Layer " + (i + 1) + " · " + layer.shape, value = i == Icon.layers.Count - 1 }; g.AddToClassList("designer-layer"); panel.Add(g);
                Choice(g, "Shape", Options(CardForgeIconPainter.Shapes, s => s, s => s), layer.shape,
                    v => { layer.shape = v; IconChanged(); });
                ColorBox(g, "Fill", layer.color, v => { layer.color = v; IconChanged(); });
                FloatBox(g, "Position X (0–1)", layer.x, v => { layer.x = Mathf.Clamp01(v); IconChanged(); });
                FloatBox(g, "Position Y (0–1)", layer.y, v => { layer.y = Mathf.Clamp01(v); IconChanged(); });
                FloatBox(g, "Size", layer.size, v => { layer.size = Mathf.Clamp(v, .01f, 2f); IconChanged(); });
                FloatBox(g, "Rotation", layer.rotation, v => { layer.rotation = v; IconChanged(); });
                ToggleBox(g, "Visible", layer.visible, v => { layer.visible = v; IconChanged(); });
                FloatBox(g, "Opacity", layer.opacity, v => { layer.opacity = Mathf.Clamp01(v); IconChanged(); });
                var local = layer;
                var layerTools = new ToolbarMenu { text = "Layer actions" }; g.Add(layerTools);
                MenuAction(layerTools, "Duplicate", () => { Icon.layers.Insert(Icon.layers.IndexOf(local) + 1, CardForgeData.Copy(local)); IconChanged(true); });
                MenuAction(layerTools, "Bring forward", () => { int at = Icon.layers.IndexOf(local); Icon.layers.RemoveAt(at); Icon.layers.Insert(at + 1, local); IconChanged(true); }, () => Icon.layers.IndexOf(local) < Icon.layers.Count - 1);
                MenuAction(layerTools, "Send backward", () => { int at = Icon.layers.IndexOf(local); Icon.layers.RemoveAt(at); Icon.layers.Insert(at - 1, local); IconChanged(true); }, () => Icon.layers.IndexOf(local) > 0);
                layerTools.menu.AppendSeparator();
                MenuAction(layerTools, "Remove", () => { Icon.layers.Remove(local); IconChanged(true); });
            }


        }

        void RenderDatabase()
        {
            if (!_showSchema && !_databaseEditorOpen) { RenderDatabaseIndex(); return; }
            if (embedded && _showSchema) {
                var definitions = Pane("sidebar", 230); _main.Add(definitions);
                if (_fieldId == null) _fieldId = _store.Schema.fields.FirstOrDefault()?.id;
                RenderSchemaList(Scroll(definitions)); RenderSchemaWorkspace(); return;
            }
            if (!embedded) {
            var left = Pane("sidebar", 265); _main.Add(left);
            var lbody = Scroll(left);
            lbody.Add(Kicker("PROJECT")); ChooseBlock(lbody);
            var switcher = Row("button-row"); lbody.Add(switcher);
            switcher.Add(Btn("Cards", () => { _showSchema = false; Render(); }, _showSchema ? "ghost-button" : "primary-button"));
            switcher.Add(Btn("Data Fields", () => { _showSchema = true; Render(); }, _showSchema ? "primary-button" : "ghost-button"));
            if (_showSchema) { RenderSchemaList(lbody); RenderSchemaWorkspace(); return; }
            lbody.Add(Title("Card library"));
            lbody.Add(Btn("+ New card", () =>
            {
                var face = Block.layoutIds.Select(id => _store.Layouts.TryGetValue(id, out var l) ? l : null)
                    .FirstOrDefault(l => l != null && !l.isBack);
                if (face == null) { EditorUtility.DisplayDialog("Create a front layout", "Add a front layout in Layout Designer first.", "OK"); return; }
                var c = _store.CreateCard(Block, face.id); _cardId = c.id; _zoneId = null; _databaseEditorOpen = true; Render();
            }, "primary-button"));
            foreach (var card in Block.cardIds.Select(id => _store.Cards.TryGetValue(id, out var c) ? c : null).Where(c => c != null))
                AddListButton(lbody, card.cardNumber + "  " + card.name, _cardId == card.id,
                    () => { _cardId = card.id; _zoneId = null; _databaseEditorOpen = true; Render(); },
                    !string.IsNullOrEmpty(card.sourceCardId) ? "REPRINT  ·  " + card.sourceCardId.Substring(0, Math.Min(8, card.sourceCardId.Length)) : null);

            }
            var center = Pane("center-pane"); _main.Add(center);
            if (Card == null)
            {
                center.Add(Title("Create or select a card"));
                _main.Add(Pane("inspector", 330)); return;
            }
            if (!embedded) {
            var bar = Row("content-bar"); center.Add(bar);
            bar.Add(Title(Card.name)); bar.Add(Flex());
            bar.Insert(1, CardForgeUi.Back("Cards", () => { _databaseEditorOpen = false; Render(); }));
            bar.Add(Btn("Duplicate", () =>
            {
                var copy = CardForgeData.Copy(Card); copy.id = CardForgeData.NewId(); copy.cardNumber += "-COPY";
                Block.cardIds.Add(copy.id); _store.SaveCard(Block.id, copy); SaveBlock(); _cardId = copy.id; Render();
            }, "small-button"));
            bar.Add(Btn("Delete", () =>
            {
                if (EditorUtility.DisplayDialog("Delete card?", "Remove this card JSON from the selected block?", "Delete", "Cancel"))
                { _store.DeleteCard(Block, Card); _cardId = null; _zoneId = null; Render(); }
            }, "small-danger"));
            }
            var content = Row("database-content"); center.Add(content);
            var form = Pane("data-form", 320); content.Add(form);
            var fields = Scroll(form);
            fields.Add(Title("Card details"));

            TextBox(fields, "Card name", Card.name, v => { Card.name = v; SaveCard(); });
            TextBox(fields, "Collector number", Card.cardNumber, v => { Card.cardNumber = v; SaveCard(); });
            TextBox(fields, "Card type", Card.cardType, v => { Card.cardType = v; SaveCard(); });
            TextBox(fields, "Card variant", Card.variantId, v => { Card.variantId = v; SaveCard(); });
            RenderCardRecordDetails(fields);
            RenderCardRuleLinks(fields);
            var requirements = new Foldout { text = "Requirements", value = false }; fields.Add(requirements);
            RenderRequirements(requirements, "Card requirements", Card.requirements);
            Choice(fields, "Front layout", Options(Block.layoutIds.Select(id => _store.Layouts.TryGetValue(id, out var l) ? l : null)
                .Where(l => l != null && !l.isBack), l => l.id, l => l.name), Card.layoutId,
                v => { Card.layoutId = v; Card.zoneOverrides.Clear(); SaveCard(); Render(); });
            var backs = new List<KeyValuePair<string,string>> { new KeyValuePair<string,string>("", "No card back") };
            backs.AddRange(Options(Block.layoutIds.Select(id => _store.Layouts.TryGetValue(id, out var l) ? l : null)
                .Where(l => l != null && l.isBack), l => l.id, l => l.name));
            Choice(fields, "Card back", backs, Card.backLayoutId,
                v => { Card.backLayoutId = v; SaveCard(); });
            RenderCardArtCatalog(fields);
            if (!string.IsNullOrEmpty(Card.sourceCardId))
                fields.Add(Note("Reprint source: " + Card.sourceBlockId + " / " + Card.sourceCardId));
            fields.Add(Title("Assigned data"));
            foreach (var definition in _store.Schema.fields)
            {
                var f = definition;
                var value = CardForgeData.Value(Card, f);
                switch (f.kind)
                {
                    case FieldKind.Boolean:
                        ToggleBox(fields, f.label, string.Equals(value, "true", StringComparison.OrdinalIgnoreCase),
                            v => { CardForgeData.SetValue(Card, f.id, v ? "true" : "false"); SaveCard(); RefreshCanvas(); });
                        break;
                    case FieldKind.Number:
                        TextBox(fields, f.label + " (number)", value, v =>
                        {
                            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                            { _notification = "Invalid numeric value for " + f.label; return; }
                            CardForgeData.SetValue(Card, f.id, v); SaveCard(); RefreshCanvas();
                        }); break;
                    case FieldKind.Image:
                        TextureBox(fields, f.label, value, v => { CardForgeData.SetValue(Card, f.id, v); SaveCard(); RefreshCanvas(); });
                        break;
                    case FieldKind.Icon:
                        Choice(fields, f.label + " (designed)",
                            new [] { new KeyValuePair<string,string>("", "No icon") }
                                .Concat(Options(_store.Icons.Values.OrderBy(i => i.name), i => i.assetPath, i => i.name)),
                            value, v => { CardForgeData.SetValue(Card, f.id, v); SaveCard(); RefreshCanvas(); });
                        TextureBox(fields, f.label + " (custom)", value,
                            v => { CardForgeData.SetValue(Card, f.id, v); SaveCard(); RefreshCanvas(); });
                        break;
                    default:
                        TextBox(fields, f.label, value,
                            v => { CardForgeData.SetValue(Card, f.id, v); SaveCard(); RefreshCanvas(); }, true);
                        break;
                }
            }
            var preview = Pane("card-preview-pane"); content.Add(preview);
            var tools = Row("preview-bar"); preview.Add(tools);
            tools.Add(Flex());
            tools.Add(Btn(_previewBack ? "Show front" : "Show back", () =>
            { _previewBack = !_previewBack; _zoneId = null; Render(); }, "small-button"));
            var live = new Toggle("Edit overrides") { value = _liveCardEdit };
            live.RegisterValueChangedCallback(ev => { _liveCardEdit = ev.newValue; Render(); });
            tools.Add(live);
            var previewLayoutId = _previewBack ? Card.backLayoutId : Card.layoutId;
            var selectedLayout = _store.Layouts.TryGetValue(previewLayoutId ?? "", out var chosen) ? chosen : null;
            SetupPreview(preview, selectedLayout, Card);
            _inspector = null;
            if (!embedded || _liveCardEdit) { _inspector = Pane("inspector", 318); _main.Add(_inspector); FillInspector(_inspector); }
        }
        void RenderCardOverrideInspector(VisualElement panel)
        {
            panel.Add(Title("Per-card overrides"));
            panel.Add(Note("Only this card changes. The shared layout and other cards remain unchanged."));
            if (Card == null) return;
            if (!_liveCardEdit) { panel.Add(Note("Enable 'Edit this card visually' to reposition or restyle zones for this card.")); return; }
            if (_drawLayout == null) { panel.Add(Note("Assign a front layout first.")); return; }
            var zone = _drawLayout.zones.FirstOrDefault(z => z.id == _zoneId);
            if (zone == null) { panel.Add(Note("Click a zone on the card to edit its own overrides.")); return; }
            panel.Add(Title(zone.name));
            var ov = CardForgeData.EnsureOverride(Card, zone.id);
            FloatBox(panel, "Extra offset X", ov.deltaX, v => { ov.deltaX = v; SaveCard(); RefreshCanvas(); });
            FloatBox(panel, "Extra offset Y", ov.deltaY, v => { ov.deltaY = v; SaveCard(); RefreshCanvas(); });
            FloatBox(panel, "Width adjustment", ov.deltaWidth, v => { ov.deltaWidth = v; SaveCard(); RefreshCanvas(); });
            FloatBox(panel, "Height adjustment", ov.deltaHeight, v => { ov.deltaHeight = v; SaveCard(); RefreshCanvas(); });
            FloatBox(panel, "Font size (0 = inherited)", ov.fontSize, v => { ov.fontSize = Mathf.Max(0, v); SaveCard(); RefreshCanvas(); });
            ColorBox(panel, "Text color", string.IsNullOrEmpty(ov.color) ? zone.color : ov.color,
                v => { ov.color = v; SaveCard(); RefreshCanvas(); });
            if (zone.kind == ZoneKind.Text || zone.kind == ZoneKind.Number || zone.kind == ZoneKind.StaticText)
                TextBox(panel, "Literal text override (optional)", ov.textOverride,
                    v => { ov.textOverride = v; SaveCard(); RefreshCanvas(); }, true);
            else TextureBox(panel, "Image override (optional)", ov.assetPathOverride,
                v => { ov.assetPathOverride = v; SaveCard(); RefreshCanvas(); });
            ToggleBox(panel, "Hide on this card", ov.hidden, v => { ov.hidden = v; SaveCard(); RefreshCanvas(); });
            panel.Add(Btn("Reset this zone to layout", () =>
            {
                Card.zoneOverrides.RemoveAll(o => o.zoneId == zone.id); SaveCard(); FillInspector(_inspector); RefreshCanvas();
            }, "ghost-button"));
            panel.Add(Btn("Reset ALL card overrides", () =>
            {
                if (!EditorUtility.DisplayDialog("Reset this card?", "Remove every individual zone override on this card?", "Reset", "Cancel")) return;
                Card.zoneOverrides.Clear(); SaveCard(); FillInspector(_inspector); RefreshCanvas();
            }, "danger-button"));
        }
        string _fieldId;
        void RenderSchemaList(VisualElement panel)
        {
            panel.Add(Title("Data definitions"));
            panel.Add(Btn("+ New data field", () =>
            {
                var field = new FieldDefinition { key = "field_" + (_store.Schema.fields.Count + 1) };
                _store.Schema.fields.Add(field); _fieldId = field.id; SaveSchema(); Render();
            }, "primary-button"));
            foreach (var field in _store.Schema.fields)
                AddListButton(panel, field.label + "  [" + field.kind + "]", _fieldId == field.id,
                    () => { _fieldId = field.id; Render(); }, field.key);
        }
        void RenderSchemaWorkspace()
        {
            var center = Pane("center-pane"); _main.Add(center);
            VisualElement pane;
            if (embedded) pane = Scroll(center);
            else {
            var cscroll = Scroll(center);
            cscroll.Add(Title("Schema & field types"));
            cscroll.Add(Note("Define fields once and reuse them across every block. Stable IDs keep bindings intact when you rename a field."));
            cscroll.Add(Kicker("SUPPORTED TYPES"));
            cscroll.Add(Note("Text  ·  Number  ·  Icon  ·  Image  ·  Boolean"));
            var inspector = Pane("inspector", 370); _main.Add(inspector);
            pane = Scroll(inspector);
            }
            var field = _store.Schema.fields.FirstOrDefault(x => x.id == _fieldId);
            if (field == null) { pane.Add(Note("Select a data definition on the left.")); return; }
            pane.Add(Title("Field definition"));
            TextBox(pane, "Key", field.key, v => { field.key = v; SaveSchema(); });
            TextBox(pane, "Display name", field.label, v => { field.label = v; SaveSchema(); });
            var kind = new EnumField("Data type", field.kind);
            kind.RegisterValueChangedCallback(ev => { field.kind = (FieldKind)ev.newValue; SaveSchema(); Render(); }); pane.Add(kind);
            TextBox(pane, "Default value", field.defaultValue, v => { field.defaultValue = v; SaveSchema(); });
            TextBox(pane, "Help / notes", field.help, v => { field.help = v; SaveSchema(); }, true);
            if (field.kind == FieldKind.Number)
            {
                IntBox(pane, "Decimal places", field.decimalPlaces, v => { field.decimalPlaces = Mathf.Clamp(v, 0, 8); SaveSchema(); });
                TextBox(pane, "Prefix", field.prefix, v => { field.prefix = v; SaveSchema(); });
                TextBox(pane, "Suffix", field.suffix, v => { field.suffix = v; SaveSchema(); });
            }
            pane.Add(Btn("Remove field", () =>
            {
                bool inUse = _store.Layouts.Values.Any(l => l.zones.Any(z => z.fieldId == field.id));
                if (inUse) { EditorUtility.DisplayDialog("Field in use", "Unbind this field from all layout zones before deleting it.", "OK"); return; }
                if (!EditorUtility.DisplayDialog("Remove field?", "Card values associated with the field will no longer be editable.", "Remove", "Cancel")) return;
                _store.Schema.fields.Remove(field); _fieldId = null; SaveSchema(); Render();
            }, "danger-button"));
        }
        string _newBlockName = "Expansion Set", _newBlockCode = "EXP";
        string _importLayoutId = "", _reprintCardId = "";
        BuildTarget _exportTarget = BuildTarget.StandaloneWindows64;
        void RenderBlocks()
        {
            if (!_blockEditorOpen) { RenderBlockIndex(); return; }
            if (!embedded) {
            var left = Pane("sidebar", 265); _main.Add(left);
            var lbody = Scroll(left);
            lbody.Add(Kicker("LIBRARY")); lbody.Add(Title("Blocks & editions"));
            foreach (var b in _store.Blocks.Values.OrderBy(b => b.name))
                AddListButton(lbody, b.code + "  " + b.name, b.id == _blockId,
                    () => { _blockId = b.id; _layoutId = null; _cardId = null; _blockEditorOpen = true; Render(); },
                    b.cardIds.Count + " cards  ·  " + b.layoutIds.Count + " layouts");
            lbody.Add(Title("Create block"));
            TextBox(lbody, "New block name", _newBlockName, v => _newBlockName = v);
            TextBox(lbody, "New block code", _newBlockCode, v => _newBlockCode = v);
            lbody.Add(Btn("+ Create blank block", () =>
            {
                if (string.IsNullOrWhiteSpace(_newBlockName) || string.IsNullOrWhiteSpace(_newBlockCode)) return;
                var b = _store.CreateBlock(_newBlockName.Trim(), _newBlockCode.Trim());
                _blockId = b.id; _layoutId = null; _cardId = null; _blockEditorOpen = true; Render();
            }, "primary-button"));
            ToggleBox(lbody, "Include cards in clone", _cloneCards, v => _cloneCards = v);
            lbody.Add(Btn("Clone selected block", () =>
            {
                if (Block == null) return;
                var clone = _store.CloneBlock(Block, _newBlockName, _newBlockCode, _cloneCards);
                _blockId = clone.id; _layoutId = clone.layoutIds.FirstOrDefault();
                _cardId = clone.cardIds.FirstOrDefault(); Render();
            }, "ghost-button"));

            }
            var center = Pane("center-pane"); _main.Add(center);
            var cscroll = Scroll(center);
            if (Block == null) return;
            if (!embedded) cscroll.Add(CardForgeUi.Back("Blocks", () => { _blockEditorOpen = false; Render(); }));
            cscroll.Add(Title("Block Builder"));
            cscroll.Add(Note("A block owns multiple front/back layouts, independent card records and optional reprints."));
            var properties = Group("BLOCK DETAILS"); cscroll.Add(properties);
            TextBox(properties, "Block name", Block.name, v => { Block.name = v; SaveBlock(); });
            TextBox(properties, "Set code", Block.code, v => { Block.code = v; SaveBlock(); });
            TextBox(properties, "Description", Block.description, v => { Block.description = v; SaveBlock(); }, true);
            var layoutGroup = Group("ASSIGNED LAYOUTS"); cscroll.Add(layoutGroup);
            foreach (var id in Block.layoutIds.ToList())
            {
                if (!_store.Layouts.TryGetValue(id, out var layout)) continue;
                var localId = id;
                var r = Row("catalog-row"); layoutGroup.Add(r);
                r.Add(new Label((layout.isBack ? "BACK" : "FRONT") + "  ·  " + layout.name)); r.Add(Flex());
                r.Add(Btn("Edit", () => { _layoutId = localId; _iconStudio = false; _layoutDesignerOpen = true; _tab = Workspace.Layouts; Render(); }, "small-button"));
            }
            var buttons = Row("button-row"); layoutGroup.Add(buttons);
            buttons.Add(Btn("+ Front layout", () => { var l = _store.CreateLayout(Block, "New Front", false); _layoutId = l.id; _layoutDesignerOpen = true; _tab = Workspace.Layouts; Render(); }, "ghost-button"));
            buttons.Add(Btn("+ Card back", () =>
            {
                var l = _store.CreateLayout(Block, "New Back", true);
                if (string.IsNullOrEmpty(Block.defaultBackLayoutId)) { Block.defaultBackLayoutId = l.id; SaveBlock(); }
                _layoutId = l.id; _layoutDesignerOpen = true; _tab = Workspace.Layouts; Render();
            }, "ghost-button"));
            var backs = new List<KeyValuePair<string,string>> { new KeyValuePair<string,string>("", "None") };
            backs.AddRange(Options(Block.layoutIds.Select(id => _store.Layouts.TryGetValue(id, out var l) ? l : null)
                .Where(x => x != null && x.isBack), x => x.id, x => x.name));
            Choice(layoutGroup, "Default card back", backs, Block.defaultBackLayoutId,
                id => { Block.defaultBackLayoutId = id; SaveBlock(); });
            var cardGroup = Group("CARDS IN THIS BLOCK"); cscroll.Add(cardGroup);
            foreach (var id in Block.cardIds.ToList())
            {
                if (!_store.Cards.TryGetValue(id, out var card)) continue;
                var localId = id;
                var r = Row("catalog-row"); cardGroup.Add(r);
                r.Add(new Label(card.cardNumber + "  ·  " + card.name + (string.IsNullOrEmpty(card.sourceCardId) ? "" : "   [REPRINT]")));
                r.Add(Flex());
                r.Add(Btn("Edit", () => { _cardId = localId; _showSchema = false; _databaseEditorOpen = true; _tab = Workspace.Database; Render(); }, "small-button"));
            }
            if (Block.cardIds.Count == 0) cardGroup.Add(Note("No cards yet. Create one from the Cards library."));
            var warnings = _store.Validate(Block);
            cscroll.Add(Title("Export readiness"));
            if (warnings.Count == 0) cscroll.Add(Note("Ready: all linked layouts and assets are valid."));
            else foreach (var warning in warnings) cscroll.Add(new Label("• " + warning) { name = "warning-text" });

            var right = Pane("inspector", 345); _main.Add(right);
            var rscroll = Scroll(right);
            rscroll.Add(Title("Import & reprint"));
            rscroll.Add(Note("Importing a layout copies its zones. Reprinting duplicates the card JSON and records its origin."));
            var otherLayouts = _store.Blocks.Values.Where(b => b.id != Block.id)
                .SelectMany(b => b.layoutIds.Select(id => new { block = b, layout = _store.Layouts.TryGetValue(id, out var l) ? l : null }))
                .Where(x => x.layout != null).ToList();
            Choice(rscroll, "Source layout", Options(otherLayouts, x => x.layout.id,
                x => x.block.code + " / " + x.layout.name), _importLayoutId, id => _importLayoutId = id);
            rscroll.Add(Btn("Import selected layout", () =>
            {
                if (string.IsNullOrEmpty(_importLayoutId) && otherLayouts.Count > 0) _importLayoutId = otherLayouts[0].layout.id;
                if (!_store.Layouts.TryGetValue(_importLayoutId, out var original)) return;
                var l = _store.ImportLayout(Block, original); _layoutId = l.id; Render();
            }, "ghost-button"));
            var otherCards = _store.Blocks.Values.Where(b => b.id != Block.id)
                .SelectMany(b => b.cardIds.Select(id => new { block = b, card = _store.Cards.TryGetValue(id, out var c) ? c : null }))
                .Where(x => x.card != null).ToList();
            rscroll.Add(Title("Reprints"));
            Choice(rscroll, "Source card", Options(otherCards, x => x.card.id,
                x => x.block.code + " / " + x.card.cardNumber + " " + x.card.name), _reprintCardId, id => _reprintCardId = id);
            rscroll.Add(Btn("Bring card into this block", () =>
            {
                if (string.IsNullOrEmpty(_reprintCardId) && otherCards.Count > 0) _reprintCardId = otherCards[0].card.id;
                var source = otherCards.FirstOrDefault(x => x.card.id == _reprintCardId);
                if (source == null) return;
                var card = _store.Reprint(Block, source.block, source.card);
                _cardId = card.id; _databaseEditorOpen = true; _tab = Workspace.Database; _showSchema = false; Render();
            }, "primary-button"));
            rscroll.Add(Title("Build & export"));
            var exportTargets = new [] { BuildTarget.StandaloneWindows64, BuildTarget.Android,
                BuildTarget.StandaloneLinux64, BuildTarget.StandaloneOSX, BuildTarget.iOS };
            Choice(rscroll, "Target platform", Options(exportTargets, t => t.ToString(), t => t.ToString()),
                _exportTarget.ToString(), v => { if (Enum.TryParse(v, out BuildTarget target)) _exportTarget = target; });
            rscroll.Add(Note("AssetBundles are platform-specific; choose the target matching your game build."));
            rscroll.Add(Btn("Build this block bundle", () =>
            {
                try
                {
                    string folder = _store.ExportBlock(Block, _exportTarget);
                    _notification = "Bundle exported";
                    EditorUtility.RevealInFinder(folder);
                    EditorUtility.DisplayDialog("Block exported", "AssetBundle and portable JSON files:\n" + folder, "OK");
                    Render();
                }
                catch (Exception e) { Error("Build failed", e); }
            }, "primary-button"));
            if (!embedded) {
            rscroll.Add(Btn("Build ALL blocks (active target)", () =>
            {
                try
                {
                    CardForgeBatchBuild.BuildAllActive();
                    EditorUtility.DisplayDialog("All blocks built", "Each block has its own AssetBundle and JSON export folder.", "OK");
                }
                catch (Exception e) { Error("Batch export failed", e); }
            }, "ghost-button"));
            rscroll.Add(Btn("Open data directory", () =>
                EditorUtility.RevealInFinder(System.IO.Path.GetFullPath(CardForgeStore.DataRoot)), "ghost-button"));
            }
            rscroll.Add(Note("Bundle contents: block.json, schema.json, per-layout/per-card JSON, designed icon JSON, referenced textures, and export-manifest.json. JSON files are also written beside the bundle."));
        }
    }
}
