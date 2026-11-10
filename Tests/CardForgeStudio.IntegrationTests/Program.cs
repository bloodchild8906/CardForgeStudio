using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardForge.GamePackages;
using TcgULtimateServer.Api.Models;
using TcgULtimateServer.Api.Services;

var suite = new IntegrationSuite();
if (args.Length == 2 && args[0] == "--package")
    return suite.ValidateExternalPackage(args[1]);
return suite.Run();

internal sealed class IntegrationSuite
{
    private int passed;
    private int failed;
    private readonly string temporaryRoot = Path.Combine(Path.GetTempPath(), "CardForgeStudioTests-" + Guid.NewGuid().ToString("N"));

    public int Run()
    {
        Directory.CreateDirectory(temporaryRoot);
        Environment.SetEnvironmentVariable("TCG_AUDIT_LOG_PATH", Path.Combine(temporaryRoot, "audit.ndjson"));
        try
        {
            Test("valid package imports, activates, and drives victory", ImportActivateAndExecute);
            Test("package versions are immutable", ImmutableVersions);
            Test("tampered file hashes are rejected", TamperedHash);
            Test("archive traversal paths are rejected", TraversalPath);
            Test("activation is blocked while a match uses the ruleset", ActivationBlockedByLiveGame);
            Test("package versions and activation survive restart", DurableRestore);
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, recursive: true); } catch { }
        }

        Console.WriteLine($"CardForgeStudio integration tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    public int ValidateExternalPackage(string path)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine("Package not found: " + path); return 2; }
        Environment.SetEnvironmentVariable("TCG_AUDIT_LOG_PATH", Path.Combine(temporaryRoot, "external-audit.ndjson"));
        Directory.CreateDirectory(temporaryRoot);
        var (_, _, packages) = Services();
        using (var validationStream = File.OpenRead(path))
        {
            var validation = packages.Validate(validationStream);
            foreach (var diagnostic in validation.Diagnostics) Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}");
            if (!validation.Valid) return 1;
        }
        using var importStream = File.OpenRead(path);
        var imported = packages.Import(importStream, Path.GetFileName(path), activate: true, "external-test");
        Console.WriteLine($"Validated, imported, and activated {imported.PackageId} v{imported.Version} for game {imported.GameId} ({imported.Content.Cards} cards, {imported.Content.Actions} actions)." );
        return 0;
    }

    private void ImportActivateAndExecute()
    {
        var (state, engine, packages) = Services();
        using var stream = new MemoryStream(PackageBuilder.Build("1.0.0"));
        var imported = packages.Import(stream, "sample-1.0.0.cardforge", activate: true, "test-admin");
        Equal(true, imported.Active, "package should be active");
        Equal(true, state.RulePacks.ContainsKey("sample-game"), "activation should publish the rule pack");

        var game = new GameState { RulePackId = "sample-game", Players = ["p1", "p2"] };
        engine.Start(game);
        engine.Execute(game, "p1", new ExecuteGameActionRequest("damage", TargetPlayerId: "p2"));
        Equal(true, game.Finished, "victory rules should finish the match");
        Equal("p1", game.WinnerPlayerId, "last remaining player should win");
        Equal("last-player-standing", game.VictoryRuleId, "the package victory rule should be recorded");
    }

    private void ImmutableVersions()
    {
        var (_, _, packages) = Services();
        packages.Import(new MemoryStream(PackageBuilder.Build("1.0.0")), "first.cardforge", false, "test-admin");
        Throws<InvalidOperationException>(() => packages.Import(new MemoryStream(PackageBuilder.Build("1.0.0", description: "different bytes")), "second.cardforge", false, "test-admin"), "same identity with different bytes must be rejected");
    }

    private void TamperedHash()
    {
        var (_, _, packages) = Services();
        var report = packages.Validate(new MemoryStream(PackageBuilder.Build("1.0.0", tamperManifestHash: true)));
        Equal(false, report.Valid, "hash mismatch should invalidate package");
        Equal(true, report.Diagnostics.Any(diagnostic => diagnostic.Code == "file.hash-mismatch"), "hash diagnostic should be present");
    }

    private void TraversalPath()
    {
        var (_, _, packages) = Services();
        var report = packages.Validate(new MemoryStream(PackageBuilder.Build("1.0.0", includeTraversal: true)));
        Equal(false, report.Valid, "traversal entry should invalidate package");
        Equal(true, report.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("Unsafe archive path", StringComparison.Ordinal)), "traversal diagnostic should be present");
    }

    private void ActivationBlockedByLiveGame()
    {
        var (state, _, packages) = Services();
        packages.Import(new MemoryStream(PackageBuilder.Build("1.0.0")), "v1.cardforge", true, "test-admin");
        packages.Import(new MemoryStream(PackageBuilder.Build("1.1.0")), "v1.1.cardforge", false, "test-admin");
        var game = new GameState { RulePackId = "sample-game", Players = ["p1", "p2"] };
        state.Games[game.Id] = game;
        Throws<InvalidOperationException>(() => packages.Activate("sample-game-package", "1.1.0", "test-admin"), "active matches should block definition replacement");
    }

    private void DurableRestore()
    {
        var storePath = Path.Combine(temporaryRoot, "durable-restore");
        var (_, _, first) = Services(storePath);
        first.Import(new MemoryStream(PackageBuilder.Build("2.0.0")), "v2.cardforge", true, "test-admin");
        var (restoredState, _, second) = Services(storePath);
        var restored = second.Packages().Single(package => package.PackageId == "sample-game-package" && package.Version == "2.0.0");
        Equal(true, restored.Active, "restored package should remain active");
        Equal(true, restoredState.RulePacks.ContainsKey("sample-game"), "restored activation should republish the rule pack");
    }

    private (AppState State, RulesEngine Engine, GamePackageService Packages) Services(string? storePath = null)
    {
        Environment.SetEnvironmentVariable("CARDFORGE_GAME_PACKAGE_STORE", storePath ?? Path.Combine(temporaryRoot, "package-store-" + Guid.NewGuid().ToString("N")));
        var state = new AppState();
        var audit = new AuditLogService();
        var engine = new RulesEngine(state);
        return (state, engine, new GamePackageService(state, engine, audit));
    }

    private void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS  " + name); }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL  " + name + Environment.NewLine + exception); }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}. Expected '{expected}', found '{actual}'.");
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }
}

internal static class PackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static byte[] Build(string version, string description = "Integration test package", bool tamperManifestHash = false, bool includeTraversal = false)
    {
        var rulePack = new RulePack(
            "sample-game",
            "Sample Game",
            version,
            description,
            [new CardDefinition("sample-card", "Sample Card", "unit", 1, 1, 1, "A test card.")],
            new Dictionary<string, int> { ["startingHand"] = 0, ["startingLife"] = 20 })
        {
            SchemaVersion = "2",
            GameSystem = "cardforge-test",
            MinimumPlayers = 2,
            MaximumPlayers = 2,
            Zones =
            [
                new("deck", "Deck", "player", "hidden", true),
                new("hand", "Hand", "player", "owner", true),
                new("battlefield", "Battlefield", "player", "public"),
                new("discard", "Discard", "player", "public", true)
            ],
            Resources = [new("life", "Life", "player", 20, 0)],
            Phases = [new("main", "Main", ["damage"])],
            Actions = [new("damage", "Deal lethal damage", RequiresActivePlayer: true, Effects: [new("modify_resource", Target: "target_player", ResourceId: "life", Amount: -20)])]
        };
        var victory = new VictoryDefinitionSet
        {
            Rules =
            [
                new VictoryDefinition
                {
                    Id = "last-player-standing",
                    Name = "Last player standing",
                    Kind = "last-player-standing",
                    Outcome = "win",
                    Scope = "player",
                    ResourceId = "life",
                    Operator = "less-than-or-equal",
                    Threshold = 0
                }
            ]
        };

        var content = new Dictionary<string, (string Kind, byte[] Bytes)>(StringComparer.OrdinalIgnoreCase)
        {
            ["cards/index.json"] = ("cards", Utf8("[\"sample-card\"]")),
            ["zones/game-zones.json"] = ("zones", Utf8("{\"version\":1}")),
            ["frames/index.json"] = ("frames", Utf8("[]")),
            ["assets/asset-map.json"] = ("assets", Utf8("{\"version\":1,\"assets\":[]}")),
            ["actions/index.json"] = ("actions", Utf8("[\"damage\"]")),
            ["abilities/index.json"] = ("abilities", Utf8("[]")),
            ["keywords/index.json"] = ("keywords", Utf8("[]")),
            ["turn-flows/index.json"] = ("turn-flow", Utf8("[\"main\"]")),
            [GamePackageContract.DefaultRulePackPath] = ("rules", JsonSerializer.SerializeToUtf8Bytes(rulePack, JsonOptions)),
            [GamePackageContract.DefaultVictoryPath] = ("victory", JsonSerializer.SerializeToUtf8Bytes(victory, JsonOptions))
        };
        var manifest = new GamePackageManifest
        {
            PackageId = "sample-game-package",
            GameId = "sample-game",
            Name = "Sample Game",
            Version = version,
            Description = description,
            CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
            SourceEditor = "integration-tests",
            Content = new GamePackageContentSummary { Cards = 1, Zones = 4, Actions = 1, TurnFlows = 1, VictoryRules = 1 },
            Files = content.Select(item => new GamePackageFile
            {
                Path = item.Key,
                Kind = item.Value.Kind,
                Length = item.Value.Bytes.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(item.Value.Bytes))
            }).ToList()
        };
        if (tamperManifestHash) manifest.Files.First().Sha256 = new string('0', 64);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, GamePackageContract.ManifestEntryName, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
            foreach (var item in content) Write(archive, item.Key, item.Value.Bytes);
            if (includeTraversal) Write(archive, "../outside.txt", Utf8("not allowed"));
        }
        return output.ToArray();
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    private static void Write(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
