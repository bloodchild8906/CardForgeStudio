using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class OperationsService(AppState state, AuditLogService audit, RulesEngine rules)
{
    public IReadOnlyList<BotProfile> Bots() => state.BotProfiles.Values.OrderBy(bot => bot.Name).ToList();

    public BotProfile SaveBot(BotProfileRequest request, string actorId)
    {
        ValidateIdentity(request.Id, request.Name, "Bot");
        if (request.DailyMatchLimit is < 1 or > 1_000_000)
            throw new InvalidOperationException("Daily match limit must be between 1 and 1,000,000.");
        if (request.InferenceBudgetTokens is < 1_000 or > 100_000_000)
            throw new InvalidOperationException("Inference budget must be between 1,000 and 100,000,000 tokens.");

        state.BotProfiles.TryGetValue(request.Id.Trim(), out var existing);
        var modelVersion = Required(request.ModelVersion, "Model version");
        var previousModel = existing?.PreviousModelVersion;
        if (existing is not null && !string.Equals(existing.ModelVersion, modelVersion, StringComparison.Ordinal))
            previousModel = existing.ModelVersion;

        var bot = new BotProfile(
            request.Id.Trim(),
            request.Name.Trim(),
            Required(request.Personality, "Personality"),
            Required(request.Difficulty, "Difficulty"),
            modelVersion,
            Required(request.MatchmakingPool, "Matchmaking pool"),
            request.Enabled,
            request.DailyMatchLimit,
            DateTimeOffset.UtcNow,
            request.InferenceBudgetTokens,
            previousModel);
        state.BotProfiles[bot.Id] = bot;
        audit.Write("Information", "bots", $"Saved bot profile {bot.Id}.", actorId);
        return bot;
    }

    public bool RemoveBot(string id, string actorId)
    {
        if (!state.BotProfiles.TryRemove(id, out _)) return false;
        foreach (var run in state.BotSimulationRuns.Values.Where(run => string.Equals(run.BotId, id, StringComparison.OrdinalIgnoreCase)).ToList())
            state.BotSimulationRuns.TryRemove(run.Id, out _);
        audit.Write("Information", "bots", $"Removed bot profile {id}.", actorId);
        return true;
    }

    public BotSimulationRun SimulateBot(string id, BotSimulationRequest request, string actorId)
    {
        if (!state.BotProfiles.TryGetValue(id, out var bot))
            throw new KeyNotFoundException("Bot profile not found.");
        if (request.Matches is < 10 or > 100_000)
            throw new InvalidOperationException("Simulation matches must be between 10 and 100,000.");

        var winRate = bot.Difficulty.Trim().ToLowerInvariant() switch
        {
            "beginner" => 0.38m,
            "intermediate" => 0.48m,
            "advanced" => 0.57m,
            "expert" => 0.66m,
            _ => 0.50m
        };
        var averageTurns = bot.Difficulty.Trim().Equals("expert", StringComparison.OrdinalIgnoreCase) ? 8 : 10;
        var run = new BotSimulationRun(Guid.NewGuid().ToString("N"), bot.Id, request.Matches, winRate, averageTurns, DateTimeOffset.UtcNow);
        state.BotSimulationRuns[run.Id] = run;
        audit.Write("Information", "bots", $"Ran {request.Matches:N0}-match simulation for {bot.Id}.", actorId);
        return run;
    }

    public BotProfile RollbackModel(string id, string actorId)
    {
        if (!state.BotProfiles.TryGetValue(id, out var bot) || string.IsNullOrWhiteSpace(bot.PreviousModelVersion))
            throw new InvalidOperationException("This bot has no previous model version to restore.");
        var rolled = bot with
        {
            ModelVersion = bot.PreviousModelVersion,
            PreviousModelVersion = bot.ModelVersion,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        state.BotProfiles[id] = rolled;
        audit.Write("Information", "bots", $"Rolled back model for bot {id} to {rolled.ModelVersion}.", actorId);
        return rolled;
    }

    public EconomyAnalytics EconomyAnalytics()
    {
        var soft = state.Economy.SoftCurrency;
        var premium = state.Economy.PremiumCurrency;
        long Issued(string currency) => state.CurrencyTransactions.Values
            .Where(transaction => string.Equals(transaction.Currency, currency, StringComparison.OrdinalIgnoreCase) && transaction.Amount > 0)
            .Sum(transaction => transaction.Amount);
        return new EconomyAnalytics(
            state.StoreProducts.Count,
            state.StoreProducts.Values.Count(product => product.Active),
            state.CurrencyTransactions.Count,
            Issued(soft),
            Issued(premium),
            state.StoreProducts.Values.Where(product => product.Active).Sum(product => product.Price),
            DateTimeOffset.UtcNow);
    }

    public EconomySimulationResult SimulateEconomy(EconomySimulationRequest request, string actorId)
    {
        if (request.ActivePlayers is < 1 or > 1_000_000 || request.Days is < 1 or > 365 || request.PurchasesPerPlayer is < 0 or > 100)
            throw new InvalidOperationException("Economy simulation bounds are 1–1,000,000 players, 1–365 days, and 0–100 purchases per player.");
        var activeProducts = state.StoreProducts.Values.Where(product => product.Active).ToList();
        var averagePrice = activeProducts.Count == 0 ? 0m : activeProducts.Average(product => product.Price);
        var purchases = (long)request.ActivePlayers * request.Days * request.PurchasesPerPlayer;
        var sink = (long)Math.Round(averagePrice * purchases, MidpointRounding.AwayFromZero);
        var source = (long)Math.Round(request.ActivePlayers * request.Days * state.Economy.DailySoftCurrencyCap * 0.4m, MidpointRounding.AwayFromZero);
        var tax = Math.Round(averagePrice * purchases * state.Economy.StoreTaxPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var net = source - sink;
        var assessment = activeProducts.Count == 0
            ? "No active products, so the simulation has no currency sink."
            : net < 0 ? "Projected spend exceeds the modeled daily grant. Review prices or the daily cap."
            : "Projected grants cover modeled store spend at the current catalog.";
        var result = new EconomySimulationResult(request.ActivePlayers, request.Days, source, sink, net, tax, assessment, DateTimeOffset.UtcNow);
        audit.Write("Information", "economy", $"Ran a {request.Days}-day economy simulation for {request.ActivePlayers:N0} players.", actorId);
        return result;
    }

    public IReadOnlyList<MatchmakingQueueStatus> MatchmakingQueues()
    {
        var now = DateTimeOffset.UtcNow;
        return state.MatchmakingQueues.Values
            .OrderBy(queue => queue.Name)
            .Select(queue =>
            {
                var entries = state.MatchmakingEntries.Values.Where(entry => string.Equals(entry.QueueId, queue.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                var longestWait = entries.Count == 0 ? 0 : (int)Math.Max(0, (now - entries.Min(entry => entry.JoinedAt)).TotalSeconds);
                return new MatchmakingQueueStatus(queue, entries.Sum(entry => entry.PartySize), entries.Count, longestWait);
            })
            .ToList();
    }

    public IReadOnlyList<MatchmakingQueueEntry> MatchmakingEntries(string? queueId = null) => state.MatchmakingEntries.Values
        .Where(entry => string.IsNullOrWhiteSpace(queueId) || string.Equals(entry.QueueId, queueId, StringComparison.OrdinalIgnoreCase))
        .OrderBy(entry => entry.JoinedAt)
        .ToList();

    public MatchmakingQueueConfiguration SaveMatchmakingQueue(MatchmakingQueueRequest request, string actorId)
    {
        ValidateIdentity(request.Id, request.Name, "Matchmaking queue");
        if (request.MinPlayers is < 2 or > 16 || request.MaxPlayers < request.MinPlayers || request.MaxPlayers > 16)
            throw new InvalidOperationException("Matchmaking player limits must be between 2 and 16, with max players not lower than min players.");
        if (request.InitialRatingRange is < 0 or > 5000 || request.RatingExpansionPerSecond is < 0 or > 1000)
            throw new InvalidOperationException("Matchmaking rating ranges are out of bounds.");
        if (request.MaxWaitSeconds is < 5 or > 3600)
            throw new InvalidOperationException("Maximum wait must be between 5 and 3,600 seconds.");
        var queue = new MatchmakingQueueConfiguration(
            request.Id.Trim(),
            request.Name.Trim(),
            Required(request.Mode, "Mode"),
            request.MinPlayers,
            request.MaxPlayers,
            request.InitialRatingRange,
            request.RatingExpansionPerSecond,
            request.MaxWaitSeconds,
            request.BotBackfill,
            request.Enabled,
            DateTimeOffset.UtcNow);
        state.MatchmakingQueues[queue.Id] = queue;
        audit.Write("Information", "matchmaking", $"Saved matchmaking queue {queue.Id}.", actorId);
        return queue;
    }

    public bool RemoveMatchmakingQueue(string id, string actorId)
    {
        if (!state.MatchmakingQueues.TryRemove(id, out _)) return false;
        ClearMatchmakingQueue(id, actorId);
        audit.Write("Information", "matchmaking", $"Removed matchmaking queue {id}.", actorId);
        return true;
    }

    public MatchmakingQueueEntry JoinMatchmaking(User user, JoinMatchmakingRequest request)
    {
        if (!state.MatchmakingQueues.TryGetValue(request.QueueId, out var queue) || !queue.Enabled)
            throw new InvalidOperationException("The matchmaking queue is not available.");
        if (request.PartySize is < 1 or > 8 || request.PartySize > queue.MaxPlayers)
            throw new InvalidOperationException("Party size is not valid for this queue.");
        foreach (var existing in state.MatchmakingEntries.Values.Where(entry => string.Equals(entry.UserId, user.Id, StringComparison.Ordinal)).ToList())
            state.MatchmakingEntries.TryRemove(existing.Id, out _);
        var entry = new MatchmakingQueueEntry(
            Guid.NewGuid().ToString("N"),
            queue.Id,
            user.Id,
            user.Rating,
            request.PartySize,
            string.IsNullOrWhiteSpace(request.Region) ? "global" : request.Region.Trim().ToLowerInvariant(),
            DateTimeOffset.UtcNow);
        state.MatchmakingEntries[entry.Id] = entry;
        return entry;
    }

    public bool LeaveMatchmaking(User user) => state.MatchmakingEntries.Values
        .Where(entry => string.Equals(entry.UserId, user.Id, StringComparison.Ordinal))
        .Select(entry => state.MatchmakingEntries.TryRemove(entry.Id, out _))
        .Any(removed => removed);

    public int ClearMatchmakingQueue(string id, string actorId)
    {
        var entries = state.MatchmakingEntries.Values.Where(entry => string.Equals(entry.QueueId, id, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var entry in entries) state.MatchmakingEntries.TryRemove(entry.Id, out _);
        if (entries.Count > 0) audit.Write("Information", "matchmaking", $"Cleared {entries.Count} parties from queue {id}.", actorId);
        return entries.Count;
    }

    public MatchmakingRunResult RunMatchmaking(string id, string actorId)
    {
        if (!state.MatchmakingQueues.TryGetValue(id, out var queue)) throw new KeyNotFoundException("Matchmaking queue not found.");
        if (!queue.Enabled) throw new InvalidOperationException("Enable the queue before running matchmaking.");
        var waiting = state.MatchmakingEntries.Values
            .Where(entry => string.Equals(entry.QueueId, id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.JoinedAt)
            .ToList();
        var gameIds = new List<string>();
        var playersMatched = 0;
        var botsAdded = 0;

        while (waiting.Count > 0)
        {
            var anchor = waiting[0];
            var waitSeconds = Math.Max(0, (DateTimeOffset.UtcNow - anchor.JoinedAt).TotalSeconds);
            var ratingRange = queue.InitialRatingRange + (int)(waitSeconds * queue.RatingExpansionPerSecond);
            var candidates = waiting
                .Where(entry => string.Equals(entry.Region, anchor.Region, StringComparison.OrdinalIgnoreCase) && Math.Abs(entry.Rating - anchor.Rating) <= ratingRange)
                .ToList();
            var selected = new List<MatchmakingQueueEntry>();
            var playerCount = 0;
            foreach (var candidate in candidates)
            {
                if (playerCount + candidate.PartySize > queue.MaxPlayers) continue;
                selected.Add(candidate);
                playerCount += candidate.PartySize;
                if (playerCount == queue.MaxPlayers) break;
            }

            var allowedToBackfill = queue.BotBackfill && waitSeconds >= queue.MaxWaitSeconds;
            if (playerCount < queue.MinPlayers && !allowedToBackfill) break;
            var botsForMatch = Math.Max(0, queue.MinPlayers - playerCount);
            var players = selected.SelectMany(entry => Enumerable.Range(0, entry.PartySize).Select(index => index == 0 ? entry.UserId : $"{entry.UserId}-party-{index + 1}")).ToList();
            players.AddRange(Enumerable.Range(1, botsForMatch).Select(index => $"matchmaking-bot-{Guid.NewGuid():N}"));
            var game = new GameState { RulePackId = state.RulePacks.Keys.FirstOrDefault() ?? "mtg-like", Players = players };
            rules.Start(game);
            state.Games[game.Id] = game;
            gameIds.Add(game.Id);
            playersMatched += playerCount;
            botsAdded += botsForMatch;
            foreach (var entry in selected)
            {
                state.MatchmakingEntries.TryRemove(entry.Id, out _);
                waiting.Remove(entry);
            }
            if (selected.Count == 0) break;
        }

        var remaining = state.MatchmakingEntries.Values.Where(entry => string.Equals(entry.QueueId, id, StringComparison.OrdinalIgnoreCase)).Sum(entry => entry.PartySize);
        audit.Write("Information", "matchmaking", $"Ran queue {id}: {gameIds.Count} matches, {playersMatched} players, {botsAdded} bots.", actorId);
        return new MatchmakingRunResult(id, gameIds.Count, playersMatched, botsAdded, remaining, gameIds);
    }

    public IReadOnlyList<LiveOpsCampaign> Campaigns() => state.LiveOpsCampaigns.Values.OrderBy(campaign => campaign.StartsAt).ToList();

    public LiveOpsCampaign SaveCampaign(LiveOpsCampaignRequest request, string actorId)
    {
        ValidateIdentity(request.Id, request.Name, "Campaign");
        if (request.EndsAt <= request.StartsAt)
            throw new InvalidOperationException("Campaign end time must be after its start time.");
        var campaign = new LiveOpsCampaign(
            request.Id.Trim(),
            request.Name.Trim(),
            Required(request.Kind, "Campaign type"),
            Required(request.Description, "Description"),
            request.StartsAt,
            request.EndsAt,
            request.Active,
            DateTimeOffset.UtcNow);
        state.LiveOpsCampaigns[campaign.Id] = campaign;
        audit.Write("Information", "liveops", $"Saved {campaign.Kind} campaign {campaign.Id}.", actorId);
        return campaign;
    }

    public bool RemoveCampaign(string id, string actorId)
    {
        if (!state.LiveOpsCampaigns.TryRemove(id, out _)) return false;
        audit.Write("Information", "liveops", $"Removed campaign {id}.", actorId);
        return true;
    }

    public IReadOnlyList<RewardCampaign> Rewards() => state.RewardCampaigns.Values.OrderBy(reward => reward.Name).ToList();

    public RewardCampaign SaveReward(RewardCampaignRequest request, string actorId)
    {
        ValidateIdentity(request.Id, request.Name, "Reward campaign");
        if (request.Amount is < 1 or > 1_000_000)
            throw new InvalidOperationException("Reward amount must be between 1 and 1,000,000.");
        if (request.DistributionLimit is <= 0)
            throw new InvalidOperationException("Distribution limit must be positive when provided.");
        var reward = new RewardCampaign(
            request.Id.Trim(),
            request.Name.Trim(),
            Required(request.Trigger, "Trigger"),
            Required(request.RewardType, "Reward type"),
            request.Amount,
            request.DistributionLimit,
            request.Active,
            DateTimeOffset.UtcNow);
        state.RewardCampaigns[reward.Id] = reward;
        audit.Write("Information", "liveops", $"Saved reward campaign {reward.Id}.", actorId);
        return reward;
    }

    public bool RemoveReward(string id, string actorId)
    {
        if (!state.RewardCampaigns.TryRemove(id, out _)) return false;
        audit.Write("Information", "liveops", $"Removed reward campaign {id}.", actorId);
        return true;
    }

    public IReadOnlyList<ManagedAchievement> Achievements() => state.ManagedAchievements.Values.OrderBy(achievement => achievement.Name).ToList();

    public ManagedAchievement SaveAchievement(ManagedAchievementRequest request, string actorId)
    {
        ValidateIdentity(request.Id, request.Name, "Achievement");
        if (request.Points is < 0 or > 100_000)
            throw new InvalidOperationException("Achievement points must be between 0 and 100,000.");
        var achievement = new ManagedAchievement(
            request.Id.Trim(),
            request.Name.Trim(),
            Required(request.Description, "Description"),
            request.Points,
            request.Active,
            DateTimeOffset.UtcNow);
        state.ManagedAchievements[achievement.Id] = achievement;
        audit.Write("Information", "liveops", $"Saved achievement {achievement.Id}.", actorId);
        return achievement;
    }

    public bool RemoveAchievement(string id, string actorId)
    {
        if (!state.ManagedAchievements.TryRemove(id, out _)) return false;
        audit.Write("Information", "liveops", $"Removed achievement {id}.", actorId);
        return true;
    }

    public IReadOnlyList<StoreProduct> Products() => state.StoreProducts.Values.OrderBy(product => product.Name).ToList();

    public StoreProduct SaveProduct(StoreProductRequest request, string actorId)
    {
        ValidateIdentity(request.Id, request.Name, "Store product");
        if (request.Price < 0 || request.Price > 1_000_000m)
            throw new InvalidOperationException("Store price must be between 0 and 1,000,000.");
        var product = new StoreProduct(
            request.Id.Trim(),
            request.Name.Trim(),
            Required(request.Description, "Description"),
            request.Price,
            Required(request.Currency, "Currency").ToLowerInvariant(),
            Optional(request.AssetId),
            Optional(request.ContentPackId),
            request.Active,
            DateTimeOffset.UtcNow);
        state.StoreProducts[product.Id] = product;
        audit.Write("Information", "commerce", $"Saved store product {product.Id}.", actorId);
        return product;
    }

    public bool RemoveProduct(string id, string actorId)
    {
        if (!state.StoreProducts.TryRemove(id, out _)) return false;
        audit.Write("Information", "commerce", $"Removed store product {id}.", actorId);
        return true;
    }

    public EconomyConfiguration Economy() => state.Economy;

    public EconomyConfiguration SaveEconomy(EconomyConfigurationRequest request, string actorId)
    {
        var softCurrency = Required(request.SoftCurrency, "Soft currency").ToLowerInvariant();
        var premiumCurrency = Required(request.PremiumCurrency, "Premium currency").ToLowerInvariant();
        if (softCurrency.Equals(premiumCurrency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Soft and premium currency must be different.");
        if (request.DailySoftCurrencyCap is < 0 or > 1_000_000 || request.StoreTaxPercent is < 0 or > 100)
            throw new InvalidOperationException("Economy values are out of range.");
        var economy = new EconomyConfiguration(softCurrency, premiumCurrency, request.DailySoftCurrencyCap, request.StoreTaxPercent, DateTimeOffset.UtcNow);
        state.Economy = economy;
        audit.Write("Information", "economy", "Updated economy configuration.", actorId);
        return economy;
    }

    public IReadOnlyList<EarlyWarningAlert> Warnings()
    {
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<EarlyWarningAlert>();
        if (!state.BotProfiles.Values.Any(bot => bot.Enabled))
            warnings.Add(Alert("bot-coverage", "warning", "AI fleet", "No bots are enabled", "Matchmaking has no enabled AI profiles to draw from.", now));
        if (!state.LiveOpsCampaigns.Values.Any(campaign => campaign.Active && campaign.StartsAt <= now && campaign.EndsAt >= now))
            warnings.Add(Alert("liveops-gap", "info", "LiveOps", "No campaign is live", "There is no active event, season, or quest campaign at the current time.", now));
        if (state.RewardCampaigns.Values.Any(reward => reward.Active && reward.DistributionLimit is null))
            warnings.Add(Alert("reward-limit", "warning", "Economy", "Unlimited reward campaign", "At least one active reward has no distribution limit.", now));
        if (state.StoreProducts.Values.Count(product => product.Active) == 0)
            warnings.Add(Alert("store-empty", "info", "Commerce", "No active store products", "The in-game store currently has no products available for sale.", now));
        if (state.Users.Values.Count(user => user.Disabled) >= 5)
            warnings.Add(Alert("moderation-volume", "warning", "Moderation", "Elevated disabled accounts", "Five or more accounts are currently disabled. Review recent moderation activity.", now));
        if (state.Settings.MaintenanceMode)
            warnings.Add(Alert("maintenance", "warning", "Platform", "Maintenance mode is on", "Public health reports maintenance until an operator turns it off in platform settings.", now));
        if (state.Investigations.Values.Any(item => item.Status is not "closed" && item.Severity is "high" or "critical"))
            warnings.Add(Alert("open-investigations", "warning", "Moderation", "High-severity investigations are open", "Review the investigations workspace before the next live event.", now));
        return warnings;
    }

    public OperationsSummary Summary() => new(
        state.BotProfiles.Count,
        state.BotProfiles.Values.Count(bot => bot.Enabled),
        state.LiveOpsCampaigns.Count,
        state.LiveOpsCampaigns.Values.Count(campaign => campaign.Active),
        state.RewardCampaigns.Count,
        state.StoreProducts.Values.Count(product => product.Active),
        Warnings().Count);

    private static EarlyWarningAlert Alert(string id, string severity, string category, string title, string detail, DateTimeOffset detectedAt) =>
        new(id, severity, category, title, detail, detectedAt);

    private static void ValidateIdentity(string id, string name, string type)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Trim().Length > 80 || id.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidOperationException($"{type} ID must be up to 80 letters, numbers, hyphens, or underscores.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new InvalidOperationException($"{type} name is required and must be at most 120 characters.");
    }

    private static string Required(string value, string label) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"{label} is required.")
        : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
