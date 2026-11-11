using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CardForge.Editor
{
    /// <summary>
    /// Deterministic Astral Rift 240-card demo expansion. The twelve legacy cards
    /// remain, and 57 authored cards are generated per faction (228 more).
    /// Every card gets a distinct ability document with an executable effect
    /// composition, not just a flavor/event-only action.
    /// </summary>
    public static partial class CardForgeDemoGenerator
    {
        private static readonly string[] ExpansionAffinities = { "Solar", "Tidal", "Verdant", "Umbral" };
        private static readonly string[] ExpansionQualifiers =
        {
            "Dawnlit", "Brilliant", "Gilded", "Patient", "Restless",
            "Silent", "Farbound", "Echoing", "Boundless", "Glassborn",
            "Wayward", "Eternal", "Starlit", "Luminous", "Whispering",
            "Radiant", "Stormforged", "Ancient", "Unbroken"
        };
        private static readonly string[][] ExpansionTitles =
        {
            new[] { "Vanguard", "Sunpiercer", "Standard" },
            new[] { "Wayfinder", "Undertow", "Tidekeeper" },
            new[] { "Warden", "Seedbearer", "Rootsinger" },
            new[] { "Shade", "Nullblade", "Whisperer" }
        };
        private static readonly string[] StrategyNames =
        {
            "Solar Vanguard", "Tidal Archive", "Verdant Sanctuary", "Umbral Erosion"
        };
        private static readonly string[] StrategyDescriptions =
        {
            "Aggro-tempo: deploy inexpensive units, convert energy into strikes and close on renown.",
            "Control-recursion: draw, mill an opponent, and return cards from discard for sustained value.",
            "Ramp-and-shields: build resource advantages, protect hull integrity, then scale with board presence.",
            "Attrition-disruption: drain energy, mill decks, remove opposing units and finish through pressure."
        };

        private static DemoCard[] GenerateExtraCards()
        {
            var generated = new List<DemoCard>(228);
            for (var affinityIndex = 0; affinityIndex < 4; affinityIndex++)
            for (var index = 0; index < 57; index++)
            {
                var affinity = ExpansionAffinities[affinityIndex];
                var id = ExpansionCardId(affinityIndex, index);
                var name = ExpansionQualifiers[index % 19] + " " +
                    ExpansionTitles[affinityIndex][index / 19];
                // Four dedicated Mana Sources replace four Relics in the 240-card set.
                var type = index == 0 ? "Command" : index == 46 ? "ManaSource" :
                    index <= 34 ? "Unit" : index <= 45 ? "Relic" : "Event";
                var rarity = index % 17 == 0 ? "Mythic" : index % 7 == 0 ?
                    "Rare" : index % 3 == 0 ? "Uncommon" : "Common";
                var cost = type == "Command" ? 3 : type == "ManaSource" ? 0 :
                    type == "Unit" ? 1 + (index % 6) :
                    type == "Relic" ? 2 + (index % 4) : 1 + (index % 4);
                if (type == "ManaSource")
                    name = affinity + " Eternal Mana Source";
                var power = type == "Unit" ? 1 + ((index + affinityIndex) % 6) : 0;
                var toughness = type == "Unit" ? 1 + ((index * 3 + affinityIndex) % 6) :
                    type == "Relic" ? 2 + index % 5 : 0;
                var card = new DemoCard(id, name, type, affinity, rarity,
                    cost, power, toughness, "Ability authored by the Astral Rift 240-card generator.",
                    "No constellation follows the same course twice.",
                    Array.Empty<string>(), Array.Empty<string>());
                generated.Add(card);
            }
            if (generated.Count != 228)
                throw new InvalidOperationException("Astral Rift expansion must generate exactly 228 cards.");
            return generated.ToArray();
        }

        private static string ExpansionCardId(int affinity, int index) =>
            "ar-card-" + ExpansionAffinities[affinity].ToLowerInvariant() + "-" +
            (index + 1).ToString("000", CultureInfo.InvariantCulture);

        private static string UniqueAbilityId(string cardId) => "ar-ability-unique-" + cardId;

        private static AbilityEffectDefinition ResourceEffect(string resource, long amount,
            string target = "")
        {
            return new AbilityEffectDefinition
            {
                kind = "modify_resource", resourceId = resource, amount = amount, target = target
            };
        }

        private static AbilityEffectDefinition AbilityEffect(string kind, int amount,
            string resource = "", string target = "")
        {
            return new AbilityEffectDefinition
            {
                kind = kind, amount = amount, resourceId = resource, target = target
            };
        }

        private static string DescribeEffect(AbilityEffectDefinition effect)
        {
            switch (effect.kind)
            {
                case "draw": return "draw " + effect.amount;
                case "mill": return "mill an opponent for " + effect.amount;
                case "reclaim": return "recover " + effect.amount + " from discard";
                case "destroy_target": return "retire an opposing unit";
                case "drain_resource": return "steal " + effect.amount + " " + effect.resourceId;
                case "scale_resource": return (effect.amount < 0 ? "deal " : "gain ") +
                    Math.Abs(effect.amount) + " " + effect.resourceId + " per friendly unit";
                case "modify_resource": return (effect.amount < 0 ? "reduce " : "gain ") +
                    Math.Abs(effect.amount) + " " + effect.resourceId +
                    (effect.target == "opponent" ? " against an opponent" :
                     effect.target == "enemy_team" ? " against the enemy team" : "");
                default: return effect.kind;
            }
        }

        private static void CreateUniqueCardAbilities(CardForgeStore store)
        {
            for (var index = 0; index < CardSet.Length; index++)
            {
                var card = CardSet[index];
                var affinity = Array.FindIndex(ExpansionAffinities,
                    name => string.Equals(name, card.Affinity, StringComparison.OrdinalIgnoreCase));
                if (affinity < 0) affinity = 0;

                var complexity = index % 6;
                var power = 1 + (index % 3);
                var effects = new List<AbilityEffectDefinition>();
                switch (affinity)
                {
                    case 0: // Solar: aggressive tempo and renown.
                        switch (index % 6)
                        {
                            case 0: effects.Add(ResourceEffect("energy", power)); break;
                            case 1: effects.Add(ResourceEffect("life", -power, "opponent")); break;
                            case 2: effects.Add(ResourceEffect("renown", 1)); break;
                            case 3: effects.Add(ResourceEffect("shield", power)); break;
                            case 4: effects.Add(AbilityEffect("scale_resource", -1, "life", "opponent")); break;
                            default: effects.Add(ResourceEffect("life", -1, "enemy_team")); break;
                        }
                        break;
                    case 1: // Tidal: draw and recycle with mill pressure.
                        switch (index % 6)
                        {
                            case 0: effects.Add(AbilityEffect("draw", 1)); break;
                            case 1: effects.Add(AbilityEffect("reclaim", 1)); break;
                            case 2: effects.Add(AbilityEffect("mill", power, "", "opponent")); break;
                            case 3: effects.Add(ResourceEffect("shield", 2)); break;
                            case 4: effects.Add(AbilityEffect("drain_resource", 1, "energy", "opponent")); break;
                            default: effects.Add(ResourceEffect("energy", 2)); break;
                        }
                        break;
                    case 2: // Verdant: ramp, sustain, grow with board.
                        switch (index % 6)
                        {
                            case 0: effects.Add(ResourceEffect("energy", power)); break;
                            case 1: effects.Add(ResourceEffect("life", power)); break;
                            case 2: effects.Add(ResourceEffect("shield", 2 + power)); break;
                            case 3: effects.Add(AbilityEffect("scale_resource", 1, "energy")); break;
                            case 4: effects.Add(ResourceEffect("renown", 1)); break;
                            default: effects.Add(AbilityEffect("scale_resource", 1, "shield")); break;
                        }
                        break;
                    default: // Umbral: attrition, life drain, denial and removal.
                        switch (index % 6)
                        {
                            case 0: effects.Add(AbilityEffect("mill", power, "", "opponent")); break;
                            case 1: effects.Add(ResourceEffect("life", -power, "opponent")); break;
                            case 2: effects.Add(AbilityEffect("drain_resource", 1, "energy", "opponent")); break;
                            case 3: effects.Add(AbilityEffect("destroy_target", 0, "", "opponent")); break;
                            case 4: effects.Add(AbilityEffect("scale_resource", -1, "life", "opponent")); break;
                            default: effects.Add(ResourceEffect("life", -1, "enemy_team")); break;
                        }
                        break;
                }

                // Cross-faction combinations and conditions introduce progressively
                // higher complexity without giving free direct game-finish effects.
                if (complexity >= 2)
                    effects.Add(index % 4 == 0 ? ResourceEffect("shield", 1) :
                        index % 4 == 1 ? ResourceEffect("momentum", 1) :
                        index % 4 == 2 ? ResourceEffect("energy", 1) :
                        AbilityEffect("mill", 1, "", "opponent"));
                if (complexity >= 4)
                    effects.Add(index % 2 == 0 ? AbilityEffect("reclaim", 1) :
                        ResourceEffect("renown", 1));
                if (complexity == 5)
                    effects.Add(index % 3 == 0 ? AbilityEffect("draw", 1) :
                        ResourceEffect("shield", 2));

                var conditions = new List<AbilityConditionDefinition>();
                if (complexity == 4)
                    conditions.Add(new AbilityConditionDefinition
                    {
                        kind = "turn_at_least", value = "2"
                    });
                else if (complexity == 5)
                    conditions.Add(new AbilityConditionDefinition
                    {
                        kind = "friendly_board_at_least", value = "1"
                    });

                var actionId = card.Type == "ManaSource" ? "" :
                    card.Type == "Command" ? "ar-activate-command" :
                    card.Type == "Relic" ? "ar-install-relic" :
                    card.Type == "Event" ? "ar-cast-event" : "ar-deploy";
                var text = string.Join("; ", effects.Select(DescribeEffect));
                var conditionText = complexity == 4 ? "After turn 1" :
                    complexity == 5 ? "With a friendly unit on the field" : "";
                if (index >= CoreCards.Length)
                    card.Rules = (string.IsNullOrEmpty(conditionText) ? "" : conditionText + ": ") +
                        text + ".";
                var uniqueId = UniqueAbilityId(card.Id);
                var ability = new AbilityDocument
                {
                    id = uniqueId,
                    name = card.Name + " — " + (affinity == 0 ? "Sunforged" :
                        affinity == 1 ? "Echo" : affinity == 2 ? "Bloom" : "Nullwake") + " Protocol",
                    cardId = card.Id, actionId = actionId,
                    activationTriggerId = card.Type == "ManaSource" ? "turn_started" :
                        "ar-trigger-" + (card.Type == "Command" ? "command" :
                        card.Type == "Relic" ? "relic" : card.Type == "Event" ? "event" : "deploy"),
                    effect = text,
                    operation = "Execute server-authoritative ability effects",
                    condition = conditionText,
                    effects = effects, conditions = conditions,
                    priority = 30 + complexity,
                    scope = "Source card and game state",
                    responseTiming = "After its source action resolves"
                };
                store.SaveAbility(ability);
                if (!card.Abilities.Contains(uniqueId))
                    card.Abilities = card.Abilities.Concat(new[] { uniqueId }).ToArray();
            }
        }

        private static List<GameDeckDocument> CreateThreeStarterDecks()
        {
            var list = new List<GameDeckDocument>();
            for (var affinity = 0; affinity < 3; affinity++)
            {
                var label = ExpansionAffinities[affinity];
                var cards = CardSet.Where(card => card.Affinity == label).ToList();
                var selected = cards.Where(c => c.Type == "Unit").Take(28)
                    .Concat(cards.Where(c => c.Type == "Relic").Take(6))
                    .Concat(cards.Where(c => c.Type == "Event").Take(6)).ToList();
                if (selected.Count != 40 || selected.Select(card => card.Id).Distinct().Count() != 40)
                    throw new InvalidOperationException("Invalid starter composition for " + label);
                var deck = new GameDeckDocument
                {
                    id = "ar-deck-" + label.ToLowerInvariant() + "-starter",
                    name = StrategyNames[affinity], strategy = StrategyDescriptions[affinity],
                    description = StrategyDescriptions[affinity],
                    minimumCards = 40, maximumCopiesPerCard = 3,
                    cardIds = selected.Select(card => card.Id)
                        .Concat(new[] { ExpansionCardId(affinity, 0), ExpansionCardId(affinity, 46) })
                        .ToList()
                };
                list.Add(deck);
            }
            var selectedCards = new HashSet<string>(list.SelectMany(deck => deck.cardIds),
                StringComparer.OrdinalIgnoreCase);
            if (list.Count != 3 || list.Any(deck => deck.cardIds.Count != 42) ||
                selectedCards.Count >= CardSet.Length)
                throw new InvalidOperationException("Exactly three 40-unique+2-card starter decks must use a subset of the set.");
            return list;
        }
    }
}
