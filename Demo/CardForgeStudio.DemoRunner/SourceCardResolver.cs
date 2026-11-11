using TcgULtimateServer.Api.Models;

namespace CardForgeStudio.DemoRunner;

/// <summary>Chooses the card instance that should be the source of a scripted action so the card's ability fires.</summary>
public sealed class SourceCardResolver
{
    private static readonly string[] ZonePreferenceOrder = ["battlefield", "hand", "discard", "void", "deck"];

    private readonly RulePack rulePack;
    private readonly GameState game;

    /// <summary>Creates a resolver for one game and its rule pack.</summary>
    public SourceCardResolver(RulePack rulePack, GameState game)
    {
        this.rulePack = rulePack;
        this.game = game;
    }

    /// <summary>
    /// Returns a card the actor owns that carries the requested ability, restricted to the action's
    /// source zone when the action moves a card. Returns null when the step needs no source card.
    /// </summary>
    public CardInstance? Resolve(string actor, string actionId, string? requiredAbilityId)
    {
        if (requiredAbilityId is null) return null;

        var playerZones = game.Zones[actor];
        foreach (var zoneName in ZonesToSearch(actionId, playerZones.Keys))
        {
            var match = playerZones[zoneName].FirstOrDefault(card => CardHasAbility(card.CardId, requiredAbilityId));
            if (match is not null) return match;
        }

        throw new InvalidOperationException($"{actor} has no card with ability '{requiredAbilityId}' available for '{actionId}'.");
    }

    private IEnumerable<string> ZonesToSearch(string actionId, IEnumerable<string> availableZones)
    {
        var requiredZone = rulePack.Actions.First(action => action.Id == actionId).SourceZone;
        if (!string.IsNullOrWhiteSpace(requiredZone)) return [requiredZone];

        var available = availableZones.ToHashSet(StringComparer.Ordinal);
        return ZonePreferenceOrder.Where(available.Contains);
    }

    private bool CardHasAbility(string cardId, string abilityId)
    {
        var definition = rulePack.Cards.FirstOrDefault(card => card.Id.Equals(cardId, StringComparison.OrdinalIgnoreCase));
        return definition?.Abilities.Any(ability => ability.Id == abilityId) == true;
    }
}
