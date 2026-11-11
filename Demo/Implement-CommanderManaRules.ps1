$ErrorActionPreference = 'Stop'
$root = 'D:\CardForgeStudio'
function Edit([string]$relative,[string]$old,[string]$new) {
  $path=Join-Path $root $relative
  $s=[IO.File]::ReadAllText($path)
  if(!$s.Contains($old)){throw "Pattern missing: $relative :: $($old.Substring(0,[Math]::Min(70,$old.Length)))"}
  [IO.File]::WriteAllText($path,$s.Replace($old,$new),[Text.UTF8Encoding]::new($false))
  Write-Output "Updated $relative"
}
$sim='Assets\CardForgeStudio\CardForgeDedicatedServer\Runtime\Simulation\Simulation.Models.cs'
$author='Assets\CardForgeStudio\CardForge\Runtime\CardForgeModels.cs'
$export='Assets\CardForgeStudio\CardForge\Editor\CardForgeGamePackageExporter.cs'
$engine='Assets\CardForgeStudio\CardForgeDedicatedServer\Runtime\Simulation\Simulation.Engine.cs'
$deck='Assets\CardForgeStudio\CardForgeDedicatedServer\Runtime\Simulation\Simulation.DeckLegality.cs'
Edit $sim @'
        public List<string> initialCardIds = new List<string>();
    }
'@ @'
        public List<string> initialCardIds = new List<string>();
        // Non-empty: exactly one matching card is taken from every player's 42-card list
        // and starts in this zone. Same game-level rule for all decks.
        public string deckStartCardType = "";
        public bool returnOnLeave;
        public bool immovable;
    }
'@
Edit $author @'
        public List<string> initialCardIds = new List<string>();
    }
'@ @'
        public List<string> initialCardIds = new List<string>();
        public string deckStartCardType = "";
        public bool returnOnLeave;
        public bool immovable;
    }
'@
Edit $export @'
                    ",\"initialCardIds\":" + StringArray(zone.initialCardIds ?? new List<string>()) + "}")) + "]";
'@ @'
                    ",\"initialCardIds\":" + StringArray(zone.initialCardIds ?? new List<string>()) +
                    ",\"deckStartCardType\":" + Quote(zone.deckStartCardType ?? "") +
                    ",\"returnOnLeave\":" + Bool(zone.returnOnLeave) +
                    ",\"immovable\":" + Bool(zone.immovable) + "}")) + "]";
'@
Edit $engine @'
                if (zone.capacity > 0 && zone.initialCardIds.Count > zone.capacity)
                    throw new InvalidOperationException("Zone starts over capacity: " + zone.id);
'@ @'
                if (zone.capacity > 0 && zone.initialCardIds.Count > zone.capacity)
                    throw new InvalidOperationException("Zone starts over capacity: " + zone.id);
                if (!string.IsNullOrWhiteSpace(zone.deckStartCardType))
                {
                    if (!Same(zone.ownerScope, "player") || zone.capacity != 1 ||
                        zone.initialCardIds.Count > 0 || !pack.cards.Any(card =>
                            DeckLegality.TypeMatches(card.type, zone.deckStartCardType)))
                        throw new InvalidOperationException(
                            "Special deck-start zone must be player-owned, capacity 1, empty, and reference a real card type: " + zone.id);
                    if (zone.immovable && zone.returnOnLeave)
                        throw new InvalidOperationException("A zone cannot be immovable and return-on-leave.");
                }
'@
Edit $engine @'
                game.playerDecks.Add(new SimPlayerDeck
                {
                    playerId = player, cardIds = requested.ToList(),
                    starterDeckId = template?.id
                });
                var deck = FindZone(game, player, "deck", false);
'@ @'
                game.playerDecks.Add(new SimPlayerDeck
                {
                    playerId = player, cardIds = requested.ToList(),
                    starterDeckId = template?.id
                });
                // The full deck list has 42 cards, but the Commander and Mana Source
                // never enter the shuffled main deck. Both are seeded by shared zones.
                var mainCards = requested.ToList();
                foreach (var special in pack.zones.Where(zone =>
                    Same(zone.ownerScope, "player") && !string.IsNullOrWhiteSpace(zone.deckStartCardType)))
                {
                    var startingCard = mainCards.First(id => pack.cards.Any(card =>
                        Same(card.id, id) &&
                        DeckLegality.TypeMatches(card.type, special.deckStartCardType)));
                    mainCards.Remove(startingCard);
                    SeedZone(pack, game, player, special, new[] { startingCard });
                }
                var deck = FindZone(game, player, "deck", false);
'@
Edit $engine @'
                foreach (var card in requested)
                    deck.cards.Add(new SimCardInstance(Guid.NewGuid().ToString("N"), card, player));
'@ @'
                foreach (var card in mainCards)
                    deck.cards.Add(new SimCardInstance(Guid.NewGuid().ToString("N"), card, player));
'@
Edit $engine @'
        private static void Move(SimRulePack pack, SimMatch game, string owner, string cardId, string from, string to)
        {
            if (string.IsNullOrWhiteSpace(cardId)) throw new InvalidOperationException("Card instance must be selected.");
            var source = FindZone(game, owner, from);
            var dest = FindZone(game, owner, to);
            var card = source.cards.FirstOrDefault(c => Same(c.instanceId, cardId));
            if (card == null) throw new InvalidOperationException("Card not in the source zone.");
            EnsureZoneCanReceive(pack, game, owner, to, card.cardId);
            source.cards.Remove(card);
            dest.cards.Add(card);
        }
'@ @'
        private static void Move(SimRulePack pack, SimMatch game, string owner, string cardId, string from, string to)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                throw new InvalidOperationException("Card instance must be selected.");
            var source = FindZone(game, owner, from);
            var card = source.cards.FirstOrDefault(c => Same(c.instanceId, cardId));
            if (card == null) throw new InvalidOperationException("Card not in the source zone.");
            var definition = pack.cards.First(c => Same(c.id, card.cardId));
            var home = pack.zones.FirstOrDefault(zone => !string.IsNullOrWhiteSpace(zone.deckStartCardType) &&
                DeckLegality.TypeMatches(definition.type, zone.deckStartCardType));
            if (home != null && home.immovable)
                throw new InvalidOperationException("Mana Sources cannot be moved or removed.");
            // Commander can only leave Command through casting onto the battlefield.
            if (home != null && Same(from, home.id) && !Same(to, "battlefield"))
                throw new InvalidOperationException("Commander must be cast onto the battlefield.");
            // Removal, exile and return-to-hand effects instead return a cast Commander home.
            if (home != null && home.returnOnLeave && Same(from, "battlefield") &&
                !Same(to, "battlefield"))
                to = home.id;
            if (Same(from, to)) return;
            EnsureZoneCanReceive(pack, game, owner, to, card.cardId);
            var dest = FindZone(game, owner, to);
            source.cards.Remove(card);
            dest.cards.Add(card);
            if (home != null && home.returnOnLeave && Same(to, home.id))
                Emit(game, "commander_returned", definition.name + " returned to Command.", owner);
        }
'@
Edit $engine @'
            var dest = FindZone(game, owner, to, false);
            if (source == null || dest == null) return;
            var definition = pack.zones.FirstOrDefault(z => Same(z.id, to));
'@ @'
            var dest = FindZone(game, owner, to, false);
            if (source == null || dest == null) return;
            var originDefinition = pack.zones.FirstOrDefault(z => Same(z.id, from));
            if (originDefinition != null && originDefinition.immovable)
                throw new InvalidOperationException("Cannot draw or move cards from an immovable zone.");
            var definition = pack.zones.FirstOrDefault(z => Same(z.id, to));
'@
Edit $engine @'
            game.activePlayerId = game.players[i];
            game.currentPhaseId = pack.phases[0].id;
            game.turn++;
'@ @'
            game.activePlayerId = game.players[i];
            game.currentPhaseId = pack.phases[0].id;
            game.turn++;
            // Persistent, immovable Mana Source supplies the new active player.
            var manaZone = pack.zones.FirstOrDefault(z => z.immovable &&
                DeckLegality.TypeMatches(z.deckStartCardType, "ManaSource"));
            if (manaZone != null && FindZone(game, game.activePlayerId, manaZone.id, false)?.cards.Count > 0 &&
                pack.resources.Any(resource => Same(resource.id, "energy")))
                ModifyPool(pack, game, game.activePlayerId, "energy", 1);
'@
Write-Output 'Native simulation supports 42-card lists, per-player special zones, Commander casting/return and immovable Mana Source.'
