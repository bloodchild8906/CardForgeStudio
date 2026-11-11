using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardForge.Simulation;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using Newtonsoft.Json;

namespace CardForge.DedicatedServer
{
    /// <summary>
    /// Native server MongoDB persistence: one BSON document per user, deck, friend,
    /// message, store product, event and match. Never persists bearer tokens.
    /// MongoDB is mandatory at runtime: connection failure stops startup.
    /// </summary>
    public sealed class MongoNativePersistence : INativePersistence
    {
        private readonly IMongoDatabase db;
        private readonly Dictionary<string, Dictionary<string, string>> saved =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private readonly string localDataPath;

        public MongoNativePersistence(string connectionString, string databaseName,
            string localDataPath = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("MongoDB connection URI is required.");
            if (string.IsNullOrWhiteSpace(databaseName) || databaseName.Length > 60 ||
                !databaseName.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                throw new InvalidOperationException("Unsafe MongoDB database name.");

            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            settings.ConnectTimeout = TimeSpan.FromSeconds(5);
            settings.SocketTimeout = TimeSpan.FromSeconds(10);
            var client = new MongoClient(settings);
            db = client.GetDatabase(databaseName);
            this.localDataPath = localDataPath;
            // Fail startup immediately instead of silently writing local JSON.
            db.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            EnsureIndexes();
        }

        public bool CheckHealth()
        {
            try
            {
                db.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
                return true;
            }
            catch (MongoException) { return false; }
        }

        public NativeState LoadAccounts()
        {
            var state = new NativeState
            {
                users = Read<NativeUser>("users"),
                decks = Read<NativeDeck>("decks"),
                friends = Read<NativeFriend>("friends"),
                messages = Read<NativeMessage>("messages"),
                products = Read<NativeProduct>("products"),
                events = Read<NativeEvent>("events")
            };

            if (localDataPath != null && state.users.Count == 0 &&
                Environment.GetEnvironmentVariable("CARDFORGE_MIGRATE_JSON") == "1")
            {
                var prior = Path.Combine(localDataPath, "players.json");
                if (File.Exists(prior))
                {
                    state = Newtonsoft.Json.JsonConvert.DeserializeObject<NativeState>(File.ReadAllText(prior))
                        ?? state;
                    SaveAccounts(state);
                }
            }
            return state;
        }

        public void SaveAccounts(NativeState state)
        {
            foreach (var person in state.users)
                person.handleKey = (person.handle ?? "").ToLowerInvariant();

            Save("users", state.users, person => person.id);
            Save("decks", state.decks, item => item.id);
            Save("friends", state.friends, item => item.id);
            Save("messages", state.messages, item => item.id);
            Save("products", state.products, item => item.id);
            Save("events", state.events, item => item.id);
        }

        public List<SimMatch> LoadMatches()
        {
            var matches = Read<SimMatch>("matches");
            if (matches.Count == 0 && localDataPath != null &&
                Environment.GetEnvironmentVariable("CARDFORGE_MIGRATE_JSON") == "1")
            {
                var prior = Path.Combine(localDataPath, "matches.json");
                if (File.Exists(prior))
                {
                    matches = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SimMatch>>(File.ReadAllText(prior))
                        ?? matches;
                    SaveMatches(matches);
                }
            }
            return matches;
        }

        public void SaveMatches(IEnumerable<SimMatch> matches) =>
            Save("matches", matches.ToList(), match => match.id);

        private List<T> Read<T>(string collection)
        {
            var docs = db.GetCollection<BsonDocument>(collection)
                .Find(FilterDefinition<BsonDocument>.Empty).ToList();
            var memo = new Dictionary<string, string>(StringComparer.Ordinal);
            var result = new List<T>();
            foreach (var doc in docs)
            {
                var id = doc["_id"].AsString;
                doc.Remove("_id");
                var json = doc.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
                {
                    OutputMode = JsonOutputMode.RelaxedExtendedJson
                });
                var model = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
                if (model != null) result.Add(model);
                memo[id] = json;
            }
            saved[collection] = memo;
            return result;
        }

        private void Save<T>(string collection, IList<T> models, Func<T, string> getId)
        {
            var known = saved.TryGetValue(collection, out var fingerprints)
                ? fingerprints : new Dictionary<string, string>(StringComparer.Ordinal);
            var current = new Dictionary<string, string>(StringComparer.Ordinal);
            var changes = new List<WriteModel<BsonDocument>>();
            foreach (var model in models)
            {
                var id = getId(model);
                if (string.IsNullOrWhiteSpace(id) || current.ContainsKey(id))
                    throw new InvalidOperationException("Duplicate or empty record ID in " + collection);
                var document = BsonDocument.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(model));
                // Keep the schema inspectable and indexable as native MongoDB BSON.
                var json = document.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
                {
                    OutputMode = JsonOutputMode.RelaxedExtendedJson
                });
                current.Add(id, json);
                if (known.TryGetValue(id, out var original) &&
                    string.Equals(original, json, StringComparison.Ordinal)) continue;
                document["_id"] = id;
                changes.Add(new ReplaceOneModel<BsonDocument>(
                    Builders<BsonDocument>.Filter.Eq("_id", id), document)
                { IsUpsert = true });
            }

            foreach (var removedId in known.Keys.Except(current.Keys, StringComparer.Ordinal))
                changes.Add(new DeleteOneModel<BsonDocument>(
                    Builders<BsonDocument>.Filter.Eq("_id", removedId)));
            if (changes.Count > 0)
            {
                // Ordered writes retain the single-process update sequence and
                // let errors propagate instead of pretending persistence succeeded.
                db.GetCollection<BsonDocument>(collection).BulkWrite(changes,
                    new BulkWriteOptions { IsOrdered = true });
            }
            saved[collection] = current;
        }

        private void EnsureIndexes()
        {
            db.GetCollection<BsonDocument>("users").Indexes.CreateOne(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("handleKey"),
                    new CreateIndexOptions { Name = "unique_user_handle", Unique = true }));
            db.GetCollection<BsonDocument>("decks").Indexes.CreateOne(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("ownerId")));
            db.GetCollection<BsonDocument>("friends").Indexes.CreateOne(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("userId")
                        .Ascending("friendUserId")));
            db.GetCollection<BsonDocument>("messages").Indexes.CreateOne(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("recipientId")
                        .Descending("sentAt")));
            db.GetCollection<BsonDocument>("matches").Indexes.CreateOne(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("rulePackId")
                        .Ascending("finished")));
        }

        public void Dispose() { }
    }
}
