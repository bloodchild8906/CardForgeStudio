using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    /// Converts a designer-authored .cardforge archive into the native Unity
    /// simulation format. Stages player content for new builds and can hot
    /// publish changes to the already-running Unity Dedicated Server.
    /// </summary>
    public static class NativeContentPublisher
    {
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public static string StagePackage(string packageFile)
        {
            var pack = ConvertPackage(packageFile);
            var id = (string)pack["id"];
            if (string.IsNullOrWhiteSpace(id) || !id.All(ch =>
                char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.'))
                throw new InvalidOperationException("Unsafe rule pack identifier.");

            var destination = Path.Combine(Application.dataPath, "StreamingAssets",
                "CardForge", "Rules");
            Directory.CreateDirectory(destination);
            var path = Path.Combine(destination, id + ".json");
            File.WriteAllText(path, pack.ToString(Newtonsoft.Json.Formatting.None),
                new UTF8Encoding(false));
            StageArt(pack);
            AssetDatabase.Refresh();
            return path;
        }

        public static async Task PublishToServerAsync(string packageFile, string baseUrl)
        {
            var pack = ConvertPackage(packageFile);
            using (var http = new HttpClient { BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(25) })
            {
                var auth = await http.PostAsync("/api/auth/login",
                    new StringContent("{\"handle\":\"demo\",\"password\":\"cardforge-demo\"}",
                        Encoding.UTF8, "application/json"));
                if (!auth.IsSuccessStatusCode)
                    throw new InvalidOperationException("Local native demo admin sign-in failed. " +
                        await auth.Content.ReadAsStringAsync());
                var session = JObject.Parse(await auth.Content.ReadAsStringAsync());
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", (string)session["token"]);
                // Rules are activated only when their game has no unfinished matches.
                var payload = new JObject { ["rulePack"] = pack };
                using (var response = await http.PostAsync("/api/admin/rules/import",
                    new StringContent(payload.ToString(), Encoding.UTF8, "application/json")))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Native rules publish rejected: " +
                            await response.Content.ReadAsStringAsync());
                }
                var assets = ArtFiles(pack);
                foreach (var asset in assets)
                {
                    var bytes = File.ReadAllBytes(asset.Value);
                    string hash;
                    using (var sha = SHA256.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    var upload = new JObject
                    {
                        ["base64"] = Convert.ToBase64String(bytes),
                        ["sha256"] = hash,
                        ["contentType"] = "image/png"
                    };
                    using (var response = await http.PostAsync(
                        "/api/admin/assets/" + Uri.EscapeDataString(asset.Key),
                        new StringContent(upload.ToString(Newtonsoft.Json.Formatting.None),
                            Encoding.UTF8, "application/json")))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("Art publish failed (" + asset.Key + "): " +
                                await response.Content.ReadAsStringAsync());
                    }
                }
            }
        }

        public static JObject ConvertPackage(string packageFile)
        {
            using (var archive = ZipFile.OpenRead(packageFile))
            {
                var rules = Read(archive, "rules/rule-pack.json");
                var victory = Read(archive, "rules/victory.json");
                var obj = JObject.Parse(rules);
                if (string.Equals((string)obj["id"], "astral-rift",
                    StringComparison.OrdinalIgnoreCase))
                    obj["maximumPlayers"] = 5;
                obj["startingHand"] = obj["startingRules"]?["startingHand"] ?? 5;
                obj["victoryRules"] = JObject.Parse(victory)["rules"] ?? new JArray();
                if (obj["triggers"] == null) obj["triggers"] = new JArray();
                foreach (var action in obj["actions"] as JArray ?? new JArray())
                {
                    if (action["costs"] is JObject costs)
                        action["costs"] = new JArray(costs.Properties().Select(p =>
                            new JObject { ["resourceId"] = p.Name, ["amount"] = p.Value }));
                    foreach (var effect in action["effects"] as JArray ?? new JArray())
                        NormalizeParameters(effect);
                }
                foreach (var card in obj["cards"] as JArray ?? new JArray())
                {
                    card["assetId"] = (string)obj["id"] + ".card." + (string)card["id"];
                    foreach (var ability in card["abilities"] as JArray ?? new JArray())
                        foreach (var effect in ability["effects"] as JArray ?? new JArray())
                            NormalizeParameters(effect);
                }
                foreach (var rule in obj["victoryRules"] as JArray ?? new JArray())
                {
                    if (rule["operator"] != null && rule["comparison"] == null)
                        rule["comparison"] = rule["operator"];
                }
                return obj;
            }
        }

        private static string Read(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name) ??
                throw new InvalidOperationException("Package missing " + name);
            using (var reader = new StreamReader(entry.Open()))
                return reader.ReadToEnd();
        }

        private static void NormalizeParameters(JToken effect)
        {
            if (effect["parameters"] is JObject values)
                effect["parameters"] = new JArray(values.Properties().Select(p =>
                    new JObject { ["key"] = p.Name, ["value"] = p.Value.ToString() }));
        }

        private static Dictionary<string, string> ArtFiles(JObject pack)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in pack["cards"] as JArray ?? new JArray())
            {
                var id = (string)card["id"];
                var key = (string)card["assetId"];
                var configured = (string)card["properties"]?["art"];
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(configured))
                    continue;
                var relative = configured.Replace('/', Path.DirectorySeparatorChar);
                var source = Path.GetFullPath(Path.Combine(ProjectRoot, relative));
                var assetsRoot = Path.GetFullPath(Path.Combine(ProjectRoot, "Assets")) +
                    Path.DirectorySeparatorChar;
                if (!source.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase) ||
                    !source.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid card artwork path: " + configured);
                if (!File.Exists(source))
                    throw new FileNotFoundException("Card illustration missing", source);
                files[key] = source;
            }
            return files;
        }

        private static void StageArt(JObject pack)
        {
            var assets = Path.Combine(Application.dataPath, "StreamingAssets",
                "CardForge", "Assets");
            Directory.CreateDirectory(assets);
            foreach (var pair in ArtFiles(pack))
                File.Copy(pair.Value, Path.Combine(assets, pair.Key + ".png"), true);
        }
    }
}
