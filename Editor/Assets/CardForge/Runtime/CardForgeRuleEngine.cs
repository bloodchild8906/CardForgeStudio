using System;
using System.Collections.Generic;
using System.Linq;

namespace CardForge
{
    public sealed class CardForgeRuleEngine
    {
        readonly RuleStateMachineDefinition machine;
        readonly Dictionary<string, TriggerDocument> triggers;
        readonly Dictionary<string, ActionDocument> actions;
        readonly Dictionary<string, AbilityDocument> abilities;
        readonly Dictionary<string, KeywordDocument> keywords;
        readonly Dictionary<string, RuleCompiledAction> compiledActions;
        readonly List<RuleResponseWindow> windows = new List<RuleResponseWindow>();
        int responseSequence;

        public IReadOnlyList<RuleResponseWindow> ResponseWindows => windows;

        public CardForgeRuleEngine(RuleStateMachineDefinition definition)
        {
            machine = definition ?? new RuleStateMachineDefinition();
            triggers = (machine.triggers ?? new List<TriggerDocument>()).Where(x => x != null && !string.IsNullOrEmpty(x.id)).ToDictionary(x => x.id);
            actions = (machine.actions ?? new List<ActionDocument>()).Where(x => x != null && !string.IsNullOrEmpty(x.id)).ToDictionary(x => x.id);
            abilities = (machine.abilities ?? new List<AbilityDocument>()).Where(x => x != null && !string.IsNullOrEmpty(x.id)).ToDictionary(x => x.id);
            keywords = (machine.keywords ?? new List<KeywordDocument>()).Where(x => x != null && !string.IsNullOrEmpty(x.id)).ToDictionary(x => x.id);
            compiledActions = (machine.compiledActions ?? new List<RuleCompiledAction>()).Where(x => x != null && !string.IsNullOrEmpty(x.id)).ToDictionary(x => x.id);
            foreach (var action in actions.Values)
                if (!compiledActions.ContainsKey(action.id)) compiledActions[action.id] = CardForgeRules.CompileAction(action);
        }

        public RuleEngineResult RaiseTrigger(string triggerId, string sourcePlayerId, IEnumerable<string> playerIds)
        {
            return OpenTrigger(triggerId, sourcePlayerId, playerIds, "");
        }

        public RuleEngineResult Pass(string triggerId, string playerId)
        {
            var window = FindOpenWindow(triggerId);
            if (window == null) return Reject(triggerId, "No open response window.");
            if (!IsTopmost(window)) return Reject(triggerId, "Resolve the nested trigger response window first.");
            if (!window.playerIds.Contains(playerId)) return Reject(triggerId, "Player is not part of this response window.");
            if (!window.passedPlayerIds.Contains(playerId)) window.passedPlayerIds.Add(playerId);
            if (!window.AllPlayersPassed) return new RuleEngineResult { kind = RuleEngineResultKind.WaitingForResponses, triggerId = triggerId, playerId = playerId, reason = "Waiting for every player to pass." };
            return ResolveNext(window, playerId);
        }

        public RuleEngineResult Respond(string triggerId, string playerId, string actionId)
        {
            var window = FindOpenWindow(triggerId);
            if (window == null) return Reject(triggerId, "No open response window.");
            if (!IsTopmost(window)) return Reject(triggerId, "Respond to the nested trigger response window first.");
            if (!window.playerIds.Contains(playerId)) return Reject(triggerId, "Player is not part of this response window.");
            if (!window.passedPlayerIds.Remove(playerId)) { }
            var trigger = triggers[triggerId];
            if (!(trigger.responseActionIds ?? new List<string>()).Contains(actionId)) return Reject(triggerId, "This action is not allowed in the trigger response window.");
            if (!actions.ContainsKey(actionId)) return Reject(triggerId, "Unknown response action.");
            window.responseStack.Add(new RuleResponseEntry { sequence = ++responseSequence, playerId = playerId, triggerId = triggerId, actionId = actionId });
            return new RuleEngineResult { kind = RuleEngineResultKind.ResponseAccepted, triggerId = triggerId, actionId = actionId, playerId = playerId, reason = "Response added to the top of the stack." };
        }

        public RuleEngineResult Resolve(string triggerId)
        {
            var window = FindOpenWindow(triggerId);
            if (window == null) return Reject(triggerId, "No open response window.");
            if (!IsTopmost(window)) return Reject(triggerId, "Resolve the nested trigger response window first.");
            if (!window.AllPlayersPassed) return Reject(triggerId, "Resolution waits until every player passes.");
            return ResolveNext(window, "");
        }

        public RuleEngineResult ResolveNext(RuleResponseWindow window, string playerId)
        {
            if (window == null || !window.open) return Reject(window == null ? "" : window.triggerId, "No open response window.");
            if (!IsTopmost(window)) return Reject(window.triggerId, "Resolve the nested trigger response window first.");
            if (!window.AllPlayersPassed) return Reject(window.triggerId, "Resolution waits until every player passes.");
            if (window.responseStack.Count == 0)
            {
                window.open = false;
                return new RuleEngineResult { kind = RuleEngineResultKind.TriggerResolved, triggerId = window.triggerId, playerId = playerId, reason = "Trigger resolved after all response passes." };
            }
            var entry = window.responseStack[window.responseStack.Count - 1];
            window.responseStack.RemoveAt(window.responseStack.Count - 1);
            window.passedPlayerIds.Clear();
            var result = ExecuteAction(entry, window);
            result.reason = "Resolved last in, first out.";
            return result;
        }

        public IReadOnlyList<string> GetAllowedResponseActions(string triggerId, string playerId)
        {
            var window = FindOpenWindow(triggerId);
            if (window == null || !IsTopmost(window) || !window.playerIds.Contains(playerId)) return new List<string>();
            return (triggers[triggerId].responseActionIds ?? new List<string>()).Where(actions.ContainsKey).ToList();
        }

        public IReadOnlyList<AbilityDocument> GetKeywordAbilities(string keywordId)
        {
            if (!keywords.TryGetValue(keywordId ?? "", out var keyword)) return new List<AbilityDocument>();
            return (keyword.abilityIds ?? new List<string>()).Where(abilities.ContainsKey).Select(id => abilities[id]).ToList();
        }

        RuleEngineResult ExecuteAction(RuleResponseEntry entry, RuleResponseWindow parent)
        {
            var result = new RuleEngineResult { kind = RuleEngineResultKind.ResponseResolved, triggerId = entry.triggerId, actionId = entry.actionId, playerId = entry.playerId };
            if (!compiledActions.TryGetValue(entry.actionId, out var compiled)) return result;
            result.graphStepIds.AddRange(compiled.orderedGraphStepIds);
            result.stateChangeStepIds.AddRange(compiled.stateChangeStepIds);
            foreach (var triggerId in compiled.causedTriggerIds)
            {
                var opened = OpenTrigger(triggerId, entry.playerId, parent.playerIds, parent.id);
                if (opened.kind != RuleEngineResultKind.Rejected) result.causedTriggerIds.Add(triggerId);
            }
            return result;
        }

        RuleEngineResult OpenTrigger(string triggerId, string sourcePlayerId, IEnumerable<string> playerIds, string parentWindowId)
        {
            if (!triggers.ContainsKey(triggerId)) return Reject(triggerId, "Unknown trigger.");
            var players = (playerIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (players.Count == 0) return Reject(triggerId, "A trigger response window requires at least one player.");
            var window = new RuleResponseWindow { triggerId = triggerId, sourcePlayerId = sourcePlayerId ?? "", parentWindowId = parentWindowId ?? "" };
            window.playerIds.AddRange(players);
            windows.Add(window);
            return new RuleEngineResult { kind = RuleEngineResultKind.WaitingForResponses, triggerId = triggerId, reason = "All players may respond before resolution." };
        }

        RuleResponseWindow FindOpenWindow(string triggerId) => windows.LastOrDefault(window => window.open && window.triggerId == triggerId);
        bool IsTopmost(RuleResponseWindow window) => windows.LastOrDefault(candidate => candidate.open) == window;
        static RuleEngineResult Reject(string triggerId, string reason) => new RuleEngineResult { kind = RuleEngineResultKind.Rejected, triggerId = triggerId, reason = reason };
    }
}
