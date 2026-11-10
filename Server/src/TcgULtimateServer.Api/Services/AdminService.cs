using System.Collections.Concurrent;
using System.Text.Json;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class AuditLogService
{
    private const int MaxEntries = 2000;
    private readonly ConcurrentQueue<AuditLogEntry> entries = new();
    private readonly object fileLock = new();
    private readonly string filePath;

    public AuditLogService()
    {
        filePath = Environment.GetEnvironmentVariable("TCG_AUDIT_LOG_PATH") ?? Path.Combine(AppContext.BaseDirectory, "logs", "audit.ndjson");
        try
        {
            if (File.Exists(filePath))
            {
                foreach (var line in File.ReadLines(filePath).TakeLast(MaxEntries))
                {
                    var entry = JsonSerializer.Deserialize<AuditLogEntry>(line);
                    if (entry is not null) entries.Enqueue(entry);
                }
            }
        }
        catch
        {
        }
    }

    public void Write(string level, string category, string message, string? actorId = null, string? requestPath = null, int? statusCode = null)
    {
        var entry = new AuditLogEntry(DateTimeOffset.UtcNow, level, category, message, actorId, requestPath, statusCode);
        entries.Enqueue(entry);
        while (entries.Count > MaxEntries && entries.TryDequeue(out _)) { }
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            lock (fileLock)
                File.AppendAllText(filePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch
        {
        }
    }

    public IReadOnlyList<AuditLogEntry> Recent(int limit = 500) => entries.Reverse().Take(Math.Clamp(limit, 1, MaxEntries)).ToList();

    public int PurgeOlderThan(DateTimeOffset cutoff)
    {
        var kept = entries.Where(entry => entry.At >= cutoff).ToArray();
        var removed = Math.Max(0, entries.Count - kept.Length);
        while (entries.TryDequeue(out _)) { }
        foreach (var entry in kept) entries.Enqueue(entry);
        return removed;
    }
}

public sealed class AdminService(AppState state, AuditLogService audit)
{
    public bool IsAdmin(User user) => string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase) ||
        ConfiguredAdminHandles().Contains(user.Handle, StringComparer.OrdinalIgnoreCase);

    public bool CanModerate(User user) => IsAdmin(user) || string.Equals(user.Role, "moderator", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<AdminUserView> Users() => state.Users.Values
        .Where(user => !string.Equals(user.Id, "local-management", StringComparison.Ordinal))
        .OrderBy(user => user.Handle)
        .Select(user => new AdminUserView(user.Id, user.Handle, user.Rating, user.Role, user.Disabled))
        .ToList();

    public AdminUserView UpdateUser(User actor, string id, UpdateUserRequest request)
    {
        if (!state.Users.TryGetValue(id, out var current)) throw new KeyNotFoundException("User not found.");
        var actorIsAdmin = IsAdmin(actor);
        if (!actorIsAdmin && !string.Equals(current.Role, "user", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Moderators can only manage standard user accounts.");
        if (!actorIsAdmin && request.Role is not null && !string.Equals(request.Role, current.Role, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only administrators can change account roles.");
        if (string.Equals(actor.Id, id, StringComparison.Ordinal) && request.Disabled == true)
            throw new InvalidOperationException("You cannot disable your own account.");
        var role = request.Role is null ? current.Role : request.Role.Trim().ToLowerInvariant();
        if (role is not "user" and not "moderator" and not "admin") throw new InvalidOperationException("Role must be user, moderator, or admin.");
        var updated = current with { Role = role, Disabled = request.Disabled ?? current.Disabled };
        state.Users[id] = updated;
        audit.Write("Information", "moderation", $"Updated user {updated.Handle}: role={updated.Role}, disabled={updated.Disabled}.", actor.Id);
        return new AdminUserView(updated.Id, updated.Handle, updated.Rating, updated.Role, updated.Disabled);
    }

    public IReadOnlyList<AuditLogEntry> Logs(int limit) => audit.Recent(limit);

    public AdminChatSnapshot ChatSnapshot(int onlinePlayers) => new(
        onlinePlayers,
        state.DirectMessages.Values.OrderByDescending(message => message.SentAt).Take(500).ToList(),
        state.Games.Values
            .OrderByDescending(game => game.Events.LastOrDefault()?.At)
            .Select(game => new AdminGameChatChannel(
                game.Id,
                game.Players.ToList(),
                state.Chat.TryGetValue(game.Id, out var messages) ? messages.ToList() : [],
                game.Finished))
            .ToList(),
        DateTimeOffset.UtcNow);

    public AdminPlayerProfileView PlayerProfile(string id)
    {
        if (!state.Users.TryGetValue(id, out var user)) throw new KeyNotFoundException("Player not found.");
        var inventory = state.Inventories.TryGetValue(id, out var currentInventory) ? currentInventory : new Inventory([], []);
        var wallet = state.Wallets.TryGetValue(id, out var currentWallet)
            ? currentWallet
            : new PlayerWallet(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                [state.Economy.SoftCurrency] = 0,
                [state.Economy.PremiumCurrency] = 0
            });
        var transactions = state.CurrencyTransactions.Values
            .Where(transaction => string.Equals(transaction.UserId, id, StringComparison.Ordinal))
            .OrderByDescending(transaction => transaction.CreatedAt)
            .Take(250)
            .ToList();
        var rankedPlayers = state.Users.Values
            .Where(candidate => !string.Equals(candidate.Id, "local-management", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.Rating)
            .ThenBy(candidate => candidate.Handle)
            .ToList();
        var rank = Math.Max(1, rankedPlayers.FindIndex(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal)) + 1);
        var percentile = rankedPlayers.Count == 0 ? 0 : decimal.Round(100m * (rankedPlayers.Count - rank + 1) / rankedPlayers.Count, 1);
        var friends = state.FriendConnections.Values
            .Where(connection => string.Equals(connection.UserId, id, StringComparison.Ordinal) || string.Equals(connection.FriendUserId, id, StringComparison.Ordinal))
            .OrderByDescending(connection => connection.CreatedAt)
            .ToList();
        var messages = state.DirectMessages.Values
            .Where(message => string.Equals(message.SenderId, id, StringComparison.Ordinal) || string.Equals(message.RecipientId, id, StringComparison.Ordinal))
            .OrderByDescending(message => message.SentAt)
            .ToList();
        var decks = state.Decks.Values
            .Where(deck => string.Equals(deck.OwnerId, id, StringComparison.Ordinal))
            .OrderByDescending(deck => deck.UpdatedAt)
            .ToList();
        var deckStatistics = decks.Select(deck =>
        {
            var definitions = state.RulePacks.TryGetValue(deck.RulePackId, out var rulePack)
                ? deck.CardIds.Select(cardId => rulePack.Cards.FirstOrDefault(card => string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase))).Where(card => card is not null).ToList()
                : [];
            var averageCost = definitions.Count == 0 ? 0 : decimal.Round((decimal)definitions.Average(card => card!.Cost), 2);
            var cardTypes = definitions
                .GroupBy(card => card!.Type, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            return new AdminDeckStatistics(deck.Id, deck.Name, deck.CardIds.Count, deck.CardIds.Distinct(StringComparer.OrdinalIgnoreCase).Count(), averageCost, cardTypes, deck.Suspended);
        }).ToList();
        var matches = state.Games.Values
            .Where(game => game.Players.Contains(id, StringComparer.Ordinal))
            .OrderByDescending(game => game.Events.LastOrDefault()?.At)
            .Select(game => new GameRecordingView(
                game.Id,
                game.RulePackId,
                game.Players.ToList(),
                game.Turn,
                game.Finished,
                game.Events.ToList(),
                state.Chat.TryGetValue(game.Id, out var chat) ? chat.ToList() : []))
            .ToList();
        return new AdminPlayerProfileView(
            new AdminUserView(user.Id, user.Handle, user.Rating, user.Role, user.Disabled),
            inventory,
            wallet,
            transactions,
            new AdminPlayerRankingView(rank, rankedPlayers.Count, user.Rating, percentile),
            friends,
            messages,
            decks,
            deckStatistics,
            matches);
    }

    public CurrencyTransaction AdjustWallet(User actor, string id, CurrencyAdjustmentRequest request)
    {
        if (!state.Users.ContainsKey(id)) throw new KeyNotFoundException("Player not found.");
        var currency = request.Currency.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(currency) || currency.Length > 40) throw new InvalidOperationException("Currency is required.");
        if (request.Amount is < -1_000_000_000 or > 1_000_000_000 || request.Amount == 0) throw new InvalidOperationException("Adjustment must be between -1,000,000,000 and 1,000,000,000 and cannot be zero.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 240) throw new InvalidOperationException("A reason of up to 240 characters is required.");
        var wallet = state.Wallets.GetOrAdd(id, _ => new PlayerWallet(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)));
        long balance;
        lock (wallet.Balances)
        {
            var current = wallet.Balances.GetValueOrDefault(currency);
            balance = checked(current + request.Amount);
            if (balance < 0) throw new InvalidOperationException("The adjustment would make the player's balance negative.");
            wallet.Balances[currency] = balance;
        }
        var transaction = new CurrencyTransaction(Guid.NewGuid().ToString("N"), id, currency, request.Amount, balance, request.Reason.Trim(), actor.Id, DateTimeOffset.UtcNow);
        state.CurrencyTransactions[transaction.Id] = transaction;
        audit.Write("Information", "economy", $"Adjusted {id} by {request.Amount} {currency}: {request.Reason.Trim()}.", actor.Id);
        return transaction;
    }

    public IReadOnlyList<CurrencyTransaction> CurrencyTransactions(string? userId = null) => state.CurrencyTransactions.Values
        .Where(transaction => string.IsNullOrWhiteSpace(userId) || string.Equals(transaction.UserId, userId, StringComparison.Ordinal))
        .OrderByDescending(transaction => transaction.CreatedAt)
        .Take(1000)
        .ToList();

    public DirectMessage ModerateMessage(User actor, string id, UpdateMessageModerationRequest request)
    {
        if (!state.DirectMessages.TryGetValue(id, out var current)) throw new KeyNotFoundException("Message not found.");
        var updated = current with { Flagged = request.Flagged ?? current.Flagged, Removed = request.Removed ?? current.Removed };
        state.DirectMessages[id] = updated;
        audit.Write("Information", "moderation", $"Updated message {id}: flagged={updated.Flagged}, removed={updated.Removed}.", actor.Id);
        return updated;
    }

    public DeckDefinition ModerateDeck(User actor, string id, UpdateDeckModerationRequest request)
    {
        if (!state.Decks.TryGetValue(id, out var current)) throw new KeyNotFoundException("Deck not found.");
        var updated = current with { Suspended = request.Suspended, UpdatedAt = DateTimeOffset.UtcNow };
        state.Decks[id] = updated;
        audit.Write("Information", "moderation", $"Updated deck {id}: suspended={updated.Suspended}.", actor.Id);
        return updated;
    }

    public AdminSummary Summary() => new(
        state.Users.Count,
        state.Users.Values.Count(user => user.Disabled),
        state.Games.Count,
        state.StateMachineInstances.Count,
        state.Assets.Count,
        audit.Recent(200).Count,
        state.FriendConnections.Count,
        state.DirectMessages.Count,
        state.Decks.Count,
        state.StoreProducts.Count,
        state.ContentPacks.Count,
        state.MatchmakingQueues.Count,
        state.MatchmakingEntries.Values.Sum(entry => entry.PartySize),
        state.CurrencyTransactions.Count);

    private static HashSet<string> ConfiguredAdminHandles() =>
        (Environment.GetEnvironmentVariable("TCG_ADMIN_HANDLES") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(handle => handle.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
