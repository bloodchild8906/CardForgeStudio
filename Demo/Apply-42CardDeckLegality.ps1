$ErrorActionPreference='Stop'
$root='D:\CardForgeStudio'
$p=Join-Path $root 'Assets\CardForgeStudio\CardForgeDedicatedServer\Runtime\Simulation\Simulation.DeckLegality.cs'
$s=[IO.File]::ReadAllText($p)
function ReplaceStrict([string]$old,[string]$new) {
 if(!$script:s.Contains($old)){ throw "Missing DeckLegality target: $($old.Substring(0,[Math]::Min(75,$old.Length)))"}
 $script:s=$script:s.Replace($old,$new)
}
ReplaceStrict @'
            var known = pack.cards ?? new List<SimCard>();
            var types = rules.typeLimits ?? new List<SimDeckTypeLimit>();
'@ @'
            var known = pack.cards ?? new List<SimCard>();
            var specialZones = pack.zones?.Where(zone =>
                !string.IsNullOrWhiteSpace(zone.deckStartCardType)).ToList()
                ?? new List<SimZone>();
            if (specialZones.GroupBy(zone => zone.deckStartCardType, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() != 1))
                errors.Add("Special deck-start zones must select distinct card types.");
            foreach (var zone in specialZones)
                if (zone.capacity != 1 || !string.Equals(zone.ownerScope, "player",
                    StringComparison.OrdinalIgnoreCase) ||
                    !known.Any(card => TypeMatches(card.type, zone.deckStartCardType)))
                    errors.Add("Special zone " + zone.id + " must be player-owned, one slot and have a known card type.");
            var types = rules.typeLimits ?? new List<SimDeckTypeLimit>();
'@
ReplaceStrict @'
            if (ids.Count < rules.minCards)
                errors.Add("Deck needs at least " + rules.minCards + " cards (has " + ids.Count + ").");
            if (ids.Count > rules.maxCards)
                errors.Add("Deck can contain at most " + rules.maxCards + " cards (has " + ids.Count + ").");
'@ @'
            // Special cards count in the submitted deck list (42 total), but
            // never toward main-deck size (40) or shuffle/draw.
            var specialZones = pack.zones?.Where(zone =>
                !string.IsNullOrWhiteSpace(zone.deckStartCardType)).ToList()
                ?? new List<SimZone>();
            var knownCards = pack.cards ?? new List<SimCard>();
            foreach (var zone in specialZones)
            {
                var specialCount = ids.Count(id => knownCards.Any(card =>
                    string.Equals(card.id, id, StringComparison.OrdinalIgnoreCase) &&
                    TypeMatches(card.type, zone.deckStartCardType)));
                if (specialCount != 1)
                    errors.Add("Deck requires exactly one " + zone.deckStartCardType +
                        " for the " + zone.name + " zone (has " + specialCount + ").");
            }
            var mainCards = ids.Where(id => !knownCards.Any(card =>
                string.Equals(card.id, id, StringComparison.OrdinalIgnoreCase) &&
                specialZones.Any(zone => TypeMatches(card.type, zone.deckStartCardType)))).ToList();
            if (mainCards.Count < rules.minCards)
                errors.Add("Main deck needs at least " + rules.minCards +
                    " cards (has " + mainCards.Count + "; special cards do not count).");
            if (mainCards.Count > rules.maxCards)
                errors.Add("Main deck allows at most " + rules.maxCards +
                    " cards (has " + mainCards.Count + "; special cards do not count).");
'@
ReplaceStrict @'
            if (cards.Count >= rules.maxCards)
            {
                reason = "Deck is full (" + rules.maxCards + ")."; return false;
            }
'@ @'
            var specials = pack.zones?.Where(zone => !string.IsNullOrWhiteSpace(zone.deckStartCardType))
                .ToList() ?? new List<SimZone>();
            var special = specials.FirstOrDefault(zone => TypeMatches(definition.type, zone.deckStartCardType));
            if (special != null)
            {
                if (cards.Any(id => pack.cards.Any(card =>
                    string.Equals(card.id, id, StringComparison.OrdinalIgnoreCase) &&
                    TypeMatches(card.type, special.deckStartCardType))))
                {
                    reason = "Only one " + special.deckStartCardType + " is permitted."; return false;
                }
            }
            else
            {
                var mainCount = cards.Count(id => !pack.cards.Any(card =>
                    string.Equals(card.id, id, StringComparison.OrdinalIgnoreCase) &&
                    specials.Any(zone => TypeMatches(card.type, zone.deckStartCardType))));
                if (mainCount >= rules.maxCards)
                {
                    reason = "Main deck is full (" + rules.maxCards + " cards)."; return false;
                }
            }
'@
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
Write-Output 'Game-wide legality counts 40 main plus one of each special type = 42.'
