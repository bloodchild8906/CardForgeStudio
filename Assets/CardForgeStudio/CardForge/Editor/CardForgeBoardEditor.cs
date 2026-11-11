using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    /// <summary>
    /// Visual board and non-physical/card zone placement editor.
    /// Writes only the selected block's game-definition JSON. Coordinates are
    /// normalized so a published layout works on every client resolution.
    /// </summary>
    public sealed class CardForgeBoardEditor : EditorWindow
    {
        private CardForgeStore store;
        private List<BlockDocument> blocks;
        private GameDefinitionDocument game;
        private DropdownField blockPicker;
        private VisualElement canvas, zoneList, inspector;
        private GameBoardZonePlacement selected;
        private VisualElement moving;
        private Vector2 dragOrigin, originalPosition;
        private bool dragging;
        private Label feedback;

        [MenuItem("CardForge/Design/Board & Card Zones")]
        public static void Open()
        {
            var window = GetWindow<CardForgeBoardEditor>("Board & Card Zones");
            window.minSize = new Vector2(1040, 670);
            window.Show();
        }

        public void CreateGUI()
        {
            store = new CardForgeStore();
            store.Reload();
            blocks = store.Blocks.Values.OrderBy(block => block.name).ToList();
            var root = rootVisualElement;
            root.style.flexGrow = 1;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 10;
            if (blocks.Count == 0)
            {
                root.Add(new HelpBox("Create a CardForge block first.", HelpBoxMessageType.Warning));
                return;
            }
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            toolbar.Add(new Label("BOARD COMPOSER") { style = { fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, marginRight = 25 } });
            blockPicker = new DropdownField("Game", blocks.Select(block => block.name).ToList(), 0);
            blockPicker.style.minWidth = 260;
            toolbar.Add(blockPicker);
            toolbar.Add(new Button(AddCardZone) { text = "+ Card zone", style = { marginLeft = 12 } });
            toolbar.Add(new Button(AddBoardPlacement) { text = "+ Board placement", style = { marginLeft = 6 } });
            toolbar.Add(new Button(Save) { text = "Save layout", style = { marginLeft = 10 } });
            root.Add(toolbar);
            feedback = new Label("Drag placements to position them. Adjust dimensions in the inspector.");
            feedback.style.marginTop = 8;
            feedback.style.marginBottom = 8;
            root.Add(feedback);

            var split = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            root.Add(split);
            var editorArea = new VisualElement { style = { flexGrow = 1, minWidth = 560 } };
            split.Add(editorArea);
            var guide = new Label("OPPONENT BOARD  •  CENTER FIELD  •  PLAYER BOARD");
            guide.style.unityTextAlign = TextAnchor.MiddleCenter;
            guide.style.color = new Color(.8f, .82f, .92f);
            editorArea.Add(guide);
            canvas = new VisualElement();
            canvas.style.flexGrow = 1;
            canvas.style.position = Position.Relative;
            canvas.style.marginRight = 12;
            canvas.style.marginTop = 8;
            canvas.style.marginBottom = 8;
            canvas.style.backgroundColor = new Color(.07f, .11f, .19f, 1);
            canvas.style.borderTopWidth = canvas.style.borderRightWidth =
                canvas.style.borderBottomWidth = canvas.style.borderLeftWidth = 1;
            canvas.style.borderTopColor = canvas.style.borderRightColor =
                canvas.style.borderBottomColor = canvas.style.borderLeftColor =
                new Color(.23f, .35f, .49f);
            editorArea.Add(canvas);

            var right = new ScrollView();
            right.style.width = 370;
            right.style.flexShrink = 0;
            split.Add(right);
            right.Add(new Label("CARD ZONE DEFINITIONS") { style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold } });
            zoneList = new VisualElement();
            right.Add(zoneList);
            right.Add(new Label("SELECTED BOARD PLACEMENT") { style = { fontSize = 15, marginTop = 16, unityFontStyleAndWeight = FontStyle.Bold } });
            inspector = new VisualElement();
            right.Add(inspector);
            blockPicker.RegisterValueChangedCallback(_ => Load());
            Load();
        }

        private void Load()
        {
            game = store.GetGameDefinition(blocks[Mathf.Clamp(blockPicker.index, 0, blocks.Count - 1)]);
            game.boardLayout ??= new List<GameBoardZonePlacement>();
            game.zones ??= new List<GameZoneDocument>();
            selected = null;
            Refresh();
        }

        private void Refresh()
        {
            if (canvas == null || game == null) return;
            canvas.Clear();
            // Area guides are non-interactive and never affect serialized placement.
            for (var index = 1; index < 4; index++)
            {
                var line = new VisualElement();
                line.style.position = Position.Absolute;
                line.style.left = 0;
                line.style.right = 0;
                line.style.top = Length.Percent(index * 25);
                line.style.height = 1;
                line.style.backgroundColor = new Color(.24f, .34f, .49f, .5f);
                line.pickingMode = PickingMode.Ignore;
                canvas.Add(line);
            }
            foreach (var placement in game.boardLayout.OrderBy(z => z.order))
            {
                var zone = new VisualElement();
                zone.style.position = Position.Absolute;
                zone.style.left = Length.Percent(placement.x * 100);
                zone.style.top = Length.Percent(placement.y * 100);
                zone.style.width = Length.Percent(Mathf.Clamp01(placement.width) * 100);
                zone.style.height = Length.Percent(Mathf.Clamp01(placement.height) * 100);
                zone.style.backgroundColor = placement == selected
                    ? new Color(.43f, .35f, .15f, .92f) : new Color(.18f, .31f, .48f, .9f);
                zone.style.borderTopWidth = zone.style.borderRightWidth =
                    zone.style.borderBottomWidth = zone.style.borderLeftWidth = 2;
                zone.style.borderTopColor = zone.style.borderRightColor =
                    zone.style.borderBottomColor = zone.style.borderLeftColor = Color.white * .7f;
                zone.Add(new Label((placement.side == "opponent" ? "OPP  " : "YOU  ") +
                    (string.IsNullOrWhiteSpace(placement.label) ? placement.zoneId : placement.label))
                { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13,
                    marginLeft = 8, marginTop = 6, color = Color.white } });
                zone.RegisterCallback<PointerDownEvent>(evt =>
                {
                    selected = placement;
                    moving = zone;
                    dragging = true;
                    dragOrigin = evt.position;
                    originalPosition = new Vector2(placement.x, placement.y);
                    zone.CapturePointer(evt.pointerId);
                    RefreshInspector();
                    evt.StopPropagation();
                });
                zone.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (!dragging || moving != zone) return;
                    var dx = (evt.position.x - dragOrigin.x) / Mathf.Max(1, canvas.resolvedStyle.width);
                    var dy = (evt.position.y - dragOrigin.y) / Mathf.Max(1, canvas.resolvedStyle.height);
                    placement.x = Mathf.Clamp(originalPosition.x + dx, 0, 1 - placement.width);
                    placement.y = Mathf.Clamp(originalPosition.y + dy, 0, 1 - placement.height);
                    zone.style.left = Length.Percent(placement.x * 100);
                    zone.style.top = Length.Percent(placement.y * 100);
                });
                zone.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (moving == zone)
                    {
                        dragging = false;
                        moving = null;
                        zone.ReleasePointer(evt.pointerId);
                        RefreshInspector();
                    }
                });
                canvas.Add(zone);
            }
            RefreshZoneDefinitions();
            RefreshInspector();
        }

        private void RefreshZoneDefinitions()
        {
            zoneList.Clear();
            foreach (var definition in game.zones.ToList())
            {
                var group = new VisualElement { style = { marginTop = 7, paddingBottom = 7, borderBottomWidth = 1,
                    borderBottomColor = new Color(.25f, .3f, .4f) } };
                var id = new TextField("Zone ID") { value = definition.id };
                id.RegisterValueChangedCallback(evt => { definition.id = evt.newValue.Trim(); RefreshInspector(); });
                group.Add(id);
                var name = new TextField("Name") { value = definition.name };
                name.RegisterValueChangedCallback(evt => definition.name = evt.newValue);
                group.Add(name);
                var scope = new DropdownField("Owner", new List<string> { "player", "game" },
                    definition.ownerScope == "game" ? 1 : 0);
                scope.RegisterValueChangedCallback(evt => definition.ownerScope = evt.newValue);
                group.Add(scope);
                var visibility = new DropdownField("Visible to", new List<string> { "owner", "public", "hidden" },
                    Mathf.Max(0, new List<string> { "owner", "public", "hidden" }.IndexOf(definition.visibility)));
                visibility.RegisterValueChangedCallback(evt => definition.visibility = evt.newValue);
                group.Add(visibility);
                var capacity = new IntegerField("Capacity (0 = unlimited)") { value = definition.capacity };
                capacity.RegisterValueChangedCallback(evt => definition.capacity = Mathf.Max(0, evt.newValue));
                group.Add(capacity);
                var allowedTypes = new TextField("Allowed types (comma-separated)") {
                    value = string.Join(", ", definition.allowedCardTypes ?? new List<string>()) };
                allowedTypes.RegisterValueChangedCallback(evt =>
                    definition.allowedCardTypes = evt.newValue.Split(',')
                        .Select(value => value.Trim()).Where(value => value.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                group.Add(allowedTypes);
                var initialCards = new TextField("Starting card IDs (comma-separated)") {
                    value = string.Join(", ", definition.initialCardIds ?? new List<string>()) };
                initialCards.RegisterValueChangedCallback(evt =>
                    definition.initialCardIds = evt.newValue.Split(',')
                        .Select(value => value.Trim()).Where(value => value.Length > 0).ToList());
                group.Add(initialCards);
                var deckStart = new TextField("Required card type from deck (optional)")
                {
                    value = definition.deckStartCardType ?? ""
                };
                deckStart.RegisterValueChangedCallback(evt =>
                    definition.deckStartCardType = evt.newValue.Trim());
                group.Add(deckStart);
                var returnOnLeave = new Toggle("Return card here when removed from battlefield")
                {
                    value = definition.returnOnLeave
                };
                returnOnLeave.RegisterValueChangedCallback(evt =>
                    definition.returnOnLeave = evt.newValue);
                group.Add(returnOnLeave);
                var immovable = new Toggle("Permanently immovable card")
                {
                    value = definition.immovable
                };
                immovable.RegisterValueChangedCallback(evt =>
                    definition.immovable = evt.newValue);
                group.Add(immovable);
                group.Add(new HelpBox(
                    "Deck-start type requires exactly one matching card in every deck. It starts here and is excluded from the main draw deck. Capacity must be 1. Return-on-leave supports Commanders; immovable supports Mana Sources. These restrictions apply to every player and deck.",
                    HelpBoxMessageType.Info));
                var ordered = new Toggle("Ordered cards") { value = definition.ordered };
                ordered.RegisterValueChangedCallback(evt => definition.ordered = evt.newValue);
                group.Add(ordered);
                group.Add(new Button(() =>
                {
                    game.boardLayout.RemoveAll(z => z.zoneId == definition.id);
                    game.zones.Remove(definition);
                    selected = null;
                    Refresh();
                }) { text = "Delete zone" });
                zoneList.Add(group);
            }
        }

        private void RefreshInspector()
        {
            inspector.Clear();
            if (selected == null)
            {
                inspector.Add(new Label("Select a board zone to edit its position and size."));
                return;
            }
            var choices = game.zones.Select(zone => zone.id).ToList();
            if (choices.Count == 0) return;
            var picker = new DropdownField("Zone", choices,
                Mathf.Max(0, choices.IndexOf(selected.zoneId)));
            picker.RegisterValueChangedCallback(evt => { selected.zoneId = evt.newValue; Refresh(); });
            inspector.Add(picker);
            var side = new DropdownField("Side", new List<string> { "self", "opponent", "shared" },
                Mathf.Max(0, new List<string> { "self", "opponent", "shared" }.IndexOf(selected.side)));
            side.RegisterValueChangedCallback(evt => { selected.side = evt.newValue; Refresh(); });
            inspector.Add(side);
            var label = new TextField("Display label") { value = selected.label };
            label.RegisterValueChangedCallback(evt => { selected.label = evt.newValue; Refresh(); });
            inspector.Add(label);
            AddDimension("X", () => selected.x, value => selected.x = value);
            AddDimension("Y", () => selected.y, value => selected.y = value);
            AddDimension("Width", () => selected.width, value => selected.width = value, .05f);
            AddDimension("Height", () => selected.height, value => selected.height = value, .05f);
            var order = new IntegerField("Draw order") { value = selected.order };
            order.RegisterValueChangedCallback(evt => { selected.order = evt.newValue; Refresh(); });
            inspector.Add(order);
            var placement = selected;
            inspector.Add(new Button(() =>
            {
                game.boardLayout.Remove(placement);
                selected = null;
                Refresh();
            }) { text = "Remove placement" });
        }

        private void AddDimension(string label, Func<float> read, Action<float> assign, float min = 0)
        {
            var slider = new Slider(label, min, 1) { value = read() };
            slider.RegisterValueChangedCallback(evt =>
            {
                assign(Mathf.Clamp(evt.newValue, min, 1));
                if (selected != null)
                {
                    selected.x = Mathf.Clamp(selected.x, 0, 1 - selected.width);
                    selected.y = Mathf.Clamp(selected.y, 0, 1 - selected.height);
                }
                // Update the canvas once the drag is finished, not on every frame.
                if (!slider.ClassListContains("dragging")) Refresh();
            });
            inspector.Add(slider);
        }

        private void AddCardZone()
        {
            if (game == null) return;
            var id = "zone-" + (game.zones.Count + 1);
            game.zones.Add(new GameZoneDocument { id = id, name = "Card Zone " + game.zones.Count,
                ownerScope = "player", visibility = "public" });
            Refresh();
        }

        private void AddBoardPlacement()
        {
            if (game == null || game.zones.Count == 0) return;
            selected = new GameBoardZonePlacement { zoneId = game.zones[0].id, x = .25f, y = .4f,
                width = .5f, height = .17f, order = game.boardLayout.Count };
            game.boardLayout.Add(selected);
            Refresh();
        }

        private void Save()
        {
            try
            {
                if (game.zones.Select(z => z.id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != game.zones.Count)
                    throw new InvalidOperationException("Zone IDs must be unique.");
                if (game.boardLayout.Any(z => !game.zones.Any(zone => zone.id == z.zoneId)))
                    throw new InvalidOperationException("All board placements must reference a defined zone.");
                store.SaveGameDefinition(game);
                AssetDatabase.SaveAssets();
                feedback.text = "Saved " + game.zones.Count + " zones and " +
                    game.boardLayout.Count + " board placements to the game definition.";
            }
            catch (Exception exception) { feedback.text = "Save failed: " + exception.Message; }
        }
    }
}
