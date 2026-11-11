using System;
using System.Collections.Generic;
using System.Linq;

namespace TcgServer.Client
{
    /// <summary>
    /// Fast advisory checks for the UI Toolkit deck editor. The Unity Dedicated
    /// Server separately validates the same rulepack and rejects bypass attempts.
    /// Main deck size excludes exactly one card in every deckStartCardType zone.
    /// </summary>
    public static class ClientDeckLegality
    {
        private static bool IsType(string type, string required) =>
            !string.IsNullOrWhiteSpace(type) &&
            !string.IsNullOrWhiteSpace(required) &&
            type.StartsWith(required, StringComparison.OrdinalIgnoreCase) &&
            (type.Length == required.Length || !char.IsLetterOrDigit(type[required.Length]));

        private static bool CardMatches(RulePackDto pack, string id, string type) =>
            pack.cards.Any(card => string.Equals(card.id, id, StringComparison.OrdinalIgnoreCase) &&
                                   IsType(card.type, type));

        private static RuleZoneDto[] Specials(RulePackDto pack) =>
            (pack.zones ?? Array.Empty<RuleZoneDto>())
                .Where(zone => !string.IsNullOrWhiteSpace(zone.deckStartCardType)).ToArray();

        public static int MainCount(RulePackDto pack, IEnumerable<string> ids)
        {
            var specials = Specials(pack);
            return (ids ?? Enumerable.Empty<string>()).Count(id =>
                !specials.Any(zone => CardMatches(pack, id, zone.deckStartCardType)));
        }

        public static List<string> Validate(RulePackDto pack, IEnumerable<string> source)
        {
            var result = new List<string>();
            if (pack == null) { result.Add("Game rules are unavailable."); return result; }
            var ids = (source ?? Enumerable.Empty<string>()).ToArray();
            var rules = pack.deckRules ?? new DeckRulesDto();
            var main = MainCount(pack, ids);
            if (main < rules.minCards)
                result.Add("Add " + (rules.minCards - main) + " main-deck cards.");
            if (main > rules.maxCards)
                result.Add("Main deck exceeds its " + rules.maxCards + "-card limit.");
            foreach (var zone in Specials(pack))
            {
                var count = ids.Count(id => CardMatches(pack, id, zone.deckStartCardType));
                if (count != 1)
                    result.Add("Exactly one " + zone.deckStartCardType + " is required (have " + count + ").");
            }
            var groups = ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var definition = pack.cards.FirstOrDefault(card =>
                    string.Equals(card.id, group.Key, StringComparison.OrdinalIgnoreCase));
                if (definition == null) { result.Add("Unknown card " + group.Key + "."); continue; }
                if ((rules.allowedCardTypes?.Length ?? 0) > 0 &&
                    !rules.allowedCardTypes.Any(type => IsType(definition.type, type)))
                    result.Add(definition.name + " is not an allowed card type.");
                var limited = rules.cardLimits?.FirstOrDefault(entry =>
                    string.Equals(entry.cardId, group.Key, StringComparison.OrdinalIgnoreCase));
                var maximum = limited == null || limited.maxCopies < 0 ?
                    rules.maxCopiesPerCard : limited.maxCopies;
                if (group.Count() > maximum)
                    result.Add(definition.name + " permits only " + maximum + " copies.");
            }
            foreach (var typeLimit in rules.typeLimits ?? Array.Empty<DeckTypeLimitDto>())
            {
                var number = ids.Count(id => CardMatches(pack, id, typeLimit.cardType));
                if (number < typeLimit.minCount)
                    result.Add("Need " + typeLimit.minCount + " " + typeLimit.cardType + " cards.");
                if (typeLimit.maxCount >= 0 && number > typeLimit.maxCount)
                    result.Add("At most " + typeLimit.maxCount + " " + typeLimit.cardType + " cards.");
            }
            foreach (var cardLimit in rules.cardLimits ?? Array.Empty<DeckCardLimitDto>())
                if (ids.Count(id => string.Equals(id, cardLimit.cardId, StringComparison.OrdinalIgnoreCase)) <
                    cardLimit.minCopies)
                    result.Add("Must include " + cardLimit.minCopies + " copies of " + cardLimit.cardId + ".");
            return result;
        }

        public static bool CanAdd(RulePackDto pack, IEnumerable<string> existing,
            string cardId, out string reason)
        {
            if (pack == null) { reason = "Rules unavailable."; return false; }
            var ids = existing.ToList();
            var rules = pack.deckRules ?? new DeckRulesDto();
            var card = pack.cards.FirstOrDefault(c =>
                string.Equals(c.id, cardId, StringComparison.OrdinalIgnoreCase));
            if (card == null) { reason = "Unknown card."; return false; }
            var special = Specials(pack).FirstOrDefault(zone =>
                IsType(card.type, zone.deckStartCardType));
            if (special != null && ids.Any(id => CardMatches(pack, id, special.deckStartCardType)))
            {
                reason = "One " + special.deckStartCardType + " only."; return false;
            }
            if (special == null && MainCount(pack, ids) >= rules.maxCards)
            {
                reason = "Main deck is full."; return false;
            }
            if (rules.allowedCardTypes?.Length > 0 &&
                !rules.allowedCardTypes.Any(type => IsType(card.type, type)))
            {
                reason = "This card type is not permitted."; return false;
            }
            var limit = rules.cardLimits?.FirstOrDefault(entry =>
                string.Equals(entry.cardId, cardId, StringComparison.OrdinalIgnoreCase));
            var max = limit == null || limit.maxCopies < 0 ? rules.maxCopiesPerCard : limit.maxCopies;
            if (ids.Count(id => string.Equals(id, cardId, StringComparison.OrdinalIgnoreCase)) >= max)
            {
                reason = "Maximum " + max + " copies of " + card.name + "."; return false;
            }
            foreach (var typeLimit in rules.typeLimits ?? Array.Empty<DeckTypeLimitDto>())
                if (typeLimit.maxCount >= 0 && IsType(card.type, typeLimit.cardType) &&
                    ids.Count(id => CardMatches(pack, id, typeLimit.cardType)) >= typeLimit.maxCount)
                {
                    reason = "Maximum " + typeLimit.maxCount + " " + typeLimit.cardType + " cards."; return false;
                }
            reason = null;
            return true;
        }
    }
}
