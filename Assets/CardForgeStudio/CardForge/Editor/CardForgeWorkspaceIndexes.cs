using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    public sealed partial class CardForgeDesignerView
    {
        bool _actionEditorOpen, _abilityEditorOpen;

        void RenderLayoutIndex()
        {
            var left = Pane("sidebar", 286); _main.Add(left);
            var list = Scroll(left); list.Add(CardForgeUi.IndexHeader("LAYOUT LIBRARY", "Layouts", "Choose a layout to edit, or start a new card surface."));
            var create = CardForgeUi.ActionRow(); list.Add(create);
            create.Add(CardForgeUi.Primary("+ New front", () => NewLayout(false)));
            create.Add(CardForgeUi.Secondary("+ New back", () => NewLayout(true)));
            foreach (var layout in Block.layoutIds.Select(id => _store.Layouts.TryGetValue(id, out var item) ? item : null).Where(x => x != null))
                AddListButton(list, layout.name, false, () => { _layoutId = layout.id; _iconStudio = false; _layoutDesignerOpen = true; Render(); }, layout.isBack ? "CARD BACK" : "CARD FRONT");

            var center = Pane("center-pane"); _main.Add(center);
            var content = Scroll(center);
            content.Add(CardForgeUi.IndexHeader("WORKSPACE", "Layout Designer", "Layouts define the reusable card surface, zones, layers, and variants. Select a layout from the library to open the designer."));
            content.Add(CardForgeUi.Divider());
            var guide = CardForgeUi.EmptyState("Start a layout", "Create a front or back, then arrange zones on the interactive canvas.");
            guide.Add(CardForgeUi.Primary("Create new front layout", () => NewLayout(false)));
            guide.Add(CardForgeUi.Secondary("Create new card back", () => NewLayout(true)));
            content.Add(guide);
            var inspector = Pane("inspector", 330); _main.Add(inspector);
            inspector.Add(CardForgeUi.Heading("Workspace")); inspector.Add(CardForgeUi.Description("The layout library is the entry point. Editing is opened explicitly from a list item."));
        }

        void RenderDatabaseIndex()
        {
            var left = Pane("sidebar", 286); _main.Add(left);
            var list = Scroll(left); list.Add(CardForgeUi.IndexHeader("CARD LIBRARY", "Cards", "Select a card to open its definition and live preview."));
            list.Add(CardForgeUi.Primary("+ New card", CreateCardFromIndex));
            foreach (var card in Block.cardIds.Select(id => _store.Cards.TryGetValue(id, out var item) ? item : null).Where(x => x != null))
                AddListButton(list, card.cardNumber + "  " + card.name, false, () => { _cardId = card.id; _databaseEditorOpen = true; Render(); }, card.cardType + "  ·  " + card.variantId);
            var center = Pane("center-pane"); _main.Add(center); var body = Scroll(center);
            body.Add(CardForgeUi.IndexHeader("WORKSPACE", "Card Database", "Card records are independent definitions built from shared layouts. Choose a card from the library to edit it."));
            body.Add(CardForgeUi.Divider());
            var surface = CardForgeUi.EmptyState("Card definitions", "Edit card data, requirements, variants, and visual overrides from the card editor.");
            surface.Add(CardForgeUi.Secondary("Open first card", () =>
            {
                _cardId = Block.cardIds.FirstOrDefault(); _databaseEditorOpen = _cardId != null; Render();
            })); body.Add(surface);
            var inspector = Pane("inspector", 330); _main.Add(inspector);
            inspector.Add(CardForgeUi.Heading("Workspace")); inspector.Add(CardForgeUi.Description("The card list is the entry point. Editing is opened explicitly from a list item."));
        }

        void CreateCardFromIndex()
        {
            var face = Block.layoutIds.Select(id => _store.Layouts.TryGetValue(id, out var layout) ? layout : null).FirstOrDefault(x => x != null && !x.isBack);
            if (face == null) { EditorUtility.DisplayDialog("Create a front layout", "Add a front layout in Layout Designer first.", "OK"); return; }
            var card = _store.CreateCard(Block, face.id); _cardId = card.id; _databaseEditorOpen = true; Render();
        }

        void RenderBlockIndex()
        {
            var left = Pane("sidebar", 286); _main.Add(left); var list = Scroll(left);
            list.Add(CardForgeUi.IndexHeader("BLOCK LIBRARY", "Blocks", "Choose a block to open its builder and export settings."));
            foreach (var block in _store.Blocks.Values.OrderBy(x => x.name))
                AddListButton(list, block.code + "  " + block.name, false, () => { _blockId = block.id; _blockEditorOpen = true; Render(); }, block.cardIds.Count + " cards  ·  " + block.layoutIds.Count + " layouts");
            var center = Pane("center-pane"); _main.Add(center); var body = Scroll(center);
            body.Add(CardForgeUi.IndexHeader("WORKSPACE", "Block Builder", "Blocks group layouts and cards into a portable set. Choose a block from the library to edit it."));
            body.Add(CardForgeUi.Divider());
            body.Add(CardForgeUi.Primary("Create blank block", () =>
            {
                var block = _store.CreateBlock(_newBlockName.Trim(), _newBlockCode.Trim()); _blockId = block.id; _blockEditorOpen = true; Render();
            }));
            var inspector = Pane("inspector", 330); _main.Add(inspector);
            inspector.Add(CardForgeUi.Heading("Workspace")); inspector.Add(CardForgeUi.Description("The block list is the entry point. Editing is opened explicitly from a list item."));
        }

        void RenderActionIndex()
        {
            var left = Pane("sidebar", 286); _main.Add(left); var list = Scroll(left);
            list.Add(CardForgeUi.IndexHeader("ACTION LIBRARY", "Actions", "Choose an action to open its executable rule graph."));
            list.Add(CardForgeUi.Primary("+ New action", () => { var action = new ActionDocument(); _store.SaveAction(action); _actionId = action.id; _actionEditorOpen = true; Render(); }));
            foreach (var action in _store.Actions.Values.OrderBy(x => x.name))
                AddListButton(list, action.name, false, () => { _actionId = action.id; _actionEditorOpen = true; Render(); }, string.IsNullOrEmpty(action.triggerId) ? "No trigger gate" : "Trigger linked");
            var center = Pane("center-pane"); _main.Add(center); var body = Scroll(center);
            body.Add(CardForgeUi.IndexHeader("WORKSPACE", "Action Designer", "Actions define viability links, executable effects, and resolution order. Phase timing is authored in Turn Flow."));
            body.Add(CardForgeUi.EmptyState("Start an action", "Create an action, then open it from the list to edit its resolution pipeline."));
            var inspector = Pane("inspector", 330); _main.Add(inspector);
            inspector.Add(CardForgeUi.Heading("Workspace")); inspector.Add(CardForgeUi.Description("The action list is the entry point. Editing is opened explicitly from a list item."));
        }

        void RenderAbilityIndex()
        {
            var left = Pane("sidebar", 286); _main.Add(left); var list = Scroll(left);
            list.Add(CardForgeUi.IndexHeader("ABILITY SYSTEM", "Abilities & keywords", "Choose an ability or keyword bundle to open its definition."));
            list.Add(CardForgeUi.Primary("+ New ability", () => { var ability = new AbilityDocument(); _store.SaveAbility(ability); _abilityId = ability.id; _abilityEditorOpen = true; Render(); }));
            foreach (var ability in _store.Abilities.Values.OrderBy(x => x.name))
                AddListButton(list, ability.name, false, () => { _abilityId = ability.id; _abilityEditorOpen = true; Render(); }, string.IsNullOrEmpty(ability.cardId) ? "Reusable ability" : "Card-bound ability");
            list.Add(Title("Keyword library"));
            list.Add(CardForgeUi.Primary("+ New keyword", () => { var keyword = new KeywordDocument(); _store.SaveKeyword(keyword); _keywordId = keyword.id; _keywordEditorOpen = true; Render(); }));
            foreach (var keyword in _store.Keywords.Values.OrderBy(x => x.name))
                AddListButton(list, keyword.name, false, () => { _keywordId = keyword.id; _keywordEditorOpen = true; Render(); }, keyword.abilityIds.Count + " abilities");
            var center = Pane("center-pane"); _main.Add(center); var body = Scroll(center);
            body.Add(CardForgeUi.IndexHeader("WORKSPACE", "Ability Designer", "Build reusable abilities, bind card-specific rules, and group abilities into keyword bundles."));
            body.Add(CardForgeUi.EmptyState("Start an ability", "Create an ability, then open it from the list to configure its links and effect."));
            var inspector = Pane("inspector", 330); _main.Add(inspector);
            inspector.Add(CardForgeUi.Heading("Workspace")); inspector.Add(CardForgeUi.Description("The ability list is the entry point. Editing is opened explicitly from a list item."));
        }
    }
}
