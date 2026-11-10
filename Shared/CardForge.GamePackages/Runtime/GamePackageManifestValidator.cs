#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace CardForge.GamePackages
{

public static class GamePackageManifestValidator
{
    public static ManifestValidationResult Validate(GamePackageManifest? manifest, IEnumerable<string>? archiveEntries = null)
    {
        var result = new ManifestValidationResult();
        if (manifest is null)
        {
            result.Errors.Add(new ManifestValidationIssue("manifest.missing", "manifest.json is missing or invalid."));
            return result;
        }

        if (!string.Equals(manifest.SchemaVersion, GamePackageContract.CurrentSchemaVersion, StringComparison.Ordinal))
            result.Errors.Add(new ManifestValidationIssue("manifest.schema-version", $"Unsupported package schema '{manifest.SchemaVersion}'. Expected {GamePackageContract.CurrentSchemaVersion}."));
        ValidateIdentifier(manifest.PackageId, "packageId", result);
        ValidateIdentifier(manifest.GameId, "gameId", result);
        if (string.IsNullOrWhiteSpace(manifest.Name))
            result.Errors.Add(new ManifestValidationIssue("manifest.name", "Package name is required."));
        if (!IsSemanticVersion(manifest.Version))
            result.Errors.Add(new ManifestValidationIssue("manifest.version", "Version must use semantic version form such as 1.0.0."));
        if (!DateTimeOffset.TryParse(manifest.CreatedUtc, out _))
            result.Errors.Add(new ManifestValidationIssue("manifest.created", "createdUtc must be an ISO-8601 timestamp."));

        var files = manifest.Files ?? new List<GamePackageFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var path = NormalizePath(file.Path);
            if (!IsSafeRelativePath(path))
                result.Errors.Add(new ManifestValidationIssue("file.path", "Package entries must be normalized relative paths without traversal segments.", file.Path));
            else if (!paths.Add(path))
                result.Errors.Add(new ManifestValidationIssue("file.duplicate", "The manifest declares the same path more than once.", path));
            if (file.Length < 0)
                result.Errors.Add(new ManifestValidationIssue("file.length", "File length cannot be negative.", path));
            if (!IsSha256(file.Sha256))
                result.Errors.Add(new ManifestValidationIssue("file.sha256", "File SHA-256 must contain 64 hexadecimal characters.", path));
            if (string.IsNullOrWhiteSpace(file.Kind))
                result.Errors.Add(new ManifestValidationIssue("file.kind", "Every file must declare a content kind.", path));
        }

        foreach (var kind in GamePackageContract.RequiredContentKinds)
            if (!files.Any(file => string.Equals(file.Kind, kind, StringComparison.OrdinalIgnoreCase)))
                result.Errors.Add(new ManifestValidationIssue("content.missing", $"Required content kind '{kind}' is missing."));

        ValidateDeclaredPath(manifest.RulePackPath, "rulePackPath", paths, result);
        ValidateDeclaredPath(manifest.VictoryDefinitionsPath, "victoryDefinitionsPath", paths, result);

        if (archiveEntries is not null)
        {
            var entries = new HashSet<string>(archiveEntries.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
                if (!entries.Contains(path))
                    result.Errors.Add(new ManifestValidationIssue("archive.missing-entry", "A manifest file is absent from the archive.", path));
            foreach (var entry in entries)
                if (!string.Equals(entry, GamePackageContract.ManifestEntryName, StringComparison.OrdinalIgnoreCase) && !paths.Contains(entry))
                    result.Errors.Add(new ManifestValidationIssue("archive.undeclared-entry", "Archive entry is not declared by the manifest.", entry));
        }
        return result;
    }

    public static string NormalizePath(string? path) => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    public static bool IsSafeRelativePath(string? path)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.EndsWith("/", StringComparison.Ordinal) || normalized.Contains(":", StringComparison.Ordinal)) return false;
        return normalized.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }

    private static void ValidateDeclaredPath(string value, string field, HashSet<string> paths, ManifestValidationResult result)
    {
        var normalized = NormalizePath(value);
        if (!IsSafeRelativePath(normalized) || !paths.Contains(normalized))
            result.Errors.Add(new ManifestValidationIssue("manifest.declared-path", $"{field} must point to a declared file.", normalized));
    }

    private static void ValidateIdentifier(string value, string field, ManifestValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || !char.IsLetterOrDigit(value[0]) || value.Any(character => !(char.IsLetterOrDigit(character) || character == '-' || character == '_')))
            result.Errors.Add(new ManifestValidationIssue("manifest.identifier", $"{field} must use 1-80 letters, numbers, hyphens, or underscores."));
    }

    private static bool IsSemanticVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Any(character => !(char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '+'))) return false;
        var core = value.Split('-')[0].Split('+')[0].Split('.');
        return core.Length == 3 && core.All(part => int.TryParse(part, out var number) && number >= 0);
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
}
