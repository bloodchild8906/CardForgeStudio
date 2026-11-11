using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

/// <summary>
/// Asymmetric game modes: precisely one solo player versus 1–4 opposing players.
/// Supports optional server-authoritative AI filling missing opponent seats.
/// </summary>
public static class TeamMatchComposer
{
    public static GameState Create(string rulePackId, IReadOnlyList<string> humanPlayers,
        int opponentCount, bool allowBotBackfill, Func<string> botIdFactory)
    {
        if (opponentCount is < 1 or > 4)
            throw new InvalidOperationException("Supported formats are 1v1, 1v2, 1v3 and 1v4.");
        if (humanPlayers == null || humanPlayers.Count == 0)
            throw new InvalidOperationException("A solo player is required.");
        if (humanPlayers.Distinct(StringComparer.Ordinal).Count() != humanPlayers.Count)
            throw new InvalidOperationException("Every participant must have a unique ID.");

        var seats = opponentCount + 1;
        if (humanPlayers.Count > seats)
            throw new InvalidOperationException($"1v{opponentCount} has exactly {seats} seats.");
        if (!allowBotBackfill && humanPlayers.Count != seats)
            throw new InvalidOperationException("Fill all seats or enable AI replacements.");

        var players = humanPlayers.ToList();
        var attempts = 0;
        while (players.Count < seats)
        {
            if (++attempts > seats * 4)
                throw new InvalidOperationException("Could not allocate unique AI identities.");
            var id = botIdFactory();
            if (string.IsNullOrWhiteSpace(id) || players.Contains(id, StringComparer.Ordinal)) continue;
            players.Add(id);
        }

        var match = new GameState { RulePackId = rulePackId, Players = players, TeamSize = opponentCount };
        // First participant is always team zero; all opponents are team one.
        for (var i = 0; i < players.Count; i++)
            match.PlayerTeams[players[i]] = i == 0 ? 0 : 1;
        return match;
    }

    public static bool IsBot(string playerId) =>
        playerId.StartsWith("bot-", StringComparison.OrdinalIgnoreCase);
}
