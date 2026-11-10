using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class BotService(RulesEngine rules)
{
    public void TakeTurn(GameState game, string botId)
    {
        if (game.ActivePlayerId != botId) return;
        var pack = rules.Get(game.RulePackId);
        var phase = pack.Phases.FirstOrDefault(candidate => candidate.Id.Equals(game.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
        var actions = pack.Actions.Where(action => phase?.AllowedActions.Contains(action.Id, StringComparer.OrdinalIgnoreCase) != false).ToList();
        var selected = actions.FirstOrDefault(action => !string.IsNullOrWhiteSpace(action.SourceZone) && SourceCard(game, botId, action.SourceZone) is not null)
            ?? actions.FirstOrDefault(action => string.IsNullOrWhiteSpace(action.SourceZone));
        if (selected is null) return;
        var source = SourceCard(game, botId, selected.SourceZone);
        try { rules.Execute(game, botId, new ExecuteGameActionRequest(selected.Id, source?.InstanceId)); }
        catch (InvalidOperationException) { }
    }

    private static CardInstance? SourceCard(GameState game, string playerId, string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return null;
        return game.Zones.TryGetValue(playerId, out var zones) && zones.TryGetValue(zoneId, out var cards) ? cards.FirstOrDefault() : null;
    }
}
