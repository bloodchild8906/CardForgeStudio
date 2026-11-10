using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CardForge.GamePackages;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class GamePackageService
{
    public const long MaximumArchiveBytes = 100L * 1024 * 1024;
    private const long MaximumExpandedBytes = 250L * 1024 * 1024;
    private const int MaximumEntries = 5_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> VictoryKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "resource-threshold", "empty-zone", "last-player-standing", "turn-limit"
    };
    private readonly AppState state;
    private readonly RulesEngine rules;
    private readonly AuditLogService audit;
    private readonly string storageRoot;
    private string ActiveVersionsPath => Path.Combine(storageRoot, "active.json");

    public GamePackageService(AppState state, RulesEngine rules, AuditLogService audit)
    {
        this.state = state;
        this.rules = rules;
        this.audit = audit;
        storageRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("CARDFORGE_GAME_PACKAGE_STORE") ??
            Path.Combine(AppContext.BaseDirectory, "data", "game-packages"));
        Restore();
    }

    public IReadOnlyList<GamePackageVersionDescriptor> Packages() => state.GamePackages.Values
        .Select(Describe)
        .OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
        .ThenByDescending(package => package.ImportedAt)
        .ToList();

    public GamePackageValidationResponse Validate(Stream content)
    {
        try
        {
            var bytes = ReadArchive(content);
            return Inspect(bytes).Response;
        }
        catch (InvalidOperationException exception)
        {
            return new(false, null, [new("error", "archive.invalid", exception.Message)]);
        }
    }

    public GamePackageVersionDescriptor Import(Stream content, string fileName, bool activate, string actorId)
    {
        var bytes = ReadArchive(content);
        var inspected = Inspect(bytes);
        if (!inspected.Response.Valid || inspected.Response.Manifest is null || inspected.RulePack is null || inspected.Victory is null)
            throw new InvalidOperationException(string.Join(" ", inspected.Response.Diagnostics.Select(diagnostic => diagnostic.Message)));

        var manifest = inspected.Response.Manifest;
        var key = VersionKey(manifest.PackageId, manifest.Version);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (state.GamePackages.TryGetValue(key, out var existing))
        {
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.Sha256), Convert.FromHexString(hash)))
                throw new InvalidOperationException($"Package {manifest.PackageId} version {manifest.Version} is immutable and already exists with different content.");
            if (activate) return Activate(manifest.PackageId, manifest.Version, actorId);
            return Describe(existing);
        }

        var record = new GamePackageVersionRecord(
            manifest,
            string.IsNullOrWhiteSpace(fileName) ? manifest.PackageId + "-" + manifest.Version + ".cardforge" : Path.GetFileName(fileName),
            bytes,
            hash,
            DateTimeOffset.UtcNow,
            inspected.RulePack,
            inspected.Victory);
        if (!state.GamePackages.TryAdd(key, record)) throw new InvalidOperationException("The package version was imported concurrently.");
        try { Persist(record); }
        catch (Exception exception)
        {
            state.GamePackages.TryRemove(key, out _);
            throw new InvalidOperationException("The validated package could not be stored durably: " + exception.Message, exception);
        }
        audit.Write("Information", "game-packages", $"Imported {manifest.PackageId} version {manifest.Version} for game {manifest.GameId}.", actorId);
        return activate ? Activate(manifest.PackageId, manifest.Version, actorId) : Describe(record);
    }

    public GamePackageVersionDescriptor Activate(string packageId, string version, string actorId)
    {
        if (!state.GamePackages.TryGetValue(VersionKey(packageId, version), out var record))
            throw new KeyNotFoundException("Game package version not found.");
        if (state.Games.Values.Any(game => !game.Finished && string.Equals(game.RulePackId, record.Manifest.GameId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This game definition is used by an unfinished match. Finish or terminate those matches before changing the active package.");
        var conflicting = state.ActiveGamePackageVersions
            .Where(active => !active.Key.Equals(packageId, StringComparison.OrdinalIgnoreCase))
            .Select(active => state.GamePackages.GetValueOrDefault(VersionKey(active.Key, active.Value)))
            .FirstOrDefault(active => active is not null && active.Manifest.GameId.Equals(record.Manifest.GameId, StringComparison.OrdinalIgnoreCase));
        if (conflicting is not null)
            throw new InvalidOperationException($"Game ID {record.Manifest.GameId} is already provided by active package {conflicting.Manifest.PackageId}.");

        var activeVersions = new Dictionary<string, string>(state.ActiveGamePackageVersions, StringComparer.OrdinalIgnoreCase)
        {
            [record.Manifest.PackageId] = record.Manifest.Version
        };
        PersistActiveVersions(activeVersions);
        rules.Save(record.RulePack);
        state.ActiveGamePackageVersions[record.Manifest.PackageId] = record.Manifest.Version;
        audit.Write("Information", "game-packages", $"Activated {record.Manifest.PackageId} version {record.Manifest.Version} as rule pack {record.Manifest.GameId}.", actorId);
        return Describe(record);
    }

    public GamePackageVersionRecord Get(string packageId, string version) => state.GamePackages.TryGetValue(VersionKey(packageId, version), out var record)
        ? record
        : throw new KeyNotFoundException("Game package version not found.");

    private Inspection Inspect(byte[] bytes)
    {
        var diagnostics = new List<GamePackageDiagnostic>();
        GamePackageManifest? manifest = null;
        RulePack? normalizedRulePack = null;
        VictoryDefinitionSet? victory = null;
        RulePackValidationResult? ruleValidation = null;

        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            var entries = archive.Entries.Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal)).ToList();
            if (entries.Count == 0) throw new InvalidOperationException("The game package is empty.");
            if (entries.Count > MaximumEntries) throw new InvalidOperationException($"The package contains more than {MaximumEntries} files.");
            var normalized = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;
            foreach (var entry in entries)
            {
                var path = GamePackageManifestValidator.NormalizePath(entry.FullName);
                if (!GamePackageManifestValidator.IsSafeRelativePath(path)) throw new InvalidOperationException($"Unsafe archive path '{entry.FullName}'.");
                if (!normalized.TryAdd(path, entry)) throw new InvalidOperationException($"Duplicate archive path '{path}'.");
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumExpandedBytes) throw new InvalidOperationException("The expanded package exceeds the 250 MB limit.");
                if (entry.CompressedLength > 0 && entry.Length > entry.CompressedLength * 1_000L) throw new InvalidOperationException($"Entry '{path}' has an unsafe compression ratio.");
            }

            if (!normalized.TryGetValue(GamePackageContract.ManifestEntryName, out var manifestEntry))
                throw new InvalidOperationException("manifest.json is required at the package root.");
            if (manifestEntry.Length > 1024 * 1024) throw new InvalidOperationException("manifest.json exceeds the 1 MB limit.");
            manifest = Deserialize<GamePackageManifest>(manifestEntry, "manifest.json");
            var manifestValidation = GamePackageManifestValidator.Validate(manifest, normalized.Keys);
            diagnostics.AddRange(manifestValidation.Errors.Select(issue => new GamePackageDiagnostic("error", issue.Code, issue.Message, issue.Path)));

            if (manifestValidation.IsValid)
            {
                foreach (var declared in manifest.Files)
                {
                    var path = GamePackageManifestValidator.NormalizePath(declared.Path);
                    var entry = normalized[path];
                    if (entry.Length != declared.Length)
                        diagnostics.Add(new("error", "file.length-mismatch", $"Expected {declared.Length} bytes but found {entry.Length}.", path));
                    else
                    {
                        using var input = entry.Open();
                        var hash = Convert.ToHexString(SHA256.HashData(input));
                        if (!hash.Equals(declared.Sha256, StringComparison.OrdinalIgnoreCase))
                            diagnostics.Add(new("error", "file.hash-mismatch", "SHA-256 does not match the manifest.", path));
                    }
                }

                if (!normalized.TryGetValue(GamePackageManifestValidator.NormalizePath(manifest.RulePackPath), out var ruleEntry))
                    diagnostics.Add(new("error", "rules.missing", "The compiled rule pack is missing.", manifest.RulePackPath));
                else
                {
                    var rulePack = Deserialize<RulePack>(ruleEntry, manifest.RulePackPath);
                    ruleValidation = rules.Validate(rulePack);
                    if (!ruleValidation.Valid || ruleValidation.Normalized is null)
                        diagnostics.AddRange(ruleValidation.Errors.Select(error => new GamePackageDiagnostic("error", "rules.invalid", error, manifest.RulePackPath)));
                    else if (!ruleValidation.Normalized.Id.Equals(manifest.GameId, StringComparison.OrdinalIgnoreCase))
                        diagnostics.Add(new("error", "rules.game-id", "The compiled rule-pack ID must match manifest gameId.", manifest.RulePackPath));
                    else if (!ruleValidation.Normalized.Version.Equals(manifest.Version, StringComparison.OrdinalIgnoreCase))
                        diagnostics.Add(new("error", "rules.version", "The compiled rule-pack version must match the package version.", manifest.RulePackPath));
                    else
                        normalizedRulePack = ruleValidation.Normalized;
                }

                if (!normalized.TryGetValue(GamePackageManifestValidator.NormalizePath(manifest.VictoryDefinitionsPath), out var victoryEntry))
                    diagnostics.Add(new("error", "victory.missing", "Victory definitions are missing.", manifest.VictoryDefinitionsPath));
                else
                {
                    victory = Deserialize<VictoryDefinitionSet>(victoryEntry, manifest.VictoryDefinitionsPath);
                    ValidateVictory(victory, normalizedRulePack, diagnostics, manifest.VictoryDefinitionsPath);
                }
            }

            if (normalizedRulePack is not null && victory is not null && diagnostics.All(diagnostic => diagnostic.Severity != "error"))
            {
                var metadata = new Dictionary<string, JsonElement>(normalizedRulePack.Metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["cardForgePackage"] = JsonSerializer.SerializeToElement(new { manifest.PackageId, manifest.Version }, JsonOptions),
                    ["victoryRules"] = JsonSerializer.SerializeToElement(victory.Rules, JsonOptions)
                };
                normalizedRulePack = normalizedRulePack with { Metadata = metadata };
            }
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(new("error", "archive.zip", "The file is not a readable ZIP archive: " + exception.Message));
        }
        catch (JsonException exception)
        {
            diagnostics.Add(new("error", "json.invalid", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            diagnostics.Add(new("error", "archive.invalid", exception.Message));
        }
        catch (OverflowException exception)
        {
            diagnostics.Add(new("error", "archive.size-overflow", exception.Message));
        }

        var response = new GamePackageValidationResponse(diagnostics.All(diagnostic => diagnostic.Severity != "error"), manifest, diagnostics, ruleValidation);
        return new Inspection(response, normalizedRulePack, victory);
    }

    private static void ValidateVictory(VictoryDefinitionSet set, RulePack? pack, List<GamePackageDiagnostic> diagnostics, string path)
    {
        if (set.Version != 1) diagnostics.Add(new("error", "victory.version", "Only victory definition version 1 is supported.", path));
        if (set.Rules is null || set.Rules.Count == 0) diagnostics.Add(new("error", "victory.empty", "At least one victory rule is required.", path));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in set.Rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id)) diagnostics.Add(new("error", "victory.id", "Victory rule IDs must be non-empty and unique.", path));
            if (!VictoryKinds.Contains(rule.Kind)) diagnostics.Add(new("error", "victory.kind", $"Unsupported victory kind '{rule.Kind}'.", path));
            if (rule.Outcome is not ("win" or "eliminate")) diagnostics.Add(new("error", "victory.outcome", $"Unsupported victory outcome '{rule.Outcome}'.", path));
            if (pack is not null && rule.Kind is "resource-threshold" or "last-player-standing" && !pack.Resources.Any(resource => resource.Id.Equals(rule.ResourceId, StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add(new("error", "victory.resource", $"Victory rule '{rule.Id}' references unknown resource '{rule.ResourceId}'.", path));
            if (pack is not null && rule.Kind == "empty-zone" && !pack.Zones.Any(zone => zone.Id.Equals(rule.ZoneId, StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add(new("error", "victory.zone", $"Victory rule '{rule.Id}' references unknown zone '{rule.ZoneId}'.", path));
            if (rule.Kind == "turn-limit" && rule.Threshold < 1) diagnostics.Add(new("error", "victory.threshold", "Turn-limit thresholds must be positive.", path));
        }
    }

    private GamePackageVersionDescriptor Describe(GamePackageVersionRecord record) => new(
        record.Manifest.PackageId,
        record.Manifest.GameId,
        record.Manifest.Name,
        record.Manifest.Version,
        record.Manifest.Description,
        record.FileName,
        record.Content.LongLength,
        record.Sha256,
        record.ImportedAt,
        state.ActiveGamePackageVersions.TryGetValue(record.Manifest.PackageId, out var active) && active.Equals(record.Manifest.Version, StringComparison.OrdinalIgnoreCase),
        record.Manifest.Content);

    private void Restore()
    {
        if (!Directory.Exists(storageRoot)) return;
        foreach (var file in Directory.EnumerateFiles(storageRoot, "*.cardforge", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length <= 0 || info.Length > MaximumArchiveBytes) throw new InvalidDataException("Stored archive size is invalid.");
                var bytes = File.ReadAllBytes(file);
                var inspected = Inspect(bytes);
                if (!inspected.Response.Valid || inspected.Response.Manifest is null || inspected.RulePack is null || inspected.Victory is null)
                    throw new InvalidDataException(string.Join(" ", inspected.Response.Diagnostics.Select(diagnostic => diagnostic.Message)));
                var manifest = inspected.Response.Manifest;
                state.GamePackages.TryAdd(VersionKey(manifest.PackageId, manifest.Version), new GamePackageVersionRecord(
                    manifest,
                    Path.GetFileName(file),
                    bytes,
                    Convert.ToHexString(SHA256.HashData(bytes)),
                    info.LastWriteTimeUtc,
                    inspected.RulePack,
                    inspected.Victory));
            }
            catch (Exception exception)
            {
                audit.Write("Warning", "game-packages", $"Skipped invalid stored package '{file}': {exception.Message}");
            }
        }

        if (!File.Exists(ActiveVersionsPath)) return;
        try
        {
            var active = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ActiveVersionsPath), JsonOptions) ?? [];
            foreach (var item in active)
            {
                if (!state.GamePackages.TryGetValue(VersionKey(item.Key, item.Value), out var record))
                {
                    audit.Write("Warning", "game-packages", $"Active package {item.Key} version {item.Value} is not present in durable storage.");
                    continue;
                }
                rules.Save(record.RulePack);
                state.ActiveGamePackageVersions[item.Key] = item.Value;
            }
        }
        catch (Exception exception)
        {
            audit.Write("Warning", "game-packages", "Could not restore active package versions: " + exception.Message);
        }
    }

    private void Persist(GamePackageVersionRecord record)
    {
        var directory = PackageVersionDirectory(record.Manifest.PackageId, record.Manifest.Version);
        Directory.CreateDirectory(directory);
        WriteAtomic(Path.Combine(directory, "package.cardforge"), record.Content);
    }

    private void PersistActiveVersions(IReadOnlyDictionary<string, string> active)
    {
        Directory.CreateDirectory(storageRoot);
        WriteAtomic(ActiveVersionsPath, JsonSerializer.SerializeToUtf8Bytes(active, JsonOptions));
    }

    private string PackageVersionDirectory(string packageId, string version)
    {
        var path = Path.GetFullPath(Path.Combine(storageRoot, packageId, version));
        if (!path.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Package storage path escaped the configured root.");
        return path;
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static T Deserialize<T>(ZipArchiveEntry entry, string path)
    {
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? throw new InvalidOperationException($"'{path}' is empty or invalid.");
    }

    private static byte[] ReadArchive(Stream content)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaximumArchiveBytes) throw new InvalidOperationException("The game package exceeds the 100 MB limit.");
            output.Write(buffer, 0, read);
        }
        if (output.Length == 0) throw new InvalidOperationException("The game package is empty.");
        return output.ToArray();
    }

    private static string VersionKey(string packageId, string version) => packageId.Trim().ToLowerInvariant() + "@" + version.Trim().ToLowerInvariant();
    private sealed record Inspection(GamePackageValidationResponse Response, RulePack? RulePack, VictoryDefinitionSet? Victory);
}
