$ErrorActionPreference='Stop'
$p='D:\CardForgeStudio\Assets\CardForgeStudio\CardForgeDedicatedServer\Runtime\Simulation\Simulation.Engine.cs'
$s=[IO.File]::ReadAllText($p)
function ReplaceStrict([string]$old,[string]$new){
 if(!$script:s.Contains($old)){throw "Missing engine section: $($old.Substring(0,[Math]::Min($old.Length,80)))"}
 $script:s=$script:s.Replace($old,$new)
}
ReplaceStrict @'
            EnsureUnique(pack.triggers.Select(t => t.id), "trigger");
            foreach (var phase in pack.phases)
'@ @'
            EnsureUnique(pack.triggers.Select(t => t.id), "trigger");
            EnsureUnique(pack.starterDecks.Select(t => t.id), "starter deck");
            foreach (var zone in pack.zones)
            {
                if (zone.capacity < 0) throw new InvalidOperationException("Negative zone capacity.");
                if (zone.initialCardIds == null) zone.initialCardIds = new List<string>();
                if (zone.allowedCardTypes == null) zone.allowedCardTypes = new List<string>();
                if (zone.capacity > 0 && zone.initialCardIds.Count > zone.capacity)
                    throw new InvalidOperationException("Zone starts over capacity: " + zone.id);
                foreach (var cardId in zone.initialCardIds)
                    ValidateZoneCard(pack, zone, cardId);
            }
            foreach (var starter in pack.starterDecks)
            {
                if (starter.cardIds.Count == 0 || starter.cardIds.Any(id =>
                    !pack.cards.Any(c => Same(c.id, id))))
                    throw new InvalidOperationException("Starter contains unknown cards: " + starter.id);
                foreach (var entry in starter.startingZones)
                {
                    var zone = pack.zones.FirstOrDefault(z => Same(z.id, entry.zoneId));
                    if (zone == null || Same(zone.ownerScope, "game") || entry.count <= 0)
                        throw new InvalidOperationException("Invalid starter zone seed: " + starter.id);
                    ValidateZoneCard(pack, zone, entry.cardId);
                    var total = zone.initialCardIds.Count +
                        starter.startingZones.Where(seed => Same(seed.zoneId, zone.id))
                        .Sum(seed => seed.count);
                    if (zone.capacity > 0 && total > zone.capacity)
                        throw new InvalidOperationException("Starter overfills zone " + zone.id);
                }
            }
            foreach (var phase in pack.phases)
'@
ReplaceStrict @'
        private static void EnsureUnique(IEnumerable<string> ids, string kind)
'@ @'
        private static bool ZoneAllows(SimZone zone, SimCard card)
        {
            if (zone.allowedCardTypes == null || zone.allowedCardTypes.Count == 0)
                return true;
            var cardType = card.type ?? "";
            return zone.allowedCardTypes.Any(allowed =>
                Same(cardType, allowed) ||
                cardType.StartsWith(allowed + " - ", StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateZoneCard(SimRulePack pack, SimZone zone, string cardId)
        {
            var definition = pack.cards.FirstOrDefault(card => Same(card.id, cardId));
            if (definition == null)
                throw new InvalidOperationException("Zone " + zone.id + " references missing card " + cardId);
            if (!ZoneAllows(zone, definition))
                throw new InvalidOperationException("Zone " + zone.id + " rejects card type " +
                    definition.type + " (" + definition.id + ")");
        }

        private static void SeedZone(SimRulePack pack, SimMatch game,
            string owner, SimZone zone, IEnumerable<string> cardIds)
        {
            var destination = FindZone(game, owner, zone.id);
            foreach (var cardId in cardIds)
            {
                ValidateZoneCard(pack, zone, cardId);
                if (zone.capacity > 0 && destination.cards.Count >= zone.capacity)
                    throw new InvalidOperationException("Zone " + zone.id + " starts over capacity.");
                destination.cards.Add(new SimCardInstance(Guid.NewGuid().ToString("N"), cardId, owner));
            }
        }

        private static void EnsureZoneCanReceive(SimRulePack pack, SimMatch game, string owner,
            string zoneId, string cardId)
        {
            var zone = pack.zones.FirstOrDefault(z => Same(z.id, zoneId));
            if (zone == null) throw new InvalidOperationException("Unknown zone " + zoneId);
            ValidateZoneCard(pack, zone, cardId);
            var dest = FindZone(game, owner, zoneId);
            if (zone.capacity > 0 && dest.cards.Count >= zone.capacity)
                throw new InvalidOperationException("Zone is full: " + zoneId);
        }

        private static void EnsureUnique(IEnumerable<string> ids, string kind)
'@
ReplaceStrict @'
                foreach (var zone in pack.zones.Where(z => Same(z.ownerScope, shared ? "game" : "player")))
                    game.zones.Add(new SimZoneState { ownerId = player, zoneId = zone.id });
                foreach (var resource in pack.resources.Where(r => Same(r.scope, shared ? "game" : "player")))
'@ @'
                foreach (var zone in pack.zones.Where(z => Same(z.ownerScope, shared ? "game" : "player")))
                    game.zones.Add(new SimZoneState { ownerId = player, zoneId = zone.id });
                foreach (var zone in pack.zones.Where(z => Same(z.ownerScope, shared ? "game" : "player")))
                    SeedZone(pack, game, player, zone, zone.initialCardIds ?? new List<string>());
                foreach (var resource in pack.resources.Where(r => Same(r.scope, shared ? "game" : "player")))
'@
ReplaceStrict @'
                IList<string> requested;
                requested = decks != null && decks.ContainsKey(player) ?
                    decks[player] : pack.startingDeck;
                if (requested == null || requested.Count == 0)
                    requested = pack.cards.Select(c => c.id).ToList();
                if (requested.Any(id => !pack.cards.Any(c => Same(c.id, id))))
                    throw new InvalidOperationException("Deck contains unknown card definitions.");
                game.playerDecks.Add(new SimPlayerDeck
                {
                    playerId = player, cardIds = requested.ToList()
                });
'@ @'
                var seatIndex = game.players.FindIndex(id => Same(id, player));
                IList<string> requested = decks != null && decks.ContainsKey(player) ?
                    decks[player] : (pack.starterDecks.Count > 0 ?
                        pack.starterDecks[seatIndex % pack.starterDecks.Count].cardIds : pack.startingDeck);
                if (requested == null || requested.Count == 0)
                    requested = pack.cards.Select(c => c.id).ToList();
                if (requested.Any(id => !pack.cards.Any(c => Same(c.id, id))))
                    throw new InvalidOperationException("Deck contains unknown card definitions.");
                var template = pack.starterDecks.FirstOrDefault(starter =>
                    starter.cardIds.Count == requested.Count &&
                    starter.cardIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                        .All(group => requested.Count(id => Same(id, group.Key)) == group.Count()));
                if (template != null)
                    foreach (var seed in template.startingZones)
                    {
                        var zone = pack.zones.First(z => Same(z.id, seed.zoneId));
                        SeedZone(pack, game, player, zone, Enumerable.Repeat(seed.cardId, seed.count));
                    }
                game.playerDecks.Add(new SimPlayerDeck
                {
                    playerId = player, cardIds = requested.ToList(),
                    starterDeckId = template?.id
                });
'@
ReplaceStrict @'
            if (!CanPay(pack, game, actorId, action.costs))
                throw new InvalidOperationException("Insufficient resource pool.");
'@ @'
            if (source != null && !string.IsNullOrWhiteSpace(action.destinationZone))
                EnsureZoneCanReceive(pack, game, actorId, action.destinationZone, source.cardId);
            if (!CanPay(pack, game, actorId, action.costs))
                throw new InvalidOperationException("Insufficient resource pool.");
'@
ReplaceStrict @'
                if (definition != null && definition.capacity > 0 && dest.cards.Count >= definition.capacity) break;
                var card = source.cards[source.cards.Count - 1];
                source.cards.RemoveAt(source.cards.Count - 1);
                dest.cards.Add(card);
'@ @'
                if (definition != null && definition.capacity > 0 && dest.cards.Count >= definition.capacity) break;
                var card = source.cards[source.cards.Count - 1];
                if (definition != null && !ZoneAllows(definition,
                    pack.cards.First(c => Same(c.id, card.cardId)))) break;
                source.cards.RemoveAt(source.cards.Count - 1);
                dest.cards.Add(card);
'@
ReplaceStrict @'
            var definition = pack.zones.FirstOrDefault(z => Same(z.id, to));
            if (definition != null && definition.capacity > 0 && dest.cards.Count >= definition.capacity)
                throw new InvalidOperationException("Target zone is full.");
            source.cards.Remove(card);
'@ @'
            EnsureZoneCanReceive(pack, game, owner, to, card.cardId);
            source.cards.Remove(card);
'@
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
Write-Output 'Implemented type/capacity validation, starting zone seeds, and starter-template recognition.'
