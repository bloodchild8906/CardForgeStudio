namespace CardForgeStudio.DemoRunner;

/// <summary>Command-line options accepted by the demo runner.</summary>
public sealed record DemoOptions(
    string? PackagePathOverride,
    bool KeepRuntimeStore,
    bool ShowFullEventLog,
    bool UseColor,
    bool UseUnicode,
    bool Compact,
    int PaceMilliseconds,
    bool ShowHelp)
{
    private const int DefaultPaceMilliseconds = 110;
    private const int MaximumPaceMilliseconds = 2000;
    private const string PacePrefix = "--pace=";

    /// <summary>Usage text printed for <c>--help</c>.</summary>
    public const string HelpText =
        "CardForgeStudio.DemoRunner [package.cardforge] [options]\n" +
        "\n" +
        "  --compact      one line per action; hide column headers, side events and zone counts\n" +
        "  --verbose      print the complete authoritative event log after the match\n" +
        "  --pace=<ms>    delay between actions so the match reads live (default 110, 0 when redirected)\n" +
        "  --fast         no delay between actions\n" +
        "  --ascii        plain ASCII borders and icons for terminals without Unicode\n" +
        "  --keep-store   keep the temporary package store and audit log on disk\n" +
        "  --no-color     disable ANSI colors (also disabled when output is redirected or NO_COLOR is set)\n" +
        "  --help         show this text\n" +
        "\n" +
        "Without a package argument the runner uses GamePackages/astral-rift-demo-1.0.0.cardforge.";

    /// <summary>Parses the raw command-line arguments into options.</summary>
    public static DemoOptions Parse(IReadOnlyList<string> arguments)
    {
        var flags = arguments
            .Where(argument => argument.StartsWith("--", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packagePath = arguments.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        var colorDisabled = flags.Contains("--no-color")
            || Environment.GetEnvironmentVariable("NO_COLOR") is not null
            || Console.IsOutputRedirected;

        return new DemoOptions(
            packagePath,
            flags.Contains("--keep-store"),
            flags.Contains("--verbose"),
            !colorDisabled,
            !flags.Contains("--ascii"),
            flags.Contains("--compact"),
            ResolvePace(arguments, flags),
            flags.Contains("--help"));
    }

    private static int ResolvePace(IReadOnlyList<string> arguments, ISet<string> flags)
    {
        var explicitPace = arguments.FirstOrDefault(argument => argument.StartsWith(PacePrefix, StringComparison.OrdinalIgnoreCase));
        if (explicitPace is not null && int.TryParse(explicitPace[PacePrefix.Length..], out var requested))
        {
            return Math.Clamp(requested, 0, MaximumPaceMilliseconds);
        }

        return flags.Contains("--fast") || Console.IsOutputRedirected ? 0 : DefaultPaceMilliseconds;
    }
}
