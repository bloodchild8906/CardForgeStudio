using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CardForge.Editor
{
#if UNITY_6000_5_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    /// <summary>
    /// Creates the complete Astral Rift showcase through the same JSON-first editor APIs used by designers.
    /// The generator is deterministic and safe to run repeatedly.
    /// </summary>
    public static class CardForgeDemoGenerator
    {
        public const string BlockId = "astral-rift-core";
        public const string PackageId = "astral-rift-demo";
        public const string GameId = "astral-rift";
        public const string Version = "1.0.0";
        const string ShowcaseDeckId = "ar-deck-showcase";
        const string AssetRoot = "Assets/CardForgeData/demo/astral-rift";

        sealed class DemoCard
        {
            public string Id, Name, Type, Affinity, Rarity, Rules, Flavor;
            public int Cost, Power, Toughness;
            public string[] Keywords, Abilities;
            public DemoCard(string id, string name, string type, string affinity, string rarity, int cost, int power, int toughness, string rules, string flavor, string[] keywords, string[] abilities)
            {
                Id = id; Name = name; Type = type; Affinity = affinity; Rarity = rarity;
                Cost = cost; Power = power; Toughness = toughness; Rules = rules; Flavor = flavor;
                Keywords = keywords; Abilities = abilities;
            }
        }

        static readonly DemoCard[] CardSet =
        {
            new DemoCard("ar-card-dawn-captain", "Dawnline Captain", "Unit", "Solar", "Rare", 3, 4, 3, "Vanguard — when deployed, announce the arrival trigger.", "She maps tomorrow by the light it casts backward.", new[]{"ar-keyword-vanguard"}, new[]{"ar-ability-arrival"}),
            new DemoCard("ar-card-ember-fox", "Emberglass Fox", "Unit", "Solar", "Common", 1, 2, 1, "Overcharge — your strike actions ignite this card's ability.", "Its footprints cool into tiny mirrors.", new[]{"ar-keyword-overcharge"}, new[]{"ar-ability-flare"}),
            new DemoCard("ar-card-sunwell-engine", "Sunwell Engine", "Relic", "Solar", "Mythic", 5, 0, 7, "Channel actions resonate through the engine.", "A captive dawn powers an impossible machine.", new[]{"ar-keyword-ward"}, new[]{"ar-ability-channel"}),
            new DemoCard("ar-card-tide-oracle", "Tidebound Oracle", "Unit", "Tidal", "Rare", 3, 2, 5, "Echo — signals through the rift repeat as ability events.", "The sea answers before the question is spoken.", new[]{"ar-keyword-echo"}, new[]{"ar-ability-echo"}),
            new DemoCard("ar-card-moonlit-archive", "Moonlit Archive", "Relic", "Tidal", "Uncommon", 2, 0, 4, "Reclaim — salvage actions awaken the archive.", "No story is lost; some merely wait below the tide.", new[]{"ar-keyword-reclaim"}, new[]{"ar-ability-salvage"}),
            new DemoCard("ar-card-rift-manta", "Rift Manta", "Unit", "Tidal", "Common", 2, 3, 2, "Riftborn — rally actions leave a renown wake.", "It swims between the moments sailors forget.", new[]{"ar-keyword-riftborn"}, new[]{"ar-ability-rally"}),
            new DemoCard("ar-card-verdant-colossus", "Verdant Colossus", "Unit", "Verdant", "Mythic", 6, 7, 8, "Ward and Vanguard.", "Every forest is one thought in its sleeping mind.", new[]{"ar-keyword-ward","ar-keyword-vanguard"}, new[]{"ar-ability-arrival","ar-ability-channel"}),
            new DemoCard("ar-card-seedship", "Starbloom Seedship", "Unit", "Verdant", "Uncommon", 3, 3, 4, "Riftborn — rally beneath a living constellation.", "Its cargo is a forest looking for a world.", new[]{"ar-keyword-riftborn"}, new[]{"ar-ability-rally"}),
            new DemoCard("ar-card-moss-memory", "Moss of Memory", "Relic", "Verdant", "Common", 1, 0, 3, "Reclaim — salvage reveals what stone remembers.", "Touch it gently; the mountain is still dreaming.", new[]{"ar-keyword-reclaim"}, new[]{"ar-ability-salvage"}),
            new DemoCard("ar-card-void-duelist", "Nullwake Duelist", "Unit", "Umbral", "Rare", 4, 5, 3, "Overcharge and Echo.", "A blade drawn in silence cuts the sound first.", new[]{"ar-keyword-overcharge","ar-keyword-echo"}, new[]{"ar-ability-flare","ar-ability-echo"}),
            new DemoCard("ar-card-event-horizon", "Pocket Event Horizon", "Relic", "Umbral", "Mythic", 5, 1, 6, "Banish cards from discard to prove void-zone movement.", "Small enough to carry. Hungry enough to notice.", new string[0], new[]{"ar-ability-void"}),
            new DemoCard("ar-card-whisper-agent", "Whisper Circuit Agent", "Unit", "Umbral", "Uncommon", 2, 2, 3, "Signals and recorded choices resolve through the rules engine.", "Secrets travel fastest when nobody speaks.", new[]{"ar-keyword-echo"}, new[]{"ar-ability-record"})
        };

        public static void GenerateBatch()
        {
            var output = Generate();
            Debug.Log("CARDFORGE_DEMO_PACKAGE=" + output);
        }

        public static string Generate()
        {
            EnsureVisualAssets();
            var store = new CardForgeStore();
            store.Reload();
            var fields = EnsureSchema(store);
            CreateRules(store);
            var block = CreateBlock(store, fields);
            CreateGameDefinition(store, block);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var errors = store.Validate(block);
            if (errors.Count > 0) throw new InvalidOperationException("Astral Rift validation failed:\n" + string.Join("\n", errors));
            var repository = Directory.GetParent(Application.dataPath).FullName;
            var output = Path.Combine(repository, "GamePackages", PackageId + "-" + Version + ".cardforge");
            return CardForgeGamePackageExporter.Export(store, block, store.GetGameDefinition(block), output);
        }

        static Dictionary<string, FieldDefinition> EnsureSchema(CardForgeStore store)
        {
            var result = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);
            AddField(store, result, "title", "Card Name", FieldKind.Text, "New Card", "The card's printed title.");
            AddField(store, result, "cost", "Energy Cost", FieldKind.Number, "1", "Energy required to deploy the card.");
            AddField(store, result, "type", "Card Type", FieldKind.Text, "Unit", "Unit, Relic, Event, or other game type.");
            AddField(store, result, "rules", "Rules Text", FieldKind.Text, "", "Authoritative printed rules text.");
            AddField(store, result, "power", "Power", FieldKind.Number, "0", "Combat power.");
            AddField(store, result, "toughness", "Integrity", FieldKind.Number, "0", "Combat integrity.");
            AddField(store, result, "art", "Artwork", FieldKind.Image, "", "Card illustration asset.");
            AddField(store, result, "affinity", "Affinity", FieldKind.Text, "Neutral", "Solar, Tidal, Verdant, or Umbral.");
            AddField(store, result, "affinity_icon", "Affinity Icon", FieldKind.Icon, "", "Portable affinity icon asset.");
            AddField(store, result, "rarity", "Rarity", FieldKind.Text, "Common", "Common, Uncommon, Rare, or Mythic.");
            AddField(store, result, "flavor", "Flavor Text", FieldKind.Text, "", "World-building text.");
            AddField(store, result, "collector", "Collector Number", FieldKind.Text, "001/012", "Set collector number.");
            AddField(store, result, "active", "Tournament Legal", FieldKind.Boolean, "true", "Demonstrates boolean card data.");
            store.SaveSchema();
            return result;
        }

        static void AddField(CardForgeStore store, Dictionary<string, FieldDefinition> result, string key, string label, FieldKind kind, string defaultValue, string help)
        {
            var field = store.Schema.fields.FirstOrDefault(candidate => string.Equals(candidate.key, key, StringComparison.OrdinalIgnoreCase));
            if (field == null)
            {
                field = new FieldDefinition { id = "ar-field-" + key.Replace('_', '-'), key = key };
                store.Schema.fields.Add(field);
            }
            field.label = label; field.kind = kind; field.defaultValue = defaultValue; field.help = help;
            result[key] = field;
        }

        static BlockDocument CreateBlock(CardForgeStore store, Dictionary<string, FieldDefinition> fields)
        {
            var front = CreateFrontLayout(fields);
            var back = CreateBackLayout();
            var block = new BlockDocument
            {
                id = BlockId,
                code = "ARFT",
                name = "Astral Rift: Convergence",
                description = "A complete CardForgeStudio showcase: authored cards, frames, assets, zones, actions, abilities, keywords, triggers, turn flow, resources, and multiple victory paths.",
                layoutIds = new List<string> { front.id, back.id },
                cardIds = CardSet.Select(card => card.Id).ToList(),
                defaultBackLayoutId = back.id
            };
            store.SaveBlock(block);
            store.SaveLayout(block.id, front);
            store.SaveLayout(block.id, back);
            CreateIcons(store);

            for (var index = 0; index < CardSet.Length; index++)
            {
                var source = CardSet[index];
                var artPath = AssetRoot + "/art/" + source.Id + ".png";
                var iconPath = AssetRoot + "/icons/" + source.Affinity.ToLowerInvariant() + ".png";
                var values = new List<CardFieldValue>();
                AddValue(values, fields["title"], source.Name);
                AddValue(values, fields["cost"], source.Cost.ToString(CultureInfo.InvariantCulture));
                AddValue(values, fields["type"], source.Type + " — " + source.Affinity);
                AddValue(values, fields["rules"], source.Rules);
                AddValue(values, fields["power"], source.Power.ToString(CultureInfo.InvariantCulture));
                AddValue(values, fields["toughness"], source.Toughness.ToString(CultureInfo.InvariantCulture));
                AddValue(values, fields["art"], artPath);
                AddValue(values, fields["affinity"], source.Affinity);
                AddValue(values, fields["affinity_icon"], iconPath);
                AddValue(values, fields["rarity"], source.Rarity);
                AddValue(values, fields["flavor"], source.Flavor);
                AddValue(values, fields["collector"], (index + 1).ToString("000", CultureInfo.InvariantCulture) + "/012 · ARFT");
                AddValue(values, fields["active"], "true");
                var card = new CardDocument
                {
                    id = source.Id,
                    name = source.Name,
                    cardNumber = (index + 1).ToString("000", CultureInfo.InvariantCulture),
                    layoutId = front.id,
                    backLayoutId = back.id,
                    cardType = source.Type,
                    variantId = source.Rarity.ToLowerInvariant(),
                    values = values,
                    keywordIds = source.Keywords.ToList(),
                    abilityIds = source.Abilities.ToList(),
                    requirements = source.Rarity == "Mythic" ? new List<RequirementDefinition>
                    {
                        Requirement("ar-card-req-" + source.Id, fields["rarity"].id, "Equals", "Mythic", "Use the animated foil treatment for Mythic cards.")
                    } : new List<RequirementDefinition>(),
                    zoneOverrides = index == 10 ? new List<ZoneOverride>
                    {
                        new ZoneOverride { zoneId = "ar-zone-art", deltaX = -3, deltaY = -3, deltaWidth = 6, deltaHeight = 6, assetPathOverride = artPath },
                        new ZoneOverride { zoneId = "ar-zone-title", color = "#F2C6FF", textOverride = "POCKET EVENT HORIZON" }
                    } : new List<ZoneOverride>()
                };
                store.SaveCard(block.id, card);
            }
            store.SaveBlock(block);
            return block;
        }

        static LayoutDocument CreateFrontLayout(Dictionary<string, FieldDefinition> fields)
        {
            var titleTemplate = new ZoneTemplateDefinition { id = "ar-template-title", name = "Title glass", backgroundAssetPath = AssetRoot + "/ui/title-panel.png", backgroundColor = "#14213DDD", imageScaleMode = ZoneImageScaleMode.Slice, sliceLeft = 14, sliceRight = 14, sliceTop = 14, sliceBottom = 14 };
            var rulesTemplate = new ZoneTemplateDefinition { id = "ar-template-rules", name = "Rules glass", backgroundAssetPath = AssetRoot + "/ui/rules-panel.png", backgroundColor = "#08111DE8", imageScaleMode = ZoneImageScaleMode.Slice, sliceLeft = 18, sliceRight = 18, sliceTop = 18, sliceBottom = 18 };
            var layout = new LayoutDocument
            {
                id = "ar-layout-front",
                name = "Astral Rift Showcase Front",
                isBack = false,
                width = 360,
                height = 504,
                layoutType = "Standard portrait",
                backgroundAssetPath = AssetRoot + "/frames/front.png",
                backgroundColor = "#07111F",
                templates = new List<ZoneTemplateDefinition> { titleTemplate, rulesTemplate },
                requirements = new List<RequirementDefinition>
                {
                    Requirement("ar-layout-affinity", fields["affinity"].id, "NotEquals", "", "Every showcase card must declare an affinity.")
                }
            };
            layout.zones = new List<ZoneDefinition>
            {
                Zone("ar-zone-title", "Title", ZoneKind.Text, fields["title"].id, 28, 25, 267, 42, 22, true, "MiddleLeft", titleTemplate.id),
                Zone("ar-zone-cost", "Energy cost", ZoneKind.Number, fields["cost"].id, 303, 23, 34, 38, 24, true, "MiddleCenter", titleTemplate.id),
                Zone("ar-zone-art", "Artwork", ZoneKind.Image, fields["art"].id, 31, 76, 298, 205, 14, false, "MiddleCenter", ""),
                Zone("ar-zone-type", "Type line", ZoneKind.Text, fields["type"].id, 32, 286, 244, 29, 14, true, "MiddleLeft", titleTemplate.id),
                Zone("ar-zone-affinity", "Affinity icon", ZoneKind.Icon, fields["affinity_icon"].id, 292, 286, 34, 29, 12, false, "MiddleCenter", titleTemplate.id),
                Zone("ar-zone-rules", "Rules", ZoneKind.Text, fields["rules"].id, 32, 320, 296, 83, 14, false, "UpperLeft", rulesTemplate.id),
                Zone("ar-zone-flavor", "Flavor", ZoneKind.Text, fields["flavor"].id, 42, 402, 276, 43, 11, false, "UpperLeft", rulesTemplate.id),
                Zone("ar-zone-power", "Power", ZoneKind.Number, fields["power"].id, 29, 448, 38, 34, 20, true, "MiddleCenter", titleTemplate.id),
                Zone("ar-zone-toughness", "Integrity", ZoneKind.Number, fields["toughness"].id, 292, 448, 38, 34, 20, true, "MiddleCenter", titleTemplate.id),
                Zone("ar-zone-collector", "Collector", ZoneKind.Text, fields["collector"].id, 91, 460, 178, 18, 9, false, "MiddleCenter", ""),
                new ZoneDefinition { id = "ar-zone-set-mark", name = "Set mark", kind = ZoneKind.StaticText, staticText = "✦ ASTRAL RIFT ✦", x = 112, y = 480, width = 136, height = 14, fontSize = 8, color = "#BFD7FF", alignment = "MiddleCenter", order = 11 }
            };
            var art = layout.zones.First(zone => zone.id == "ar-zone-art");
            art.layers = new List<ZoneLayer>
            {
                new ZoneLayer { id = "ar-layer-art-binding", name = "Bound artwork", order = 0, bindingPath = "art", imageScaleMode = ZoneImageScaleMode.Cover },
                new ZoneLayer { id = "ar-layer-mythic-foil", name = "Mythic prism overlay", order = 1, opacity = .7f, blendMode = "Screen", assetPath = AssetRoot + "/ui/foil-overlay.png", requirements = new List<RequirementDefinition>{ Requirement("ar-layer-mythic", fields["rarity"].id, "Equals", "Mythic", "Mythic foil overlay") } }
            };
            art.variants = new List<ZoneVariant>
            {
                new ZoneVariant { id = "ar-variant-mythic", name = "Mythic full-bleed", layerId = "ar-layer-mythic-foil", x = 27, y = 72, width = 306, height = 213, visible = true, requirements = new List<RequirementDefinition>{ Requirement("ar-variant-mythic-req", fields["rarity"].id, "Equals", "Mythic", "Mythic art treatment") } }
            };
            return layout;
        }

        static LayoutDocument CreateBackLayout()
        {
            return new LayoutDocument
            {
                id = "ar-layout-back",
                name = "Astral Rift Card Back",
                isBack = true,
                width = 360,
                height = 504,
                layoutType = "Card back",
                backgroundAssetPath = AssetRoot + "/frames/back.png",
                backgroundColor = "#020713",
                zones = new List<ZoneDefinition>
                {
                    new ZoneDefinition { id = "ar-zone-back-logo", name = "Game logo", kind = ZoneKind.StaticText, staticText = "ASTRAL\nRIFT", x = 78, y = 188, width = 204, height = 108, fontSize = 35, bold = true, color = "#EACD8D", alignment = "MiddleCenter", order = 0 },
                    new ZoneDefinition { id = "ar-zone-back-tagline", name = "Tagline", kind = ZoneKind.StaticText, staticText = "FORGE A PATH BETWEEN STARS", x = 70, y = 310, width = 220, height = 22, fontSize = 10, color = "#9BC8FF", alignment = "MiddleCenter", order = 1 }
                }
            };
        }

        static ZoneDefinition Zone(string id, string name, ZoneKind kind, string fieldId, float x, float y, float width, float height, float fontSize, bool bold, string alignment, string templateId)
        {
            return new ZoneDefinition { id = id, name = name, kind = kind, fieldId = fieldId, x = x, y = y, width = width, height = height, fontSize = fontSize, bold = bold, alignment = alignment, templateId = templateId, color = "#F4F7FF", order = 0 };
        }

        static RequirementDefinition Requirement(string id, string fieldId, string comparison, string expected, string message)
        {
            return new RequirementDefinition { id = id, fieldId = fieldId, comparison = comparison, expectedValue = expected, message = message };
        }

        static void AddValue(List<CardFieldValue> values, FieldDefinition field, string value)
        {
            values.Add(new CardFieldValue { fieldId = field.id, value = value });
        }

        static void CreateRules(CardForgeStore store)
        {
            var triggers = new[]
            {
                Trigger("ar-trigger-deploy", "A card enters the battlefield", "Card moved", "hand", "battlefield", new[]{"ar-deploy"}, new[]{"ar-ability-arrival"}),
                Trigger("ar-trigger-strike", "A player launches a strike", "Action resolved", "", "", new[]{"ar-strike"}, new[]{"ar-ability-flare"}),
                Trigger("ar-trigger-resource", "A resource changes", "Resource changed", "", "", new[]{"ar-channel","ar-rally","ar-stabilize"}, new[]{"ar-ability-channel","ar-ability-rally"}),
                Trigger("ar-trigger-signal", "A rift signal is emitted", "Custom event", "", "", new[]{"ar-signal","ar-record"}, new[]{"ar-ability-echo","ar-ability-record"}),
                Trigger("ar-trigger-zone", "A card crosses a memory boundary", "Card moved", "", "", new[]{"ar-retire","ar-salvage","ar-banish"}, new[]{"ar-ability-salvage","ar-ability-void"})
            };
            foreach (var trigger in triggers) store.SaveTrigger(trigger);

            var actions = new[]
            {
                Action("ar-channel", "Channel starlight", "Gain 2 energy.", "modify_resource", "ar-trigger-resource", resource:"energy", amount:2),
                Action("ar-draw", "Chart the rift", "Draw one card.", "draw", "", source:"deck", destination:"hand", amount:1),
                Action("ar-deploy", "Deploy card", "Move a card from hand to the battlefield.", "move_card", "ar-trigger-deploy", source:"hand", destination:"battlefield", costResource:"energy", cost:2, condition:"source_in_zone"),
                Action("ar-strike", "Launch strike", "Deal 5 damage to the target player.", "modify_resource", "ar-trigger-strike", resource:"life", amount:-5, target:"target_player", costResource:"energy", cost:1, condition:"resource_at_least", conditionKey:"energy", conditionValue:"1"),
                Action("ar-rally", "Rally the constellations", "Gain 2 renown toward an Ascension victory.", "modify_resource", "ar-trigger-resource", resource:"renown", amount:2, costResource:"energy", cost:1),
                Action("ar-stabilize", "Stabilize the shared rift", "Increase the game-scoped momentum resource.", "modify_resource", "ar-trigger-resource", resource:"momentum", amount:1),
                Action("ar-retire", "Retire from the starfield", "Move one of your battlefield cards to the discard zone.", "move_card", "ar-trigger-zone", source:"battlefield", destination:"discard", condition:"source_in_zone"),
                Action("ar-salvage", "Salvage memory", "Return one of your discarded cards to hand.", "move_card", "ar-trigger-zone", source:"discard", destination:"hand", costResource:"energy", cost:1, condition:"source_in_zone"),
                Action("ar-banish", "Banish memory", "Move one of your discarded cards into the void.", "move_card", "ar-trigger-zone", source:"discard", destination:"void", costResource:"energy", cost:1, condition:"source_in_zone"),
                Action("ar-record", "Record navigation choice", "Store a rules-engine datum after checking an action parameter.", "set_data", "ar-trigger-signal", condition:"parameter_equals", conditionKey:"mode", conditionValue:"beacon", dataKey:"navigationBeacon", dataValue:"lit"),
                Action("ar-signal", "Broadcast rift signal", "Emit a custom authoritative game event.", "emit_event", "ar-trigger-signal", eventType:"astral_rift_signal"),
                Action("ar-next-phase", "Advance phase", "Advance to the next authored phase.", "advance_phase", ""),
                Action("ar-end-turn", "End turn", "Pass priority and begin the next player's Ready phase.", "end_turn", ""),
                Action("ar-concede", "Concede", "Finish the match immediately for demonstration purposes.", "finish_game", "")
            };
            foreach (var action in actions) store.SaveAction(action);

            var abilities = new[]
            {
                Ability("ar-ability-arrival", "Vanguard Arrival", "ar-card-dawn-captain", "ar-deploy", "ar-trigger-deploy", "The unit reports for duty after deployment."),
                Ability("ar-ability-flare", "Overcharge Flare", "ar-card-ember-fox", "ar-strike", "ar-trigger-strike", "The strike releases a visible solar flare."),
                Ability("ar-ability-channel", "Ward Resonance", "ar-card-sunwell-engine", "ar-channel", "ar-trigger-resource", "The ward resonates when energy changes."),
                Ability("ar-ability-rally", "Riftborn Procession", "ar-card-rift-manta", "ar-rally", "ar-trigger-resource", "The riftborn answer a call to rally."),
                Ability("ar-ability-echo", "Echoed Signal", "ar-card-tide-oracle", "ar-signal", "ar-trigger-signal", "The signal echoes as a card ability event."),
                Ability("ar-ability-salvage", "Reclaimed Memory", "ar-card-moonlit-archive", "ar-salvage", "ar-trigger-zone", "A recovered card restores a forgotten memory."),
                Ability("ar-ability-void", "Event Horizon Feed", "ar-card-event-horizon", "ar-banish", "ar-trigger-zone", "The horizon registers a card entering the void."),
                Ability("ar-ability-record", "Whispered Coordinates", "ar-card-whisper-agent", "ar-record", "ar-trigger-signal", "The agent records the chosen navigation beacon.")
            };
            foreach (var ability in abilities) store.SaveAbility(ability);

            var keywords = new[]
            {
                Keyword("ar-keyword-vanguard", "Vanguard", "This card reacts when a card is deployed.", "Deploy this card to resolve its arrival ability.", "ar-ability-arrival", "unit","deployment"),
                Keyword("ar-keyword-overcharge", "Overcharge", "This card reacts to strike actions.", "A strike emits this card's flare event.", "ar-ability-flare", "combat","energy"),
                Keyword("ar-keyword-ward", "Ward", "This card reacts when energy is channeled.", "Channel to demonstrate resource-linked abilities.", "ar-ability-channel", "defense","resource"),
                Keyword("ar-keyword-riftborn", "Riftborn", "This card reacts when its controller rallies.", "Rally to advance the Ascension victory path.", "ar-ability-rally", "renown","victory"),
                Keyword("ar-keyword-echo", "Echo", "This card reacts to custom signal events.", "Signals demonstrate authored event hooks.", "ar-ability-echo", "event","response"),
                Keyword("ar-keyword-reclaim", "Reclaim", "This card reacts when a discarded card returns.", "Salvage demonstrates cross-zone movement.", "ar-ability-salvage", "discard","zone")
            };
            foreach (var keyword in keywords) store.SaveKeyword(keyword);

            var ready = Phase("ar-ready", "Ready", "Refresh planning and gather energy.", 60, 70, "ar-channel", "ar-next-phase");
            var draw = Phase("ar-draw-phase", "Draw", "Draw a card from the ordered deck.", 310, 70, "ar-draw", "ar-next-phase");
            var main = Phase("ar-main", "Main", "Deploy, move cards, write data, and emit events.", 560, 70, "ar-channel", "ar-deploy", "ar-stabilize", "ar-retire", "ar-salvage", "ar-banish", "ar-record", "ar-signal", "ar-next-phase");
            var clash = Phase("ar-clash", "Clash", "Strike an opponent or rally toward Ascension.", 310, 270, "ar-strike", "ar-rally", "ar-next-phase");
            var end = Phase("ar-end", "End", "End the turn or exercise the explicit finish-game hook.", 560, 270, "ar-end-turn", "ar-concede");
            ready.actionRules[0].afterTriggerIds.Add("ar-trigger-resource");
            main.actionRules.First(rule => rule.actionId == "ar-deploy").afterTriggerIds.Add("ar-trigger-deploy");
            clash.actionRules.First(rule => rule.actionId == "ar-strike").afterTriggerIds.Add("ar-trigger-strike");
            var flow = new TurnFlowDocument
            {
                id = "ar-standard-turn",
                name = "Astral Rift five-phase turn",
                description = "Ready, Draw, Main, Clash, and End phases with explicit server action availability.",
                nodes = new List<TurnFlowNode> { ready, draw, main, clash, end },
                links = new List<TurnFlowLink>
                {
                    new TurnFlowLink{from=ready.id,to=draw.id,label="starlight gathered"},
                    new TurnFlowLink{from=draw.id,to=main.id,label="chart complete"},
                    new TurnFlowLink{from=main.id,to=clash.id,label="formations set"},
                    new TurnFlowLink{from=clash.id,to=end.id,label="clash resolved"},
                    new TurnFlowLink{from=end.id,to=ready.id,label="next player"}
                }
            };
            store.SaveTurnFlow(flow);
        }

        static TriggerDocument Trigger(string id, string name, string eventType, string from, string to, string[] actionIds, string[] abilityIds)
        {
            return new TriggerDocument
            {
                id = id, name = name, eventType = eventType, source = "Any game object", fromZone = from, toZone = to,
                condition = "The authoritative action resolves successfully.", responseWindow = "Immediate response window", priority = 10,
                causeActionIds = actionIds.ToList(), causeAbilityIds = abilityIds.ToList(), responseActionIds = new List<string>{"ar-signal"},
                rules = new List<TriggerRule>{ new TriggerRule{id=id+"-rule",subject="Action",property="Resolution",comparison="Equals",expectedValue="Successful",condition="The game is not finished."} }
            };
        }

        static ActionDocument Action(string id, string name, string effect, string kind, string trigger, string source = "", string destination = "", string resource = "", int amount = 0, string target = "", string costResource = "", int cost = 0, string condition = "", string conditionKey = "", string conditionValue = "", string eventType = "", string dataKey = "", string dataValue = "")
        {
            var start = new ResolutionGraphNode { id = id + "-validate", title = "Validate request", detail = "Check phase, player priority, targets, conditions, and costs.", tone = "check", x = 70, y = 90 };
            var apply = new ResolutionGraphNode { id = id + "-apply", title = "Apply authoritative effect", detail = effect, tone = "process", x = 360, y = 90, causesStateChange = true, emitsTriggers = !string.IsNullOrWhiteSpace(trigger) };
            return new ActionDocument
            {
                id = id, name = name, effect = effect, resolution = "Validate, pay costs, apply effect, resolve abilities, emit events, then evaluate victory.",
                condition = string.IsNullOrWhiteSpace(condition) ? "Available in its assigned phase." : condition,
                cost = cost > 0 ? cost + " " + costResource : "No resource cost", targets = string.IsNullOrWhiteSpace(target) ? "Acting player or selected card" : target,
                triggerId = trigger, causeTriggerIds = string.IsNullOrWhiteSpace(trigger) ? new List<string>() : new List<string>{trigger},
                requiresActivePlayer = true, serverEffectKind = kind, sourceZoneId = source, destinationZoneId = destination,
                resourceId = resource, effectAmount = amount, effectTarget = target, eventType = eventType,
                costResourceId = costResource, costAmount = cost, conditionKind = condition, conditionKey = conditionKey, conditionValue = conditionValue,
                dataKey = dataKey, dataValue = dataValue,
                graphNodes = new List<ResolutionGraphNode>{start,apply}, graphLinks = new List<ResolutionGraphLink>{new ResolutionGraphLink{from=start.id,to=apply.id,label="valid"}}
            };
        }

        static AbilityDocument Ability(string id, string name, string cardId, string actionId, string triggerId, string effect)
        {
            var start = new ResolutionGraphNode { id = id + "-listen", title = "Listen", detail = "Match the authored action trigger.", tone = "event", x = 70, y = 80 };
            var resolve = new ResolutionGraphNode { id = id + "-resolve", title = "Resolve", detail = effect, tone = "process", x = 350, y = 80, causesStateChange = true, emitsTriggers = true };
            return new AbilityDocument
            {
                id = id, name = name, cardId = cardId, actionId = actionId, activationTriggerId = triggerId, phaseId = "ar-main",
                condition = "The linked card is the source of the matching action when a source is required.", operation = "Emit an authoritative ability event", effect = effect,
                priority = 20, scope = "Source card and game state", responseTiming = "Immediately after the action", responseResolution = "Resolve in authored priority order",
                responseActionIds = new List<string>{"ar-signal"}, causeTriggerIds = new List<string>{triggerId},
                rules = new List<AbilityRule>{new AbilityRule{id=id+"-rule",target="Game event log",property="ability",operation="Append",value=name,condition="The trigger matched."}},
                graphNodes = new List<ResolutionGraphNode>{start,resolve}, graphLinks = new List<ResolutionGraphLink>{new ResolutionGraphLink{from=start.id,to=resolve.id,label="matched"}}
            };
        }

        static KeywordDocument Keyword(string id, string name, string rules, string reminder, string abilityId, params string[] tags)
        {
            return new KeywordDocument { id = id, name = name, rulesText = rules, reminderText = reminder, abilityIds = new List<string>{abilityId}, tags = tags.ToList() };
        }

        static TurnFlowNode Phase(string id, string name, string check, float x, float y, params string[] actions)
        {
            var node = new TurnFlowNode { id = id, name = name, phase = name, check = check, resolution = "Resolve legal actions in priority order, then advance when requested.", x = x, y = y };
            for (var index = 0; index < actions.Length; index++) node.actionRules.Add(new PhaseActionRule { id = id + "-rule-" + actions[index], actionId = actions[index], availability = "Available during " + name, condition = "Active player has priority.", priority = 100 - index, resolutionOrder = "Priority order" });
            return node;
        }

        static void CreateGameDefinition(CardForgeStore store, BlockDocument block)
        {
            var game = new GameDefinitionDocument
            {
                blockId = block.id, packageId = PackageId, gameId = GameId, packageVersion = Version, gameSystem = "astral-rift-showcase",
                minimumPlayers = 2, maximumPlayers = 4, startingHand = 5, turnFlowId = "ar-standard-turn", defaultDeckId = ShowcaseDeckId,
                zones = new List<GameZoneDocument>
                {
                    new GameZoneDocument{id="deck",name="Constellation Deck",ownerScope="player",visibility="hidden",ordered=true},
                    new GameZoneDocument{id="hand",name="Flight Hand",ownerScope="player",visibility="owner",ordered=true,capacity=9},
                    new GameZoneDocument{id="battlefield",name="Starfield",ownerScope="player",visibility="public",ordered=false,capacity=8},
                    new GameZoneDocument{id="discard",name="Memory Wake",ownerScope="player",visibility="public",ordered=true},
                    new GameZoneDocument{id="void",name="The Void",ownerScope="player",visibility="public",ordered=true},
                    new GameZoneDocument{id="rift",name="Shared Rift",ownerScope="game",visibility="public",ordered=false,capacity=4}
                },
                resources = new List<GameResourceDocument>
                {
                    new GameResourceDocument{id="life",name="Hull Integrity",scope="player",initialValue=15,minimumValue=0,maximumValue=15},
                    new GameResourceDocument{id="energy",name="Starlight Energy",scope="player",initialValue=3,minimumValue=0,maximumValue=12},
                    new GameResourceDocument{id="renown",name="Renown",scope="player",initialValue=0,minimumValue=0,maximumValue=10},
                    new GameResourceDocument{id="momentum",name="Rift Momentum",scope="game",initialValue=0,minimumValue=0,maximumValue=99}
                },
                victoryRules = new List<VictoryRuleDocument>
                {
                    new VictoryRuleDocument{id="ar-last-pilot",name="Last Pilot Standing",kind="last-player-standing",outcome="win",scope="player",resourceId="life",comparison="less-than-or-equal",threshold=0,description="A pilot at zero integrity is eliminated; the last pilot wins."},
                    new VictoryRuleDocument{id="ar-ascension",name="Constellation Ascension",kind="resource-threshold",outcome="win",scope="player",resourceId="renown",comparison="greater-than-or-equal",threshold=8,description="A player with 8 renown wins through Ascension."},
                    new VictoryRuleDocument{id="ar-deck-exhaustion",name="Lost Beyond the Charts",kind="empty-zone",outcome="eliminate",scope="player",zoneId="deck",resourceId="",comparison="equal",threshold=0,description="A player whose deck is empty is eliminated when victory is evaluated."},
                    new VictoryRuleDocument{id="ar-turn-limit",name="Rift Collapse",kind="turn-limit",outcome="win",scope="game",resourceId="",comparison="greater-than-or-equal",threshold=20,description="At turn 20, the acting player wins the collapsed-rift tiebreak."}
                },
                sampleDecks = new List<GameDeckDocument>
                {
                    new GameDeckDocument{id=ShowcaseDeckId,name="Astral Rift Showcase",description="Two copies of every card so each ability can fire within a single match.",minimumCards=24,maximumCopiesPerCard=4,cardIds=ShowcaseDeckCards()},
                    new GameDeckDocument{id="ar-deck-dawn-grove",name="Dawn Grove Expedition",description="Solar and Verdant pilots race toward Ascension.",minimumCards=24,maximumCopiesPerCard=4,cardIds=DeckCards("Solar", "Verdant")},
                    new GameDeckDocument{id="ar-deck-tide-void",name="Tide of the Void",description="Tidal recursion and Umbral disruption pressure every zone.",minimumCards=24,maximumCopiesPerCard=4,cardIds=DeckCards("Tidal", "Umbral")}
                }
            };
            store.SaveGameDefinition(game);
        }

        /// <summary>Builds the default deck holding two copies of every demo card so every ability is reachable.</summary>
        static List<string> ShowcaseDeckCards()
        {
            return CardSet.SelectMany(card => Enumerable.Repeat(card.Id, 2)).ToList();
        }

        static List<string> DeckCards(params string[] affinities)
        {
            var allowed = new HashSet<string>(affinities, StringComparer.OrdinalIgnoreCase);
            return CardSet.Where(card => allowed.Contains(card.Affinity))
                .SelectMany(card => Enumerable.Repeat(card.Id, 4))
                .ToList();
        }

        static void CreateIcons(CardForgeStore store)
        {
            var affinities = new[] { "solar", "tidal", "verdant", "umbral" };
            foreach (var affinity in affinities)
            {
                var icon = new IconDocument
                {
                    id = "ar-icon-" + affinity, name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(affinity) + " affinity", assetPath = AssetRoot + "/icons/" + affinity + ".png",
                    layers = new List<IconLayer>
                    {
                        new IconLayer{shape="Circle",color=AffinityColor(affinity),size=.88f},
                        new IconLayer{shape=affinity=="solar"?"Star":affinity=="tidal"?"Crescent":affinity=="verdant"?"Diamond":"Triangle",color="#F8FBFFFF",size=.48f,rotation=affinity=="umbral"?180:0}
                    }
                };
                store.SaveIcon(icon);
            }
        }

        static string AffinityColor(string affinity)
        {
            if (affinity == "solar") return "#F3A43BFF";
            if (affinity == "tidal") return "#47A9EFFF";
            if (affinity == "verdant") return "#56C271FF";
            return "#9A66D9FF";
        }

        static void EnsureVisualAssets()
        {
            Directory.CreateDirectory(Path.GetFullPath(AssetRoot + "/frames"));
            Directory.CreateDirectory(Path.GetFullPath(AssetRoot + "/art"));
            Directory.CreateDirectory(Path.GetFullPath(AssetRoot + "/icons"));
            Directory.CreateDirectory(Path.GetFullPath(AssetRoot + "/ui"));
            WriteTexture(AssetRoot + "/frames/front.png", 360, 504, (x, y) => FramePixel(x, y, false));
            WriteTexture(AssetRoot + "/frames/back.png", 360, 504, (x, y) => FramePixel(x, y, true));
            WriteTexture(AssetRoot + "/ui/title-panel.png", 64, 64, (x, y) => PanelPixel(x, y, new Color32(21, 42, 70, 235)));
            WriteTexture(AssetRoot + "/ui/rules-panel.png", 64, 64, (x, y) => PanelPixel(x, y, new Color32(6, 17, 31, 240)));
            WriteTexture(AssetRoot + "/ui/foil-overlay.png", 256, 256, FoilPixel);
            var colors = new[] { new Color32(240, 151, 48, 255), new Color32(55, 151, 224, 255), new Color32(52, 176, 91, 255), new Color32(126, 72, 184, 255) };
            var names = new[] { "solar", "tidal", "verdant", "umbral" };
            for (var index = 0; index < names.Length; index++)
            {
                var captured = colors[index];
                var symbol = index;
                WriteTexture(AssetRoot + "/icons/" + names[index] + ".png", 64, 64, (x, y) => IconPixel(x, y, captured, symbol));
            }
            for (var index = 0; index < CardSet.Length; index++)
            {
                var captured = index;
                WriteTexture(AssetRoot + "/art/" + CardSet[index].Id + ".png", 360, 240, (x, y) => ArtPixel(x, y, captured));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void WriteTexture(string path, int width, int height, Func<int, int, Color32> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) pixels[y * width + x] = pixel(x, y);
            texture.SetPixels32(pixels); texture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        static Color32 FramePixel(int x, int y, bool back)
        {
            var edge = Math.Min(Math.Min(x, 359 - x), Math.Min(y, 503 - y));
            if (edge < 8) return new Color32(221, 179, 91, 255);
            if (edge < 13) return new Color32(48, 82, 126, 255);
            var cx = x - 180f; var cy = y - 252f; var radius = Mathf.Sqrt(cx * cx + cy * cy);
            var wave = (Mathf.Sin(radius * .08f + (back ? 1.5f : 0f)) + 1f) * .5f;
            var star = Hash(x / 3, y / 3, back ? 91 : 37) % 173 == 0;
            var baseColor = back ? new Color32(5, 10, 30, 255) : new Color32(8, 20, 38, 255);
            if (star) return new Color32(210, 232, 255, 255);
            return new Color32((byte)(baseColor.r + wave * 12), (byte)(baseColor.g + wave * 18), (byte)(baseColor.b + wave * 28), 255);
        }

        static Color32 PanelPixel(int x, int y, Color32 center)
        {
            var edge = Math.Min(Math.Min(x, 63 - x), Math.Min(y, 63 - y));
            if (edge < 4) return new Color32(188, 219, 255, 220);
            if (edge < 7) return new Color32(68, 112, 158, 230);
            return center;
        }

        static Color32 FoilPixel(int x, int y)
        {
            var band = (x + y) % 96;
            if (band > 28) return new Color32(255, 255, 255, 0);
            var hue = ((x * 3 + y * 5) % 255) / 255f;
            var color = Color.HSVToRGB(hue, .42f, 1f);
            return new Color32((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255), (byte)(75 - band * 2));
        }

        static Color32 IconPixel(int x, int y, Color32 color, int symbol)
        {
            var dx = x - 31.5f; var dy = y - 31.5f; var distance = Mathf.Sqrt(dx * dx + dy * dy);
            if (distance > 30) return new Color32(0, 0, 0, 0);
            if (distance > 26) return new Color32(235, 220, 168, 255);
            var mark = symbol == 0 ? Math.Abs(dx) < 4 || Math.Abs(dy) < 4 || Math.Abs(dx - dy) < 3 || Math.Abs(dx + dy) < 3
                : symbol == 1 ? distance > 10 && distance < 17 && dx < 7
                : symbol == 2 ? Math.Abs(dx) + Math.Abs(dy) < 17
                : dy > -15 && dy < 15 && Math.Abs(dx) < (15 - dy) * .65f;
            return mark ? new Color32(248, 251, 255, 255) : color;
        }

        static Color32 ArtPixel(int x, int y, int index)
        {
            var palettes = new[]
            {
                new[]{new Color32(31,43,74,255),new Color32(242,133,48,255)},
                new[]{new Color32(8,48,79,255),new Color32(48,179,226,255)},
                new[]{new Color32(14,54,45,255),new Color32(75,196,103,255)},
                new[]{new Color32(25,15,52,255),new Color32(155,78,204,255)}
            };
            var affinity = index / 3;
            var palette = palettes[Math.Min(3, affinity)];
            var t = y / 239f;
            var r = Mathf.Lerp(palette[1].r, palette[0].r, t); var g = Mathf.Lerp(palette[1].g, palette[0].g, t); var b = Mathf.Lerp(palette[1].b, palette[0].b, t);
            var cx = 70 + (index * 83) % 230; var cy = 55 + (index * 47) % 120;
            var distance = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            var glow = Mathf.Clamp01(1f - distance / (45 + (index % 3) * 18));
            var horizon = Mathf.Sin((x + index * 31) * .035f) * 17 + 155;
            if (y < horizon && y > horizon - 5) glow = 1f;
            if (Hash(x, y, index * 101 + 7) % 887 == 0) return new Color32(245, 250, 255, 255);
            return new Color32((byte)Mathf.Clamp(r + glow * 90, 0, 255), (byte)Mathf.Clamp(g + glow * 70, 0, 255), (byte)Mathf.Clamp(b + glow * 100, 0, 255), 255);
        }

        static int Hash(int x, int y, int seed)
        {
            unchecked { var value = x * 73856093 ^ y * 19349663 ^ seed * 83492791; return value & int.MaxValue; }
        }
    }
}
