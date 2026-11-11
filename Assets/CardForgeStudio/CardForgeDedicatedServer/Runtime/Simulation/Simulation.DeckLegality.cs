using System;
using System.Collections.Generic;
using System.Linq;

namespace CardForge.Simulation
{
    /// <summary>
    /// Game-wide deck construction rules. These never vary by starter deck.
    /// Maximum of -1 in individual/type overrides means unrestricted or use global.
    /// A card override maximum of zero explicitly bans that card.
    /// </summary>
    [Serializable]
    public sealed class SimDeckRules
    {
        public int minCards = 1;
        public int maxCards = 120;
        public int maxCopiesPerCard = 4;
        public List<string> allowedCardTypes = new List<string>();
        public List<SimDeckTypeLimit> typeLimits = new List<SimDeckTypeLimit>();
        public List<SimDeckCardLimit> cardLimits = new List<SimDeckCardLimit>();
    }

    [Serializable]
    public sealed class SimDeckTypeLimit
    {
        public string cardType;
        public int minCount;
        public int maxCount = -1;
    }

    [Serializable]
    public sealed class SimDeckCardLimit
    {
        public string cardId;
        public int minCopies;
        public int maxCopies = -1;
    }

    /// <summary>
    /// Authoritative and UI-consumable legality checks. Pure C#, no UnityEngine.
    /// For interactive adds, only upper limits are enforced; saving requires all minima.
    /// </summary>
    public static class DeckLegality
    {
        public static SimDeckRules Rules(SimRulePack pack) =>
            pack?.deckRules ?? new SimDeckRules();

        public static bool TypeMatches(string cardType, string required)
        {
            if (string.IsNullOrWhiteSpace(required) || string.IsNullOrWhiteSpace(cardType))
                return false;
            if (string.Equals(cardType, required, StringComparison.OrdinalIgnoreCase))
                return true;
            return cardType.Length > required.Length &&
                cardType.StartsWith(required, StringComparison.OrdinalIgnoreCase) &&
                !char.IsLetterOrDigit(cardType[required.Length]);
        }

        public static List<string> ValidateDefinition(SimRulePack pack)
        {
            var errors = new List<string>();
            if (pack == null) { errors.Add("Game rules are missing."); return errors; }
            var rules = Rules(pack);
            if (rules.minCards < 1 || rules.maxCards < rules.minCards ||
                rules.maxCards > 1000 || rules.maxCopiesPerCard < 1)
                errors.Add("Deck min/max size or global copy limit is invalid.");
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
            var cards = rules.cardLimits ?? new List<SimDeckCardLimit>();
            if (types.Any(t => t == null || string.IsNullOrWhiteSpace(t.cardType) ||
                t.minCount < 0 || (t.maxCount >= 0 && t.maxCount < t.minCount) ||
                !known.Any(c => TypeMatches(c.type, t.cardType))))
                errors.Add("Deck type requirements contain an unknown type or invalid count.");
            if (types.Where(t => t != null).Select(t => t.cardType)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != types.Count)
                errors.Add("Duplicate deck card-type restrictions.");
            if (cards.Any(c => c == null || string.IsNullOrWhiteSpace(c.cardId) ||
                c.minCopies < 0 || c.maxCopies < -1 ||
                (c.maxCopies >= 0 && c.minCopies > c.maxCopies) ||
                !known.Any(d => string.Equals(d.id, c.cardId, StringComparison.OrdinalIgnoreCase))))
                errors.Add("Deck individual-card restrictions contain unknown IDs or invalid counts.");
            if (cards.Where(c => c != null).Select(c => c.cardId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != cards.Count)
                errors.Add("Duplicate individual-card deck restrictions.");
            var allowed = rules.allowedCardTypes ?? new List<string>();
            if (allowed.Any(type => string.IsNullOrWhiteSpace(type) ||
                !known.Any(card => TypeMatches(card.type, type))))
                errors.Add("Allowed deck types must refer to existing card types.");
            if (allowed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != allowed.Count)
                errors.Add("Duplicate allowed deck card type.");
            foreach (var type in types)
                if (type != null && allowed.Count > 0 &&
                    !known.Any(card => TypeMatches(card.type, type.cardType) &&
                        allowed.Any(a => TypeMatches(card.type, a))) && type.minCount > 0)
                    errors.Add("Required type " + type.cardType + " is excluded by allowed card types.");
            foreach (var card in cards)
                if (card != null && card.minCopies > 0 &&
                    allowed.Count > 0 && known.Any(c => c.id == card.cardId &&
                        !allowed.Any(type => TypeMatches(c.type, type))))
                    errors.Add("Required card " + card.cardId + " is excluded by allowed types.");
            return errors;
        }

        public static List<string> ValidateDeck(SimRulePack pack, IEnumerable<string> cardIds)
        {
            var errors = new List<string>();
            var rules = Rules(pack);
            var ids = cardIds?.ToList() ?? new List<string>();
            if (pack == null) { errors.Add("Rule pack is missing."); return errors; }
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
            var definitions = (pack.cards ?? new List<SimCard>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.id))
                .GroupBy(c => c.id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var counts = ids.Where(id => !string.IsNullOrWhiteSpace(id))
                .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids.Where(id => string.IsNullOrWhiteSpace(id) ||
                !definitions.ContainsKey(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                errors.Add("Unknown card in deck: " + (id ?? "(null)"));
            foreach (var group in counts)
            {
                if (!definitions.TryGetValue(group.Key, out var card)) continue;
                var allowed = rules.allowedCardTypes ?? new List<string>();
                if (allowed.Count > 0 && !allowed.Any(type => TypeMatches(card.type, type)))
                    errors.Add(card.name + " is not an allowed deck card type (" + card.type + ").");
                var overrideRule = (rules.cardLimits ?? new List<SimDeckCardLimit>())
                    .FirstOrDefault(r => string.Equals(r.cardId, group.Key, StringComparison.OrdinalIgnoreCase));
                var maximum = overrideRule == null || overrideRule.maxCopies == -1 ?
                    rules.maxCopiesPerCard : overrideRule.maxCopies;
                if (group.Value > maximum)
                    errors.Add(card.name + " has " + group.Value + " copies; maximum is " + maximum + ".");
            }
            foreach (var restriction in rules.cardLimits ?? new List<SimDeckCardLimit>())
            {
                if (restriction == null) continue;
                var number = counts.TryGetValue(restriction.cardId, out var count) ? count : 0;
                if (number < restriction.minCopies)
                    errors.Add("Deck requires at least " + restriction.minCopies + " copies of " +
                        restriction.cardId + " (has " + number + ").");
            }
            foreach (var requirement in rules.typeLimits ?? new List<SimDeckTypeLimit>())
            {
                if (requirement == null) continue;
                var number = counts.Where(pair => definitions.TryGetValue(pair.Key, out var card) &&
                    TypeMatches(card.type, requirement.cardType)).Sum(pair => pair.Value);
                if (number < requirement.minCount)
                    errors.Add("Deck requires at least " + requirement.minCount + " " +
                        requirement.cardType + " cards (has " + number + ").");
                if (requirement.maxCount >= 0 && number > requirement.maxCount)
                    errors.Add("Deck allows at most " + requirement.maxCount + " " +
                        requirement.cardType + " cards (has " + number + ").");
            }
            return errors;
        }

        public static bool CanAdd(SimRulePack pack, IEnumerable<string> existing,
            string cardId, out string reason)
        {
            if (pack == null) { reason = "Missing game rules."; return false; }
            var rules = Rules(pack);
            var cards = existing?.ToList() ?? new List<string>();
            var definition = pack.cards?.FirstOrDefault(card =>
                string.Equals(card.id, cardId, StringComparison.OrdinalIgnoreCase));
            if (definition == null) { reason = "Card does not exist in this game."; return false; }
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
            if (rules.allowedCardTypes != null && rules.allowedCardTypes.Count > 0 &&
                !rules.allowedCardTypes.Any(type => TypeMatches(definition.type, type)))
            {
                reason = "Card type " + definition.type + " is not allowed in decks."; return false;
            }
            var count = cards.Count(id => string.Equals(id, cardId, StringComparison.OrdinalIgnoreCase));
            var overrideRule = rules.cardLimits?.FirstOrDefault(rule =>
                string.Equals(rule.cardId, cardId, StringComparison.OrdinalIgnoreCase));
            var maximum = overrideRule == null || overrideRule.maxCopies == -1 ?
                rules.maxCopiesPerCard : overrideRule.maxCopies;
            if (count >= maximum)
            {
                reason = definition.name + " is limited to " + maximum + " copies."; return false;
            }
            foreach (var limit in rules.typeLimits ?? new List<SimDeckTypeLimit>())
            {
                if (limit.maxCount < 0 || !TypeMatches(definition.type, limit.cardType)) continue;
                var countOfType = cards.Count(id => pack.cards.Any(card =>
                    string.Equals(card.id, id, StringComparison.OrdinalIgnoreCase) &&
                    TypeMatches(card.type, limit.cardType)));
                if (countOfType >= limit.maxCount)
                {
                    reason = "Maximum " + limit.maxCount + " " + limit.cardType + " cards."; return false;
                }
            }
            reason = "";
            return true;
        }
    }
}
