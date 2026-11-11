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

    public void TakeTurn(GameState game, string botId)
    {
        var pack = rules.Get(game.RulePackId);
        var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var opponent = game.Players.FirstOrDefault(player => !player.Equals(botId, StringComparison.Ordinal) && !game.EliminatedPlayerIds.Contains(player));

        for (var step = 0; step < MaxActionsPerTurn; step++)
        {
            if (game.Finished || !game.ActivePlayerId.Equals(botId, StringComparison.Ordinal)) return;

            var phase = pack.Phases.FirstOrDefault(candidate => candidate.Id.Equals(game.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
            var allowed = pack.Actions
                .Where(action => phase is null || phase.AllowedActions.Contains(action.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var productive = allowed
                .Where(action => !IsStructural(action) && uses.GetValueOrDefault(action.Id) < MaxRepeatsPerAction)
                .OrderByDescending(action => SourceCard(game, botId, action.SourceZone) is not null)
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
                uses[action.Id] = MaxRepeatsPerAction;
            }
            if (!acted) return;
        }
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
