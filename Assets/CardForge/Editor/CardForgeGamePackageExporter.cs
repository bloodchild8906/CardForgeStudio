using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CardForge.GamePackages;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    public static class CardForgeGamePackageExporter
    {
        sealed class PackageEntry
        {
            public string Path;
            public string Kind;
            public byte[] Content;
            public string Sha256;
        }

        sealed class ScopedContent
        {
            public TurnFlowDocument Flow;
            public List<ActionDocument> Actions = new List<ActionDocument>();
            public List<AbilityDocument> Abilities = new List<AbilityDocument>();
            public List<KeywordDocument> Keywords = new List<KeywordDocument>();
            public List<TriggerDocument> Triggers = new List<TriggerDocument>();
        }

        public static string Export(CardForgeStore store, BlockDocument block, GameDefinitionDocument game, string outputPath)
        {
            var errors = store.Validate(block);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("An output path is required.", nameof(outputPath));

            var scoped = ScopeContent(store, block, game);
            var entries = new List<PackageEntry>();
            AddFile(entries, "schema/card-fields.json", "schema", ReadAsset(CardForgeStore.DataRoot + "/schema.json"));
            AddFile(entries, "block/block.json", "block", ReadAsset(CardForgeStore.DataRoot + "/blocks/" + block.id + "/block.json"));
            foreach (var id in block.cardIds.Where(store.Cards.ContainsKey))
                AddFile(entries, "cards/" + id + ".json", "cards", ReadAsset(CardForgeStore.DataRoot + "/blocks/" + block.id + "/cards/" + id + ".json"));
            foreach (var id in block.layoutIds.Where(store.Layouts.ContainsKey))
                AddFile(entries, "frames/" + id + ".json", "frames", ReadAsset(CardForgeStore.DataRoot + "/blocks/" + block.id + "/layouts/" + id + ".json"));
            AddSourceDocuments(entries, scoped.Actions, "actions", "actions", document => document.id);
            AddSourceDocuments(entries, scoped.Abilities, "abilities", "abilities", document => document.id);
            AddSourceDocuments(entries, scoped.Keywords, "keywords", "keywords", document => document.id);
            AddSourceDocuments(entries, scoped.Triggers, "triggers", "triggers", document => document.id);
            if (scoped.Flow != null) AddSourceDocuments(entries, new[] { scoped.Flow }, "turn-flows", "turn-flow", document => document.id);
            foreach (var deck in game.sampleDecks)
                AddText(entries, "decks/" + deck.id + ".json", "decks", BuildDeckJson(deck));

            AddText(entries, "cards/index.json", "cards", StringArray(block.cardIds));
            AddText(entries, "frames/index.json", "frames", StringArray(block.layoutIds));
            AddText(entries, "actions/index.json", "actions", StringArray(scoped.Actions.Select(document => document.id)));
            AddText(entries, "abilities/index.json", "abilities", StringArray(scoped.Abilities.Select(document => document.id)));
            AddText(entries, "keywords/index.json", "keywords", StringArray(scoped.Keywords.Select(document => document.id)));
            AddText(entries, "turn-flows/index.json", "turn-flow", StringArray(scoped.Flow == null ? new string[0] : new[] { scoped.Flow.id }));
            AddText(entries, "decks/index.json", "decks", StringArray(game.sampleDecks.Select(deck => deck.id)));
            AddText(entries, "zones/game-zones.json", "zones", BuildZonesJson(game));
            AddAssets(entries, store, block);
            AddText(entries, GamePackageContract.DefaultRulePackPath, "rules", BuildRulePackJson(store, block, game));
            AddText(entries, GamePackageContract.DefaultVictoryPath, "victory", BuildVictoryJson(game));

            var summary = new GamePackageContentSummary
            {
                Cards = block.cardIds.Count,
                Zones = game.zones.Count,
                Frames = block.layoutIds.Count,
                Assets = entries.Count(entry => entry.Kind == "assets") - 1,
                Actions = scoped.Actions.Count,
                Abilities = scoped.Abilities.Count,
                Keywords = scoped.Keywords.Count,
                TurnFlows = scoped.Flow == null ? 0 : 1,
                VictoryRules = game.victoryRules.Count,
                Decks = game.sampleDecks.Count
            };
            var manifest = new GamePackageManifest
            {
                PackageId = game.packageId,
                GameId = game.gameId,
                Name = block.name,
                Version = game.packageVersion,
                Description = block.description ?? "",
                CreatedUtc = ExistingCreatedUtc(outputPath, game.packageId, game.packageVersion) ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                SourceEditor = "CardForgeStudio Unity " + Application.unityVersion,
                Content = summary,
                Files = entries.Select(entry => new GamePackageFile { Path = entry.Path, Kind = entry.Kind, Length = entry.Content.LongLength, Sha256 = entry.Sha256 }).ToList()
            };
            var validation = GamePackageManifestValidator.Validate(manifest, entries.Select(entry => entry.Path).Concat(new[] { GamePackageContract.ManifestEntryName }));
            if (!validation.IsValid) throw new InvalidOperationException(string.Join("\n", validation.Errors.Select(issue => issue.Message + (string.IsNullOrEmpty(issue.Path) ? "" : " [" + issue.Path + "]"))));

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = outputPath + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
            using (var stream = File.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteArchiveEntry(archive, GamePackageContract.ManifestEntryName, Encoding.UTF8.GetBytes(BuildManifestJson(manifest)));
                foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
                    WriteArchiveEntry(archive, entry.Path, entry.Content);
            }
            File.Copy(temporary, outputPath, true);
            File.Delete(temporary);
            return outputPath;
        }

        static void AddSourceDocuments<T>(List<PackageEntry> entries, IEnumerable<T> documents, string folder, string kind, Func<T, string> id)
        {
            foreach (var document in documents)
            {
                var documentId = id(document);
                AddFile(entries, folder + "/" + documentId + ".json", kind, ReadAsset(CardForgeStore.DataRoot + "/" + folder + "/" + documentId + ".json"));
            }
        }

        static ScopedContent ScopeContent(CardForgeStore store, BlockDocument block, GameDefinitionDocument game)
        {
            var result = new ScopedContent();
            result.Flow = store.TurnFlows.TryGetValue(game.turnFlowId ?? "", out var selectedFlow) ? selectedFlow : store.TurnFlows.Values.FirstOrDefault();
            result.Actions = ScopeActions(store, block, result.Flow);

            var keywordIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var abilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cardId in block.cardIds.Where(store.Cards.ContainsKey))
            {
                var card = store.Cards[cardId];
                foreach (var keywordId in card.keywordIds ?? new List<string>()) keywordIds.Add(keywordId);
                foreach (var abilityId in card.abilityIds ?? new List<string>()) abilityIds.Add(abilityId);
            }
            foreach (var keywordId in keywordIds)
                if (store.Keywords.TryGetValue(keywordId, out var keyword))
                    foreach (var abilityId in keyword.abilityIds ?? new List<string>()) abilityIds.Add(abilityId);

            result.Keywords = store.Keywords.Values.Where(keyword => keywordIds.Contains(keyword.id)).ToList();
            result.Abilities = store.Abilities.Values.Where(ability => abilityIds.Contains(ability.id)).ToList();

            var triggerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in result.Actions)
            {
                if (!string.IsNullOrWhiteSpace(action.triggerId)) triggerIds.Add(action.triggerId);
                foreach (var triggerId in action.causeTriggerIds ?? new List<string>()) triggerIds.Add(triggerId);
            }
            foreach (var ability in result.Abilities)
            {
                if (!string.IsNullOrWhiteSpace(ability.activationTriggerId)) triggerIds.Add(ability.activationTriggerId);
                foreach (var triggerId in ability.causeTriggerIds ?? new List<string>()) triggerIds.Add(triggerId);
            }
            foreach (var phase in result.Flow?.nodes ?? new List<TurnFlowNode>())
                foreach (var rule in phase.actionRules ?? new List<PhaseActionRule>())
                {
                    foreach (var triggerId in rule.beforeTriggerIds ?? new List<string>()) triggerIds.Add(triggerId);
                    foreach (var triggerId in rule.afterTriggerIds ?? new List<string>()) triggerIds.Add(triggerId);
                }
            result.Triggers = store.Triggers.Values.Where(trigger => triggerIds.Contains(trigger.id)).ToList();
            return result;
        }

        static void AddAssets(List<PackageEntry> entries, CardForgeStore store, BlockDocument block)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var layoutId in block.layoutIds.Where(store.Layouts.ContainsKey))
            {
                var layout = store.Layouts[layoutId];
                Collect(paths, layout.backgroundAssetPath);
                foreach (var template in layout.templates ?? new List<ZoneTemplateDefinition>()) Collect(paths, template == null ? "" : template.backgroundAssetPath);
                foreach (var zone in layout.zones ?? new List<ZoneDefinition>())
                {
                    if (zone == null) continue;
                    Collect(paths, zone.staticAssetPath); Collect(paths, zone.fontAssetPath);
                    foreach (var layer in zone.layers ?? new List<ZoneLayer>()) Collect(paths, layer == null ? "" : layer.assetPath);
                    foreach (var variant in zone.variants ?? new List<ZoneVariant>()) Collect(paths, variant == null ? "" : variant.staticAssetPath);
                }
            }
            foreach (var cardId in block.cardIds.Where(store.Cards.ContainsKey))
            {
                var card = store.Cards[cardId];
                foreach (var value in card.values ?? new List<CardFieldValue>())
                {
                    var field = store.Schema.fields.FirstOrDefault(candidate => candidate.id == value.fieldId);
                    if (field != null && (field.kind == FieldKind.Image || field.kind == FieldKind.Icon)) Collect(paths, value.value);
                }
                foreach (var zoneOverride in card.zoneOverrides ?? new List<ZoneOverride>()) Collect(paths, zoneOverride.assetPathOverride);
            }

            var map = new List<string>();
            foreach (var sourcePath in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!CardForgeStore.ValidAsset(sourcePath)) continue;
                var bytes = File.ReadAllBytes(Path.GetFullPath(sourcePath));
                var hash = Hash(bytes);
                var packagePath = "assets/" + hash.Substring(0, 12).ToLowerInvariant() + "-" + SafeFileName(Path.GetFileName(sourcePath));
                AddFile(entries, packagePath, "assets", bytes);
                map.Add("{\"source\":" + Quote(sourcePath) + ",\"path\":" + Quote(packagePath) + ",\"sha256\":" + Quote(hash) + "}");
            }
            AddText(entries, "assets/asset-map.json", "assets", "{\"version\":1,\"assets\":[" + string.Join(",", map) + "]}");
        }

        /// <summary>
        /// The actions this game can actually use: those its turn flow allows in some phase, plus those its cards' abilities react to.
        /// Exporting every action in the project made a second game inherit the first game's actions, which reference zones and
        /// resources the second game does not define, so the server rejected the package.
        /// </summary>
        static List<ActionDocument> ScopeActions(CardForgeStore store, BlockDocument block, TurnFlowDocument flow)
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            if (flow != null)
                foreach (var node in flow.nodes ?? new List<TurnFlowNode>())
                    foreach (var rule in node.actionRules ?? new List<PhaseActionRule>())
                        if (!string.IsNullOrWhiteSpace(rule.actionId)) wanted.Add(rule.actionId);
            // No flow rules authored: keep the original behavior (every action is allowed in every phase).
            if (wanted.Count == 0) return store.Actions.Values.ToList();

            foreach (var cardId in block.cardIds.Where(store.Cards.ContainsKey))
            {
                var card = store.Cards[cardId];
                var abilityIds = new HashSet<string>(card.abilityIds ?? new List<string>());
                foreach (var keywordId in card.keywordIds ?? new List<string>())
                    if (store.Keywords.TryGetValue(keywordId, out var keyword))
                        foreach (var id in keyword.abilityIds ?? new List<string>()) abilityIds.Add(id);
                foreach (var abilityId in abilityIds)
                    if (store.Abilities.TryGetValue(abilityId, out var ability) && !string.IsNullOrWhiteSpace(ability.actionId)) wanted.Add(ability.actionId);
            }
            return store.Actions.Values.Where(action => wanted.Contains(action.id)).ToList();
        }

        static string BuildRulePackJson(CardForgeStore store, BlockDocument block, GameDefinitionDocument game)
        {
            var flow = store.TurnFlows.TryGetValue(game.turnFlowId ?? "", out var selectedFlow) ? selectedFlow : store.TurnFlows.Values.FirstOrDefault();
            var actions = ScopeActions(store, block, flow);
            var actionIds = actions.Select(action => action.id).ToList();
            if (actionIds.Count == 0) actionIds.Add("end_turn");
            var phases = flow == null || flow.nodes.Count == 0 ? new List<TurnFlowNode> { new TurnFlowNode { id = "main", name = "Main", phase = "Main" } } : flow.nodes;
            var cards = block.cardIds.Where(store.Cards.ContainsKey).Select(id => BuildCardJson(store, store.Cards[id])).ToList();
            var cardJson = cards.Count == 0 ? "[]" : "[" + string.Join(",", cards) + "]";
            var zones = "[" + string.Join(",", game.zones.Select(zone => "{\"id\":" + Quote(zone.id) + ",\"name\":" + Quote(zone.name) + ",\"ownerScope\":" + Quote(zone.ownerScope) + ",\"visibility\":" + Quote(zone.visibility) + ",\"ordered\":" + Bool(zone.ordered) + (zone.capacity > 0 ? ",\"capacity\":" + zone.capacity : "") + "}")) + "]";
            var resources = "[" + string.Join(",", game.resources.Select(resource => "{\"id\":" + Quote(resource.id) + ",\"name\":" + Quote(resource.name) + ",\"scope\":" + Quote(resource.scope) + ",\"initialValue\":" + resource.initialValue + ",\"minimumValue\":" + resource.minimumValue + (resource.maximumValue > 0 ? ",\"maximumValue\":" + resource.maximumValue : "") + "}")) + "]";
            var phaseJson = "[" + string.Join(",", phases.Select(phase =>
            {
                var allowed = (phase.actionRules ?? new List<PhaseActionRule>()).Select(rule => rule.actionId).Where(actionIds.Contains).Distinct().ToList();
                if (allowed.Count == 0) allowed.AddRange(actionIds);
                return "{\"id\":" + Quote(PhaseId(phase)) + ",\"name\":" + Quote(string.IsNullOrWhiteSpace(phase.name) ? phase.phase : phase.name) + ",\"allowedActions\":" + StringArray(allowed) + ",\"automaticAdvance\":" + Bool(phase.optional) + "}";
            })) + "]";
            var actionJson = actions.Count == 0
                ? "[{\"id\":\"end_turn\",\"name\":\"End turn\",\"requiresActivePlayer\":true,\"effects\":[{\"kind\":\"end_turn\"}]}]"
                : "[" + string.Join(",", actions.Select(BuildActionJson)) + "]";
            var defaultDeck = game.sampleDecks.FirstOrDefault(deck => deck.id == game.defaultDeckId) ?? game.sampleDecks.FirstOrDefault();
            var startingDeck = defaultDeck == null || defaultDeck.cardIds.Count == 0 ? block.cardIds : defaultDeck.cardIds;
            return "{" +
                "\"id\":" + Quote(game.gameId) + "," +
                "\"name\":" + Quote(block.name) + "," +
                "\"version\":" + Quote(game.packageVersion) + "," +
                "\"description\":" + Quote(block.description ?? "") + "," +
                "\"cards\":" + cardJson + "," +
                "\"startingRules\":{\"startingHand\":" + game.startingHand + ",\"startingLife\":" + StartingLife(game) + "}," +
                "\"startingDeck\":" + StringArray(startingDeck) + "," +
                "\"schemaVersion\":\"2\"," +
                "\"gameSystem\":" + Quote(game.gameSystem) + "," +
                "\"minimumPlayers\":" + game.minimumPlayers + ",\"maximumPlayers\":" + game.maximumPlayers + "," +
                "\"zones\":" + zones + ",\"resources\":" + resources + ",\"phases\":" + phaseJson + ",\"actions\":" + actionJson + "," +
                "\"metadata\":{\"cardForgeBlockId\":" + Quote(block.id) + ",\"turnFlowId\":" + Quote(game.turnFlowId ?? "") + "}}";
        }

        static string BuildCardJson(CardForgeStore store, CardDocument card)
        {
            var abilities = new HashSet<string>(card.abilityIds ?? new List<string>());
            foreach (var keywordId in card.keywordIds ?? new List<string>())
                if (store.Keywords.TryGetValue(keywordId, out var keyword)) foreach (var abilityId in keyword.abilityIds ?? new List<string>()) abilities.Add(abilityId);
            var abilityJson = abilities.Where(store.Abilities.ContainsKey).Select(id => BuildAbilityJson(store.Abilities[id])).ToList();
            var tags = (card.keywordIds ?? new List<string>()).Select(id => store.Keywords.TryGetValue(id, out var keyword) ? keyword.name : id);
            var properties = new List<string> { "\"layoutId\":" + Quote(card.layoutId), "\"backLayoutId\":" + Quote(card.backLayoutId) };
            foreach (var value in card.values ?? new List<CardFieldValue>())
            {
                var field = store.Schema.fields.FirstOrDefault(candidate => candidate.id == value.fieldId);
                if (field != null && !string.IsNullOrWhiteSpace(field.key)) properties.Add(Quote(field.key) + ":" + Quote(value.value));
            }
            return "{\"id\":" + Quote(card.id) + ",\"name\":" + Quote(card.name) + ",\"type\":" + Quote(Value(store, card, "type", card.cardType)) +
                ",\"cost\":" + Number(Value(store, card, "cost", "0")) + ",\"power\":" + Number(Value(store, card, "power", "0")) +
                ",\"toughness\":" + Number(Value(store, card, "toughness", "0")) + ",\"text\":" + Quote(Value(store, card, "rules", "")) +
                ",\"tags\":" + StringArray(tags) + ",\"properties\":{" + string.Join(",", properties) + "},\"abilities\":[" + string.Join(",", abilityJson) + "]}";
        }

        static string BuildAbilityJson(AbilityDocument ability)
        {
            var trigger = !string.IsNullOrWhiteSpace(ability.actionId) ? "action:" + ability.actionId : !string.IsNullOrWhiteSpace(ability.activationTriggerId) ? ability.activationTriggerId : "action_executed";
            return "{\"id\":" + Quote(ability.id) + ",\"name\":" + Quote(ability.name) + ",\"trigger\":" + Quote(trigger) +
                ",\"conditions\":[],\"effects\":[{\"kind\":\"emit_event\",\"eventType\":\"cardforge_ability\",\"message\":" + Quote(string.IsNullOrWhiteSpace(ability.effect) ? ability.name : ability.effect) + "}]," +
                "\"metadata\":{\"priority\":" + ability.priority + ",\"scope\":" + Quote(ability.scope) + "}}";
        }

        static string BuildActionJson(ActionDocument action)
        {
            var kind = EffectKind(action);
            var effect = "{\"kind\":" + Quote(kind);
            if (!string.IsNullOrWhiteSpace(action.sourceZoneId)) effect += ",\"fromZone\":" + Quote(action.sourceZoneId);
            if (!string.IsNullOrWhiteSpace(action.destinationZoneId)) effect += ",\"toZone\":" + Quote(action.destinationZoneId);
            if (!string.IsNullOrWhiteSpace(action.resourceId)) effect += ",\"resourceId\":" + Quote(action.resourceId);
            if (!string.IsNullOrWhiteSpace(action.effectTarget)) effect += ",\"target\":" + Quote(action.effectTarget);
            if (action.effectAmount != 0) effect += ",\"amount\":" + action.effectAmount;
            if (kind == "emit_event") effect += ",\"eventType\":" + Quote(string.IsNullOrWhiteSpace(action.eventType) ? "cardforge_action" : action.eventType) + ",\"message\":" + Quote(string.IsNullOrWhiteSpace(action.effect) ? action.name : action.effect);
            if (kind == "set_data" && !string.IsNullOrWhiteSpace(action.dataKey)) effect += ",\"parameters\":{" + Quote(action.dataKey) + ":" + Quote(action.dataValue) + "}";
            effect += "}";
            var effects = kind == "move_card" && !string.IsNullOrWhiteSpace(action.sourceZoneId) && !string.IsNullOrWhiteSpace(action.destinationZoneId)
                ? "[]"
                : "[" + effect + "]";
            var costs = string.IsNullOrWhiteSpace(action.costResourceId) || action.costAmount <= 0
                ? "{}"
                : "{" + Quote(action.costResourceId) + ":" + action.costAmount + "}";
            var conditions = string.IsNullOrWhiteSpace(action.conditionKind)
                ? "[]"
                : "[{\"kind\":" + Quote(action.conditionKind) +
                  (string.IsNullOrWhiteSpace(action.conditionKey) ? "" : ",\"key\":" + Quote(action.conditionKey)) +
                  (string.IsNullOrWhiteSpace(action.conditionValue) ? "" : ",\"value\":" + JsonScalar(action.conditionValue)) + "}]";
            return "{\"id\":" + Quote(action.id) + ",\"name\":" + Quote(action.name) +
                (!string.IsNullOrWhiteSpace(action.sourceZoneId) ? ",\"sourceZone\":" + Quote(action.sourceZoneId) : "") +
                (!string.IsNullOrWhiteSpace(action.destinationZoneId) ? ",\"destinationZone\":" + Quote(action.destinationZoneId) : "") +
                ",\"requiresActivePlayer\":" + Bool(action.requiresActivePlayer) + ",\"costs\":" + costs + ",\"conditions\":" + conditions + ",\"effects\":" + effects + "," +
                "\"metadata\":{\"resolution\":" + Quote(action.resolution) + ",\"targets\":" + Quote(action.targets) + "}}";
        }

        static string EffectKind(ActionDocument action)
        {
            var supported = new[] { "move_card", "draw", "modify_resource", "set_data", "emit_event", "advance_phase", "end_turn", "finish_game" };
            if (supported.Contains(action.serverEffectKind)) return action.serverEffectKind;
            var text = ((action.name ?? "") + " " + (action.effect ?? "")).ToLowerInvariant();
            if (text.Contains("draw")) return "draw";
            if (text.Contains("end turn")) return "end_turn";
            if (text.Contains("advance") && text.Contains("phase")) return "advance_phase";
            if (text.Contains("win") || text.Contains("finish game")) return "finish_game";
            return "emit_event";
        }

        static string BuildZonesJson(GameDefinitionDocument game) => "{\"version\":1,\"zones\":[" + string.Join(",", game.zones.Select(zone => "{\"id\":" + Quote(zone.id) + ",\"name\":" + Quote(zone.name) + ",\"ownerScope\":" + Quote(zone.ownerScope) + ",\"visibility\":" + Quote(zone.visibility) + ",\"ordered\":" + Bool(zone.ordered) + ",\"capacity\":" + zone.capacity + "}")) + "]}";
        static string BuildDeckJson(GameDeckDocument deck) => "{\"version\":1,\"id\":" + Quote(deck.id) + ",\"name\":" + Quote(deck.name) + ",\"description\":" + Quote(deck.description) + ",\"minimumCards\":" + deck.minimumCards + ",\"maximumCopiesPerCard\":" + deck.maximumCopiesPerCard + ",\"cardIds\":" + StringArray(deck.cardIds) + "}";
        static string BuildVictoryJson(GameDefinitionDocument game) => "{\"version\":1,\"rules\":[" + string.Join(",", game.victoryRules.Select(rule => "{\"id\":" + Quote(rule.id) + ",\"name\":" + Quote(rule.name) + ",\"kind\":" + Quote(rule.kind) + ",\"outcome\":" + Quote(rule.outcome) + ",\"scope\":" + Quote(rule.scope) + ",\"resourceId\":" + Quote(rule.resourceId) + ",\"zoneId\":" + Quote(rule.zoneId) + ",\"operator\":" + Quote(rule.comparison) + ",\"threshold\":" + rule.threshold + ",\"description\":" + Quote(rule.description) + "}")) + "]}";

        static string BuildManifestJson(GamePackageManifest manifest)
        {
            var files = string.Join(",", manifest.Files.Select(file => "{\"path\":" + Quote(file.Path) + ",\"kind\":" + Quote(file.Kind) + ",\"length\":" + file.Length + ",\"sha256\":" + Quote(file.Sha256) + "}"));
            var content = manifest.Content;
            return "{\"schemaVersion\":" + Quote(manifest.SchemaVersion) + ",\"packageId\":" + Quote(manifest.PackageId) + ",\"gameId\":" + Quote(manifest.GameId) +
                ",\"name\":" + Quote(manifest.Name) + ",\"version\":" + Quote(manifest.Version) + ",\"description\":" + Quote(manifest.Description) +
                ",\"createdUtc\":" + Quote(manifest.CreatedUtc) + ",\"sourceEditor\":" + Quote(manifest.SourceEditor) +
                ",\"rulePackPath\":" + Quote(manifest.RulePackPath) + ",\"victoryDefinitionsPath\":" + Quote(manifest.VictoryDefinitionsPath) +
                ",\"content\":{\"cards\":" + content.Cards + ",\"zones\":" + content.Zones + ",\"frames\":" + content.Frames + ",\"assets\":" + content.Assets +
                ",\"actions\":" + content.Actions + ",\"abilities\":" + content.Abilities + ",\"keywords\":" + content.Keywords + ",\"turnFlows\":" + content.TurnFlows + ",\"victoryRules\":" + content.VictoryRules + ",\"decks\":" + content.Decks + "},\"files\":[" + files + "]}";
        }

        static void WriteArchiveEntry(ZipArchive archive, string path, byte[] content)
        {
            var entry = archive.CreateEntry(path, System.IO.Compression.CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (var output = entry.Open()) output.Write(content, 0, content.Length);
        }

        static string ExistingCreatedUtc(string outputPath, string packageId, string version)
        {
            if (!File.Exists(outputPath)) return null;
            try
            {
                using (var stream = File.OpenRead(outputPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry(GamePackageContract.ManifestEntryName);
                    if (entry == null) return null;
                    string json;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) json = reader.ReadToEnd();
                    if (!Regex.IsMatch(json, "\\\"packageId\\\"\\s*:\\s*\\\"" + Regex.Escape(packageId) + "\\\"", RegexOptions.IgnoreCase) ||
                        !Regex.IsMatch(json, "\\\"version\\\"\\s*:\\s*\\\"" + Regex.Escape(version) + "\\\"", RegexOptions.IgnoreCase)) return null;
                    var match = Regex.Match(json, "\\\"createdUtc\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                    return match.Success ? match.Groups[1].Value : null;
                }
            }
            catch (InvalidDataException) { return null; }
            catch (IOException) { return null; }
        }

        static byte[] ReadAsset(string assetPath)
        {
            var path = Path.GetFullPath(assetPath);
            if (!File.Exists(path)) throw new FileNotFoundException("Required CardForge source is missing.", assetPath);
            return File.ReadAllBytes(path);
        }

        static void AddText(List<PackageEntry> entries, string path, string kind, string content) => AddFile(entries, path, kind, Encoding.UTF8.GetBytes(content));
        static void AddFile(List<PackageEntry> entries, string path, string kind, byte[] content)
        {
            if (entries.Any(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            entries.Add(new PackageEntry { Path = path.Replace('\\', '/'), Kind = kind, Content = content, Sha256 = Hash(content) });
        }
        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        static void Collect(HashSet<string> paths, string path) { if (!string.IsNullOrWhiteSpace(path)) paths.Add(path); }
        static string SafeFileName(string value) => new string((value ?? "asset").Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character).ToArray());
        static string Value(CardForgeStore store, CardDocument card, string key, string fallback) { var field = store.Schema.fields.FirstOrDefault(candidate => string.Equals(candidate.key, key, StringComparison.OrdinalIgnoreCase)); return field == null ? fallback : CardForgeData.Value(card, field); }
        static int StartingLife(GameDefinitionDocument game) { var life = game.resources.FirstOrDefault(resource => string.Equals(resource.id, "life", StringComparison.OrdinalIgnoreCase)); return life == null ? 0 : life.initialValue; }
        static string PhaseId(TurnFlowNode phase) { var raw = string.IsNullOrWhiteSpace(phase.id) ? phase.phase : phase.id; return new string(raw.Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_').Take(80).ToArray()); }
        static string Number(string value) { return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed.ToString(CultureInfo.InvariantCulture) : "0"; }
        static string JsonScalar(string value)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number.ToString(CultureInfo.InvariantCulture);
            if (bool.TryParse(value, out var boolean)) return Bool(boolean);
            return Quote(value);
        }
        static string Bool(bool value) => value ? "true" : "false";
        static string StringArray(IEnumerable<string> values) => "[" + string.Join(",", (values ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(Quote)) + "]";
        static string Quote(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (var character in value ?? "")
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: if (character < 32) builder.Append("\\u" + ((int)character).ToString("x4")); else builder.Append(character); break;
                }
            }
            return builder.Append('"').ToString();
        }
    }

    public sealed class CardForgeGamePackageWindow : EditorWindow
    {
        CardForgeStore store;
        List<BlockDocument> blocks;
        DropdownField blockField, flowField;
        TextField packageIdField, gameIdField, versionField, systemField;
        IntegerField minimumPlayersField, maximumPlayersField, startingHandField, victoryThresholdField;
        Label status;

        public static void Open()
        {
            var window = GetWindow<CardForgeGamePackageWindow>("Game Package Publisher");
            window.minSize = new Vector2(620, 580);
        }

        public void CreateGUI()
        {
            store = new CardForgeStore(); store.Reload(); blocks = store.Blocks.Values.OrderBy(block => block.name).ToList();
            var root = rootVisualElement; root.style.paddingLeft = 18; root.style.paddingRight = 18; root.style.paddingTop = 16; root.style.paddingBottom = 16;
            root.Add(new Label("CARD FORGE · AUTHORITATIVE RELEASE") { style = { fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold } });
            root.Add(new Label("Game Package Publisher") { style = { fontSize = 24, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 6 } });
            root.Add(new Label("Compile editor sources and referenced assets into a validated, immutable .cardforge release for the server.") { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 14 } });
            if (blocks.Count == 0) { root.Add(new HelpBox("Create a CardForge block before publishing.", HelpBoxMessageType.Warning)); return; }
            blockField = new DropdownField("Block", blocks.Select(block => block.name + " · " + block.code).ToList(), 0); root.Add(blockField);
            packageIdField = AddText(root, "Package ID"); gameIdField = AddText(root, "Authoritative game ID"); versionField = AddText(root, "Semantic version"); systemField = AddText(root, "Game system");
            minimumPlayersField = new IntegerField("Minimum players"); maximumPlayersField = new IntegerField("Maximum players"); startingHandField = new IntegerField("Starting hand"); victoryThresholdField = new IntegerField("Elimination threshold");
            root.Add(minimumPlayersField); root.Add(maximumPlayersField); root.Add(startingHandField);
            flowField = new DropdownField("Turn flow", store.TurnFlows.Values.OrderBy(flow => flow.name).Select(flow => flow.name).ToList(), 0); root.Add(flowField); root.Add(victoryThresholdField);
            root.Add(new HelpBox("Gameplay zones default to deck, hand, battlefield, and discard. Life is a player resource; victory eliminates players at or below the configured threshold. Structured action execution fields remain on each action document.", HelpBoxMessageType.Info));
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 14 } };
            var save = new Button(Save) { text = "Save package settings" }; var publish = new Button(Publish) { text = "Publish .cardforge" };
            save.style.marginRight = 8; buttons.Add(save); buttons.Add(publish); root.Add(buttons);
            status = new Label { style = { marginTop = 10, whiteSpace = WhiteSpace.Normal } }; root.Add(status);
            blockField.RegisterValueChangedCallback(_ => LoadSelected()); LoadSelected();
        }

        static TextField AddText(VisualElement root, string label) { var field = new TextField(label); root.Add(field); return field; }
        BlockDocument SelectedBlock => blocks[Math.Max(0, blockField.index)];
        void LoadSelected()
        {
            var game = store.GetGameDefinition(SelectedBlock); packageIdField.value = game.packageId; gameIdField.value = game.gameId; versionField.value = game.packageVersion; systemField.value = game.gameSystem;
            minimumPlayersField.value = game.minimumPlayers; maximumPlayersField.value = game.maximumPlayers; startingHandField.value = game.startingHand;
            var flows = store.TurnFlows.Values.OrderBy(flow => flow.name).ToList(); var selected = flows.FindIndex(flow => flow.id == game.turnFlowId); flowField.index = Math.Max(0, selected);
            victoryThresholdField.value = game.victoryRules.FirstOrDefault() == null ? 0 : game.victoryRules[0].threshold; status.text = "";
        }
        GameDefinitionDocument ReadFields()
        {
            var game = store.GetGameDefinition(SelectedBlock); game.packageId = packageIdField.value.Trim(); game.gameId = gameIdField.value.Trim(); game.packageVersion = versionField.value.Trim(); game.gameSystem = systemField.value.Trim();
            game.minimumPlayers = minimumPlayersField.value; game.maximumPlayers = maximumPlayersField.value; game.startingHand = Math.Max(0, startingHandField.value);
            var flows = store.TurnFlows.Values.OrderBy(flow => flow.name).ToList(); game.turnFlowId = flows.Count == 0 ? "" : flows[Math.Max(0, flowField.index)].id;
            if (game.victoryRules.Count == 0) game.victoryRules.Add(new VictoryRuleDocument()); game.victoryRules[0].threshold = victoryThresholdField.value; return game;
        }
        void Save()
        {
            try { store.SaveGameDefinition(ReadFields()); AssetDatabase.SaveAssets(); status.text = "Package settings saved to CardForgeData/game-definitions."; }
            catch (Exception exception) { status.text = exception.Message; }
        }
        void Publish()
        {
            try
            {
                var game = ReadFields(); store.SaveGameDefinition(game); var defaultName = game.packageId + "-" + game.packageVersion + ".cardforge";
                var path = EditorUtility.SaveFilePanel("Publish CardForge game package", Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GamePackages")), defaultName, "cardforge");
                if (string.IsNullOrWhiteSpace(path)) return; CardForgeGamePackageExporter.Export(store, SelectedBlock, game, path); status.text = "Published " + path; EditorUtility.RevealInFinder(path);
            }
            catch (Exception exception) { status.text = exception.Message; Debug.LogException(exception); }
        }
    }
}
