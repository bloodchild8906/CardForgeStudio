using System.Text.Json;
using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Net.Http.Headers;
using TcgULtimateServer.Api.Models;
using TcgULtimateServer.Api.Services;

var localManagementKey = Environment.GetEnvironmentVariable("TCG_LOCAL_MANAGEMENT_KEY");
if (string.IsNullOrWhiteSpace(localManagementKey))
{
    localManagementKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    Environment.SetEnvironmentVariable("TCG_LOCAL_MANAGEMENT_KEY", localManagementKey);
}

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = (Environment.GetEnvironmentVariable("TCG_ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

// Explicitly remove any upper limit on the number of concurrent client connections so the
// server can scale to an unbounded number of connected SignalR/REST clients. Both Kestrel
// connection caps default to null (unlimited); setting them explicitly here guarantees the
// behavior regardless of any future configuration file or default changes.
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxConcurrentConnections = null;
    o.Limits.MaxConcurrentUpgradedConnections = null;
});

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AuditLogService>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<OperationsService>();
builder.Services.AddSingleton<ReportingService>();
builder.Services.AddSingleton<GovernanceService>();
builder.Services.AddSingleton<ContentPackService>();
builder.Services.AddSingleton<RulesEngine>();
builder.Services.AddSingleton<GamePackageService>();
builder.Services.AddSingleton<StateMachineService>();
builder.Services.AddSingleton<BotService>();
builder.Services.AddSingleton<SecurePacketService>();
builder.Services.AddSingleton<IAssetStore, InMemoryAssetStore>();
builder.Services.AddSingleton<AssetService>();
builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length == 0)
    {
        // Native Unity and WinUI clients do not require browser CORS. Browser access must opt in explicitly.
        policy.SetIsOriginAllowed(_ => false);
        return;
    }

    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseResponseCompression();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (Exception exception)
    {
        context.RequestServices.GetRequiredService<AuditLogService>().Write("Error", "request", exception.Message, requestPath: context.Request.Path, statusCode: 500);
        throw;
    }
    finally
    {
        if (!context.Request.Path.StartsWithSegments("/api/admin/logs"))
            context.RequestServices.GetRequiredService<AuditLogService>().Write("Information", "request", $"{context.Request.Method} {context.Request.Path}", requestPath: context.Request.Path, statusCode: context.Response.StatusCode);
    }
});
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    await next();
});
app.UseHsts(); app.UseHttpsRedirection(); app.UseCors();
app.UseRateLimiter();

var state = app.Services.GetRequiredService<AppState>();
var rules = app.Services.GetRequiredService<RulesEngine>();
var assetService = app.Services.GetRequiredService<AssetService>();
Seed(state, assetService);
rules.Save(state.RulePacks["mtg-like"]);

app.MapPost("/api/auth/register", (RegisterRequest request, AuthService auth) =>
{
    try { return Results.Ok(auth.Register(request)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
}).RequireRateLimiting("authentication");
app.MapGet("/", () => Results.NotFound());
app.MapGet("/api/health", (AppState state) => Results.Ok(new
{
    service = "TcgULtimateServer",
    status = state.Settings.MaintenanceMode ? "maintenance" : "healthy",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "development",
    serverTime = DateTimeOffset.UtcNow,
    maintenanceMode = state.Settings.MaintenanceMode
}));
app.MapPost("/api/auth/login", (LoginRequest request, AuthService auth) =>
{
    try { return Results.Ok(auth.Login(request)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
}).RequireRateLimiting("authentication");
app.MapDelete("/api/auth/session", (HttpRequest request, AuthService auth) =>
{
    var token = request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    return auth.Revoke(token) ? Results.NoContent() : Results.Unauthorized();
});
app.MapPost("/api/auth/local-management", (HttpRequest request, AuthService auth) =>
{
    if (request.HttpContext.Connection.RemoteIpAddress is not { } remoteAddress || !IPAddress.IsLoopback(remoteAddress))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var suppliedKey = request.Headers["X-Tcg-Management-Key"].FirstOrDefault();
    var configuredKey = Environment.GetEnvironmentVariable("TCG_LOCAL_MANAGEMENT_KEY");
    if (string.IsNullOrWhiteSpace(suppliedKey) || string.IsNullOrWhiteSpace(configuredKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
    var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
    return suppliedBytes.Length == configuredBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, configuredBytes)
        ? Results.Ok(auth.LocalManagementSession())
        : Results.StatusCode(StatusCodes.Status403Forbidden);
});
app.MapGet("/api/admin/users", (HttpRequest http, AuthService auth, AdminService admin) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    return user is null ? Results.Unauthorized() : admin.CanModerate(user) ? Results.Ok(admin.Users()) : Results.Forbid();
});
app.MapGet("/api/admin/summary", (HttpRequest http, AuthService auth, AdminService admin) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    return user is null ? Results.Unauthorized() : admin.IsAdmin(user) ? Results.Ok(admin.Summary()) : Results.Forbid();
});
app.MapGet("/api/admin/presence", (HttpRequest http, AuthService auth, AdminService admin) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    return user is null ? Results.Unauthorized() : admin.IsAdmin(user) ? Results.Ok(auth.Presence()) : Results.Forbid();
});
app.MapGet("/api/admin/chat", (HttpRequest http, AuthService auth, AdminService admin) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    return admin.CanModerate(user) ? Results.Ok(admin.ChatSnapshot(auth.Presence().OnlinePlayers)) : Results.Forbid();
});
app.MapPost("/api/admin/chat/announcements", async (ServerAnnouncementRequest request, HttpRequest http, AuthService auth, AdminService admin, AppState appState, AuditLogService audit, IHubContext<GameHub> hub) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    var body = request.Body.Trim();
    if (string.IsNullOrWhiteSpace(body) || body.Length > 500) return Results.BadRequest("Announcement must contain 1 to 500 characters.");
    var message = $"SERVER: {body}";
    foreach (var game in appState.Games.Values)
    {
        var channel = appState.Chat.GetOrAdd(game.Id, _ => []);
        lock (channel) channel.Add(message);
    }
    await hub.Clients.All.SendAsync("serverAnnouncement", message);
    audit.Write("Information", "chat", $"Broadcast server announcement: {body}", user.Id);
    return Results.Ok(new { message, deliveredToGames = appState.Games.Count, sentAt = DateTimeOffset.UtcNow });
});
app.MapPatch("/api/admin/users/{id}", (string id, UpdateUserRequest request, HttpRequest http, AuthService auth, AdminService admin) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.CanModerate(user)) return Results.Forbid();
    try { return Results.Ok(admin.UpdateUser(user, id, request)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapGet("/api/admin/players/{id}/profile", (string id, HttpRequest request, AuthService auth, AdminService admin) =>
{
    if (!TryGetAdmin(request, auth, admin, out _)) return Results.Forbid();
    try { return Results.Ok(admin.PlayerProfile(id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapPost("/api/admin/players/{id}/wallet-adjustments", (string id, CurrencyAdjustmentRequest request, HttpRequest http, AuthService auth, AdminService admin) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(admin.AdjustWallet(user, id, request)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
    catch (OverflowException) { return Results.BadRequest("The resulting balance is too large."); }
});
app.MapGet("/api/admin/economy/transactions", (string? userId, HttpRequest request, AuthService auth, AdminService admin) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(admin.CurrencyTransactions(userId)) : Results.Forbid();
});
app.MapPatch("/api/admin/messages/{id}", (string id, UpdateMessageModerationRequest request, HttpRequest http, AuthService auth, AdminService admin) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(admin.ModerateMessage(user, id, request)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapPatch("/api/admin/decks/{id}", (string id, UpdateDeckModerationRequest request, HttpRequest http, AuthService auth, AdminService admin) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(admin.ModerateDeck(user, id, request)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapGet("/api/admin/logs", (HttpRequest http, AuthService auth, AdminService admin, int? limit) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    return user is null ? Results.Unauthorized() : admin.IsAdmin(user) ? Results.Ok(admin.Logs(limit ?? 500)) : Results.Forbid();
});
app.MapGet("/api/admin/operations/summary", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.Summary()) : Results.Forbid();
});
app.MapGet("/api/admin/bots", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.Bots()) : Results.Forbid();
});
app.MapPut("/api/admin/bots/{id}", (string id, BotProfileRequest request, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    if (!string.Equals(id, request.Id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route ID must match the bot ID.");
    try { return Results.Ok(operations.SaveBot(request, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/admin/bots/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return !TryGetAdmin(http, auth, admin, out var user) ? Results.Forbid() : operations.RemoveBot(id, user.Id) ? Results.NoContent() : Results.NotFound();
});
app.MapPost("/api/admin/bots/{id}/simulations", (string id, BotSimulationRequest request, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(operations.SimulateBot(id, request, user.Id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapPost("/api/admin/bots/{id}/rollback", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(operations.RollbackModel(id, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapGet("/api/admin/matchmaking/queues", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.MatchmakingQueues()) : Results.Forbid();
});
app.MapPut("/api/admin/matchmaking/queues/{id}", (string id, MatchmakingQueueRequest request, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    if (!string.Equals(id, request.Id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route ID must match the queue ID.");
    try { return Results.Ok(operations.SaveMatchmakingQueue(request, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/admin/matchmaking/queues/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return !TryGetAdmin(http, auth, admin, out var user) ? Results.Forbid() : operations.RemoveMatchmakingQueue(id, user.Id) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/api/admin/matchmaking/queues/{id}/entries", (string id, HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.MatchmakingEntries(id)) : Results.Forbid();
});
app.MapPost("/api/admin/matchmaking/queues/{id}/run", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(operations.RunMatchmaking(id, user.Id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/admin/matchmaking/queues/{id}/entries", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return !TryGetAdmin(http, auth, admin, out var user) ? Results.Forbid() : Results.Ok(new { removed = operations.ClearMatchmakingQueue(id, user.Id) });
});
app.MapGet("/api/admin/liveops/campaigns", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.Campaigns()) : Results.Forbid();
});
app.MapPut("/api/admin/liveops/campaigns/{id}", (string id, LiveOpsCampaignRequest request, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    if (!string.Equals(id, request.Id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route ID must match the campaign ID.");
    try { return Results.Ok(operations.SaveCampaign(request, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/admin/liveops/campaigns/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return !TryGetAdmin(http, auth, admin, out var user) ? Results.Forbid() : operations.RemoveCampaign(id, user.Id) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/api/admin/liveops/rewards", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.Rewards()) : Results.Forbid();
});
app.MapPut("/api/admin/liveops/rewards/{id}", (string id, RewardCampaignRequest request, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    if (!string.Equals(id, request.Id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route ID must match the reward ID.");
    try { return Results.Ok(operations.SaveReward(request, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/admin/liveops/rewards/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return !TryGetAdmin(http, auth, admin, out var user) ? Results.Forbid() : operations.RemoveReward(id, user.Id) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/api/admin/liveops/achievements", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.Achievements()) : Results.Forbid();
});
app.MapPut("/api/admin/liveops/achievements/{id}", (string id, ManagedAchievementRequest request, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    if (!string.Equals(id, request.Id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route ID must match the achievement ID.");
    try { return Results.Ok(operations.SaveAchievement(request, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/admin/liveops/achievements/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return !TryGetAdmin(http, auth, admin, out var user) ? Results.Forbid() : operations.RemoveAchievement(id, user.Id) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/api/admin/store/products", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.Products()) : Results.Forbid();
});
app.MapPut("/api/admin/store/products/{id}", (string id, StoreProductRequest request, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    if (!string.Equals(id, request.Id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route ID must match the product ID.");
    try { return Results.Ok(operations.SaveProduct(request, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/admin/store/products/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return !TryGetAdmin(http, auth, admin, out var user) ? Results.Forbid() : operations.RemoveProduct(id, user.Id) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/api/admin/economy", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.Economy()) : Results.Forbid();
});
app.MapPut("/api/admin/economy", (EconomyConfigurationRequest request, HttpRequest http, AuthService auth, AdminService admin, OperationsService operations) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(operations.SaveEconomy(request, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapGet("/api/admin/early-warnings", (HttpRequest request, AuthService auth, AdminService admin, OperationsService operations) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(operations.Warnings()) : Results.Forbid();
});
app.MapGet("/api/admin/reports/{reportType}", (string reportType, string? format, HttpRequest request, AuthService auth, AdminService admin, ReportingService reporting) =>
{
    if (!TryGetAdmin(request, auth, admin, out _)) return Results.Forbid();
    try
    {
        var report = reporting.CreateReport(reportType, format ?? "json");
        return Results.File(report.Content, report.ContentType, report.FileName);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapPost("/api/admin/archives", (ArchiveRequest request, HttpRequest http, AuthService auth, AdminService admin, ReportingService reporting) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(reporting.CreateArchive(request.Scope, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapGet("/api/admin/archives", (HttpRequest request, AuthService auth, AdminService admin, ReportingService reporting) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(reporting.Archives()) : Results.Forbid();
});
app.MapGet("/api/admin/archives/{id}", (string id, HttpRequest request, AuthService auth, AdminService admin, ReportingService reporting) =>
{
    if (!TryGetAdmin(request, auth, admin, out _)) return Results.Forbid();
    try
    {
        var archive = reporting.GetArchive(id);
        return Results.File(archive.Content, "application/zip", archive.Name);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapPost("/api/admin/archives/{id}/verify", (string id, HttpRequest request, AuthService auth, AdminService admin, ReportingService reporting) =>
{
    if (!TryGetAdmin(request, auth, admin, out _)) return Results.Forbid();
    try { return Results.Ok(reporting.Verify(id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapGet("/api/admin/content-packs", (HttpRequest request, AuthService auth, AdminService admin, ContentPackService packs) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(packs.Packs()) : Results.Forbid();
});
app.MapPost("/api/admin/content-packs/upload", async (HttpRequest request, AuthService auth, AdminService admin, ContentPackService packs) =>
{
    if (!TryGetAdmin(request, auth, admin, out var user)) return Results.Forbid();
    if (!request.HasFormContentType) return Results.BadRequest("multipart/form-data required");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null) return Results.BadRequest("file is required");
    if (file.Length > 50L * 1024 * 1024) return Results.BadRequest("Content pack exceeds the 50 MB limit.");
    var id = form["id"].FirstOrDefault() ?? string.Empty;
    var name = form["name"].FirstOrDefault() ?? string.Empty;
    var version = form["version"].FirstOrDefault() ?? string.Empty;
    var description = form["description"].FirstOrDefault() ?? string.Empty;
    var active = !bool.TryParse(form["active"].FirstOrDefault(), out var parsedActive) || parsedActive;
    try { return Results.Ok(packs.Upload(id, name, version, description, active, file.FileName, file.ContentType, file.OpenReadStream(), user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapPatch("/api/admin/content-packs/{id}", (string id, UpdateContentPackRequest request, HttpRequest http, AuthService auth, AdminService admin, ContentPackService packs) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(packs.SetActive(id, request.Active, user.Id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapDelete("/api/admin/content-packs/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, ContentPackService packs) =>
{
    if (!TryGetAdmin(http, auth, admin, out var user)) return Results.Forbid();
    try { return packs.Remove(id, user.Id) ? Results.NoContent() : Results.NotFound(); }
    catch (InvalidOperationException exception) { return Results.Conflict(exception.Message); }
});
app.MapGet("/api/admin/content-packs/{id}/download", (string id, HttpRequest request, AuthService auth, AdminService admin, ContentPackService packs) =>
{
    if (!TryGetAdmin(request, auth, admin, out _)) return Results.Forbid();
    try
    {
        var pack = packs.Get(id);
        return Results.File(pack.Content, pack.ContentType, pack.FileName);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapGet("/api/admin/game-packages", (HttpRequest request, AuthService auth, AdminService admin, GamePackageService packages) =>
{
    return TryGetAdmin(request, auth, admin, out _) ? Results.Ok(packages.Packages()) : Results.Forbid();
});
app.MapPost("/api/admin/game-packages/validate", async (HttpRequest request, AuthService auth, AdminService admin, GamePackageService packages) =>
{
    if (!TryGetAdmin(request, auth, admin, out _)) return Results.Forbid();
    if (!request.HasFormContentType) return Results.BadRequest("multipart/form-data required");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null) return Results.BadRequest("file is required");
    if (file.Length > GamePackageService.MaximumArchiveBytes) return Results.BadRequest("Game package exceeds the 100 MB limit.");
    return Results.Ok(packages.Validate(file.OpenReadStream()));
});
app.MapPost("/api/admin/game-packages/import", async (HttpRequest request, AuthService auth, AdminService admin, GamePackageService packages) =>
{
    if (!TryGetAdmin(request, auth, admin, out var user)) return Results.Forbid();
    if (!request.HasFormContentType) return Results.BadRequest("multipart/form-data required");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null) return Results.BadRequest("file is required");
    if (file.Length > GamePackageService.MaximumArchiveBytes) return Results.BadRequest("Game package exceeds the 100 MB limit.");
    var activate = bool.TryParse(form["activate"].FirstOrDefault(), out var parsed) && parsed;
    try { return Results.Ok(packages.Import(file.OpenReadStream(), file.FileName, activate, user.Id)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapPost("/api/admin/game-packages/{packageId}/versions/{version}/activate", (string packageId, string version, HttpRequest request, AuthService auth, AdminService admin, GamePackageService packages) =>
{
    if (!TryGetAdmin(request, auth, admin, out var user)) return Results.Forbid();
    try { return Results.Ok(packages.Activate(packageId, version, user.Id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
    catch (InvalidOperationException exception) { return Results.Conflict(exception.Message); }
});
app.MapGet("/api/admin/game-packages/{packageId}/versions/{version}/download", (string packageId, string version, HttpRequest request, AuthService auth, AdminService admin, GamePackageService packages) =>
{
    if (!TryGetAdmin(request, auth, admin, out _)) return Results.Forbid();
    try
    {
        var package = packages.Get(packageId, version);
        return Results.File(package.Content, "application/vnd.cardforge.game-package+zip", package.FileName);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapGet("/api/rules", (AppState s) => s.RulePacks.Values);
app.MapGet("/api/rules/capabilities", (RulesEngine engine) => Results.Ok(engine.Capabilities()));
app.MapGet("/api/rules/{id}", (string id, RulesEngine engine) =>
{
    try { return Results.Ok(engine.Get(id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapPost("/api/rules/validate", (RulePack pack, HttpRequest request, AuthService auth, AdminService admin, RulesEngine engine) =>
{
    return !TryGetAdmin(request, auth, admin, out _) ? Results.Forbid() : Results.Ok(engine.Validate(pack));
});
app.MapPut("/api/rules/{id}", (string id, RulePack pack, HttpRequest request, AuthService auth, AdminService admin, RulesEngine engine, AuditLogService audit) =>
{
    if (!TryGetAdmin(request, auth, admin, out var user)) return Results.Forbid();
    if (!id.Equals(pack.Id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route ID must match the rule pack ID.");
    try
    {
        var saved = engine.Save(pack);
        audit.Write("Information", "rules", $"Saved rule pack {saved.Id} version {saved.Version} from the visual editor.", user.Id);
        return Results.Ok(saved);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapPost("/api/rules/upload", async (HttpRequest request, AuthService auth, AdminService admin, RulesEngine engine, AuditLogService audit) =>
{
    var user = auth.FromToken(request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    if (!request.HasFormContentType) return Results.BadRequest("multipart/form-data required");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null) return Results.BadRequest("file is required");
    try
    {
        var uploaded = engine.Upload(file.OpenReadStream());
        audit.Write("Information", "rules", $"Uploaded rule pack {uploaded.Id} version {uploaded.Version}.", user.Id);
        return Results.Ok(uploaded);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/rules/{id}", (string id, HttpRequest request, AuthService auth, AdminService admin, AppState s, AuditLogService audit) =>
{
    var user = auth.FromToken(request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    if (s.Games.Values.Any(game => string.Equals(game.RulePackId, id, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict("The rule pack is in use by an active game.");
    if (!s.RulePacks.TryRemove(id, out _)) return Results.NotFound("Rule pack not found.");
    audit.Write("Information", "rules", $"Removed rule pack {id}.", user.Id);
    return Results.NoContent();
});
app.MapGet("/api/state-machines/configurations", (AppState s) => s.StateMachineConfigurations.Values);
app.MapGet("/api/state-machines/configurations/{id}", (string id, StateMachineService machines) =>
{
    try { return Results.Ok(machines.GetConfiguration(id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapPost("/api/state-machines/configurations/validate", (StateMachineConfiguration configuration, HttpRequest request, AuthService auth, AdminService admin, StateMachineService machines) =>
{
    return !TryGetAdmin(request, auth, admin, out _) ? Results.Forbid() : Results.Ok(machines.ValidateConfiguration(configuration));
});
app.MapPut("/api/state-machines/configurations/{id}", (string id, StateMachineConfiguration configuration, HttpRequest request, AuthService auth, AdminService admin, StateMachineService machines, AuditLogService audit) =>
{
    if (!TryGetAdmin(request, auth, admin, out var user)) return Results.Forbid();
    if (!id.Equals(configuration.Id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route ID must match the workflow configuration ID.");
    try
    {
        var saved = machines.Save(configuration);
        audit.Write("Information", "workflows", $"Saved workflow configuration {saved.Id} version {saved.Version} from the visual editor.", user.Id);
        return Results.Ok(saved);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapPost("/api/state-machines/configurations/upload", async (HttpRequest request, AuthService auth, AdminService admin, StateMachineService machines, AuditLogService audit) =>
{
    var user = auth.FromToken(request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    if (!request.HasFormContentType) return Results.BadRequest("multipart/form-data required");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null) return Results.BadRequest("file is required");
    try
    {
        var uploaded = machines.Upload(file.OpenReadStream());
        audit.Write("Information", "workflows", $"Uploaded workflow configuration {uploaded.Id} version {uploaded.Version}.", user.Id);
        return Results.Ok(uploaded);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/state-machines/configurations/{id}", (string id, HttpRequest request, AuthService auth, AdminService admin, AppState s, AuditLogService audit) =>
{
    var user = auth.FromToken(request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    if (s.StateMachineInstances.Values.Any(instance => string.Equals(instance.ConfigurationId, id, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict("The workflow is in use by an active state-machine instance.");
    if (!s.StateMachineConfigurations.TryRemove(id, out _)) return Results.NotFound("Workflow configuration not found.");
    audit.Write("Information", "workflows", $"Removed workflow configuration {id}.", user.Id);
    return Results.NoContent();
});
app.MapPost("/api/state-machines", (CreateStateMachineRequest request, HttpRequest http, AuthService auth, StateMachineService machines) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    try { return Results.Ok(machines.Create(request, user.Id)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapGet("/api/state-machines", (HttpRequest http, AuthService auth, AdminService admin, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    return admin.IsAdmin(user)
        ? Results.Ok(s.StateMachineInstances.Values)
        : Results.Ok(s.StateMachineInstances.Values.Where(instance =>
            string.Equals(instance.OwnerId, user.Id, StringComparison.Ordinal) ||
            instance.ParticipantIds.Contains(user.Id, StringComparer.Ordinal)));
});
app.MapGet("/api/state-machines/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, StateMachineService machines) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    try
    {
        var instance = machines.GetInstance(id);
        return machines.CanAccess(instance, user.Id, admin.IsAdmin(user)) ? Results.Ok(instance) : Results.NotFound("Workflow not found.");
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
});
app.MapDelete("/api/state-machines/{id}", (string id, HttpRequest request, AuthService auth, AdminService admin, AppState s, AuditLogService audit) =>
{
    var user = auth.FromToken(request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    if (!s.StateMachineInstances.TryRemove(id, out _)) return Results.NotFound("State-machine instance not found.");
    audit.Write("Information", "workflows", $"Removed state-machine instance {id}.", user.Id);
    return Results.NoContent();
});
app.MapPost("/api/state-machines/{id}/transitions", (string id, TransitionStateMachineRequest request, HttpRequest http, AuthService auth, AdminService admin, StateMachineService machines) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    try
    {
        var instance = machines.GetInstance(id);
        if (!machines.CanAccess(instance, user.Id, admin.IsAdmin(user))) return Results.NotFound("Workflow not found.");
        return Results.Ok(machines.Transition(id, user.Id, request));
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(exception.Message); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapGet("/api/assets/manifest", (HttpRequest request, AssetService assets) => Results.Ok(assets.Manifest($"{request.Scheme}://{request.Host}")));
app.MapPost("/api/assets/upload", async (HttpRequest request, AssetService assets, AuthService auth, AdminService admin, AuditLogService audit) =>
{
    var token = request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    var user = auth.FromToken(token);
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    if (!request.HasFormContentType) return Results.BadRequest("multipart/form-data required");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    var id = form["id"].FirstOrDefault();
    var name = form["name"].FirstOrDefault() ?? file?.FileName;
    if (file is null || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return Results.BadRequest("file, id, and name are required");
    try
    {
        var uploaded = assets.Upload(id, name, file.ContentType, file.OpenReadStream(), $"{request.Scheme}://{request.Host}");
        audit.Write("Information", "assets", $"Uploaded asset {uploaded.Id} ({uploaded.Length} bytes).", user.Id);
        return Results.Ok(uploaded);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/assets/{id}", (string id, HttpRequest request, AuthService auth, AdminService admin, AssetService assets, AppState s, AuditLogService audit) =>
{
    var user = auth.FromToken(request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    if (s.RulePacks.Values.SelectMany(pack => pack.Cards).Any(card => string.Equals(card.AssetId, id, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict("The asset is referenced by a rule-pack card.");
    if (!assets.Remove(id)) return Results.NotFound("Asset not found.");
    audit.Write("Information", "assets", $"Removed asset {id}.", user.Id);
    return Results.NoContent();
});
app.MapGet("/api/assets/{id}", (string id, HttpRequest request, AuthService auth, AssetService assets) =>
{
    var token = request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    if (auth.FromToken(token) is null) return Results.Unauthorized();
    try
    {
        var asset = assets.Get(id);
        var payload = asset.Content;
        var encoding = request.Headers.AcceptEncoding.FirstOrDefault();
        if (AcceptsEncoding(encoding, "br"))
        {
            var compressed = Compress(asset.Content, useBrotli: true);
            if (compressed.Length < payload.Length) { payload = compressed; request.HttpContext.Response.Headers.ContentEncoding = "br"; }
        }
        else if (AcceptsEncoding(encoding, "gzip"))
        {
            var compressed = Compress(asset.Content, useBrotli: false);
            if (compressed.Length < payload.Length) { payload = compressed; request.HttpContext.Response.Headers.ContentEncoding = "gzip"; }
        }
        request.HttpContext.Response.Headers.Vary = "Accept-Encoding";
        return Results.Bytes(payload, asset.ContentType, entityTag: new EntityTagHeaderValue($"\"{asset.Sha256}\""));
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
});
app.MapGet("/api/inventory/{userId}", (string userId, AppState s) => s.Inventories.TryGetValue(userId, out var inventory) ? Results.Ok(inventory) : Results.NotFound());
app.MapGet("/api/decks", (HttpRequest http, AuthService auth, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    return Results.Ok(s.Decks.Values.Where(deck => string.Equals(deck.OwnerId, user.Id, StringComparison.Ordinal)).OrderByDescending(deck => deck.UpdatedAt).ToList());
});
app.MapPost("/api/decks", (SaveDeckRequest request, HttpRequest http, AuthService auth, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.RulePackId)) return Results.BadRequest("Deck name and rule pack are required.");
    if (!s.RulePacks.ContainsKey(request.RulePackId)) return Results.BadRequest("Unknown rule pack.");
    if (request.CardIds.Count == 0) return Results.BadRequest("A deck must contain at least one card.");
    var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id.Trim();
    var suspended = s.Decks.TryGetValue(id, out var existing) && string.Equals(existing.OwnerId, user.Id, StringComparison.Ordinal) && existing.Suspended;
    var deck = new DeckDefinition(id, user.Id, request.Name.Trim(), request.RulePackId, request.CardIds, DateTimeOffset.UtcNow, suspended);
    s.Decks[id] = deck;
    return Results.Ok(deck);
});
app.MapDelete("/api/decks/{id}", (string id, HttpRequest http, AuthService auth, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!s.Decks.TryGetValue(id, out var deck)) return Results.NotFound("Deck not found.");
    if (!string.Equals(deck.OwnerId, user.Id, StringComparison.Ordinal)) return Results.Forbid();
    s.Decks.TryRemove(id, out _);
    return Results.NoContent();
});
app.MapGet("/api/messages", (HttpRequest http, AuthService auth, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    return Results.Ok(s.DirectMessages.Values.Where(message => string.Equals(message.SenderId, user.Id, StringComparison.Ordinal) || string.Equals(message.RecipientId, user.Id, StringComparison.Ordinal)).OrderByDescending(message => message.SentAt).ToList());
});
app.MapPost("/api/messages", (SendMessageRequest request, HttpRequest http, AuthService auth, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Body)) return Results.BadRequest("Message body is required.");
    if (!s.Users.TryGetValue(request.RecipientId, out var recipient)) return Results.NotFound("Recipient not found.");
    if (recipient.Disabled) return Results.BadRequest("The recipient account is disabled.");
    var message = new DirectMessage(Guid.NewGuid().ToString("N"), user.Id, request.RecipientId, request.Body.Trim(), DateTimeOffset.UtcNow);
    s.DirectMessages[message.Id] = message;
    return Results.Ok(message);
});
app.MapGet("/api/friends", (HttpRequest http, AuthService auth, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    return Results.Ok(s.FriendConnections.Values.Where(connection => string.Equals(connection.UserId, user.Id, StringComparison.Ordinal) || string.Equals(connection.FriendUserId, user.Id, StringComparison.Ordinal)).OrderByDescending(connection => connection.CreatedAt).ToList());
});
app.MapPost("/api/friends", (CreateFriendRequest request, HttpRequest http, AuthService auth, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!s.Users.TryGetValue(request.FriendUserId, out var friend)) return Results.NotFound("Friend account not found.");
    if (string.Equals(request.FriendUserId, user.Id, StringComparison.Ordinal)) return Results.BadRequest("You cannot add yourself as a friend.");
    if (s.FriendConnections.Values.Any(c => (string.Equals(c.UserId, user.Id, StringComparison.Ordinal) && string.Equals(c.FriendUserId, request.FriendUserId, StringComparison.Ordinal)) || (string.Equals(c.UserId, request.FriendUserId, StringComparison.Ordinal) && string.Equals(c.FriendUserId, user.Id, StringComparison.Ordinal))))
        return Results.Conflict("You are already friends with this account.");
    var connection = new FriendConnection(Guid.NewGuid().ToString("N"), user.Id, request.FriendUserId, "active", DateTimeOffset.UtcNow);
    s.FriendConnections[connection.Id] = connection;
    return Results.Ok(connection);
});
app.MapDelete("/api/friends/{id}", (string id, HttpRequest http, AuthService auth, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!s.FriendConnections.TryGetValue(id, out var connection)) return Results.NotFound("Friend connection not found.");
    if (!string.Equals(connection.UserId, user.Id, StringComparison.Ordinal) && !string.Equals(connection.FriendUserId, user.Id, StringComparison.Ordinal)) return Results.Forbid();
    s.FriendConnections.TryRemove(id, out _);
    return Results.NoContent();
});
app.MapGet("/api/rankings", (AppState s) => s.Users.Values.OrderByDescending(x => x.Rating).Select((u, i) => new { rank = i + 1, user = new PublicUser(u.Id, u.Handle, u.Rating) }));
app.MapGet("/api/presence/count", (AuthService auth) => Results.Ok(new { onlinePlayers = auth.Presence().OnlinePlayers }));
app.MapGet("/api/matchmaking/queues", (OperationsService operations) => Results.Ok(operations.MatchmakingQueues().Where(status => status.Queue.Enabled)));
app.MapPost("/api/matchmaking/join", (JoinMatchmakingRequest request, HttpRequest http, AuthService auth, OperationsService operations) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    try { return Results.Ok(operations.JoinMatchmaking(user, request)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/matchmaking/leave", (HttpRequest http, AuthService auth, OperationsService operations) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    return user is null ? Results.Unauthorized() : operations.LeaveMatchmaking(user) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/api/matchmaking/me", (HttpRequest http, AuthService auth, OperationsService operations) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    return user is null ? Results.Unauthorized() : Results.Ok(operations.MatchmakingEntries().Where(entry => string.Equals(entry.UserId, user.Id, StringComparison.Ordinal)));
});
app.MapPost("/api/games", (CreateGameRequest request, HttpRequest http, AuthService auth, AppState s, RulesEngine engine, BotService bots) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "")); if (user is null) return Results.Unauthorized();
    if (!s.RulePacks.ContainsKey(request.RulePackId)) return Results.BadRequest("Unknown rule pack.");
    var players = request.PlayerIds.Distinct(StringComparer.Ordinal).ToList();
    if (players.Any(playerId => !s.Users.ContainsKey(playerId))) return Results.BadRequest("Every player must be a registered user.");
    if (!players.Contains(user.Id, StringComparer.Ordinal)) players.Insert(0, user.Id);
    if (request.AddAi) players.Add("bot-" + Guid.NewGuid().ToString("N")[..8]);
    if (players.Count is < 2 or > 8) return Results.BadRequest("A game needs between two and eight players.");
    var game = new GameState { RulePackId = request.RulePackId, Players = players }; engine.Start(game);
    // If the AI moves first, play its turn now so the human is never handed a game that is waiting on a bot.
    if (game.ActivePlayerId.StartsWith("bot-", StringComparison.Ordinal)) bots.TakeTurn(game, game.ActivePlayerId);
    s.Games[game.Id] = game; return Results.Ok(game);
});
app.MapGet("/api/games", (HttpRequest http, AuthService auth, AdminService admin, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    return admin.IsAdmin(user)
        ? Results.Ok(s.Games.Values)
        : Results.Ok(s.Games.Values.Where(game => game.Players.Contains(user.Id, StringComparer.Ordinal)));
});
app.MapGet("/api/games/{id}", (string id, HttpRequest http, AuthService auth, AdminService admin, AppState s) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    return s.Games.TryGetValue(id, out var game) &&
           (admin.IsAdmin(user) || game.Players.Contains(user.Id, StringComparer.Ordinal))
        ? Results.Ok(game)
        : Results.NotFound("Game not found.");
});
app.MapGet("/api/games/{id}/client-view", (string id, HttpRequest http, AuthService auth, AdminService admin, AppState s, RulesEngine engine) =>
{
    var user = auth.FromToken(http.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!s.Games.TryGetValue(id, out var game) || (!admin.IsAdmin(user) && !game.Players.Contains(user.Id, StringComparer.Ordinal)))
        return Results.NotFound("Game not found.");
    return Results.Ok(BuildClientGameView(game, engine));
});
app.MapPost("/api/games/{id}/actions", (string id, ExecuteGameActionRequest action, HttpRequest request, AuthService auth, AppState s, RulesEngine engine, BotService bots) =>
{
    var user = auth.FromToken(request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!s.Games.TryGetValue(id, out var game) || !game.Players.Contains(user.Id)) return Results.NotFound("Game not found or access denied.");
    try
    {
        engine.Execute(game, user.Id, action);
        if (game.ActivePlayerId.StartsWith("bot-", StringComparison.Ordinal)) bots.TakeTurn(game, game.ActivePlayerId);
        return Results.Ok(game);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
});
app.MapDelete("/api/games/{id}", (string id, HttpRequest request, AuthService auth, AdminService admin, AppState s, AuditLogService audit) =>
{
    var user = auth.FromToken(request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    if (!admin.IsAdmin(user)) return Results.Forbid();
    if (!s.Games.TryRemove(id, out _)) return Results.NotFound("Game not found.");
    audit.Write("Information", "games", $"Terminated game {id}.", user.Id);
    return Results.NoContent();
});
app.MapHub<GameHub>("/hubs/game");
app.MapHub<StateMachineHub>("/hubs/state-machine");
await app.StartAsync();
LaunchClientIfConfigured();
await app.WaitForShutdownAsync();

static void Seed(AppState state, AssetService assets)
{
    state.MatchmakingQueues.TryAdd("ranked", new MatchmakingQueueConfiguration("ranked", "Ranked Duel", "ranked", 2, 2, 100, 10, 90, true, true, DateTimeOffset.UtcNow));
    state.MatchmakingQueues.TryAdd("casual", new MatchmakingQueueConfiguration("casual", "Casual Play", "casual", 2, 2, 300, 25, 30, true, true, DateTimeOffset.UtcNow));
    state.RulePacks.TryAdd("mtg-like", new RulePack("mtg-like", "TcgULtimateServer", "1.0.0", "A small MTG-inspired starter ruleset.", [
        new("spark-mage", "Spark Mage", "creature", 1, 2, 1, "When played, deal 1 damage to an opponent."),
        new("stone-golem", "Stone Golem", "creature", 3, 4, 5, "A sturdy defender."),
        new("bolt", "Lightning Bolt", "spell", 1, 3, 0, "Deal 3 damage to any target."),
        new("forest-warden", "Forest Warden", "creature", 2, 3, 3, "Your creatures get +1 power."),
        new("mystic-draw", "Mystic Draw", "spell", 2, 0, 0, "Draw two cards.")], new() { ["startingLife"] = 20 }));
    // Enumerate a snapshot because each card is replaced in the source list below.
    foreach (var card in state.RulePacks["mtg-like"].Cards.ToList())
    {
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='600' height='840'><rect width='100%' height='100%' fill='#172218'/><rect x='24' y='24' width='552' height='792' rx='24' fill='#263b25' stroke='#b8ee62' stroke-width='4'/><text x='48' y='100' fill='#eff4e9' font-family='sans-serif' font-size='34'>{card.Name}</text><text x='48' y='155' fill='#b8ee62' font-family='monospace' font-size='20'>{card.Type.ToUpperInvariant()} · COST {card.Cost}</text><text x='48' y='720' fill='#eff4e9' font-family='sans-serif' font-size='20'>{card.Text}</text></svg>";
        var added = assets.Add($"card-{card.Id}", $"{card.Name} card art", "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(svg), "");
        var updated = card with { AssetId = added.Id }; var cards = state.RulePacks["mtg-like"].Cards; cards[cards.IndexOf(card)] = updated;
    }
}

static void LaunchClientIfConfigured()
{
    if (!string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("TCG_CLIENT_MANAGED_SERVER"), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("TCG_LAUNCH_CLIENT"), "false", StringComparison.OrdinalIgnoreCase)) return;

    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null)
    {
        var clientProject = Path.Combine(directory.FullName, "src", "TcgULtimateServer.App", "TcgULtimateServer.App.csproj");
        if (File.Exists(clientProject))
        {
            var clientExecutable = Environment.GetEnvironmentVariable("TCG_CLIENT_EXE");
            if (!string.IsNullOrWhiteSpace(clientExecutable) && File.Exists(clientExecutable))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = clientExecutable,
                    WorkingDirectory = directory.FullName,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                });
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = directory.FullName,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["TCG_CLIENT_MANAGED_SERVER"] = "true";
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(clientProject);
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add("Debug");
            startInfo.ArgumentList.Add("--no-launch-profile");
            startInfo.ArgumentList.Add("-p:Platform=x64");
            Process.Start(startInfo);
            return;
        }
        directory = directory.Parent;
    }
}

static bool TryGetAdmin(HttpRequest request, AuthService auth, AdminService admin, out User user)
{
    var token = request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    var candidate = auth.FromToken(token);
    if (candidate is not null && admin.IsAdmin(candidate))
    {
        user = candidate;
        return true;
    }
    user = null!;
    return false;
}

static ClientGameView BuildClientGameView(GameState game, RulesEngine engine)
{
    var pack = engine.Get(game.RulePackId);
    var phase = pack.Phases.FirstOrDefault(candidate => candidate.Id.Equals(game.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
    var allowed = (phase?.AllowedActions ?? [])
        .Select(actionId => pack.Actions.FirstOrDefault(action => action.Id.Equals(actionId, StringComparison.OrdinalIgnoreCase)))
        .Where(action => action is not null)
        .Select(action => new ClientGameAction(action!.Id, action.Name, action.SourceZone, action.DestinationZone, action.RequiresActivePlayer,
            action.Effects?.Select(effect => effect.Target).FirstOrDefault(target => !string.IsNullOrWhiteSpace(target))))
        .ToList();
    var zones = game.Zones.SelectMany(owner => owner.Value.Select(zone => new ClientGameZone(owner.Key, zone.Key, zone.Value))).ToList();
    var resources = game.Resources.SelectMany(owner => owner.Value.Select(resource => new ClientGameResource(owner.Key, resource.Key, resource.Value))).ToList();
    return new ClientGameView(game.Id, game.RulePackId, game.Players, game.ActivePlayerId, game.CurrentPhaseId, game.Turn, game.Finished,
        game.WinnerPlayerId, game.VictoryRuleId, zones, resources, allowed, game.Events.TakeLast(40).ToList());
}

static bool AcceptsEncoding(string? header, string encoding) => header?.Split(',').Any(value =>
{
    var parts = value.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return parts.Length > 0 && parts[0].Equals(encoding, StringComparison.OrdinalIgnoreCase) &&
        !parts.Skip(1).Any(part => part.StartsWith("q=0", StringComparison.OrdinalIgnoreCase));
}) == true;

static byte[] Compress(byte[] content, bool useBrotli)
{
    using var output = new MemoryStream();
    using (Stream compressor = useBrotli
        ? new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true)
        : new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
    {
        compressor.Write(content);
    }
    return output.ToArray();
}
