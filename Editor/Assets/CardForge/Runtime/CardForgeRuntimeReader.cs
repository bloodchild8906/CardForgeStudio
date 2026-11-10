using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CardForge
{
    /// <summary>
    /// Runtime entry point for loading authored CardForge data and compiling its rule graphs.
    /// </summary>
    public sealed class CardForgeRuntimeReader : IDisposable
    {
        public AssetBundle Bundle { get; private set; }
        public ExportManifest Manifest { get; private set; }
        public SchemaDocument Schema { get; private set; }
        public BlockDocument Block { get; private set; }
        public readonly List<LayoutDocument> Layouts = new List<LayoutDocument>();
        public readonly List<CardDocument> Cards = new List<CardDocument>();
        public readonly List<IconDocument> Icons = new List<IconDocument>();

        public readonly List<TurnFlowDocument> TurnFlows = new List<TurnFlowDocument>();
        public readonly List<TriggerDocument> Triggers = new List<TriggerDocument>();
        public readonly List<ActionDocument> Actions = new List<ActionDocument>();
        public readonly List<AbilityDocument> Abilities = new List<AbilityDocument>();
        public readonly List<KeywordDocument> Keywords = new List<KeywordDocument>();

        public RuleStateMachineDefinition BuildRuleStateMachine(string turnFlowId)
        {
            var flow = TurnFlows.FirstOrDefault(candidate => candidate.id == turnFlowId);
            if (flow == null) throw new InvalidOperationException("Turn flow was not found: " + turnFlowId);
            return CardForgeRules.BuildStateMachine(flow, Triggers, Actions, Abilities, Keywords);
        }

        public CardForgeRuleEngine CreateRuleEngine(string turnFlowId)
        {
            return new CardForgeRuleEngine(BuildRuleStateMachine(turnFlowId));
        }

        public static CardForgeRuntimeReader Load(string bundleFilePath)
        {
            var reader = new CardForgeRuntimeReader();
            reader.Bundle = AssetBundle.LoadFromFile(bundleFilePath);
            if (reader.Bundle == null) throw new InvalidOperationException("Could not open CardForge bundle " + bundleFilePath);
            try
            {
                var texts = reader.Bundle.LoadAllAssets<TextAsset>();
                foreach (var text in texts)
                {
                    string json = text.text;
                    if (json.Contains("\"bundleName\"") && json.Contains("\"jsonAssetPaths\""))
                        reader.Manifest = JsonUtility.FromJson<ExportManifest>(json);
                }
                // Data is identified by its manifest, not by ambiguous asset filenames.
                if (reader.Manifest == null) throw new InvalidOperationException("CardForge export manifest was not found in bundle.");
                foreach (string path in reader.Manifest.jsonAssetPaths)
                {
                    var asset = reader.Bundle.LoadAsset<TextAsset>(path.ToLowerInvariant());
                    if (asset == null) asset = reader.Bundle.LoadAsset<TextAsset>(path);
                    if (asset == null) throw new InvalidOperationException("Missing JSON asset " + path);
                    string json = asset.text;
                    string normal = path.Replace('\\', '/');
                    if (normal.EndsWith("/schema.json", StringComparison.OrdinalIgnoreCase)) reader.Schema = JsonUtility.FromJson<SchemaDocument>(json);
                    else if (normal.EndsWith("/block.json", StringComparison.OrdinalIgnoreCase)) reader.Block = JsonUtility.FromJson<BlockDocument>(json);
                    else if (normal.Contains("/layouts/")) reader.Layouts.Add(JsonUtility.FromJson<LayoutDocument>(json));
                    else if (normal.Contains("/cards/")) reader.Cards.Add(JsonUtility.FromJson<CardDocument>(json));
                    else if (normal.Contains("/turn-flows/")) reader.TurnFlows.Add(JsonUtility.FromJson<TurnFlowDocument>(json));
                    else if (normal.Contains("/triggers/")) reader.Triggers.Add(JsonUtility.FromJson<TriggerDocument>(json));
                    else if (normal.Contains("/actions/")) reader.Actions.Add(JsonUtility.FromJson<ActionDocument>(json));
                    else if (normal.Contains("/abilities/")) reader.Abilities.Add(JsonUtility.FromJson<AbilityDocument>(json));
                    else if (normal.Contains("/keywords/")) reader.Keywords.Add(JsonUtility.FromJson<KeywordDocument>(json));
                    else if (normal.Contains("/icons/")) reader.Icons.Add(JsonUtility.FromJson<IconDocument>(json));
                }
                if (reader.Schema == null || reader.Block == null) throw new InvalidOperationException("CardForge schema or block JSON missing from bundle.");
                return reader;
            }
            catch { reader.Dispose(); throw; }
        }

        public Texture2D GetImage(string assetPath)
        {
            if (Bundle == null || string.IsNullOrEmpty(assetPath)) return null;
            return Bundle.LoadAsset<Texture2D>(assetPath.ToLowerInvariant());
        }

        public void Dispose()
        {
            if (Bundle != null) Bundle.Unload(false);
            Bundle = null;
        }
    }
}
