using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    #if UNITY_6000_5_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    #endif
    public sealed partial class CardForgeDesignerView
    {
        readonly Stack<string> undo = new Stack<string>();
        readonly Stack<string> redo = new Stack<string>();
        string historyKey, lastState;
        bool restoring, _snap = true, _showGrid = true, _previewVariants;
        string _previewCardId;
        float _gridSize = 8;
        Button undoButton, redoButton;
        static List<ZoneDefinition> clipboard = new List<ZoneDefinition>();
        object EditedDocument => _showSchema ? (object)_store.Schema : _iconStudio ? Icon :
            _tab == Workspace.Layouts ? (object)Layout : _tab == Workspace.Database ? Card :
            _tab == Workspace.TurnFlow ? (object)Flow : _tab == Workspace.Triggers ? Trigger : _tab == Workspace.Actions ? Action :
            _tab == Workspace.Abilities ? (_keywordEditorOpen ? (object)Keyword : Ability) : Block;
        /// <summary>Commits the active control and explicitly persists the current document.</summary>
        public void SaveDocument()
        {
            // Delayed fields commit on focus loss, before the JSON document is written.
            Focus();
            if (EditedDocument == null) return;
            if (_tab == Workspace.Database && _showSchema) SaveSchema();
            else if (_tab == Workspace.Layouts && _iconStudio) IconChanged();
            else switch (_tab) {
                case Workspace.Layouts: SaveLayout(); break;
                case Workspace.Database: SaveCard(); break;
                case Workspace.Blocks: SaveBlock(); break;
                case Workspace.TurnFlow: SaveFlow(); break;
                case Workspace.Triggers: SaveTrigger(); break;
                case Workspace.Actions: SaveAction(); break;
                case Workspace.Abilities: if (_keywordEditorOpen) SaveKeyword(); else SaveAbility(); break;
            }
            ContextChanged?.Invoke();
        }
        void SyncHistory()
        {
            string key = _showSchema ? "schema" : _iconStudio ? _iconId : _tab == Workspace.Layouts ? _layoutId :
                _tab == Workspace.Database ? _cardId : _tab == Workspace.TurnFlow ? _flowId :
                _tab == Workspace.Triggers ? _triggerId : _tab == Workspace.Actions ? _actionId : _tab == Workspace.Abilities ? _abilityId : _blockId;
            if (key == historyKey) return;
            historyKey = key; undo.Clear(); redo.Clear(); lastState = EditedDocument == null ? null : JsonUtility.ToJson(EditedDocument);
        }
        void TrackHistory()
        {
            if (restoring || (_canvas != null && _canvas.ApplyingTransforms) || EditedDocument == null) return;
            SyncHistory();
            string state = JsonUtility.ToJson(EditedDocument);
            if (lastState != null && lastState != state) { undo.Push(lastState); redo.Clear(); }
            lastState = state;
            ContextChanged?.Invoke();
            undoButton?.SetEnabled(undo.Count > 0); redoButton?.SetEnabled(redo.Count > 0);
        }
        void Restore(bool forward)
        {
            var source = forward ? redo : undo; var target = forward ? undo : redo;
            if (source.Count == 0 || EditedDocument == null) return;
            target.Push(JsonUtility.ToJson(EditedDocument));
            lastState = source.Pop(); restoring = true;
            try
            {
                JsonUtility.FromJsonOverwrite(lastState, EditedDocument);
                if (_showSchema) SaveSchema(); else if (_iconStudio) IconChanged();
                else switch (_tab) {
                    case Workspace.Layouts: SaveLayout(); break;
                    case Workspace.Database: SaveCard(); break;
                    case Workspace.Blocks: SaveBlock(); break;
                    case Workspace.TurnFlow: SaveFlow(); break;
                    case Workspace.Triggers: SaveTrigger(); break;
                    case Workspace.Actions: SaveAction(); break;
                    case Workspace.Abilities: SaveAbility(); break;
                }
            }
            finally { restoring = false; }
            Render();
        }
        ToolbarMenu arrangeMenu;
        static void MenuAction(ToolbarMenu menu, string label, System.Action action, Func<bool> enabled = null, Func<bool> selected = null)
        {
            menu.menu.AppendAction(label, _ => action(), _ => enabled != null && !enabled()
                ? DropdownMenuAction.Status.Disabled : selected != null && selected()
                ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
        }
        void AddDesignToolbar()
        {
            var tools = Row("design-toolbar"); Add(tools);
            bool layoutTools = _tab == Workspace.Layouts && !_iconStudio && Layout != null;
            if (embedded && layoutTools) {
                var add = new ToolbarMenu { text = "Add zone" }; add.AddToClassList("design-primary-menu");
                foreach (ZoneKind kind in Enum.GetValues(typeof(ZoneKind))) {
                    var value = kind; MenuAction(add, kind == ZoneKind.StaticText ? "Label" : kind.ToString(), () => NewZone(value));
                }
                add.SetEnabled(!_previewVariants); tools.Add(add);
            }
            undoButton = Btn("Undo", () => Restore(false), "small-button");
            redoButton = Btn("Redo", () => Restore(true), "small-button");
            undoButton.tooltip = "Undo (Ctrl/Cmd+Z)"; redoButton.tooltip = "Redo (Ctrl/Cmd+Shift+Z)";
            tools.Add(undoButton); tools.Add(redoButton);
            undoButton.SetEnabled(undo.Count > 0); redoButton.SetEnabled(redo.Count > 0);
            arrangeMenu = null;
            if (layoutTools) {
                var edit = new ToolbarMenu { text = "Edit" };
                MenuAction(edit, "Copy", CopyZones, () => !_previewVariants && SelectedZones().Count > 0);
                MenuAction(edit, "Paste", PasteZones, () => !_previewVariants && clipboard.Count > 0);
                MenuAction(edit, "Duplicate", () => { CopyZones(); PasteZones(); }, () => !_previewVariants && SelectedZones().Count > 0);
                edit.menu.AppendSeparator();
                MenuAction(edit, "Lock / unlock", () => EditZones(z => z.locked = !z.locked), () => !_previewVariants && SelectedZones().Count > 0);
                MenuAction(edit, "Show / hide", () => EditZones(z => z.visible = !z.visible), () => !_previewVariants && SelectedZones().Count > 0);
                MenuAction(edit, "Delete", DeleteZones, () => !_previewVariants && SelectedZones().Any(z => !z.locked));
                tools.Add(edit);
                arrangeMenu = new ToolbarMenu { text = "Arrange" };
                foreach (var mode in new[] { "Left", "Center X", "Right", "Top", "Center Y", "Bottom", "Distribute X", "Distribute Y", "Match width", "Match height", "Bring to front", "Send to back" }) {
                    string action = mode;
                    MenuAction(arrangeMenu, mode, () => Arrange(action), () => !_previewVariants && SelectedZones().Count(z => !z.locked) >= (action.StartsWith("Distribute") ? 3 : 1));
                }
                tools.Add(arrangeMenu);
                var view = new ToolbarMenu { text = "View" };
                MenuAction(view, "Fit card", FitCanvas);
                MenuAction(view, "Actual size (100%)", () => { _zoom = 1; Render(); });
                view.menu.AppendSeparator();
                MenuAction(view, "Show grid", () => { _showGrid = !_showGrid; RefreshCanvas(); }, selected: () => _showGrid);
                MenuAction(view, "Snap to grid", () => { _snap = !_snap; RefreshCanvas(); }, selected: () => _snap);
                foreach (var step in new[] { 4, 8, 16, 32 }) {
                    var spacing = step; MenuAction(view, "Grid spacing/" + spacing + " px", () => { _gridSize = spacing; RefreshCanvas(); }, selected: () => _gridSize == spacing);
                }
                MenuAction(view, "Preview variants", () => { _previewVariants = !_previewVariants; Render(); }, selected: () => _previewVariants);
                tools.Add(view);
            }
            if (embedded && _tab == Workspace.TurnFlow && Flow != null) {
                tools.Add(Btn("Add phase", AddFlowNode, "primary-button"));
                var diagram = new ToolbarMenu { text = "Diagram" };
                MenuAction(diagram, "Validate", () => EditorUtility.DisplayDialog("Turn flow", ValidateFlow(), "OK"));
                MenuAction(diagram, "Auto arrange", AutoArrangeFlow); tools.Add(diagram);
            }
            if (embedded && _tab == Workspace.Database && Card != null && !_showSchema)
            {
                var cards = (Block?.cardIds ?? new List<string>()).Where(_store.Cards.ContainsKey).ToList();
                int at = cards.IndexOf(_cardId);
                var previous = Btn("◀ Previous", () => SwitchCard(-1), "small-button");
                previous.SetEnabled(at > 0);
                previous.tooltip = "Previous card in this edition";
                tools.Add(previous);
                var position = Note(cards.Count == 0 ? "No cards" : (at + 1) + " / " + cards.Count);
                position.AddToClassList("cf-card-position"); tools.Add(position);
                var next = Btn("Next ▶", () => SwitchCard(1), "small-button");
                next.SetEnabled(at >= 0 && at < cards.Count - 1);
                next.tooltip = "Next card in this edition"; tools.Add(next);
            }
            if (layoutTools || (_tab == Workspace.Database && Card != null && !_showSchema)) {
                var file = new ToolbarMenu { text = "Export" };
                MenuAction(file, "SVG image…", () => CardForgeSvgExporter.Export(_tab == Workspace.Layouts ? Layout : _drawLayout, _tab == Workspace.Layouts ? null : Card, _store.Schema));
                tools.Add(file);
            }
            UpdateDesignToolState();
        }
        void UpdateDesignToolState() { arrangeMenu?.SetEnabled(!_previewVariants && SelectedZones().Any(z => !z.locked)); }
        void SwitchCard(int step)
        {
            if (Block == null) return;
            var cards = Block.cardIds.Where(_store.Cards.ContainsKey).ToList();
            int selected = cards.IndexOf(_cardId);
            int next = selected + step;
            if (selected < 0 || next < 0 || next >= cards.Count) return;
            Focus();
            SaveCard();
            _cardId = cards[next]; _zoneId = null; _previewBack = false;
            Render();
        }
        void FitCanvas()
        {
            if (_canvas == null || Layout == null) return;
            float availableWidth = _canvas.resolvedStyle.width, availableHeight = _canvas.resolvedStyle.height;
            if (float.IsNaN(availableWidth) || float.IsNaN(availableHeight)) return;
            _zoom = Mathf.Clamp(Mathf.Min((availableWidth - 130) / Layout.width, (availableHeight - 130) / Layout.height), .2f, 2.5f);
            Render();
        }
        List<ZoneDefinition> SelectedZones() => Layout == null ? new List<ZoneDefinition>() :
            Layout.zones.Where(z => _canvas != null && _canvas.Selection.Count > 0 ? _canvas.Selection.Contains(z.id) : z.id == _zoneId).ToList();
        static void RenewZoneIds(ZoneDefinition zone)
        {
            zone.id = CardForgeData.NewId();
            var map = new Dictionary<string,string>();
            foreach (var l in zone.layers) { string old = l.id; l.id = CardForgeData.NewId(); map[old] = l.id; }
            foreach (var v in zone.variants) {
                v.id = CardForgeData.NewId(); if (map.TryGetValue(v.layerId ?? "", out var id)) v.layerId = id;
                foreach (var r in v.requirements) r.id = CardForgeData.NewId();
            }
        }
        void CopyZones() { clipboard = SelectedZones().Select(CardForgeData.Copy).ToList(); }
        void PasteZones()
        {
            if (Layout == null || clipboard.Count == 0) return;
            int order = Layout.zones.Count == 0 ? 0 : Layout.zones.Max(z => z.order) + 1;
            foreach (var item in clipboard) { var z = CardForgeData.Copy(item); RenewZoneIds(z); z.name += " copy"; z.x += 12; z.y += 12; z.order = order++; Layout.zones.Add(z); _zoneId = z.id; }
            SaveLayout(); Render();
        }
        void EditZones(System.Action<ZoneDefinition> edit)
        {
            foreach (var z in SelectedZones()) edit(z);
            SaveLayout(); FillInspector(_inspector); RefreshCanvas();
        }
        void DeleteZones()
        {
            if (Layout == null) return;
            var ids = new HashSet<string>(SelectedZones().Where(z => !z.locked).Select(z => z.id));
            Layout.zones.RemoveAll(z => ids.Contains(z.id)); _zoneId = null;
            _canvas?.Selection.Clear(); SaveLayout(); Render();
        }
        void Arrange(string mode)
        {
            var zones = SelectedZones().Where(z => !z.locked).ToList(); if (zones.Count == 0) return;
            float left = zones.Count == 1 ? 0 : zones.Min(z => z.x), top = zones.Count == 1 ? 0 : zones.Min(z => z.y);
            float right = zones.Count == 1 ? Layout.width : zones.Max(z => z.x + z.width);
            float bottom = zones.Count == 1 ? Layout.height : zones.Max(z => z.y + z.height);
            float width = zones[0].width, height = zones[0].height;
            foreach (var z in zones) switch (mode) {
                case "Left": z.x = left; break; case "Center X": z.x = (left + right - z.width) / 2; break;
                case "Right": z.x = right-z.width; break; case "Top": z.y = top; break;
                case "Center Y": z.y = (top+bottom-z.height)/2; break; case "Bottom": z.y=bottom-z.height; break;
                case "Match width": z.width=width; break; case "Match height": z.height=height; break;
            }
            if ((mode == "Distribute X" || mode == "Distribute Y") && zones.Count > 2) {
                bool horizontal = mode.EndsWith("X"); zones = zones.OrderBy(z => horizontal ? z.x : z.y).ToList();
                float total = zones.Sum(z => horizontal ? z.width : z.height);
                float gap = ((horizontal ? right-left : bottom-top)-total)/(zones.Count-1), position = horizontal ? left : top;
                foreach(var z in zones) { if(horizontal) z.x=position; else z.y=position; position += (horizontal ? z.width : z.height)+gap; }
            }
            if (mode == "Bring to front" || mode == "Send to back") {
                var sequence = Layout.zones.OrderBy(z=>z.order).ToList(); sequence.RemoveAll(z=>zones.Contains(z));
                if(mode == "Bring to front") sequence.AddRange(zones); else sequence.InsertRange(0,zones);
                for(int i=0;i<sequence.Count;i++) sequence[i].order=i;
            }
            SaveLayout(); FillInspector(_inspector); RefreshCanvas();
        }
        void OnDesignerKey(KeyDownEvent e)
        {
            bool command = e.ctrlKey || e.commandKey;
            if (command && e.keyCode == KeyCode.S) {
                // Let FocusOut finish committing delayed values before saving on the next UI update.
                Focus(); schedule.Execute(SaveDocument); e.StopPropagation(); return;
            }
            if (e.target is TextElement || e.target is TextField || e.target is FloatField || e.target is IntegerField) return;
            if (command && e.keyCode == KeyCode.Z) Restore(e.shiftKey);
            else if (command && e.keyCode == KeyCode.Y) Restore(true);
            else if (_tab == Workspace.Layouts && !_iconStudio && Layout != null && !_previewVariants) {
                if (command && e.keyCode == KeyCode.C) CopyZones();
                else if (command && e.keyCode == KeyCode.V) PasteZones();
                else if (command && e.keyCode == KeyCode.D) { CopyZones(); PasteZones(); }
                else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) DeleteZones();
                else {
                    float step = e.shiftKey ? 10 : 1; float dx=0,dy=0;
                    switch(e.keyCode) { case KeyCode.LeftArrow: dx=-step; break; case KeyCode.RightArrow: dx=step; break; case KeyCode.UpArrow: dy=-step; break; case KeyCode.DownArrow: dy=step; break; default:return; }
                    EditZones(z=> { if(!z.locked) { z.x+=dx;z.y+=dy; } });
                }
            } else return;
            e.StopPropagation();
        }
    }
}
