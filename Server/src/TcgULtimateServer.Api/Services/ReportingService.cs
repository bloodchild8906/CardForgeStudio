using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed record ReportFile(string FileName, string ContentType, byte[] Content);

public sealed class ReportingService(AppState state, AuditLogService audit, OperationsService operations)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ReportFile CreateReport(string reportType, string format)
    {
        var normalizedType = NormalizeReportType(reportType);
        var normalizedFormat = format.Trim().ToLowerInvariant();
        return normalizedFormat switch
        {
            "json" => new ReportFile($"tcg-{normalizedType}-report.json", "application/json", JsonSerializer.SerializeToUtf8Bytes(Snapshot(normalizedType), JsonOptions)),
            "csv" => new ReportFile($"tcg-{normalizedType}-report.csv", "text/csv", Encoding.UTF8.GetBytes(Csv(normalizedType))),
            _ => throw new InvalidOperationException("Report format must be json or csv.")
        };
    }

    public ArchiveDescriptor CreateArchive(string scope, string actorId)
    {
        var normalizedScope = NormalizeReportType(scope);
        var createdAt = DateTimeOffset.UtcNow;
        var snapshot = JsonSerializer.SerializeToUtf8Bytes(Snapshot(normalizedScope), JsonOptions);
        var snapshotHash = Convert.ToHexString(SHA256.HashData(snapshot));
        byte[] archiveContent;
        using (var output = new MemoryStream())
        {
            using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteZipEntry(package, "snapshot.json", snapshot);
                WriteZipEntry(package, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 1,
                    scope = normalizedScope,
                    createdAt,
                    snapshotSha256 = snapshotHash
                }, JsonOptions));
            }
            archiveContent = output.ToArray();
        }

        var archiveId = Guid.NewGuid().ToString("N");
        var archiveHash = Convert.ToHexString(SHA256.HashData(archiveContent));
        var archive = new GeneratedArchive(archiveId, $"tcg-{normalizedScope}-{createdAt:yyyyMMdd-HHmmss}.zip", normalizedScope, archiveContent, archiveHash, createdAt);
        state.Archives[archive.Id] = archive;
        audit.Write("Information", "archive", $"Created {normalizedScope} archive {archive.Name}.", actorId);
        return Describe(archive);
    }

    public IReadOnlyList<ArchiveDescriptor> Archives() => state.Archives.Values
        .OrderByDescending(archive => archive.CreatedAt)
        .Select(Describe)
        .ToList();

    public GeneratedArchive GetArchive(string id) => state.Archives.TryGetValue(id, out var archive)
        ? archive
        : throw new KeyNotFoundException("Archive not found.");

    public ArchiveVerification Verify(string id)
    {
        var archive = GetArchive(id);
        var actualHash = Convert.ToHexString(SHA256.HashData(archive.Content));
        return new ArchiveVerification(archive.Id, string.Equals(archive.Sha256, actualHash, StringComparison.OrdinalIgnoreCase), actualHash, archive.Content.LongLength, DateTimeOffset.UtcNow);
    }

    private object Snapshot(string reportType) => reportType switch
    {
        "players" => state.Users.Values.OrderBy(user => user.Handle).Select(user => new { user.Id, user.Handle, user.Rating, user.Role, user.Disabled }).ToList(),
        "games" => state.Games.Values.OrderByDescending(game => game.Events.LastOrDefault()?.At).Select(game => new
        {
            game.Id,
            game.RulePackId,
            Players = game.Players,
            game.Turn,
            game.Finished,
            EventCount = game.Events.Count,
            ChatMessages = state.Chat.TryGetValue(game.Id, out var messages) ? messages.Count : 0
        }).ToList(),
        "content" => new
        {
            RulePacks = state.RulePacks.Values.OrderBy(pack => pack.Name).Select(pack => new { pack.Id, pack.Name, pack.Version, pack.Description, CardCount = pack.Cards.Count }).ToList(),
            Assets = state.Assets.Values.OrderBy(asset => asset.Name).Select(asset => new { asset.Id, asset.Name, asset.ContentType, Length = asset.Content.LongLength, asset.Sha256 }).ToList()
        },
        "bots" => new { Profiles = BotsForReport(), Simulations = state.BotSimulationRuns.Values.OrderByDescending(run => run.CompletedAt).ToList() },
        "matchmaking" => new { Queues = operations.MatchmakingQueues(), Entries = operations.MatchmakingEntries() },
        "liveops" => new { Campaigns = operations.Campaigns(), Rewards = operations.Rewards(), Achievements = operations.Achievements() },
        "economy" => new { Economy = operations.Economy(), Products = operations.Products(), Transactions = state.CurrencyTransactions.Values.OrderByDescending(transaction => transaction.CreatedAt).ToList() },
        "audit" => audit.Recent(2_000),
        "full" => new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Players = state.Users.Values.OrderBy(user => user.Handle).Select(user => new { user.Id, user.Handle, user.Rating, user.Role, user.Disabled }).ToList(),
            Games = state.Games.Values.ToList(),
            Content = Snapshot("content"),
            Bots = Snapshot("bots"),
            Matchmaking = Snapshot("matchmaking"),
            LiveOps = Snapshot("liveops"),
            Economy = Snapshot("economy"),
            Audit = audit.Recent(2_000),
            Configuration = Snapshot("configuration")
        },
        "configuration" => new
        {
            Bots = state.BotProfiles.Values.OrderBy(bot => bot.Name).ToList(),
            Queues = state.MatchmakingQueues.Values.OrderBy(queue => queue.Name).ToList(),
            Campaigns = state.LiveOpsCampaigns.Values.OrderBy(campaign => campaign.Name).ToList(),
            Rewards = state.RewardCampaigns.Values.OrderBy(reward => reward.Name).ToList(),
            Achievements = state.ManagedAchievements.Values.OrderBy(achievement => achievement.Name).ToList(),
            Economy = state.Economy,
            Products = state.StoreProducts.Values.OrderBy(product => product.Name).ToList(),
            Plugins = state.Plugins.Values.OrderBy(plugin => plugin.Name).ToList(),
            Settings = state.Settings,
            Schedules = state.ReportSchedules.Values.OrderBy(schedule => schedule.Name).ToList(),
            Retention = state.Retention,
            Investigations = state.Investigations.Values.OrderBy(item => item.OpenedAt).ToList(),
            Appeals = state.Appeals.Values.OrderBy(item => item.SubmittedAt).ToList()
        },
        _ => throw new InvalidOperationException("Unknown report type.")
    };

    private List<object> BotsForReport() => state.BotProfiles.Values
        .OrderBy(bot => bot.Name)
        .Select(bot => (object)new
        {
            bot.Id,
            bot.Name,
            bot.Personality,
            bot.Difficulty,
            bot.ModelVersion,
            bot.MatchmakingPool,
            bot.Enabled,
            bot.DailyMatchLimit,
            bot.UpdatedAt
        })
        .ToList();

    private string Csv(string reportType)
    {
        var rows = reportType switch
        {
            "players" => state.Users.Values.OrderBy(user => user.Handle).Select(user => new[] { user.Id, user.Handle, user.Rating.ToString(CultureInfo.InvariantCulture), user.Role, user.Disabled.ToString() }),
            "games" => state.Games.Values.OrderBy(game => game.Id).Select(game => new[] { game.Id, game.RulePackId, string.Join(";", game.Players), game.Turn.ToString(CultureInfo.InvariantCulture), game.Finished.ToString(), game.Events.Count.ToString(CultureInfo.InvariantCulture) }),
            "bots" => state.BotProfiles.Values.OrderBy(bot => bot.Name).Select(bot => new[] { bot.Id, bot.Name, bot.Personality, bot.Difficulty, bot.ModelVersion, bot.MatchmakingPool, bot.Enabled.ToString(), bot.DailyMatchLimit.ToString(CultureInfo.InvariantCulture), bot.UpdatedAt.ToString("O") }),
            "matchmaking" => state.MatchmakingQueues.Values.OrderBy(queue => queue.Name).Select(queue => new[] { queue.Id, queue.Name, queue.Mode, queue.MinPlayers.ToString(CultureInfo.InvariantCulture), queue.MaxPlayers.ToString(CultureInfo.InvariantCulture), queue.InitialRatingRange.ToString(CultureInfo.InvariantCulture), queue.RatingExpansionPerSecond.ToString(CultureInfo.InvariantCulture), queue.MaxWaitSeconds.ToString(CultureInfo.InvariantCulture), queue.BotBackfill.ToString(), queue.Enabled.ToString(), state.MatchmakingEntries.Values.Count(entry => string.Equals(entry.QueueId, queue.Id, StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture) }),
            "liveops" => state.LiveOpsCampaigns.Values.OrderBy(campaign => campaign.StartsAt).Select(campaign => new[] { campaign.Id, campaign.Name, campaign.Kind, campaign.Description, campaign.StartsAt.ToString("O"), campaign.EndsAt.ToString("O"), campaign.Active.ToString() }),
            "economy" => state.StoreProducts.Values.OrderBy(product => product.Name).Select(product => new[] { product.Id, product.Name, product.Description, product.Price.ToString(CultureInfo.InvariantCulture), product.Currency, product.Active.ToString(), product.UpdatedAt.ToString("O") }),
            "audit" => audit.Recent(2_000).Select(entry => new[] { entry.At.ToString("O"), entry.Level, entry.Category, entry.Message, entry.ActorId ?? string.Empty, entry.RequestPath ?? string.Empty, entry.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty }),
            "content" => state.RulePacks.Values.OrderBy(pack => pack.Name).Select(pack => new[] { pack.Id, pack.Name, pack.Version, pack.Description, pack.Cards.Count.ToString(CultureInfo.InvariantCulture) }),
            "full" or "configuration" => new[] { new[] { "This snapshot is available as JSON or a ZIP archive." } },
            _ => throw new InvalidOperationException("Unknown report type.")
        };
        var headers = reportType switch
        {
            "players" => new[] { "id", "handle", "rating", "role", "disabled" },
            "games" => new[] { "id", "rulePackId", "players", "turn", "finished", "eventCount" },
            "bots" => new[] { "id", "name", "personality", "difficulty", "modelVersion", "matchmakingPool", "enabled", "dailyMatchLimit", "updatedAt" },
            "matchmaking" => new[] { "id", "name", "mode", "minPlayers", "maxPlayers", "initialRatingRange", "ratingExpansionPerSecond", "maxWaitSeconds", "botBackfill", "enabled", "waitingParties" },
            "liveops" => new[] { "id", "name", "kind", "description", "startsAt", "endsAt", "active" },
            "economy" => new[] { "id", "name", "description", "price", "currency", "active", "updatedAt" },
            "audit" => new[] { "at", "level", "category", "message", "actorId", "requestPath", "statusCode" },
            "content" => new[] { "id", "name", "version", "description", "cardCount" },
            "full" or "configuration" => new[] { "notice" },
            _ => throw new InvalidOperationException("Unknown report type.")
        };
        return string.Join(Environment.NewLine, new[] { CsvLine(headers) }.Concat(rows.Select(CsvLine)));
    }

    private static ArchiveDescriptor Describe(GeneratedArchive archive) => new(archive.Id, archive.Name, archive.Scope, archive.Content.LongLength, archive.Sha256, archive.CreatedAt);

    private static void WriteZipEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string NormalizeReportType(string reportType)
    {
        var value = reportType.Trim().ToLowerInvariant();
        return value is "players" or "games" or "content" or "bots" or "matchmaking" or "liveops" or "economy" or "audit" or "configuration" or "full"
            ? value
            : throw new InvalidOperationException("Report type is not supported.");
    }

    private static string CsvLine(IEnumerable<string> fields) => string.Join(",", fields.Select(value => $"\"{value.Replace("\"", "\"\"")}\""));
}
