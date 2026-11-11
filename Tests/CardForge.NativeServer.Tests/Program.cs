using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using CardForge.Simulation;
using Newtonsoft.Json.Linq;
using CardForge.DedicatedServer;

var passed = 0;
void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAILED: " + message);
    passed++;
}
JToken Parse(NativeReply r) => JToken.Parse(Encoding.UTF8.GetString(r.bytes));
var temp = Path.Combine(Path.GetTempPath(), "cardforge-native-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var shipping = Path.GetFullPath(@"D:\CardForgeStudio\Assets\StreamingAssets");
    var persistence = new MemoryPersistence();
    var native = new NativeServerApi(shipping, temp, true, persistence);
    NativeReply Call(string method, string path, string json = "{}", string token = null) =>
        native.Handle(method, path, json, token, "http://localhost:5168");
    Check(Call("GET", "/api/health").status == 200, "health works");
    Check(Call("GET", "/api/rules/astral-rift").status == 200, "rules available");
    Check((int)Parse(Call("GET", "/api/rules/astral-rift"))["maximumPlayers"] == 5,
        "Astral Rift supports 1v4");
    var assets = Parse(Call("GET", "/api/assets/manifest"));
    Check(assets.Count() >= 240, "art manifest contains at least 240 images");
    var artId = (string)assets[0]["id"];
    var binary = Call("GET", "/api/assets/" + Uri.EscapeDataString(artId));
    Check(binary.status == 200 && binary.bytes.Length > 100, "art is downloaded on demand");
    var auth = Call("POST", "/api/auth/login",
        "{\"handle\":\"demo\",\"password\":\"cardforge-demo\"}");
    Check(auth.status == 200, "demo account login");
    var token = (string)Parse(auth)["token"];
    var id = (string)Parse(auth)["user"]["id"];
    Check(!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(id), "bearer issued");
    Check(Call("GET", "/api/decks").status == 401, "anonymous decks blocked");
    Check(Parse(Call("GET", "/api/decks", token: token)).Count() == 3,
        "all three starter decks created for demo account");
    for (var n = 1; n <= 4; n++)
    {
        var cmd = "{\"rulePackId\":\"astral-rift\",\"playerIds\":[\"" + id +
            "\"],\"addAi\":true,\"teamSize\":" + n + "}";
        var created = Call("POST", "/api/games", cmd, token);
        Check(created.status == 200, "1v" + n + " created");
        var game = Parse(created);
        Check(game["players"].Count() == n + 1, "seat count for 1v" + n);
        var gameId = (string)game["id"];
        var view = Call("GET", "/api/games/" + gameId + "/client-view", token: token);
        Check(view.status == 200, "match visible");
        var v = Parse(view);
        var opponent = (string)v["players"][1];
        var cards = v["zones"].First(z => (string)z["playerId"] == opponent &&
            (string)z["zoneId"] == "hand")["cards"];
        Check(cards.All(c => string.IsNullOrWhiteSpace((string)c["cardId"]) &&
            string.IsNullOrWhiteSpace((string)c["instanceId"])), "opponent cards concealed");
        Check(Call("GET", "/api/games/" + gameId + "/client-view", token: "bad").status == 401,
            "unauthorized viewing blocked");
    }
    var inventory = Parse(Call("GET", "/api/inventory/" + id, token: token));
    Check(inventory["cards"].Count() > 0, "inventory seeded");
    var card = (string)inventory["cards"][0]["cardId"];
    var invalid = Call("POST", "/api/decks",
        "{\"name\":\"Illegal one-card deck\",\"rulePackId\":\"astral-rift\",\"cardIds\":[\"" + card + "\"]}", token);
    Check(invalid.status == 400, "native server rejects decks without 40 main plus two special cards");
    var starter = Parse(Call("GET", "/api/decks", token: token)).First();
    var validRequest = new JObject
    {
        ["name"] = "My deck",
        ["rulePackId"] = "astral-rift",
        ["cardIds"] = starter["cardIds"].DeepClone()
    };
    var deck = Call("POST", "/api/decks", validRequest.ToString(), token);
    Check(deck.status == 200, "42-card deck saved");
    var deckId = (string)Parse(deck)["id"];
    Check(Call("DELETE", "/api/decks/" + deckId, token: token).status == 200, "deck removed");
    var balance = (long)Parse(Call("GET", "/api/store/wallet", token: token))["balances"][0]["amount"];
    var purchased = Call("POST", "/api/store/purchase",
        "{\"productId\":\"astral-starter-pack\"}", token);
    Check(purchased.status == 200 && Parse(purchased)["cards"].Count() == 5, "store grants cards");
    Check((long)Parse(purchased)["balance"] == balance - 25, "wallet debited");
    var register = Call("POST", "/api/auth/register",
        "{\"handle\":\"friendtest\",\"password\":\"strong-password-123\"}");
    Check(register.status == 200, "account registration");
    var other = (string)Parse(register)["user"]["id"];
    Check(Call("POST", "/api/friends",
        "{\"friendUserId\":\"" + other + "\"}", token).status == 200, "friend requests");
    Check(Call("POST", "/api/messages",
        "{\"recipientId\":\"" + other + "\",\"body\":\"Hello\"}", token).status == 200,
        "messages sent");
    var friendToken = (string)Parse(register)["token"];
    Check(Call("GET", "/api/inventory/" + id, token: friendToken).status == 403,
        "other player inventory private");
    // Native content publication supports rules and artwork without an ASP.NET service.
    var importedRules = (JObject)Parse(Call("GET", "/api/rules/astral-rift")).DeepClone();
    importedRules["id"] = "astral-rift-alt";
    importedRules["name"] = "Native Published Alternate";
    var import = Call("POST", "/api/admin/rules/import",
        new JObject { ["rulePack"] = importedRules }.ToString(), token);
    Check(import.status == 200, "native rules published");
    Check(Call("GET", "/api/rules/astral-rift-alt").status == 200, "published rules live");
    var locked = Call("POST", "/api/admin/rules/import",
        new JObject { ["rulePack"] = Parse(Call("GET", "/api/rules/astral-rift")) }.ToString(), token);
    Check(locked.status == 409, "active match protects ruleset from replacement");
    var pngBytes = binary.bytes;
    string assetHash;
    using (var sha = System.Security.Cryptography.SHA256.Create())
        assetHash = BitConverter.ToString(sha.ComputeHash(pngBytes)).Replace("-", "").ToLowerInvariant();
    var publishedAssetId = "astral-rift.card.native-test-art";
    var upload = new JObject
    {
        ["base64"] = Convert.ToBase64String(pngBytes),
        ["sha256"] = assetHash,
        ["contentType"] = "image/png"
    };
    Check(Call("POST", "/api/admin/assets/" + publishedAssetId, upload.ToString(), token).status == 200,
        "native asset hot-published");
    Check(Call("GET", "/api/assets/" + publishedAssetId).bytes.SequenceEqual(pngBytes),
        "published asset downloadable byte-for-byte");
    Check(Call("POST", "/api/admin/assets/" + publishedAssetId,
        upload.ToString(), "unauthorized").status == 401, "anonymous asset publish blocked");
    var restart = new NativeServerApi(shipping, temp, true, persistence);
    var reauth = restart.Handle("POST", "/api/auth/login",
        "{\"handle\":\"demo\",\"password\":\"cardforge-demo\"}", null, "http://localhost:5168");
    Check(reauth.status == 200, "login survives native server restart");
    var t = (string)Parse(reauth)["token"];
    var savedWallet = restart.Handle("GET", "/api/store/wallet", null, t, "http://localhost:5168");
    Check((long)Parse(savedWallet)["balances"][0]["amount"] == balance - 25,
        "store purchase persists after restart");
    Console.WriteLine("Native-only server passed " + passed + " API and match assertions.");
}
finally { try { Directory.Delete(temp, true); } catch { } }

sealed class MemoryPersistence : INativePersistence
{
    private NativeState accounts = new NativeState();
    private List<SimMatch> matches = new List<SimMatch>();
    private static T Clone<T>(T data) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<T>(
            Newtonsoft.Json.JsonConvert.SerializeObject(data));
    public NativeState LoadAccounts() => Clone(accounts);
    public void SaveAccounts(NativeState state) => accounts = Clone(state);
    public List<SimMatch> LoadMatches() => Clone(matches);
    public void SaveMatches(IEnumerable<SimMatch> values) => matches = Clone(values.ToList());
    public bool CheckHealth() => true;
    public void Dispose() { }
}