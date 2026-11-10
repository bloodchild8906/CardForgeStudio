using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class GovernanceService(AppState state, AuditLogService audit, ReportingService reporting)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static readonly string[] ReportTypes = ["players", "games", "content", "bots", "matchmaking", "liveops", "economy", "audit", "configuration", "full"];
    private static readonly string[] Cadences = ["hourly", "daily", "weekly"];
    private static readonly string[] PluginKinds = ["content", "liveops", "commerce", "moderation", "telemetry"];

    public IReadOnlyList<InvestigationCase> Investigations() => state.Investigations.Values.OrderByDescending(item => item.UpdatedAt).ToList();

    public InvestigationCase SaveInvestigation(InvestigationRequest request, string actorId)
    {
        ValidateIdentity(request.Id, "Investigation");
        RequireUser(request.SubjectUserId);
        var category = Required(request.Category, "Category");
        var summary = Required(request.Summary, "Summary");
        if (summary.Length > 500) throw new InvalidOperationException("Investigation summary must be at most 500 characters.");
        var severity = Normalize(request.Severity, ["low", "medium", "high", "critical"], "Severity");
        var status = Normalize(request.Status, ["open", "investigating", "closed"], "Investigation status");
        var openedAt = state.Investigations.TryGetValue(request.Id.Trim(), out var existing) ? existing.OpenedAt : DateTimeOffset.UtcNow;
        var item = new InvestigationCase(request.Id.Trim(), request.SubjectUserId.Trim(), category, summary, status, severity, openedAt, DateTimeOffset.UtcNow);
        state.Investigations[item.Id] = item;
        audit.Write("Information", "moderation", $"Saved investigation {item.Id} for {item.SubjectUserId}.", actorId);
        return item;
    }

    public bool RemoveInvestigation(string id, string actorId)
    {
        if (!state.Investigations.TryRemove(id, out _)) return false;
        audit.Write("Information", "moderation", $"Removed investigation {id}.", actorId);
        return true;
    }

    public IReadOnlyList<PlayerAppeal> Appeals() => state.Appeals.Values.OrderByDescending(item => item.UpdatedAt).ToList();

    public PlayerAppeal SaveAppeal(AppealRequest request, string actorId)
    {
        ValidateIdentity(request.Id, "Appeal");
        RequireUser(request.UserId);
        var subject = Required(request.Subject, "Subject");
        var statement = Required(request.Statement, "Statement");
        if (statement.Length > 2000) throw new InvalidOperationException("Appeal statement must be at most 2,000 characters.");
        var submittedAt = state.Appeals.TryGetValue(request.Id.Trim(), out var existing) ? existing.SubmittedAt : DateTimeOffset.UtcNow;
        var status = existing?.Status ?? "pending";
        var item = new PlayerAppeal(request.Id.Trim(), request.UserId.Trim(), subject, statement, status, existing?.DecisionNote, submittedAt, DateTimeOffset.UtcNow);
        state.Appeals[item.Id] = item;
        audit.Write("Information", "moderation", $"Saved appeal {item.Id} for {item.UserId}.", actorId);
        return item;
    }

    public PlayerAppeal DecideAppeal(string id, AppealDecisionRequest request, string actorId)
    {
        if (!state.Appeals.TryGetValue(id, out var appeal)) throw new KeyNotFoundException("Appeal not found.");
        var status = Normalize(request.Status, ["pending", "approved", "denied", "needs-info"], "Appeal status");
        var note = Required(request.DecisionNote, "Decision note");
        if (note.Length > 500) throw new InvalidOperationException("Decision note must be at most 500 characters.");
        var updated = appeal with { Status = status, DecisionNote = note, UpdatedAt = DateTimeOffset.UtcNow };
        state.Appeals[id] = updated;
        audit.Write("Information", "moderation", $"Recorded {status} decision for appeal {id}.", actorId);
        return updated;
    }

    public bool RemoveAppeal(string id, string actorId)
    {
        if (!state.Appeals.TryRemove(id, out _)) return false;
        audit.Write("Information", "moderation", $"Removed appeal {id}.", actorId);
        return true;
    }

    public IReadOnlyList<ReportSchedule> Schedules() => state.ReportSchedules.Values.OrderBy(item => item.Name).ToList();

    public ReportSchedule SaveSchedule(ReportScheduleRequest request, string actorId)
    {
        ValidateIdentity(request.Id, "Report schedule");
        var name = Required(request.Name, "Schedule name");
        var reportType = Normalize(request.ReportType, ReportTypes, "Report type");
        var format = Normalize(request.Format, ["json", "csv"], "Report format");
        if (reportType is "full" or "configuration" && format == "csv")
            throw new InvalidOperationException("Configuration and full reports must use JSON.");
        var cadence = Normalize(request.Cadence, Cadences, "Cadence");
        var existing = state.ReportSchedules.TryGetValue(request.Id.Trim(), out var current) ? current : null;
        var schedule = new ReportSchedule(request.Id.Trim(), name, reportType, format, cadence, request.Enabled, existing?.NextRunAt ?? NextRun(cadence), existing?.LastRunAt, DateTimeOffset.UtcNow);
        state.ReportSchedules[schedule.Id] = schedule;
        audit.Write("Information", "reports", $"Saved report schedule {schedule.Id}.", actorId);
        return schedule;
    }

    public bool RemoveSchedule(string id, string actorId)
    {
        if (!state.ReportSchedules.TryRemove(id, out _)) return false;
        audit.Write("Information", "reports", $"Removed report schedule {id}.", actorId);
        return true;
    }

    public ArchiveDescriptor RunSchedule(string id, string actorId)
    {
        if (!state.ReportSchedules.TryGetValue(id, out var schedule)) throw new KeyNotFoundException("Report schedule not found.");
        if (!schedule.Enabled) throw new InvalidOperationException("Enable the schedule before running it.");
        var archive = reporting.CreateArchive(schedule.ReportType, actorId);
        state.ReportSchedules[id] = schedule with { LastRunAt = DateTimeOffset.UtcNow, NextRunAt = NextRun(schedule.Cadence), UpdatedAt = DateTimeOffset.UtcNow };
        audit.Write("Information", "reports", $"Ran schedule {id} and created archive {archive.Name}.", actorId);
        return archive;
    }

    public RetentionPolicy Retention() => state.Retention;

    public RetentionPolicy SaveRetention(RetentionPolicyRequest request, string actorId)
    {
        ValidateRetention(request);
        var policy = new RetentionPolicy(request.ArchiveDays, request.AuditLogDays, request.ChatDays, request.AutoPurgeEnabled, DateTimeOffset.UtcNow);
        state.Retention = policy;
        audit.Write("Information", "retention", "Updated retention policy.", actorId);
        return policy;
    }

    public RetentionRunResult ApplyRetention(string actorId)
    {
        var policy = state.Retention;
        ValidateRetention(new RetentionPolicyRequest(policy.ArchiveDays, policy.AuditLogDays, policy.ChatDays, policy.AutoPurgeEnabled));
        var now = DateTimeOffset.UtcNow;
        var archivesRemoved = RemoveOlder(state.Archives, now.AddDays(-policy.ArchiveDays), archive => archive.CreatedAt);
        var messagesRemoved = RemoveOlder(state.DirectMessages, now.AddDays(-policy.ChatDays), message => message.SentAt);
        var auditRemoved = audit.PurgeOlderThan(now.AddDays(-policy.AuditLogDays));
        audit.Write("Information", "retention", $"Applied retention · {archivesRemoved} archives · {messagesRemoved} messages · {auditRemoved} audit entries.", actorId);
        return new RetentionRunResult(archivesRemoved, messagesRemoved, auditRemoved, now);
    }

    public IReadOnlyList<PluginRecord> Plugins() => state.Plugins.Values.OrderBy(plugin => plugin.Name).ToList();

    public PluginRecord SavePlugin(PluginRequest request, string actorId)
    {
        ValidateIdentity(request.Id, "Plugin");
        var name = Required(request.Name, "Plugin name");
        var version = Required(request.Version, "Plugin version");
        var kind = Normalize(request.Kind, PluginKinds, "Plugin kind");
        var plugin = new PluginRecord(request.Id.Trim(), name, version, kind, request.Enabled, DateTimeOffset.UtcNow);
        state.Plugins[plugin.Id] = plugin;
        audit.Write("Information", "platform", $"Saved plugin {plugin.Id}.", actorId);
        return plugin;
    }

    public bool RemovePlugin(string id, string actorId)
    {
        if (!state.Plugins.TryRemove(id, out _)) return false;
        audit.Write("Information", "platform", $"Removed plugin {id}.", actorId);
        return true;
    }

    public SystemSettings Settings() => state.Settings;

    public SystemSettings SaveSettings(SystemSettingsRequest request, string actorId)
    {
        var environment = Required(request.EnvironmentName, "Environment name");
        if (environment.Length > 40) throw new InvalidOperationException("Environment name must be at most 40 characters.");
        if (request.SessionTimeoutMinutes is < 5 or > 1_440) throw new InvalidOperationException("Session timeout must be between 5 and 1,440 minutes.");
        if (request.MaxUploadMegabytes is < 1 or > 512) throw new InvalidOperationException("Maximum upload size must be between 1 and 512 MB.");
        var settings = new SystemSettings(environment, request.SessionTimeoutMinutes, request.MaxUploadMegabytes, request.MaintenanceMode, request.BotBackfillDefault, DateTimeOffset.UtcNow);
        state.Settings = settings;
        audit.Write("Information", "platform", $"Updated system settings · maintenance {(settings.MaintenanceMode ? "on" : "off")}.", actorId);
        return settings;
    }

    public ConfigurationRestoreResult Restore(string archiveId, string actorId)
    {
        var verification = reporting.Verify(archiveId);
        if (!verification.IsValid) throw new InvalidOperationException("Archive integrity check failed. Restore was not applied.");
        var archive = reporting.GetArchive(archiveId);
        using var package = new ZipArchive(new MemoryStream(archive.Content), ZipArchiveMode.Read);
        var entry = package.GetEntry("snapshot.json") ?? throw new InvalidOperationException("The archive does not contain snapshot.json.");
        using var stream = entry.Open();
        var root = JsonSerializer.Deserialize<JsonElement>(stream, JsonOptions);
        var configuration = root.TryGetProperty("configuration", out var nested) ? nested : root;
        var notes = new List<string> { "Player accounts, passwords, and active games were left unchanged." };
        var bots = Replace(state.BotProfiles, ReadList<BotProfile>(configuration, "bots"), item => item.Id);
        var queues = Replace(state.MatchmakingQueues, ReadList<MatchmakingQueueConfiguration>(configuration, "queues"), item => item.Id);
        var campaigns = Replace(state.LiveOpsCampaigns, ReadList<LiveOpsCampaign>(configuration, "campaigns"), item => item.Id);
        var rewards = Replace(state.RewardCampaigns, ReadList<RewardCampaign>(configuration, "rewards"), item => item.Id);
        var achievements = Replace(state.ManagedAchievements, ReadList<ManagedAchievement>(configuration, "achievements"), item => item.Id);
        var products = Replace(state.StoreProducts, ReadList<StoreProduct>(configuration, "products"), item => item.Id);
        var plugins = Replace(state.Plugins, ReadList<PluginRecord>(configuration, "plugins"), item => item.Id);
        var schedules = Replace(state.ReportSchedules, ReadList<ReportSchedule>(configuration, "schedules"), item => item.Id);
        if (configuration.TryGetProperty("economy", out var economyElement) && economyElement.ValueKind == JsonValueKind.Object)
            state.Economy = economyElement.Deserialize<EconomyConfiguration>(JsonOptions) ?? state.Economy;
        if (configuration.TryGetProperty("settings", out var settingsElement) && settingsElement.ValueKind == JsonValueKind.Object)
            state.Settings = settingsElement.Deserialize<SystemSettings>(JsonOptions) ?? state.Settings;
        if (configuration.TryGetProperty("retention", out var retentionElement) && retentionElement.ValueKind == JsonValueKind.Object)
            state.Retention = retentionElement.Deserialize<RetentionPolicy>(JsonOptions) ?? state.Retention;
        if (configuration.TryGetProperty("investigations", out _))
            Replace(state.Investigations, ReadList<InvestigationCase>(configuration, "investigations"), item => item.Id);
        if (configuration.TryGetProperty("appeals", out _))
            Replace(state.Appeals, ReadList<PlayerAppeal>(configuration, "appeals"), item => item.Id);
        if (bots + queues + campaigns + products + plugins == 0)
            notes.Add("This archive had no configuration records. Create a configuration or full archive to capture bots, queues, LiveOps, store, plugins, and settings.");
        audit.Write("Information", "archive", $"Restored configuration from archive {archive.Name}.", actorId);
        return new ConfigurationRestoreResult(archive.Id, archive.Scope, bots, queues, campaigns, rewards, achievements, products, plugins, schedules, notes, DateTimeOffset.UtcNow);
    }

    private static List<T> ReadList<T>(JsonElement configuration, string name) =>
        configuration.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.Deserialize<List<T>>(JsonOptions) ?? []
            : [];

    private static int Replace<T>(ConcurrentDictionary<string, T> target, List<T> items, Func<T, string> id)
    {
        if (items.Count == 0) return 0;
        foreach (var key in target.Keys.ToList()) target.TryRemove(key, out _);
        foreach (var item in items) target[id(item)] = item;
        return items.Count;
    }

    private static int RemoveOlder<T>(ConcurrentDictionary<string, T> target, DateTimeOffset cutoff, Func<T, DateTimeOffset> timestamp)
    {
        var removed = 0;
        foreach (var entry in target.Where(entry => timestamp(entry.Value) < cutoff).Select(entry => entry.Key).ToList())
        {
            if (target.TryRemove(entry, out _)) removed++;
        }
        return removed;
    }

    private void RequireUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || !state.Users.ContainsKey(userId.Trim()))
            throw new InvalidOperationException("Select a registered player.");
    }

    private static DateTimeOffset NextRun(string cadence) => cadence switch
    {
        "hourly" => DateTimeOffset.UtcNow.AddHours(1),
        "weekly" => DateTimeOffset.UtcNow.AddDays(7),
        _ => DateTimeOffset.UtcNow.AddDays(1)
    };

    private static void ValidateRetention(RetentionPolicyRequest request)
    {
        if (request.ArchiveDays is < 1 or > 3650 || request.AuditLogDays is < 1 or > 3650 || request.ChatDays is < 1 or > 3650)
            throw new InvalidOperationException("Retention windows must be between 1 and 3,650 days.");
    }

    private static void ValidateIdentity(string id, string type)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Trim().Length > 80 || id.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidOperationException($"{type} ID must be up to 80 letters, numbers, hyphens, or underscores.");
    }

    private static string Required(string value, string label) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"{label} is required.")
        : value.Trim();

    private static string Normalize(string value, IEnumerable<string> allowed, string label)
    {
        var match = allowed.FirstOrDefault(item => item.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException($"{label} is not supported.");
    }
}
