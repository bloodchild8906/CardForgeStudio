using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    public sealed partial class CardForgeDesignerView
    {
        KeywordDocument Keyword => _keywordId != null && _store.Keywords.TryGetValue(_keywordId, out var keyword) ? keyword : null;

        void RenderKeywordEditor()
        {
            var left = Pane("sidebar", 286); _main.Add(left);
            var list = Scroll(left);
            list.Add(Kicker("KEYWORD LIBRARY"));
            list.Add(Title("Keywords"));
            list.Add(Note("A keyword is a named bundle of abilities. Cards can use the title while the rules remain reusable."));
            list.Add(Btn("+ New keyword", () =>
            {
                var keyword = new KeywordDocument();
                _store.SaveKeyword(keyword);
                _keywordId = keyword.id;
                _keywordEditorOpen = true;
                Render();
            }, "primary-button"));
            foreach (var keyword in _store.Keywords.Values.OrderBy(item => item.name))
                AddListButton(list, keyword.name, keyword.id == _keywordId, () => { _keywordId = keyword.id; Render(); }, keyword.abilityIds.Count + " abilities");

            var center = Pane("center-pane"); _main.Add(center);
            var bar = Row("content-bar"); center.Add(bar);
            bar.Add(CardForgeUi.Back("Abilities", () => { _keywordEditorOpen = false; Render(); }));
            bar.Add(Title(Keyword?.name ?? "Keyword")); bar.Add(Flex());
            if (Keyword != null)
            {
                bar.Add(Btn("Duplicate", DuplicateKeyword, "small-button"));
                bar.Add(Btn("Delete", DeleteKeyword, "small-danger"));
            }
            var body = Scroll(center);
            if (Keyword == null)
            {
                body.Add(Note("Select a keyword to edit its title and ability bundle."));
                return;
            }
            RenderKeywordDefinition(body, Keyword);

            _inspector = Pane("inspector", 330); _main.Add(_inspector);
            var inspector = Scroll(_inspector);
            inspector.Add(Title("Keyword audit"));
            RenderValidation(inspector, "Keyword", _store.ValidateKeyword(Keyword));
            inspector.Add(Note("Every ability linked here is expanded when this keyword is resolved. Individual cards can still link their own abilities."));
            inspector.Add(new Label("ID  " + Keyword.id));
            inspector.Add(new Label("ABILITIES  " + Keyword.abilityIds.Count));
            inspector.Add(new Label("TAGS  " + Keyword.tags.Count));
        }

        void RenderKeywordDefinition(VisualElement parent, KeywordDocument keyword)
        {
            parent.Add(Title("Keyword definition"));
            parent.Add(Note("The title is the reusable token used in card rules text, for example [keyword]."));
            TextBox(parent, "Title", keyword.name, value => { keyword.name = value; SaveKeyword(); });
            TextBox(parent, "Rules text", keyword.rulesText, value => { keyword.rulesText = value; SaveKeyword(); }, true);
            TextBox(parent, "Reminder text", keyword.reminderText, value => { keyword.reminderText = value; SaveKeyword(); }, true);
            TextBox(parent, "Tags", string.Join(", ", keyword.tags ?? new List<string>()), value =>
            {
                keyword.tags = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                SaveKeyword();
            });

            if (keyword.abilityIds == null) keyword.abilityIds = new List<string>();
            var group = Group("ABILITIES IN THIS KEYWORD"); parent.Add(group);
            group.Add(Note("Add reusable abilities in the order they should be expanded under this keyword."));
            foreach (var id in keyword.abilityIds.ToList())
            {
                var local = id;
                var row = Row("requirement-row"); group.Add(row);
                row.Add(new Label(_store.Abilities.TryGetValue(local, out var ability) ? ability.name : "Missing ability"));
                row.Add(Btn("Remove", () => { keyword.abilityIds.Remove(local); SaveKeyword(); Render(); }, "small-danger"));
            }
            var options = _store.Abilities.Values.Where(ability => !keyword.abilityIds.Contains(ability.id));
            Choice(group, "Add ability", new[] { new KeyValuePair<string, string>("", "Select ability") }.Concat(Options(options, item => item.id, item => item.name)), "", id =>
            {
                if (string.IsNullOrEmpty(id)) return;
                keyword.abilityIds.Add(id);
                SaveKeyword();
                Render();
            });
        }

        void SaveKeyword()
        {
            if (Keyword == null) return;
            _store.SaveKeyword(Keyword);
            TrackHistory();
            _notification = "Keyword saved";
        }

        void DuplicateKeyword()
        {
            if (Keyword == null) return;
            var copy = CardForgeData.Copy(Keyword);
            copy.id = CardForgeData.NewId();
            copy.name += " copy";
            _store.SaveKeyword(copy);
            _keywordId = copy.id;
            Render();
        }

        void DeleteKeyword()
        {
            if (Keyword == null) return;
            try
            {
                _store.DeleteKeyword(Keyword.id);
                _keywordId = null;
                _keywordEditorOpen = false;
                Render();
            }
            catch (Exception error) { Error("Keyword is in use", error); }
        }
    }
}
