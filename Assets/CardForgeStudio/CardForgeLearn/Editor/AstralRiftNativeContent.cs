using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CardForge.Learn
{
    /// <summary>
    /// Deploys the authoritative Astral Rift 3 rulepack and the same card-ID-matched
    /// illustrations used by the editor and runtime. Never replaces rules used by
    /// an unfinished server match and never silently falls back to legacy 2.x.
    /// </summary>
    public static class AstralRiftNativeContent
    {
        public const string PackId = "astral-rift";
        public const string Version = "3.0.0";
        private static string Root => Directory.GetParent(Application.dataPath).FullName;
        public static string RulesFile => Path.Combine(Application.dataPath,
            "StreamingAssets", "CardForge", "Rules", PackId + ".json");

        private static string IllustrationFolder => Path.Combine(Application.dataPath,
            "CardForgeStudio", "CardForgeClient", "Resources", "AstralRift", "Illustrations");

        /// <summary>
        /// Ensures the native 240-card rules exist and that all public card art
        /// points to exactly one matching illustration. Safe to call repeatedly.
        /// </summary>
        public static string Prepare()
        {
            bool generated = !IsCurrent();
            if (generated)
            {
                var script = Path.Combine(Root, "Demo", "Build-AstralRift3.ps1");
                if (!File.Exists(script))
                    throw new FileNotFoundException("Astral Rift 3 content generator missing.", script);
                using (var process = Process.Start(new ProcessStartInfo("powershell.exe")
                {
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"",
                    WorkingDirectory = Root,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    if (process == null || !process.WaitForExit(90000) || process.ExitCode != 0)
                        throw new InvalidOperationException(
                            "Failed to prepare Astral Rift 3 content. Run Demo/Build-AstralRift3.ps1 and inspect its output.");
                }
            }

            var pack = JObject.Parse(File.ReadAllText(RulesFile, Encoding.UTF8));
            Validate(pack);

            var shipping = Path.Combine(Application.dataPath, "StreamingAssets", "CardForge", "Assets");
            var editable = Path.Combine(Application.dataPath,
                "CardForgeStudio", "CardForgeData", "demo", "astral-rift", "art");
            Directory.CreateDirectory(shipping);
            Directory.CreateDirectory(editable);
            bool changed = false;
            foreach (var pair in ArtFiles(pack))
            {
                var id = Path.GetFileNameWithoutExtension(pair.Value);
                var sourceBytes = File.ReadAllBytes(pair.Value);
                if (sourceBytes.Length < 24 || sourceBytes[0] != 137 || sourceBytes[1] != 80 ||
                    sourceBytes[2] != 78 || sourceBytes[3] != 71)
                    throw new InvalidOperationException("Invalid illustration PNG: " + id);

                var editorFile = Path.Combine(editable, id + ".png");
                var serverFile = Path.Combine(shipping, pair.Key + ".png");
                if (CopyIfDifferent(sourceBytes, editorFile)) changed = true;
                if (CopyIfDifferent(sourceBytes, serverFile)) changed = true;
            }
            if (generated || changed) AssetDatabase.Refresh();
            return RulesFile;
        }

        private static bool CopyIfDifferent(byte[] bytes, string destination)
        {
            if (File.Exists(destination))
            {
                var existing = File.ReadAllBytes(destination);
                if (bytes.SequenceEqual(existing)) return false;
            }
            File.WriteAllBytes(destination, bytes);
            return true;
        }

        private static bool IsCurrent()
        {
            if (!File.Exists(RulesFile)) return false;
            try
            {
                var data = JObject.Parse(File.ReadAllText(RulesFile, Encoding.UTF8));
                return string.Equals((string)data["version"], Version, StringComparison.Ordinal) &&
                       (data["cards"] as JArray)?.Count == 240 &&
                       (data["starterDecks"] as JArray)?.Count == 3;
            }
            catch (Newtonsoft.Json.JsonException) { return false; }
        }

        private static void Validate(JObject pack)
        {
            if ((string)pack["id"] != PackId || (string)pack["version"] != Version)
                throw new InvalidOperationException("Astral Rift 3.0 rules are not staged.");
            var cards = pack["cards"] as JArray;
            var starters = pack["starterDecks"] as JArray;
            if (cards?.Count != 240 || starters?.Count != 3)
                throw new InvalidOperationException("Expected 240 cards and exactly three starter decks.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
            {
                var id = (string)card["id"];
                if (!SafeCardId(id) || !names.Add(id) ||
                    (string)card["assetId"] != PackId + ".card." + id)
                    throw new InvalidOperationException("A card has a duplicate/invalid ID or artwork ID: " + id);
            }
            foreach (var starter in starters)
            {
                var ids = ((JArray)starter["cardIds"]).Values<string>().ToArray();
                if (ids.Length != 42 || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 42 ||
                    ids.Any(id => !names.Contains(id)))
                    throw new InvalidOperationException("Starter deck must have 42 distinct valid card IDs.");
            }
            if (ArtFiles(pack).Count != 240)
                throw new InvalidOperationException("The 240-card artwork catalog is incomplete.");
        }

        private static bool SafeCardId(string id) =>
            !string.IsNullOrWhiteSpace(id) && id.All(c =>
                (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-');

        private static Dictionary<string, string> ArtFiles(JObject pack)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in pack["cards"] as JArray ?? new JArray())
            {
                var id = (string)card["id"];
                var key = (string)card["assetId"];
                if (!SafeCardId(id) || key != PackId + ".card." + id)
                    throw new InvalidOperationException("Invalid artwork key for card " + id);
                var path = Path.Combine(IllustrationFolder, id + ".png");
                if (!File.Exists(path))
                    throw new FileNotFoundException("Card " + id + " has no matching illustration.", path);
                if (files.ContainsKey(key))
                    throw new InvalidOperationException("Duplicate artwork key: " + key);
                files.Add(key, path);
            }
            return files;
        }

        /// <summary>
        /// Idempotent publishing: read the live rulepack first and skip the rules
        /// import if its semantic fields match. Upload only changed illustration
        /// hashes. A 409 from the server remains a hard, actionable conflict.
        /// </summary>
        public static async Task PublishAsync(string baseUrl)
        {
            var pack = JObject.Parse(File.ReadAllText(RulesFile, Encoding.UTF8));
            Validate(pack);
            var art = ArtFiles(pack);
            using (var http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(100)
            })
            {
                var login = await http.PostAsync("api/auth/login",
                    new StringContent("{\"handle\":\"demo\",\"password\":\"cardforge-demo\"}",
                        Encoding.UTF8, "application/json"));
                if (!login.IsSuccessStatusCode)
                    throw new InvalidOperationException("Cannot authenticate to the local native server: " +
                        await login.Content.ReadAsStringAsync());
                var loginBody = JObject.Parse(await login.Content.ReadAsStringAsync());
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer", (string)loginBody["token"]);

                bool sameRules = false;
                using (var response = await http.GetAsync("api/rules/" + PackId))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var live = JObject.Parse(await response.Content.ReadAsStringAsync());
                        sameRules = SameRules(pack, live);
                    }
                }
                if (!sameRules)
                {
                    using (var response = await http.PostAsync("api/admin/rules/import",
                        new StringContent(new JObject { ["rulePack"] = pack }.ToString(
                            Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")))
                    {
                        if (response.StatusCode == HttpStatusCode.Conflict)
                            throw new InvalidOperationException(
                                "Astral Rift 3 rules are staged, but the server has an unfinished match using older/different rules. " +
                                "Finish those matches before activating this version. No live match or save was erased.");
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("Astral Rift 3 rules import failed: " +
                                await response.Content.ReadAsStringAsync());
                    }
                }

                var remoteHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var manifest = await http.GetAsync("api/assets/manifest"))
                {
                    if (manifest.IsSuccessStatusCode)
                    {
                        var entries = JArray.Parse(await manifest.Content.ReadAsStringAsync());
                        foreach (var entry in entries)
                        {
                            var id = (string)entry["id"];
                            var hash = (string)entry["sha256"];
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(hash))
                                remoteHashes[id] = hash;
                        }
                    }
                }
                int uploaded = 0;
                foreach (var pair in art)
                {
                    var bytes = File.ReadAllBytes(pair.Value);
                    string hash;
                    using (var sha = SHA256.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(bytes))
                            .Replace("-", "").ToLowerInvariant();
                    if (remoteHashes.TryGetValue(pair.Key, out var remote) &&
                        string.Equals(remote, hash, StringComparison.OrdinalIgnoreCase)) continue;
                    var upload = new JObject
                    {
                        ["base64"] = Convert.ToBase64String(bytes),
                        ["sha256"] = hash,
                        ["contentType"] = "image/png"
                    };
                    using (var response = await http.PostAsync(
                        "api/admin/assets/" + Uri.EscapeDataString(pair.Key),
                        new StringContent(upload.ToString(Newtonsoft.Json.Formatting.None),
                            Encoding.UTF8, "application/json")))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("Illustration publish failed for " +
                                pair.Key + ": " + await response.Content.ReadAsStringAsync());
                    }
                    uploaded++;
                }
                UnityEngine.Debug.Log("Astral Rift 3 native content ready: 240 matched illustrations; " +
                    uploaded + " changed files uploaded; rules " + (sameRules ? "unchanged" : "imported") + ".");
            }
        }

        // A SimRulePack serialized back from the server can include default fields
        // absent from its source JSON and omit designer-only properties. Compare
        // the common semantic fields recursively rather than raw JSON bytes.
        private static bool SameRules(JToken desired, JToken live)
        {
            if (desired == null || live == null) return desired == null && live == null;
            if (desired is JObject expectedObject && live is JObject actualObject)
            {
                foreach (var field in expectedObject.Properties())
                {
                    if (!actualObject.TryGetValue(field.Name, out var value)) continue;
                    if (!SameRules(field.Value, value)) return false;
                }
                return true;
            }
            if (desired is JArray expectedArray && live is JArray actualArray)
                return expectedArray.Count == actualArray.Count &&
                    !expectedArray.Where((value, index) => !SameRules(value, actualArray[index])).Any();
            return JToken.DeepEquals(desired, live);
        }
    }
}
