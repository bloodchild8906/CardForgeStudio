#nullable enable
using System;
using System.Collections.Generic;

namespace CardForge.GamePackages
{

public static class GamePackageContract
{
    public const string CurrentSchemaVersion = "1.0";
    public const string ManifestEntryName = "manifest.json";
    public const string DefaultRulePackPath = "rules/rule-pack.json";
    public const string DefaultVictoryPath = "rules/victory.json";

    public static readonly string[] RequiredContentKinds =
    {
        "cards", "zones", "frames", "assets", "actions", "abilities", "keywords", "turn-flow", "rules", "victory"
    };
}

public sealed class GamePackageManifest
{
    public string SchemaVersion { get; set; } = GamePackageContract.CurrentSchemaVersion;
    public string PackageId { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatedUtc { get; set; } = string.Empty;
    public string SourceEditor { get; set; } = string.Empty;
    public string RulePackPath { get; set; } = GamePackageContract.DefaultRulePackPath;
    public string VictoryDefinitionsPath { get; set; } = GamePackageContract.DefaultVictoryPath;
    public GamePackageContentSummary Content { get; set; } = new();
    public List<GamePackageFile> Files { get; set; } = new();
}

public sealed class GamePackageFile
{
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class GamePackageContentSummary
{
    public int Cards { get; set; }
    public int Zones { get; set; }
    public int Frames { get; set; }
    public int Assets { get; set; }
    public int Actions { get; set; }
    public int Abilities { get; set; }
    public int Keywords { get; set; }
    public int TurnFlows { get; set; }
    public int VictoryRules { get; set; }
    public int Decks { get; set; }
}

public sealed class VictoryDefinitionSet
{
    public int Version { get; set; } = 1;
    public List<VictoryDefinition> Rules { get; set; } = new();
}

public sealed class VictoryDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "last-player-standing";
    public string Outcome { get; set; } = "win";
    public string Scope { get; set; } = "player";
    public string ResourceId { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string Operator { get; set; } = "greater-than-or-equal";
    public long Threshold { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class ManifestValidationIssue
{
    public ManifestValidationIssue(string code, string message, string? path = null)
    {
        Code = code;
        Message = message;
        Path = path;
    }

    public string Code { get; }
    public string Message { get; }
    public string? Path { get; }
}

public sealed class ManifestValidationResult
{
    public List<ManifestValidationIssue> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;
}
}
