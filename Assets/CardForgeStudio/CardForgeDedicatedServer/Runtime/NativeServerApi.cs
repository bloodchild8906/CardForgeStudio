using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CardForge.Simulation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CardForge.DedicatedServer
{
    public sealed class NativeReply
    {
        public int status;
        public string contentType;
        public byte[] bytes;
        public NativeReply(int statusCode, byte[] data, string type)
        { status = statusCode; bytes = data; contentType = type; }
        public static NativeReply Json(object payload, int status = 200) =>
            new NativeReply(status, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)), "application/json");
        public static NativeReply Error(string message, int status = 400) =>
            Json(new { error = message }, status);
    }

    /// <summary>
    /// The single CardForge player/server API. Unity owns rules, games, AI,
    /// matchmaking, users, decks, inventory, social data and content hosting.
    /// No ASP.NET Core process or gateway is used.
    /// </summary>
    public sealed class NativeServerApi
    {
        private readonly SimulationEngine engine = new SimulationEngine();
        private readonly Dictionary<string, SimMatch> games = new Dictionary<string, SimMatch>();
        private readonly Dictionary<string, int> difficulty = new Dictionary<string, int>();
        private readonly Dictionary<string, NativeQueueEntry> queues = new Dictionary<string, NativeQueueEntry>();
        private readonly Dictionary<string, string> matched = new Dictionary<string, string>();
        private readonly string persistentContent, shippingContent;
        private readonly INativePersistence persistence;
        private readonly Dictionary<string, SimRulePack> rules = new Dictionary<string, SimRulePack>();
        private readonly NativeAccounts accounts;
        private DateTimeOffset lastMatchCheck = DateTimeOffset.MinValue;
        public bool Ready { get; private set; }
        public int ActiveSessions => accounts.ActiveSessions;
        public int ActiveMatches => games.Count;
        public NativeAccounts Accounts => accounts;
        public string PersistenceMode => persistence is MongoNativePersistence ? "MongoDB" :
            persistence is JsonNativePersistence ? "Local JSON (demo)" : persistence.GetType().Name;

        public NativeServerApi(string streamingAssetsPath, string persistentDataPath,
            bool demoMode, INativePersistence externalPersistence = null)
        {
            shippingContent = Path.Combine(streamingAssetsPath, "CardForge");
            persistentContent = Path.Combine(persistentDataPath, "CardForge", "Content");
            var stateDirectory = Path.Combine(persistentDataPath, "CardForge", "State");
            var configuredMongo = Environment.GetEnvironmentVariable("CARDFORGE_MONGODB_URI");
            if (externalPersistence != null)
                persistence = externalPersistence;
            else if (demoMode && string.IsNullOrWhiteSpace(configuredMongo))
                persistence = new JsonNativePersistence(stateDirectory);
            else
                persistence = new MongoNativePersistence(
                    configuredMongo ?? "mongodb://127.0.0.1:27017/?directConnection=true",
                    Environment.GetEnvironmentVariable("CARDFORGE_MONGODB_DATABASE") ?? "cardforge",
                    stateDirectory);
            Directory.CreateDirectory(persistentContent);
            Directory.CreateDirectory(Path.Combine(persistentContent, "Rules"));
            Directory.CreateDirectory(Path.Combine(persistentContent, "Assets"));
            LoadRules();

            if (!rules.TryGetValue("astral-rift", out var astral))
                throw new InvalidOperationException("The native Astral Rift ruleset was not packaged.");
            accounts = new NativeAccounts(persistence, demoMode, astral);
            RestoreMatches();
            Ready = true;
        }

        public void Tick()
        {
            if ((DateTimeOffset.UtcNow - lastMatchCheck).TotalSeconds < 2) return;
            lastMatchCheck = DateTimeOffset.UtcNow;
            foreach (var opponents in new[] { 1, 2, 3, 4 })
                MatchQueue(opponents);
        }

        private void LoadRules()
        {
            foreach (var folder in new[] { Path.Combine(shippingContent, "Rules"),
                                           Path.Combine(persistentContent, "Rules") })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var path in Directory.GetFiles(folder, "*.json"))
                {
                    var data = JObject.Parse(File.ReadAllText(path));
                    NormalizeRules(data);
                    var pack = data.ToObject<SimRulePack>();
                    engine.Register(pack);
                    rules[pack.id] = pack;
                }
            }
        }

        private static void NormalizeRules(JObject data)
        {
            foreach (var action in data["actions"] as JArray ?? new JArray())
            {
                var costs = action["costs"];
                if (costs is JObject obj)
                {
                    var items = new JArray();
                    foreach (var property in obj.Properties())
                        items.Add(new JObject { ["resourceId"] = property.Name,
                                                ["amount"] = property.Value });
                    action["costs"] = items;
                }
                foreach (var condition in action["conditions"] as JArray ?? new JArray())
                    if (condition["op"] == null && condition["operator"] != null)
                        condition["op"] = condition["operator"];
                foreach (var effect in action["effects"] as JArray ?? new JArray())
                    NormalizeParameters(effect);
            }
            foreach (var card in data["cards"] as JArray ?? new JArray())
                foreach (var ability in card["abilities"] as JArray ?? new JArray())
                    foreach (var effect in ability["effects"] as JArray ?? new JArray())
                        NormalizeParameters(effect);
            foreach (var rule in data["victoryRules"] as JArray ?? new JArray())
                if (rule["comparison"] == null && rule["operator"] != null)
                    rule["comparison"] = rule["operator"];
            if (data["startingHand"] == null)
                data["startingHand"] = data["startingRules"]?["startingHand"] ?? 5;
            if (data["triggers"] == null) data["triggers"] = new JArray();
        }

        private static void NormalizeParameters(JToken effect)
        {
            if (effect["parameters"] is JObject obj)
            {
                var list = new JArray();
                foreach (var property in obj.Properties())
                    list.Add(new JObject { ["key"] = property.Name,
                        ["value"] = property.Value.Type == JTokenType.String ?
                            property.Value : property.Value.ToString() });
                effect["parameters"] = list;
            }
        }

        public NativeReply Handle(string method, string rawPath, string json, string token,
            string origin)
        {
            try
            {
                var path = Uri.UnescapeDataString((rawPath ?? "/").Split('?')[0]).TrimEnd('/');
                if (path == "") path = "/";
                var parts = path.Split('/').Where(p => p.Length > 0).ToArray();
                var request = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);

                if (method == "GET" && path == "/api/health") return NativeReply.Json(new
                {
                    service = "CardForge Native Unity Dedicated Server",
                    status = Ready ? "healthy" : "starting",
                    simulationMode = "Unity Dedicated Server",
                    simulationReady = Ready, version = "1.0.0",
                    serverTime = DateTimeOffset.UtcNow.ToString("o"),
                    activeMatches = games.Count, connectedSessions = accounts.ActiveSessions,
                    persistence = PersistenceMode, persistenceHealthy = persistence.CheckHealth()
                }, Ready ? 200 : 503);

                if (method == "GET" && path == "/api/presence/count")
                    return NativeReply.Json(new { onlinePlayers = accounts.ActiveSessions });

                if (method == "GET" && path == "/api/rules")
                    return NativeReply.Json(rules.Values.ToArray());
                if (method == "GET" && parts.Length == 3 &&
                    parts[0] == "api" && parts[1] == "rules")
                {
                    if (!rules.TryGetValue(parts[2], out var rule))
                        return NativeReply.Error("Unknown rule pack.", 404);
                    return NativeReply.Json(rule);
                }

                if (method == "POST" && (path == "/api/auth/login" || path == "/api/auth/register"))
                {
                    var handle = (string)request["handle"];
                    var password = (string)request["password"];
                    var user = path.EndsWith("/register", StringComparison.Ordinal) ?
                        accounts.Register(handle, password) : accounts.Authenticate(handle, password);
                    if (path.EndsWith("/register", StringComparison.Ordinal))
                        accounts.SeedCards(user, rules["astral-rift"]);
                    return NativeReply.Json(new
                    {
                        token = accounts.IssueSession(user), user = NativeAccounts.Public(user)
                    });
                }

                if (method == "GET" && path == "/api/rankings")
                    return NativeReply.Json(accounts.State.users.OrderByDescending(u => u.rating)
                        .Select((u, i) => new { rank = i + 1, user = NativeAccounts.Public(u) }).ToArray());
                if (method == "GET" && path == "/api/store/products")
                    return NativeReply.Json(accounts.State.products.Where(p => p.active).ToArray());
                if (method == "GET" && path == "/api/events")
                    return NativeReply.Json(accounts.State.events.Where(e => e.active &&
                        DateTimeOffset.TryParse(e.endsAt, out var end) && end > DateTimeOffset.UtcNow).ToArray());
                if (method == "GET" && path == "/api/assets/manifest")
                    return NativeReply.Json(AssetManifest(origin));
                if (method == "GET" && parts.Length == 3 && parts[0] == "api" && parts[1] == "assets")
                    return ReadAsset(parts[2]);
                if (method == "GET" && path == "/api/scene-index")
                    return ReadAsset("cardforge.scene-index");

                var self = accounts.Resolve(token);
                if (method == "DELETE" && path == "/api/auth/session")
                {
                    accounts.Revoke(token); return NativeReply.Json(new { success = true });
                }

                // Admin publishing is handled entirely inside the Unity process.
                if (method == "POST" && path == "/api/admin/rules/import")
                {
                    if (self.role != "admin") return NativeReply.Error("Admin privileges required.", 403);
                    var incoming = request["rulePack"] as JObject;
                    if (incoming == null) return NativeReply.Error("rulePack must be an object.");
                    NormalizeRules(incoming);
                    var pack = incoming.ToObject<SimRulePack>();
                    if (pack == null || !SafeId(pack.id))
                        return NativeReply.Error("Invalid ruleset ID.");
                    if (pack.minimumPlayers < 2 || pack.maximumPlayers > 5)
                        return NativeReply.Error("Only 1v1 through 1v4 are supported.");
                    if (games.Values.Any(game => game.rulePackId == pack.id && !game.finished))
                        return NativeReply.Error("Cannot replace rules while a matching game is active.", 409);
                    // Validate and activate before persisting the authoritative version.
                    engine.Register(pack);
                    var folder = Path.Combine(persistentContent, "Rules");
                    Directory.CreateDirectory(folder);
                    var dest = Path.Combine(folder, pack.id + ".json");
                    WriteAtomic(dest, incoming.ToString(Formatting.None));
                    rules[pack.id] = pack;
                    return NativeReply.Json(new { success = true, id = pack.id, version = pack.version });
                }
                if (method == "POST" && parts.Length == 4 && parts[0] == "api" &&
                    parts[1] == "admin" && parts[2] == "assets")
                {
                    if (self.role != "admin") return NativeReply.Error("Admin privileges required.", 403);
                    var id = parts[3];
                    if (!SafeId(id)) return NativeReply.Error("Invalid asset identifier.");
                    var content = (string)request["base64"];
                    var expected = (string)request["sha256"];
                    var media = (string)request["contentType"] ?? "application/octet-stream";
                    if (string.IsNullOrWhiteSpace(content) || content.Length > 6 * 1024 * 1024)
                        return NativeReply.Error("Asset body exceeds size limits.", 413);
                    var bytes = Convert.FromBase64String(content);
                    if (bytes.Length > 4 * 1024 * 1024) return NativeReply.Error("Asset too large.", 413);
                    if (!string.Equals(Sha256(bytes), expected, StringComparison.OrdinalIgnoreCase))
                        return NativeReply.Error("Asset SHA-256 does not match.");
                    if (media != "image/png" && media != "application/json" &&
                        media != "application/octet-stream")
                        return NativeReply.Error("Unsupported media type.");
                    if (media == "image/png" && (bytes.Length < 8 ||
                        bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71))
                        return NativeReply.Error("Invalid PNG header.");
                    var ext = media == "image/png" ? ".png" :
                        media == "application/json" ? ".json" : ".bin";
                    var destination = Path.Combine(persistentContent, "Assets", id + ext);
                    WriteAtomic(destination, bytes);
                    return NativeReply.Json(new { success = true, id, sha256 = expected, length = bytes.Length });
                }
                if (method == "GET" && path == "/api/store/wallet")
                    return NativeReply.Json(new { balances = new[] { new { currency = "gold", amount = self.gold } } });

                if (method == "POST" && path == "/api/store/purchase")
                    return NativeReply.Json(accounts.Purchase(self, (string)request["productId"],
                        rules["astral-rift"]));

                if (method == "GET" && parts.Length == 3 && parts[0] == "api" &&
                    parts[1] == "inventory")
                {
                    if (parts[2] != self.id && self.role != "admin")
                        return NativeReply.Error("Not your inventory.", 403);
                    var person = accounts.State.users.FirstOrDefault(u => u.id == parts[2]);
                    return person == null ? NativeReply.Error("User not found.", 404) :
                        NativeReply.Json(new { cards = person.cards, achievements = person.achievements });
                }

                if (path == "/api/decks")
                {
                    if (method == "GET")
                        return NativeReply.Json(accounts.State.decks.Where(d => d.ownerId == self.id).ToArray());
                    if (method == "POST")
                    {
                        var rulePackId = (string)request["rulePackId"] ?? "astral-rift";
                        if (!rules.TryGetValue(rulePackId, out var pack))
                            return NativeReply.Error("Unknown game.", 404);
                        var cardIds = request["cardIds"]?.Values<string>() ?? Enumerable.Empty<string>();
                        return NativeReply.Json(accounts.SaveDeck(self, (string)request["id"],
                            (string)request["name"], rulePackId, cardIds, pack));
                    }
                }
                if (method == "DELETE" && parts.Length == 3 && parts[0] == "api" &&
                    parts[1] == "decks")
                {
                    var deck = accounts.State.decks.FirstOrDefault(d => d.id == parts[2] && d.ownerId == self.id);
                    if (deck == null) return NativeReply.Error("Deck not found.", 404);
                    accounts.State.decks.Remove(deck);
                    accounts.Save();
                    return NativeReply.Json(new { success = true });
                }

                if (path == "/api/friends")
                {
                    if (method == "GET") return NativeReply.Json(accounts.State.friends.Where(f =>
                        f.userId == self.id || f.friendUserId == self.id).ToArray());
                    if (method == "POST")
                    {
                        var otherId = (string)request["friendUserId"];
                        var other = accounts.State.users.FirstOrDefault(u => u.id == otherId ||
                            string.Equals(u.handle, otherId, StringComparison.OrdinalIgnoreCase));
                        if (other == null || other.id == self.id)
                            return NativeReply.Error("Friend not found.", 404);
                        var connection = accounts.State.friends.FirstOrDefault(f =>
                            f.userId == self.id && f.friendUserId == other.id ||
                            f.userId == other.id && f.friendUserId == self.id);
                        if (connection == null)
                        {
                            connection = new NativeFriend
                            {
                                id = Guid.NewGuid().ToString("N"), userId = self.id,
                                friendUserId = other.id, createdAt = DateTimeOffset.UtcNow.ToString("o")
                            };
                            accounts.State.friends.Add(connection);
                            accounts.Save();
                        }
                        return NativeReply.Json(connection);
                    }
                }
                if (parts.Length == 3 && parts[0] == "api" && parts[1] == "friends")
                {
                    var friend = accounts.State.friends.FirstOrDefault(f => f.id == parts[2] &&
                        (f.userId == self.id || f.friendUserId == self.id));
                    if (friend == null) return NativeReply.Error("Friend request not found.", 404);
                    if (method == "DELETE") accounts.State.friends.Remove(friend);
                    else if (method == "POST" && friend.friendUserId == self.id)
                        friend.status = "accepted";
                    else return NativeReply.Error("Unsupported friendship action.", 403);
                    accounts.Save();
                    return NativeReply.Json(new { success = true });
                }

                if (path == "/api/messages")
                {
                    if (method == "GET") return NativeReply.Json(accounts.State.messages.Where(m =>
                        m.senderId == self.id || m.recipientId == self.id).TakeLast(200).ToArray());
                    if (method == "POST")
                    {
                        var recipientId = (string)request["recipientId"];
                        var body = ((string)request["body"] ?? "").Trim();
                        if (body.Length is < 1 or > 1000 ||
                            !accounts.State.users.Any(u => u.id == recipientId))
                            return NativeReply.Error("Invalid recipient or message.");
                        var item = new NativeMessage
                        {
                            id = Guid.NewGuid().ToString("N"), senderId = self.id,
                            recipientId = recipientId, body = body,
                            sentAt = DateTimeOffset.UtcNow.ToString("o")
                        };
                        accounts.State.messages.Add(item);
                        accounts.Save();
                        return NativeReply.Json(item);
                    }
                }

                if (path == "/api/games")
                {
                    if (method == "GET") return NativeReply.Json(games.Values
                        .Where(g => g.players.Contains(self.id)).Select(PublicGame).ToArray());
                    if (method == "POST")
                    {
                        var packId = (string)request["rulePackId"] ?? "astral-rift";
                        if (!rules.TryGetValue(packId, out var pack))
                            return NativeReply.Error("Unknown rule pack.", 404);
                        var teamSize = (int?)request["teamSize"] ?? 1;
                        var declaredHumans = request["playerIds"]?.Values<string>().ToList() ?? new List<string>();
                        if (declaredHumans.Any(p => p != self.id))
                            return NativeReply.Error("Other players must join through matchmaking.", 403);
                        var deck = accounts.OwnedDeck(self, (string)request["deckId"], packId);
                        var selected = new Dictionary<string, IList<string>>();
                        if (deck != null) selected[self.id] = deck.cardIds;
                        var game = engine.Create(packId, new[] { self.id }, teamSize,
                            (bool?)request["addAi"] ?? true, null, selected);
                        games.Add(game.id, game);
                        difficulty[game.id] = SimulationBots.Difficulty(self.rating, accounts.Power(deck, pack));
                        new SimulationBots(engine).PlayPendingTurns(game, difficulty[game.id]);
                        PersistMatches();
                        return NativeReply.Json(PublicGame(game));
                    }
                }

                if (parts.Length >= 3 && parts[0] == "api" && parts[1] == "games")
                {
                    if (!games.TryGetValue(parts[2], out var game) || !game.players.Contains(self.id))
                        return NativeReply.Error("Game not found.", 404);
                    if (method == "GET" && parts.Length == 3)
                        return NativeReply.Json(PublicGame(game));
                    if (method == "GET" && parts.Length == 4 && parts[3] == "client-view")
                        return NativeReply.Json(ClientView(game, self.id));
                    if (method == "POST" && parts.Length == 4 && parts[3] == "actions")
                    {
                        var action = request.ToObject<SimActionRequest>();
                        engine.Execute(game, self.id, action);
                        new SimulationBots(engine).PlayPendingTurns(game, difficulty[game.id]);
                        PersistMatches();
                        return NativeReply.Json(PublicGame(game));
                    }
                }

                if (path.StartsWith("/api/matchmaking/", StringComparison.Ordinal))
                    return Matchmaking(method, path, request, self);

                if (path == "/api/admin/rules/reload" && method == "POST" && self.role == "admin")
                {
                    LoadRules();
                    return NativeReply.Json(new { rules = rules.Keys.ToArray(), success = true });
                }

                return NativeReply.Error("API endpoint not found.", 404);
            }
            catch (UnauthorizedAccessException ex) { return NativeReply.Error(ex.Message, 401); }
            catch (KeyNotFoundException ex) { return NativeReply.Error(ex.Message, 404); }
            catch (JsonException ex) { return NativeReply.Error("Malformed request: " + ex.Message); }
            catch (FormatException) { return NativeReply.Error("Malformed binary data or field."); }
            catch (OverflowException) { return NativeReply.Error("Numeric value out of range."); }
            catch (InvalidOperationException ex) { return NativeReply.Error(ex.Message); }
        }

        private object PublicGame(SimMatch game) => new
        {
            id = game.id, rulePackId = game.rulePackId, players = game.players,
            activePlayerId = game.activePlayerId, currentPhaseId = game.currentPhaseId,
            turn = game.turn, finished = game.finished, winnerPlayerId = game.winnerPlayerId,
            teamSize = game.opponentCount
        };

        private object ClientView(SimMatch game, string viewerId)
        {
            var view = engine.View(game, viewerId);
            var pack = rules[game.rulePackId];
            var allowed = view.allowedActions.Select(id => pack.actions.FirstOrDefault(a => a.id == id))
                .Where(a => a != null).Select(a => new
                {
                    id = a.id, name = a.name, sourceZone = a.sourceZone,
                    destinationZone = a.destinationZone, requiresActivePlayer = a.requiresActivePlayer,
                    target = a.effects.FirstOrDefault(e => !string.IsNullOrEmpty(e.target))?.target
                }).ToArray();
            return new
            {
                id = view.id, rulePackId = view.rulePackId, players = view.players,
                activePlayerId = view.activePlayerId, currentPhaseId = view.currentPhaseId,
                turn = view.turn, finished = view.finished,
                winnerPlayerId = view.winnerPlayerId,
                zones = view.zones.Select(z => new
                {
                    playerId = z.ownerId, zoneId = z.zoneId, cards = z.cards
                }).ToArray(),
                resources = view.resources.Select(r => new
                {
                    playerId = r.ownerId, resourceId = r.resourceId, value = r.value
                }).ToArray(),
                allowedActions = allowed,
                data = view.data.Select(item => new { key = item.key, value = item.value }).ToArray(),
                events = view.events.Select(e => new
                {
                    type = e.type, message = e.message, actorId = e.actorId,
                    at = DateTimeOffset.UtcNow.ToString("o")
                }).ToArray()
            };
        }

        private NativeReply Matchmaking(string method, string path, JObject request, NativeUser self)
        {
            if (method == "GET" && path == "/api/matchmaking/queues")
                return NativeReply.Json(Enumerable.Range(1, 4).Select(n => new
                {
                    queue = new
                    {
                        id = n == 1 ? "casual" : "one-v-" + n, name = "1v" + n,
                        mode = "skill-and-deck-power", minPlayers = n + 1, maxPlayers = n + 1,
                        initialRatingRange = 120, ratingExpansionPerSecond = 5,
                        maxWaitSeconds = 30, botBackfill = true, enabled = true,
                        updatedAt = DateTimeOffset.UtcNow.ToString("o")
                    },
                    waitingPlayers = queues.Values.Count(q => q.queueId == (n == 1 ? "casual" : "one-v-" + n)),
                    waitingParties = queues.Values.Count(q => q.queueId == (n == 1 ? "casual" : "one-v-" + n)),
                    longestWaitSeconds = 0
                }).ToArray());

            if (method == "GET" && path == "/api/matchmaking/result")
                return NativeReply.Json(new { gameId = matched.TryGetValue(self.id, out var id) ? id : null });
            if (method == "GET" && path == "/api/matchmaking/me")
                return NativeReply.Json(queues.Values.Where(q => q.userId == self.id).ToArray());
            if (method == "DELETE" && path == "/api/matchmaking/leave")
            {
                foreach (var q in queues.Values.Where(q => q.userId == self.id).ToList())
                    queues.Remove(q.id);
                return NativeReply.Json(new { success = true });
            }
            if (method == "POST" && path == "/api/matchmaking/join")
            {
                var queueId = (string)request["queueId"] ?? "casual";
                var opponents = queueId == "casual" ? 1 :
                    queueId.StartsWith("one-v-", StringComparison.Ordinal) &&
                    int.TryParse(queueId.Substring(6), out var count) ? count : -1;
                if (opponents is < 1 or > 4 || (int?)request["partySize"] is > 1)
                    return NativeReply.Error("Choose a valid 1v1â€“1v4 queue. Party matchmaking is not enabled.");
                var rulePack = "astral-rift";
                var deck = accounts.OwnedDeck(self, (string)request["deckId"], rulePack);
                foreach (var q in queues.Values.Where(q => q.userId == self.id).ToList())
                    queues.Remove(q.id);
                matched.Remove(self.id);
                var entry = new NativeQueueEntry
                {
                    id = Guid.NewGuid().ToString("N"), queueId = queueId,
                    userId = self.id, rating = self.rating,
                    region = (string)request["region"] ?? "global",
                    rulePackId = rulePack, deckId = deck?.id,
                    deckPower = accounts.Power(deck, rules[rulePack]),
                    joinedAt = DateTimeOffset.UtcNow.ToString("o")
                };
                queues.Add(entry.id, entry);
                return NativeReply.Json(entry);
            }
            return NativeReply.Error("Unknown matchmaking route.", 404);
        }

        private void MatchQueue(int opponents)
        {
            var seats = opponents + 1;
            var queueId = opponents == 1 ? "casual" : "one-v-" + opponents;
            var available = queues.Values.Where(q => q.queueId == queueId)
                .OrderBy(q => q.Joined).ToList();
            while (available.Count > 0)
            {
                var anchor = available[0];
                var seconds = Math.Max(0, (DateTimeOffset.UtcNow - anchor.Joined).TotalSeconds);
                var skillRange = 120 + 5 * seconds;
                var powerRange = Math.Min(1000, 120 + 5 * seconds);
                var group = available.Where(q => q.rulePackId == anchor.rulePackId &&
                    q.region == anchor.region &&
                    Math.Abs(q.rating - anchor.rating) <= skillRange &&
                    Math.Abs(q.deckPower - anchor.deckPower) <= powerRange).Take(seats).ToList();
                if (group.Count < seats && seconds < 30) break;
                if (!rules.TryGetValue(anchor.rulePackId, out var pack) ||
                    pack.maximumPlayers < seats) break;
                var decks = new Dictionary<string, IList<string>>();
                foreach (var entry in group)
                {
                    var person = accounts.State.users.First(u => u.id == entry.userId);
                    var deck = accounts.OwnedDeck(person, entry.deckId, pack.id);
                    if (deck != null) decks[person.id] = deck.cardIds;
                }
                var game = engine.Create(pack.id, group.Select(q => q.userId).ToList(),
                    opponents, true, null, decks);
                games[game.id] = game;
                difficulty[game.id] = SimulationBots.Difficulty(anchor.rating, anchor.deckPower);
                new SimulationBots(engine).PlayPendingTurns(game, difficulty[game.id]);
                PersistMatches();
                foreach (var member in group)
                {
                    matched[member.userId] = game.id;
                    queues.Remove(member.id);
                    available.Remove(member);
                }
            }
        }

        private void RestoreMatches()
        {
            foreach (var game in persistence.LoadMatches())
            {
                if (game == null || string.IsNullOrWhiteSpace(game.id) ||
                    !rules.ContainsKey(game.rulePackId) ||
                    game.players == null || game.players.Count == 0 || game.teams == null ||
                    game.resources == null || game.zones == null) continue;
                games[game.id] = game;
                var owner = accounts.State.users.FirstOrDefault(u => u.id == game.players[0]);
                difficulty[game.id] = SimulationBots.Difficulty(owner?.rating ?? 1000, 500);
            }
        }

        private void PersistMatches() =>
            persistence.SaveMatches(games.Values);

        private static bool SafeId(string id) =>
            !string.IsNullOrWhiteSpace(id) && id.Length <= 120 &&
            id.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.');

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static void WriteAtomic(string path, string text) =>
            WriteAtomic(path, Encoding.UTF8.GetBytes(text));

        private static void WriteAtomic(string path, byte[] data)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, data);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private sealed class AssetDescriptor
        {
            public string id, name, contentType, sha256, url;
            public long length;
        }
        private List<AssetDescriptor> AssetManifest(string origin)
        {
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in new[] { Path.Combine(shippingContent, "Assets"),
                                           Path.Combine(persistentContent, "Assets") })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder))
                {
                    var key = Path.GetFileNameWithoutExtension(file);
                    if (folder == Path.Combine(shippingContent, "Assets") &&
                        file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                        !key.Contains(".card."))
                        key = "astral-rift.card." + key;
                    else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                             key == "scene-index") key = "cardforge.scene-index";
                    else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                             key == "scene-catalog") key = "cardforge.scene-catalog";
                    assets[key] = file;
                }
            }
            var host = Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https") ? uri.GetLeftPart(UriPartial.Authority) : "";
            return assets.Select(pair =>
            {
                var bytes = File.ReadAllBytes(pair.Value);
                string hash;
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                return new AssetDescriptor
                {
                    id = pair.Key, name = Path.GetFileName(pair.Value),
                    contentType = MediaType(pair.Value),
                    length = bytes.Length, sha256 = hash,
                    url = host + "/api/assets/" + Uri.EscapeDataString(pair.Key)
                };
            }).ToList();
        }

        private NativeReply ReadAsset(string key)
        {
            var files = AssetManifest("");
            var descriptor = files.FirstOrDefault(asset => asset.id == key);
            if (descriptor == null) return NativeReply.Error("Asset not found.", 404);
            string found = null;
            foreach (var folder in new[] { Path.Combine(persistentContent, "Assets"),
                                           Path.Combine(shippingContent, "Assets") })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var logical = folder == Path.Combine(shippingContent, "Assets") &&
                        file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains(".card.") ?
                        "astral-rift.card." + name :
                        name == "scene-index" ? "cardforge.scene-index" :
                        name == "scene-catalog" ? "cardforge.scene-catalog" : name;
                    if (logical == key) { found = file; break; }
                }
                if (found != null) break;
            }
            return found == null ? NativeReply.Error("Asset not found.", 404) :
                new NativeReply(200, File.ReadAllBytes(found), MediaType(found));
        }

        private static string MediaType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" ? "image/png" :
                   ext == ".json" ? "application/json" : "application/octet-stream";
        }
    }
}
