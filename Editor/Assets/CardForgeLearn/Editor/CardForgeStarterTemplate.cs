using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CardForge.Editor;
using UnityEditor;

namespace CardForge.Learn
{
    /// <summary>
    /// "Starter Duel": the smallest complete two-player game that the server accepts.
    /// Ten unit cards, one front and one back layout (flat colours, no art needed), a four-phase turn,
    /// six actions, and two victory rules. Everything is built through the same store APIs as the designers.
    /// The data builders are pure so tests can check them without touching the project.
    /// </summary>
    public static class CardForgeStarterTemplate
    {
        public const string BlockId = "starter-core";
        public const string PackageId = "starter-duel";
        public const string GameId = "starter-duel";
        public const string Version = "1.0.0";
        public const string FlowId = "st-turn";
        public const string FrontLayoutId = "st-layout-front";
        public const string BackLayoutId = "st-layout-back";

        public sealed class StarterCard
        {
            public string Id, Name, Rules;
            public int Cost, Power, Toughness;
            public StarterCard(string id, string name, int cost, int power, int toughness, string rules)
            {
                Id = id; Name = name; Cost = cost; Power = power; Toughness = toughness; Rules = rules;
            }
        }

        public static readonly StarterCard[] Cards =
        {
            new StarterCard("st-sprout", "Sprout", 1, 1, 2, "A small friend."),
            new StarterCard("st-squire", "Squire", 1, 2, 1, "Eager, not careful."),
            new StarterCard("st-wolf", "Bramble Wolf", 2, 3, 2, "Fast and prickly."),
            new StarterCard("st-shield", "Shield Bearer", 2, 1, 4, "Soaks up hits."),
            new StarterCard("st-knight", "Knight", 3, 3, 3, "Dependable."),
            new StarterCard("st-mage", "Fire Mage", 3, 2, 3, "Burns bright, burns briefly."),
            new StarterCard("st-ogre", "Ogre", 4, 5, 4, "Hits like a cart."),
            new StarterCard("st-captain", "Captain", 4, 4, 3, "Leads from the front."),
            new StarterCard("st-golem", "Stone Golem", 5, 5, 6, "Very hard to move."),
            new StarterCard("st-drake", "Ember Drake", 6, 6, 6, "The set's biggest threat.")
        };

        /// <summary>Actions the turn flow uses. Ids are prefixed so they never collide with other games in the project.</summary>
        public static List<ActionDocument> BuildActions()
        {
            return new List<ActionDocument>
            {
                Action("st-gain-energy", "Gather energy", "Gain 1 energy.", "modify_resource", resource: "energy", amount: 1),
                Action("st-draw", "Draw a card", "Draw one card.", "draw", source: "deck", destination: "hand", amount: 1),
                Action("st-deploy", "Deploy a unit", "Move a card from hand to the battlefield.", "move_card", source: "hand", destination: "battlefield", costResource: "energy", cost: 1, condition: "source_in_zone"),
                Action("st-strike", "Strike", "Deal 2 damage to the target player.", "modify_resource", resource: "life", amount: -2, target: "target_player", costResource: "energy", cost: 1, condition: "resource_at_least", conditionKey: "energy", conditionValue: "1"),
                Action("st-next-phase", "Next phase", "Advance to the next phase.", "advance_phase"),
                Action("st-end-turn", "End turn", "Pass the turn to the other player.", "end_turn")
            };
        }

        public static TurnFlowDocument BuildTurnFlow()
        {
            var ready = Phase("st-ready", "Ready", "Gather energy.", 60, 70, "st-gain-energy", "st-next-phase");
            var draw = Phase("st-draw-phase", "Draw", "Draw a card.", 310, 70, "st-draw", "st-next-phase");
            var main = Phase("st-main", "Main", "Deploy units and strike.", 560, 70, "st-deploy", "st-strike", "st-next-phase");
            var end = Phase("st-end", "End", "Pass the turn.", 810, 70, "st-end-turn");
            return new TurnFlowDocument
            {
                id = FlowId,
                name = "Starter four-phase turn",
                description = "Ready, Draw, Main and End.",
                nodes = new List<TurnFlowNode> { ready, draw, main, end },
                links = new List<TurnFlowLink>
                {
                    new TurnFlowLink { from = ready.id, to = draw.id, label = "energy gathered" },
                    new TurnFlowLink { from = draw.id, to = main.id, label = "card drawn" },
                    new TurnFlowLink { from = main.id, to = end.id, label = "done" },
                    new TurnFlowLink { from = end.id, to = ready.id, label = "next player" }
                }
            };
        }

        public static GameDefinitionDocument BuildGameDefinition()
        {
            return new GameDefinitionDocument
            {
                blockId = BlockId, packageId = PackageId, gameId = GameId, packageVersion = Version, gameSystem = "starter-duel",
                minimumPlayers = 2, maximumPlayers = 2, startingHand = 4, turnFlowId = FlowId,
                zones = new List<GameZoneDocument>
                {
                    new GameZoneDocument { id = "deck", name = "Deck", ownerScope = "player", visibility = "hidden", ordered = true },
                    new GameZoneDocument { id = "hand", name = "Hand", ownerScope = "player", visibility = "owner", ordered = true, capacity = 8 },
                    new GameZoneDocument { id = "battlefield", name = "Battlefield", ownerScope = "player", visibility = "public", ordered = false, capacity = 6 },
                    new GameZoneDocument { id = "discard", name = "Discard", ownerScope = "player", visibility = "public", ordered = true }
                },
                resources = new List<GameResourceDocument>
                {
                    new GameResourceDocument { id = "life", name = "Life", scope = "player", initialValue = 20, minimumValue = 0, maximumValue = 20 },
                    new GameResourceDocument { id = "energy", name = "Energy", scope = "player", initialValue = 1, minimumValue = 0, maximumValue = 10 }
                },
                victoryRules = new List<VictoryRuleDocument>
                {
                    new VictoryRuleDocument { id = "st-last-standing", name = "Last player standing", kind = "last-player-standing", outcome = "win", scope = "player", resourceId = "life", comparison = "less-than-or-equal", threshold = 0, description = "A player at zero life is eliminated; the last player wins." },
                    new VictoryRuleDocument { id = "st-deck-out", name = "Out of cards", kind = "empty-zone", outcome = "eliminate", scope = "player", zoneId = "deck", resourceId = "", comparison = "equal", threshold = 0, description = "A player with an empty deck is eliminated." }
                }
            };
        }

        public static LayoutDocument BuildFrontLayout(IDictionary<string, FieldDefinition> fields)
        {
            var layout = new LayoutDocument
            {
                id = FrontLayoutId, name = "Starter front", isBack = false, width = 360, height = 504, layoutType = "Standard portrait",
                backgroundColor = "#1B2A41",
                templates = new List<ZoneTemplateDefinition>(),
                requirements = new List<RequirementDefinition>()
            };
            layout.zones = new List<ZoneDefinition>
            {
                Zone("st-zone-title", "Title", ZoneKind.Text, fields["title"].id, 24, 22, 262, 44, 22, true, "MiddleLeft"),
                Zone("st-zone-cost", "Energy cost", ZoneKind.Number, fields["cost"].id, 300, 22, 36, 44, 24, true, "MiddleCenter"),
                Zone("st-zone-type", "Type line", ZoneKind.Text, fields["type"].id, 24, 74, 312, 26, 14, false, "MiddleLeft"),
                Zone("st-zone-rules", "Rules", ZoneKind.Text, fields["rules"].id, 24, 320, 312, 100, 15, false, "UpperLeft"),
                Zone("st-zone-power", "Power", ZoneKind.Number, fields["power"].id, 24, 440, 48, 40, 22, true, "MiddleCenter"),
                Zone("st-zone-toughness", "Toughness", ZoneKind.Number, fields["toughness"].id, 288, 440, 48, 40, 22, true, "MiddleCenter"),
                new ZoneDefinition { id = "st-zone-mark", name = "Set mark", kind = ZoneKind.StaticText, staticText = "STARTER SET", x = 110, y = 452, width = 140, height = 16, fontSize = 10, color = "#9FB4D0", alignment = "MiddleCenter", order = 6 }
            };
            return layout;
        }

        public static LayoutDocument BuildBackLayout()
        {
            return new LayoutDocument
            {
                id = BackLayoutId, name = "Starter back", isBack = true, width = 360, height = 504, layoutType = "Card back",
                backgroundColor = "#0E1624",
                zones = new List<ZoneDefinition>
                {
                    new ZoneDefinition { id = "st-zone-back-logo", name = "Game logo", kind = ZoneKind.StaticText, staticText = "STARTER\nDUEL", x = 78, y = 190, width = 204, height = 110, fontSize = 34, bold = true, color = "#E8C468", alignment = "MiddleCenter", order = 0 }
                }
            };
        }

        /// <summary>Creates (or refreshes) the Starter Duel content in the project. Safe to run repeatedly.</summary>
        public static BlockDocument Generate()
        {
            var store = new CardForgeStore();
            store.Reload();
            var fields = EnsureSchema(store);

            foreach (var action in BuildActions()) store.SaveAction(action);
            store.SaveTurnFlow(BuildTurnFlow());

            var front = BuildFrontLayout(fields);
            var back = BuildBackLayout();
            var block = new BlockDocument
            {
                id = BlockId,
                code = "STRT",
                name = "Starter Set",
                description = "The smallest complete game: ten units, a four-phase turn and two ways to lose.",
                layoutIds = new List<string> { front.id, back.id },
                cardIds = Cards.Select(card => card.Id).ToList(),
                defaultBackLayoutId = back.id
            };
            store.SaveBlock(block);
            store.SaveLayout(block.id, front);
            store.SaveLayout(block.id, back);

            for (var index = 0; index < Cards.Length; index++)
            {
                var source = Cards[index];
                var values = new List<CardFieldValue>();
                AddValue(values, fields["title"], source.Name);
                AddValue(values, fields["cost"], source.Cost.ToString(CultureInfo.InvariantCulture));
                AddValue(values, fields["type"], "Unit");
                AddValue(values, fields["rules"], source.Rules);
                AddValue(values, fields["power"], source.Power.ToString(CultureInfo.InvariantCulture));
                AddValue(values, fields["toughness"], source.Toughness.ToString(CultureInfo.InvariantCulture));
                store.SaveCard(block.id, new CardDocument
                {
                    id = source.Id,
                    name = source.Name,
                    cardNumber = (index + 1).ToString("000", CultureInfo.InvariantCulture),
                    layoutId = front.id,
                    backLayoutId = back.id,
                    cardType = "Unit",
                    variantId = "common",
                    values = values,
                    keywordIds = new List<string>(),
                    abilityIds = new List<string>(),
                    requirements = new List<RequirementDefinition>(),
                    zoneOverrides = new List<ZoneOverride>()
                });
            }
            store.SaveBlock(block);
            store.SaveGameDefinition(BuildGameDefinition());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var errors = store.Validate(block);
            if (errors.Count > 0) throw new InvalidOperationException("The Starter Duel template did not validate:\n" + string.Join("\n", errors));
            return block;
        }

        /// <summary>Exports the Starter Duel to GamePackages/starter-duel-1.0.0.cardforge and returns the file path.</summary>
        public static string Publish()
        {
            var root = CardForgeLearnPaths.RepoRoot;
            if (root == null) throw new InvalidOperationException("This Unity project is not inside a CardForgeStudio checkout, so there is no GamePackages folder to publish into.");
            var store = new CardForgeStore();
            store.Reload();
            if (!store.Blocks.TryGetValue(BlockId, out var block)) throw new InvalidOperationException("Create the Starter Duel template first.");
            var output = Path.Combine(root, "GamePackages", PackageId + "-" + Version + ".cardforge");
            return CardForgeGamePackageExporter.Export(store, block, store.GetGameDefinition(block), output);
        }

        /// <summary>Finds a schema field by key, adding it only when the project does not have one yet. Existing fields are never changed.</summary>
        static Dictionary<string, FieldDefinition> EnsureSchema(CardForgeStore store)
        {
            var result = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);
            Field(store, result, "title", "Card Name", FieldKind.Text, "New Card", "The card's printed title.");
            Field(store, result, "cost", "Energy Cost", FieldKind.Number, "1", "Energy needed to deploy the card.");
            Field(store, result, "type", "Card Type", FieldKind.Text, "Unit", "The kind of card.");
            Field(store, result, "rules", "Rules Text", FieldKind.Text, "", "Printed rules text.");
            Field(store, result, "power", "Power", FieldKind.Number, "0", "Attack strength.");
            Field(store, result, "toughness", "Toughness", FieldKind.Number, "1", "How much damage the unit survives.");
            store.SaveSchema();
            return result;
        }

        static void Field(CardForgeStore store, Dictionary<string, FieldDefinition> result, string key, string label, FieldKind kind, string defaultValue, string help)
        {
            var field = store.Schema.fields.FirstOrDefault(candidate => string.Equals(candidate.key, key, StringComparison.OrdinalIgnoreCase));
            if (field == null)
            {
                field = new FieldDefinition { id = "st-field-" + key, key = key, label = label, kind = kind, defaultValue = defaultValue, help = help };
                store.Schema.fields.Add(field);
            }
            result[key] = field;
        }

        static void AddValue(List<CardFieldValue> values, FieldDefinition field, string value)
        {
            values.Add(new CardFieldValue { fieldId = field.id, value = value });
        }

        static ZoneDefinition Zone(string id, string name, ZoneKind kind, string fieldId, float x, float y, float width, float height, float fontSize, bool bold, string alignment)
        {
            return new ZoneDefinition { id = id, name = name, kind = kind, fieldId = fieldId, x = x, y = y, width = width, height = height, fontSize = fontSize, bold = bold, alignment = alignment, templateId = "", color = "#F4F7FF", order = 0 };
        }

        static ActionDocument Action(string id, string name, string effect, string kind, string source = "", string destination = "", string resource = "", int amount = 0, string target = "", string costResource = "", int cost = 0, string condition = "", string conditionKey = "", string conditionValue = "")
        {
            var start = new ResolutionGraphNode { id = id + "-validate", title = "Validate request", detail = "Check phase, player priority, targets, conditions and costs.", tone = "check", x = 70, y = 90 };
            var apply = new ResolutionGraphNode { id = id + "-apply", title = "Apply effect", detail = effect, tone = "process", x = 360, y = 90, causesStateChange = true, emitsTriggers = false };
            return new ActionDocument
            {
                id = id, name = name, effect = effect, resolution = "Validate, pay costs, apply the effect, then check victory.",
                condition = string.IsNullOrWhiteSpace(condition) ? "Available in its assigned phase." : condition,
                cost = cost > 0 ? cost + " " + costResource : "No resource cost",
                targets = string.IsNullOrWhiteSpace(target) ? "Acting player or selected card" : target,
                triggerId = "", causeTriggerIds = new List<string>(),
                requiresActivePlayer = true, serverEffectKind = kind, sourceZoneId = source, destinationZoneId = destination,
                resourceId = resource, effectAmount = amount, effectTarget = target, eventType = "",
                costResourceId = costResource, costAmount = cost, conditionKind = condition, conditionKey = conditionKey, conditionValue = conditionValue,
                dataKey = "", dataValue = "",
                graphNodes = new List<ResolutionGraphNode> { start, apply },
                graphLinks = new List<ResolutionGraphLink> { new ResolutionGraphLink { from = start.id, to = apply.id, label = "valid" } }
            };
        }

        static TurnFlowNode Phase(string id, string name, string check, float x, float y, params string[] actions)
        {
            var node = new TurnFlowNode { id = id, name = name, phase = name, check = check, resolution = "Resolve legal actions in order, then advance when requested.", x = x, y = y };
            for (var index = 0; index < actions.Length; index++)
                node.actionRules.Add(new PhaseActionRule { id = id + "-rule-" + actions[index], actionId = actions[index], availability = "Available during " + name, condition = "Active player has priority.", priority = 100 - index, resolutionOrder = "Priority order" });
            return node;
        }
    }
}
