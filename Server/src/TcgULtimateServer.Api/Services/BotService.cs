using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

/// <summary>
/// A deliberately simple, rule-pack-neutral opponent. When it is the bot's turn it plays a complete turn:
/// it uses the actions the current phase allows (cards first, a couple of times each), then advances the phase
/// or ends the turn, until priority passes back to someone else or the game finishes.
/// It never concedes, never targets itself, and never gets stuck in a phase it cannot leave.
/// </summary>
public sealed class BotService(RulesEngine rules)
{
    /// <summary>Hard stop so a malformed rule pack can never keep a request running forever.</summary>
    private const int MaxActionsPerTurn = 64;

    /// <summary>How many times the bot uses the same non-structural action in one turn.</summary>
    private const int MaxRepeatsPerAction = 2;

    /// <summary>Advances consecutive bot seats, without letting one malfunctioning bot lock the server.</summary>
    public void ResolvePendingBotTurns(GameState game)
    {
        for (var i = 0; i < game.Players.Count && !game.Finished &&
            TeamMatchComposer.IsBot(game.ActivePlayerId); i++)
        {
            var current = game.ActivePlayerId;
            TakeTurn(game, current);
            if (game.ActivePlayerId == current) break;
        }
    }

    public void TakeTurn(GameState game, string botId)
    {
        var pack = rules.Get(game.RulePackId);
        var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var difficulty = Math.Clamp(game.BotDifficulty, 1, 4);
        var maxRepeats = difficulty == 1 ? 1 : difficulty >= 4 ? 3 : MaxRepeatsPerAction;
        var ownTeam = game.PlayerTeams.GetValueOrDefault(botId, -1);
        var opponent = game.Players.FirstOrDefault(player =>
            player != botId && !game.EliminatedPlayerIds.Contains(player) &&
            (game.PlayerTeams.Count == 0 || game.PlayerTeams.GetValueOrDefault(player, -2) != ownTeam));

        for (var step = 0; step < MaxActionsPerTurn; step++)
        {
            if (game.Finished || !game.ActivePlayerId.Equals(botId, StringComparison.Ordinal)) return;

            var phase = pack.Phases.FirstOrDefault(candidate => candidate.Id.Equals(game.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
            var allowed = pack.Actions
                .Where(action => phase is null || phase.AllowedActions.Contains(action.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var productive = allowed
                .Where(action => !IsStructural(action) && uses.GetValueOrDefault(action.Id) < maxRepeats)
                .OrderByDescending(action => EvaluateAction(pack, game, botId, action, difficulty))
                .ToList();
            var leaving = allowed.Where(action => HasEffect(action, "advance_phase"))
                .Concat(allowed.Where(action => HasEffect(action, "end_turn")))
                .ToList();

            var acted = false;
            foreach (var action in productive.Concat(leaving))
            {
                if (TryExecute(game, botId, action, opponent))
                {
                    uses[action.Id] = uses.GetValueOrDefault(action.Id) + 1;
                    acted = true;
                    break;
                }
                // An illegal attempt must not be retried this turn (cost, condition or target was not satisfied).
                uses[action.Id] = maxRepeats;
            }
            if (!acted) return;
        }
    }

    private static double EvaluateAction(RulePack pack, GameState game, string botId, TcgActionDefinition action, int difficulty)
    {
        var source = SourceCard(game, botId, action.SourceZone);
        if (difficulty == 1) return source == null ? 0 : 1;
        var card = source == null ? null : pack.Cards.FirstOrDefault(def => def.Id == source.CardId);
        var benefit = (source == null ? 0 : 5) + (card == null ? 0 :
            (card.Power + card.Toughness + card.Abilities.Count * 3.0) / Math.Max(1, card.Cost + 1));
        foreach (var effect in action.Effects ?? [])
        {
            if (effect.Kind == "draw") benefit += 2 * Math.Max(1, effect.Amount);
            if (effect.Kind == "modify_resource") benefit += effect.Target == "target_player" && effect.Amount < 0
                ? -effect.Amount * 3 : effect.Amount * 2;
            if (effect.Kind == "move_card") benefit += 4;
        }
        return difficulty >= 4 ? benefit : Math.Round(benefit / 3);
    }

    private bool TryExecute(GameState game, string botId, TcgActionDefinition action, string? opponent)
    {
        var source = SourceCard(game, botId, action.SourceZone);
        // A card-moving action without a card in its source zone can never succeed.
        if (!string.IsNullOrWhiteSpace(action.SourceZone) && !string.IsNullOrWhiteSpace(action.DestinationZone) && source is null) return false;
        try
        {
            rules.Execute(game, botId, new ExecuteGameActionRequest(action.Id, source?.InstanceId, opponent));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsStructural(TcgActionDefinition action) =>
        HasEffect(action, "advance_phase") || HasEffect(action, "end_turn") || HasEffect(action, "finish_game");

    private static bool HasEffect(TcgActionDefinition action, string kind) =>
        action.Effects?.Any(effect => effect.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) == true;

    private static CardInstance? SourceCard(GameState game, string playerId, string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return null;
        return game.Zones.TryGetValue(playerId, out var zones) && zones.TryGetValue(zoneId, out var cards) ? cards.FirstOrDefault() : null;
    }
}
