using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CardForge.GamePackages;
using NUnit.Framework;
using UnityEngine;

namespace TcgServer.Client.Tests
{
    public sealed class CardForgePackageReaderTests
    {
        private static readonly string[] Kinds = GamePackageContract.RequiredContentKinds;

        [Test]
        public void ValidPackageLoadsWithoutIssues()
        {
            var package = CardForgePackageReader.Load(new MemoryStream(BuildArchive()));
            Assert.IsTrue(package.IsValid, Describe(package));
            Assert.AreEqual("test-game", package.Manifest.GameId);
            Assert.AreEqual(Kinds.Length, package.Manifest.Files.Count);
            Assert.IsTrue(package.TryGetText("rules/rule-pack.json", out var rules));
            Assert.IsNotEmpty(rules);
        }

        [Test]
        public void TamperedEntryIsRejectedByHash()
        {
            var package = CardForgePackageReader.Load(new MemoryStream(BuildArchive(tamperKind: "cards")));
            Assert.IsFalse(package.IsValid);
            Assert.IsTrue(package.Issues.Any(issue => issue.Code == "file.hash-mismatch"), Describe(package));
        }

        [Test]
        public void UndeclaredEntryIsRejected()
        {
            var package = CardForgePackageReader.Load(new MemoryStream(BuildArchive(extraEntry: "cards/smuggled.json")));
            Assert.IsTrue(package.Issues.Any(issue => issue.Code == "archive.undeclared-entry"), Describe(package));
        }

        [Test]
        public void ArchiveWithoutManifestIsRejected()
        {
            var package = CardForgePackageReader.Load(new MemoryStream(BuildArchive(omitManifest: true)));
            Assert.IsFalse(package.IsValid);
            Assert.IsTrue(package.Issues.Any(issue => issue.Code == "manifest.missing"), Describe(package));
        }

        [Test]
        public void ExportedCorePackageValidatesWhenPresent()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "GamePackages", "core-1.0.0.cardforge"));
            if (!File.Exists(path)) Assert.Ignore("No exported core package at " + path);
            var package = CardForgePackageReader.Load(path);
            Assert.IsTrue(package.IsValid, Describe(package));
            Assert.Greater(package.PathsOfKind("cards").Count(), 0);
        }

        private static string Describe(CardForgePackage package) =>
            string.Join("; ", package.Issues.Select(issue => issue.Code + " " + issue.Path));

        private static byte[] BuildArchive(string tamperKind = null, string extraEntry = null, bool omitManifest = false)
        {
            var files = new List<(string path, string kind, byte[] bytes)>();
            foreach (var kind in Kinds)
            {
                var path = kind == "rules" ? GamePackageContract.DefaultRulePackPath
                    : kind == "victory" ? GamePackageContract.DefaultVictoryPath
                    : kind + "/index.json";
                files.Add((path, kind, Encoding.UTF8.GetBytes("{\"kind\":\"" + kind + "\"}")));
            }

            var manifest = new StringBuilder();
            manifest.Append("{\"schemaVersion\":\"1.0\",\"packageId\":\"test-game\",\"gameId\":\"test-game\",\"name\":\"Test\",\"version\":\"1.0.0\",");
            manifest.Append("\"description\":\"\",\"createdUtc\":\"2026-01-01T00:00:00+00:00\",\"sourceEditor\":\"tests\",");
            manifest.Append("\"rulePackPath\":\"" + GamePackageContract.DefaultRulePackPath + "\",\"victoryDefinitionsPath\":\"" + GamePackageContract.DefaultVictoryPath + "\",");
            manifest.Append("\"content\":{\"cards\":0,\"zones\":0,\"frames\":0,\"assets\":0,\"actions\":0,\"abilities\":0,\"keywords\":0,\"turnFlows\":0,\"victoryRules\":0},\"files\":[");
            manifest.Append(string.Join(",", files.Select(f =>
                "{\"path\":\"" + f.path + "\",\"kind\":\"" + f.kind + "\",\"length\":" + f.bytes.Length + ",\"sha256\":\"" + CardForgePackageReader.Sha256Hex(f.bytes) + "\"}")));
            manifest.Append("]}");

            using (var output = new MemoryStream())
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    if (!omitManifest) Write(archive, GamePackageContract.ManifestEntryName, Encoding.UTF8.GetBytes(manifest.ToString()));
                    foreach (var file in files)
                    {
                        var bytes = file.kind == tamperKind ? Encoding.UTF8.GetBytes("{\"kind\":\"tampered\"}!") : file.bytes;
                        Write(archive, file.path, bytes);
                    }
                    if (extraEntry != null) Write(archive, extraEntry, new byte[] { 1 });
                }
                return output.ToArray();
            }
        }

        private static void Write(ZipArchive archive, string path, byte[] bytes)
        {
            var entry = archive.CreateEntry(path);
            using (var stream = entry.Open()) stream.Write(bytes, 0, bytes.Length);
        }
    }
}
