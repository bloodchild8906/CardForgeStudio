using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CardForge.GamePackages;
using UnityEngine;

namespace TcgServer.Client
{
    /// <summary>An opened, in-memory .cardforge archive. Nothing is ever extracted to disk.</summary>
    public sealed class CardForgePackage
    {
        private readonly Dictionary<string, byte[]> entries;

        internal CardForgePackage(GamePackageManifest manifest, Dictionary<string, byte[]> entries, List<ManifestValidationIssue> issues)
        {
            Manifest = manifest;
            this.entries = entries;
            Issues = issues;
        }

        public GamePackageManifest Manifest { get; }
        public IReadOnlyList<ManifestValidationIssue> Issues { get; }
        public bool IsValid => Issues.Count == 0;
        public IEnumerable<string> EntryPaths => entries.Keys;

        public bool TryGetBytes(string path, out byte[] bytes) =>
            entries.TryGetValue(GamePackageManifestValidator.NormalizePath(path), out bytes);

        public bool TryGetText(string path, out string text)
        {
            if (TryGetBytes(path, out var bytes))
            {
                text = Encoding.UTF8.GetString(bytes);
                return true;
            }
            text = null;
            return false;
        }

        public IEnumerable<string> PathsOfKind(string kind) =>
            Manifest.Files.Where(file => string.Equals(file.Kind, kind, StringComparison.OrdinalIgnoreCase)).Select(file => file.Path);
    }

    /// <summary>
    /// Opens a .cardforge archive produced by the CardForge Studio exporter and validates it with the same
    /// contract the authoritative server uses: manifest rules, declared-versus-actual entries, lengths and SHA-256 hashes.
    /// </summary>
    public static class CardForgePackageReader
    {
        public const int MaxEntries = 20000;
        public const long MaxTotalBytes = 512L * 1024 * 1024;

        public static CardForgePackage Load(string path)
        {
            using (var stream = File.OpenRead(path))
                return Load(stream);
        }

        public static CardForgePackage Load(Stream stream)
        {
            var issues = new List<ManifestValidationIssue>();
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                if (archive.Entries.Count > MaxEntries)
                {
                    issues.Add(new ManifestValidationIssue("archive.too-many-entries", $"Archive has more than {MaxEntries} entries."));
                    return new CardForgePackage(null, entries, issues);
                }

                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    total += entry.Length;
                    if (total > MaxTotalBytes)
                    {
                        issues.Add(new ManifestValidationIssue("archive.too-large", "Archive expands beyond the permitted size."));
                        return new CardForgePackage(null, entries, issues);
                    }

                    using (var input = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        input.CopyTo(buffer);
                        entries[GamePackageManifestValidator.NormalizePath(entry.FullName)] = buffer.ToArray();
                    }
                }
            }

            GamePackageManifest manifest = null;
            if (entries.TryGetValue(GamePackageContract.ManifestEntryName, out var manifestBytes))
                manifest = ParseManifest(Encoding.UTF8.GetString(manifestBytes));

            var validation = GamePackageManifestValidator.Validate(manifest, entries.Keys);
            issues.AddRange(validation.Errors);

            if (manifest != null)
                foreach (var file in manifest.Files)
                {
                    var normalized = GamePackageManifestValidator.NormalizePath(file.Path);
                    if (!entries.TryGetValue(normalized, out var bytes)) continue;
                    if (bytes.LongLength != file.Length)
                        issues.Add(new ManifestValidationIssue("file.length-mismatch", "Entry length differs from the manifest.", normalized));
                    if (!string.Equals(Sha256Hex(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        issues.Add(new ManifestValidationIssue("file.hash-mismatch", "Entry SHA-256 differs from the manifest.", normalized));
                }

            return new CardForgePackage(manifest, entries, issues);
        }

        internal static GamePackageManifest ParseManifest(string json)
        {
            ManifestDto dto;
            try { dto = JsonUtility.FromJson<ManifestDto>(json); }
            catch (ArgumentException) { return null; }
            if (dto == null) return null;

            var manifest = new GamePackageManifest
            {
                SchemaVersion = dto.schemaVersion ?? string.Empty,
                PackageId = dto.packageId ?? string.Empty,
                GameId = dto.gameId ?? string.Empty,
                Name = dto.name ?? string.Empty,
                Version = dto.version ?? string.Empty,
                Description = dto.description ?? string.Empty,
                CreatedUtc = dto.createdUtc ?? string.Empty,
                SourceEditor = dto.sourceEditor ?? string.Empty,
                RulePackPath = dto.rulePackPath ?? string.Empty,
                VictoryDefinitionsPath = dto.victoryDefinitionsPath ?? string.Empty,
            };
            if (dto.content != null)
                manifest.Content = new GamePackageContentSummary
                {
                    Cards = dto.content.cards, Zones = dto.content.zones, Frames = dto.content.frames, Assets = dto.content.assets,
                    Actions = dto.content.actions, Abilities = dto.content.abilities, Keywords = dto.content.keywords,
                    TurnFlows = dto.content.turnFlows, VictoryRules = dto.content.victoryRules,
                };
            if (dto.files != null)
                foreach (var file in dto.files)
                    manifest.Files.Add(new GamePackageFile { Path = file.path ?? string.Empty, Kind = file.kind ?? string.Empty, Length = file.length, Sha256 = file.sha256 ?? string.Empty });
            return manifest;
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) builder.Append(b.ToString("X2"));
                return builder.ToString();
            }
        }

        [Serializable] private sealed class ManifestDto
        {
            public string schemaVersion, packageId, gameId, name, version, description, createdUtc, sourceEditor, rulePackPath, victoryDefinitionsPath;
            public ContentDto content;
            public FileDto[] files;
        }

        [Serializable] private sealed class ContentDto
        {
            public int cards, zones, frames, assets, actions, abilities, keywords, turnFlows, victoryRules;
        }

        [Serializable] private sealed class FileDto
        {
            public string path, kind, sha256;
            public long length;
        }
    }
}
