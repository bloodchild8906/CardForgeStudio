using System.Collections.Concurrent;
using System.Text.Json;
using CardForge.GamePackages;

namespace TcgULtimateServer.Api.Models;

public record User(string Id, string Handle, string PasswordHash, int Rating = 1000, string Role = "user", bool Disabled = false);
public record PublicUser(string Id, string Handle, int Rating, string Role = "user", bool Disabled = false);
public record RegisterRequest(string Handle, string Password);
public record LoginRequest(string Handle, string Password);
public record AuthResponse(string Token, PublicUser User);
public record PlayerPresence(string Id, string Handle, int Rating, DateTimeOffset LastSeenAt);
public record PresenceSnapshot(int OnlinePlayers, List<PlayerPresence> Players, DateTimeOffset GeneratedAt);
public record AdminUserView(string Id, string Handle, int Rating, string Role, bool Disabled);
public record UpdateUserRequest(string? Role = null, bool? Disabled = null);
public record AuditLogEntry(DateTimeOffset At, string Level, string Category, string Message, string? ActorId = null, string? RequestPath = null, int? StatusCode = null);
public record AdminSummary(
    int Users,
    int DisabledUsers,
    int Games,
    int StateMachines,
    int Assets,
    int RecentLogs,
    int FriendConnections = 0,
    int Messages = 0,
    int Decks = 0,
    int StoreProducts = 0,
    int ContentPacks = 0,
    int MatchmakingQueues = 0,
    int WaitingPlayers = 0,
    int CurrencyTransactions = 0);
public record TcgZoneDefinition(
    string Id,
    string Name,
    string OwnerScope = "player",
    string Visibility = "owner",
    bool Ordered = false,
    int? Capacity = null);
public record TcgBoardZonePlacement(
    string Id, string ZoneId, string Side, string Label,
    float X, float Y, float Width, float Height, int Order = 0);
public record TcgResourceDefinition(
    string Id,
    string Name,
    string Scope = "player",
    long InitialValue = 0,
    long MinimumValue = 0,
    long? MaximumValue = null);
public record TcgConditionDefinition(
    string Kind,
    string? Subject = null,
    string? Key = null,
    string? Operator = null,
    JsonElement? Value = null,
    Dictionary<string, JsonElement>? Parameters = null);
public record TcgEffectDefinition(
    string Kind,
    string? Target = null,
    string? ResourceId = null,
    string? FromZone = null,
    string? ToZone = null,
    long Amount = 0,
    string? EventType = null,
    string? Message = null,
    Dictionary<string, JsonElement>? Parameters = null);
public record TcgActionDefinition(
    string Id,
    string Name,
    string? SourceZone = null,
    string? DestinationZone = null,
    bool RequiresActivePlayer = true,
    Dictionary<string, long>? Costs = null,
    List<TcgConditionDefinition>? Conditions = null,
    List<TcgEffectDefinition>? Effects = null,
    Dictionary<string, JsonElement>? Metadata = null);
public record TcgTriggerDefinition(
    string Id,
    string Name,
    List<string> CauseActionIds,
    List<string> ResponseActionIds,
    int Priority = 0);
public record TcgPhaseDefinition(
    string Id,
    string Name,
    List<string> AllowedActions,
    bool AutomaticAdvance = false,
    Dictionary<string, JsonElement>? Metadata = null);
public record TcgAbilityDefinition(
    string Id,
    string Name,
    string Trigger,
    List<TcgConditionDefinition>? Conditions = null,
    List<TcgEffectDefinition>? Effects = null,
    Dictionary<string, JsonElement>? Metadata = null);
public record ExecuteGameActionRequest(
    string ActionId,
    string? SourceInstanceId = null,
    string? TargetPlayerId = null,
    string? TargetInstanceId = null,
    Dictionary<string, JsonElement>? Parameters = null);
public record RulePackValidationResult(bool Valid, List<string> Errors, List<string> Warnings, RulePack? Normalized = null);

public record CardDefinition(string Id, string Name, string Type, int Cost, int Power, int Toughness, string Text)
{
    public string? AssetId { get; init; }
    public List<string> Tags { get; init; } = [];
    public Dictionary<string, JsonElement> Properties { get; init; } = [];
    public List<TcgAbilityDefinition> Abilities { get; init; } = [];
}
public record AssetDescriptor(string Id, string Name, string ContentType, long Length, string Sha256, string Url);
public record RulePack(string Id, string Name, string Version, string Description, List<CardDefinition> Cards, Dictionary<string, int> StartingRules)
{
    public string SchemaVersion { get; init; } = "2";
    public string GameSystem { get; init; } = "custom";
    public int MinimumPlayers { get; init; } = 2;
    public int MaximumPlayers { get; init; } = 2;
    public List<TcgZoneDefinition> Zones { get; init; } = [];
    public List<TcgBoardZonePlacement> BoardLayout { get; init; } = [];
    public List<TcgResourceDefinition> Resources { get; init; } = [];
    public List<TcgPhaseDefinition> Phases { get; init; } = [];
    public List<TcgActionDefinition> Actions { get; init; } = [];
    public List<TcgTriggerDefinition> Triggers { get; init; } = [];
    public List<string> StartingDeck { get; init; } = [];
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];
}
public record CardInstance(string InstanceId, string CardId, string OwnerId);
public record Achievement(string Id, string Name, string Description, int Points);
public record Inventory(List<CardInstance> Cards, List<string> Achievements);
public record PlayerWallet(Dictionary<string, long> Balances);
public record FriendConnection(string Id, string UserId, string FriendUserId, string Status, DateTimeOffset CreatedAt);
public record CreateFriendRequest(string FriendUserId);
public record DirectMessage(string Id, string SenderId, string RecipientId, string Body, DateTimeOffset SentAt, bool Flagged = false, bool Removed = false);
public record SendMessageRequest(string RecipientId, string Body);
public record ServerAnnouncementRequest(string Body);
public record UpdateMessageModerationRequest(bool? Flagged = null, bool? Removed = null);
public record AdminGameChatChannel(string GameId, List<string> Players, List<string> Messages, bool Finished);
public record AdminChatSnapshot(int OnlinePlayers, List<DirectMessage> DirectMessages, List<AdminGameChatChannel> GameChannels, DateTimeOffset GeneratedAt);
public record DeckDefinition(string Id, string OwnerId, string Name, string RulePackId, List<string> CardIds, DateTimeOffset UpdatedAt, bool Suspended = false);
public record SaveDeckRequest(string? Id, string Name, string RulePackId, List<string> CardIds);
public record UpdateDeckModerationRequest(bool Suspended);
public record GameRecordingView(string Id, string RulePackId, List<string> Players, int Turn, bool Finished, List<GameEvent> Events, List<string> Chat);
public record AdminPlayerRankingView(int Rank, int TotalPlayers, int Rating, decimal Percentile);
public record AdminDeckStatistics(string DeckId, string Name, int CardCount, int UniqueCards, decimal AverageCost, Dictionary<string, int> CardTypes, bool Suspended);
public record AdminPlayerProfileView(
    AdminUserView User,
    Inventory Inventory,
    PlayerWallet Wallet,
    List<CurrencyTransaction> Transactions,
    AdminPlayerRankingView Ranking,
    List<FriendConnection> Friends,
    List<DirectMessage> Messages,
    List<DeckDefinition> Decks,
    List<AdminDeckStatistics> DeckStatistics,
    List<GameRecordingView> Matches);
public record StoreProduct(
    string Id,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string? AssetId,
    string? ContentPackId,
    bool Active,
    DateTimeOffset UpdatedAt);
public record StoreProductRequest(string Id, string Name, string Description, decimal Price, string Currency, string? AssetId = null, string? ContentPackId = null, bool Active = true);
public record BotProfile(
    string Id,
    string Name,
    string Personality,
    string Difficulty,
    string ModelVersion,
    string MatchmakingPool,
    bool Enabled,
    int DailyMatchLimit,
    DateTimeOffset UpdatedAt,
    int InferenceBudgetTokens = 250_000,
    string? PreviousModelVersion = null);
public record BotProfileRequest(
    string Id,
    string Name,
    string Personality,
    string Difficulty,
    string ModelVersion,
    string MatchmakingPool,
    bool Enabled = true,
    int DailyMatchLimit = 1000,
    int InferenceBudgetTokens = 250_000);
public record BotSimulationRequest(int Matches = 500);
public record BotSimulationRun(
    string Id,
    string BotId,
    int Matches,
    decimal EstimatedWinRate,
    int AverageTurns,
    DateTimeOffset CompletedAt);
public record MatchmakingQueueConfiguration(
    string Id,
    string Name,
    string Mode,
    int MinPlayers,
    int MaxPlayers,
    int InitialRatingRange,
    int RatingExpansionPerSecond,
    int MaxWaitSeconds,
    bool BotBackfill,
    bool Enabled,
    DateTimeOffset UpdatedAt);
public record MatchmakingQueueRequest(
    string Id,
    string Name,
    string Mode,
    int MinPlayers = 2,
    int MaxPlayers = 2,
    int InitialRatingRange = 100,
    int RatingExpansionPerSecond = 10,
    int MaxWaitSeconds = 90,
    bool BotBackfill = true,
    bool Enabled = true);
public record MatchmakingQueueEntry(
    string Id,
    string QueueId,
    string UserId,
    int Rating,
    int PartySize,
    string Region,
    DateTimeOffset JoinedAt,
    int DeckPower = 500,
    string? DeckId = null,
    string? RulePackId = null);
public record JoinMatchmakingRequest(string QueueId, string Region = "global", int PartySize = 1, string? DeckId = null);
public record MatchmakingQueueStatus(MatchmakingQueueConfiguration Queue, int WaitingPlayers, int WaitingParties, int LongestWaitSeconds);
public record MatchmakingRunResult(string QueueId, int MatchesCreated, int PlayersMatched, int BotsAdded, int PlayersRemaining, List<string> GameIds);
public record LiveOpsCampaign(
    string Id,
    string Name,
    string Kind,
    string Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool Active,
    DateTimeOffset UpdatedAt);
public record LiveOpsCampaignRequest(
    string Id,
    string Name,
    string Kind,
    string Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool Active = true);
public record RewardCampaign(
    string Id,
    string Name,
    string Trigger,
    string RewardType,
    int Amount,
    int? DistributionLimit,
    bool Active,
    DateTimeOffset UpdatedAt);
public record RewardCampaignRequest(
    string Id,
    string Name,
    string Trigger,
    string RewardType,
    int Amount,
    int? DistributionLimit = null,
    bool Active = true);
public record ManagedAchievement(
    string Id,
    string Name,
    string Description,
    int Points,
    bool Active,
    DateTimeOffset UpdatedAt);
public record ManagedAchievementRequest(string Id, string Name, string Description, int Points, bool Active = true);
public record EconomyConfiguration(
    string SoftCurrency,
    string PremiumCurrency,
    int DailySoftCurrencyCap,
    decimal StoreTaxPercent,
    DateTimeOffset UpdatedAt);
public record EconomyConfigurationRequest(string SoftCurrency, string PremiumCurrency, int DailySoftCurrencyCap, decimal StoreTaxPercent);
public record EconomyAnalytics(int Products, int ActiveProducts, int Transactions, long SoftIssued, long PremiumIssued, decimal CatalogValue, DateTimeOffset GeneratedAt);
public record EconomySimulationRequest(int ActivePlayers, int Days, int PurchasesPerPlayer);
public record EconomySimulationResult(int ActivePlayers, int Days, long SoftSource, long SoftSink, long NetSoft, decimal TaxCollected, string Assessment, DateTimeOffset GeneratedAt);
public record InvestigationCase(string Id, string SubjectUserId, string Category, string Summary, string Status, string Severity, DateTimeOffset OpenedAt, DateTimeOffset UpdatedAt);
public record InvestigationRequest(string Id, string SubjectUserId, string Category, string Summary, string Severity = "medium", string Status = "open");
public record PlayerAppeal(string Id, string UserId, string Subject, string Statement, string Status, string? DecisionNote, DateTimeOffset SubmittedAt, DateTimeOffset UpdatedAt);
public record AppealRequest(string Id, string UserId, string Subject, string Statement);
public record AppealDecisionRequest(string Status, string DecisionNote);
public record ReportSchedule(string Id, string Name, string ReportType, string Format, string Cadence, bool Enabled, DateTimeOffset NextRunAt, DateTimeOffset? LastRunAt, DateTimeOffset UpdatedAt);
public record ReportScheduleRequest(string Id, string Name, string ReportType, string Format, string Cadence, bool Enabled = true);
public record RetentionPolicy(int ArchiveDays, int AuditLogDays, int ChatDays, bool AutoPurgeEnabled, DateTimeOffset UpdatedAt);
public record RetentionPolicyRequest(int ArchiveDays, int AuditLogDays, int ChatDays, bool AutoPurgeEnabled);
public record RetentionRunResult(int ArchivesRemoved, int MessagesRemoved, int AuditEntriesRemoved, DateTimeOffset AppliedAt);
public record PluginRecord(string Id, string Name, string Version, string Kind, bool Enabled, DateTimeOffset UpdatedAt);
public record PluginRequest(string Id, string Name, string Version, string Kind, bool Enabled = true);
public record SystemSettings(string EnvironmentName, int SessionTimeoutMinutes, int MaxUploadMegabytes, bool MaintenanceMode, bool BotBackfillDefault, DateTimeOffset UpdatedAt);
public record SystemSettingsRequest(string EnvironmentName, int SessionTimeoutMinutes, int MaxUploadMegabytes, bool MaintenanceMode, bool BotBackfillDefault);
public record ConfigurationRestoreResult(string ArchiveId, string Scope, int Bots, int Queues, int Campaigns, int Rewards, int Achievements, int Products, int Plugins, int Schedules, List<string> Notes, DateTimeOffset RestoredAt);
public record CurrencyAdjustmentRequest(string Currency, long Amount, string Reason);
public record CurrencyTransaction(string Id, string UserId, string Currency, long Amount, long BalanceAfter, string Reason, string ActorId, DateTimeOffset CreatedAt);
public record EarlyWarningAlert(string Id, string Severity, string Category, string Title, string Detail, DateTimeOffset DetectedAt);
public record OperationsSummary(int Bots, int ActiveBots, int Campaigns, int ActiveCampaigns, int Rewards, int ActiveProducts, int EarlyWarnings);
public record ArchiveRequest(string Scope = "full");
public record ArchiveDescriptor(string Id, string Name, string Scope, long Length, string Sha256, DateTimeOffset CreatedAt);
public record ArchiveVerification(string Id, bool IsValid, string Sha256, long Length, DateTimeOffset VerifiedAt);
public record GeneratedArchive(string Id, string Name, string Scope, byte[] Content, string Sha256, DateTimeOffset CreatedAt);
public record ContentPackDescriptor(
    string Id,
    string Name,
    string Version,
    string Description,
    string FileName,
    string ContentType,
    long Length,
    string Sha256,
    DateTimeOffset UploadedAt,
    bool Active);
public record ContentPackRecord(
    string Id,
    string Name,
    string Version,
    string Description,
    string FileName,
    string ContentType,
    byte[] Content,
    string Sha256,
    DateTimeOffset UploadedAt,
    bool Active);
public record UpdateContentPackRequest(bool Active);
public record GamePackageDiagnostic(string Severity, string Code, string Message, string? Path = null);
public record GamePackageValidationResponse(
    bool Valid,
    GamePackageManifest? Manifest,
    List<GamePackageDiagnostic> Diagnostics,
    RulePackValidationResult? RulePackValidation = null);
public record GamePackageVersionDescriptor(
    string PackageId,
    string GameId,
    string Name,
    string Version,
    string Description,
    string FileName,
    long Length,
    string Sha256,
    DateTimeOffset ImportedAt,
    bool Active,
    GamePackageContentSummary Content);
public record GamePackageVersionRecord(
    GamePackageManifest Manifest,
    string FileName,
    byte[] Content,
    string Sha256,
    DateTimeOffset ImportedAt,
    RulePack RulePack,
    VictoryDefinitionSet VictoryDefinitions);
public record CreateGameRequest(string RulePackId, List<string> PlayerIds, bool AddAi = false, string? DeckId = null, int TeamSize = 1);
public record PlayCardRequest(string CardInstanceId);
public record ClientGameTeam(string PlayerId, int TeamId);
public record ClientGameZone(string PlayerId, string ZoneId, List<CardInstance> Cards);
public record ClientGameResource(string PlayerId, string ResourceId, long Value);
public record ClientGameAction(string Id, string Name, string? SourceZone, string? DestinationZone, bool RequiresActivePlayer, string? Target);
public record ClientGameView(
    string Id,
    string RulePackId,
    List<string> Players,
    string ActivePlayerId,
    string CurrentPhaseId,
    int Turn,
    bool Finished,
    string? WinnerPlayerId,
    string? VictoryRuleId,
    List<ClientGameZone> Zones,
    List<ClientGameResource> Resources,
    List<ClientGameAction> AllowedActions,
    List<GameEvent> Events)
{
    public int TeamSize { get; init; } = 1;
    public List<ClientGameTeam> Teams { get; init; } = [];
}
public record GameEvent(string Type, string Message, string? ActorId = null, object? Data = null, DateTimeOffset? At = null);
public record StateMachineTransition(
    string Event,
    string From,
    string To,
    JsonElement? Data = null,
    Dictionary<string, JsonElement>? Guards = null,
    Dictionary<string, JsonElement>? Effects = null,
    Dictionary<string, JsonElement>? Metadata = null);
public record StateMachineConfiguration(
    string Id,
    string Name,
    string Version,
    string InitialState,
    List<string> States,
    List<StateMachineTransition> Transitions,
    Dictionary<string, JsonElement>? Metadata = null,
    string SchemaVersion = "1",
    List<string>? TerminalStates = null,
    Dictionary<string, Dictionary<string, JsonElement>>? StateMetadata = null);
public record StateMachineValidationResult(bool Valid, List<string> Errors, StateMachineConfiguration? Normalized = null);
public record CreateStateMachineRequest(string ConfigurationId, Dictionary<string, JsonElement>? Data = null, List<string>? ParticipantIds = null);
public record TransitionStateMachineRequest(string Event, JsonElement? Data = null);
public record StateMachineEvent(string Event, string From, string To, string? ActorId = null, JsonElement? Data = null, DateTimeOffset? At = null);

public class StateMachineInstance
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ConfigurationId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public List<string> ParticipantIds { get; init; } = [];
    public string CurrentState { get; set; } = "";
    public Dictionary<string, JsonElement> Data { get; init; } = [];
    public Dictionary<string, JsonElement> Context { get; init; } = [];
    public List<StateMachineEvent> Events { get; init; } = [];
    public bool Finished { get; set; }
}

public class GameState
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string RulePackId { get; init; } = "mtg-like";
    public List<string> Players { get; init; } = [];
    public Dictionary<string, int> Life { get; init; } = [];
    public Dictionary<string, List<string>> PlayerDecks { get; init; } = [];
    /// <summary>Team membership. Missing entries denote free-for-all games.</summary>
    public Dictionary<string, int> PlayerTeams { get; init; } = [];
    public int TeamSize { get; set; } = 1;
    public int BotDifficulty { get; set; } = 2;
    public Dictionary<string, List<CardInstance>> Hands { get; init; } = [];
    public Dictionary<string, List<CardInstance>> Battlefield { get; init; } = [];
    public Dictionary<string, Dictionary<string, List<CardInstance>>> Zones { get; init; } = [];
    public Dictionary<string, Dictionary<string, long>> Resources { get; init; } = [];
    public Dictionary<string, JsonElement> Data { get; init; } = [];
    public List<GameEvent> Events { get; init; } = [];
    public string ActivePlayerId { get; set; } = "";
    public string CurrentPhaseId { get; set; } = "";
    public long ActionSequence { get; set; }
    public int Turn { get; set; } = 1;
    public string? WinnerPlayerId { get; set; }
    public string? VictoryRuleId { get; set; }
    public List<string> EliminatedPlayerIds { get; init; } = [];
    public bool Finished { get; set; }
}

public class AppState
{
    public ConcurrentDictionary<string, User> Users { get; } = new();
    public ConcurrentDictionary<string, string> Sessions { get; } = new();
    public ConcurrentDictionary<string, DateTimeOffset> SessionActivity { get; } = new();
    public ConcurrentDictionary<string, RulePack> RulePacks { get; } = new();
    public ConcurrentDictionary<string, Inventory> Inventories { get; } = new();
    public ConcurrentDictionary<string, PlayerWallet> Wallets { get; } = new();
    public ConcurrentDictionary<string, GameState> Games { get; } = new();
    public ConcurrentDictionary<string, List<string>> Chat { get; } = new();
    public ConcurrentDictionary<string, FriendConnection> FriendConnections { get; } = new();
    public ConcurrentDictionary<string, DirectMessage> DirectMessages { get; } = new();
    public ConcurrentDictionary<string, DeckDefinition> Decks { get; } = new();
    public ConcurrentDictionary<string, StoreProduct> StoreProducts { get; } = new();
    public ConcurrentDictionary<string, BotProfile> BotProfiles { get; } = new();
    public ConcurrentDictionary<string, BotSimulationRun> BotSimulationRuns { get; } = new();
    public ConcurrentDictionary<string, MatchmakingQueueConfiguration> MatchmakingQueues { get; } = new();
    public ConcurrentDictionary<string, MatchmakingQueueEntry> MatchmakingEntries { get; } = new();
    public ConcurrentDictionary<string, string> MatchedGamesByUser { get; } = new();
    public ConcurrentDictionary<string, LiveOpsCampaign> LiveOpsCampaigns { get; } = new();
    public ConcurrentDictionary<string, RewardCampaign> RewardCampaigns { get; } = new();
    public ConcurrentDictionary<string, ManagedAchievement> ManagedAchievements { get; } = new();
    public ConcurrentDictionary<string, GeneratedArchive> Archives { get; } = new();
    public EconomyConfiguration Economy { get; set; } = new("gold", "gems", 1000, 0, DateTimeOffset.UtcNow);
    public ConcurrentDictionary<string, CurrencyTransaction> CurrencyTransactions { get; } = new();
    public ConcurrentDictionary<string, InvestigationCase> Investigations { get; } = new();
    public ConcurrentDictionary<string, PlayerAppeal> Appeals { get; } = new();
    public ConcurrentDictionary<string, ReportSchedule> ReportSchedules { get; } = new();
    public ConcurrentDictionary<string, PluginRecord> Plugins { get; } = new();
    public RetentionPolicy Retention { get; set; } = new(30, 90, 14, false, DateTimeOffset.UtcNow);
    public SystemSettings Settings { get; set; } = new("local", 120, 32, false, true, DateTimeOffset.UtcNow);
    public ConcurrentDictionary<string, ContentPackRecord> ContentPacks { get; } = new();
    public ConcurrentDictionary<string, GamePackageVersionRecord> GamePackages { get; } = new();
    public ConcurrentDictionary<string, string> ActiveGamePackageVersions { get; } = new();
    public ConcurrentDictionary<string, AssetRecord> Assets { get; } = new();
    public ConcurrentDictionary<string, StateMachineConfiguration> StateMachineConfigurations { get; } = new();
    public ConcurrentDictionary<string, StateMachineInstance> StateMachineInstances { get; } = new();
}

public record AssetRecord(string Id, string Name, string ContentType, byte[] Content, string Sha256);
