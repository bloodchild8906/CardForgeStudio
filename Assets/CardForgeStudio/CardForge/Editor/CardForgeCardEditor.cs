using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    public sealed partial class CardForgeDesignerView
    {
        void RenderCardRecordDetails(VisualElement parent)
        {
            if (Card == null) return;
            var record = Group("CARD RECORD DETAILS"); parent.Add(record);
            record.Add(Note("Every authored value remains available here, including metadata that is not bound to a visible card zone."));
            record.Add(new Label("ID  " + Card.id));
            record.Add(new Label("BLOCK  " + (Block?.name ?? "Unassigned")));
            record.Add(new Label("FRONT  " + LayoutName(Card.layoutId)));
            record.Add(new Label("BACK  " + (string.IsNullOrEmpty(Card.backLayoutId) ? "None" : LayoutName(Card.backLayoutId))));
            record.Add(new Label("SOURCE  " + (string.IsNullOrEmpty(Card.sourceCardId) ? "Original card" : Card.sourceBlockId + " / " + Card.sourceCardId)));
        }

        void RenderCardRuleLinks(VisualElement parent)
        {
            if (Card == null) return;
            if (Card.keywordIds == null) Card.keywordIds = new List<string>();
            if (Card.abilityIds == null) Card.abilityIds = new List<string>();
            var group = Group("KEYWORDS & CARD ABILITIES"); parent.Add(group);
            group.Add(Note("Use [keyword] in rules text to reference a reusable bundle. Card abilities remain available for mechanics unique to this card."));
            foreach (var id in Card.keywordIds.ToList())
            {
                var local = id;
                var row = Row("requirement-row"); group.Add(row);
                row.Add(new Label(_store.Keywords.TryGetValue(local, out var keyword) ? "[" + keyword.name + "]" : "Missing keyword"));
                row.Add(Btn("Remove", () => { Card.keywordIds.Remove(local); SaveCard(); Render(); }, "small-danger"));
            }
            var keywordOptions = _store.Keywords.Values.Where(keyword => !Card.keywordIds.Contains(keyword.id));
            Choice(group, "Add keyword", new[] { new KeyValuePair<string, string>("", "Select keyword") }.Concat(Options(keywordOptions, keyword => keyword.id, keyword => "[" + keyword.name + "]")), "", id =>
            {
                if (string.IsNullOrEmpty(id)) return;
                Card.keywordIds.Add(id); SaveCard(); Render();
            });
            foreach (var id in Card.abilityIds.ToList())
            {
                var local = id;
                var row = Row("requirement-row"); group.Add(row);
                row.Add(new Label(_store.Abilities.TryGetValue(local, out var ability) ? ability.name : "Missing ability"));
                row.Add(Btn("Remove", () => { Card.abilityIds.Remove(local); SaveCard(); Render(); }, "small-danger"));
            }
            var abilityOptions = _store.Abilities.Values.Where(ability => !Card.abilityIds.Contains(ability.id));
            Choice(group, "Add card ability", new[] { new KeyValuePair<string, string>("", "Select ability") }.Concat(Options(abilityOptions, ability => ability.id, ability => ability.name)), "", id =>
            {
                if (string.IsNullOrEmpty(id)) return;
                Card.abilityIds.Add(id); SaveCard(); Render();
            });
        }

        void RenderCardArtCatalog(VisualElement parent)
        {
            if (Card == null) return;
            var fields = _store.Schema.fields.Where(field => field.kind == FieldKind.Image || field.kind == FieldKind.Icon).ToList();
            var group = Group("ART CATALOG"); parent.Add(group);
            group.Add(Note("Choose a project texture, then assign it to any image or icon field on this card."));
            if (fields.Count == 0)
            {
                group.Add(Note("Add an Image or Icon data field to enable art selection."));
                return;
            }
            if (string.IsNullOrEmpty(_cardArtFieldId) || !fields.Any(field => field.id == _cardArtFieldId)) _cardArtFieldId = fields[0].id;
            Choice(group, "Assign art to", Options(fields, field => field.id, field => field.label), _cardArtFieldId, id => { _cardArtFieldId = id; Render(); });
            var selectedField = fields.First(field => field.id == _cardArtFieldId);
            var selectedPath = CardForgeData.Value(Card, selectedField);
            var current = Row("requirement-row"); group.Add(current);
            current.Add(new Label(string.IsNullOrEmpty(selectedPath) ? "No art assigned" : selectedPath));
            current.Add(Btn("Clear", () => { CardForgeData.SetValue(Card, selectedField.id, ""); SaveCard(); RefreshCanvas(); Render(); }, "small-danger"));
            group.Add(CardForgeArtCatalog.Create(selectedPath, path =>
            {
                CardForgeData.SetValue(Card, selectedField.id, path);
                SaveCard();
                RefreshCanvas();
                Render();
            }));
        }

        string LayoutName(string id)
        {
            return _store.Layouts.TryGetValue(id ?? "", out var layout) ? layout.name : "Missing layout";
        }
    }
}
