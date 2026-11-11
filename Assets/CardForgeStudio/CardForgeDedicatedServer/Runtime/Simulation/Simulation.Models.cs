using System;
using System.Collections.Generic;

namespace CardForge.Simulation
{
    // Unity-compatible, JSON-serializable DTOs. These contracts intentionally
    // contain no UnityEngine, ASP.NET, JsonElement, or .NET 8 dependencies.
    // All field names follow the existing CardForge package JSON contract.
    [Serializable] public sealed class SimRulePack
    {
        public string id, name, version;
        public int minimumPlayers = 2, maximumPlayers = 5, startingHand = 7;
        /// <summary>Cards the new active player draws automatically when their turn starts (0 = none).</summary>
        public int drawPerTurn;
        public List<string> startingDeck = new List<string>();
        public List<SimStarterDeck> starterDecks = new List<SimStarterDeck>();
        public SimDeckRules deckRules = new SimDeckRules();
        public List<SimCard> cards = new List<SimCard>();
        public List<SimZone> zones = new List<SimZone>();
        public List<SimResource> resources = new List<SimResource>();
        public List<SimPhase> phases = new List<SimPhase>();
        public List<SimAction> actions = new List<SimAction>();
        public List<SimTrigger> triggers = new List<SimTrigger>();
        public List<SimVictoryRule> victoryRules = new List<SimVictoryRule>();
    }
    [Serializable] public sealed class SimCard
    {
        public string id, name, type, text, assetId;
        public string affinity, rarity, flavor;
        public int cost, power, toughness;
        /// <summary>Static keywords: haste (attacks the turn it arrives), guard (must be attacked first), ranged (takes no retaliation).</summary>
        public List<string> keywords = new List<string>();
        public List<SimAbility> abilities = new List<SimAbility>();
    }
    [Serializable] public sealed class SimZone
    {
        public string id, name, ownerScope = "player", visibility = "owner";
        public bool ordered;
        public int capacity;
        public List<string> allowedCardTypes = new List<string>();
        public List<string> initialCardIds = new List<string>();
        // Non-empty: exactly one matching card is taken from every player's 42-card list
        // and starts in this zone. Same game-level rule for all decks.
        public string deckStartCardType = "";
        public bool returnOnLeave;
        public bool immovable;
    }
    /// <summary>
    /// Starter decks define card selection and strategy only.
    /// All zone occupancy, allowed types and starting cards belong to SimZone,
    /// shared by every deck and player under this ruleset.
    /// </summary>
    [Serializable] public sealed class SimStarterDeck
    {
        public string id, name, strategy;
        public List<string> cardIds = new List<string>();
    }
    [Serializable] public sealed class SimResource
    {
        public string id, name, scope = "player";
        public long initialValue, minimumValue, maximumValue; // 0 max means unbounded
    }
    [Serializable] public sealed class SimPhase
    {
        public string id, name;
        public List<string> allowedActions = new List<string>();
        public bool automaticAdvance;
    }
    [Serializable] public sealed class SimCost
    {
        public string resourceId;
        public long amount;
    }
    [Serializable] public sealed class SimCondition
    {
        public string kind, subject, key, op, value;
    }
    [Serializable] public sealed class SimEffect
    {
        public string kind, target, resourceId, fromZone, toZone, eventType, message;
        public long amount;
        public List<SimParameter> parameters = new List<SimParameter>();
    }
    [Serializable] public sealed class SimAction
    {
        public string id, name, sourceZone, destinationZone;
        public bool requiresActivePlayer = true;
        public List<SimCost> costs = new List<SimCost>();
        public List<SimCondition> conditions = new List<SimCondition>();
        public List<SimEffect> effects = new List<SimEffect>();
    }
    [Serializable] public sealed class SimTrigger
    {
        public string id, name;
        public List<string> causeActionIds = new List<string>();
        public List<string> responseActionIds = new List<string>();
        public int priority;
    }
    [Serializable] public sealed class SimAbility
    {
        public string id, name, trigger;
        public List<SimCondition> conditions = new List<SimCondition>();
        public List<SimEffect> effects = new List<SimEffect>();
    }
    [Serializable] public sealed class SimVictoryRule
    {
        public string id, name, kind, outcome = "win", scope = "player";
        public string resourceId, zoneId, comparison = "less-than-or-equal";
        public long threshold;
    }
    [Serializable] public sealed class SimCardInstance
    {
        public string instanceId, cardId, ownerId;
        public SimCardInstance() { }
        public SimCardInstance(string instanceId, string cardId, string ownerId)
        { this.instanceId = instanceId; this.cardId = cardId; this.ownerId = ownerId; }
    }
    [Serializable] public sealed class SimZoneState
    {
        public string ownerId, zoneId;
        public List<SimCardInstance> cards = new List<SimCardInstance>();
    }
    [Serializable] public sealed class SimResourceState
    {
        public string ownerId, resourceId;
        public long value;
    }
    [Serializable] public sealed class SimTeamMember
    {
        public string playerId;
        public int teamId;
    }
    [Serializable] public sealed class SimEvent
    {
        public string type, message, actorId;
        public int sequence;
    }
    [Serializable] public sealed class SimMatch
    {
        public string id, rulePackId, activePlayerId, currentPhaseId, winnerPlayerId;
        public int opponentCount = 1, turn = 1, actionSequence;
        public bool finished;
        public List<string> players = new List<string>();
        public List<SimTeamMember> teams = new List<SimTeamMember>();
        public List<SimZoneState> zones = new List<SimZoneState>();
        public List<SimResourceState> resources = new List<SimResourceState>();
        public List<string> eliminated = new List<string>();
        public List<SimEvent> events = new List<SimEvent>();
        public List<SimParameter> data = new List<SimParameter>();
        public List<SimPlayerDeck> playerDecks = new List<SimPlayerDeck>();
    }
    [Serializable] public sealed class SimPlayerDeck
    {
        public string playerId;
        public string starterDeckId;
        public List<string> cardIds = new List<string>();
    }
    [Serializable] public sealed class SimActionRequest
    {
        public string actionId, sourceInstanceId, targetInstanceId, targetPlayerId;
        public List<SimParameter> parameters = new List<SimParameter>();
    }
    [Serializable] public sealed class SimParameter { public string key, value; }
    [Serializable] public sealed class SimMatchView
    {
        public string id, rulePackId, activePlayerId, currentPhaseId, winnerPlayerId;
        public int turn, opponentCount;
        public bool finished;
        public List<string> players = new List<string>();
        public List<SimTeamMember> teams = new List<SimTeamMember>();
        public List<SimZoneState> zones = new List<SimZoneState>();
        public List<SimResourceState> resources = new List<SimResourceState>();
        public List<string> allowedActions = new List<string>();
        public List<SimEvent> events = new List<SimEvent>();
        /// <summary>Per-card state for the UI: dmg:, bp:, bt:, exh:, sick:, level:, tax: keys keyed by instance ID.</summary>
        public List<SimParameter> data = new List<SimParameter>();
    }
}
