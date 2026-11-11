using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CardForge.Editor
{
    /// <summary>JSON is authoritative. There are no hidden ScriptableObject databases.</summary>
    public sealed partial class CardForgeStore
    {
        public const string DataRoot = "Assets/CardForgeData";
        public readonly Dictionary<string, BlockDocument> Blocks = new Dictionary<string, BlockDocument>();
        public readonly Dictionary<string, LayoutDocument> Layouts = new Dictionary<string, LayoutDocument>();
        public readonly Dictionary<string, CardDocument> Cards = new Dictionary<string, CardDocument>();
        public readonly Dictionary<string, IconDocument> Icons = new Dictionary<string, IconDocument>();
        public readonly Dictionary<string, TurnFlowDocument> TurnFlows = new Dictionary<string, TurnFlowDocument>();
        public readonly Dictionary<string, TriggerDocument> Triggers = new Dictionary<string, TriggerDocument>();
        public readonly Dictionary<string, ActionDocument> Actions = new Dictionary<string, ActionDocument>();
        public readonly Dictionary<string, AbilityDocument> Abilities = new Dictionary<string, AbilityDocument>();
        public readonly Dictionary<string, KeywordDocument> Keywords = new Dictionary<string, KeywordDocument>();
        public readonly Dictionary<string, GameDefinitionDocument> GameDefinitions = new Dictionary<string, GameDefinitionDocument>();
        public SchemaDocument Schema = new SchemaDocument();

        static string DiskPath(string relative) { return Path.GetFullPath(relative); }
        static string SafeId(string id) { if (string.IsNullOrEmpty(id) || id.Length > 128 || id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_')) throw new InvalidOperationException("Invalid document ID."); return id; }
        static string BlockRoot(string id) { return DataRoot + "/blocks/" + SafeId(id); }
        static string BlockPath(string id) { return BlockRoot(id) + "/block.json"; }
        static string LayoutPath(string blockId, string layoutId) { return BlockRoot(blockId) + "/layouts/" + SafeId(layoutId) + ".json"; }
        static string CardPath(string blockId, string cardId) { return BlockRoot(blockId) + "/cards/" + SafeId(cardId) + ".json"; }
        static string IconPath(string id) { return DataRoot + "/icons/" + SafeId(id) + ".json"; }
        static string TurnFlowPath(string id) { return DataRoot + "/turn-flows/" + SafeId(id) + ".json"; }
        static string ActionPath(string id) { return DataRoot + "/actions/" + SafeId(id) + ".json"; }
        static string AbilityPath(string id) { return DataRoot + "/abilities/" + SafeId(id) + ".json"; }
        static string KeywordPath(string id) { return DataRoot + "/keywords/" + SafeId(id) + ".json"; }
        static string TriggerPath(string id) { return DataRoot + "/triggers/" + SafeId(id) + ".json"; }
        static string GameDefinitionPath(string blockId) { return DataRoot + "/game-definitions/" + SafeId(blockId) + ".json"; }
        public static string IconImagePath(string id) { return DataRoot + "/icons/" + SafeId(id) + ".png"; }

        static T Read<T>(string path) where T : class
        {
            try { return JsonUtility.FromJson<T>(File.ReadAllText(DiskPath(path))); }
            catch (Exception ex) { Debug.LogError("CardForge: invalid JSON at " + path + ": " + ex.Message); return null; }
        }
        static void Write<T>(string path, T data)
        {
            var absolute = DiskPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            var temp = absolute + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));
            File.Copy(temp, absolute, true); File.Delete(temp);
        }
        public void Reload()
        {
            bool firstRun = !File.Exists(DiskPath(DataRoot + "/schema.json"));
            Blocks.Clear(); Layouts.Clear(); Cards.Clear(); Icons.Clear(); TurnFlows.Clear(); Triggers.Clear(); Actions.Clear(); Abilities.Clear(); Keywords.Clear(); GameDefinitions.Clear();
            Directory.CreateDirectory(DiskPath(DataRoot));
            Schema = File.Exists(DiskPath(DataRoot + "/schema.json"))
                ? Read<SchemaDocument>(DataRoot + "/schema.json") : null;
            if (Schema == null && !firstRun) throw new InvalidDataException("CardForge schema.json is invalid. Fix the JSON before reloading.");
            if (Schema == null) { Schema = new SchemaDocument(); SeedFields(); SaveSchema(); }
            if (Schema.fields == null) Schema.fields = new List<FieldDefinition>();
            string blocksDir = DiskPath(DataRoot + "/blocks");
            if (Directory.Exists(blocksDir))
            {
                foreach (var dir in Directory.GetDirectories(blocksDir))
                {
                    var block = Read<BlockDocument>(dir + "/block.json");
                    if (block == null || string.IsNullOrEmpty(block.id)) continue;
                    block.layoutIds = block.layoutIds ?? new List<string>();
                    block.cardIds = block.cardIds ?? new List<string>();
                    Blocks[block.id] = block;
                    LoadEntries(Path.Combine(dir, "layouts"), Layouts, (LayoutDocument x) => x.id);
                    LoadEntries(Path.Combine(dir, "cards"), Cards, (CardDocument x) => x.id);
                }
            }
            LoadEntries(DiskPath(DataRoot + "/icons"), Icons, (IconDocument x) => x.id);
            LoadEntries(DiskPath(DataRoot + "/turn-flows"), TurnFlows, (TurnFlowDocument x) => x.id);
            LoadEntries(DiskPath(DataRoot + "/triggers"), Triggers, (TriggerDocument x) => x.id);
            LoadEntries(DiskPath(DataRoot + "/actions"), Actions, (ActionDocument x) => x.id);
            LoadEntries(DiskPath(DataRoot + "/abilities"), Abilities, (AbilityDocument x) => x.id);
            LoadEntries(DiskPath(DataRoot + "/keywords"), Keywords, (KeywordDocument x) => x.id);
            LoadEntries(DiskPath(DataRoot + "/game-definitions"), GameDefinitions, (GameDefinitionDocument x) => x.blockId);
            foreach (var block in Blocks.Values)
            {
                if (!GameDefinitions.TryGetValue(block.id, out var definition))
                    GameDefinitions[block.id] = CreateDefaultGameDefinition(block);
                else
                    NormalizeGameDefinition(block, definition);
            }
            foreach (var layout in Layouts.Values)
            {
                if (layout.zones == null) layout.zones = new List<ZoneDefinition>();
                layout.zones.RemoveAll(z=>z==null);
                if (layout.requirements == null) layout.requirements = new List<RequirementDefinition>();
                if (layout.templates == null) layout.templates = new List<ZoneTemplateDefinition>();
                layout.templates.RemoveAll(t => t == null);
                foreach (var zone in layout.zones)
                {
                    if (zone.layers == null) zone.layers = new List<ZoneLayer>();
                    if (zone.variants == null) zone.variants = new List<ZoneVariant>();
                    zone.layers.RemoveAll(l=>l==null);zone.variants.RemoveAll(v=>v==null);
                    foreach (var layer in zone.layers)
                    {
                        if (layer.requirements == null) layer.requirements = new List<RequirementDefinition>();
                        layer.requirements.RemoveAll(r => r == null);
                    }
                    foreach (var variant in zone.variants)
                        if (variant.requirements == null) variant.requirements = new List<RequirementDefinition>();
                }
            }
            foreach (var action in Actions.Values) { if (action.name == null) action.name = "Untitled action"; }
            foreach (var action in Actions.Values)
            {
                if (action.causeTriggerIds == null) action.causeTriggerIds = new List<string>();
                action.causeTriggerIds.RemoveAll(id => string.IsNullOrEmpty(id));
                if (action.graphNodes == null) action.graphNodes = new List<ResolutionGraphNode>();
                if (action.graphLinks == null) action.graphLinks = new List<ResolutionGraphLink>();
                action.graphNodes.RemoveAll(n => n == null); action.graphLinks.RemoveAll(l => l == null);
            }
            foreach (var ability in Abilities.Values)
            {
                if (ability.responseActionIds == null) ability.responseActionIds = new List<string>();
                if (ability.causeTriggerIds == null) ability.causeTriggerIds = new List<string>();
                ability.causeTriggerIds.RemoveAll(id => string.IsNullOrEmpty(id));
                if (ability.rules == null) ability.rules = new List<AbilityRule>();
                if (ability.graphNodes == null) ability.graphNodes = new List<ResolutionGraphNode>();
                if (ability.graphLinks == null) ability.graphLinks = new List<ResolutionGraphLink>();
                ability.rules.RemoveAll(r => r == null); ability.graphNodes.RemoveAll(n => n == null); ability.graphLinks.RemoveAll(l => l == null);
            }
            foreach (var keyword in Keywords.Values)
            {
                if (keyword.abilityIds == null) keyword.abilityIds = new List<string>();
                if (keyword.tags == null) keyword.tags = new List<string>();
                keyword.abilityIds.RemoveAll(id => string.IsNullOrEmpty(id));
                keyword.tags.RemoveAll(tag => string.IsNullOrWhiteSpace(tag));
            }
            foreach (var trigger in Triggers.Values)
            {
                if (trigger.responseActionIds == null) trigger.responseActionIds = new List<string>();
                trigger.responseActionIds.RemoveAll(id => string.IsNullOrEmpty(id));
                if (trigger.causeActionIds == null) trigger.causeActionIds = new List<string>();
                trigger.causeActionIds.RemoveAll(id => string.IsNullOrEmpty(id));
                if (trigger.causeAbilityIds == null) trigger.causeAbilityIds = new List<string>();
                trigger.causeAbilityIds.RemoveAll(id => string.IsNullOrEmpty(id));
                if (string.IsNullOrWhiteSpace(trigger.eventType)) trigger.eventType = "State changed";
                if (trigger.rules == null) trigger.rules = new List<TriggerRule>();
                trigger.rules.RemoveAll(rule => rule == null);
            }
            foreach (var trigger in Triggers.Values)
                foreach (var actionId in trigger.causeActionIds)
                    if (Actions.TryGetValue(actionId, out var action) && !action.causeTriggerIds.Contains(trigger.id)) action.causeTriggerIds.Add(trigger.id);
            foreach (var card in Cards.Values)
            {
                if (card.values == null) card.values = new List<CardFieldValue>();
                if (card.zoneOverrides == null) card.zoneOverrides = new List<ZoneOverride>();
                if (card.requirements == null) card.requirements = new List<RequirementDefinition>();
                if (card.keywordIds == null) card.keywordIds = new List<string>();
                if (card.abilityIds == null) card.abilityIds = new List<string>();
                card.keywordIds.RemoveAll(id => string.IsNullOrEmpty(id));
                card.abilityIds.RemoveAll(id => string.IsNullOrEmpty(id));
            }
            foreach (var icon in Icons.Values) {
                if (icon.layers == null) icon.layers = new List<IconLayer>();
                icon.layers.RemoveAll(l=>l==null);
                if(icon.version<2) { foreach(var layer in icon.layers){layer.visible=true;layer.opacity=1;}icon.version=2; }
            }
            foreach (var flow in TurnFlows.Values)
            {
                if (flow.nodes == null) flow.nodes = new List<TurnFlowNode>();
                if (flow.links == null) flow.links = new List<TurnFlowLink>();
                flow.nodes.RemoveAll(n=>n==null);flow.links.RemoveAll(l=>l==null);
                foreach (var node in flow.nodes)
                {
                    if (node.actionRules == null) node.actionRules = new List<PhaseActionRule>();
                    node.actionRules.RemoveAll(rule => rule == null || string.IsNullOrEmpty(rule.actionId));
                    foreach (var rule in node.actionRules)
                    {
                        if (rule.beforeTriggerIds == null) rule.beforeTriggerIds = new List<string>();
                        if (rule.afterTriggerIds == null) rule.afterTriggerIds = new List<string>();
                        rule.beforeTriggerIds.RemoveAll(id => string.IsNullOrEmpty(id));
                        rule.afterTriggerIds.RemoveAll(id => string.IsNullOrEmpty(id));
                    }
                }
            }
            if (firstRun && TurnFlows.Count == 0) CreateStarterWorkflow();
            if (firstRun && Blocks.Count == 0) CreateStarterBlock();
        }
        static void LoadEntries<T>(string dir, Dictionary<string, T> result, Func<T, string> id) where T : class
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                var obj = Read<T>(f);
                if (obj != null && !string.IsNullOrEmpty(id(obj))) result[id(obj)] = obj;
            }
        }

        void SeedFields()
        {
            Schema.fields.Add(new FieldDefinition { key = "title", label = "Card Name", kind = FieldKind.Text, defaultValue = "New Discovery" });
            Schema.fields.Add(new FieldDefinition { key = "cost", label = "Cost", kind = FieldKind.Number, defaultValue = "3" });
            Schema.fields.Add(new FieldDefinition { key = "type", label = "Type", kind = FieldKind.Text, defaultValue = "Creature" });
            Schema.fields.Add(new FieldDefinition { key = "rules", label = "Rules Text", kind = FieldKind.Text, defaultValue = "Describe this card's ability here." });
            Schema.fields.Add(new FieldDefinition { key = "power", label = "Power", kind = FieldKind.Number, defaultValue = "2" });
            Schema.fields.Add(new FieldDefinition { key = "art", label = "Art", kind = FieldKind.Image });
            Schema.fields.Add(new FieldDefinition { key = "faction", label = "Faction Icon", kind = FieldKind.Icon });
        }

        public BlockDocument CreateStarterBlock()
        {
            var block = CreateBlock("Core Set", "CORE");
            var face = CreateLayout(block, "Standard Front", false);
            face.backgroundColor = "#29384B";
            AddZone(face, "Name", ZoneKind.Text, "title", 20, 17, 270, 33, 26, true, "MiddleLeft");
            AddZone(face, "Cost", ZoneKind.Number, "cost", 302, 17, 42, 33, 25, true, "MiddleCenter");
            AddZone(face, "Artwork", ZoneKind.Image, "art", 20, 62, 320, 232, 16, false, "MiddleCenter");
            AddZone(face, "Type", ZoneKind.Text, "type", 22, 304, 315, 29, 17, true, "MiddleLeft");
            AddZone(face, "Rules", ZoneKind.Text, "rules", 23, 344, 313, 110, 17, false, "UpperLeft");
            AddZone(face, "Power", ZoneKind.Number, "power", 282, 461, 53, 29, 24, true, "MiddleCenter");
            SaveLayout(block.id, face);
            var back = CreateLayout(block, "Classic Back", true);
            back.backgroundColor = "#0C1A2A";
            var backTitle = AddZone(back, "Back title", ZoneKind.StaticText, "", 25, 182, 310, 90, 30, true, "MiddleCenter");
            backTitle.staticText = "CARD FORGE";
            SaveLayout(block.id, back);
            block.defaultBackLayoutId = back.id;
            var card = CreateCard(block, face.id);
            card.name = "New Discovery"; card.backLayoutId = back.id;
            SaveCard(block.id, card);
            SaveBlock(block);
            return block;
        }
        ZoneDefinition AddZone(LayoutDocument l, string name, ZoneKind kind, string fieldKey, float x, float y, float w, float h, float font, bool bold, string align)
        {
            var field = Schema.fields.Find(f => f.key == fieldKey);
            var z = new ZoneDefinition { name = name, kind = kind, fieldId = field == null ? "" : field.id,
                x = x, y = y, width = w, height = h, fontSize = font, bold = bold, alignment = align, order = l.zones.Count };
            l.zones.Add(z);
            return z;
        }
        public void SaveSchema() { Write(DataRoot + "/schema.json", Schema); AssetDatabase.ImportAsset(DataRoot + "/schema.json"); }
        public void SaveBlock(BlockDocument b) { Blocks[b.id] = b; Write(BlockPath(b.id), b); AssetDatabase.ImportAsset(BlockPath(b.id)); }
        public void SaveLayout(string blockId, LayoutDocument l)
        { Layouts[l.id] = l; Write(LayoutPath(blockId, l.id), l); AssetDatabase.ImportAsset(LayoutPath(blockId, l.id)); }
        public void SaveCard(string blockId, CardDocument c)
        { Cards[c.id] = c; Write(CardPath(blockId, c.id), c); AssetDatabase.ImportAsset(CardPath(blockId, c.id)); }
        public void SaveIcon(IconDocument i)
        { Icons[i.id] = i; Write(IconPath(i.id), i); AssetDatabase.ImportAsset(IconPath(i.id)); }
        public void SaveTurnFlow(TurnFlowDocument flow)
        { TurnFlows[flow.id] = flow; Write(TurnFlowPath(flow.id), flow); AssetDatabase.ImportAsset(TurnFlowPath(flow.id)); }
        public void SaveTrigger(TriggerDocument trigger)
        {
            trigger.causeActionIds = trigger.causeActionIds ?? new List<string>();
            Triggers[trigger.id] = trigger; Write(TriggerPath(trigger.id), trigger); AssetDatabase.ImportAsset(TriggerPath(trigger.id));
            foreach (var action in Actions.Values)
            {
                action.causeTriggerIds = action.causeTriggerIds ?? new List<string>();
                bool shouldLink = trigger.causeActionIds.Contains(action.id);
                bool isLinked = action.causeTriggerIds.Contains(trigger.id);
                if (shouldLink == isLinked) continue;
                if (shouldLink) action.causeTriggerIds.Add(trigger.id); else action.causeTriggerIds.Remove(trigger.id);
                Write(ActionPath(action.id), action); AssetDatabase.ImportAsset(ActionPath(action.id));
            }
        }
        public void SaveAction(ActionDocument action)
        {
            action.causeTriggerIds = action.causeTriggerIds ?? new List<string>();
            Actions[action.id] = action; Write(ActionPath(action.id), action); AssetDatabase.ImportAsset(ActionPath(action.id));
            foreach (var trigger in Triggers.Values)
            {
                trigger.causeActionIds = trigger.causeActionIds ?? new List<string>();
                bool shouldLink = action.causeTriggerIds.Contains(trigger.id);
                bool isLinked = trigger.causeActionIds.Contains(action.id);
                if (shouldLink == isLinked) continue;
                if (shouldLink) trigger.causeActionIds.Add(action.id); else trigger.causeActionIds.Remove(action.id);
                Write(TriggerPath(trigger.id), trigger); AssetDatabase.ImportAsset(TriggerPath(trigger.id));
            }
        }
        public void SaveAbility(AbilityDocument ability)
        { Abilities[ability.id] = ability; Write(AbilityPath(ability.id), ability); AssetDatabase.ImportAsset(AbilityPath(ability.id)); }
        public void SaveKeyword(KeywordDocument keyword)
        { Keywords[keyword.id] = keyword; Write(KeywordPath(keyword.id), keyword); AssetDatabase.ImportAsset(KeywordPath(keyword.id)); }
        public void SaveGameDefinition(GameDefinitionDocument definition)
        {
            if (definition == null || !Blocks.TryGetValue(definition.blockId, out var block))
                throw new InvalidOperationException("A game definition must reference an existing block.");
            NormalizeGameDefinition(block, definition);
            GameDefinitions[definition.blockId] = definition;
            Write(GameDefinitionPath(definition.blockId), definition);
            AssetDatabase.ImportAsset(GameDefinitionPath(definition.blockId));
        }

        public GameDefinitionDocument GetGameDefinition(BlockDocument block)
        {
            if (!GameDefinitions.TryGetValue(block.id, out var definition))
            {
                definition = CreateDefaultGameDefinition(block);
                GameDefinitions[block.id] = definition;
            }
            NormalizeGameDefinition(block, definition);
            return definition;
        }

        static GameDefinitionDocument CreateDefaultGameDefinition(BlockDocument block)
        {
            var id = Slug(string.IsNullOrWhiteSpace(block.code) ? block.name : block.code);
            var definition = new GameDefinitionDocument
            {
                blockId = block.id,
                packageId = id,
                gameId = id,
                zones = new List<GameZoneDocument>
                {
                    new GameZoneDocument { id = "deck", name = "Deck", visibility = "hidden", ordered = true },
                    new GameZoneDocument { id = "hand", name = "Hand", visibility = "owner", ordered = true },
                    new GameZoneDocument { id = "battlefield", name = "Battlefield", visibility = "public", ordered = false },
                    new GameZoneDocument { id = "discard", name = "Discard", visibility = "public", ordered = true }
                },
                resources = new List<GameResourceDocument> { new GameResourceDocument() },
                victoryRules = new List<VictoryRuleDocument> { new VictoryRuleDocument() }
            };
            return definition;
        }

        void NormalizeGameDefinition(BlockDocument block, GameDefinitionDocument definition)
        {
            definition.blockId = block.id;
            if (string.IsNullOrWhiteSpace(definition.packageId)) definition.packageId = Slug(block.code);
            if (string.IsNullOrWhiteSpace(definition.gameId)) definition.gameId = definition.packageId;
            if (string.IsNullOrWhiteSpace(definition.packageVersion)) definition.packageVersion = "1.0.0";
            if (string.IsNullOrWhiteSpace(definition.gameSystem)) definition.gameSystem = "cardforge-custom";
            definition.minimumPlayers = Math.Max(1, definition.minimumPlayers);
            definition.maximumPlayers = Math.Max(definition.minimumPlayers, definition.maximumPlayers);
            definition.zones = definition.zones ?? new List<GameZoneDocument>();
            definition.resources = definition.resources ?? new List<GameResourceDocument>();
            definition.victoryRules = definition.victoryRules ?? new List<VictoryRuleDocument>();
            definition.sampleDecks = definition.sampleDecks ?? new List<GameDeckDocument>();
            definition.zones.RemoveAll(zone => zone == null);
            definition.resources.RemoveAll(resource => resource == null);
            definition.victoryRules.RemoveAll(rule => rule == null);
            definition.sampleDecks.RemoveAll(deck => deck == null);
            if (definition.zones.Count == 0) definition.zones.AddRange(CreateDefaultGameDefinition(block).zones);
            if (definition.resources.Count == 0) definition.resources.Add(new GameResourceDocument());
            if (definition.victoryRules.Count == 0) definition.victoryRules.Add(new VictoryRuleDocument());
            if (definition.sampleDecks.Count == 0 && block.cardIds.Count > 0)
            {
                definition.sampleDecks.Add(new GameDeckDocument
                {
                    id = "default-deck",
                    name = "Default Deck",
                    minimumCards = block.cardIds.Count,
                    maximumCopiesPerCard = 4,
                    cardIds = new List<string>(block.cardIds)
                });
            }
            if (string.IsNullOrWhiteSpace(definition.defaultDeckId) || !definition.sampleDecks.Any(deck => deck.id == definition.defaultDeckId))
                definition.defaultDeckId = definition.sampleDecks.FirstOrDefault()?.id ?? "";
            if (string.IsNullOrWhiteSpace(definition.turnFlowId) || !TurnFlows.ContainsKey(definition.turnFlowId))
                definition.turnFlowId = TurnFlows.Keys.FirstOrDefault() ?? "";
        }

        public List<string> ValidateGameDefinition(BlockDocument block, GameDefinitionDocument definition)
        {
            NormalizeGameDefinition(block, definition);
            var errors = new List<string>();
            if (!IsPackageId(definition.packageId)) errors.Add("Package ID must use 1-80 letters, numbers, hyphens, or underscores.");
            if (!IsPackageId(definition.gameId)) errors.Add("Game ID must use 1-80 letters, numbers, hyphens, or underscores.");
            var versionParts = (definition.packageVersion ?? "").Split('-')[0].Split('+')[0].Split('.');
            if (versionParts.Length != 3 || versionParts.Any(part => !int.TryParse(part, out _))) errors.Add("Package version must use semantic version form such as 1.0.0.");
            if (definition.minimumPlayers < 1 || definition.maximumPlayers < definition.minimumPlayers) errors.Add("Player limits are invalid.");
            if (definition.zones.Count == 0) errors.Add("At least one gameplay zone is required.");
            if (definition.victoryRules.Count == 0) errors.Add("At least one victory rule is required.");
            AddDuplicateErrors(definition.zones.Select(zone => zone.id), "gameplay zone", errors);
            AddDuplicateErrors(definition.resources.Select(resource => resource.id), "resource", errors);
            AddDuplicateErrors(definition.victoryRules.Select(rule => rule.id), "victory rule", errors);
            AddDuplicateErrors(definition.sampleDecks.Select(deck => deck.id), "sample deck", errors);
            foreach (var resource in definition.resources)
                if (resource.maximumValue > 0 && resource.maximumValue < resource.initialValue) errors.Add("Resource " + resource.id + " starts above its maximum.");
            foreach (var rule in definition.victoryRules)
                if (!string.IsNullOrWhiteSpace(rule.resourceId) && !definition.resources.Any(resource => resource.id == rule.resourceId)) errors.Add("Victory rule " + rule.id + " references missing resource " + rule.resourceId + ".");
            foreach (var deck in definition.sampleDecks)
            {
                if (string.IsNullOrWhiteSpace(deck.id)) errors.Add("Sample deck IDs are required.");
                if (deck.minimumCards < 1) errors.Add("Sample deck " + deck.name + " must require at least one card.");
                if (deck.maximumCopiesPerCard < 1) errors.Add("Sample deck " + deck.name + " must allow at least one copy per card.");
                if (deck.cardIds.Count < deck.minimumCards) errors.Add("Sample deck " + deck.name + " needs at least " + deck.minimumCards + " cards.");
                if (deck.cardIds.Count < definition.startingHand) errors.Add("Sample deck " + deck.name + " has fewer cards than the starting hand.");
                foreach (var cardId in deck.cardIds.Where(cardId => !block.cardIds.Contains(cardId))) errors.Add("Sample deck " + deck.name + " references card outside the block: " + cardId + ".");
                foreach (var copies in deck.cardIds.GroupBy(cardId => cardId, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > deck.maximumCopiesPerCard))
                    errors.Add("Sample deck " + deck.name + " contains " + copies.Count() + " copies of " + copies.Key + "; the limit is " + deck.maximumCopiesPerCard + ".");
            }
            return errors;
        }

        static bool IsPackageId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && char.IsLetterOrDigit(value[0]) && value.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_');
        }

        static void AddDuplicateErrors(IEnumerable<string> ids, string kind, List<string> errors)
        {
            foreach (var duplicate in ids.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                errors.Add("Duplicate " + kind + " ID: " + duplicate.Key);
        }

        public TurnFlowDocument CreateStarterWorkflow()
        {
            var flow = new TurnFlowDocument();
            var phases = new[] { "Untap", "Upkeep", "Draw", "Main", "Combat", "End" };
            var checks = new[] { "Ready cards and refresh resources", "Resolve upkeep triggers", "Draw one card", "Check action permissions", "Declare attackers, blockers, then damage", "Discard to hand limit and pass priority" };
            for (int i = 0; i < phases.Length; i++)
                flow.nodes.Add(new TurnFlowNode { name = phases[i], phase = phases[i], check = checks[i], x = 60 + (i % 3) * 260, y = 70 + (i / 3) * 185 });
            for (int i = 0; i < flow.nodes.Count - 1; i++)
                flow.links.Add(new TurnFlowLink { from = flow.nodes[i].id, to = flow.nodes[i + 1].id, label = "continue" });
            SaveTurnFlow(flow);
            return flow;
        }

        public BlockDocument CreateBlock(string name, string code)
        {
            var b = new BlockDocument { name = name, code = code };
            SaveBlock(b);
            SaveGameDefinition(CreateDefaultGameDefinition(b));
            return b;
        }
        public LayoutDocument CreateLayout(BlockDocument block, string name, bool back)
        {
            var l = new LayoutDocument { name = name, isBack = back };
            block.layoutIds.Add(l.id); SaveLayout(block.id, l); SaveBlock(block);
            return l;
        }
        public CardDocument CreateCard(BlockDocument block, string layoutId)
        {
            var card = new CardDocument { layoutId = layoutId, backLayoutId = block.defaultBackLayoutId,
                cardNumber = (block.cardIds.Count + 1).ToString("000", CultureInfo.InvariantCulture) };
            block.cardIds.Add(card.id); SaveCard(block.id, card); SaveBlock(block);
            return card;
        }
        public void DeleteCard(BlockDocument block, CardDocument card)
        {
            block.cardIds.Remove(card.id); Cards.Remove(card.id);
            DeleteJson(CardPath(block.id, card.id)); SaveBlock(block);
        }
        public void DeleteLayout(BlockDocument block, LayoutDocument layout)
        {
            if (block.cardIds.Any(id => Cards.ContainsKey(id) && (Cards[id].layoutId == layout.id || Cards[id].backLayoutId == layout.id)))
                throw new InvalidOperationException("Layout is assigned to cards. Reassign those cards first.");
            if (block.defaultBackLayoutId == layout.id) block.defaultBackLayoutId = "";
            block.layoutIds.Remove(layout.id); Layouts.Remove(layout.id);
            DeleteJson(LayoutPath(block.id, layout.id)); SaveBlock(block);
        }
        public BlockDocument CloneBlock(BlockDocument original, string name, string code, bool includeCards)
        {
            var clone = CreateBlock(name, code);
            var layoutMap = new Dictionary<string, string>();
            var zoneMap = new Dictionary<string, string>();
            foreach (string id in original.layoutIds)
            {
                if (!Layouts.TryGetValue(id, out var originalLayout)) continue;
                var newLayout = CardForgeData.Copy(originalLayout);
                newLayout.id = CardForgeData.NewId();
                layoutMap[id] = newLayout.id;
                foreach (var z in newLayout.zones)
                {
                    string oldId = z.id;
                    RenewZone(z);
                    zoneMap[oldId] = z.id;
                }
                clone.layoutIds.Add(newLayout.id);
                SaveLayout(clone.id, newLayout);
            }
            clone.defaultBackLayoutId = layoutMap.TryGetValue(original.defaultBackLayoutId ?? "", out var back) ? back : "";
            if (includeCards)
            {
                foreach (string id in original.cardIds)
                {
                    if (!Cards.TryGetValue(id, out var card)) continue;
                    var copy = CardForgeData.Copy(card);
                    copy.id = CardForgeData.NewId();
                    copy.layoutId = layoutMap.TryGetValue(copy.layoutId ?? "", out var face) ? face : "";
                    copy.backLayoutId = layoutMap.TryGetValue(copy.backLayoutId ?? "", out var cb) ? cb : "";
                    copy.zoneOverrides.RemoveAll(o => !zoneMap.ContainsKey(o.zoneId));
                    foreach (var zoneOverride in copy.zoneOverrides)
                        zoneOverride.zoneId = zoneMap[zoneOverride.zoneId];
                    copy.sourceBlockId = original.id;
                    copy.sourceCardId = card.id;
                    clone.cardIds.Add(copy.id);
                    SaveCard(clone.id, copy);
                }
            }
            SaveBlock(clone); return clone;
        }
        public LayoutDocument ImportLayout(BlockDocument destination, LayoutDocument from)
        {
            var l = CardForgeData.Copy(from); l.id = CardForgeData.NewId(); l.name += " (Imported)";
            foreach (var z in l.zones) RenewZone(z);
            destination.layoutIds.Add(l.id); SaveLayout(destination.id, l); SaveBlock(destination);
            return l;
        }
        public CardDocument Reprint(BlockDocument target, BlockDocument source, CardDocument original)
        {
            // Reprints are independent card instances, not shared mutable aliases.
            var c = CardForgeData.Copy(original);
            c.id = CardForgeData.NewId(); c.sourceBlockId = source.id; c.sourceCardId = original.id;
            if (!target.layoutIds.Contains(c.layoutId))
            {
                var firstFace = target.layoutIds.Select(id => Layouts.TryGetValue(id, out var l) ? l : null)
                    .FirstOrDefault(l => l != null && !l.isBack);
                c.layoutId = firstFace == null ? "" : firstFace.id;
                c.zoneOverrides.Clear(); // original overrides refer to another layout's zone IDs
            }
            if (!target.layoutIds.Contains(c.backLayoutId)) c.backLayoutId = target.defaultBackLayoutId;
            target.cardIds.Add(c.id); SaveCard(target.id, c); SaveBlock(target);
            return c;
        }
        public static bool ValidAsset(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            return AssetDatabase.LoadMainAssetAtPath(path) != null;
        }
        public List<string> Validate(BlockDocument block)
        {
            var errors = new List<string>();
            if (block == null) { errors.Add("Select a block."); return errors; }
            block.layoutIds = block.layoutIds ?? new List<string>(); block.cardIds = block.cardIds ?? new List<string>();
            if (string.IsNullOrWhiteSpace(block.code)) errors.Add("Block code is empty.");
            if (block.layoutIds.Count == 0) errors.Add("No layouts exist in this block.");
            if (!string.IsNullOrEmpty(block.defaultBackLayoutId) &&
                (!Layouts.TryGetValue(block.defaultBackLayoutId, out var defaultBack) || !defaultBack.isBack || !block.layoutIds.Contains(defaultBack.id)))
                errors.Add("Default back must refer to a back layout in this block.");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in Schema.fields)
            {
                if (string.IsNullOrWhiteSpace(field.key) || !keys.Add(field.key))
                    errors.Add("Duplicate or empty data key: " + field.key);
            }
            foreach (var id in block.layoutIds)
            {
                if (!Layouts.TryGetValue(id, out var layout)) { errors.Add("Layout file missing: " + id); continue; }
                if (layout.width < 50 || layout.height < 50) errors.Add("Invalid card dimensions in " + layout.name);
                if (!string.IsNullOrEmpty(layout.backgroundAssetPath) && !ValidAsset(layout.backgroundAssetPath))
                    errors.Add("Missing background image: " + layout.backgroundAssetPath);
                var templates = layout.templates ?? new List<ZoneTemplateDefinition>();
                var templateIds = new HashSet<string>(templates.Where(t=>t!=null).Select(t=>t.id));
                foreach (var template in templates)
                    if (template != null && !string.IsNullOrEmpty(template.backgroundAssetPath) && !ValidAsset(template.backgroundAssetPath)) errors.Add("Missing zone template image: " + template.backgroundAssetPath);
                foreach (var requirement in layout.requirements ?? new List<RequirementDefinition>())
                    if (requirement != null && !Schema.fields.Any(f=>f.id==requirement.fieldId)) errors.Add("Missing field for layout requirement in " + layout.name);
                foreach (var zone in layout.zones ?? new List<ZoneDefinition>())
                {
                    if (zone == null) continue;
                    if (!string.IsNullOrEmpty(zone.templateId) && !templateIds.Contains(zone.templateId)) errors.Add("Missing template for zone " + zone.name + " in " + layout.name);
                    if (!string.IsNullOrEmpty(zone.fieldId) && !Schema.fields.Any(f => f.id == zone.fieldId))
                        errors.Add("Missing field for zone " + zone.name + " in " + layout.name);
                    foreach(var layer in zone.layers ?? new List<ZoneLayer>()) {
                        if (layer == null) continue;
                        if (!string.IsNullOrEmpty(layer.assetPath) && !ValidAsset(layer.assetPath)) errors.Add("Missing layer image: " + layer.assetPath);
                        if (!string.IsNullOrEmpty(layer.templateId) && !templateIds.Contains(layer.templateId)) errors.Add("Missing template for layer " + layer.name + " in " + layout.name);
                        foreach (var requirement in layer.requirements ?? new List<RequirementDefinition>())
                            if (requirement != null && !Schema.fields.Any(f=>f.id==requirement.fieldId)) errors.Add("Missing field for layer requirement in " + layer.name);
                    }
                    foreach(var variant in zone.variants ?? new List<ZoneVariant>()) {
                        if (variant == null) continue;
                        if(!string.IsNullOrEmpty(variant.staticAssetPath)&&!ValidAsset(variant.staticAssetPath))errors.Add("Missing variant image: "+variant.staticAssetPath);
                        if (!string.IsNullOrEmpty(variant.layerId) && !(zone.layers??new List<ZoneLayer>()).Any(l=>l!=null&&l.id==variant.layerId)) errors.Add("Missing layer for variant " + variant.name + " in " + layout.name);
                        foreach (var requirement in variant.requirements ?? new List<RequirementDefinition>())
                            if (requirement != null && !Schema.fields.Any(f=>f.id==requirement.fieldId)) errors.Add("Missing field for variant requirement in " + variant.name);
                    }
                    if (!string.IsNullOrEmpty(zone.staticAssetPath) && !ValidAsset(zone.staticAssetPath))
                        errors.Add("Missing zone asset: " + zone.staticAssetPath);
                    if (!string.IsNullOrEmpty(zone.fontAssetPath) && !ValidAsset(zone.fontAssetPath))
                        errors.Add("Missing font asset for zone " + zone.name + ": " + zone.fontAssetPath);
                }
            }
            foreach (var id in block.cardIds)
            {
                if (!Cards.TryGetValue(id, out var card)) { errors.Add("Card file missing: " + id); continue; }
                if (!block.layoutIds.Contains(card.layoutId)) errors.Add(card.name + " references a layout outside this block.");
                if (!string.IsNullOrEmpty(card.backLayoutId) && !block.layoutIds.Contains(card.backLayoutId))
                    errors.Add(card.name + " references a card back outside this block.");
                foreach (var zoneOverride in card.zoneOverrides)
                    if (!string.IsNullOrEmpty(zoneOverride.assetPathOverride) && !ValidAsset(zoneOverride.assetPathOverride))
                        errors.Add(card.name + ": missing custom zone image " + zoneOverride.assetPathOverride);
                foreach (var keywordId in card.keywordIds ?? new List<string>())
                    if (!Keywords.ContainsKey(keywordId)) errors.Add(card.name + " references missing keyword " + keywordId + ".");
                foreach (var abilityId in card.abilityIds ?? new List<string>())
                    if (!Abilities.ContainsKey(abilityId)) errors.Add(card.name + " references missing ability " + abilityId + ".");
                foreach (var field in Schema.fields.Where(f => f.kind == FieldKind.Image || f.kind == FieldKind.Icon))
                {
                    string val = CardForgeData.Value(card, field);
                    if (!string.IsNullOrWhiteSpace(val) && !ValidAsset(val)) errors.Add(card.name + ": missing image " + val);
                }
            }
            errors.AddRange(ValidateGameDefinition(block, GetGameDefinition(block)));
            return errors;
        }

        public string ExportBlock(BlockDocument block, BuildTarget target)
        {
            var errors = Validate(block);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            var jsonPaths = new List<string> { DataRoot + "/schema.json", BlockPath(block.id) };
            jsonPaths.AddRange(block.layoutIds.Select(id => LayoutPath(block.id, id)));
            jsonPaths.AddRange(block.cardIds.Select(id => CardPath(block.id, id)));
            jsonPaths.AddRange(TurnFlows.Keys.Select(TurnFlowPath));
            jsonPaths.AddRange(Triggers.Keys.Select(TriggerPath));
            jsonPaths.AddRange(Actions.Keys.Select(ActionPath));
            jsonPaths.AddRange(Abilities.Keys.Select(AbilityPath));
            jsonPaths.AddRange(Keywords.Keys.Select(KeywordPath));
            var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in block.layoutIds)
            {
                var l = Layouts[id];
                AddPath(images, l.backgroundAssetPath);
                foreach (var template in l.templates ?? new List<ZoneTemplateDefinition>()) AddPath(images, template?.backgroundAssetPath);
                foreach (var zone in l.zones ?? new List<ZoneDefinition>()) {
                    if (zone == null) continue;
                    AddPath(images, zone.staticAssetPath);
                    AddPath(fonts, zone.fontAssetPath);
                    foreach(var layer in zone.layers ?? new List<ZoneLayer>()) AddPath(images, layer?.assetPath);
                    foreach(var variant in zone.variants ?? new List<ZoneVariant>()) AddPath(images,variant?.staticAssetPath);
                }
            }
            foreach (string id in block.cardIds)
            {
                var c = Cards[id];
                foreach (var zoneOverride in c.zoneOverrides) AddPath(images, zoneOverride.assetPathOverride);
                foreach (var field in Schema.fields.Where(f => f.kind == FieldKind.Image || f.kind == FieldKind.Icon))
                    AddPath(images, CardForgeData.Value(c, field));
            }
            foreach (var icon in Icons.Values.Where(i => images.Contains(i.assetPath))) jsonPaths.Add(IconPath(icon.id));
            string bundleName = "cards-" + Slug(block.code) + "-" + block.id.Substring(0, Math.Min(8, block.id.Length));
            string manifestPath = BlockRoot(block.id) + "/export-manifest.json";
            var manifest = new ExportManifest
            {
                blockId = block.id, blockName = block.name, bundleName = bundleName,
                unityBuildTarget = target.ToString(), jsonAssetPaths = jsonPaths.ToList(),
                imageAssetPaths = images.OrderBy(s => s).ToList(),
                fontAssetPaths = fonts.OrderBy(s => s).ToList()
            };
            Write(manifestPath, manifest);
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            var inputs = jsonPaths.Concat(new [] { manifestPath }).Concat(images).Concat(fonts)
                .Where(ValidAsset).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (inputs.Length < jsonPaths.Count + 1) throw new InvalidOperationException("One or more JSON source files were not imported.");
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string root = Path.Combine(projectRoot, "CardForgeExports", bundleName, target.ToString());
            Directory.CreateDirectory(root);
            var build = new AssetBundleBuild { assetBundleName = bundleName, assetNames = inputs };
            var result = BuildPipeline.BuildAssetBundles(root, new [] { build }, BuildAssetBundleOptions.None, target);
            if (result == null || !File.Exists(Path.Combine(root, bundleName)))
                throw new InvalidOperationException("Unity AssetBundle build returned no bundle. See Editor Console.");
            string jsonDest = Path.Combine(root, "json");
            foreach (var asset in jsonPaths.Concat(new [] { manifestPath }))
            {
                var relative = asset.Substring(DataRoot.Length + 1).Replace('/', Path.DirectorySeparatorChar);
                string destination = Path.Combine(jsonDest, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(DiskPath(asset), destination, true);
            }
            return root;
        }
        static void AddPath(HashSet<string> paths, string path)
        { if (!string.IsNullOrEmpty(path)) paths.Add(path); }
        public static string Slug(string raw)
        {
            var slug = new string((raw ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            return string.IsNullOrEmpty(slug) ? "block" : slug;
        }
    }
}
