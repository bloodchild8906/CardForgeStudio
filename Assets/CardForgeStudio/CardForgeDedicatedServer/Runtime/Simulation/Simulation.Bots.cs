using System;
using System.Collections.Generic;
using System.Linq;

namespace CardForge.Simulation
{
    /// <summary>
    /// Deterministic, bounded bot turns for a Unity Dedicated Server.
    /// Difficulty is derived from the same rating/deck-power inputs as matchmaking.
    /// Bots never target allies with hostile effects.
    /// </summary>
    public sealed class SimulationBots
    {
        private readonly SimulationEngine simulation;
        public SimulationBots(SimulationEngine simulation) { this.simulation = simulation; }

        public static int Difficulty(int skillRating, int deckPower)
        {
            var score = skillRating * .65 + deckPower * .35;
            return score < 450 ? 1 : score < 850 ? 2 : score < 1300 ? 3 : 4;
        }

        public void PlayPendingTurns(SimMatch match, int difficulty = 2)
        {
            for (var seat = 0; seat < match.players.Count && !match.finished &&
                match.activePlayerId.StartsWith("bot-", StringComparison.OrdinalIgnoreCase); seat++)
            {
                var previousActor = match.activePlayerId;
                PlayTurn(match, previousActor, difficulty);
                if (match.activePlayerId == previousActor) break;
            }
        }

        public void PlayTurn(SimMatch match, string actor, int difficulty = 2)
        {
            if (match.finished || match.activePlayerId != actor) return;
            var rules = simulation.GetRules(match.rulePackId);
            var team = match.teams.First(t => t.playerId == actor).teamId;
            var enemy = match.players.FirstOrDefault(player =>
                player != actor && !match.eliminated.Contains(player) &&
                match.teams.Any(t => t.playerId == player && t.teamId != team));
            if (enemy == null) return;

            var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var step = 0; step < 64 && !match.finished && match.activePlayerId == actor; step++)
            {
                var phase = rules.phases.FirstOrDefault(p => p.id == match.currentPhaseId);
                if (phase == null) break;
                var actions = rules.actions.Where(a => phase.allowedActions.Contains(a.id)).ToList();
                var available = actions.Where(a =>
                    usage.GetValueOrDefault(a.id) < (difficulty == 1 ? 1 : difficulty >= 4 ? 3 : 2))
                    .OrderByDescending(a => Value(a, match, actor, rules, difficulty)).ToList();
                var used = false;
                foreach (var action in available)
                {
                    if (action.effects.Any(e => e.kind == "end_turn" || e.kind == "advance_phase"))
                        continue;
                    var zone = match.zones.FirstOrDefault(z =>
                        z.ownerId == actor && z.zoneId == action.sourceZone);
                    if (!string.IsNullOrEmpty(action.sourceZone) &&
                        (zone == null || zone.cards.Count == 0)) continue;
                    var instance = zone == null ? null : zone.cards.FirstOrDefault();
                    var targetCard = match.zones.Where(z => z.ownerId == enemy)
                        .SelectMany(z => z.cards).FirstOrDefault();
                    var request = new SimActionRequest
                    {
                        actionId = action.id,
                        sourceInstanceId = instance == null ? null : instance.instanceId,
                        targetPlayerId = enemy,
                        targetInstanceId = targetCard == null ? null : targetCard.instanceId
                    };
                    try
                    {
                        simulation.Execute(match, actor, request);
                        usage[action.id] = usage.GetValueOrDefault(action.id) + 1;
                        used = true;
                        break;
                    }
                    catch (InvalidOperationException) { /* Try the next legal action. */ }
                }
                if (used) continue;

                // No more usable action in this phase: advance or end the turn.
                var advance = actions.FirstOrDefault(a =>
                    a.effects.Any(e => e.kind == "advance_phase" || e.kind == "end_turn"));
                if (advance == null) break;
                try { simulation.Execute(match, actor, new SimActionRequest { actionId = advance.id }); }
                catch (InvalidOperationException) { break; }
            }
        }

        private static double Value(SimAction action, SimMatch match, string actor,
            SimRulePack rules, int difficulty)
        {
            if (difficulty == 1) return string.IsNullOrEmpty(action.sourceZone) ? 0 : 1;
            var source = match.zones.FirstOrDefault(z => z.ownerId == actor && z.zoneId == action.sourceZone)?
                .cards.FirstOrDefault();
            var card = source == null ? null : rules.cards.FirstOrDefault(c => c.id == source.cardId);
            var score = card == null ? 0 :
                (card.power + card.toughness + card.abilities.Count * 3.0) / Math.Max(1, card.cost + 1);
            foreach (var effect in action.effects)
            {
                if (effect.kind == "draw") score += Math.Max(1, effect.amount) * 2;
                if (effect.kind == "modify_resource") score += Math.Abs(effect.amount) * 2;
                if (effect.kind == "move_card") score += 3;
            }
            return difficulty == 4 ? score : Math.Round(score / 3);
        }
    }
}
