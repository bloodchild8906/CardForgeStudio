$ErrorActionPreference='Stop'
$path='D:\CardForgeStudio\Assets\CardForgeStudio\CardForgeClient\Runtime\UI\CardForgeToolkitClient.cs'
$s=[IO.File]::ReadAllText($path)
function Rep([string]$old,[string]$new) {
 if(!$script:s.Contains($old)){throw "Missing client code: $($old.Substring(0,[Math]::Min(70,$old.Length)))"}
 $script:s=$script:s.Replace($old,$new)
}
Rep @'
            content.Add(saved);

            var draft = editing == null ? new List<string>() : editing.cardIds.ToList();
'@ @'
            var templates = Panel();
            templates.Add(Label("FOUR STARTER STRATEGIES  •  40 MAIN + COMMANDER + MANA SOURCE", 14, Gold));
            foreach (var starter in rules.starterDecks ?? Array.Empty<RuleStarterDeckDto>())
            {
                var template = starter;
                templates.Add(Action(starter.name + "  •  " + starter.strategy, () =>
                    BuildDeckEditor(decks, inventory, rules, new DeckDto
                    {
                        name = template.name,
                        cardIds = template.cardIds
                    })));
            }
            saved.Add(templates);
            content.Add(saved);

            var draft = editing == null ? new List<string>() : editing.cardIds.ToList();
'@
Rep @'
            editor.Add(count);
            var draftList = Row();
'@ @'
            editor.Add(count);
            var validation = Label("", 12, Soft);
            editor.Add(validation);
            var draftList = Row();
'@
Rep @'
                if (draft.Count == 0) { SetNotice("Add at least one card."); return; }
                StartCoroutine(api.SaveDeck(session.Token, new SaveDeckRequestDto
'@ @'
                var errors = ClientDeckLegality.Validate(rules, draft);
                if (errors.Count > 0)
                {
                    SetNotice("Deck is not legal: " + string.Join(" ", errors));
                    return;
                }
                StartCoroutine(api.SaveDeck(session.Token, new SaveDeckRequestDto
'@
Rep @'
                count.text = draft.Count + " / 120 cards";
                draftList.Clear();
'@ @'
                var mainCount = ClientDeckLegality.MainCount(rules, draft);
                var goal = rules.deckRules?.minCards ?? 40;
                var commandCount = draft.Count(id => rules.cards.Any(card => card.id == id &&
                    card.type.StartsWith("Command", StringComparison.OrdinalIgnoreCase)));
                var manaCount = draft.Count(id => rules.cards.Any(card => card.id == id &&
                    card.type.StartsWith("ManaSource", StringComparison.OrdinalIgnoreCase)));
                count.text = "Main deck " + mainCount + " / " + goal + " minimum  •  Commander " +
                    commandCount + "/1  •  Mana Source " + manaCount + "/1  •  Total " + draft.Count;
                var errors = ClientDeckLegality.Validate(rules, draft);
                save.SetEnabled(errors.Count == 0);
                validation.text = errors.Count == 0 ? "Deck legal and ready to save." :
                    string.Join("  |  ", errors.Take(5));
                draftList.Clear();
'@
Rep @'
                    if (draft.Count >= 120 || draft.Count(id => id == cardId) >= ownedCount)
                    {
                        SetNotice("No additional owned copies available.");
                        return;
                    }
                    draft.Add(cardId); RefreshDraft();
'@ @'
                    if (draft.Count(id => string.Equals(id, cardId, StringComparison.OrdinalIgnoreCase)) >= ownedCount)
                    {
                        SetNotice("No additional owned copies available.");
                        return;
                    }
                    if (!ClientDeckLegality.CanAdd(rules, draft, cardId, out var restriction))
                    {
                        SetNotice(restriction);
                        return;
                    }
                    draft.Add(cardId); RefreshDraft();
'@
[IO.File]::WriteAllText($path,$s,[Text.UTF8Encoding]::new($false))
Write-Output 'UI Toolkit deck builder now enforces special cards, 40 main and three-copy rules.'
