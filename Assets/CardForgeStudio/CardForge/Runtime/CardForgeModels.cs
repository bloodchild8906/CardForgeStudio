using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CardForge
{
    public enum FieldKind { Text, Number, Icon, Image, Boolean }
    public enum ZoneKind { Text, Number, Icon, Image, StaticText }
    public enum ZoneImageScaleMode { Stretch, Cover, Contain, Tile, Slice }
    public enum CardOrientationChange { None, Rotate90, Rotate180, Rotate270, FlipFrontBack }

    [Serializable]
    public class RequirementDefinition
    {
        public string id = Guid.NewGuid().ToString("N");
        public string fieldId = "";
        public string comparison = "Equals";
        public string expectedValue = "";
        public string message = "";
    }

    [Serializable]
    public class ZoneLayer
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New layer";
        public int order = 0;
        public bool visible = true;
        public string blendMode = "Normal";
        public float opacity = 1f;
        public string backgroundColor = "#00000000";
        public string assetPath = "";
        public string templateId = "";
        public string bindingPath = "";
        public string requiredCardType = "";
        public List<RequirementDefinition> requirements = new List<RequirementDefinition>();
        public ZoneImageScaleMode imageScaleMode = ZoneImageScaleMode.Cover;
        public int sliceLeft = 12;
        public int sliceRight = 12;
        public int sliceTop = 12;
        public int sliceBottom = 12;
    }

    [Serializable]
    public class ZoneTemplateDefinition
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New zone template";
        public string backgroundAssetPath = "";
        public string backgroundColor = "#00000000";
        public ZoneImageScaleMode imageScaleMode = ZoneImageScaleMode.Slice;
        public int sliceLeft = 12;
        public int sliceRight = 12;
        public int sliceTop = 12;
        public int sliceBottom = 12;
    }

    [Serializable]
    public class ZoneVariant
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Default variant";
        public string layerId = "";
        public string staticText = "";
        public string staticAssetPath = "";
        public float x = 20f;
        public float y = 20f;
        public float width = 160f;
        public float height = 40f;
        public float fontSize = 21f;
        public string color = "#F2F0EB";
        public bool visible = true;
        public List<RequirementDefinition> requirements = new List<RequirementDefinition>();
    }

    [Serializable]
    public class PhaseActionRule
    {
        public string id = Guid.NewGuid().ToString("N");
        public string actionId = "";
        public string availability = "Available during phase";
        public string condition = "";
        public int priority = 0;
        public string resolutionOrder = "Priority order";
        public bool optional = false;
        public List<string> beforeTriggerIds = new List<string>();
        public List<string> afterTriggerIds = new List<string>();
    }

    [Serializable]
    public class TurnFlowNode
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New phase";
        public string phase = "Main";
        public string check = "";
        public string resolution = "Resolve actions";
        public float x = 80f;
        public float y = 80f;
        public bool optional = false;
        public List<PhaseActionRule> actionRules = new List<PhaseActionRule>();
    }

    [Serializable]
    public class TurnFlowLink
    {
        public string from = "";
        public string to = "";
        public string label = "continue";
    }

    [Serializable]
    public class TurnFlowDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Standard turn";
        public string description = "The default turn sequence and its checks.";
        public List<TurnFlowNode> nodes = new List<TurnFlowNode>();
        public List<TurnFlowLink> links = new List<TurnFlowLink>();
    }

    [Serializable]
    public class TriggerDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New trigger";
        public string eventType = "State changed";
        public string source = "Any object";
        public string fromZone = "";
        public string toZone = "";
        public string orientation = "";
        public List<TriggerRule> rules = new List<TriggerRule>();
        public string condition = "";
        public string responseWindow = "Immediate response window";
        public int priority = 0;
        public List<string> causeActionIds = new List<string>();
        public List<string> causeAbilityIds = new List<string>();
        public List<string> responseActionIds = new List<string>();
    }

    [Serializable]
    public class TriggerRule
    {
        public string id = Guid.NewGuid().ToString("N");
        public string subject = "Card";
        public string property = "Health";
        public string comparison = "Less than or equal";
        public string expectedValue = "0";
        public string condition = "";
    }

    [Serializable]
    public class ResolutionGraphNode
    {
        public string id = Guid.NewGuid().ToString("N");
        public string title = "New step";
        public string detail = "";
        public string tone = "process";
        public float x = 80f;
        public float y = 80f;
        public bool causesStateChange = false;
        public bool emitsTriggers = false;
    }

    [Serializable]
    public class ResolutionGraphLink
    {
        public string from = "";
        public string to = "";
        public string label = "next";
    }

    [Serializable]
    public class ActionDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New action";
        public string triggerId = "";
        public string resolution = "Resolve immediately";
        public string effect = "";
        public string condition = "";
        public string cost = "";
        public string targets = "";
        public CardOrientationChange orientationChange = CardOrientationChange.None;
        public string orientationTarget = "Target card";
        public List<string> causeTriggerIds = new List<string>();
        public List<ResolutionGraphNode> graphNodes = new List<ResolutionGraphNode>();
        public List<ResolutionGraphLink> graphLinks = new List<ResolutionGraphLink>();
        public bool requiresActivePlayer = true;
        public string serverEffectKind = "emit_event";
        public string sourceZoneId = "";
        public string destinationZoneId = "";
        public string resourceId = "";
        public int effectAmount = 0;
        public string effectTarget = "";
        public string eventType = "";
        public string costResourceId = "";
        public int costAmount = 0;
        public string conditionKind = "";
        public string conditionKey = "";
        public string conditionValue = "";
        public string dataKey = "";
        public string dataValue = "";
    }

    [Serializable]
    public class AbilityEffectDefinition
    {
        public string kind = "emit_event";
        public string target = "";
        public string resourceId = "";
        public string fromZone = "";
        public string toZone = "";
        public string eventType = "";
        public string message = "";
        public long amount;
        public List<AbilityParameterDefinition> parameters = new List<AbilityParameterDefinition>();
    }

    [Serializable]
    public class AbilityConditionDefinition
    {
        public string kind = "";
        public string subject = "";
        public string key = "";
        public string op = "";
        public string value = "";
    }

    [Serializable]
    public class AbilityParameterDefinition
    {
        public string key = "";
        public string value = "";
    }

    [Serializable]
    public class AbilityDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New ability";
        public string cardId = "";
        public string activationTriggerId = "";
        public string actionId = "";
        public string phaseId = "";
        public string condition = "";
        public string operation = "Modify the current action";
        public string effect = "";
        public int priority = 0;
        public string scope = "Game state";
        public string responseTiming = "Immediately";
        public string responseResolution = "Resolve in priority order";
        public List<string> responseActionIds = new List<string>();
        public List<string> causeTriggerIds = new List<string>();
        public List<AbilityRule> rules = new List<AbilityRule>();
        public List<AbilityEffectDefinition> effects = new List<AbilityEffectDefinition>();
        public List<AbilityConditionDefinition> conditions = new List<AbilityConditionDefinition>();
        public List<ResolutionGraphNode> graphNodes = new List<ResolutionGraphNode>();
        public List<ResolutionGraphLink> graphLinks = new List<ResolutionGraphLink>();
    }

    [Serializable]
    public class KeywordDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New keyword";
        public string rulesText = "";
        public string reminderText = "";
        public List<string> abilityIds = new List<string>();
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public class AbilityRule
    {
        public string id = Guid.NewGuid().ToString("N");
        public string target = "Game state";
        public string property = "";
        public string operation = "Set";
        public string value = "";
        public string condition = "";
    }

    [Serializable]
    public class RuleStateMachineDefinition
    {
        public int version = 1;
        public string initialStateId = "";
        public string responseOrder = "Last in, first out";
        public bool allPlayersMayRespond = true;
        public bool requireAllPlayersToPass = true;
        public List<TriggerDocument> triggers = new List<TriggerDocument>();
        public List<ActionDocument> actions = new List<ActionDocument>();
        public List<AbilityDocument> abilities = new List<AbilityDocument>();
        public List<KeywordDocument> keywords = new List<KeywordDocument>();
        public List<RuleCompiledAction> compiledActions = new List<RuleCompiledAction>();
        public List<RuleStateDefinition> states = new List<RuleStateDefinition>();
    }

    [Serializable]
    public class RuleResponseEntry
    {
        public int sequence;
        public string playerId = "";
        public string triggerId = "";
        public string actionId = "";
    }

    [Serializable]
    public class RuleResponseWindow
    {
        public string id = Guid.NewGuid().ToString("N");
        public string parentWindowId = "";
        public string triggerId = "";
        public string sourcePlayerId = "";
        public bool open = true;
        public List<string> playerIds = new List<string>();
        public List<string> passedPlayerIds = new List<string>();
        public List<RuleResponseEntry> responseStack = new List<RuleResponseEntry>();

        public bool AllPlayersPassed
        {
            get { return playerIds.Count > 0 && playerIds.All(playerId => passedPlayerIds.Contains(playerId)); }
        }
    }

    public enum RuleEngineResultKind
    {
        Rejected,
        WaitingForResponses,
        ResponseAccepted,
        ResponseResolved,
        TriggerResolved
    }

    [Serializable]
    public class RuleEngineResult
    {
        public RuleEngineResultKind kind;
        public string reason = "";
        public string triggerId = "";
        public string actionId = "";
        public string playerId = "";
        public List<string> causedTriggerIds = new List<string>();
        public List<string> graphStepIds = new List<string>();
        public List<string> stateChangeStepIds = new List<string>();
    }

    [Serializable]
    public class RuleCompiledAction
    {
        public string id = "";
        public string name = "";
        public List<string> orderedGraphStepIds = new List<string>();
        public List<string> stateChangeStepIds = new List<string>();
        public List<string> triggerBoundaryStepIds = new List<string>();
        public List<string> causedTriggerIds = new List<string>();
    }

    [Serializable]
    public class RuleStateDefinition
    {
        public string id = "";
        public string name = "";
        public List<PhaseActionRule> actionRules = new List<PhaseActionRule>();
        public List<string> abilityIds = new List<string>();
        public List<RuleStateTransition> transitions = new List<RuleStateTransition>();
    }

    [Serializable]
    public class RuleStateTransition
    {
        public string targetStateId = "";
        public string condition = "";
        public string resolution = "";
    }

    [Serializable]
    public class FieldDefinition
    {
        public string id = Guid.NewGuid().ToString("N");
        public string key = "new_field";
        public string label = "New Field";
        public FieldKind kind = FieldKind.Text;
        public string defaultValue = "";
        public string help = "";
        public int decimalPlaces = 0;
        public string prefix = "";
        public string suffix = "";
    }

    [Serializable]
    public class SchemaDocument
    {
        public int version = 1;
        public List<FieldDefinition> fields = new List<FieldDefinition>();
    }

    [Serializable]
    public class ZoneDefinition
    {
        public bool locked = false;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New zone";
        public ZoneKind kind = ZoneKind.Text;
        public string fieldId = "";
        public string staticText = "";
        public string staticAssetPath = "";
        public float x = 20f;
        public float y = 20f;
        public float width = 160f;
        public float height = 40f;
        public int order = 0;
        public float offsetX = 0f;
        public float offsetY = 0f;
        public float fontSize = 21f;
        public string fontAssetPath = "";
        public string color = "#F2F0EB";
        public string backgroundColor = "#00000000";
        public string templateId = "";
        public bool bold = false;
        public bool visible = true;
        public string alignment = "MiddleCenter";
        public List<ZoneLayer> layers = new List<ZoneLayer>();
        public List<ZoneVariant> variants = new List<ZoneVariant>();
    }

    [Serializable]
    public class LayoutDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Untitled Layout";
        public bool isBack = false;
        public int width = 360;
        public int height = 504;
        public string backgroundAssetPath = "";
        public string backgroundColor = "#131d2b";
        public List<ZoneTemplateDefinition> templates = new List<ZoneTemplateDefinition>();
        public List<ZoneDefinition> zones = new List<ZoneDefinition>();
        public string layoutType = "Standard";
        public List<RequirementDefinition> requirements = new List<RequirementDefinition>();
    }

    [Serializable]
    public class CardFieldValue
    {
        public string fieldId = "";
        public string value = "";
    }

    [Serializable]
    public class ZoneOverride
    {
        public string zoneId = "";
        public float deltaX = 0f;
        public float deltaY = 0f;
        public float deltaWidth = 0f;
        public float deltaHeight = 0f;
        public float fontSize = 0f; // 0 = inherit layout
        public string color = ""; // empty = inherit layout
        public string textOverride = ""; // optional per-card literal replacement
        public string assetPathOverride = ""; // optional per-card image replacement
        public bool hidden = false;
    }

    [Serializable]
    public class CardDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New card";
        public string cardNumber = "001";
        public string layoutId = "";
        public string backLayoutId = "";
        public string sourceBlockId = ""; // reprint provenance
        public string sourceCardId = "";
        public string cardType = "Card";
        public string variantId = "";
        public List<CardFieldValue> values = new List<CardFieldValue>();
        public List<ZoneOverride> zoneOverrides = new List<ZoneOverride>();
        public List<RequirementDefinition> requirements = new List<RequirementDefinition>();
        public List<string> keywordIds = new List<string>();
        public List<string> abilityIds = new List<string>();
    }

    [Serializable]
    public class BlockDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string code = "NEW";
        public string name = "New Block";
        public string description = "";
        public List<string> layoutIds = new List<string>();
        public List<string> cardIds = new List<string>();
        public string defaultBackLayoutId = "";
    }

    [Serializable]
    public class GameZoneDocument
    {
        public string id = "deck";
        public string name = "Deck";
        public string ownerScope = "player";
        public string visibility = "owner";
        public bool ordered = true;
        public int capacity = 0;
        public List<string> allowedCardTypes = new List<string>();
        public List<string> initialCardIds = new List<string>();
        public string deckStartCardType = "";
        public bool returnOnLeave;
        public bool immovable;
    }

    [Serializable]
    public class GameResourceDocument
    {
        public string id = "life";
        public string name = "Life";
        public string scope = "player";
        public int initialValue = 20;
        public int minimumValue = 0;
        public int maximumValue = 0;
    }

    [Serializable]
    public class VictoryRuleDocument
    {
        public string id = "last-player-standing";
        public string name = "Last player standing";
        public string kind = "last-player-standing";
        public string outcome = "win";
        public string scope = "player";
        public string resourceId = "life";
        public string zoneId = "";
        public string comparison = "less-than-or-equal";
        public int threshold = 0;
        public string description = "Eliminate players at zero life; the final remaining player wins.";
    }

    [Serializable]
    public class GameDeckDocument
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Starter Deck";
        public string description = "A ready-to-play deck included with this game package.";
        public int minimumCards = 1;
        public int maximumCopiesPerCard = 4;
        public List<string> cardIds = new List<string>();
        public string strategy = "";
        // Board rules and starting cards belong to GameZoneDocument.
    }
    /// <summary>Normalized board rectangle; positions are fractions of the play surface.</summary>
    [Serializable]
    public class GameBoardZonePlacement
    {
        public string id = Guid.NewGuid().ToString("N");
        public string zoneId = "battlefield";
        public string side = "self";
        public string label = "";
        public float x = 0.2f;
        public float y = 0.6f;
        public float width = 0.6f;
        public float height = 0.2f;
        public int order;
    }

    [Serializable]
    public class DeckConstructionRulesDocument
    {
        public int minCards = 1;
        public int maxCards = 120;
        public int maxCopiesPerCard = 4;
        public List<string> allowedCardTypes = new List<string>();
        public List<DeckTypeLimitDocument> typeLimits = new List<DeckTypeLimitDocument>();
        public List<DeckCardLimitDocument> cardLimits = new List<DeckCardLimitDocument>();
    }

    [Serializable]
    public class DeckTypeLimitDocument
    {
        public string cardType = "";
        public int minCount;
        public int maxCount = -1; // -1 = no type-specific maximum
    }

    [Serializable]
    public class DeckCardLimitDocument
    {
        public string cardId = "";
        public int minCopies;
        public int maxCopies = -1; // -1 = global limit; zero = forbidden
    }

    [Serializable]
    public class GameDefinitionDocument
    {
        public int version = 1;
        public string blockId = "";
        public string packageId = "cardforge-game";
        public string gameId = "cardforge-game";
        public string packageVersion = "1.0.0";
        public string gameSystem = "cardforge-custom";
        public int minimumPlayers = 2;
        public int maximumPlayers = 2;
        public int startingHand = 7;
        public string turnFlowId = "";
        public string defaultDeckId = "";
        public DeckConstructionRulesDocument deckRules = new DeckConstructionRulesDocument();
        public List<GameZoneDocument> zones = new List<GameZoneDocument>();
        public List<GameBoardZonePlacement> boardLayout = new List<GameBoardZonePlacement>();
        public List<GameResourceDocument> resources = new List<GameResourceDocument>();
        public List<VictoryRuleDocument> victoryRules = new List<VictoryRuleDocument>();
        public List<GameDeckDocument> sampleDecks = new List<GameDeckDocument>();
    }

    [Serializable]
    public class IconLayer
    {
        public bool visible = true;
        public float opacity = 1f;
        public string shape = "Circle";
        public string color = "#FFFFFF";
        public float x = .5f;
        public float y = .5f;
        public float size = .7f;
        public float rotation = 0f;
    }

    [Serializable]
    public class IconDocument
    {
        public int version = 2;
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Untitled Icon";
        public string assetPath = "";
        public List<IconLayer> layers = new List<IconLayer>();
    }

    [Serializable]
    public class ExportManifest
    {
        public int version = 1;
        public string blockId = "";
        public string blockName = "";
        public string bundleName = "";
        public string unityBuildTarget = "";
        public List<string> jsonAssetPaths = new List<string>();
        public List<string> imageAssetPaths = new List<string>();
        public List<string> fontAssetPaths = new List<string>();
    }

    public static class CardForgeData
    {
        public static string NewId() { return Guid.NewGuid().ToString("N"); }
        public static T Copy<T>(T original) { return JsonUtility.FromJson<T>(JsonUtility.ToJson(original)); }
        public static string Value(CardDocument card, FieldDefinition definition)
        {
            if (card != null)
            {
                var v = card.values.Find(x => x.fieldId == definition.id);
                if (v != null) return v.value ?? "";
            }
            return definition.defaultValue ?? "";
        }
        public static void SetValue(CardDocument card, string fieldId, string value)
        {
            var slot = card.values.Find(x => x.fieldId == fieldId);
            if (slot == null)
            {
                slot = new CardFieldValue { fieldId = fieldId };
                card.values.Add(slot);
            }
            slot.value = value ?? "";
        }
        public static ZoneOverride GetOverride(CardDocument card, string zoneId)
        {
            return card == null ? null : card.zoneOverrides.Find(x => x.zoneId == zoneId);
        }
        public static ZoneOverride EnsureOverride(CardDocument card, string zoneId)
        {
            var entry = GetOverride(card, zoneId);
            if (entry == null)
            {
                entry = new ZoneOverride { zoneId = zoneId };
                card.zoneOverrides.Add(entry);
            }
            return entry;
        }
    }
}
