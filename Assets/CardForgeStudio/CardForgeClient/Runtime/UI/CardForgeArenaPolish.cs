using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TcgServer.Client
{
    // Higher-level runtime screens. Server DTOs remain the source of truth for card legality and matches.
    public sealed partial class CardForgeToolkitClient
    {
        private string selectedDeckName;

        private static Button UiButton(string title, Action onClick, bool primary = false)
        {
            var button = new Button(onClick) { text = title };
            button.AddToClassList(primary ? "cf-primary" : "cf-secondary");
            return button;
        }

        private static VisualElement UiSurface(bool hero = false)
        {
            var panel = Flex();
            panel.AddToClassList(hero ? "cf-hero" : "cf-panel");
            panel.style.paddingTop = 18;
            panel.style.paddingBottom = 18;
            panel.style.paddingLeft = 20;
            panel.style.paddingRight = 20;
            return panel;
        }

        private static VisualElement UiArt(string cardId, int width, int height)
        {
            var frame = Flex();
            frame.style.width = width;
            frame.style.height = height;
            frame.style.flexShrink = 0;
            frame.style.backgroundColor = ArenaInk;
            frame.style.marginRight = 10;
            frame.style.paddingTop = 3;
            frame.style.paddingBottom = 3;
            frame.style.paddingLeft = 3;
            frame.style.paddingRight = 3;
            Frame(frame, ArenaTrim, 2);
            Round(frame, 7);
            var image = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            image.style.flexGrow = 1;
            image.style.flexShrink = 1;
            image.style.width = width - 6;
            var texture = Resources.Load<Texture2D>("AstralRift/Illustrations/" + cardId);
            if (texture != null) image.image = texture;
            frame.Add(image);
            return frame;
        }

        private void SelectStarterFromHub(string name)
        {
            if (!RequireAccount()) return;
            StartCoroutine(api.Decks(session.Token, decks =>
            {
                var found = decks.FirstOrDefault(deck =>
                    string.Equals(deck.name, name, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    SetNotice("This starter is not saved yet. Open Decks to build it.");
                    Show("Decks");
                    return;
                }
                selectedDeckId = found.id;
                selectedDeckName = found.name;
                SetNotice(found.name + " is ready.");
                if (page == "Play" || page == "Matchmaking") BuildPlayHub();
            }, error => SetNotice("Decks unavailable: " + error)));
        }

        private void BeginAiMatch(int opponentCount)
        {
            if (!RequireAccount()) return;
            if (actionBusy) return;
            actionBusy = true;
            SetNotice("Preparing your match...");
            StartCoroutine(api.CreateAiGame(session.Token, session.User.id, result =>
            {
                actionBusy = false;
                matchId = result.id;
                rulePack = null;
                match = null;
                selected = null;
                targetAction = null;
                lastViewKey = null;
                page = "Play";
                StartCoroutine(LoadRulePackAndMatch());
            }, error =>
            {
                actionBusy = false;
                SetNotice("Unable to create match: " + error);
            }, selectedDeckId, opponentCount));
        }

        private void BuildPlayHub()
        {
            content.Clear();
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.AddToClassList("cf-scroll");
            content.Add(scroll);

            var hero = UiSurface(true);
            hero.style.minHeight = 302;
            hero.style.marginBottom = 15;
            var heroRow = Flex(true);
            heroRow.style.alignItems = Align.Center;
            hero.Add(heroRow);
            var copy = Flex();
            copy.style.flexGrow = 1;
            copy.style.flexShrink = 1;
            copy.style.minWidth = 340;
            copy.style.paddingRight = 12;
            heroRow.Add(copy);
            copy.Add(Label("THE ASTRAL RIFT", 12, CommandTone));
            var heading = Label("Build your legend.\nCommand the battlefield.", 35, Pale);
            heading.AddToClassList("cf-heading");
            heading.style.marginTop = 9;
            heading.style.marginBottom = 11;
            copy.Add(heading);
            copy.Add(Label("Every turn matters. Build a 40-card strategy, bind a Commander and evolve your Mana Source.", 15, Soft));
            var stats = Flex(true);
            stats.style.marginTop = 14;
            foreach (var line in new[] { "240 ORIGINAL CARDS", "3 STARTER DECKS", "5 CARD TYPES" })
            {
                var pill = Label(line, 10, CommandTone);
                pill.style.marginRight = 13;
                stats.Add(pill);
            }
            copy.Add(stats);

            var art = Flex(true);
            art.style.justifyContent = Justify.FlexEnd;
            art.style.flexShrink = 0;
            art.Add(UiArt("ar-card-solar-001", 134, 213));
            art.Add(UiArt("ar-card-tidal-001", 134, 213));
            art.Add(UiArt("ar-card-verdant-001", 134, 213));
            heroRow.Add(art);
            scroll.Add(hero);

            var play = UiSurface();
            play.style.marginBottom = 15;
            play.Add(Label("QUICK PLAY", 16, CommandTone));
            play.Add(Label("Play against server-controlled opponents. Your selected deck is used for the match.", 12, Soft));
            var controls = Flex(true);
            controls.style.alignItems = Align.Center;
            controls.style.marginTop = 15;
            var modes = new DropdownField("OPPONENTS",
                new List<string> { "1 opponent", "2 opponents", "3 opponents", "4 opponents" }, 0);
            modes.style.width = 220;
            modes.style.marginRight = 18;
            modes.AddToClassList("cf-textfield");
            controls.Add(modes);
            controls.Add(UiButton("START AI MATCH", () => BeginAiMatch(modes.index + 1), true));
            controls.Add(UiButton("EDIT YOUR DECK", () => Show("Decks")));
            play.Add(controls);
            play.Add(Label("ACTIVE DECK  /  " +
                (string.IsNullOrWhiteSpace(selectedDeckName) ? "Choose a starter below or select a saved deck" : selectedDeckName),
                12, CommandTone));
            scroll.Add(play);

            var sectionTitle = Label("CHOOSE YOUR STRATEGY", 19, Pale);
            sectionTitle.style.marginBottom = 11;
            scroll.Add(sectionTitle);
            var decks = Flex(true);
            decks.style.flexWrap = Wrap.Wrap;
            foreach (var starter in new[]
            {
                (name: "Solar Vanguard", subtitle: "Solar", id: "ar-card-solar-001",
                    description: "Aggressive creatures, radiant shields and burst damage.", color: QuickTone),
                (name: "Tidal Archive", subtitle: "Tidal", id: "ar-card-tidal-001",
                    description: "Quickspells, card advantage and disruption.", color: ManaTone),
                (name: "Verdant Sanctuary", subtitle: "Verdant", id: "ar-card-verdant-001",
                    description: "Creature growth, defense and sustained advantage.", color: CreatureTone)
            })
            {
                var name = starter.name;
                var card = UiSurface();
                card.style.flexGrow = 1;
                card.style.flexBasis = 0;
                card.style.minWidth = 305;
                card.style.marginRight = 9;
                card.style.marginBottom = 9;
                card.AddToClassList("cf-deck-card");
                var body = Flex(true);
                body.Add(UiArt(starter.id, 87, 119));
                var info = Flex();
                info.style.flexGrow = 1;
                info.Add(Label(starter.subtitle.ToUpperInvariant() + "  /  STARTER", 10, starter.color));
                var label = Label(name, 17, Pale);
                label.style.marginTop = 5;
                label.style.marginBottom = 5;
                info.Add(label);
                info.Add(Label(starter.description, 11, Soft));
                body.Add(info);
                card.Add(body);
                var use = UiButton("USE THIS DECK", () => SelectStarterFromHub(name));
                use.style.marginTop = 11;
                card.Add(use);
                decks.Add(card);
            }
            scroll.Add(decks);

            var utilities = UiSurface();
            utilities.style.marginTop = 5;
            utilities.Add(Label("OTHER WAYS TO PLAY", 14, Pale));
            var misc = Flex(true);
            misc.style.flexWrap = Wrap.Wrap;
            misc.style.alignItems = Align.Center;
            misc.style.marginTop = 9;
            var matchField = new TextField { label = "MATCH ID" };
            matchField.style.minWidth = 255;
            matchField.style.marginRight = 10;
            misc.Add(matchField);
            misc.Add(UiButton("OPEN MATCH", () =>
            {
                var requested = matchField.value?.Trim();
                if (string.IsNullOrEmpty(requested))
                {
                    SetNotice("Enter a match ID to join.");
                    return;
                }
                matchId = requested;
                match = null;
                lastViewKey = null;
                StartCoroutine(LoadRulePackAndMatch());
            }));
            misc.Add(UiButton("FRIENDS", () => Show("Friends")));
            misc.Add(UiButton("MESSAGES", () => Show("Messages")));
            misc.Add(UiButton("NEWS", () => Show("News & Events")));
            misc.Add(UiButton("RETRY SERVER", () => StartCoroutine(ConnectDemo())));
            utilities.Add(misc);
            scroll.Add(utilities);
        }

        private void BuildDeckWorkshopV2(DeckDto[] decks, InventoryDto inventory,
            RulePackDto rules, DeckDto editing)
        {
            content.Clear();
            var layout = Flex(true);
            layout.style.flexGrow = 1;
            layout.style.minHeight = 0;
            content.Add(layout);

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.width = 415;
            left.style.flexShrink = 0;
            left.style.marginRight = 12;
            left.AddToClassList("cf-panel");
            layout.Add(left);
            var intro = UiSurface();
            intro.Add(Label("DECK WORKSHOP", 23, CommandTone));
            intro.Add(Label("40 main cards  +  1 Commander  +  1 Mana Source", 12, Soft));
            left.Add(intro);

            var owned = UiSurface();
            owned.style.marginTop = 9;
            owned.Add(Label("SAVED DECKS", 15, Pale));
            foreach (var deck in decks ?? Array.Empty<DeckDto>())
            {
                var current = deck;
                var row = Flex(true);
                row.style.alignItems = Align.Center;
                row.style.marginTop = 7;
                var name = Label(current.name + "  /  " + (current.cardIds?.Length ?? 0), 12,
                    selectedDeckId == current.id ? CommandTone : Pale);
                name.style.flexGrow = 1;
                row.Add(name);
                row.Add(UiButton("EDIT", () => BuildDeckWorkshopV2(decks, inventory, rules, current)));
                row.Add(UiButton(selectedDeckId == current.id ? "ACTIVE" : "USE", () =>
                {
                    selectedDeckId = current.id;
                    selectedDeckName = current.name;
                    SetNotice(current.name + " selected for play.");
                    BuildDeckWorkshopV2(decks, inventory, rules, editing);
                }));
                owned.Add(row);
            }
            if ((decks?.Length ?? 0) == 0) owned.Add(Label("No decks saved yet. Select a starter below.", 12, Soft));
            left.Add(owned);

            var starterPanel = UiSurface();
            starterPanel.style.marginTop = 9;
            starterPanel.Add(Label("READY-MADE STRATEGIES", 15, Pale));
            foreach (var starter in rules.starterDecks ?? Array.Empty<RuleStarterDeckDto>())
            {
                var current = starter;
                var button = UiButton(current.name + "  /  42 UNIQUE", () =>
                    BuildDeckWorkshopV2(decks, inventory, rules, new DeckDto
                    {
                        name = current.name, cardIds = current.cardIds
                    }));
                button.style.marginTop = 6;
                starterPanel.Add(button);
            }
            left.Add(starterPanel);

            var draft = (editing?.cardIds ?? Array.Empty<string>()).ToList();
            var editPanel = UiSurface();
            editPanel.style.marginTop = 9;
            editPanel.Add(Label("YOUR DRAFT", 15, Pale));
            var nameInput = new TextField("DECK NAME") { value = editing?.name ?? "New deck" };
            nameInput.AddToClassList("cf-textfield");
            editPanel.Add(nameInput);
            var mainCount = Label("", 15, CommandTone);
            editPanel.Add(mainCount);
            var validity = Label("", 11, Soft);
            editPanel.Add(validity);
            var actionRow = Flex(true);
            var save = UiButton("SAVE DECK", () =>
            {
                var errors = ClientDeckLegality.Validate(rules, draft);
                if (errors.Count != 0)
                {
                    SetNotice(string.Join(" ", errors.Take(2)));
                    return;
                }
                StartCoroutine(api.SaveDeck(session.Token, new SaveDeckRequestDto
                {
                    id = editing?.id,
                    name = nameInput.value,
                    rulePackId = settings.DefaultRulePackId,
                    cardIds = draft.ToArray()
                }, result =>
                {
                    selectedDeckId = result.id;
                    selectedDeckName = result.name;
                    Show("Decks");
                }, error => SetNotice("Save failed: " + error)));
            }, true);
            actionRow.Add(save);
            actionRow.Add(UiButton("NEW DRAFT", () =>
                BuildDeckWorkshopV2(decks, inventory, rules, null)));
            editPanel.Add(actionRow);
            var entries = Flex();
            editPanel.Add(entries);
            left.Add(editPanel);

            var right = UiSurface();
            right.style.flexGrow = 1;
            right.style.flexShrink = 1;
            right.style.minWidth = 0;
            layout.Add(right);
            right.Add(Label("CARD LIBRARY", 22, Pale));
            right.Add(Label("Select a card to add it to the draft. Hover to read its full ability.", 12, Soft));
            var search = new TextField("Search cards");
            search.AddToClassList("cf-textfield");
            search.style.marginTop = 12;
            right.Add(search);
            var filters = Flex(true);
            filters.style.flexWrap = Wrap.Wrap;
            right.Add(filters);
            var browse = new ScrollView(ScrollViewMode.Vertical);
            browse.style.flexGrow = 1;
            browse.style.minHeight = 0;
            right.Add(browse);
            var gallery = Flex(true);
            gallery.style.flexWrap = Wrap.Wrap;
            browse.Add(gallery);
            var resultsLabel = Label("", 11, Soft);
            right.Add(resultsLabel);
            string activeType = "ALL";
            int visibleLimit = 24;
            var definitions = rules.cards.ToDictionary(c => c.id, StringComparer.OrdinalIgnoreCase);
            Button loadMore = null;
            Label preview = null;
            loadMore = UiButton("LOAD MORE CARDS", () =>
            {
                visibleLimit += 24;
                RenderGallery();
            });
            right.Add(loadMore);
            preview = Label("HOVER A CARD  /  FULL RULES AND OWNED COPIES", 11, Soft);
            preview.style.minHeight = 34;
            right.Add(preview);
            foreach (var choice in new[] { "ALL", "CREATURE", "SPELL", "QUICKSPELL", "MANASOURCE", "COMMANDER" })
            {
                var type = choice;
                var button = UiButton(type, () =>
                {
                    activeType = type;
                    visibleLimit = 24;
                    foreach (var item in filters.Children().OfType<Button>())
                        item.EnableInClassList("cf-filter--active", item.text == activeType);
                    RenderGallery();
                });
                button.AddToClassList("cf-filter");
                button.EnableInClassList("cf-filter--active", type == activeType);
                filters.Add(button);
            }

            void RefreshDraft()
            {
                var main = ClientDeckLegality.MainCount(rules, draft);
                var commander = draft.Count(id => definitions.TryGetValue(id, out var c) &&
                    CardKind(c) == "COMMANDER");
                var mana = draft.Count(id => definitions.TryGetValue(id, out var c) &&
                    CardKind(c) == "MANASOURCE");
                mainCount.text = "MAIN " + main + "/40     COMMANDER " + commander + "/1     MANA " + mana + "/1";
                var errors = ClientDeckLegality.Validate(rules, draft);
                validity.text = errors.Count == 0 ? "READY TO SAVE" : string.Join("  |  ", errors.Take(3));
                validity.style.color = errors.Count == 0 ? CreatureTone : QuickTone;
                save.SetEnabled(errors.Count == 0);
                entries.Clear();
                foreach (var group in draft.GroupBy(id => id).OrderBy(g => g.Key))
                {
                    var cardId = group.Key;
                    var name = definitions.TryGetValue(cardId, out var def) ? def.name : cardId;
                    var row = Flex(true);
                    row.style.alignItems = Align.Center;
                    row.style.marginTop = 4;
                    var text = Label(name + "   x" + group.Count(), 11, Pale);
                    text.style.flexGrow = 1;
                    row.Add(text);
                    row.Add(UiButton("REMOVE", () =>
                    {
                        draft.Remove(cardId);
                        RefreshDraft();
                    }));
                    entries.Add(row);
                }
            }
            void RenderGallery()
            {
                gallery.Clear();
                var groups = inventory.cards.GroupBy(card => card.cardId)
                    .Select(g => new { id = g.Key, owned = g.Count() })
                    .Where(g => definitions.TryGetValue(g.id, out var def) &&
                        (activeType == "ALL" || CardKind(def) == activeType) &&
                        (string.IsNullOrWhiteSpace(search.value) ||
                            def.name.IndexOf(search.value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            def.type.IndexOf(search.value, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(g => definitions[g.id].cost)
                    .ThenBy(g => definitions[g.id].name)
                    .ToList();
                foreach (var group in groups.Take(visibleLimit))
                {
                    var def = definitions[group.id];
                    var cardId = def.id;
                    var ownedCount = group.owned;
                    var tile = Flex();
                    tile.style.marginRight = 11;
                    tile.style.marginBottom = 9;
                    var cardFace = MakeCardFace(def, 137, () =>
                    {
                        if (draft.Count(id => string.Equals(id, cardId, StringComparison.OrdinalIgnoreCase)) >= ownedCount)
                        {
                            SetNotice("No more owned copies of " + def.name + ".");
                            return;
                        }
                        if (!ClientDeckLegality.CanAdd(rules, draft, cardId, out var reason))
                        {
                            SetNotice(reason);
                            return;
                        }
                        draft.Add(cardId);
                        RefreshDraft();
                    });
                    cardFace.RegisterCallback<PointerEnterEvent>(_ =>
                        preview.text = def.name.ToUpperInvariant() + "   /   OWNED " + ownedCount +
                            "\n" + (def.text ?? ""));
                    tile.Add(cardFace);
                    tile.Add(Label("OWNED  " + ownedCount, 10, CommandTone));
                    gallery.Add(tile);
                }
                resultsLabel.text = groups.Count + " MATCHES  /  SHOWING " + Math.Min(visibleLimit, groups.Count);
                loadMore.style.display = visibleLimit < groups.Count ? DisplayStyle.Flex : DisplayStyle.None;
            }
            search.RegisterValueChangedCallback(_ =>
            {
                visibleLimit = 24;
                RenderGallery();
            });
            RefreshDraft();
            RenderGallery();
        }

        private void ShowCollectionGallery(InventoryDto inventory, RulePackDto rules)
        {
            content.Clear();
            var source = inventory.cards
                .GroupBy(card => card.cardId)
                .Select(group => new { id = group.Key, count = group.Count() })
                .ToList();
            var definitions = rules.cards.ToDictionary(c => c.id, StringComparer.OrdinalIgnoreCase);
            var title = Flex(true);
            title.style.alignItems = Align.Center;
            title.Add(Label("YOUR COLLECTION", 23, CommandTone));
            var total = Label("    " + source.Count + " DISTINCT CARDS  /  " + inventory.cards.Length + " COPIES", 12, Soft);
            title.Add(total);
            content.Add(title);
            var main = Flex(true);
            main.style.flexGrow = 1;
            main.style.minHeight = 0;
            content.Add(main);
            var browser = UiSurface();
            browser.style.flexGrow = 1;
            browser.style.flexShrink = 1;
            browser.style.marginRight = 10;
            main.Add(browser);
            var search = new TextField("Search collection");
            browser.Add(search);
            var tabs = Flex(true);
            tabs.style.flexWrap = Wrap.Wrap;
            browser.Add(tabs);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            browser.Add(scroll);
            var grid = Flex(true);
            grid.style.flexWrap = Wrap.Wrap;
            scroll.Add(grid);
            int limit = 36;
            string filter = "ALL";
            Button more = null;
            VisualElement preview = null;
            more = UiButton("SHOW MORE", () => { limit += 36; Render(); });
            browser.Add(more);
            preview = UiSurface();
            preview.style.width = 285;
            preview.style.flexShrink = 0;
            main.Add(preview);
            preview.Add(Label("INSPECT CARD", 17, CommandTone));
            preview.Add(Label("Click a card to view its full description.", 12, Soft));
            foreach (var option in new[] { "ALL", "CREATURE", "SPELL", "QUICKSPELL", "MANASOURCE", "COMMANDER" })
            {
                var kind = option;
                var tab = UiButton(kind, () =>
                {
                    filter = kind;
                    limit = 36;
                    foreach (var b in tabs.Children().OfType<Button>())
                        b.EnableInClassList("cf-filter--active", b.text == filter);
                    Render();
                });
                tab.AddToClassList("cf-filter");
                tab.EnableInClassList("cf-filter--active", filter == kind);
                tabs.Add(tab);
            }
            void Inspect(CardDefinitionDto definition, int count)
            {
                preview.Clear();
                preview.Add(Label("INSPECT CARD", 17, CommandTone));
                preview.Add(MakeCardFace(definition, 245, null));
                var rulesView = new ScrollView(ScrollViewMode.Vertical);
                rulesView.style.flexGrow = 1;
                rulesView.Add(Label(definition.name + "  /  OWNED " + count + "\n\n" + definition.text, 12, Pale));
                preview.Add(rulesView);
            }
            void Render()
            {
                grid.Clear();
                var matches = source.Where(item =>
                    definitions.TryGetValue(item.id, out var def) &&
                    (filter == "ALL" || CardKind(def) == filter) &&
                    (string.IsNullOrWhiteSpace(search.value) ||
                        def.name.IndexOf(search.value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        def.type.IndexOf(search.value, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(item => definitions[item.id].name).ToList();
                foreach (var item in matches.Take(limit))
                {
                    var def = definitions[item.id];
                    var ownedCount = item.count;
                    var itemView = Flex();
                    itemView.style.marginRight = 10;
                    itemView.style.marginBottom = 10;
                    itemView.Add(MakeCardFace(def, 133, () => Inspect(def, ownedCount)));
                    itemView.Add(Label("OWNED  " + item.count, 10, CommandTone));
                    grid.Add(itemView);
                }
                more.style.display = limit < matches.Count ? DisplayStyle.Flex : DisplayStyle.None;
            }
            search.RegisterValueChangedCallback(_ => { limit = 36; Render(); });
            Render();
        }
    }
}
