using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

/// <summary>
/// Produces the per-player game projection. Never return private card identities to
/// opponents: the visual card backs are not a substitute for server-side redaction.
/// </summary>
public static class ClientGameViewBuilder
{
    public static ClientGameView Build(GameState game, RulesEngine engine, string viewerId)
    {
        var pack = engine.Get(game.RulePackId);
        var phase = pack.Phases.FirstOrDefault(candidate =>
            candidate.Id.Equals(game.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
        var allowed = (phase?.AllowedActions ?? [])
            .Select(id => pack.Actions.FirstOrDefault(action => action.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(action => action is not null)
            .Select(action => new ClientGameAction(action!.Id, action.Name, action.SourceZone,
                action.DestinationZone, action.RequiresActivePlayer,
                action.Effects?.Select(effect => effect.Target).FirstOrDefault(target => !string.IsNullOrWhiteSpace(target))))
            .ToList();

        var zones = game.Zones.SelectMany(owner => owner.Value.Select(zone =>
        {
            var definition = pack.Zones.FirstOrDefault(candidate =>
                candidate.Id.Equals(zone.Key, StringComparison.OrdinalIgnoreCase));
            var visibility = definition?.Visibility ?? "hidden";
            var visible = visibility.Equals("public", StringComparison.OrdinalIgnoreCase) ||
                (owner.Key == viewerId && visibility.Equals("owner", StringComparison.OrdinalIgnoreCase));
            // Keep the count while hiding instance ID, card ID, and owner ID.
            var cards = visible ? zone.Value : zone.Value.Select(_ => new CardInstance("", "", "")).ToList();
            return new ClientGameZone(owner.Key, zone.Key, cards);
        })).ToList();

        var resources = game.Resources.SelectMany(owner => owner.Value.Select(resource =>
            new ClientGameResource(owner.Key, resource.Key, resource.Value))).ToList();

        return new ClientGameView(game.Id, game.RulePackId, game.Players, game.ActivePlayerId,
            game.CurrentPhaseId, game.Turn, game.Finished, game.WinnerPlayerId,
            game.VictoryRuleId, zones, resources, allowed, game.Events.TakeLast(40).ToList())
        {
            TeamSize = game.TeamSize,
            Teams = game.PlayerTeams.Select(pair => new ClientGameTeam(pair.Key, pair.Value)).ToList()
        };
    }
}
