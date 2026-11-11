using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

/// <summary>
/// Shared deck-strength estimator used by matchmaking and AI. It measures card
/// efficiency, curve, density and repeatability, rather than treating every deck
/// as equal. Skill rating is kept separate to avoid hiding large ELO gaps.
/// </summary>
public static class MatchQuality
{
    public static DeckDefinition? SelectDeck(AppState state, string userId, string rulePackId, string? deckId)
    {
        var decks = state.Decks.Values.Where(deck =>
            deck.OwnerId == userId && !deck.Suspended &&
            deck.RulePackId.Equals(rulePackId, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(deckId)
            ? decks.OrderByDescending(deck => deck.UpdatedAt).FirstOrDefault()
            : decks.FirstOrDefault(deck => deck.Id == deckId);
    }

    public static int DeckPower(AppState state, string rulePackId, DeckDefinition? deck)
    {
        if (deck is null || deck.CardIds.Count == 0 || !state.RulePacks.TryGetValue(rulePackId, out var pack))
            return 500;

        var lookup = pack.Cards.ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);
        var valid = deck.CardIds.Where(lookup.ContainsKey).Select(id => lookup[id]).ToList();
        if (valid.Count == 0) return 500;

        var averageEfficiency = valid.Average(card =>
        {
            var stats = Math.Max(0, card.Power) + Math.Max(0, card.Toughness);
            var abilities = card.Abilities?.Count ?? 0;
            return (stats + 2.5 * abilities + 1.5) / Math.Max(1, card.Cost + 1);
        });
        var lowCostFraction = valid.Count(card => card.Cost <= 3) / (double)valid.Count;
        var repeatability = deck.CardIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() >= 2) / (double)Math.Max(1, deck.CardIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var score = 300 + 90 * averageEfficiency + 105 * lowCostFraction + 95 * repeatability;
        return (int)Math.Clamp(Math.Round(score), 100, 1000);
    }

    public static bool Compatible(int skillOne, int skillTwo, int powerOne, int powerTwo,
        int skillTolerance, int powerTolerance)
    {
        return Math.Abs(skillOne - skillTwo) <= skillTolerance &&
               Math.Abs(powerOne - powerTwo) <= powerTolerance;
    }

    public static int AiDifficulty(int skill, int deckPower)
    {
        // 1: novice, 2: intermediate, 3: advanced, 4: expert
        var combined = skill * .65 + deckPower * .35;
        if (combined < 450) return 1;
        if (combined < 850) return 2;
        if (combined < 1300) return 3;
        return 4;
    }
}
