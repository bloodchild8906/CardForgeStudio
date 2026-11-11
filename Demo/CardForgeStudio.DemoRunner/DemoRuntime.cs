using TcgULtimateServer.Api.Models;
using TcgULtimateServer.Api.Services;

namespace CardForgeStudio.DemoRunner;

/// <summary>Counts describing the game package that was imported for the demo.</summary>
public sealed record ImportedPackageSummary(string Name, string Version, int Cards, int Actions, int Abilities);

/// <summary>Owns the temporary server state used for one demo run and removes it on disposal.</summary>
public sealed class DemoRuntime : IDisposable
{
    private readonly bool keepRuntimeStore;

    private DemoRuntime(string runtimeRoot, bool keepRuntimeStore, RulesEngine engine, ImportedPackageSummary package)
    {
        RuntimeRoot = runtimeRoot;
        this.keepRuntimeStore = keepRuntimeStore;
        Engine = engine;
        Package = package;
    }

    /// <summary>Directory holding the temporary package store and audit log.</summary>
    public string RuntimeRoot { get; }

    /// <summary>The authoritative rules engine with the demo package activated.</summary>
    public RulesEngine Engine { get; }

    /// <summary>Summary of the imported package.</summary>
    public ImportedPackageSummary Package { get; }

    /// <summary>Whether the temporary files survive disposal.</summary>
    public bool RetainsStore => keepRuntimeStore;

    /// <summary>Creates the temporary store, imports and activates the package, and returns the ready runtime.</summary>
    public static DemoRuntime Start(string packagePath, bool keepRuntimeStore)
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), "CardForge-AstralRift-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeRoot);
        Environment.SetEnvironmentVariable("CARDFORGE_GAME_PACKAGE_STORE", Path.Combine(runtimeRoot, "packages"));
        Environment.SetEnvironmentVariable("TCG_AUDIT_LOG_PATH", Path.Combine(runtimeRoot, "audit.ndjson"));

        var state = new AppState();
        var engine = new RulesEngine(state);
        var packageService = new GamePackageService(state, engine, new AuditLogService());
        using var packageStream = File.OpenRead(packagePath);
        var imported = packageService.Import(packageStream, Path.GetFileName(packagePath), activate: true, "demo-runner");
        var summary = new ImportedPackageSummary(
            imported.Name,
            imported.Version,
            imported.Content.Cards,
            imported.Content.Actions,
            imported.Content.Abilities);

        return new DemoRuntime(runtimeRoot, keepRuntimeStore, engine, summary);
    }

    /// <summary>Deletes the temporary files unless the run was asked to keep them.</summary>
    public void Dispose()
    {
        if (keepRuntimeStore) return;
        try { Directory.Delete(RuntimeRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
