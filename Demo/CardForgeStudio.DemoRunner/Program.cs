using System.Text.Json;
using TcgULtimateServer.Api.Models;
using TcgULtimateServer.Api.Services;

var repository = FindRepository(AppContext.BaseDirectory);
var packagePath = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
    ?? Path.Combine(repository, "GamePackages", "astral-rift-demo-1.0.0.cardforge");
if (!File.Exists(packagePath))
{
    Console.Error.WriteLine("Astral Rift package not found: " + packagePath);
    Console.Error.WriteLine("Generate it in Unity with CardForge > Generate Full Demo Game first.");
    return 2;
}

var keepStore = args.Contains("--keep-store", StringComparer.OrdinalIgnoreCase);
var runtimeRoot = Path.Combine(Path.GetTempPath(), "CardForge-AstralRift-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runtimeRoot);
Environment.SetEnvironmentVariable("CARDFORGE_GAME_PACKAGE_STORE", Path.Combine(runtimeRoot, "packages"));
Environment.SetEnvironmentVariable("TCG_AUDIT_LOG_PATH", Path.Combine(runtimeRoot, "audit.ndjson"));

try
{
    var state = new AppState();
    var engine = new RulesEngine(state);
    var packages = new GamePackageService(state, engine, new AuditLogService());
    using (var package = File.OpenRead(packagePath))
    {
        var imported = packages.Import(package, Path.GetFileName(packagePath), activate: true, "demo-runner");
        Console.WriteLine($"Loaded {imported.Name} v{imported.Version}: {imported.Content.Cards} cards, {imported.Content.Actions} actions, {imported.Content.Abilities} abilities.");
    }

    var game = new GameState { RulePackId = "astral-rift", Players = ["Nova", "Umbra"] };
    engine.Start(game);
    Console.WriteLine("\nA two-player authoritative match begins. The script will exercise every gameplay subsystem.\n");

    RunOpeningTurn(engine, game, "Nova", "Umbra", salvage: true);
    RunOpeningTurn(engine, game, "Umbra", "Nova", salvage: false);
    RunCombatTurn(engine, game, "Nova", "Umbra", rally: true);
    RunCombatTurn(engine, game, "Umbra", "Nova", rally: true, strike: false);
    RunCombatTurn(engine, game, "Nova", "Umbra", rally: true);

    if (!game.Finished) throw new InvalidOperationException("The scripted match did not reach a victory state.");
    Console.WriteLine($"\nWINNER: {game.WinnerPlayerId} via {game.VictoryRuleId} on turn {game.Turn}.");
    Console.WriteLine($"Final resources — Nova: {Resources(game, "Nova")}; Umbra: {Resources(game, "Umbra")}; shared momentum: {game.Resources["__game__"].GetValueOrDefault("momentum")}.");
    Console.WriteLine($"Zones exercised — battlefield {Count(game, "Nova", "battlefield")}, discard {Count(game, "Nova", "discard")}, void {Count(game, "Umbra", "void")}, hand {Count(game, "Nova", "hand")}.");
    Console.WriteLine($"Stored navigation beacon: {(game.Data.TryGetValue("navigationBeacon", out var beacon) ? beacon.ToString() : "missing")}.");
    Console.WriteLine($"Authoritative event log: {game.Events.Count} events ({game.Events.Count(item => item.Type == "ability_resolved")} ability resolutions).\n");
    foreach (var item in game.Events.TakeLast(12)) Console.WriteLine($"  [{item.Type}] {item.Message}");
    return 0;
}
finally
{
    if (keepStore) Console.WriteLine("Demo runtime retained at " + runtimeRoot);
    else try { Directory.Delete(runtimeRoot, true); } catch { }
}

static void RunOpeningTurn(RulesEngine engine, GameState game, string actor, string opponent, bool salvage)
{
    Console.WriteLine($"TURN {game.Turn} — {actor}");
    Execute(engine, game, actor, "ar-channel");
    Execute(engine, game, actor, "ar-next-phase");
    Execute(engine, game, actor, "ar-draw");
    Execute(engine, game, actor, "ar-next-phase");
    var card = game.Zones[actor]["hand"].First().InstanceId;
    Execute(engine, game, actor, "ar-deploy", source: card);
    Execute(engine, game, actor, "ar-retire", source: card);
    Execute(engine, game, actor, salvage ? "ar-salvage" : "ar-banish", source: card);
    Execute(engine, game, actor, "ar-stabilize");
    Execute(engine, game, actor, "ar-record", parameters: new Dictionary<string, JsonElement> { ["mode"] = JsonSerializer.SerializeToElement("beacon") });
    Execute(engine, game, actor, "ar-signal");
    Execute(engine, game, actor, "ar-next-phase");
    Execute(engine, game, actor, "ar-strike", targetPlayer: opponent);
    Execute(engine, game, actor, "ar-rally");
    Execute(engine, game, actor, "ar-next-phase");
    Execute(engine, game, actor, "ar-end-turn");
    Console.WriteLine("  " + Resources(game, actor) + "\n");
}

static void RunCombatTurn(RulesEngine engine, GameState game, string actor, string opponent, bool rally, bool strike = true)
{
    if (game.Finished) return;
    Console.WriteLine($"TURN {game.Turn} — {actor}");
    Execute(engine, game, actor, "ar-channel");
    Execute(engine, game, actor, "ar-next-phase");
    Execute(engine, game, actor, "ar-draw");
    Execute(engine, game, actor, "ar-next-phase");
    Execute(engine, game, actor, "ar-signal");
    Execute(engine, game, actor, "ar-next-phase");
    if (strike) Execute(engine, game, actor, "ar-strike", targetPlayer: opponent);
    if (!game.Finished && rally) Execute(engine, game, actor, "ar-rally");
    if (!game.Finished) Execute(engine, game, actor, "ar-next-phase");
    if (!game.Finished) Execute(engine, game, actor, "ar-end-turn");
    Console.WriteLine("  " + Resources(game, actor) + "\n");
}

static void Execute(RulesEngine engine, GameState game, string actor, string action, string? source = null, string? targetPlayer = null, Dictionary<string, JsonElement>? parameters = null)
{
    engine.Execute(game, actor, new ExecuteGameActionRequest(action, source, targetPlayer, Parameters: parameters));
    Console.WriteLine($"  {game.CurrentPhaseId,-13} {action}");
}

static string Resources(GameState game, string player) => string.Join(", ", game.Resources[player].OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value));
static int Count(GameState game, string player, string zone) => game.Zones[player][zone].Count;

static string FindRepository(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory != null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "Server")) &&
            Directory.Exists(Path.Combine(directory.FullName, "Shared"))) return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not find the CardForgeStudio repository.");
}
