using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using CardForge.Simulation;

namespace CardForge.DedicatedServer
{
    [Serializable] public sealed class NativeUser
    {
        public string id, handle, handleKey, role = "player", salt, passwordHash;
        public int rating = 1000;
        public bool disabled;
        public long gold = 100;
        public List<SimCardInstance> cards = new List<SimCardInstance>();
        public List<string> achievements = new List<string>();
    }
    [Serializable] public sealed class NativeDeck
    {
        public string id, ownerId, name, rulePackId, updatedAt;
        public bool suspended;
        public List<string> cardIds = new List<string>();
    }
    [Serializable] public sealed class NativeFriend
    {
        public string id, userId, friendUserId, status = "pending", createdAt;
    }
    [Serializable] public sealed class NativeMessage
    {
        public string id, senderId, recipientId, body, sentAt;
        public bool flagged, removed;
    }
    [Serializable] public sealed class NativeProduct
    {
        public string id, name, description, currency = "gold", assetId, contentPackId;
        public long price;
        public bool active = true;
    }
    [Serializable] public sealed class NativeEvent
    {
        public string id, name, kind, description, startsAt, endsAt;
        public bool active;
    }
    [Serializable] public sealed class NativeQueueEntry
    {
        public string id, queueId, userId, region, joinedAt, deckId, rulePackId;
        public int rating, partySize = 1, deckPower = 500;
        public DateTimeOffset Joined => DateTimeOffset.TryParse(joinedAt, out var dt) ? dt : DateTimeOffset.UtcNow;
    }
    [Serializable] public sealed class NativeState
    {
        public List<NativeUser> users = new List<NativeUser>();
        public List<NativeDeck> decks = new List<NativeDeck>();
        public List<NativeFriend> friends = new List<NativeFriend>();
        public List<NativeMessage> messages = new List<NativeMessage>();
        public List<NativeProduct> products = new List<NativeProduct>();
        public List<NativeEvent> events = new List<NativeEvent>();
    }
    public sealed class NativeSession
    {
        public string token, userId;
        public DateTimeOffset expires;
    }

    /// <summary>
    /// Native local-account and soft-currency store backed by MongoDB.
    /// This class can be tested in .NET without Unity and never invokes the old API.
    /// </summary>
    public sealed class NativeAccounts
    {
        private readonly INativePersistence persistence;
        private readonly Dictionary<string, NativeSession> sessions = new Dictionary<string, NativeSession>();
        public NativeState State { get; private set; }

        public NativeAccounts(INativePersistence persistence, bool demoMode, SimRulePack startingRules)
        {
            this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            State = persistence.LoadAccounts() ?? new NativeState();
            if (demoMode)
            {
                var demo = State.users.FirstOrDefault(user =>
                    string.Equals(user.handle, "demo", StringComparison.OrdinalIgnoreCase));
                if (demo == null)
                    demo = Register("demo", "cardforge-demo");
                // Existing demo accounts are upgraded without replacing collections or decks.
                SeedCards(demo, startingRules);
                // Demo accounts from older releases can hold invalid starter
                // lists. Bring canonical starters forward without touching
                // their custom legal decks or account credentials.
                foreach (var existing in State.users.Where(user => user.id != demo.id))
                    SeedCards(existing, startingRules);
                // Admin privileges are granted ONLY when the host is in local demo mode.
                demo.role = "admin";
                Save();
            }
            else
            {
                // Never retain demo-admin elevation if a state folder is reused in production.
                foreach (var demo in State.users.Where(u =>
                    string.Equals(u.handle, "demo", StringComparison.OrdinalIgnoreCase)))
                    demo.role = "player";
                var adminHandle = Environment.GetEnvironmentVariable("CARDFORGE_ADMIN_HANDLE");
                var adminPassword = Environment.GetEnvironmentVariable("CARDFORGE_ADMIN_PASSWORD");
                if (!string.IsNullOrWhiteSpace(adminHandle) &&
                    !string.IsNullOrWhiteSpace(adminPassword))
                {
                    var admin = State.users.FirstOrDefault(u =>
                        string.Equals(u.handle, adminHandle, StringComparison.OrdinalIgnoreCase));
                    if (admin == null) admin = Register(adminHandle, adminPassword);
                    admin.role = "admin";
                }
                Save();
            }
            if (!State.products.Any(product => product.id == "astral-starter-pack"))
            {
                State.products.Add(new NativeProduct
                {
                    id = "astral-starter-pack", name = "Astral Rift Starter Pack",
                    description = "Five random Astral Rift cards.",
                    currency = "gold", price = 25, contentPackId = startingRules.id, active = true
                });
                Save();
            }
            if (!State.events.Any(e => e.id == "astral-rift-season"))
            {
                State.events.Add(new NativeEvent
                {
                    id = "astral-rift-season", name = "Astral Rift Launch Season", kind = "event",
                    description = "Build a deck and compete in 1v1, 1v2, 1v3 and 1v4.",
                    startsAt = DateTimeOffset.UtcNow.ToString("o"),
                    endsAt = DateTimeOffset.UtcNow.AddDays(90).ToString("o"), active = true
                });
                Save();
            }
        }

        public NativeUser Register(string handle, string password)
        {
            handle = (handle ?? "").Trim();
            if (handle.Length < 3 || handle.Length > 32 ||
                !handle.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                throw new InvalidOperationException("Handle must be 3–32 letters, digits, - or _.");
            if (password == null || password.Length < 10 || password.Length > 256)
                throw new InvalidOperationException("Password must contain at least 10 characters.");
            if (State.users.Any(user => string.Equals(user.handle, handle, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Handle is already registered.");
            var salt = SecureRandom(16);
            var userRecord = new NativeUser
            {
                id = Guid.NewGuid().ToString("N"), handle = handle,
                handleKey = handle.ToLowerInvariant(),
                salt = Convert.ToBase64String(salt), passwordHash = Hash(password, salt)
            };
            State.users.Add(userRecord);
            Save();
            return userRecord;
        }

        public NativeUser Authenticate(string handle, string password)
        {
            var user = State.users.FirstOrDefault(record =>
                string.Equals(record.handle, handle, StringComparison.OrdinalIgnoreCase));
            if (user == null || user.disabled || password == null ||
                !ConstantTimeEqual(user.passwordHash, Hash(password, Convert.FromBase64String(user.salt))))
                throw new UnauthorizedAccessException("Invalid credentials.");
            return user;
        }

        public string IssueSession(NativeUser user)
        {
            var token = Convert.ToBase64String(SecureRandom(48))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');
            sessions[token] = new NativeSession
            {
                token = token, userId = user.id, expires = DateTimeOffset.UtcNow.AddDays(2)
            };
            return token;
        }

        public NativeUser Resolve(string token)
        {
            if (string.IsNullOrWhiteSpace(token) ||
                !sessions.TryGetValue(token, out var session) ||
                session.expires <= DateTimeOffset.UtcNow)
                throw new UnauthorizedAccessException("Authentication is required.");
            var user = State.users.FirstOrDefault(u => u.id == session.userId);
            if (user == null || user.disabled)
                throw new UnauthorizedAccessException("Session is invalid.");
            return user;
        }

        public void Revoke(string token) { if (token != null) sessions.Remove(token); }
        public int ActiveSessions => sessions.Values.Count(session => session.expires > DateTimeOffset.UtcNow);

        public static object Public(NativeUser u) => new
        {
            u.id, u.handle, u.rating, u.role, u.disabled
        };

        public NativeDeck SaveDeck(NativeUser owner, string id, string name, string rulePack,
            IEnumerable<string> cards, SimRulePack rules)
        {
            var requested = cards?.ToList() ?? new List<string>();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
                throw new InvalidOperationException("Invalid deck name.");
            if (!string.Equals(rulePack, rules.id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unknown rule pack.");
            var legality = DeckLegality.ValidateDeck(rules, requested);
            if (legality.Count > 0)
                throw new InvalidOperationException("Illegal deck: " + string.Join(" ", legality));
            var known = new HashSet<string>(rules.cards.Select(card => card.id), StringComparer.OrdinalIgnoreCase);
            var owned = owner.cards.GroupBy(card => card.cardId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var group in requested.GroupBy(card => card, StringComparer.OrdinalIgnoreCase))
            {
                if (!known.Contains(group.Key) || group.Count() > (owned.TryGetValue(group.Key, out var count) ? count : 0))
                    throw new InvalidOperationException("Deck contains unavailable or unowned copies of " + group.Key);
            }
            var deck = string.IsNullOrWhiteSpace(id) ? null : State.decks.FirstOrDefault(d => d.id == id);
            if (deck != null && deck.ownerId != owner.id)
                throw new UnauthorizedAccessException("Cannot modify another player's deck.");
            if (deck == null)
            {
                deck = new NativeDeck { id = Guid.NewGuid().ToString("N"), ownerId = owner.id };
                State.decks.Add(deck);
            }
            deck.name = name.Trim();
            deck.rulePackId = rulePack;
            deck.cardIds = requested;
            deck.updatedAt = DateTimeOffset.UtcNow.ToString("o");
            Save();
            return deck;
        }

        public NativeDeck OwnedDeck(NativeUser owner, string deckId, string ruleset)
        {
            if (string.IsNullOrWhiteSpace(deckId)) return null;
            var deck = State.decks.FirstOrDefault(d => d.id == deckId &&
                d.ownerId == owner.id && !d.suspended && d.rulePackId == ruleset);
            if (deck == null) throw new InvalidOperationException("Deck unavailable or unowned.");
            return deck;
        }

        public int Power(NativeDeck deck, SimRulePack rules)
        {
            if (deck == null || deck.cardIds.Count == 0) return 500;
            var definitions = rules.cards.ToDictionary(card => card.id, StringComparer.OrdinalIgnoreCase);
            var valid = deck.cardIds.Where(definitions.ContainsKey).Select(id => definitions[id]).ToList();
            if (valid.Count == 0) return 500;
            var efficiency = valid.Average(card =>
                (Math.Max(0, card.power) + Math.Max(0, card.toughness) +
                 card.abilities.Count * 2.5 + 1.5) / Math.Max(1, card.cost + 1.0));
            var curve = valid.Count(card => card.cost <= 3) / (double)valid.Count;
            var repeats = deck.cardIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Count(g => g.Count() > 1) / (double)Math.Max(1, deck.cardIds.Distinct().Count());
            return (int)Math.Max(100, Math.Min(1000, 300 + efficiency * 90 + curve * 105 + repeats * 95));
        }

        public object Purchase(NativeUser owner, string productId, SimRulePack rules)
        {
            var product = State.products.FirstOrDefault(p => p.id == productId && p.active);
            if (product == null) throw new InvalidOperationException("Product unavailable.");
            if (product.currency != "gold" || product.price < 0 || owner.gold < product.price)
                throw new InvalidOperationException("Insufficient funds.");
            if (!string.IsNullOrWhiteSpace(product.contentPackId) &&
                product.contentPackId != rules.id)
                throw new InvalidOperationException("Invalid product content pack.");
            var granted = new List<SimCardInstance>();
            if (product.contentPackId == rules.id)
            {
                var options = rules.startingDeck.Count > 0 ? rules.startingDeck :
                    rules.cards.Select(c => c.id).ToList();
                if (options.Count == 0) throw new InvalidOperationException("No cards to grant.");
                for (var i = 0; i < 5; i++)
                {
                    var card = options[SecureInt(options.Count)];
                    granted.Add(new SimCardInstance(Guid.NewGuid().ToString("N"), card, owner.id));
                }
            }
            if (granted.Count == 0 && string.IsNullOrWhiteSpace(product.assetId))
                throw new InvalidOperationException("Product has no deliverables.");
            owner.gold -= product.price;
            owner.cards.AddRange(granted);
            if (!string.IsNullOrEmpty(product.assetId))
                owner.achievements.Add("entitlement:" + product.assetId);
            Save();
            return new
            {
                productId, currency = "gold", balance = owner.gold,
                cards = granted, assetId = product.assetId
            };
        }

        public void SeedCards(NativeUser user, SimRulePack rules)
        {
            // Starter ownership is idempotent across sign-in and restarts.
            // Grant only the cards required by the three 42-card starter decks,
            // with one Commander and one Mana Source in each list.
            var starters = rules.starterDecks ?? new List<SimStarterDeck>();
            if (starters.Count > 0)
            {
                var needs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var starter in starters)
                {
                    foreach (var group in starter.cardIds.GroupBy(id => id,
                        StringComparer.OrdinalIgnoreCase))
                        needs[group.Key] = Math.Max(needs.TryGetValue(group.Key, out var current) ?
                            current : 0, group.Count());
                    // The Commander and Mana Source are part of the 42-card list.
                    // Board rules, not deck definitions, determine their starting zones.

                    if (!State.decks.Any(deck => deck.ownerId == user.id &&
                        deck.rulePackId == rules.id && deck.name == starter.name))
                        State.decks.Add(new NativeDeck
                        {
                            id = Guid.NewGuid().ToString("N"), ownerId = user.id,
                            name = starter.name, rulePackId = rules.id,
                            cardIds = starter.cardIds.ToList(),
                            updatedAt = DateTimeOffset.UtcNow.ToString("o")
                        });
                }
                foreach (var entry in needs)
                {
                    var existing = user.cards.Count(card => string.Equals(card.cardId,
                        entry.Key, StringComparison.OrdinalIgnoreCase));
                    for (var i = existing; i < entry.Value; i++)
                        user.cards.Add(new SimCardInstance(Guid.NewGuid().ToString("N"),
                            entry.Key, user.id));
                }
            }
            else
            {
                foreach (var id in rules.startingDeck.Distinct())
                {
                    var existing = user.cards.Count(card => card.cardId == id);
                    for (var i = existing; i < 4; i++)
                        user.cards.Add(new SimCardInstance(Guid.NewGuid().ToString("N"), id, user.id));
                }
            }
            Save();
        }

        public void Save() => persistence.SaveAccounts(State);

        private static byte[] SecureRandom(int length)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }
        private static int SecureInt(int max)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            var limit = uint.MaxValue - (uint.MaxValue % (uint)max);
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[4];
                uint value;
                do { rng.GetBytes(bytes); value = BitConverter.ToUInt32(bytes, 0); }
                while (value >= limit);
                return (int)(value % (uint)max);
            }
        }
        private static string Hash(string pass, byte[] salt)
        {
            using (var derivation = new Rfc2898DeriveBytes(pass, salt, 150000, HashAlgorithmName.SHA256))
                return Convert.ToBase64String(derivation.GetBytes(32));
        }
        private static bool ConstantTimeEqual(string a, string b)
        {
            var one = Convert.FromBase64String(a);
            var two = Convert.FromBase64String(b);
            if (one.Length != two.Length) return false;
            var result = 0;
            for (var i = 0; i < one.Length; i++) result |= one[i] ^ two[i];
            return result == 0;
        }
    }
}
