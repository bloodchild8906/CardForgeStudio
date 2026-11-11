using System.Globalization;
using System.Text;
using TcgULtimateServer.Api.Models;
using static CardForgeStudio.DemoRunner.ConsoleStyle;

namespace CardForgeStudio.DemoRunner;

/// <summary>Writes the demo's console output: banner, turn timeline, scoreboards and the final report.</summary>
public sealed class ConsoleReporter
{
    private const string SharedResourceOwner = "__game__";
    private const string ActionPerformedMarker = " performed ";
    private const string PackIdPrefix = "ar";
    private const int RecentEventCount = 8;
    private const int PhaseColumn = 10;
    private const int ResourceBarCells = 6;
    private const int ResourceLabelWidth = 8;
    private const long MinimumBarScale = 5;
    private const long MaximumReadableBarScale = 20;
    private const int GaugeCells = 24;

    private readonly DemoOptions options;
    private readonly ConsoleStyle ui;
    private readonly Dictionary<string, long> observedMaximum = new(StringComparer.Ordinal);
    private RulePack? rulePack;
    private GameSnapshot? turnStart;

    /// <summary>Creates a reporter and prepares the console for UTF-8 and ANSI output.</summary>
    public ConsoleReporter(DemoOptions options)
    {
        this.options = options;
        Console.OutputEncoding = new UTF8Encoding(false);
        if (options.UseColor) EnableVirtualTerminal();
        ui = new ConsoleStyle(options.UseColor, options.UseUnicode, DetectWidth());
    }

    private int ActionColumn => Math.Clamp(ui.Width - 46, 26, 46);

    /// <summary>Supplies the rule pack so ids can be shown as the names the designer chose.</summary>
    public void UseRulePack(RulePack pack) => rulePack = pack;

    /// <summary>Prints the title panel and the imported package counts.</summary>
    public void PrintBanner(ImportedPackageSummary package, IReadOnlyList<string> players, int turnCount)
    {
        var g = ui.Glyphs;
        var center = ui.BoxTextWidth;
        var lines = new List<string>
        {
            Center(ui.Paint("C A R D F O R G E   S T U D I O", Bold + Amber), center),
            Center(ui.Paint("authoritative match demo  " + g.Sep + "  every rule decided by the server engine", Gray), center),
            string.Empty,
            $"{ui.Paint("Package", Gray)}  {ui.Paint(package.Name, Bold + White)} {ui.Paint("v" + package.Version, Gray)}",
            $"{ui.Paint("Content", Gray)}  {Count(package.Cards, "cards")} {g.Sep} {Count(package.Actions, "actions")} {g.Sep} {Count(package.Abilities, "abilities")}",
            $"{ui.Paint("Match  ", Gray)}  {string.Join(ui.Paint("  vs  ", Gray), players.Select(name => ui.Paint(name, Bold + Teal)))}  {ui.Paint(g.Sep + " " + turnCount + " scripted turns", Gray)}",
        };

        Console.WriteLine();
        foreach (var line in ui.Box(lines, Amber, heavy: true)) Console.WriteLine(line);
        Console.WriteLine();
    }

    /// <summary>Prints a completed start-up step such as "Package imported".</summary>
    public void PrintStage(string label, string detail)
    {
        Console.WriteLine($"  {ui.Paint(ui.Glyphs.Ok, Green)} {PadRight(label, 22)}{ui.Paint(detail, Gray)}");
        Pause(options.PaceMilliseconds);
    }

    /// <summary>Prints the header that opens a turn and remembers the starting numbers for deltas.</summary>
    public void PrintTurnHeader(GameState game, string actor, string opponent)
    {
        turnStart = GameSnapshot.Capture(game);
        Console.WriteLine();
        Console.WriteLine(ui.Header($"TURN {game.Turn} {ui.Glyphs.Arrow} {actor.ToUpperInvariant()}", "vs " + opponent));
        if (options.Compact) return;
        Console.WriteLine(ui.Paint($"  {"PHASE".PadRight(PhaseColumn)}   {PadRight("ACTION", ActionColumn)}  EFFECT", Gray));
    }

    /// <summary>Prints one executed action: phase, action, source card, what changed and any extra events it raised.</summary>
    public void PrintStep(string phaseBefore, string phaseAfter, string actionId, string? sourceCardId, IReadOnlyList<GameEvent> newEvents, IReadOnlyList<string> changes)
    {
        var g = ui.Glyphs;
        var actionEvent = newEvents.FirstOrDefault(item => item.Type == "action_executed");
        var sideEvents = newEvents.Where(item => !ReferenceEquals(item, actionEvent)).ToList();
        var advanced = !string.Equals(phaseBefore, phaseAfter, StringComparison.Ordinal);
        var abilityFired = sideEvents.Any(item => item.Type.Contains("ability", StringComparison.OrdinalIgnoreCase));

        var icon = advanced ? ui.Paint(g.Advance, Cyan) : abilityFired ? ui.Paint(g.Spark, Violet) : ui.Paint(g.Diamond, Amber);
        var phase = ui.Paint(ui.Fit(PhaseName(phaseBefore).ToUpperInvariant(), PhaseColumn).PadRight(PhaseColumn), Gray);
        var actionName = ActionName(actionId, actionEvent);
        var actionCell = ui.Paint(ui.Fit(actionName, ActionColumn - 2), Bold + White);
        if (sourceCardId is not null)
        {
            var room = ActionColumn - actionName.Length - 5;
            if (room >= 6) actionCell += " " + ui.Paint(g.LeftCard + ui.Fit(CardName(sourceCardId), room) + g.RightCard, Teal);
        }

        var effects = new List<string>();
        if (advanced) effects.Add(ui.Paint($"{g.Right} {PhaseName(phaseAfter).ToUpperInvariant()}", Cyan) + " " + PhaseProgress(phaseAfter));
        effects.AddRange(changes.Select(FormatChange));
        if (effects.Count == 0) effects.Add(ui.Paint("no state change", Gray));

        Console.WriteLine($"  {phase} {icon} {PadRight(actionCell, ActionColumn)}  {string.Join(ui.Paint(" " + g.Sep + " ", Gray), effects)}");
        if (!options.Compact)
        {
            var indent = new string(' ', PhaseColumn + 5);
            foreach (var sideEvent in sideEvents)
            {
                Console.WriteLine(indent + ui.Paint($"{g.Branch} {EventLabel(sideEvent.Type)}", EventStyle(sideEvent.Type)) + " " + ui.Paint(sideEvent.Message, Gray));
            }
        }

        Pause(options.PaceMilliseconds);
    }

    /// <summary>Prints the scoreboard at the end of a turn, with changes since the turn began.</summary>
    public void PrintTurnFooter(GameState game, string actor)
    {
        Console.WriteLine();
        PrintScoreboard(game, actor, turnStart);
        Pause(options.PaceMilliseconds * 2);
    }

    /// <summary>Prints the winner, final boards, coverage gauges and the recent event log.</summary>
    public void PrintFinalReport(GameState game, ImportedPackageSummary package, int distinctActionsExecuted, bool showFullEventLog)
    {
        var g = ui.Glyphs;
        var center = ui.BoxTextWidth;
        var winner = game.WinnerPlayerId ?? "nobody";
        var rule = string.IsNullOrWhiteSpace(game.VictoryRuleId) ? "victory rule" : Humanize(game.VictoryRuleId);
        var banner = new List<string>
        {
            Center(ui.Paint($"{g.Star}  VICTORY  {g.Star}", Gold), center),
            Center(ui.Paint(winner.ToUpperInvariant(), Bold + White) + ui.Paint($"  via {rule}", Gray), center),
            Center(ui.Paint($"turn {game.Turn} {g.Sep} {game.Events.Count} authoritative events", Gray), center),
        };

        Console.WriteLine();
        Console.WriteLine(ui.Header("RESULT"));
        Console.WriteLine();
        foreach (var line in ui.Box(banner, Gold, heavy: true)) Console.WriteLine(line);
        Console.WriteLine();
        turnStart = null;
        PrintScoreboard(game, null, null);

        var abilityResolutions = game.Events.Count(item => item.Type == "ability_resolved");
        var momentum = game.Resources.TryGetValue(SharedResourceOwner, out var shared) ? shared.GetValueOrDefault("momentum") : 0;
        var beacon = game.Data.TryGetValue("navigationBeacon", out var beaconValue) ? beaconValue.ToString() : "missing";
        Console.WriteLine();
        Console.WriteLine(ui.Header("COVERAGE"));
        PrintGauge("Actions exercised", distinctActionsExecuted, package.Actions);
        PrintGauge("Abilities fired", Math.Min(abilityResolutions, Math.Max(package.Abilities, 1)), package.Abilities, $"{abilityResolutions} resolutions");
        Console.WriteLine($"  {PadRight(ui.Paint("Shared momentum", Gray), 20)}{ui.Paint(momentum.ToString(CultureInfo.InvariantCulture), Bold + Amber)}");
        var beaconStyle = beacon is null or "missing" ? Red : Green;
        Console.WriteLine($"  {PadRight(ui.Paint("Navigation beacon", Gray), 20)}{ui.Paint(beacon ?? "missing", beaconStyle)}");
        Console.WriteLine();
        Console.WriteLine(ui.Header(showFullEventLog ? "EVENT LOG" : "LAST EVENTS"));
        PrintEvents(showFullEventLog ? game.Events : game.Events.TakeLast(RecentEventCount).ToList());
        Console.WriteLine();
    }

    /// <summary>Prints the message shown when the package file is missing.</summary>
    public void PrintMissingPackage(string packagePath)
    {
        Console.Error.WriteLine($"  {ui.Paint(ui.Glyphs.Fail, Red)} {ui.Paint("Astral Rift package not found", Bold + Red)}");
        Console.Error.WriteLine($"    {ui.Paint(packagePath, Gray)}");
        Console.Error.WriteLine($"    Generate it in Unity with {ui.Paint("CardForge > Generate Full Demo Game", Amber)} first.");
    }

    /// <summary>Prints a failure message and, when requested, the full exception.</summary>
    public void PrintFailure(Exception exception, bool includeStackTrace)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  {ui.Paint(ui.Glyphs.Fail, Red)} {ui.Paint("Demo failed", Bold + Red)}  {exception.Message}");
        if (includeStackTrace) Console.Error.WriteLine(exception);
        else Console.Error.WriteLine(ui.Paint("    Re-run with --verbose for the full stack trace.", Gray));
    }

    /// <summary>Prints where the temporary files were kept.</summary>
    public void PrintRetainedStore(string runtimeRoot) =>
        Console.WriteLine(ui.Paint($"  Demo runtime retained at {runtimeRoot}", Gray));

    private void PrintScoreboard(GameState game, string? activePlayer, GameSnapshot? baseline)
    {
        var g = ui.Glyphs;
        var nameWidth = game.Players.Max(player => player.Length) + 2;
        foreach (var player in game.Players)
        {
            var isActive = string.Equals(player, activePlayer, StringComparison.Ordinal);
            var marker = isActive ? ui.Paint(g.Arrow, Amber) : " ";
            var name = ui.Paint(player.ToUpperInvariant().PadRight(nameWidth), isActive ? Bold + Amber : Bold + Teal);
            var resources = game.Resources.TryGetValue(player, out var owned) ? owned : [];
            var cells = OrderedResources(resources.Keys).Select(key => ResourceCell(player, key, resources[key], baseline));
            Console.WriteLine($"  {marker} {name} {string.Join("   ", cells)}");
            if (!options.Compact) Console.WriteLine($"      {PadRight(string.Empty, nameWidth)} {ui.Paint(ZoneSummary(game, player), Gray)}");
        }

        if (game.Resources.TryGetValue(SharedResourceOwner, out var sharedResources) && sharedResources.Count > 0)
        {
            var cells = OrderedResources(sharedResources.Keys).Select(key => ResourceCell(SharedResourceOwner, key, sharedResources[key], baseline));
            Console.WriteLine($"    {ui.Paint("SHARED".PadRight(nameWidth), Gray)} {string.Join("   ", cells)}");
        }
    }

    private string ResourceCell(string owner, string key, long value, GameSnapshot? baseline)
    {
        var definedMaximum = rulePack?.Resources.FirstOrDefault(item => item.Id == key)?.MaximumValue;
        observedMaximum[key] = Math.Max(observedMaximum.GetValueOrDefault(key, MinimumBarScale), value);
        var maximum = definedMaximum is > 0 and <= MaximumReadableBarScale ? definedMaximum.Value : observedMaximum[key];

        var delta = baseline is null ? 0 : value - baseline.ResourceTotals.GetValueOrDefault($"{owner}.{key}");
        var deltaText = delta == 0 ? string.Empty : " " + ui.Paint(delta > 0 ? $"+{delta}" : delta.ToString(CultureInfo.InvariantCulture), delta > 0 ? Green : Red);
        var barStyle = key == "life" && value * 3 <= maximum ? Red : Cyan;
        var label = ui.Paint(Humanize(key).PadRight(ResourceLabelWidth), Gray);
        return $"{label} {ui.Bar(value, maximum, ResourceBarCells, barStyle)} {ui.Paint(value.ToString(CultureInfo.InvariantCulture), Bold + White)}{deltaText}";
    }

    private IEnumerable<string> OrderedResources(IEnumerable<string> keys)
    {
        var order = rulePack?.Resources.Select(item => item.Id).ToList() ?? [];
        return keys.OrderBy(key => order.IndexOf(key) is var index and >= 0 ? index : int.MaxValue).ThenBy(key => key, StringComparer.Ordinal);
    }

    private string ZoneSummary(GameState game, string player)
    {
        if (!game.Zones.TryGetValue(player, out var zones)) return string.Empty;
        var order = rulePack?.Zones.Select(item => item.Id).ToList() ?? [];
        var ordered = zones.OrderBy(item => order.IndexOf(item.Key) is var index and >= 0 ? index : int.MaxValue).ThenBy(item => item.Key, StringComparer.Ordinal);
        return string.Join($" {ui.Glyphs.Sep} ", ordered.Select(item => $"{ZoneName(item.Key)} {item.Value.Count}"));
    }

    private void PrintGauge(string label, long value, long maximum, string? note = null)
    {
        var percent = maximum <= 0 ? 0 : (int)Math.Round(100.0 * value / maximum);
        var style = percent >= 100 ? Green : percent >= 60 ? Amber : Red;
        Console.WriteLine($"  {PadRight(ui.Paint(label, Gray), 20)}{ui.Bar(value, maximum, GaugeCells, style)}  {ui.Paint($"{value}/{maximum}", Bold + White)} {ui.Paint($"{percent}%", style)}"
            + (note is null ? string.Empty : ui.Paint($"  ({note})", Gray)));
    }

    private void PrintEvents(IReadOnlyList<GameEvent> events)
    {
        foreach (var item in events)
        {
            Console.WriteLine($"  {ui.Paint(ui.Glyphs.Rail, Gray)} {ui.Paint(PadRight(EventLabel(item.Type), 20), EventStyle(item.Type))} {item.Message}");
        }
    }

    private string PhaseProgress(string phaseId)
    {
        var phases = rulePack?.Phases;
        var index = phases?.FindIndex(item => item.Id == phaseId) ?? -1;
        if (phases is null || index < 0) return string.Empty;
        var dots = string.Concat(phases.Select((_, position) => position <= index ? ui.Glyphs.Dot : ui.Glyphs.DotEmpty));
        return ui.Paint(dots, Cyan);
    }

    private string ActionName(string actionId, GameEvent? actionEvent)
    {
        var defined = rulePack?.Actions.FirstOrDefault(item => item.Id == actionId)?.Name;
        if (!string.IsNullOrWhiteSpace(defined)) return defined;
        return actionEvent is null ? Humanize(actionId) : ExtractActionName(actionEvent.Message);
    }

    private string PhaseName(string phaseId) => rulePack?.Phases.FirstOrDefault(item => item.Id == phaseId)?.Name ?? Humanize(phaseId);

    private string CardName(string cardId) =>
        rulePack?.Cards.FirstOrDefault(item => item.Id.Equals(cardId, StringComparison.OrdinalIgnoreCase))?.Name ?? Humanize(cardId);

    private string ZoneName(string zoneId) => rulePack?.Zones.FirstOrDefault(item => item.Id == zoneId)?.Name ?? Humanize(zoneId);

    private string FormatChange(string change)
    {
        var split = change.LastIndexOf(' ');
        if (split < 0) return change;
        var delta = change[(split + 1)..];
        var style = delta.StartsWith('+') ? Green : delta.StartsWith('-') ? Red : Gray;
        return ui.Paint(change[..split], Gray) + " " + ui.Paint(delta, Bold + style);
    }

    private string Count(int value, string noun) => ui.Paint(value.ToString(CultureInfo.InvariantCulture), Bold + Cyan) + " " + noun;

    private string PadRight(string text, int width) => ConsoleStyle.PadRight(text, width);

    private static string EventStyle(string type) => type switch
    {
        _ when type.Contains("victory", StringComparison.OrdinalIgnoreCase) || type.Contains("winner", StringComparison.OrdinalIgnoreCase) => Gold,
        _ when type.Contains("ability", StringComparison.OrdinalIgnoreCase) => Violet,
        _ when type.Contains("reject", StringComparison.OrdinalIgnoreCase) || type.Contains("error", StringComparison.OrdinalIgnoreCase) => Red,
        _ when type.Contains("phase", StringComparison.OrdinalIgnoreCase) || type.Contains("turn", StringComparison.OrdinalIgnoreCase) => Cyan,
        _ => Amber,
    };

    private static string EventLabel(string type) => type.Replace('_', ' ');

    private static string ExtractActionName(string message)
    {
        var markerIndex = message.IndexOf(ActionPerformedMarker, StringComparison.Ordinal);
        var name = markerIndex < 0 ? message : message[(markerIndex + ActionPerformedMarker.Length)..];
        return name.TrimEnd('.');
    }

    private static string Humanize(string id)
    {
        var words = id.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[0].Equals(PackIdPrefix, StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        return words.Count == 0 ? id : string.Join(' ', words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static void Pause(int milliseconds)
    {
        if (milliseconds > 0) Thread.Sleep(milliseconds);
    }
}
