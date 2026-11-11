using TcgULtimateServer.Api.Models;

namespace CardForgeStudio.DemoRunner;

/// <summary>Point-in-time copy of the numbers that change during a match, used to describe what an action did.</summary>
public sealed record GameSnapshot(
    IReadOnlyDictionary<string, long> ResourceTotals,
    IReadOnlyDictionary<string, long> ZoneCardCounts,
    int EventCount)
{
    private const string SharedResourceOwner = "__game__";

    /// <summary>Copies the current resources, zone sizes and event count from the game.</summary>
    public static GameSnapshot Capture(GameState game)
    {
        var resourceTotals = new Dictionary<string, long>();
        foreach (var (owner, resources) in game.Resources)
        {
            foreach (var (resourceName, amount) in resources) resourceTotals[$"{owner}.{resourceName}"] = amount;
        }

        var zoneCardCounts = new Dictionary<string, long>();
        foreach (var (owner, zones) in game.Zones)
        {
            foreach (var (zoneName, cards) in zones) zoneCardCounts[$"{owner}.{zoneName}"] = cards.Count;
        }

        return new GameSnapshot(resourceTotals, zoneCardCounts, game.Events.Count);
    }

    /// <summary>Lists every resource and zone count that differs from an earlier snapshot, such as "Nova energy +1".</summary>
    public IReadOnlyList<string> DescribeChangesSince(GameSnapshot earlier)
    {
        return Differences(earlier.ResourceTotals, ResourceTotals)
            .Concat(Differences(earlier.ZoneCardCounts, ZoneCardCounts))
            .ToList();
    }

    private static IEnumerable<string> Differences(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after)
    {
        foreach (var (key, valueAfter) in after)
        {
            var delta = valueAfter - before.GetValueOrDefault(key);
            if (delta != 0) yield return $"{FormatLabel(key)} {delta:+#;-#}";
        }
    }

    private static string FormatLabel(string key)
    {
        var sharedPrefix = SharedResourceOwner + ".";
        return key.StartsWith(sharedPrefix, StringComparison.Ordinal)
            ? key[sharedPrefix.Length..]
            : key.Replace('.', ' ');
    }
}
